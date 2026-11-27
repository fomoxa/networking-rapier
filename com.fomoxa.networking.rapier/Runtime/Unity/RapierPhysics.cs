using System;
using System.Collections.Generic;
using Fomoxa.Networking;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Rapier;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Rapier
{
    [DisallowMultipleComponent]
    public sealed class RapierPhysics : NetworkPhysics, IPhysicsMotion
    {
        [SerializeField] private Vector3 gravity = new Vector3(0f, -9.81f, 0f);

        private readonly Dictionary<NetworkObject, Bound> bound = new Dictionary<NetworkObject, Bound>();
        private readonly Dictionary<IPhysicsSimulation, SteppedWorld> stepped = new Dictionary<IPhysicsSimulation, SteppedWorld>();
        private readonly Dictionary<Scene, Placement> placed = new Dictionary<Scene, Placement>();
        private readonly HashSet<Scene> refusedScenes = new HashSet<Scene>();
        private readonly HashSet<Scene> presentScenes = new HashSet<Scene>();
        private readonly List<Scene> leavingScenes = new List<Scene>();
        private readonly HashSet<NetworkScene> clientLoading = new HashSet<NetworkScene>();
        private readonly Dictionary<Scene, Additions> additions = new Dictionary<Scene, Additions>();
        private readonly List<NetworkObject> leaving = new List<NetworkObject>();
        private readonly HashSet<NetworkObject> spawned = new HashSet<NetworkObject>();
        private readonly List<NetworkObject> binding = new List<NetworkObject>();
        private readonly List<IPhysicsSimulation> inner = new List<IPhysicsSimulation>();
        private readonly List<Rigidbody> treeBodies = new List<Rigidbody>();
        private readonly List<Rigidbody2D> treeBodies2D = new List<Rigidbody2D>();
        private readonly List<Joint> partJoints = new List<Joint>();
        private readonly List<Joint2D> partJoints2D = new List<Joint2D>();
        private readonly List<string> ignored = new List<string>();
        private readonly List<(Effector2D Effector, List<int> Colliders)> partEffectors = new List<(Effector2D Effector, List<int> Colliders)>();
        private NetworkManager manager;
        private RapierScenes scenes;
        private RapierContacts contacts;

        public override PhysicsBackend Backend => PhysicsBackend.Rapier;

        public RapierScenes Scenes => scenes;

        internal Func<Scene, uint> SceneIdOf { get; set; }

        internal Func<uint, byte[]> SceneFileOf { get; set; }

        public override void Begin()
        {
            if (scenes != null)
            {
                throw new InvalidOperationException("this RapierPhysics is already simulating for another NetworkManager");
            }

            manager = GetComponent<NetworkManager>();
            if (manager == null)
            {
                throw new InvalidOperationException("RapierPhysics belongs on the GameObject of the NetworkManager that uses it");
            }

            scenes = new RapierScenes(gravity.ToNumerics());
            contacts = new RapierContacts(scenes);
            ClientScenes clientScenes = manager.ClientManager.Scenes;
            clientScenes.OnLoadStarted += RememberClientLoad;
            clientScenes.OnLoaded += ForgetClientLoad;
            clientScenes.OnUnloaded += ForgetClientLoad;
        }

        public override void Release()
        {
            if (scenes == null)
            {
                return;
            }

            foreach (KeyValuePair<NetworkObject, Bound> entry in bound)
            {
                entry.Value.Restore(entry.Key);
            }

            ClientScenes clientScenes = manager.ClientManager?.Scenes;
            if (clientScenes != null)
            {
                clientScenes.OnLoadStarted -= RememberClientLoad;
                clientScenes.OnLoaded -= ForgetClientLoad;
                clientScenes.OnUnloaded -= ForgetClientLoad;
            }

            contacts.Clear();
            contacts = null;
            bound.Clear();
            stepped.Clear();
            placed.Clear();
            refusedScenes.Clear();
            clientLoading.Clear();
            additions.Clear();
            scenes.Dispose();
            scenes = null;
            manager = null;
        }

        public override void WorldsOf(Scene scene, List<IPhysicsSimulation> worlds)
        {
            if (scenes == null || !TryPlace(scene, out NetworkScene instance))
            {
                return;
            }

            if (scenes.Has(instance))
            {
                worlds.Add(Wrap(scenes.WorldOf(instance)));
            }

            if (scenes.Has2D(instance))
            {
                worlds.Add(Wrap(scenes.WorldOf2D(instance)));
            }
        }

        public override PhysicsBody BodyOf(NetworkObject networkObject)
        {
            Bound found = Bind(networkObject);
            return found?.Body ?? default;
        }

        public override PhysicsBody2D Body2DOf(NetworkObject networkObject)
        {
            Bound found = Bind(networkObject);
            return found?.Body2D ?? default;
        }

        public override IReadOnlyList<PhysicsBody> BodiesOf(NetworkObject networkObject) => Bind(networkObject)?.Bodies ?? (IReadOnlyList<PhysicsBody>)Array.Empty<PhysicsBody>();

        public override IReadOnlyList<PhysicsBody2D> Bodies2DOf(NetworkObject networkObject) => Bind(networkObject)?.Bodies2D ?? (IReadOnlyList<PhysicsBody2D>)Array.Empty<PhysicsBody2D>();

        public override PhysicsBody BodyOf(Transform part) => PartOf(part)?.Body ?? default;

        public override PhysicsBody2D Body2DOf(Transform part) => PartOf(part)?.Body2D ?? default;

        public override PhysicsHistory HistoryOf(IPhysicsSimulation world, int capacity) => scenes.HistoryOf(world, capacity);

        public bool ReportsMotion(INetworkEntity entity) => scenes != null && scenes.ReportsMotion(entity);

        public void CollectMoved(List<INetworkEntity> into)
        {
            scenes?.CollectMoved(into);
            foreach (KeyValuePair<NetworkObject, Bound> entry in bound)
            {
                if (entry.Key == null)
                {
                    continue;
                }

                bool shifted = false;
                foreach (BoundPart part in entry.Value.Parts)
                {
                    if (!part.IsProxy && part.Transform != null)
                    {
                        shifted |= part.Shifted();
                    }
                }

                if (shifted)
                {
                    into.Add(entry.Key);
                }
            }
        }

        public override void PlaceProxy(NetworkObject networkObject)
        {
            Bound found = Bind(networkObject);
            if (found == null || !found.HasBody)
            {
                return;
            }

            foreach (BoundPart part in found.Parts)
            {
                if (part.Transform == null || (!part.IsRoot && !part.Transform.TryGetComponent(out NetworkTransform _)))
                {
                    continue;
                }

                Transform placed = part.Transform;
                if (part.Body.IsValid)
                {
                    scenes.PlaceProxy(networkObject, part.Body);
                    PhysicsBody body = part.Body;
                    body.State = new BodyState { Position = placed.position.ToNumerics(), Rotation = placed.rotation.ToNumerics() };
                }
                else if (part.Body2D.IsValid)
                {
                    scenes.PlaceProxy(networkObject, part.Body2D);
                    PhysicsBody2D body = part.Body2D;
                    Vector3 position = placed.position;
                    body.State = new BodyState2D { Position = new System.Numerics.Vector2(position.x, position.y), Rotation = placed.eulerAngles.z * Mathf.Deg2Rad };
                }

                part.IsProxy = true;
            }
        }

        public override void EndProxy(NetworkObject networkObject)
        {
            if (networkObject == null || !bound.TryGetValue(networkObject, out Bound found))
            {
                return;
            }

            bool proxied = false;
            foreach (BoundPart part in found.Parts)
            {
                proxied |= part.IsProxy;
                part.IsProxy = false;
            }

            if (proxied)
            {
                scenes.EndProxy(networkObject);
            }
        }

        public override void ForgetDestroyedProxies()
        {
            leaving.Clear();
            foreach (NetworkObject networkObject in bound.Keys)
            {
                if (networkObject == null)
                {
                    leaving.Add(networkObject);
                }
            }

            foreach (NetworkObject networkObject in leaving)
            {
                Unbind(networkObject);
            }

            leaving.Clear();
        }

        public override void WorldsToStep(List<IPhysicsSimulation> worlds)
        {
            if (scenes == null)
            {
                return;
            }

            SyncScenes();
            SyncBodies();
            foreach (KeyValuePair<NetworkObject, Bound> entry in bound)
            {
                if (entry.Value.HasBody)
                {
                    contacts.Claim(entry.Key);
                }
            }

            inner.Clear();
            scenes.WorldsToStep(inner);
            foreach (IPhysicsSimulation world in inner)
            {
                worlds.Add(Wrap(world));
            }

            inner.Clear();
        }

        public override IContactTracker TrackerOf(IPhysicsSimulation world) => contacts?.TrackerOf(world is SteppedWorld wrapper ? wrapper.Inner : world);

        public override StaticGroup AddStatic(Scene scene, IReadOnlyList<ColliderDesc> colliders)
        {
            NetworkScene target = Target(scene);
            return Remember(scene, target, scenes.AddStatic(target, colliders));
        }

        public override StaticGroup AddStatic2D(Scene scene, IReadOnlyList<ColliderDesc2D> colliders)
        {
            NetworkScene target = Target(scene);
            return Remember(scene, target, scenes.AddStatic2D(target, colliders));
        }

        public override StaticGroup AddStatic(GameObject root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            NetworkScene target = Target(root.scene);
            var sources = new List<Collider>();
            var colliders = new List<ColliderDesc>();
            BodyDescriptions.StaticColliders(root, sources);
            for (int index = 0; index < sources.Count; index++)
            {
                if (index == 0 || sources[index] != sources[index - 1])
                {
                    BodyDescriptions.DescribeStatic(sources[index], null, colliders);
                }
            }

            StaticGroup group = scenes.AddStatic(target, colliders);
            contacts.AddGroup(group, scenes.WorldOf(target), sources, root);
            return Remember(root.scene, target, group);
        }

        public override StaticGroup AddStatic2D(GameObject root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            NetworkScene target = Target(root.scene);
            var sources = new List<Collider2D>();
            var colliders = new List<ColliderDesc2D>();
            BodyDescriptions.StaticColliders2D(root, sources);
            for (int index = 0; index < sources.Count; index++)
            {
                if (index == 0 || sources[index] != sources[index - 1])
                {
                    BodyDescriptions.DescribeStatic2D(sources[index], null, colliders);
                }
            }

            StaticGroup group = scenes.AddStatic2D(target, colliders);
            contacts.AddGroup2D(group, scenes.WorldOf2D(target), sources, root);
            AddEffectors(sources, null, (indices, desc) => scenes.AddEffector(group, indices, desc));
            return Remember(root.scene, target, group);
        }

        public override void RemoveStatic(StaticGroup group)
        {
            if (scenes == null)
            {
                return;
            }

            contacts.RemoveGroup(group.Id);
            scenes.RemoveStatic(group);
            foreach (Additions added in additions.Values)
            {
                added.Groups.Remove(group);
            }
        }

        public override PhysicsBody AddBody(Scene scene, in BodyDesc body)
        {
            NetworkScene target = Target(scene);
            PhysicsBody created = scenes.AddBody(target, body);
            if (target == default)
            {
                AdditionsOf(scene).Bodies.Add(created);
            }

            return created;
        }

        public override PhysicsBody2D AddBody2D(Scene scene, in BodyDesc2D body)
        {
            NetworkScene target = Target(scene);
            PhysicsBody2D created = scenes.AddBody2D(target, body);
            if (target == default)
            {
                AdditionsOf(scene).Bodies2D.Add(created);
            }

            return created;
        }

        public override void RemoveBody(PhysicsBody body)
        {
            if (scenes == null)
            {
                return;
            }

            scenes.RemoveBody(body);
            foreach (Additions added in additions.Values)
            {
                added.Bodies.Remove(body);
            }
        }

        public override void RemoveBody2D(PhysicsBody2D body)
        {
            if (scenes == null)
            {
                return;
            }

            scenes.RemoveBody2D(body);
            foreach (Additions added in additions.Values)
            {
                added.Bodies2D.Remove(body);
            }
        }

        internal void WriteBack(IPhysicsSimulation world)
        {
            foreach (KeyValuePair<NetworkObject, Bound> entry in bound)
            {
                if (entry.Key == null)
                {
                    continue;
                }

                foreach (BoundPart part in entry.Value.Parts)
                {
                    Transform target = part.Transform;
                    if (part.IsProxy || target == null)
                    {
                        continue;
                    }

                    if (part.Body.IsValid && ReferenceEquals(entry.Value.World, world))
                    {
                        target.SetPositionAndRotation(part.Body.Position.ToUnity(), part.Body.Rotation.ToUnity());
                    }
                    else if (part.Body2D.IsValid && ReferenceEquals(entry.Value.World2D, world))
                    {
                        System.Numerics.Vector2 position = part.Body2D.Position;
                        target.SetPositionAndRotation(new Vector3(position.X, position.Y, target.position.z), Quaternion.AngleAxis(part.Body2D.Rotation * Mathf.Rad2Deg, Vector3.forward));
                    }
                }
            }
        }

        private SteppedWorld Wrap(IPhysicsSimulation world)
        {
            if (!stepped.TryGetValue(world, out SteppedWorld wrapper))
            {
                wrapper = new SteppedWorld(world, this);
                stepped.Add(world, wrapper);
            }

            return wrapper;
        }

        private void SyncScenes()
        {
            presentScenes.Clear();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                uint sceneId = scene.isLoaded ? IdOf(scene) : 0;
                if (sceneId == 0 || !TryPlace(scene, sceneId, out NetworkScene instance))
                {
                    continue;
                }

                presentScenes.Add(scene);
                if (placed.TryGetValue(scene, out Placement current))
                {
                    if (current.Instance == instance)
                    {
                        continue;
                    }

                    Leave(scene, current);
                }

                if (refusedScenes.Contains(scene))
                {
                    continue;
                }

                byte[] bytes = FileOf(sceneId);
                if (bytes == null)
                {
                    refusedScenes.Add(scene);
                    Debug.LogWarning($"[Fomoxa] scene 0x{sceneId:X8} has no scene file; Rapier simulates it without static geometry. Export the network scene files");
                    continue;
                }

                try
                {
                    SceneFile file = SceneFileFormat.Read(manager.Registry, bytes);
                    scenes.LoadScene(instance, new SceneFile
                    {
                        SceneId = file.SceneId,
                        Colliders = file.Colliders,
                        Colliders2D = file.Colliders2D,
                        LayerCollisions = file.LayerCollisions,
                        LayerCollisions2D = file.LayerCollisions2D,
                        Effectors2D = file.Effectors2D,
                    });
                    placed.Add(scene, new Placement(instance, file.SceneId));
                    scenes.TryGetFileStatics(instance, file.SceneId, out StaticGroup statics, out StaticGroup statics2D);
                    contacts.LoadScene(scene, statics.IsValid ? scenes.WorldOf(instance) : null, statics, statics2D.IsValid ? scenes.WorldOf2D(instance) : null, statics2D);
                }
                catch (Exception exception) when (exception is System.IO.InvalidDataException || exception is InvalidOperationException || exception is ArgumentException)
                {
                    refusedScenes.Add(scene);
                    Debug.LogException(exception);
                }
            }

            leavingScenes.Clear();
            foreach (KeyValuePair<Scene, Placement> entry in placed)
            {
                if (!presentScenes.Contains(entry.Key))
                {
                    leavingScenes.Add(entry.Key);
                }
            }

            foreach (Scene scene in leavingScenes)
            {
                Leave(scene, placed[scene]);
            }

            leavingScenes.Clear();
            foreach (Scene scene in additions.Keys)
            {
                if (!scene.isLoaded)
                {
                    leavingScenes.Add(scene);
                }
            }

            foreach (Scene scene in leavingScenes)
            {
                DropAdditions(scene);
            }

            refusedScenes.RemoveWhere(scene => !scene.isLoaded);
            leavingScenes.Clear();
        }

        private void Leave(Scene scene, Placement placement)
        {
            placed.Remove(scene);
            contacts.UnloadScene(scene, placement.Instance);
            if (placement.Instance == default)
            {
                scenes.UnloadSceneFile(placement.SceneId);
                return;
            }

            ForgetWorlds(placement.Instance);
            scenes.UnloadScene(placement.Instance);
        }

        private void DropAdditions(Scene scene)
        {
            if (!additions.Remove(scene, out Additions added))
            {
                return;
            }

            foreach (StaticGroup group in added.Groups)
            {
                contacts.RemoveGroup(group.Id);
                scenes.RemoveStatic(group);
            }

            foreach (PhysicsBody body in added.Bodies)
            {
                scenes.RemoveBody(body);
            }

            foreach (PhysicsBody2D body in added.Bodies2D)
            {
                scenes.RemoveBody2D(body);
            }
        }

        private void ForgetWorlds(NetworkScene instance)
        {
            if (scenes.Has(instance))
            {
                stepped.Remove(scenes.WorldOf(instance));
            }

            if (scenes.Has2D(instance))
            {
                stepped.Remove(scenes.WorldOf2D(instance));
            }
        }

        private bool TryPlace(Scene scene, out NetworkScene instance) => TryPlace(scene, IdOf(scene), out instance);

        private bool TryPlace(Scene scene, uint sceneId, out NetworkScene instance)
        {
            ServerManager server = manager.ServerManager;
            if (server.State == ServerState.Started)
            {
                if (server.Scenes.TryGetNetworkScene(scene, out instance))
                {
                    return true;
                }

                instance = default;
                return InDefaultWorld(scene) || !ServerLoads(server.Scenes, sceneId);
            }

            if (manager.ClientManager.Scenes.TryGetNetworkScene(scene, out instance))
            {
                return true;
            }

            instance = default;
            return InDefaultWorld(scene) || !ClientLoads(sceneId);
        }

        private bool InDefaultWorld(Scene scene) =>
            (placed.TryGetValue(scene, out Placement placement) && placement.Instance == default) || additions.ContainsKey(scene);

        private bool ServerLoads(NetworkScenes serverScenes, uint sceneId)
        {
            if (sceneId == 0 || !manager.Scenes.Contains(sceneId))
            {
                return false;
            }

            foreach (NetworkScene instance in serverScenes.InstancesOf(sceneId))
            {
                if (!serverScenes.TryGetScene(instance, out Scene _))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ClientLoads(uint sceneId)
        {
            if (manager.ClientManager.State != ConnectionState.Started)
            {
                clientLoading.Clear();
                return false;
            }

            foreach (NetworkScene instance in clientLoading)
            {
                if (manager.ClientManager.Scenes.SceneIdOf(instance) == sceneId)
                {
                    return true;
                }
            }

            return false;
        }

        private void RememberClientLoad(NetworkScene instance, uint sceneId) => clientLoading.Add(instance);

        private void ForgetClientLoad(NetworkScene instance) => clientLoading.Remove(instance);

        private NetworkScene Target(Scene scene)
        {
            Begun();
            if (!TryPlace(scene, out NetworkScene instance))
            {
                throw new InvalidOperationException($"scene {scene.name} is still loading as a network scene; add geometry and bodies to it once it has loaded");
            }

            return instance;
        }

        private StaticGroup Remember(Scene scene, NetworkScene target, StaticGroup group)
        {
            if (target == default && group.IsValid)
            {
                AdditionsOf(scene).Groups.Add(group);
            }

            return group;
        }

        private Additions AdditionsOf(Scene scene)
        {
            if (!additions.TryGetValue(scene, out Additions added))
            {
                added = new Additions();
                additions.Add(scene, added);
            }

            return added;
        }

        private void SyncBodies()
        {
            spawned.Clear();
            Collect(manager.ServerManager.Spawned);
            Collect(manager.ClientManager.Spawned);
            binding.Clear();
            binding.AddRange(spawned);
            binding.Sort((left, right) => left.ObjectId.CompareTo(right.ObjectId));
            foreach (NetworkObject networkObject in binding)
            {
                Bind(networkObject);
            }

            binding.Clear();

            leaving.Clear();
            foreach (NetworkObject networkObject in bound.Keys)
            {
                if (networkObject == null || !spawned.Contains(networkObject))
                {
                    leaving.Add(networkObject);
                }
            }

            foreach (NetworkObject networkObject in leaving)
            {
                Unbind(networkObject);
            }

            leaving.Clear();
            spawned.Clear();
        }

        private void Collect(IReadOnlyDictionary<uint, INetworkEntity> entities)
        {
            foreach (INetworkEntity entity in entities.Values)
            {
                if (entity is NetworkObject networkObject && networkObject != null)
                {
                    spawned.Add(networkObject);
                }
            }
        }

        private BoundPart PartOf(Transform part)
        {
            if (part == null)
            {
                return null;
            }

            Bound found = Bind(part.GetComponentInParent<NetworkObject>(true));
            if (found == null)
            {
                return null;
            }

            foreach (BoundPart candidate in found.Parts)
            {
                if (candidate.Transform == part)
                {
                    return candidate;
                }
            }

            return null;
        }

        private Bound Bind(NetworkObject networkObject)
        {
            if (scenes == null || networkObject == null)
            {
                return null;
            }

            if (bound.TryGetValue(networkObject, out Bound found))
            {
                return found;
            }

            if (!TryPlace(networkObject.gameObject.scene, out NetworkScene instance))
            {
                return null;
            }

            found = new Bound();
            bound.Add(networkObject, found);
            GameObject root = networkObject.gameObject;
            try
            {
                AddPart(networkObject, found, instance, root);
                networkObject.GetComponentsInChildren(true, treeBodies);
                foreach (Rigidbody rigidbody in treeBodies)
                {
                    if (rigidbody.gameObject != root && ActiveInTree(rigidbody.transform, root.transform))
                    {
                        AddPart(networkObject, found, instance, rigidbody.gameObject);
                    }
                }

                if (!root.TryGetComponent(out Rigidbody2D root2D) || root2D.simulated)
                {
                    AddPart2D(networkObject, found, instance, root);
                }

                networkObject.GetComponentsInChildren(true, treeBodies2D);
                foreach (Rigidbody2D rigidbody in treeBodies2D)
                {
                    if (rigidbody.gameObject != root && rigidbody.simulated && ActiveInTree(rigidbody.transform, root.transform))
                    {
                        AddPart2D(networkObject, found, instance, rigidbody.gameObject);
                    }
                }

                AddJoints(networkObject, found);
            }
            catch (Exception exception) when (exception is NotSupportedException || exception is ArgumentException)
            {
                Debug.LogException(exception, networkObject);
            }

            treeBodies.Clear();
            treeBodies2D.Clear();
            return found;
        }

        private void AddPart(NetworkObject networkObject, Bound found, NetworkScene instance, GameObject source)
        {
            var sources = new List<Collider>();
            if (!BodyDescriptions.TryDescribe(source, out BodyDesc body, sources))
            {
                return;
            }

            var part = new BoundPart { Transform = source.transform, IsRoot = source == networkObject.gameObject, Body = scenes.AddBody(networkObject, instance, body) };
            found.World = scenes.WorldOf(instance);
            if (source.TryGetComponent(out Rigidbody rigidbody))
            {
                part.Rigidbody = rigidbody;
                part.WasKinematic = rigidbody.isKinematic;
                rigidbody.isKinematic = true;
            }

            if (part.IsRoot)
            {
                found.Body = part.Body;
            }

            found.Parts.Add(part);
            found.Bodies.Add(part.Body);
            contacts.AddBody(networkObject, found.World, part.Body.Handle, sources);
        }

        private void AddPart2D(NetworkObject networkObject, Bound found, NetworkScene instance, GameObject source)
        {
            var sources = new List<Collider2D>();
            if (!BodyDescriptions.TryDescribe2D(source, out BodyDesc2D body, sources))
            {
                return;
            }

            var part = new BoundPart { Transform = source.transform, IsRoot = source == networkObject.gameObject, Body2D = scenes.AddBody2D(networkObject, instance, body) };
            found.World2D = scenes.WorldOf2D(instance);
            if (source.TryGetComponent(out Rigidbody2D rigidbody))
            {
                part.Rigidbody2D = rigidbody;
                part.BodyType2D = rigidbody.bodyType;
                rigidbody.bodyType = RigidbodyType2D.Kinematic;
            }

            if (part.IsRoot)
            {
                found.Body2D = part.Body2D;
            }

            found.Parts.Add(part);
            found.Bodies2D.Add(part.Body2D);
            contacts.AddBody2D(networkObject, found.World2D, part.Body2D.Handle, sources);
            AddEffectors(sources, part.Transform, (indices, desc) => scenes.AddEffector(networkObject, part.Body2D, indices, desc));
        }

        private void AddEffectors(List<Collider2D> sources, Transform owner, Func<List<int>, EffectorDesc2D, EffectorHandle> add)
        {
            partEffectors.Clear();
            for (int index = 0; index < sources.Count; index++)
            {
                if (!EffectorDescriptions.TryFind(sources[index], out Effector2D effector))
                {
                    continue;
                }

                int entry = partEffectors.FindIndex(candidate => candidate.Effector == effector);
                if (entry < 0)
                {
                    partEffectors.Add((effector, new List<int>()));
                    entry = partEffectors.Count - 1;
                }

                partEffectors[entry].Colliders.Add(index);
            }

            foreach ((Effector2D effector, List<int> colliders) in partEffectors)
            {
                ignored.Clear();
                try
                {
                    add(colliders, RapierEffectors.FromFile(EffectorDescriptions.Describe(effector, owner, ignored)));
                }
                catch (Exception exception) when (exception is NotSupportedException || exception is ArgumentException)
                {
                    Debug.LogException(exception, effector);
                    continue;
                }

                if (ignored.Count > 0)
                {
                    Debug.LogWarning($"{effector.GetType().Name} on {effector.name}: Rapier does not simulate {string.Join(", ", ignored)}, so they count as 0", effector);
                }
            }

            partEffectors.Clear();
        }

        private void AddJoints(NetworkObject networkObject, Bound found)
        {
            foreach (BoundPart part in found.Parts)
            {
                if (part.Body.IsValid)
                {
                    part.Transform.GetComponents(partJoints);
                    foreach (Joint joint in partJoints)
                    {
                        AddJoint(networkObject, found, part, joint);
                    }
                }
                else if (part.Body2D.IsValid)
                {
                    part.Transform.GetComponents(partJoints2D);
                    foreach (Joint2D joint in partJoints2D)
                    {
                        AddJoint2D(networkObject, found, part, joint);
                    }
                }
            }

            partJoints.Clear();
            partJoints2D.Clear();
        }

        private void AddJoint(NetworkObject networkObject, Bound found, BoundPart part, Joint joint)
        {
            BoundPart connected = null;
            if (joint.connectedBody != null || joint.connectedArticulationBody != null)
            {
                connected = found.Parts.Find(other => other.Body.IsValid && other.Rigidbody != null && other.Rigidbody == joint.connectedBody);
                if (connected == null)
                {
                    Debug.LogError($"{joint.GetType().Name} on {joint.name}: the connected body is not a body of the same NetworkObject, so Rapier does not build this joint", joint);
                    return;
                }
            }

            ignored.Clear();
            if (!RapierJoints.TryDescribe(joint, connected?.Transform, ignored, out JointDesc desc))
            {
                Debug.LogError($"{joint.GetType().Name} on {joint.name} is not supported under Rapier, so the joint is not built", joint);
                return;
            }

            Report(joint, RapierJoints.Breaks(joint));
            Create(joint, () => scenes.CreateJoint(networkObject, part.Body, connected?.Body ?? default, desc));
        }

        private void AddJoint2D(NetworkObject networkObject, Bound found, BoundPart part, Joint2D joint)
        {
            BoundPart connected = null;
            if (joint.connectedBody != null)
            {
                connected = found.Parts.Find(other => other.Body2D.IsValid && other.Rigidbody2D != null && other.Rigidbody2D == joint.connectedBody);
                if (connected == null)
                {
                    Debug.LogError($"{joint.GetType().Name} on {joint.name}: the connected body is not a body of the same NetworkObject, so Rapier does not build this joint", joint);
                    return;
                }
            }

            ignored.Clear();
            if (!RapierJoints.TryDescribe2D(joint, connected?.Transform, ignored, out JointDesc2D desc))
            {
                Debug.LogError($"{joint.GetType().Name} on {joint.name} is not supported under Rapier, so the joint is not built", joint);
                return;
            }

            Report(joint, RapierJoints.Breaks(joint));
            Create(joint, () => scenes.CreateJoint(networkObject, part.Body2D, connected?.Body2D ?? default, desc));
        }

        private static void Create(Component joint, Func<JointHandle> create)
        {
            try
            {
                create();
            }
            catch (ArgumentException exception)
            {
                Debug.LogException(exception, joint);
            }
        }

        private void Report(Component joint, bool breaks)
        {
            if (breaks)
            {
                Debug.LogError($"{joint.GetType().Name} on {joint.name}: Rapier joints do not break, so breakForce and breakTorque are ignored", joint);
            }

            if (ignored.Count > 0)
            {
                Debug.LogWarning($"{joint.GetType().Name} on {joint.name}: Rapier ignores {string.Join(", ", ignored)}", joint);
            }
        }

        private static bool ActiveInTree(Transform node, Transform root)
        {
            for (Transform current = node; current != root; current = current.parent)
            {
                if (!current.gameObject.activeSelf)
                {
                    return false;
                }
            }

            return true;
        }

        private void Unbind(NetworkObject networkObject)
        {
            if (!bound.Remove(networkObject, out Bound found))
            {
                return;
            }

            contacts.RemoveBody(networkObject);
            scenes.RemoveBodies(networkObject);
            found.Restore(networkObject);
        }

        private RapierScenes Begun()
        {
            if (scenes == null)
            {
                throw new InvalidOperationException("this RapierPhysics has not begun; start its NetworkManager first");
            }

            SyncScenes();
            return scenes;
        }

        private uint IdOf(Scene scene)
        {
            if (SceneIdOf != null)
            {
                return SceneIdOf(scene);
            }

            return scene.IsValid() && !string.IsNullOrEmpty(scene.path) ? manager.Scenes.IdOf(scene.path) : 0;
        }

        private byte[] FileOf(uint sceneId)
        {
            if (SceneFileOf != null)
            {
                return SceneFileOf(sceneId);
            }

            return manager.Scenes.TryGetSceneFile(sceneId, out byte[] bytes) ? bytes : null;
        }

        private readonly struct Placement
        {
            public Placement(NetworkScene instance, uint sceneId)
            {
                Instance = instance;
                SceneId = sceneId;
            }

            public NetworkScene Instance { get; }

            public uint SceneId { get; }
        }

        private sealed class Additions
        {
            public readonly List<StaticGroup> Groups = new List<StaticGroup>();
            public readonly List<PhysicsBody> Bodies = new List<PhysicsBody>();
            public readonly List<PhysicsBody2D> Bodies2D = new List<PhysicsBody2D>();
        }

        private sealed class Bound
        {
            public readonly List<BoundPart> Parts = new List<BoundPart>();
            public readonly List<PhysicsBody> Bodies = new List<PhysicsBody>();
            public readonly List<PhysicsBody2D> Bodies2D = new List<PhysicsBody2D>();
            public PhysicsBody Body;
            public PhysicsBody2D Body2D;
            public RapierWorld World;
            public RapierWorld2D World2D;

            public bool HasBody => Parts.Count > 0;

            public void Restore(NetworkObject networkObject)
            {
                foreach (BoundPart part in Parts)
                {
                    part.Restore();
                }
            }
        }

        private sealed class BoundPart
        {
            public Transform Transform;
            public bool IsRoot;
            public PhysicsBody Body;
            public PhysicsBody2D Body2D;
            public Rigidbody Rigidbody;
            public bool WasKinematic;
            public Rigidbody2D Rigidbody2D;
            public RigidbodyType2D BodyType2D;
            public bool IsProxy;
            public bool Seen;
            public float SeenZ;
            public Transform SeenParent;
            public Vector3 SeenLocalPosition;
            public Quaternion SeenLocalRotation;

            public bool Shifted()
            {
                Transform parent = Transform.parent;
                bool shifted = Seen && !ReferenceEquals(parent, SeenParent);
                if (Body2D.IsValid)
                {
                    float z = Transform.position.z;
                    shifted |= Seen && !z.Equals(SeenZ);
                    SeenZ = z;
                }

                if (parent != null)
                {
                    Transform.GetLocalPositionAndRotation(out Vector3 localPosition, out Quaternion localRotation);
                    shifted |= Seen && (!localPosition.Equals(SeenLocalPosition) || !localRotation.Equals(SeenLocalRotation));
                    SeenLocalPosition = localPosition;
                    SeenLocalRotation = localRotation;
                }

                SeenParent = parent;
                Seen = true;
                return shifted;
            }

            public void Restore()
            {
                if (Rigidbody != null)
                {
                    Rigidbody.isKinematic = WasKinematic;
                }

                if (Rigidbody2D != null)
                {
                    Rigidbody2D.bodyType = BodyType2D;
                }
            }
        }

        private sealed class SteppedWorld : IPhysicsSimulation
        {
            private readonly IPhysicsSimulation world;
            private readonly RapierPhysics owner;

            public SteppedWorld(IPhysicsSimulation world, RapierPhysics owner)
            {
                this.world = world;
                this.owner = owner;
            }

            public IPhysicsSimulation Inner => world;

            public PhysicsBackend Backend => world.Backend;

            public void Step(float seconds)
            {
                world.Step(seconds);
                owner.WriteBack(world);
            }

            public PhysicsSnapshot CreateSnapshot() => world.CreateSnapshot();

            public void Save(PhysicsSnapshot into) => world.Save(into);

            public void Load(PhysicsSnapshot from)
            {
                world.Load(from);
                owner.WriteBack(world);
            }
        }
    }
}
