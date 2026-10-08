# 08.15 Tier 1: `RapierScenes` and the contact sets of a world

> The part of sub-step 08.15 that does not reference `UnityEngine`. The plan and the contract are in the section "Hợp đồng đề xuất của 08.15: nguồn tập chạm" (proposed contract of 08.15: contact source) of `implementation/08-prediction-physics.md` in the `unity` repository. The listings of the contact code in the crate (`contact.rs` and the `touching` functions of `world3d.rs` and `world2d.rs`) are in [`08-13-tier1-rapier-3d.md`](08-13-tier1-rapier-3d.md) and [`08-14-tier1-rapier-2d.md`](08-14-tier1-rapier-2d.md). Tests: `dotnet test` 33/33 on Linux (.NET 8) and Windows (.NET 9).

| | Work | Status |
|---|---|:---:|
| 1 | `RapierScenes : IPhysicsScenes`: one world per scene and per dimension, static geometry loaded from the scene file | ✅ |
| 2 | Entity bodies, proxy bodies, snapshot history per world | ✅ |
| 3 | Collider contact sets: `fr_collider_touching`, `fr2_collider_touching`, `RapierCollider`, `Touching` | ✅ |
| 4 | `EntityOf` and `TryGetBody` for the Unity part | ✅ |
| 5 | dotnet tests, including a console server with a console client | ✅ |

Rules: P27 (Q163 (5) A, (7) A; Q165 (3) A, (4) A; Q166 (1) A, (2) A).

---

## 1. Overview

### `RapierScenes`

- Each `sceneId` has one `RapierWorld` and one `RapierWorld2D`; `sceneId` 0 is for objects outside network scenes. Both world tables are `SortedDictionary`, so the step order is fixed: `WorldsToStep` returns every 3D world by `sceneId`, then every 2D world by `sceneId`.
- `LoadScene(file)` remembers the 3D and 2D layer matrices of the file by `sceneId`, and only creates a world for a dimension in which the file has colliders. For that world it sets the matrix and then adds the static colliders. A world created later, when `WorldOf` or `WorldOf2D` is called to add a body, also gets the remembered matrix. Broken geometry (a degenerate hull, a faulty mesh) throws `InvalidDataException` with a request to export the scene again. A matrix with fewer than 32 masks throws `ArgumentException`.
- `UnloadScene` drops the remembered matrices and disposes the two worlds of the scene, together with their history and their body owner table.
- `AddBody` and `AddBody2D` create a body in the world of `sceneId` and record the owning entity. `RemoveBodies` removes every body of an entity.
- `PlaceProxy` switches the entity's bodies to `Kinematic`, marks them as not rewindable so that `Load` keeps their current state, and remembers the old kind. `EndProxy` restores the old kind and makes them rewindable again.
- `HistoryOf(world, capacity)` returns one `PhysicsHistory` per world.
- The constructor takes the gravity, (0, -9.81, 0) by default; 2D worlds use its X and Y. The constructor calls `RapierPackage.CheckCore`.
- `TrackerOf` returns `null`, because the console backend has no contact event components.

### Contact sets (Q166 (1) A, (2) A)

- A `RapierCollider` is a body and an index; an invalid body handle means a static collider. Each world only holds the geometry of one scene, so the index of a static collider, its position in `AddStatic`, matches the order of `SceneFile.Colliders` or `Colliders2D`. The index within a body is the order of `BodyDesc.Colliders`.
- `Touching(collider, into)` adds to `into` the colliders touching it after the last `Step`. For a sensor, those are the intersection pairs that are `intersecting`. For a regular collider, those are the contact pairs with `has_any_active_contact`, which includes contacts within Rapier's prediction distance and contacts predicted from the velocity. A collider that is not in the world adds nothing. The C# side grows its buffer when the crate reports more results than it can hold.
- Pairs between a kinematic body and a static body, and between two kinematic bodies, also have contact sets, because `ActiveCollisionTypes` includes everything except `FIXED_FIXED`. These pairs produce no force, so the canonical 3D and 2D hashes did not change.
- `Load` restores the narrow phase together with the world, so `Touching` after `Load` returns the contact set of the saved tick.
- `EntityOf(world, body)` returns the entity that owns the body, looked up by world and handle; the table is updated when bodies are added or removed and when a scene is unloaded. `TryGetBody(entity, world, out body)` returns the handle of the entity's body in one world.

### Differences from the contract

- `TryGetBody` is not in the contract. The Unity part needs the handle of a body it just created to build its collider table, and `PhysicsBody` does not expose the handle.

---

## 2. Code

`com.fomoxa.networking.rapier/Runtime/Rapier/RapierCollider.cs`:

```csharp
using System;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Rapier
{
    public readonly struct RapierCollider : IEquatable<RapierCollider>
    {
        private RapierCollider(BodyHandle body, int index)
        {
            Body = body;
            Index = index;
        }

        public BodyHandle Body { get; }

        public int Index { get; }

        public bool IsStatic => !Body.IsValid;

        public static RapierCollider Static(int index)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return new RapierCollider(default, index);
        }

        public static RapierCollider OfBody(BodyHandle body, int index)
        {
            if (!body.IsValid)
            {
                throw new ArgumentException("the body handle is not valid", nameof(body));
            }

            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return new RapierCollider(body, index);
        }

        public bool Equals(RapierCollider other) => Body.Equals(other.Body) && Index == other.Index;

        public override bool Equals(object obj) => obj is RapierCollider other && Equals(other);

        public override int GetHashCode() => (Body.Value * 397) ^ Index;

        internal static RapierCollider FromNative(RapierNative.ColliderRef native) => new RapierCollider(new BodyHandle((int)native.Body), (int)native.Index);
    }
}
```

`com.fomoxa.networking.rapier/Runtime/Rapier/RapierScenes.cs`:

```csharp
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
```

`Touching` of `RapierWorld` (`RapierWorld2D` is the same and calls `fr2_collider_touching`):

```csharp
public void Touching(RapierCollider collider, List<RapierCollider> into)
{
    if (into == null)
    {
        throw new ArgumentNullException(nameof(into));
    }

    uint count = RapierNative.fr_collider_touching(world, Id(collider.Body), (uint)collider.Index, touched, (uint)touched.Length);
    if (count > touched.Length)
    {
        touched = new RapierNative.ColliderRef[count];
        count = RapierNative.fr_collider_touching(world, Id(collider.Body), (uint)collider.Index, touched, (uint)touched.Length);
    }

    for (int index = 0; index < count; index++)
    {
        into.Add(RapierCollider.FromNative(touched[index]));
    }
}
```

---

## 3. Tests

`tests/RapierScenesTest.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;
using Fomoxa.Networking.Standalone;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Rapier.Tests
{
    public sealed class RapierScenesTest
    {
        private const uint ArenaSceneId = 0x00A1_E7A0;
        private const uint LobbySceneId = 0x00B0_B000;
        private const uint BallPrefabId = 0x0000_0B01;
        private const float Tick = 1f / 60f;

        [Test]
        public void ASceneFileBuildsTheStaticGeometryOfTheWorldOfItsScene()
        {
            using (var scenes = new RapierScenes())
            {
                scenes.LoadScene(Arena(ArenaSceneId));
                PhysicsBody ball = scenes.AddBody(Entity(), ArenaSceneId, Ball(new Vector3(0f, 5f, 0f)));

                Run(scenes, 300);

                CollectionAssert.AreEqual(new[] { ArenaSceneId }, scenes.SceneIds);
                Assert.AreEqual(0.5f, ball.Position.Y, 0.05f);
                Assert.AreEqual(PhysicsBackend.Rapier, scenes.Backend);
            }
        }

        [Test]
        public void BodiesOfDifferentScenesLiveInDifferentWorlds()
        {
            using (var scenes = new RapierScenes())
            {
                scenes.LoadScene(Arena(ArenaSceneId));
                StandaloneEntity inArena = Entity();
                StandaloneEntity inLobby = Entity();
                PhysicsBody resting = scenes.AddBody(inArena, ArenaSceneId, Ball(new Vector3(0f, 2f, 0f)));
                PhysicsBody falling = scenes.AddBody(inLobby, LobbySceneId, Ball(new Vector3(0f, 2f, 0f)));
                var worlds = new List<IPhysicsSimulation>();

                Run(scenes, 120);
                scenes.WorldsOf(inLobby, worlds);

                Assert.AreEqual(0.5f, resting.Position.Y, 0.05f);
                Assert.Less(falling.Position.Y, -1f);
                CollectionAssert.AreEqual(new IPhysicsSimulation[] { scenes.WorldOf(LobbySceneId) }, worlds);
                CollectionAssert.AreEqual(new[] { ArenaSceneId, LobbySceneId }, scenes.SceneIds);
            }
        }

        [Test]
        public void UnloadingASceneDisposesItsWorldAndRemovingAnEntityRemovesItsBodies()
        {
            using (var scenes = new RapierScenes())
            {
                scenes.LoadScene(Arena(ArenaSceneId));
                StandaloneEntity left = Entity();
                PhysicsBody first = scenes.AddBody(left, ArenaSceneId, Ball(Vector3.Zero));
                PhysicsBody second = scenes.AddBody(left, ArenaSceneId, Ball(Vector3.UnitX * 3f));
                PhysicsBody other = scenes.AddBody(Entity(), ArenaSceneId, Ball(Vector3.UnitX * 6f));

                scenes.RemoveBodies(left);
                bool otherBeforeUnload = other.IsValid;
                scenes.UnloadScene(ArenaSceneId);
                var stepping = new List<IPhysicsSimulation>();
                scenes.WorldsToStep(stepping);

                Assert.IsFalse(first.IsValid);
                Assert.IsFalse(second.IsValid);
                Assert.IsTrue(otherBeforeUnload);
                Assert.IsFalse(other.IsValid);
                Assert.IsEmpty(stepping);
                CollectionAssert.IsEmpty(scenes.SceneIds);
            }
        }

        [Test]
        public void AColliderTouchingTheGroundLeadsBackToItsEntityUntilItsBodiesAreRemoved()
        {
            using (var scenes = new RapierScenes())
            {
                scenes.LoadScene(Arena(ArenaSceneId));
                StandaloneEntity entity = Entity();
                scenes.AddBody(entity, ArenaSceneId, Ball(new Vector3(0f, 1f, 0f)));
                RapierWorld world = scenes.WorldOf(ArenaSceneId);
                Run(scenes, 60);
                var touching = new List<RapierCollider>();

                world.Touching(RapierCollider.Static(0), touching);

                Assert.AreEqual(1, touching.Count);
                Assert.AreSame(entity, scenes.EntityOf(world, touching[0].Body));
                Assert.IsNull(scenes.EntityOf(null, touching[0].Body));

                scenes.RemoveBodies(entity);

                Assert.IsNull(scenes.EntityOf(world, touching[0].Body));
            }
        }

        [Test]
        public void AProxyIsKinematicAndKeepsItsStateWhenTheWorldIsLoaded()
        {
            using (var scenes = new RapierScenes())
            {
                StandaloneEntity entity = Entity();
                PhysicsBody body = scenes.AddBody(entity, ArenaSceneId, Ball(Vector3.Zero));
                RapierWorld world = scenes.WorldOf(ArenaSceneId);
                PhysicsHistory history = scenes.HistoryOf(world, 4);

                scenes.PlaceProxy(entity);
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);
                body.Position = new Vector3(4f, 0f, 0f);
                world.Load(snapshot);
                bool proxyKinematic = body.IsKinematic;
                scenes.EndProxy(entity);

                Assert.IsTrue(proxyKinematic);
                Assert.AreEqual(4f, body.Position.X, 1e-5f);
                Assert.IsFalse(body.IsKinematic);
                Assert.AreSame(history, scenes.HistoryOf(world, 4));
            }
        }

        [Test]
        public void BrokenGeometryAsksForANewExport()
        {
            using (var scenes = new RapierScenes())
            {
                var broken = new SceneFile { SceneId = LobbySceneId };
                Vector3[] point = { Vector3.One, Vector3.One, Vector3.One, Vector3.One };
                broken.Colliders.Add(SceneFileGeometry.ToFile(new ColliderDesc(BodyShape.ConvexHull(point), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false)));

                InvalidDataException exception = Assert.Throws<InvalidDataException>(() => scenes.LoadScene(broken));
                StringAssert.Contains("export the scene again", exception.Message);
            }
        }

        [Test]
        public void AConsoleServerSimulatesTheSceneAndItsClientFollowsWithAProxy()
        {
            var files = new SceneFiles();
            files.Add(Arena(ArenaSceneId));
            var prefabs = new StandalonePrefabs();
            prefabs.Register(BallPrefabId, 0xBA11, () => new BallEntity());
            var logged = new List<Exception>();
            var log = new NetworkLog(logged.Add, message => { });
            var settings = new NetworkSettings { PhysicsBackend = PhysicsBackend.Rapier };
            var factory = new LoopbackFactory();
            TimeSpan now = TimeSpan.Zero;
            using (var serverPhysics = new RapierScenes())
            using (var clientPhysics = new RapierScenes())
            {
                NetworkRuntime server = StandaloneRuntime.Create(TestObjects.Registry(), settings, factory, prefabs, new StandaloneBehaviours(), files, () => now, log, serverPhysics);
                NetworkRuntime client = StandaloneRuntime.Create(TestObjects.Registry(), settings, factory, prefabs, new StandaloneBehaviours(), files, () => now, log, clientPhysics);
                void Frames(int count)
                {
                    for (int frame = 0; frame < count; frame++)
                    {
                        now += TimeSpan.FromSeconds(Tick);
                        server.BeginFrame(now, Tick);
                        client.BeginFrame(now, Tick);
                        server.EndFrame();
                        client.EndFrame();
                    }
                }

                server.ServerManager.StartConnection(7777);
                client.ClientManager.StartConnection("127.0.0.1", 7777);
                Frames(10);
                server.ServerManager.Scenes.LoadGlobal(new[] { ArenaSceneId });
                Frames(10);
                var ball = new BallEntity { SceneId = ArenaSceneId, Position = new Vector3(0f, 5f, 0f) };
                server.ServerManager.Spawn(ball);
                Frames(180);

                var onClient = (StandaloneEntity)client.ClientManager.Spawned[ball.Record.ObjectId];
                Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
                Assert.IsEmpty(logged);
                Assert.AreEqual(0.5f, ball.Position.Y, 0.05f);
                Assert.IsTrue(onClient.Body.IsValid);
                Assert.IsTrue(onClient.Body.IsKinematic);
                CollectionAssert.AreEqual(new[] { ArenaSceneId }, clientPhysics.SceneIds);
            }
        }

        [Test]
        public void ATwoDimensionalSceneHasItsOwnWorldSteppedAfterTheThreeDimensionalOne()
        {
            using (var scenes = new RapierScenes())
            {
                scenes.LoadScene(Flatland(ArenaSceneId));
                StandaloneEntity entity = Entity();
                PhysicsBody2D crate = scenes.AddBody2D(entity, ArenaSceneId, new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Box(new Vector2(0.5f)), new Vector2(0f, 4f), 0f, 1f));
                var worlds = new List<IPhysicsSimulation>();
                var stepping = new List<IPhysicsSimulation>();

                Run(scenes, 300);
                scenes.WorldsOf(entity, worlds);
                scenes.WorldsToStep(stepping);

                Assert.AreEqual(0.5f, crate.Position.Y, 0.05f);
                CollectionAssert.AreEqual(new IPhysicsSimulation[] { scenes.WorldOf2D(ArenaSceneId) }, worlds);
                CollectionAssert.AreEqual(new IPhysicsSimulation[] { scenes.WorldOf2D(ArenaSceneId) }, stepping);
                CollectionAssert.IsEmpty(scenes.SceneIds);
                CollectionAssert.AreEqual(new[] { ArenaSceneId }, scenes.SceneIds2D);
            }
        }

        [Test]
        public void AWorldCreatedAfterTheSceneLoadedGetsTheLayersOfItsSceneFile()
        {
            using (var scenes = new RapierScenes())
            {
                SceneFile file = Arena(ArenaSceneId);
                for (int layer = 0; layer < 32; layer++)
                {
                    file.LayerCollisions2D.Add(layer == 2 ? ~(1u << 3) : layer == 3 ? ~(1u << 2) : uint.MaxValue);
                }

                scenes.LoadScene(file);
                bool twoDimensionalBeforeBodies = scenes.SceneIds2D.GetEnumerator().MoveNext();
                PhysicsBody2D floor = scenes.AddBody2D(Entity(), ArenaSceneId, new BodyDesc2D(BodyKind.Static, new[] { new ColliderDesc2D(BodyShape2D.Box(new Vector2(10f, 0.5f)), Vector2.Zero, 0f, ColliderMaterial.Default, 2, false) }, new Vector2(0f, -0.5f), 0f, 0f));
                PhysicsBody2D ghost = scenes.AddBody2D(Entity(), ArenaSceneId, new BodyDesc2D(BodyKind.Dynamic, new[] { new ColliderDesc2D(BodyShape2D.Circle(0.5f), Vector2.Zero, 0f, ColliderMaterial.Default, 3, false) }, new Vector2(0f, 2f), 0f, 1f));

                Run(scenes, 120);

                Assert.IsFalse(twoDimensionalBeforeBodies);
                Assert.IsTrue(floor.IsValid);
                Assert.Less(ghost.Position.Y, -1f);
            }
        }

        [Test]
        public void AConsoleServerSimulatesATwoDimensionalSceneObjectFromTheSceneFile()
        {
            SceneFile flatland = Flatland(ArenaSceneId);
            flatland.Objects.Add(new SceneFileObject
            {
                SceneObjectId = 21,
                Fingerprint = 0xC0,
                Pose = new SceneFilePose { PositionY = 4f, RotationW = 1f, ScaleX = 1f, ScaleY = 1f, ScaleZ = 1f },
                Body2D = SceneFileGeometry.ToFile(new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Circle(0.5f), new Vector2(0f, 4f), 0f, 1f)),
            });
            var files = new SceneFiles();
            files.Add(flatland);
            var logged = new List<Exception>();
            var log = new NetworkLog(logged.Add, message => { });
            var settings = new NetworkSettings { PhysicsBackend = PhysicsBackend.Rapier };
            var factory = new LoopbackFactory();
            TimeSpan now = TimeSpan.Zero;
            using (var serverPhysics = new RapierScenes())
            using (var clientPhysics = new RapierScenes())
            {
                NetworkRuntime server = StandaloneRuntime.Create(TestObjects.Registry(), settings, factory, new StandalonePrefabs(), new StandaloneBehaviours(), files, () => now, log, serverPhysics);
                NetworkRuntime client = StandaloneRuntime.Create(TestObjects.Registry(), settings, factory, new StandalonePrefabs(), new StandaloneBehaviours(), files, () => now, log, clientPhysics);
                void Frames(int count)
                {
                    for (int frame = 0; frame < count; frame++)
                    {
                        now += TimeSpan.FromSeconds(Tick);
                        server.BeginFrame(now, Tick);
                        client.BeginFrame(now, Tick);
                        server.EndFrame();
                        client.EndFrame();
                    }
                }

                server.ServerManager.StartConnection(7777);
                client.ClientManager.StartConnection("127.0.0.1", 7777);
                Frames(10);
                server.ServerManager.Scenes.LoadGlobal(new[] { ArenaSceneId });
                Frames(240);

                StandaloneEntity crate = null;
                foreach (INetworkEntity entity in server.ServerManager.Spawned.Values)
                {
                    crate = (StandaloneEntity)entity;
                }

                Assert.IsEmpty(logged);
                Assert.IsNotNull(crate);
                Assert.IsTrue(crate.Body2D.IsValid);
                Assert.AreEqual(0.5f, crate.Position.Y, 0.05f);
                CollectionAssert.AreEqual(new[] { ArenaSceneId }, clientPhysics.SceneIds2D);
            }
        }

        private static SceneFile Flatland(uint sceneId)
        {
            var file = new SceneFile { SceneId = sceneId };
            file.Colliders2D.Add(SceneFileGeometry.ToFile(new ColliderDesc2D(BodyShape2D.Polyline(new[] { new Vector2(-10f, 0f), new Vector2(10f, 0f) }), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false)));
            for (int layer = 0; layer < 32; layer++)
            {
                file.LayerCollisions.Add(uint.MaxValue);
                file.LayerCollisions2D.Add(uint.MaxValue);
            }

            return file;
        }

        private static SceneFile Arena(uint sceneId)
        {
            var file = new SceneFile { SceneId = sceneId };
            file.Colliders.Add(SceneFileGeometry.ToFile(new ColliderDesc(BodyShape.Box(new Vector3(10f, 0.5f, 10f)), new Vector3(0f, -0.5f, 0f), Quaternion.Identity, ColliderMaterial.Default, 0, false)));
            for (int layer = 0; layer < 32; layer++)
            {
                file.LayerCollisions.Add(uint.MaxValue);
            }

            return file;
        }

        private static BodyDesc Ball(Vector3 position) =>
            new BodyDesc(BodyKind.Dynamic, BodyShape.Sphere(0.5f), position, Quaternion.Identity, 1f);

        private static StandaloneEntity Entity() => new StandaloneEntity(1, Array.Empty<EntityBehaviour>());

        private static void Run(RapierScenes scenes, int ticks)
        {
            var worlds = new List<IPhysicsSimulation>();
            for (int tick = 0; tick < ticks; tick++)
            {
                worlds.Clear();
                scenes.WorldsToStep(worlds);
                foreach (IPhysicsSimulation world in worlds)
                {
                    world.Step(Tick);
                }
            }
        }

        private sealed class BallEntity : StandaloneEntity
        {
            public BallEntity()
                : base(BallPrefabId, Array.Empty<EntityBehaviour>())
            {
            }

            public override bool TryGetBody(out BodyDesc body)
            {
                body = Ball(Vector3.Zero);
                return true;
            }
        }

        private sealed class SceneFiles : ISceneFiles
        {
            private readonly Dictionary<uint, byte[]> scenes = new Dictionary<uint, byte[]>();

            public void Add(SceneFile file) => scenes[file.SceneId] = SceneFileFormat.Write(TestObjects.Registry(), file);

            public bool Knows(uint sceneId) => scenes.ContainsKey(sceneId);

            public byte[] Read(uint sceneId) => scenes[sceneId];
        }

        private sealed class LoopbackFactory : ITransportFactory
        {
            private readonly LoopbackListener listener = new LoopbackListener(256);

            public int FrameBudget => FomoxaWire.MaxDataFrameSize;

            public IListenerTransport CreateListener(ushort port, out ushort boundPort)
            {
                boundPort = port;
                return listener;
            }

            public ITransportConnector CreateConnector(string address, ushort port) => new Connector(listener);
        }

        private sealed class Connector : ITransportConnector
        {
            private readonly LoopbackListener listener;

            public Connector(LoopbackListener listener)
            {
                this.listener = listener;
            }

            public ConnectStatus Poll(out ITransport transport)
            {
                transport = listener.Connect();
                return ConnectStatus.Connected;
            }

            public void Dispose()
            {
            }
        }
    }
}
```

| Test | Checks |
|---|---|
| `ASceneFileBuildsTheStaticGeometryOfTheWorldOfItsScene` | A scene file builds the floor of that scene's world; a falling ball stops on it |
| `BodiesOfDifferentScenesLiveInDifferentWorlds` | Two scenes, two worlds: the ball of the scene with a floor stops on it, the ball of the scene without one falls; `WorldsOf` returns the entity's world |
| `UnloadingASceneDisposesItsWorldAndRemovingAnEntityRemovesItsBodies` | `RemoveBodies` removes every body of the entity; unloading the scene disposes its world |
| `AColliderTouchingTheGroundLeadsBackToItsEntityUntilItsBodiesAreRemoved` | `Touching` of the floor returns the ball's collider; `EntityOf` returns the entity, and nothing after `RemoveBodies` |
| `AProxyIsKinematicAndKeepsItsStateWhenTheWorldIsLoaded` | A proxy body is kinematic and keeps its state through `Load`; `EndProxy` restores the old kind; `HistoryOf` returns the same history |
| `BrokenGeometryAsksForANewExport` | Broken geometry throws `InvalidDataException` |
| `AConsoleServerSimulatesTheSceneAndItsClientFollowsWithAProxy` | A console server and a console client over loopback, both with `RapierScenes`: the server simulates, the client has a proxy body at the server's pose |
| `ATwoDimensionalSceneHasItsOwnWorldSteppedAfterTheThreeDimensionalOne` | A file with only 2D colliders creates only a 2D world; a 2D body falls onto the 2D floor; `WorldsOf` and `WorldsToStep` return the 2D world |
| `AWorldCreatedAfterTheSceneLoadedGetsTheLayersOfItsSceneFile` | A file without 2D colliders creates no 2D world; the 2D world created when a body is added gets the file's 2D layer matrix |
| `AConsoleServerSimulatesATwoDimensionalSceneObjectFromTheSceneFile` | A 2D scene object read from the file has a body and falls on the console server |
