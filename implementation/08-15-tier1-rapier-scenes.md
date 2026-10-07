# 08.15 — Tầng 1: `RapierScenes`, tập chạm của thế giới

> Phần không tham chiếu `UnityEngine` của bước con 08.15. Kế hoạch và hợp đồng nằm ở mục "Hợp đồng đề xuất của 08.15: nguồn tập chạm" của `implementation/08-prediction-physics.md` trong repo `unity`. Listing phần tập chạm trong crate (`contact.rs`, hàm `touching` của `world3d.rs`, `world2d.rs`) nằm ở [`08-13-tier1-rapier-3d.md`](08-13-tier1-rapier-3d.md) và [`08-14-tier1-rapier-2d.md`](08-14-tier1-rapier-2d.md). Test: `dotnet test` 33/33 trên Linux (.NET 8) và Windows (.NET 9).

| | Việc | Trạng thái |
|---|---|:---:|
| 1 | `RapierScenes : IPhysicsScenes`: một thế giới mỗi scene và mỗi chiều, nạp hình học tĩnh từ tệp scene | ✅ |
| 2 | Body của entity, body đại diện, lịch sử chụp theo thế giới | ✅ |
| 3 | Tập chạm của collider: `fr_collider_touching`, `fr2_collider_touching`, `RapierCollider`, `Touching` | ✅ |
| 4 | `EntityOf`, `TryGetBody` cho phần Unity | ✅ |
| 5 | Test `dotnet`, gồm server console với client console | ✅ |

**Luật:** P27 (Q163 (5) A, (7) A; Q165 (3) A, (4) A; Q166 (1) A, (2) A).

---

## 1. Tổng quan

### `RapierScenes`

- Mỗi `sceneId` có một `RapierWorld` và một `RapierWorld2D`; `sceneId` 0 dành cho object ngoài scene mạng. Hai bảng thế giới là `SortedDictionary`, nên thứ tự bước cố định: `WorldsToStep` trả mọi thế giới 3D theo `sceneId`, rồi mọi thế giới 2D theo `sceneId`.
- `LoadScene(file)` nhớ ma trận layer 3D và 2D của tệp theo `sceneId`, và chỉ tạo thế giới cho chiều mà tệp có collider. Với thế giới đó, hàm đặt ma trận rồi thêm collider tĩnh. Thế giới tạo về sau, khi `WorldOf` hoặc `WorldOf2D` được gọi để thêm body, cũng nhận ma trận đã nhớ. Hình học hỏng (bao lồi suy biến, lưới lỗi) ném `InvalidDataException` kèm yêu cầu xuất lại scene. Ma trận không đủ 32 mặt nạ ném `ArgumentException`.
- `UnloadScene` bỏ ma trận đã nhớ và hủy hai thế giới của scene, cùng lịch sử và bảng chủ body của chúng.
- `AddBody`, `AddBody2D` tạo body trong thế giới của `sceneId` và ghi lại entity sở hữu. `RemoveBodies` xóa mọi body của một entity.
- `PlaceProxy` đổi body của entity sang `Kinematic`, đánh dấu không chạy lại được để `Load` giữ trạng thái hiện tại, và nhớ loại cũ. `EndProxy` trả lại loại cũ và cho chạy lại.
- `HistoryOf(world, capacity)` trả một `PhysicsHistory` cho mỗi thế giới.
- Constructor nhận gravity, mặc định (0, -9.81, 0); thế giới 2D lấy thành phần X, Y. Constructor gọi `RapierPackage.CheckCore`.
- `TrackerOf` trả `null`, vì backend console không có component sự kiện chạm.

### Tập chạm (Q166 (1) A, (2) A)

- `RapierCollider` gồm body và chỉ số; handle body không hợp lệ nghĩa là collider tĩnh. Mỗi thế giới chỉ chứa hình học của một scene, nên chỉ số của collider tĩnh, tức thứ tự trong `AddStatic`, trùng thứ tự của `SceneFile.Colliders` hoặc `Colliders2D`. Chỉ số trong body là thứ tự của `BodyDesc.Colliders`.
- `Touching(collider, into)` thêm vào `into` các collider đang chạm sau `Step` gần nhất. Với sensor, đó là các cặp giao nhau đang `intersecting`. Với collider thường, đó là các cặp tiếp xúc có `has_any_active_contact`, gồm cả tiếp xúc trong khoảng dự đoán của Rapier và tiếp xúc dự báo theo vận tốc. Collider không có trong thế giới thì hàm không thêm gì. Phía C# nới bộ đệm khi crate báo nhiều kết quả hơn sức chứa.
- Cặp giữa body kinematic và body tĩnh, và giữa hai body kinematic, cũng có tập chạm nhờ `ActiveCollisionTypes` trừ `FIXED_FIXED`. Các cặp này không sinh lực, nên băm chuẩn 3D và 2D không đổi.
- `Load` khôi phục pha hẹp cùng với thế giới, nên `Touching` sau `Load` trả tập chạm của tick đã chụp.
- `EntityOf(world, body)` trả entity sở hữu body, tra theo cặp thế giới và handle; bảng này cập nhật khi thêm, xóa body và khi gỡ scene. `TryGetBody(entity, world, out body)` trả handle body của entity trong một thế giới.

### Khác hợp đồng

- `TryGetBody` không có trong hợp đồng. Phần Unity cần handle của body vừa tạo để dựng bảng collider, mà `PhysicsBody` không lộ handle.

---

## 2. Mã

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

`Touching` của `RapierWorld` (`RapierWorld2D` giống, gọi `fr2_collider_touching`):

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

## 3. Test

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

| Test | Kiểm |
|---|---|
| `ASceneFileBuildsTheStaticGeometryOfTheWorldOfItsScene` | Tệp scene dựng sàn của thế giới scene đó; bóng rơi dừng trên sàn |
| `BodiesOfDifferentScenesLiveInDifferentWorlds` | Hai scene, hai thế giới: bóng của scene có sàn dừng trên sàn, bóng của scene không sàn rơi; `WorldsOf` trả thế giới của entity |
| `UnloadingASceneDisposesItsWorldAndRemovingAnEntityRemovesItsBodies` | `RemoveBodies` xóa mọi body của entity; gỡ scene hủy thế giới |
| `AColliderTouchingTheGroundLeadsBackToItsEntityUntilItsBodiesAreRemoved` | `Touching` của sàn trả collider của bóng; `EntityOf` trả entity; sau `RemoveBodies` không còn |
| `AProxyIsKinematicAndKeepsItsStateWhenTheWorldIsLoaded` | Body đại diện kinematic, giữ trạng thái qua `Load`; `EndProxy` trả loại cũ; `HistoryOf` trả cùng lịch sử |
| `BrokenGeometryAsksForANewExport` | Hình học hỏng ném `InvalidDataException` |
| `AConsoleServerSimulatesTheSceneAndItsClientFollowsWithAProxy` | Server console và client console qua loopback, cả hai dùng `RapierScenes`: server mô phỏng, client có body đại diện theo tư thế của server |
| `ATwoDimensionalSceneHasItsOwnWorldSteppedAfterTheThreeDimensionalOne` | Tệp chỉ có collider 2D chỉ tạo thế giới 2D; body 2D rơi lên sàn 2D; `WorldsOf`, `WorldsToStep` trả thế giới 2D |
| `AWorldCreatedAfterTheSceneLoadedGetsTheLayersOfItsSceneFile` | Tệp không có collider 2D không tạo thế giới 2D; thế giới 2D tạo khi thêm body nhận ma trận layer 2D của tệp |
| `AConsoleServerSimulatesATwoDimensionalSceneObjectFromTheSceneFile` | Scene object 2D đọc từ tệp có body và rơi trên server console |
