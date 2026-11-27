using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Rapier
{
    public sealed class RapierScenes : IPhysicsScenes, IPhysicsMotion, IDisposable
    {
        private static readonly Vector3 EarthGravity = new Vector3(0f, -9.81f, 0f);

        private readonly Vector3 gravity;
        private readonly SortedDictionary<uint, RapierWorld> worlds = new SortedDictionary<uint, RapierWorld>();
        private readonly SortedDictionary<uint, RapierWorld2D> worlds2D = new SortedDictionary<uint, RapierWorld2D>();
        private readonly Dictionary<INetworkEntity, List<Part>> bodies = new Dictionary<INetworkEntity, List<Part>>();
        private readonly Dictionary<(IPhysicsSimulation World, BodyHandle Body), INetworkEntity> owners = new Dictionary<(IPhysicsSimulation World, BodyHandle Body), INetworkEntity>();
        private readonly List<(IPhysicsSimulation World, BodyHandle Body)> forgotten = new List<(IPhysicsSimulation World, BodyHandle Body)>();
        private readonly Dictionary<IPhysicsSimulation, PhysicsHistory> histories = new Dictionary<IPhysicsSimulation, PhysicsHistory>();
        private readonly Dictionary<uint, List<uint>> layers = new Dictionary<uint, List<uint>>();
        private readonly Dictionary<uint, List<uint>> layers2D = new Dictionary<uint, List<uint>>();
        private readonly Dictionary<int, StaticEntry> statics = new Dictionary<int, StaticEntry>();
        private readonly List<int> leavingStatics = new List<int>();
        private readonly List<UnownedBody> unowned = new List<UnownedBody>();
        private readonly List<UnownedBody2D> unowned2D = new List<UnownedBody2D>();
        private readonly List<BodyHandle> movedBodies = new List<BodyHandle>();
        private readonly List<INetworkEntity> released = new List<INetworkEntity>();
        private readonly Dictionary<(uint Scene, ulong SceneObjectId), List<List<FileEffector>>> objectEffectors = new Dictionary<(uint Scene, ulong SceneObjectId), List<List<FileEffector>>>();
        private readonly List<(uint Scene, ulong SceneObjectId)> leavingObjects = new List<(uint Scene, ulong SceneObjectId)>();
        private readonly Dictionary<(uint Scene, uint SceneId), FileEntry> files = new Dictionary<(uint Scene, uint SceneId), FileEntry>();
        private readonly List<(uint Scene, uint SceneId)> leavingFiles = new List<(uint Scene, uint SceneId)>();
        private int nextStatic = 1;

        public RapierScenes()
            : this(EarthGravity)
        {
        }

        public RapierScenes(Vector3 gravity)
        {
            RapierPackage.CheckCore();
            this.gravity = gravity;
        }

        public PhysicsBackend Backend => PhysicsBackend.Rapier;

        public IEnumerable<NetworkScene> Scenes
        {
            get
            {
                foreach (uint scene in worlds.Keys)
                {
                    yield return new NetworkScene(scene);
                }
            }
        }

        public IEnumerable<NetworkScene> Scenes2D
        {
            get
            {
                foreach (uint scene in worlds2D.Keys)
                {
                    yield return new NetworkScene(scene);
                }
            }
        }

        public bool Has(NetworkScene scene) => worlds.ContainsKey(scene.Id);

        public bool Has2D(NetworkScene scene) => worlds2D.ContainsKey(scene.Id);

        public RapierWorld WorldOf(NetworkScene scene)
        {
            if (!worlds.TryGetValue(scene.Id, out RapierWorld world))
            {
                world = new RapierWorld(gravity);
                if (layers.TryGetValue(scene.Id, out List<uint> masks))
                {
                    world.SetLayerCollisions(masks);
                }

                worlds.Add(scene.Id, world);
            }

            return world;
        }

        public RapierWorld2D WorldOf2D(NetworkScene scene)
        {
            if (!worlds2D.TryGetValue(scene.Id, out RapierWorld2D world))
            {
                world = new RapierWorld2D(new Vector2(gravity.X, gravity.Y));
                if (layers2D.TryGetValue(scene.Id, out List<uint> masks))
                {
                    world.SetLayerCollisions(masks);
                }

                worlds2D.Add(scene.Id, world);
            }

            return world;
        }

        public bool TryGetFileStatics(NetworkScene scene, uint sceneId, out StaticGroup statics, out StaticGroup statics2D)
        {
            if (files.TryGetValue((scene.Id, sceneId), out FileEntry entry))
            {
                statics = entry.Statics;
                statics2D = entry.Statics2D;
                return true;
            }

            statics = default;
            statics2D = default;
            return false;
        }

        public void LoadScene(NetworkScene scene, SceneFile file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            if (files.ContainsKey((scene.Id, file.SceneId)))
            {
                throw new ArgumentException($"the scene file of scene 0x{file.SceneId:X8} is already loaded into the world of network scene {scene.Id}", nameof(file));
            }

            var colliders = new List<ColliderDesc>(file.Colliders.Count);
            foreach (SceneFileCollider collider in file.Colliders)
            {
                colliders.Add(SceneFileGeometry.ToDesc(collider));
            }

            var colliders2D = new List<ColliderDesc2D>(file.Colliders2D.Count);
            foreach (SceneFileCollider2D collider in file.Colliders2D)
            {
                colliders2D.Add(SceneFileGeometry.ToDesc(collider));
            }

            try
            {
                List<FileEffector> staticEffectors = EffectorsOf(file.Colliders2D, file.Effectors2D);
                var sceneObjectEffectors = new Dictionary<ulong, List<List<FileEffector>>>();
                foreach (SceneFileObject sceneObject in file.Objects)
                {
                    List<List<FileEffector>> perBody = EffectorsOf(sceneObject, file.Effectors2D);
                    if (perBody != null)
                    {
                        sceneObjectEffectors[sceneObject.SceneObjectId] = perBody;
                    }
                }

                Remember(layers, scene.Id, file.LayerCollisions);
                Remember(layers2D, scene.Id, file.LayerCollisions2D);
                StaticGroup statics = default;
                StaticGroup statics2D = default;
                if (colliders.Count > 0)
                {
                    RapierWorld world = WorldOf(scene);
                    if (layers.TryGetValue(scene.Id, out List<uint> masks))
                    {
                        world.SetLayerCollisions(masks);
                    }

                    statics = Remember(new StaticEntry(scene, world, null, world.AddStatic(colliders)));
                }

                if (colliders2D.Count > 0)
                {
                    RapierWorld2D world = WorldOf2D(scene);
                    if (layers2D.TryGetValue(scene.Id, out List<uint> masks))
                    {
                        world.SetLayerCollisions(masks);
                    }

                    StaticGroup group = world.AddStatic(colliders2D);
                    foreach (FileEffector effector in staticEffectors)
                    {
                        world.AddEffector(group, effector.Colliders, effector.Desc);
                    }

                    statics2D = Remember(new StaticEntry(scene, null, world, group));
                }

                foreach (KeyValuePair<ulong, List<List<FileEffector>>> entry in sceneObjectEffectors)
                {
                    objectEffectors[(scene.Id, entry.Key)] = entry.Value;
                }

                files.Add((scene.Id, file.SceneId), new FileEntry(statics, statics2D, new List<ulong>(sceneObjectEffectors.Keys)));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"scene 0x{file.SceneId:X8}: {exception.Message}; export the scene again", exception);
            }
        }

        private static List<List<FileEffector>> EffectorsOf(SceneFileObject sceneObject, List<SceneFileEffector2D> effectors)
        {
            var perBody = new List<List<FileEffector>>();
            bool any = false;
            if (sceneObject.Body2D != null && sceneObject.Body2D.Kind != 0)
            {
                perBody.Add(EffectorsOf(sceneObject.Body2D.Colliders, effectors));
                any |= perBody[perBody.Count - 1].Count > 0;
            }

            foreach (SceneFileChildBody2D child in sceneObject.ChildBodies2D)
            {
                perBody.Add(EffectorsOf(child.Body?.Colliders ?? new List<SceneFileCollider2D>(), effectors));
                any |= perBody[perBody.Count - 1].Count > 0;
            }

            return any ? perBody : null;
        }

        private static List<FileEffector> EffectorsOf(List<SceneFileCollider2D> colliders, List<SceneFileEffector2D> effectors)
        {
            var grouped = new SortedDictionary<int, List<int>>();
            for (int index = 0; index < colliders.Count; index++)
            {
                int effector = colliders[index].Effector;
                if (effector == 0)
                {
                    continue;
                }

                if (effector > effectors.Count)
                {
                    throw new ArgumentException($"collider {index} names effector {effector} of {effectors.Count}");
                }

                if (!grouped.TryGetValue(effector, out List<int> indices))
                {
                    indices = new List<int>();
                    grouped.Add(effector, indices);
                }

                indices.Add(index);
            }

            var found = new List<FileEffector>(grouped.Count);
            foreach (KeyValuePair<int, List<int>> entry in grouped)
            {
                found.Add(new FileEffector(entry.Value, RapierEffectors.FromFile(effectors[entry.Key - 1])));
            }

            return found;
        }

        public void UnloadSceneFile(uint sceneId)
        {
            if (!files.Remove((0, sceneId), out FileEntry entry))
            {
                return;
            }

            RemoveStatic(entry.Statics);
            RemoveStatic(entry.Statics2D);
            foreach (ulong sceneObjectId in entry.SceneObjects)
            {
                objectEffectors.Remove((0, sceneObjectId));
            }
        }

        public void UnloadScene(NetworkScene scene)
        {
            uint key = scene.Id;
            layers.Remove(key);
            layers2D.Remove(key);
            leavingObjects.Clear();
            foreach ((uint Scene, ulong SceneObjectId) entry in objectEffectors.Keys)
            {
                if (entry.Scene == key)
                {
                    leavingObjects.Add(entry);
                }
            }

            foreach ((uint Scene, ulong SceneObjectId) entry in leavingObjects)
            {
                objectEffectors.Remove(entry);
            }

            leavingObjects.Clear();
            leavingFiles.Clear();
            foreach ((uint Scene, uint SceneId) entry in files.Keys)
            {
                if (entry.Scene == key)
                {
                    leavingFiles.Add(entry);
                }
            }

            foreach ((uint Scene, uint SceneId) entry in leavingFiles)
            {
                files.Remove(entry);
            }

            leavingFiles.Clear();
            if (worlds.Remove(key, out RapierWorld world))
            {
                histories.Remove(world);
                ForgetOwners(world);
                unowned.RemoveAll(entry => ReferenceEquals(entry.World, world));
                world.Dispose();
            }

            if (worlds2D.Remove(key, out RapierWorld2D world2D))
            {
                histories.Remove(world2D);
                ForgetOwners(world2D);
                unowned2D.RemoveAll(entry => ReferenceEquals(entry.World, world2D));
                world2D.Dispose();
            }

            ForgetStatics(scene);
        }

        public StaticGroup AddStatic(NetworkScene scene, IReadOnlyList<ColliderDesc> colliders)
        {
            RapierWorld world = WorldOf(scene);
            return Remember(new StaticEntry(scene, world, null, world.AddStatic(colliders)));
        }

        public StaticGroup AddStatic2D(NetworkScene scene, IReadOnlyList<ColliderDesc2D> colliders)
        {
            RapierWorld2D world = WorldOf2D(scene);
            return Remember(new StaticEntry(scene, null, world, world.AddStatic(colliders)));
        }

        public void RemoveStatic(StaticGroup group)
        {
            if (!statics.Remove(group.Id, out StaticEntry entry))
            {
                return;
            }

            entry.World?.RemoveStatic(entry.Inner);
            entry.World2D?.RemoveStatic(entry.Inner);
        }

        public PhysicsBody AddBody(NetworkScene scene, in BodyDesc body)
        {
            RapierWorld world = WorldOf(scene);
            BodyHandle handle = world.CreateBody(body);
            var created = new PhysicsBody(world, handle);
            unowned.Add(new UnownedBody(world, handle, created));
            return created;
        }

        public PhysicsBody2D AddBody2D(NetworkScene scene, in BodyDesc2D body)
        {
            RapierWorld2D world = WorldOf2D(scene);
            BodyHandle handle = world.CreateBody(body);
            var created = new PhysicsBody2D(world, handle);
            unowned2D.Add(new UnownedBody2D(world, handle, created));
            return created;
        }

        public void RemoveBody(PhysicsBody body)
        {
            int index = unowned.FindIndex(entry => EqualityComparer<PhysicsBody>.Default.Equals(entry.Body, body));
            if (index >= 0)
            {
                unowned[index].World.RemoveBody(unowned[index].Handle);
                unowned.RemoveAt(index);
            }
        }

        public void RemoveBody2D(PhysicsBody2D body)
        {
            int index = unowned2D.FindIndex(entry => EqualityComparer<PhysicsBody2D>.Default.Equals(entry.Body, body));
            if (index >= 0)
            {
                unowned2D[index].World.RemoveBody(unowned2D[index].Handle);
                unowned2D.RemoveAt(index);
            }
        }

        public bool TryGetBody(INetworkEntity entity, IPhysicsSimulation world, out BodyHandle body)
        {
            if (entity != null && world != null && bodies.TryGetValue(entity, out List<Part> parts))
            {
                foreach (Part part in parts)
                {
                    if (ReferenceEquals(part.World, world))
                    {
                        body = part.Handle;
                        return true;
                    }
                }
            }

            body = default;
            return false;
        }

        public INetworkEntity EntityOf(IPhysicsSimulation world, BodyHandle body) =>
            world != null && owners.TryGetValue((world, body), out INetworkEntity entity) ? entity : null;

        public PhysicsBody AddBody(INetworkEntity entity, NetworkScene scene, in BodyDesc body)
        {
            RapierWorld world = WorldOf(scene);
            BodyHandle handle = world.CreateBody(body);
            Track(entity, world, handle);
            return new PhysicsBody(world, handle);
        }

        public PhysicsBody2D AddBody2D(INetworkEntity entity, NetworkScene scene, in BodyDesc2D body)
        {
            RapierWorld2D world = WorldOf2D(scene);
            BodyHandle handle = world.CreateBody(body);
            int order = Count2D(entity);
            Track(entity, world, handle);
            if (entity.SceneObjectId != 0 && objectEffectors.TryGetValue((scene.Id, entity.SceneObjectId), out List<List<FileEffector>> perBody) && order < perBody.Count)
            {
                foreach (FileEffector effector in perBody[order])
                {
                    world.AddEffector(handle, effector.Colliders, effector.Desc);
                }
            }

            return new PhysicsBody2D(world, handle);
        }

        public EffectorHandle AddEffector(INetworkEntity owner, PhysicsBody2D body, IReadOnlyList<int> colliders, in EffectorDesc2D desc) =>
            ((RapierWorld2D)OwnerWorld(owner, body.Handle, true)).AddEffector(body.Handle, colliders, desc);

        public EffectorHandle AddEffector(StaticGroup group, IReadOnlyList<int> colliders, in EffectorDesc2D desc)
        {
            if (!statics.TryGetValue(group.Id, out StaticEntry entry) || entry.World2D == null)
            {
                throw new ArgumentException($"static group {group.Id} is not a 2D group of these scenes", nameof(group));
            }

            return entry.World2D.AddEffector(entry.Inner, colliders, desc);
        }

        public EffectorHandle AddEffector(PhysicsBody2D body, IReadOnlyList<int> colliders, in EffectorDesc2D desc)
        {
            int index = unowned2D.FindIndex(entry => EqualityComparer<PhysicsBody2D>.Default.Equals(entry.Body, body));
            if (index < 0)
            {
                throw new ArgumentException("the body is not an unowned 2D body of these scenes; the body of an entity takes the overload with its owner", nameof(body));
            }

            return unowned2D[index].World.AddEffector(unowned2D[index].Handle, colliders, desc);
        }

        public JointHandle CreateJoint(INetworkEntity owner, PhysicsBody a, PhysicsBody b, in JointDesc desc) =>
            ((RapierWorld)OwnerWorld(owner, a.Handle, false)).CreateJoint(a.Handle, b.Handle, desc);

        public JointHandle CreateJoint(INetworkEntity owner, PhysicsBody2D a, PhysicsBody2D b, in JointDesc2D desc) =>
            ((RapierWorld2D)OwnerWorld(owner, a.Handle, true)).CreateJoint(a.Handle, b.Handle, desc);

        public CharacterMove MoveCharacter(INetworkEntity owner, PhysicsBody body, Vector3 desired, float seconds, in CharacterSettings settings) =>
            MoveCharacter(owner, body, desired, seconds, settings, QueryFilter.All);

        public CharacterMove MoveCharacter(INetworkEntity owner, PhysicsBody body, Vector3 desired, float seconds, in CharacterSettings settings, in QueryFilter filter) =>
            ((RapierWorld)OwnerWorld(owner, body.Handle, false)).MoveCharacter(body.Handle, desired, seconds, settings, filter);

        public CharacterMove2D MoveCharacter(INetworkEntity owner, PhysicsBody2D body, Vector2 desired, float seconds, in CharacterSettings2D settings) =>
            MoveCharacter(owner, body, desired, seconds, settings, QueryFilter.All);

        public CharacterMove2D MoveCharacter(INetworkEntity owner, PhysicsBody2D body, Vector2 desired, float seconds, in CharacterSettings2D settings, in QueryFilter filter) =>
            ((RapierWorld2D)OwnerWorld(owner, body.Handle, true)).MoveCharacter(body.Handle, desired, seconds, settings, filter);

        public void RemoveBodies(INetworkEntity entity)
        {
            if (entity == null || !bodies.Remove(entity, out List<Part> parts))
            {
                return;
            }

            foreach (Part part in parts)
            {
                owners.Remove((part.World, part.Handle));
                part.World.RemoveBody(part.Handle);
            }

            released.Add(entity);
        }

        public bool ReportsMotion(INetworkEntity entity)
        {
            if (entity == null || !bodies.TryGetValue(entity, out List<Part> parts))
            {
                return false;
            }

            foreach (Part part in parts)
            {
                if (!part.World.IsDisposed)
                {
                    return true;
                }
            }

            return false;
        }

        public void CollectMoved(List<INetworkEntity> into)
        {
            foreach (RapierWorld world in worlds.Values)
            {
                CollectMoved(world, into);
            }

            foreach (RapierWorld2D world in worlds2D.Values)
            {
                CollectMoved(world, into);
            }

            into.AddRange(released);
            released.Clear();
        }

        public void WorldsOf(INetworkEntity entity, List<IPhysicsSimulation> into)
        {
            if (entity == null || !bodies.TryGetValue(entity, out List<Part> parts))
            {
                return;
            }

            foreach (Part part in parts)
            {
                if (!part.World.IsDisposed && !into.Contains(part.World))
                {
                    into.Add(part.World);
                }
            }
        }

        public PhysicsHistory HistoryOf(IPhysicsSimulation world, int capacity)
        {
            if (!histories.TryGetValue(world, out PhysicsHistory history))
            {
                history = new PhysicsHistory(world, capacity);
                histories.Add(world, history);
            }

            return history;
        }

        public void PlaceProxy(INetworkEntity entity, PhysicsBody body) => PlaceProxy(entity, body.Handle, false);

        public void PlaceProxy(INetworkEntity entity, PhysicsBody2D body) => PlaceProxy(entity, body.Handle, true);

        public void EndProxy(INetworkEntity entity)
        {
            if (entity == null || !bodies.TryGetValue(entity, out List<Part> parts))
            {
                return;
            }

            for (int index = 0; index < parts.Count; index++)
            {
                Part part = parts[index];
                if (!part.ProxiedFrom.HasValue)
                {
                    continue;
                }

                if (part.World.Contains(part.Handle))
                {
                    part.World.SetKind(part.Handle, part.ProxiedFrom.Value);
                    part.World.SetRewindable(part.Handle, true);
                }

                parts[index] = new Part(part.World, part.Handle);
            }
        }

        public void WorldsToStep(List<IPhysicsSimulation> into)
        {
            foreach (RapierWorld world in worlds.Values)
            {
                into.Add(world);
            }

            foreach (RapierWorld2D world in worlds2D.Values)
            {
                into.Add(world);
            }
        }

        public IContactTracker TrackerOf(IPhysicsSimulation world) => null;

        public void Dispose()
        {
            foreach (RapierWorld world in worlds.Values)
            {
                world.Dispose();
            }

            foreach (RapierWorld2D world in worlds2D.Values)
            {
                world.Dispose();
            }

            worlds.Clear();
            worlds2D.Clear();
            layers.Clear();
            layers2D.Clear();
            bodies.Clear();
            owners.Clear();
            histories.Clear();
            statics.Clear();
            objectEffectors.Clear();
            files.Clear();
            unowned.Clear();
            unowned2D.Clear();
            released.Clear();
        }

        private IRapierBodies OwnerWorld(INetworkEntity owner, BodyHandle body, bool is2D)
        {
            if (owner != null && bodies.TryGetValue(owner, out List<Part> parts))
            {
                foreach (Part part in parts)
                {
                    if ((part.World is IPhysicsWorld2D) == is2D && part.Handle.Equals(body))
                    {
                        return part.World;
                    }
                }
            }

            throw new ArgumentException($"body {body.Value} is not a {(is2D ? "2D " : string.Empty)}body of the owner", nameof(body));
        }

        private void PlaceProxy(INetworkEntity entity, BodyHandle handle, bool is2D)
        {
            if (entity == null || !bodies.TryGetValue(entity, out List<Part> parts))
            {
                return;
            }

            for (int index = 0; index < parts.Count; index++)
            {
                Part part = parts[index];
                if ((part.World is IPhysicsWorld2D) != is2D || !part.Handle.Equals(handle) || part.ProxiedFrom.HasValue || !part.World.Contains(part.Handle))
                {
                    continue;
                }

                BodyKind kind = part.World.GetKind(part.Handle);
                part.World.SetKind(part.Handle, BodyKind.Kinematic);
                part.World.SetRewindable(part.Handle, false);
                parts[index] = new Part(part.World, part.Handle, kind);
            }
        }

        private static void Remember(Dictionary<uint, List<uint>> remembered, uint scene, List<uint> masks)
        {
            if (masks.Count == 0)
            {
                remembered.Remove(scene);
                return;
            }

            if (masks.Count != 32)
            {
                throw new ArgumentException($"a layer collision matrix has 32 masks, not {masks.Count}");
            }

            remembered[scene] = new List<uint>(masks);
        }

        private void Track(INetworkEntity entity, IRapierBodies world, BodyHandle handle)
        {
            if (entity == null)
            {
                world.RemoveBody(handle);
                throw new ArgumentNullException(nameof(entity));
            }

            if (!bodies.TryGetValue(entity, out List<Part> parts))
            {
                parts = new List<Part>();
                bodies.Add(entity, parts);
            }

            parts.Add(new Part(world, handle));
            owners[(world, handle)] = entity;
        }

        private StaticGroup Remember(StaticEntry entry)
        {
            int id = nextStatic++;
            statics.Add(id, entry);
            return new StaticGroup(entry.Scene, id, entry.Inner.FirstIndex, entry.Inner.Count);
        }

        private void ForgetStatics(NetworkScene scene)
        {
            leavingStatics.Clear();
            foreach (KeyValuePair<int, StaticEntry> entry in statics)
            {
                if (entry.Value.Scene == scene)
                {
                    leavingStatics.Add(entry.Key);
                }
            }

            foreach (int id in leavingStatics)
            {
                statics.Remove(id);
            }

            leavingStatics.Clear();
        }

        private void CollectMoved(IRapierBodies world, List<INetworkEntity> into)
        {
            movedBodies.Clear();
            world.CollectMoved(movedBodies);
            foreach (BodyHandle body in movedBodies)
            {
                if (owners.TryGetValue((world, body), out INetworkEntity entity))
                {
                    into.Add(entity);
                }
            }

            movedBodies.Clear();
        }

        private void ForgetOwners(IPhysicsSimulation world)
        {
            forgotten.Clear();
            foreach (KeyValuePair<(IPhysicsSimulation World, BodyHandle Body), INetworkEntity> owner in owners)
            {
                if (ReferenceEquals(owner.Key.World, world))
                {
                    forgotten.Add(owner.Key);
                    released.Add(owner.Value);
                }
            }

            foreach ((IPhysicsSimulation World, BodyHandle Body) key in forgotten)
            {
                owners.Remove(key);
            }

            forgotten.Clear();
        }

        private int Count2D(INetworkEntity entity)
        {
            int count = 0;
            if (entity != null && bodies.TryGetValue(entity, out List<Part> parts))
            {
                foreach (Part part in parts)
                {
                    if (part.World is IPhysicsWorld2D)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private readonly struct FileEffector
        {
            public FileEffector(List<int> colliders, EffectorDesc2D desc)
            {
                Colliders = colliders;
                Desc = desc;
            }

            public List<int> Colliders { get; }

            public EffectorDesc2D Desc { get; }
        }

        private readonly struct StaticEntry
        {
            public StaticEntry(NetworkScene scene, RapierWorld world, RapierWorld2D world2D, StaticGroup inner)
            {
                Scene = scene;
                World = world;
                World2D = world2D;
                Inner = inner;
            }

            public NetworkScene Scene { get; }

            public RapierWorld World { get; }

            public RapierWorld2D World2D { get; }

            public StaticGroup Inner { get; }
        }

        private readonly struct FileEntry
        {
            public FileEntry(StaticGroup statics, StaticGroup statics2D, List<ulong> sceneObjects)
            {
                Statics = statics;
                Statics2D = statics2D;
                SceneObjects = sceneObjects;
            }

            public StaticGroup Statics { get; }

            public StaticGroup Statics2D { get; }

            public List<ulong> SceneObjects { get; }
        }

        private readonly struct UnownedBody
        {
            public UnownedBody(RapierWorld world, BodyHandle handle, PhysicsBody body)
            {
                World = world;
                Handle = handle;
                Body = body;
            }

            public RapierWorld World { get; }

            public BodyHandle Handle { get; }

            public PhysicsBody Body { get; }
        }

        private readonly struct UnownedBody2D
        {
            public UnownedBody2D(RapierWorld2D world, BodyHandle handle, PhysicsBody2D body)
            {
                World = world;
                Handle = handle;
                Body = body;
            }

            public RapierWorld2D World { get; }

            public BodyHandle Handle { get; }

            public PhysicsBody2D Body { get; }
        }

        private readonly struct Part
        {
            public Part(IRapierBodies world, BodyHandle handle, BodyKind? proxiedFrom = null)
            {
                World = world;
                Handle = handle;
                ProxiedFrom = proxiedFrom;
            }

            public IRapierBodies World { get; }

            public BodyHandle Handle { get; }

            public BodyKind? ProxiedFrom { get; }
        }
    }
}
