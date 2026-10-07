# 08.15 — Tầng 2: `RapierPhysics`, nguồn tập chạm ở Unity

> Phần Unity của package trong bước con 08.15; kế hoạch và hợp đồng ở mục 8b của `implementation/08-prediction-physics.md` trong repo `unity`. Điểm cắm ở `Fomoxa.Unity` (`IContactQuery`, `Attach`, `Detach`, `TryDescribe*` có `sources`, `StaticColliders*`) làm ở repo `unity` và mô tả trong `implementation/08-15-tier2-contact-attach.md` của repo đó. Test: Unity EditMode 7/7 trên Unity 6000.5.7f1 (Windows); một test nữa bị bỏ qua vì đó là phép kiểm hai phía của 08.16.

| | Việc | Trạng thái |
|---|---|:---:|
| 1 | Assembly `Fomoxa.Unity.Rapier`; `RapierPhysics : NetworkPhysics` | ✅ |
| 2 | Nạp collider tĩnh của tệp scene khi scene mạng được nạp, gỡ khi scene gỡ | ✅ |
| 3 | Body từ prefab qua bộ chuyển của 08.10, theo thứ tự `ObjectId`; tư thế về `Transform` mỗi bước và sau `Load` | ✅ |
| 4 | Body đại diện ở client | ✅ |
| 5 | Nguồn tập chạm: tracker mỗi thế giới, nhận component, đổi collider Rapier sang `Collider`/`Collider2D` | ✅ |
| 6 | `.meta` của binary theo nền tảng; project Unity kiểm; `Tools/unity-windows-check.sh` | ✅ |

**Luật:** P27 (Q163 (5) A; Q164 (3) A, (4.1) A, (4.2) A; Q165 (4) A; Q166 (1) A, (2) A, (3) A).

---

## 1. Tổng quan

### `RapierPhysics`

- Component đặt trên GameObject của `NetworkManager` và được gán vào trường `physics` của manager. `Backend` là `Rapier`. Trường `gravity` mặc định là (0, -9.81, 0).
- `Begin` lấy `NetworkManager` trên cùng GameObject (không có thì ném), rồi tạo `RapierScenes` và `RapierContacts`. Gọi `Begin` lần hai cũng ném. `Release` trả `Rigidbody` về trạng thái cũ, gỡ mọi component chạm đã nhận và hủy mọi thế giới.
- Mỗi lần Core hỏi các thế giới cần bước (`WorldsToStep`), component làm bốn việc theo thứ tự:
  1. Đồng bộ scene. Mỗi scene Unity đã nạp có `SceneId` trong danh sách scene mạng của manager được nạp tệp scene một lần, qua `SceneRegistry.TryGetSceneFile` và registry của manager. Scene không có tệp hoặc có tệp hỏng được ghi cảnh báo hoặc lỗi một lần, rồi chạy không có hình học tĩnh. Scene đã gỡ thì thế giới của nó cũng bị gỡ.
  2. Đồng bộ body. Object trong `Spawned` của server và của client được gắn body theo thứ tự `ObjectId`; object đã despawn hoặc bị hủy được gỡ body.
  3. Nhận component chạm trên các object có body.
  4. Trả các thế giới, mỗi thế giới bọc trong một lớp ghi tư thế body về `Transform` sau `Step` và sau `Load`, trừ body đại diện.
- Body được mô tả bằng `BodyDescriptions.TryDescribe` và `TryDescribe2D` (bản có `sources`) trên GameObject của object, trong thế giới của scene chứa object. `Rigidbody` chuyển sang kinematic và `Rigidbody2D` sang `Kinematic`; trạng thái cũ được nhớ để trả lại. Nếu object có collider không hỗ trợ, ngoại lệ được ghi vào log và object chạy không có body.
- `BodyOf`, `Body2DOf` gắn body ngay nếu object chưa có. `WorldsOf(scene)` trả các thế giới của scene. `PlaceProxy` gọi `RapierScenes.PlaceProxy` rồi đặt body theo tư thế của `Transform`; `EndProxy` trả lại loại cũ.
- Body được gắn theo thứ tự `ObjectId` để id body và thứ tự giải của Rapier ở client giống server. 08.16 phát hiện điểm này; trước đó thứ tự gắn là thứ tự duyệt một `HashSet`.

### Nguồn tập chạm (`RapierContacts`)

- Mỗi thế giới có một `ContactTracker<Collider>` hoặc `ContactTracker<Collider2D>`, tạo khi nhận component đầu tiên. `TrackerOf` của `RapierPhysics` trả tracker của thế giới nằm trong lớp bọc.
- Với body, `sources` của bộ chuyển cho biết collider Unity ứng với mỗi chỉ số; các mảnh liên tiếp của cùng một nguồn gộp thành một mục kèm số mảnh. Với scene, `BodyDescriptions.StaticColliders*` cho chỉ số tĩnh. Nếu số phần tử khác số collider trong tệp, component ghi cảnh báo và scene đó không có bảng tĩnh.
- `Collect(own, into)` tra `own` ra thế giới và các `RapierCollider` của nó rồi gọi `Touching`. Collider tĩnh trong kết quả đổi về `Collider` Unity qua bảng tĩnh; collider của body đổi qua `RapierScenes.EntityOf` rồi `sources` của object. Kết quả không đổi được, ví dụ của object thuộc manager khác, bị bỏ.
- Theo Q166 (3) A, mỗi bước component nhận `NetworkTrigger`, `NetworkCollision` dưới object có body 3D vào tracker 3D của thế giới chứa body, và `NetworkTrigger2D`, `NetworkCollision2D` dưới object có body 2D vào tracker 2D. Khi nạp scene, component trên vật tĩnh của scene (không nằm dưới `NetworkObject`) được gắn vào tracker của thế giới scene. `Attach` trả sai khi component đã có chủ, nên với host hoặc nhiều manager trong một tiến trình, backend nhận trước giữ component. Component được gỡ khi bỏ body, khi gỡ scene và khi `Release`.

### Project kiểm

- `manifest.json` của `test-project/` trỏ `file:` tới Core ở `../unity` và tới package này; `testables` gồm cả hai package.
- `Tools/unity-windows-check.sh` chép repo và Core sang `C:\Users\<user>\unity-check-rapier`, chạy Unity batchmode với `-assemblyNames Fomoxa.Unity.Rapier.Tests`, rồi chép các `.meta` mới về. Biến `TEST_FILTER` thêm `-testFilter`, biến `FOMOXA_CORE` đổi nguồn Core.
- `.meta` của tệp Linux bật cho Editor Linux và Standalone Linux64; `.meta` của tệp Windows bật cho Editor Windows và Standalone Win64. Cả hai đặt CPU x86_64.

---

## 2. Mã

`com.fomoxa.networking.rapier/Runtime/Unity/Fomoxa.Unity.Rapier.asmdef`:

```json
{
    "name": "Fomoxa.Unity.Rapier",
    "rootNamespace": "Fomoxa.Unity.Rapier",
    "references": [
        "Fomoxa.Unity",
        "Fomoxa.Networking",
        "Fomoxa.Networking.Rapier"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "Fomoxa.Net.dll",
        "Fomoxa.Attributes.dll"
    ],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`com.fomoxa.networking.rapier/Runtime/Unity/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Fomoxa.Unity.Rapier.Tests")]
```

`com.fomoxa.networking.rapier/Runtime/Unity/RapierConversion.cs`:

```csharp
using UnityEngine;

namespace Fomoxa.Unity.Rapier
{
    internal static class RapierConversion
    {
        public static System.Numerics.Vector3 ToNumerics(this Vector3 value) => new System.Numerics.Vector3(value.x, value.y, value.z);

        public static System.Numerics.Quaternion ToNumerics(this Quaternion value) => new System.Numerics.Quaternion(value.x, value.y, value.z, value.w);

        public static Vector3 ToUnity(this System.Numerics.Vector3 value) => new Vector3(value.X, value.Y, value.Z);

        public static Quaternion ToUnity(this System.Numerics.Quaternion value) => new Quaternion(value.X, value.Y, value.Z, value.W);
    }
}
```

`com.fomoxa.networking.rapier/Runtime/Unity/RapierPhysics.cs`:

```csharp
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
```

`com.fomoxa.networking.rapier/Runtime/Unity/RapierContacts.cs`:

```csharp
using System.Collections.Generic;
using Fomoxa.Networking.Rapier;
using Fomoxa.Networking.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Rapier
{
    internal sealed class RapierContacts : IContactQuery, IContactQuery2D
    {
        private readonly RapierScenes scenes;
        private readonly Dictionary<IPhysicsSimulation, ContactTracker<Collider>> trackers = new Dictionary<IPhysicsSimulation, ContactTracker<Collider>>();
        private readonly Dictionary<IPhysicsSimulation, ContactTracker<Collider2D>> trackers2D = new Dictionary<IPhysicsSimulation, ContactTracker<Collider2D>>();
        private readonly Dictionary<IPhysicsSimulation, List<Collider>> statics = new Dictionary<IPhysicsSimulation, List<Collider>>();
        private readonly Dictionary<IPhysicsSimulation, List<Collider2D>> statics2D = new Dictionary<IPhysicsSimulation, List<Collider2D>>();
        private readonly Dictionary<Collider, Place> places = new Dictionary<Collider, Place>();
        private readonly Dictionary<Collider2D, Place> places2D = new Dictionary<Collider2D, Place>();
        private readonly Dictionary<NetworkObject, BodyColliders> bodies = new Dictionary<NetworkObject, BodyColliders>();
        private readonly Dictionary<uint, SceneColliders> loaded = new Dictionary<uint, SceneColliders>();
        private readonly List<RapierCollider> touched = new List<RapierCollider>();
        private readonly List<NetworkTrigger> triggers = new List<NetworkTrigger>();
        private readonly List<NetworkCollision> collisions = new List<NetworkCollision>();
        private readonly List<NetworkTrigger2D> triggers2D = new List<NetworkTrigger2D>();
        private readonly List<NetworkCollision2D> collisions2D = new List<NetworkCollision2D>();

        public RapierContacts(RapierScenes scenes)
        {
            this.scenes = scenes;
        }

        public IContactTracker TrackerOf(IPhysicsSimulation world)
        {
            if (world == null)
            {
                return null;
            }

            if (trackers.TryGetValue(world, out ContactTracker<Collider> tracker))
            {
                return tracker;
            }

            return trackers2D.TryGetValue(world, out ContactTracker<Collider2D> tracker2D) ? tracker2D : null;
        }

        public void AddBody(NetworkObject networkObject, RapierWorld world, List<Collider> sources, RapierWorld2D world2D, List<Collider2D> sources2D)
        {
            var entry = new BodyColliders { World = world, World2D = world2D, Sources = sources, Sources2D = sources2D };
            bodies[networkObject] = entry;
            if (world != null && scenes.TryGetBody(networkObject, world, out BodyHandle body))
            {
                Locate(sources, world, body, places);
            }

            if (world2D != null && scenes.TryGetBody(networkObject, world2D, out BodyHandle body2D))
            {
                Locate(sources2D, world2D, body2D, places2D);
            }
        }

        public void RemoveBody(NetworkObject networkObject)
        {
            if (!bodies.Remove(networkObject, out BodyColliders entry))
            {
                return;
            }

            Release(entry.Claimed);
            Forget(entry.Sources, entry.World, places);
            Forget(entry.Sources2D, entry.World2D, places2D);
        }

        public void Claim(NetworkObject networkObject)
        {
            if (networkObject == null || !bodies.TryGetValue(networkObject, out BodyColliders entry))
            {
                return;
            }

            if (entry.World != null)
            {
                ContactTracker<Collider> tracker = TrackerFor(entry.World);
                networkObject.GetComponentsInChildren(true, triggers);
                networkObject.GetComponentsInChildren(true, collisions);
                Claim(tracker, entry.Claimed);
            }

            if (entry.World2D != null)
            {
                ContactTracker<Collider2D> tracker2D = TrackerFor2D(entry.World2D);
                networkObject.GetComponentsInChildren(true, triggers2D);
                networkObject.GetComponentsInChildren(true, collisions2D);
                Claim(tracker2D, entry.Claimed);
            }
        }

        public void LoadScene(Scene scene, uint sceneId, RapierWorld world, int colliderCount, RapierWorld2D world2D, int colliderCount2D)
        {
            var entry = new SceneColliders();
            loaded[sceneId] = entry;
            if (world != null)
            {
                var sources = new List<Collider>();
                BodyDescriptions.StaticColliders(scene, sources);
                if (sources.Count == colliderCount)
                {
                    entry.World = world;
                    statics[world] = sources;
                    Locate(sources, world, default, places);
                    ContactTracker<Collider> tracker = TrackerFor(world);
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        root.GetComponentsInChildren(true, triggers);
                        root.GetComponentsInChildren(true, collisions);
                        DropNetworkObjects(triggers);
                        DropNetworkObjects(collisions);
                        Claim(tracker, entry.Claimed);
                    }
                }
                else
                {
                    Mismatch(scene, "3D", sources.Count, colliderCount);
                }
            }

            if (world2D != null)
            {
                var sources2D = new List<Collider2D>();
                BodyDescriptions.StaticColliders2D(scene, sources2D);
                if (sources2D.Count == colliderCount2D)
                {
                    entry.World2D = world2D;
                    statics2D[world2D] = sources2D;
                    Locate(sources2D, world2D, default, places2D);
                    ContactTracker<Collider2D> tracker2D = TrackerFor2D(world2D);
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        root.GetComponentsInChildren(true, triggers2D);
                        root.GetComponentsInChildren(true, collisions2D);
                        DropNetworkObjects(triggers2D);
                        DropNetworkObjects(collisions2D);
                        Claim(tracker2D, entry.Claimed);
                    }
                }
                else
                {
                    Mismatch(scene, "2D", sources2D.Count, colliderCount2D);
                }
            }
        }

        public void UnloadScene(uint sceneId)
        {
            if (!loaded.Remove(sceneId, out SceneColliders entry))
            {
                return;
            }

            Release(entry.Claimed);
            if (entry.World != null && statics.Remove(entry.World, out List<Collider> sources))
            {
                Forget(sources, entry.World, places);
                trackers.Remove(entry.World);
            }

            if (entry.World2D != null && statics2D.Remove(entry.World2D, out List<Collider2D> sources2D))
            {
                Forget(sources2D, entry.World2D, places2D);
                trackers2D.Remove(entry.World2D);
            }
        }

        public void Clear()
        {
            foreach (BodyColliders entry in bodies.Values)
            {
                Release(entry.Claimed);
            }

            foreach (SceneColliders entry in loaded.Values)
            {
                Release(entry.Claimed);
            }

            bodies.Clear();
            loaded.Clear();
            trackers.Clear();
            trackers2D.Clear();
            statics.Clear();
            statics2D.Clear();
            places.Clear();
            places2D.Clear();
        }

        public void Collect(Collider own, HashSet<Collider> into)
        {
            if (!places.TryGetValue(own, out Place place) || !(place.World is RapierWorld world) || world.IsDisposed)
            {
                return;
            }

            touched.Clear();
            for (int piece = 0; piece < place.Count; piece++)
            {
                world.Touching(place.At(piece), touched);
            }

            foreach (RapierCollider collider in touched)
            {
                Collider other = ColliderAt(world, collider);
                if (other != null)
                {
                    into.Add(other);
                }
            }

            touched.Clear();
        }

        public void Collect(Collider2D own, HashSet<Collider2D> into)
        {
            if (!places2D.TryGetValue(own, out Place place) || !(place.World is RapierWorld2D world) || world.IsDisposed)
            {
                return;
            }

            touched.Clear();
            for (int piece = 0; piece < place.Count; piece++)
            {
                world.Touching(place.At(piece), touched);
            }

            foreach (RapierCollider collider in touched)
            {
                Collider2D other = ColliderAt(world, collider);
                if (other != null)
                {
                    into.Add(other);
                }
            }

            touched.Clear();
        }

        private Collider ColliderAt(RapierWorld world, RapierCollider collider)
        {
            if (collider.IsStatic)
            {
                return statics.TryGetValue(world, out List<Collider> sources) && collider.Index < sources.Count ? sources[collider.Index] : null;
            }

            return scenes.EntityOf(world, collider.Body) is NetworkObject networkObject && bodies.TryGetValue(networkObject, out BodyColliders entry) && ReferenceEquals(entry.World, world) && collider.Index < entry.Sources.Count
                ? entry.Sources[collider.Index]
                : null;
        }

        private Collider2D ColliderAt(RapierWorld2D world, RapierCollider collider)
        {
            if (collider.IsStatic)
            {
                return statics2D.TryGetValue(world, out List<Collider2D> sources) && collider.Index < sources.Count ? sources[collider.Index] : null;
            }

            return scenes.EntityOf(world, collider.Body) is NetworkObject networkObject && bodies.TryGetValue(networkObject, out BodyColliders entry) && ReferenceEquals(entry.World2D, world) && collider.Index < entry.Sources2D.Count
                ? entry.Sources2D[collider.Index]
                : null;
        }

        private ContactTracker<Collider> TrackerFor(IPhysicsSimulation world)
        {
            if (!trackers.TryGetValue(world, out ContactTracker<Collider> tracker))
            {
                tracker = new ContactTracker<Collider>();
                trackers.Add(world, tracker);
            }

            return tracker;
        }

        private ContactTracker<Collider2D> TrackerFor2D(IPhysicsSimulation world)
        {
            if (!trackers2D.TryGetValue(world, out ContactTracker<Collider2D> tracker))
            {
                tracker = new ContactTracker<Collider2D>();
                trackers2D.Add(world, tracker);
            }

            return tracker;
        }

        private void Claim(ContactTracker<Collider> tracker, List<MonoBehaviour> claimed)
        {
            foreach (NetworkTrigger trigger in triggers)
            {
                if (trigger.Attach(tracker, this))
                {
                    claimed.Add(trigger);
                }
            }

            foreach (NetworkCollision collision in collisions)
            {
                if (collision.Attach(tracker, this))
                {
                    claimed.Add(collision);
                }
            }

            triggers.Clear();
            collisions.Clear();
        }

        private void Claim(ContactTracker<Collider2D> tracker, List<MonoBehaviour> claimed)
        {
            foreach (NetworkTrigger2D trigger in triggers2D)
            {
                if (trigger.Attach(tracker, this))
                {
                    claimed.Add(trigger);
                }
            }

            foreach (NetworkCollision2D collision in collisions2D)
            {
                if (collision.Attach(tracker, this))
                {
                    claimed.Add(collision);
                }
            }

            triggers2D.Clear();
            collisions2D.Clear();
        }

        private void Release(List<MonoBehaviour> claimed)
        {
            foreach (MonoBehaviour component in claimed)
            {
                switch (component)
                {
                    case NetworkTrigger trigger:
                        trigger.Detach((IContactQuery)this);
                        break;
                    case NetworkCollision collision:
                        collision.Detach((IContactQuery)this);
                        break;
                    case NetworkTrigger2D trigger2D:
                        trigger2D.Detach((IContactQuery2D)this);
                        break;
                    case NetworkCollision2D collision2D:
                        collision2D.Detach((IContactQuery2D)this);
                        break;
                }
            }

            claimed.Clear();
        }

        private static void Locate<TCollider>(List<TCollider> sources, IPhysicsSimulation world, BodyHandle body, Dictionary<TCollider, Place> into)
        {
            for (int index = 0; index < sources.Count; index++)
            {
                TCollider source = sources[index];
                if (into.TryGetValue(source, out Place place) && ReferenceEquals(place.World, world) && place.First.Index + place.Count == index)
                {
                    into[source] = new Place(world, place.First, place.Count + 1);
                }
                else
                {
                    into[source] = new Place(world, body.IsValid ? RapierCollider.OfBody(body, index) : RapierCollider.Static(index), 1);
                }
            }
        }

        private static void Forget<TCollider>(List<TCollider> sources, IPhysicsSimulation world, Dictionary<TCollider, Place> from)
        {
            if (sources == null || world == null)
            {
                return;
            }

            foreach (TCollider source in sources)
            {
                if (from.TryGetValue(source, out Place place) && ReferenceEquals(place.World, world))
                {
                    from.Remove(source);
                }
            }
        }

        private static void DropNetworkObjects<TComponent>(List<TComponent> components)
            where TComponent : Component =>
            components.RemoveAll(component => component.GetComponentInParent<NetworkObject>(true) != null);

        private static void Mismatch(Scene scene, string dimension, int found, int expected) =>
            Debug.LogWarning($"[Fomoxa] scene {scene.path} has {found} static {dimension} colliders but its scene file has {expected}; contacts with its static {dimension} geometry are left out. Export the network scene files again");

        private readonly struct Place
        {
            public Place(IPhysicsSimulation world, RapierCollider first, int count)
            {
                World = world;
                First = first;
                Count = count;
            }

            public IPhysicsSimulation World { get; }

            public RapierCollider First { get; }

            public int Count { get; }

            public RapierCollider At(int piece) =>
                First.IsStatic ? RapierCollider.Static(First.Index + piece) : RapierCollider.OfBody(First.Body, First.Index + piece);
        }

        private sealed class BodyColliders
        {
            public RapierWorld World;
            public RapierWorld2D World2D;
            public List<Collider> Sources;
            public List<Collider2D> Sources2D;
            public readonly List<MonoBehaviour> Claimed = new List<MonoBehaviour>();
        }

        private sealed class SceneColliders
        {
            public RapierWorld World;
            public RapierWorld2D World2D;
            public readonly List<MonoBehaviour> Claimed = new List<MonoBehaviour>();
        }
    }
}
```

`com.fomoxa.networking.rapier/Runtime/Plugins/Linux/x86_64/libfomoxa_rapier.so.meta`:

```yaml
fileFormatVersion: 2
guid: 08a1ccbe55ab4fc08de37e0dfd6baa4c
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 1
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      : Any
    second:
      enabled: 0
      settings:
        Exclude Editor: 0
        Exclude Win64: 1
        Exclude Linux64: 0
  - first:
      Any: 
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        CPU: x86_64
        DefaultValueInitialized: true
        OS: Linux
  - first:
      Standalone: Linux64
    second:
      enabled: 1
      settings:
        CPU: x86_64
  - first:
      Standalone: Win64
    second:
      enabled: 0
      settings:
        CPU: None
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

`com.fomoxa.networking.rapier/Runtime/Plugins/Windows/x86_64/fomoxa_rapier.dll.meta`:

```yaml
fileFormatVersion: 2
guid: f8dd3a70224d42f9830553046938e68a
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 1
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      : Any
    second:
      enabled: 0
      settings:
        Exclude Editor: 0
        Exclude Linux64: 1
        Exclude Win64: 0
  - first:
      Any: 
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        CPU: x86_64
        DefaultValueInitialized: true
        OS: Windows
  - first:
      Standalone: Win64
    second:
      enabled: 1
      settings:
        CPU: x86_64
  - first:
      Standalone: Linux64
    second:
      enabled: 0
      settings:
        CPU: None
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

`test-project/Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.fomoxa.networking": "file:../../../unity/com.fomoxa.networking",
    "com.fomoxa.networking.rapier": "file:../../com.fomoxa.networking.rapier",
    "com.unity.test-framework": "1.6.0"
  },
  "testables": [
    "com.fomoxa.networking",
    "com.fomoxa.networking.rapier"
  ]
}
```

`Tools/unity-windows-check.sh`:

```bash
#!/bin/bash
set -e

UNITY_VERSION="6000.5.7f1"
WIN_USER="admin"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$SCRIPT_DIR/.."
CORE="${FOMOXA_CORE:-$REPO/../unity/com.fomoxa.networking}"
WIN_ROOT="C:\\Users\\$WIN_USER\\unity-check-rapier"
DST="/mnt/c/Users/$WIN_USER/unity-check-rapier"
UNITY="/mnt/c/Program Files/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity.exe"
LOG_DIR="$REPO/test-project/Logs"
LOG="$LOG_DIR/unity-windows-check.log"
RESULTS="$LOG_DIR/editmode-results.xml"

mkdir -p "$LOG_DIR" "$DST/unity" "$DST/networking-rapier"

rsync -a --delete \
  --exclude='.git' \
  --exclude='test-project/Library' \
  --exclude='test-project/Logs' \
  --exclude='test-project/Temp' \
  --exclude='test-project/UserSettings' \
  --exclude='tests/bin' \
  --exclude='tests/obj' \
  --exclude='native/fomoxa-rapier/target' \
  "$REPO/" "$DST/networking-rapier/"
rsync -a --delete "$CORE/" "$DST/unity/com.fomoxa.networking/"

rm -f "$DST/unity.log" "$DST/editmode-results.xml"

set +e
(
  cd "$DST"
  "$UNITY" -batchmode -nographics \
    -projectPath "$WIN_ROOT\\networking-rapier\\test-project" \
    -runTests -testPlatform EditMode -assemblyNames "Fomoxa.Unity.Rapier.Tests" ${TEST_FILTER:+-testFilter "$TEST_FILTER"} \
    -testResults "$WIN_ROOT\\editmode-results.xml" \
    -logFile "$WIN_ROOT\\unity.log" < /dev/null > /dev/null 2>&1
)
EXIT_CODE=$?
set -e

cp "$DST/unity.log" "$LOG" 2>/dev/null || true
cp "$DST/editmode-results.xml" "$RESULTS" 2>/dev/null || true

rsync -a --include='*/' --include='*.meta' --exclude='*' "$DST/networking-rapier/com.fomoxa.networking.rapier/" "$REPO/com.fomoxa.networking.rapier/"
rsync -a "$DST/networking-rapier/test-project/ProjectSettings/" "$REPO/test-project/ProjectSettings/"
cp "$DST/networking-rapier/test-project/Packages/packages-lock.json" "$REPO/test-project/Packages/" 2>/dev/null || true

echo "=== exit code: $EXIT_CODE ==="
echo "=== compile errors ==="
grep -n "error CS\|Aborting batchmode\|Fatal Error" "$LOG" || echo "none found"
echo "=== test summary ==="
grep -o '<test-run [^>]*>' "$RESULTS" 2>/dev/null | grep -o 'result="[^"]*"\|total="[^"]*"\|passed="[^"]*"\|failed="[^"]*"' || echo "no results file"
echo "=== full log: $LOG ==="
```

---

## 3. Test

`com.fomoxa.networking.rapier/Tests/Unity/Fomoxa.Unity.Rapier.Tests.asmdef`:

```json
{
    "name": "Fomoxa.Unity.Rapier.Tests",
    "rootNamespace": "Fomoxa.Unity.Rapier.Tests",
    "references": [
        "Fomoxa.Unity.Rapier",
        "Fomoxa.Networking.Rapier",
        "Fomoxa.Unity",
        "Fomoxa.Networking",
        "Fomoxa.Networking.Tests.Fixtures",
        "Fomoxa.Unity.Tests.Support",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll",
        "Fomoxa.Attributes.dll",
        "Fomoxa.Net.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`com.fomoxa.networking.rapier/Tests/Unity/RapierPhysicsTest.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using BundleFixture;
using Fomoxa.Networking;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Rapier.Tests
{
    public sealed class RapierPhysicsTest
    {
        private const double FrameSeconds = 1.0 / 60;
        private const uint BallPrefabId = 0xBA11;
        private const uint GroundPrefabId = 0x6400;
        private const uint ArenaSceneId = 0x77;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private readonly List<MonoBehaviour> enabled = new List<MonoBehaviour>();
        private InMemoryNetworkTransport network;
        private TimeSpan now;

        [SetUp]
        public void CreateNetwork()
        {
            now = TimeSpan.FromSeconds(1);
            network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
        }

        [TearDown]
        public void DestroyManagers()
        {
            foreach (MonoBehaviour behaviour in enabled)
            {
                if (behaviour != null)
                {
                    Disable(behaviour);
                }
            }

            enabled.Clear();
            foreach (NetworkManager manager in managers)
            {
                manager.ClientManager.StopConnection();
                manager.ServerManager.StopConnection();
                manager.ReleasePhysicsSimulation();
            }

            foreach (GameObject gameObject in created)
            {
                if (gameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }

            managers.Clear();
            created.Clear();
        }

        [Test]
        public void TheServerSimulatesSpawnedObjectsWithRapierAndMovesTheirTransforms()
        {
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball => { ball.AddComponent<SphereCollider>().radius = 0.5f; ball.AddComponent<Rigidbody>().mass = 2f; });
            NetworkObject groundPrefab = Prefab("Ground", GroundPrefabId, ground => ground.AddComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f));
            NetworkManager server = Manager(ballPrefab, groundPrefab);
            server.ServerManager.StartConnection(1);
            NetworkObject ground = Spawn(server, groundPrefab, new Vector3(0f, -0.5f, 0f));
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 4f, 0f));

            RunFrames(180);

            Assert.AreEqual(0.5f, ball.transform.position.y, 0.05f);
            Assert.IsTrue(ball.GetComponent<Rigidbody>().isKinematic);
            Assert.IsTrue(ball.Body.IsValid);
            Assert.AreEqual(2f, ball.Body.Mass, 1e-4f);
            Assert.AreEqual(ball.transform.position.y, ball.Body.Position.Y, 1e-5f);
            Assert.IsTrue(ground.Body.IsValid);

            server.ReleasePhysicsSimulation();

            Assert.IsFalse(ball.GetComponent<Rigidbody>().isKinematic);
        }

        [Test]
        public void TwoDimensionalObjectsFallOntoTwoDimensionalGround()
        {
            NetworkObject ballPrefab = Prefab("Ball2D", BallPrefabId, ball => { ball.AddComponent<CircleCollider2D>().radius = 0.5f; ball.AddComponent<Rigidbody2D>(); });
            NetworkObject groundPrefab = Prefab("Ground2D", GroundPrefabId, ground => ground.AddComponent<BoxCollider2D>().size = new Vector2(20f, 1f));
            NetworkManager server = Manager(ballPrefab, groundPrefab);
            server.ServerManager.StartConnection(1);
            Spawn(server, groundPrefab, new Vector3(0f, -0.5f, 0f));
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 4f, 3f));

            RunFrames(180);

            Assert.AreEqual(0.5f, ball.transform.position.y, 0.05f);
            Assert.AreEqual(3f, ball.transform.position.z, 1e-5f);
            Assert.AreEqual(RigidbodyType2D.Kinematic, ball.GetComponent<Rigidbody2D>().bodyType);
            Assert.IsTrue(ball.Body2D.IsValid);
            Assert.IsFalse(ball.Body.IsValid);
        }

        [Test]
        public void TheStaticGeometryOfANetworkSceneComesFromItsSceneFile()
        {
            Static("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f), false);
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball => ball.AddComponent<SphereCollider>().radius = 0.5f);
            ballPrefab.gameObject.AddComponent<Rigidbody>();
            NetworkManager server = Manager(ballPrefab);
            var physics = server.GetComponent<RapierPhysics>();
            UseActiveScene(server);
            server.ServerManager.StartConnection(1);
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 4f, 0f));

            RunFrames(180);

            CollectionAssert.AreEqual(new[] { ArenaSceneId }, physics.Scenes.SceneIds);
            Assert.AreEqual(0.5f, ball.transform.position.y, 0.05f);
        }

        [Test]
        public void AClientKeepsTheObjectsItDoesNotPredictAsKinematicProxies()
        {
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball => { ball.AddComponent<SphereCollider>().radius = 0.5f; ball.AddComponent<Rigidbody>(); });
            NetworkManager server = Manager(ballPrefab);
            NetworkManager client = Manager(ballPrefab);
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 4f, 0f));
            RunFrames(30);

            var onClient = (NetworkObject)client.ClientManager.Spawned[ball.ObjectId];
            Vector3 placed = onClient.transform.position;
            RunFrames(30);

            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            Assert.IsTrue(onClient.Body.IsValid);
            Assert.IsTrue(onClient.Body.IsKinematic);
            Assert.AreEqual(placed, onClient.transform.position);
            Assert.AreEqual(onClient.transform.position.y, onClient.Body.Position.Y, 1e-5f);
        }

        [Test]
        public void ACollisionOnABallReportsTheGroundOfTheSceneFile()
        {
            Static("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f), false);
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball =>
            {
                ball.AddComponent<SphereCollider>().radius = 0.5f;
                ball.AddComponent<Rigidbody>();
                ball.AddComponent<NetworkCollision>();
            });
            NetworkManager server = Manager(ballPrefab);
            UseActiveScene(server);
            server.ServerManager.StartConnection(1);
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 3f, 0f));
            var collision = Enable(ball.GetComponent<NetworkCollision>());
            var events = new List<string>();
            collision.OnEnter += other => events.Add("enter " + other.name);
            collision.OnExit += other => events.Add("exit " + other.name);

            RunFrames(120);

            Assert.IsTrue(collision.IsAttached);
            CollectionAssert.AreEqual(new[] { "enter Ground" }, events);
            CollectionAssert.AreEqual(new[] { "Ground" }, Names(collision.Touching));
        }

        [Test]
        public void AStaticTriggerSeesABallPassThroughUntilThePhysicsIsReleased()
        {
            GameObject zone = Static("Zone", Vector3.zero, new Vector3(4f, 1f, 4f), true);
            var trigger = Enable(zone.AddComponent<NetworkTrigger>());
            var events = new List<string>();
            trigger.OnEnter += other => events.Add("enter " + other.name);
            trigger.OnExit += other => events.Add("exit " + other.name);
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball =>
            {
                ball.AddComponent<SphereCollider>().radius = 0.25f;
                ball.AddComponent<Rigidbody>();
            });
            NetworkManager server = Manager(ballPrefab);
            UseActiveScene(server);
            server.ServerManager.StartConnection(1);
            Spawn(server, ballPrefab, new Vector3(0f, 2f, 0f));

            RunFrames(90);

            Assert.IsTrue(trigger.IsAttached);
            CollectionAssert.AreEqual(new[] { "enter Ball(Clone)", "exit Ball(Clone)" }, events);

            server.ReleasePhysicsSimulation();

            Assert.IsFalse(trigger.IsAttached);
        }

        [Test]
        public void AClientProxyWithATriggerTouchesTheStaticGeometryOfItsOwnWorld()
        {
            Static("Wall", new Vector3(0f, 4f, 0f), new Vector3(2f, 2f, 2f), false);
            NetworkObject probePrefab = Prefab("Probe", BallPrefabId, probe =>
            {
                var sphere = probe.AddComponent<SphereCollider>();
                sphere.radius = 0.5f;
                sphere.isTrigger = true;
                probe.AddComponent<Rigidbody>().isKinematic = true;
                probe.AddComponent<NetworkTrigger>();
            });
            NetworkManager server = Manager(probePrefab);
            NetworkManager client = Manager(probePrefab);
            UseActiveScene(server);
            UseActiveScene(client);
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            NetworkObject probe = Spawn(server, probePrefab, new Vector3(0f, 4f, 0f));
            RunFrames(30);
            var onClient = (NetworkObject)client.ClientManager.Spawned[probe.ObjectId];
            var trigger = Enable(onClient.GetComponent<NetworkTrigger>());

            RunFrames(10);

            Assert.IsTrue(onClient.Body.IsKinematic);
            Assert.IsTrue(trigger.IsAttached);
            CollectionAssert.AreEqual(new[] { "Wall" }, Names(trigger.Touching));
        }

        private GameObject Static(string name, Vector3 position, Vector3 size, bool isTrigger)
        {
            var gameObject = new GameObject(name);
            created.Add(gameObject);
            gameObject.transform.position = position;
            var box = gameObject.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = isTrigger;
            return gameObject;
        }

        private static void UseActiveScene(NetworkManager manager)
        {
            Scene active = SceneManager.GetActiveScene();
            var sources = new List<Collider>();
            var colliders = new List<ColliderDesc>();
            BodyDescriptions.StaticColliders(active, sources);
            foreach (Collider source in sources)
            {
                BodyDescriptions.DescribeStatic(source, null, colliders);
            }

            var file = new SceneFile { SceneId = ArenaSceneId };
            foreach (ColliderDesc collider in colliders)
            {
                file.Colliders.Add(SceneFileGeometry.ToFile(collider));
            }

            for (int layer = 0; layer < 32; layer++)
            {
                file.LayerCollisions.Add(uint.MaxValue);
            }

            byte[] bytes = SceneFileFormat.Write(TestObjects.Registry(), file);
            var physics = manager.GetComponent<RapierPhysics>();
            physics.SceneIdOf = scene => scene == active ? ArenaSceneId : 0;
            physics.SceneFileOf = sceneId => sceneId == ArenaSceneId ? bytes : null;
        }

        private static List<string> Names(IEnumerable<Collider> colliders)
        {
            var names = new List<string>();
            foreach (Collider collider in colliders)
            {
                names.Add(collider.name);
            }

            return names;
        }

        private T Enable<T>(T behaviour)
            where T : MonoBehaviour
        {
            enabled.Add(behaviour);
            behaviour.GetType().GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(behaviour, null);
            return behaviour;
        }

        private static void Disable(MonoBehaviour behaviour) =>
            behaviour.GetType().GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(behaviour, null);

        private NetworkObject Prefab(string name, uint prefabId, Action<GameObject> build)
        {
            var gameObject = new GameObject(name);
            created.Add(gameObject);
            var networkObject = gameObject.AddComponent<NetworkObject>();
            var serialized = new SerializedObject(networkObject);
            serialized.FindProperty("prefabId").uintValue = prefabId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            build(gameObject);
            return networkObject;
        }

        private NetworkManager Manager(params NetworkObject[] prefabs)
        {
            var gameObject = new GameObject("NetworkManager");
            created.Add(gameObject);
            var manager = gameObject.AddComponent<NetworkManager>();
            var physics = gameObject.AddComponent<RapierPhysics>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = network;
            serialized.FindProperty("physics").objectReferenceValue = physics;
            serialized.FindProperty("tickRate").intValue = 60;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            manager.FindServerSceneObjects = () => new List<NetworkObject>();
            manager.FindClientSceneObjects = () => new List<NetworkObject>();
            foreach (NetworkObject prefab in prefabs)
            {
                manager.Prefabs.Register(prefab);
            }

            managers.Add(manager);
            return manager;
        }

        private NetworkObject Spawn(NetworkManager server, NetworkObject prefab, Vector3 position)
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
            created.Add(instance.gameObject);
            server.ServerManager.Spawn(instance);
            return instance;
        }

        private void RunFrames(int count)
        {
            for (int frame = 0; frame < count; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                foreach (NetworkManager manager in managers)
                {
                    manager.RunFrameStart(FrameSeconds, now);
                }

                foreach (NetworkManager manager in managers)
                {
                    manager.RunFrameEnd();
                }
            }
        }
    }
}
```

| Test | Kiểm |
|---|---|
| `TheServerSimulatesSpawnedObjectsWithRapierAndMovesTheirTransforms` | Server mô phỏng object spawn bằng Rapier: bóng rơi lên object tĩnh có body; `Transform` theo body; khối lượng theo `Rigidbody`; `Rigidbody` thành kinematic khi chạy, trở lại sau `Release` |
| `TwoDimensionalObjectsFallOntoTwoDimensionalGround` | Object 2D rơi lên nền 2D, giữ Z; `Rigidbody2D` thành `Kinematic`; chỉ có `Body2D` |
| `TheStaticGeometryOfANetworkSceneComesFromItsSceneFile` | Nền tĩnh lấy từ tệp scene của scene đang mở |
| `AClientKeepsTheObjectsItDoesNotPredictAsKinematicProxies` | Client giữ object không dự đoán làm body đại diện kinematic tại tư thế `Transform` |
| `ACollisionOnABallReportsTheGroundOfTheSceneFile` | `NetworkCollision` của bóng được nhận, phát `OnEnter` với collider của nền tĩnh |
| `AStaticTriggerSeesABallPassThroughUntilThePhysicsIsReleased` | `NetworkTrigger` trên vật tĩnh được nhận khi nạp scene; `Enter`, `Exit` khi bóng rơi qua; `Release` gỡ |
| `AClientProxyWithATriggerTouchesTheStaticGeometryOfItsOwnWorld` | Ở client, trigger trên body đại diện (kinematic) chạm tường tĩnh của thế giới của client |
