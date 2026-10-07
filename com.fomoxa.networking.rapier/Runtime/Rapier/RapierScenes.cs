using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Rapier
{
    public sealed class RapierScenes : IPhysicsScenes, IDisposable
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

        public IEnumerable<uint> SceneIds => worlds.Keys;

        public IEnumerable<uint> SceneIds2D => worlds2D.Keys;

        public RapierWorld WorldOf(uint sceneId)
        {
            if (!worlds.TryGetValue(sceneId, out RapierWorld world))
            {
                world = new RapierWorld(gravity);
                if (layers.TryGetValue(sceneId, out List<uint> masks))
                {
                    world.SetLayerCollisions(masks);
                }

                worlds.Add(sceneId, world);
            }

            return world;
        }

        public RapierWorld2D WorldOf2D(uint sceneId)
        {
            if (!worlds2D.TryGetValue(sceneId, out RapierWorld2D world))
            {
                world = new RapierWorld2D(new Vector2(gravity.X, gravity.Y));
                if (layers2D.TryGetValue(sceneId, out List<uint> masks))
                {
                    world.SetLayerCollisions(masks);
                }

                worlds2D.Add(sceneId, world);
            }

            return world;
        }

        public void LoadScene(SceneFile file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
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
                Remember(layers, file.SceneId, file.LayerCollisions);
                Remember(layers2D, file.SceneId, file.LayerCollisions2D);
                if (colliders.Count > 0)
                {
                    RapierWorld world = WorldOf(file.SceneId);
                    if (layers.TryGetValue(file.SceneId, out List<uint> masks))
                    {
                        world.SetLayerCollisions(masks);
                    }

                    world.AddStatic(colliders);
                }

                if (colliders2D.Count > 0)
                {
                    RapierWorld2D world = WorldOf2D(file.SceneId);
                    if (layers2D.TryGetValue(file.SceneId, out List<uint> masks))
                    {
                        world.SetLayerCollisions(masks);
                    }

                    world.AddStatic(colliders2D);
                }
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"scene 0x{file.SceneId:X8}: {exception.Message}; export the scene again", exception);
            }
        }

        public void UnloadScene(uint sceneId)
        {
            layers.Remove(sceneId);
            layers2D.Remove(sceneId);
            if (worlds.Remove(sceneId, out RapierWorld world))
            {
                histories.Remove(world);
                ForgetOwners(world);
                world.Dispose();
            }

            if (worlds2D.Remove(sceneId, out RapierWorld2D world2D))
            {
                histories.Remove(world2D);
                ForgetOwners(world2D);
                world2D.Dispose();
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

        public PhysicsBody AddBody(INetworkEntity entity, uint sceneId, in BodyDesc body)
        {
            RapierWorld world = WorldOf(sceneId);
            BodyHandle handle = world.CreateBody(body);
            Track(entity, world, handle);
            return new PhysicsBody(world, handle);
        }

        public PhysicsBody2D AddBody2D(INetworkEntity entity, uint sceneId, in BodyDesc2D body)
        {
            RapierWorld2D world = WorldOf2D(sceneId);
            BodyHandle handle = world.CreateBody(body);
            Track(entity, world, handle);
            return new PhysicsBody2D(world, handle);
        }

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

        public void PlaceProxy(INetworkEntity entity)
        {
            if (entity == null || !bodies.TryGetValue(entity, out List<Part> parts))
            {
                return;
            }

            for (int index = 0; index < parts.Count; index++)
            {
                Part part = parts[index];
                if (part.ProxiedFrom.HasValue || !part.World.Contains(part.Handle))
                {
                    continue;
                }

                BodyKind kind = part.World.GetKind(part.Handle);
                part.World.SetKind(part.Handle, BodyKind.Kinematic);
                part.World.SetRewindable(part.Handle, false);
                parts[index] = new Part(part.World, part.Handle, kind);
            }
        }

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
        }

        private static void Remember(Dictionary<uint, List<uint>> remembered, uint sceneId, List<uint> masks)
        {
            if (masks.Count == 0)
            {
                remembered.Remove(sceneId);
                return;
            }

            if (masks.Count != 32)
            {
                throw new ArgumentException($"a layer collision matrix has 32 masks, not {masks.Count}");
            }

            remembered[sceneId] = new List<uint>(masks);
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

        private void ForgetOwners(IPhysicsSimulation world)
        {
            forgotten.Clear();
            foreach ((IPhysicsSimulation World, BodyHandle Body) key in owners.Keys)
            {
                if (ReferenceEquals(key.World, world))
                {
                    forgotten.Add(key);
                }
            }

            foreach ((IPhysicsSimulation World, BodyHandle Body) key in forgotten)
            {
                owners.Remove(key);
            }

            forgotten.Clear();
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
