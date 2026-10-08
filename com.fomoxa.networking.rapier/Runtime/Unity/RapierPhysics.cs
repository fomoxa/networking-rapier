using System;
using System.Collections.Generic;
using Fomoxa.Networking;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Rapier;
using Fomoxa.Networking.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Rapier
{
    [DisallowMultipleComponent]
    public sealed class RapierPhysics : NetworkPhysics
    {
        [SerializeField] private Vector3 gravity = new Vector3(0f, -9.81f, 0f);

        private readonly Dictionary<NetworkObject, Bound> bound = new Dictionary<NetworkObject, Bound>();
        private readonly Dictionary<IPhysicsSimulation, SteppedWorld> stepped = new Dictionary<IPhysicsSimulation, SteppedWorld>();
        private readonly HashSet<uint> loadedScenes = new HashSet<uint>();
        private readonly HashSet<uint> refusedScenes = new HashSet<uint>();
        private readonly HashSet<uint> presentScenes = new HashSet<uint>();
        private readonly List<uint> leavingScenes = new List<uint>();
        private readonly List<NetworkObject> leaving = new List<NetworkObject>();
        private readonly HashSet<NetworkObject> spawned = new HashSet<NetworkObject>();
        private readonly List<NetworkObject> binding = new List<NetworkObject>();
        private readonly List<IPhysicsSimulation> inner = new List<IPhysicsSimulation>();
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

            contacts.Clear();
            contacts = null;
            bound.Clear();
            stepped.Clear();
            loadedScenes.Clear();
            refusedScenes.Clear();
            scenes.Dispose();
            scenes = null;
            manager = null;
        }

        public override void WorldsOf(Scene scene, List<IPhysicsSimulation> worlds)
        {
            if (scenes == null)
            {
                return;
            }

            uint sceneId = IdOf(scene);
            inner.Clear();
            foreach (uint id in scenes.SceneIds)
            {
                if (id == sceneId)
                {
                    inner.Add(scenes.WorldOf(id));
                }
            }

            foreach (uint id in scenes.SceneIds2D)
            {
                if (id == sceneId)
                {
                    inner.Add(scenes.WorldOf2D(id));
                }
            }

            foreach (IPhysicsSimulation world in inner)
            {
                worlds.Add(Wrap(world));
            }

            inner.Clear();
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

        public override PhysicsHistory HistoryOf(IPhysicsSimulation world, int capacity) => scenes.HistoryOf(world, capacity);

        public override void PlaceProxy(NetworkObject networkObject)
        {
            Bound found = Bind(networkObject);
            if (found == null || !found.HasBody)
            {
                return;
            }

            scenes.PlaceProxy(networkObject);
            found.IsProxy = true;
            Transform placed = networkObject.transform;
            if (found.Body.IsValid)
            {
                PhysicsBody body = found.Body;
                body.State = new BodyState { Position = placed.position.ToNumerics(), Rotation = placed.rotation.ToNumerics() };
            }

            if (found.Body2D.IsValid)
            {
                PhysicsBody2D body = found.Body2D;
                Vector3 position = placed.position;
                body.State = new BodyState2D { Position = new System.Numerics.Vector2(position.x, position.y), Rotation = placed.eulerAngles.z * Mathf.Deg2Rad };
            }
        }

        public override void EndProxy(NetworkObject networkObject)
        {
            if (networkObject != null && bound.TryGetValue(networkObject, out Bound found) && found.IsProxy)
            {
                scenes.EndProxy(networkObject);
                found.IsProxy = false;
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

        public override StaticGroup AddStatic(Scene scene, IReadOnlyList<ColliderDesc> colliders) => Begun().AddStatic(IdOf(scene), colliders);

        public override StaticGroup AddStatic2D(Scene scene, IReadOnlyList<ColliderDesc2D> colliders) => Begun().AddStatic2D(IdOf(scene), colliders);

        public override StaticGroup AddStatic(GameObject root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            uint sceneId = IdOf(root.scene);
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

            StaticGroup group = Begun().AddStatic(sceneId, colliders);
            contacts.AddGroup(group, scenes.WorldOf(sceneId), sources, root);
            return group;
        }

        public override StaticGroup AddStatic2D(GameObject root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            uint sceneId = IdOf(root.scene);
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

            StaticGroup group = Begun().AddStatic2D(sceneId, colliders);
            contacts.AddGroup2D(group, scenes.WorldOf2D(sceneId), sources, root);
            return group;
        }

        public override void RemoveStatic(StaticGroup group)
        {
            if (scenes == null)
            {
                return;
            }

            contacts.RemoveGroup(group.Id);
            scenes.RemoveStatic(group);
        }

        public override PhysicsBody AddBody(Scene scene, in BodyDesc body) => Begun().AddBody(IdOf(scene), body);

        public override PhysicsBody2D AddBody2D(Scene scene, in BodyDesc2D body) => Begun().AddBody2D(IdOf(scene), body);

        public override void RemoveBody(PhysicsBody body) => scenes?.RemoveBody(body);

        public override void RemoveBody2D(PhysicsBody2D body) => scenes?.RemoveBody2D(body);

        internal void WriteBack(IPhysicsSimulation world)
        {
            foreach (KeyValuePair<NetworkObject, Bound> entry in bound)
            {
                Bound found = entry.Value;
                if (found.IsProxy || entry.Key == null)
                {
                    continue;
                }

                Transform target = entry.Key.transform;
                if (found.Body.IsValid && ReferenceEquals(found.World, world))
                {
                    target.SetPositionAndRotation(found.Body.Position.ToUnity(), found.Body.Rotation.ToUnity());
                }
                else if (found.Body2D.IsValid && ReferenceEquals(found.World2D, world))
                {
                    System.Numerics.Vector2 position = found.Body2D.Position;
                    target.SetPositionAndRotation(new Vector3(position.X, position.Y, target.position.z), Quaternion.AngleAxis(found.Body2D.Rotation * Mathf.Rad2Deg, Vector3.forward));
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
                if (sceneId == 0)
                {
                    continue;
                }

                presentScenes.Add(sceneId);
                if (loadedScenes.Contains(sceneId) || refusedScenes.Contains(sceneId))
                {
                    continue;
                }

                byte[] bytes = FileOf(sceneId);
                if (bytes == null)
                {
                    refusedScenes.Add(sceneId);
                    Debug.LogWarning($"[Fomoxa] scene 0x{sceneId:X8} has no scene file; Rapier simulates it without static geometry. Export the network scene files");
                    continue;
                }

                try
                {
                    SceneFile file = SceneFileFormat.Read(manager.Registry, bytes);
                    scenes.LoadScene(file);
                    loadedScenes.Add(sceneId);
                    contacts.LoadScene(scene, sceneId, Has(scenes.SceneIds, sceneId) ? scenes.WorldOf(sceneId) : null, file.Colliders.Count, Has(scenes.SceneIds2D, sceneId) ? scenes.WorldOf2D(sceneId) : null, file.Colliders2D.Count);
                }
                catch (Exception exception) when (exception is System.IO.InvalidDataException || exception is InvalidOperationException)
                {
                    refusedScenes.Add(sceneId);
                    Debug.LogException(exception);
                }
            }

            leavingScenes.Clear();
            foreach (uint sceneId in loadedScenes)
            {
                if (!presentScenes.Contains(sceneId))
                {
                    leavingScenes.Add(sceneId);
                }
            }

            foreach (uint sceneId in leavingScenes)
            {
                loadedScenes.Remove(sceneId);
                contacts.UnloadScene(sceneId);
                ForgetWorlds(sceneId);
                scenes.UnloadScene(sceneId);
            }

            refusedScenes.IntersectWith(presentScenes);
            leavingScenes.Clear();
        }

        private void ForgetWorlds(uint sceneId)
        {
            foreach (uint id in scenes.SceneIds)
            {
                if (id == sceneId)
                {
                    stepped.Remove(scenes.WorldOf(id));
                }
            }

            foreach (uint id in scenes.SceneIds2D)
            {
                if (id == sceneId)
                {
                    stepped.Remove(scenes.WorldOf2D(id));
                }
            }
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

            found = new Bound();
            bound.Add(networkObject, found);
            var sources = new List<Collider>();
            var sources2D = new List<Collider2D>();
            uint sceneId = IdOf(networkObject.gameObject.scene);
            try
            {
                if (BodyDescriptions.TryDescribe(networkObject.gameObject, out BodyDesc body, sources))
                {
                    found.Body = scenes.AddBody(networkObject, sceneId, body);
                    found.World = scenes.WorldOf(sceneId);
                    if (networkObject.TryGetComponent(out Rigidbody rigidbody))
                    {
                        found.Rigidbody = rigidbody;
                        found.WasKinematic = rigidbody.isKinematic;
                        rigidbody.isKinematic = true;
                    }
                }

                if (BodyDescriptions.TryDescribe2D(networkObject.gameObject, out BodyDesc2D body2D, sources2D))
                {
                    found.Body2D = scenes.AddBody2D(networkObject, sceneId, body2D);
                    found.World2D = scenes.WorldOf2D(sceneId);
                    if (networkObject.TryGetComponent(out Rigidbody2D rigidbody2D))
                    {
                        found.Rigidbody2D = rigidbody2D;
                        found.BodyType2D = rigidbody2D.bodyType;
                        rigidbody2D.bodyType = RigidbodyType2D.Kinematic;
                    }
                }
            }
            catch (Exception exception) when (exception is NotSupportedException || exception is ArgumentException)
            {
                Debug.LogException(exception, networkObject);
            }

            contacts.AddBody(networkObject, found.Body.IsValid ? found.World : null, sources, found.Body2D.IsValid ? found.World2D : null, sources2D);
            return found;
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

        private static bool Has(IEnumerable<uint> sceneIds, uint sceneId)
        {
            foreach (uint id in sceneIds)
            {
                if (id == sceneId)
                {
                    return true;
                }
            }

            return false;
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

        private sealed class Bound
        {
            public PhysicsBody Body;
            public PhysicsBody2D Body2D;
            public RapierWorld World;
            public RapierWorld2D World2D;
            public Rigidbody Rigidbody;
            public bool WasKinematic;
            public Rigidbody2D Rigidbody2D;
            public RigidbodyType2D BodyType2D;
            public bool IsProxy;

            public bool HasBody => Body.IsValid || Body2D.IsValid;

            public void Restore(NetworkObject networkObject)
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
