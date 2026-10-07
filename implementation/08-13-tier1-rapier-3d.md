# 08.13 — Tầng 1: crate Rapier 3D, `RapierWorld`

> Bước con 08.13 (kế hoạch: `implementation/08-prediction-physics.md` của repo `unity`, mục 8b). Listing là mã hiện tại của repo, gồm cả phần 2D (08.14) trong các tệp dùng chung và phần tập chạm (08.15) của `world3d.rs`, `RapierWorld`. Test: `dotnet test` 33/33 trên Linux (.NET 8) và Windows (.NET 9).

| | Việc | Trạng thái |
|---|---|:---:|
| 1 | Crate `fomoxa-rapier`: `rapier3d =0.36.0` + `enhanced-determinism`, C ABI `fr_*` | ✅ |
| 2 | Body có công thức dựng lại; `Load` theo body (Q163 (7) A) | ✅ |
| 3 | Băm trạng thái chuẩn, kiểm tất định giữa nền tảng | ✅ |
| 4 | `RapierWorld : IPhysicsWorld` trong assembly `Fomoxa.Networking.Rapier` | ✅ |
| 5 | `Tools/build-rapier.sh` (Linux x64, Windows x64 qua `cargo.exe`); binary trong `Runtime/Plugins` | ✅ |
| 6 | Test `dotnet`; đo chi phí chụp | ✅ |

**Luật:** P27 (Q163 (1) A, (2) C, (3) A, (7) A; Q164 (1) A; Q165 (1) A, (2) A).

---

## 1. Tổng quan

### Crate

- Một crate cho 3D và 2D (`rapier3d`, `rapier2d` cùng `=0.36.0`, feature `enhanced-determinism`, `serde-serialize`), `crate-type` `cdylib` cho Unity và server console, `rlib` cho kiểm thử Rust. Bản release bật `lto`, `codegen-units = 1`, `panic = "abort"` (lỗi trong Rust dừng tiến trình, không tháo ngăn xếp qua ranh giới C) và `strip = true` (bỏ symbol: thư viện Linux từ 5.9 MB còn 5.2 MB, phần còn lại là mã máy; bản Windows để symbol ở tệp PDB riêng nên kích thước không đổi).
- Thế giới (`World`) là con trỏ hộp (`Box`) trả cho C#; C# giữ `IntPtr` và gọi `fr_world_destroy` khi `Dispose`. Mọi hàm nhận con trỏ `null` thì trả giá trị rỗng (`false`, `0`), không đọc bộ nhớ.
- Body có id `u32` do crate cấp, tăng dần từ 1, không dùng lại (P27: `BodyHandle` là định danh ổn định do crate cấp, ánh xạ sang handle Rapier). Bảng `BTreeMap<u32, BodyEntry>` giữ handle Rapier, công thức dựng (`BodyRecipe`: loại, khối lượng, danh sách collider) và cờ chạy lại được. `user_data` của body là id; của collider là id body ở 32 bit thấp, chỉ số collider trong body ở 32 bit kế (collider tĩnh có id body 0, chỉ số theo thứ tự thêm).
- Collider: `FrCollider` `repr(C)` mang shape, tư thế cục bộ, kích thước, con trỏ đỉnh và chỉ số tam giác, ma sát, độ nảy, hai cách kết hợp, layer, cờ trigger. Mã shape và mã cách kết hợp trùng thứ tự `ShapeKind`, `CombineRule` của Core (C# truyền thẳng giá trị enum). `CombineRule.Mean` là `GeometricMean` của Rapier, không cần hook. Trigger là sensor. Layer thành `InteractionGroups` (thành viên `1 << layer`, lọc theo mặt nạ của layer, chế độ `And`). Mọi collider bật `ActiveCollisionTypes` trừ `FIXED_FIXED` (08.15). Bao lồi suy biến hoặc lưới hỏng thì việc dựng trả `None`, hàm FFI trả lỗi.
- Khối lượng: body động có `mass > 0` thì tính khối lượng theo mật độ 1, rồi đặt lại mật độ mọi collider bằng `mass / khối lượng tính được`, để tổng đúng `mass` mà tâm và quán tính vẫn theo hình.
- `Step(seconds)` đặt `dt` rồi gọi `PhysicsWorld::step`, sau đó xóa lực của mọi body: lực kéo dài một bước (P27).
- Truy vấn (`raycast`, `overlap`) duyệt mọi collider ở tư thế hiện tại của body cha, không dùng BVH của pha rộng: BVH chỉ cập nhật khi bước, nên body vừa tạo hoặc vừa đặt tư thế sẽ bị bỏ sót. Tia trúng collider tĩnh trả body 0 (handle không hợp lệ); `overlap` chỉ trả body, theo thứ tự id.

### Chụp và khôi phục (Q163 (7) A)

- `fr_world_snapshot` mã hóa `bincode` của `PhysicsWorld` (mọi tập, gồm pha hẹp và cặp tiếp xúc) cùng bảng `(id, chỉ số, thế hệ)` của body và số định dạng; `fr_world_snapshot_copy` chép ra mảng của C#. Bản chụp của bản build khác (định dạng khác, giải mã lỗi) bị từ chối.
- `fr_world_load`: đọc trạng thái và loại hiện tại của mọi body; thay cả thế giới bằng bản chụp; xóa body có trong bản chụp nhưng đã xóa sau lúc chụp; body tạo sau lúc chụp được tạo lại từ công thức với trạng thái và loại đọc trước khi khôi phục, theo thứ tự id; body không chạy lại được (body đại diện, `fr_body_set_rewindable`) giữ trạng thái và loại hiện tại. Collider tĩnh nằm trong bản chụp.
- Body tạo lại mất tiếp xúc, nên tick đầu sau `Load` của body đó chỉ gần đúng (P27).

### Băm trạng thái

`fr_world_hash`: FNV-1a 64 bit trên id, loại, vị trí, xoay, vận tốc, vận tốc góc của mọi body theo thứ tự id; mỗi `f32` được chuẩn hóa (`-0` thành `0`, mọi NaN thành một NaN) rồi băm theo byte little-endian. Băm dùng cho test tất định và phép kiểm hai phía, không dùng lúc chạy game.

### C#

- `RapierNative`: `DllImport("fomoxa_rapier")` không đuôi; Unity chọn tệp theo `.meta` của nền tảng, server console tìm tệp cạnh tệp chạy. `bool` trả về được đánh dấu `MarshalAs(UnmanagedType.U1)`.
- `RapierColliders`: đổi `ColliderDesc` sang `FrCollider`, ghim mảng đỉnh và tam giác bằng `GCHandle` trong suốt lời gọi.
- `RapierWorld : IPhysicsWorld, IDisposable`: tạo body ném `ArgumentException` khi crate từ chối; đọc, ghi body không có trong thế giới ném `ArgumentException`; `Save`, `Load` với `PhysicsSnapshot` riêng của backend (mảng byte tái dùng); thêm `StateHash`, `SetLayerCollisions` (32 mặt nạ), `AddStatic`, `SetKind`, `IsDisposed`.
- `IRapierBodies` (`internal`): phần chung của thế giới 3D và 2D mà `RapierScenes` dùng cho body đại diện.
- `RapierPackage.CheckCore`: so `NetworkRuntime.Version` với phiên bản Core mà package được build cùng (P12); `RapierScenes` gọi khi tạo.

### Đo chi phí chụp

Thế giới có lưới tam giác 20 000 tam giác và 100 cầu sau 60 bước, 50 lần mỗi thao tác:

| Nền tảng | `Save` | `Load` | `Step` |
|---|---:|---:|---:|
| Linux, .NET 8 | 1.09 ms | 2.04 ms | 0.22 ms |
| Windows, .NET 9 | 2.7 ms | 4.9 ms | 0.64 ms |

Với lịch sử 64 tick, một lần chạy lại dài cỡ một RTT gồm một `Load` và vài `Step`.

### Tất định giữa nền tảng

`APileReachesTheSameStateOnEveryPlatform` thả 20 hộp xoay lên sàn, chạy 300 bước và so với băm cố định `0xF1860E86303F3AED`; cùng băm trên Linux (glibc, .NET 8) và Windows (MSVC, .NET 9). 08.16 kiểm thêm giữa server console và client Unity (Mono).

### Khác design

- P27 ghi crate "lấy `afjk/rapier-unity` làm mẫu (bảng thế giới theo `world_id`, handle `index` + `generation`, …)". Crate tự viết, không chép mã của mẫu đó, nên không có ghi chú giấy phép MIT: thế giới là con trỏ hộp thay cho bảng `world_id`, body là id `u32` không dùng lại thay cho `index` + `generation` (đúng ý P27 "định danh ổn định do crate cấp"). Snapshot có số định dạng, băm trạng thái chuẩn như mẫu.

---

## 2. Mã

`native/fomoxa-rapier/Cargo.toml`:

```toml
[package]
name = "fomoxa-rapier"
version = "0.1.0"
edition = "2021"
license = "Apache-2.0"
publish = false

[lib]
name = "fomoxa_rapier"
crate-type = ["cdylib", "rlib"]

[dependencies]
rapier3d = { version = "=0.36.0", features = ["enhanced-determinism", "serde-serialize"] }
rapier2d = { version = "=0.36.0", features = ["enhanced-determinism", "serde-serialize"] }
bincode = { version = "1.3", default-features = false }
serde = { version = "1", features = ["derive"] }

[profile.release]
opt-level = 3
lto = true
codegen-units = 1
panic = "abort"
strip = true
```

`native/fomoxa-rapier/src/lib.rs`:

```rust
mod contact;
mod hash;
mod world2d;
mod world3d;

pub use contact::FrColliderRef;
pub use world2d::*;
pub use world3d::*;
```

`native/fomoxa-rapier/src/hash.rs`:

```rust
const FNV_OFFSET: u64 = 0xcbf2_9ce4_8422_2325;
const FNV_PRIME: u64 = 0x0000_0100_0000_01b3;

pub struct StableHasher {
    value: u64,
}

impl StableHasher {
    pub fn new() -> Self {
        Self { value: FNV_OFFSET }
    }

    pub fn finish(&self) -> u64 {
        self.value
    }

    pub fn write_u8(&mut self, value: u8) {
        self.value ^= u64::from(value);
        self.value = self.value.wrapping_mul(FNV_PRIME);
    }

    pub fn write_u32(&mut self, value: u32) {
        for byte in value.to_le_bytes() {
            self.write_u8(byte);
        }
    }

    pub fn write_f32(&mut self, value: f32) {
        self.write_u32(canonical_bits(value));
    }
}

fn canonical_bits(value: f32) -> u32 {
    if value == 0.0 {
        0.0f32.to_bits()
    } else if value.is_nan() {
        f32::NAN.to_bits()
    } else {
        value.to_bits()
    }
}
```

`native/fomoxa-rapier/src/contact.rs` (08.15):

```rust
use std::slice;

#[repr(C)]
#[derive(Clone, Copy, Default, PartialEq, Eq, PartialOrd, Ord)]
pub struct FrColliderRef {
    pub body: u32,
    pub index: u32,
}

pub fn collider_tag(body: u32, index: u32) -> u128 {
    u128::from(body) | (u128::from(index) << 32)
}

pub fn collider_ref(tag: u128) -> FrColliderRef {
    FrColliderRef { body: tag as u32, index: (tag >> 32) as u32 }
}

pub unsafe fn write_refs(found: &[FrColliderRef], out: *mut FrColliderRef, capacity: u32) -> u32 {
    let count = found.len().min(capacity as usize);
    if count > 0 && !out.is_null() {
        slice::from_raw_parts_mut(out, count).copy_from_slice(&found[..count]);
    }

    found.len() as u32
}
```

`native/fomoxa-rapier/src/world3d.rs`:

```rust
use std::collections::{BTreeMap, BTreeSet};
use std::slice;

use rapier3d::prelude::*;
use serde::{Deserialize, Serialize};

use crate::contact::{collider_ref, collider_tag, write_refs, FrColliderRef};
use crate::hash::StableHasher;

const SNAPSHOT_FORMAT: u32 = 1;
const ALL_LAYERS: u32 = u32::MAX;

const KIND_DYNAMIC: u32 = 0;
const KIND_KINEMATIC: u32 = 1;
const KIND_STATIC: u32 = 2;

const SHAPE_BOX: u32 = 0;
const SHAPE_SPHERE: u32 = 1;
const SHAPE_CAPSULE: u32 = 2;
const SHAPE_CONVEX_HULL: u32 = 3;
const SHAPE_TRIANGLE_MESH: u32 = 4;

#[repr(C)]
#[derive(Clone, Copy)]
pub struct FrCollider {
    pub shape: u32,
    pub position: [f32; 3],
    pub rotation: [f32; 4],
    pub half_extents: [f32; 3],
    pub radius: f32,
    pub half_height: f32,
    pub points: *const f32,
    pub point_count: u32,
    pub triangles: *const u32,
    pub triangle_index_count: u32,
    pub friction: f32,
    pub restitution: f32,
    pub friction_combine: u32,
    pub restitution_combine: u32,
    pub layer: u32,
    pub is_trigger: u32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct FrBodyState {
    pub position: [f32; 3],
    pub rotation: [f32; 4],
    pub velocity: [f32; 3],
    pub angular_velocity: [f32; 3],
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct FrRayHit {
    pub body: u32,
    pub point: [f32; 3],
    pub normal: [f32; 3],
    pub distance: f32,
}

#[derive(Clone)]
enum ShapeRecipe {
    Box([f32; 3]),
    Sphere(f32),
    Capsule { radius: f32, half_height: f32 },
    ConvexHull(Vec<Vector>),
    TriangleMesh(Vec<Vector>, Vec<[u32; 3]>),
}

#[derive(Clone)]
struct ColliderRecipe {
    shape: ShapeRecipe,
    position: [f32; 3],
    rotation: [f32; 4],
    friction: f32,
    restitution: f32,
    friction_combine: u32,
    restitution_combine: u32,
    layer: u32,
    is_trigger: bool,
}

#[derive(Clone)]
struct BodyRecipe {
    kind: u32,
    mass: f32,
    colliders: Vec<ColliderRecipe>,
}

struct BodyEntry {
    handle: RigidBodyHandle,
    recipe: BodyRecipe,
    rewindable: bool,
}

#[derive(Serialize)]
struct SnapshotRef<'a> {
    format: u32,
    world: &'a PhysicsWorld,
    handles: Vec<(u32, u32, u32)>,
}

#[derive(Deserialize)]
struct Snapshot {
    format: u32,
    world: PhysicsWorld,
    handles: Vec<(u32, u32, u32)>,
}

pub struct World {
    physics: PhysicsWorld,
    bodies: BTreeMap<u32, BodyEntry>,
    next_id: u32,
    layers: [u32; 32],
    statics: Vec<ColliderHandle>,
    scratch: Vec<u8>,
}

impl World {
    fn new(gravity: Vector) -> Self {
        let mut physics = PhysicsWorld::new();
        physics.gravity = gravity;
        Self {
            physics,
            bodies: BTreeMap::new(),
            next_id: 1,
            layers: [ALL_LAYERS; 32],
            statics: Vec::new(),
            scratch: Vec::new(),
        }
    }

    fn step(&mut self, seconds: f32) {
        self.physics.integration_parameters.dt = seconds;
        self.physics.step();
        for entry in self.bodies.values() {
            if let Some(body) = self.physics.bodies.get_mut(entry.handle) {
                body.reset_forces(false);
            }
        }
    }

    fn add_static(&mut self, colliders: Vec<ColliderRecipe>) -> bool {
        let mut built = Vec::with_capacity(colliders.len());
        for (offset, recipe) in colliders.iter().enumerate() {
            let tag = collider_tag(0, (self.statics.len() + offset) as u32);
            match build_collider(recipe, &self.layers, 1.0, tag) {
                Some(collider) => built.push(collider),
                None => return false,
            }
        }

        for collider in built {
            let handle = self.physics.insert_collider(collider, None);
            self.statics.push(handle);
        }

        true
    }

    fn create_body(&mut self, recipe: BodyRecipe, position: [f32; 3], rotation: [f32; 4]) -> u32 {
        let id = self.next_id;
        match self.insert_body(id, &recipe, position, rotation) {
            Some(handle) => {
                self.next_id += 1;
                self.bodies.insert(id, BodyEntry { handle, recipe, rewindable: true });
                id
            }
            None => 0,
        }
    }

    fn insert_body(&mut self, id: u32, recipe: &BodyRecipe, position: [f32; 3], rotation: [f32; 4]) -> Option<RigidBodyHandle> {
        let mut built = Vec::with_capacity(recipe.colliders.len());
        for (index, collider) in recipe.colliders.iter().enumerate() {
            built.push(build_collider(collider, &self.layers, 1.0, collider_tag(id, index as u32))?);
        }

        let builder = match recipe.kind {
            KIND_DYNAMIC => RigidBodyBuilder::dynamic(),
            KIND_KINEMATIC => RigidBodyBuilder::kinematic_position_based(),
            _ => RigidBodyBuilder::fixed(),
        };
        let handle = self.physics.insert_body(builder.pose(pose(position, rotation)).user_data(u128::from(id)).build());
        let mut collider_handles = Vec::with_capacity(built.len());
        for collider in built {
            collider_handles.push(self.physics.insert_collider(collider, Some(handle)));
        }

        if recipe.kind == KIND_DYNAMIC && recipe.mass > 0.0 {
            let body = self.physics.bodies.get_mut(handle)?;
            body.recompute_mass_properties_from_colliders(&self.physics.colliders);
            let computed = body.mass();
            if computed > 0.0 {
                let density = recipe.mass / computed;
                for collider_handle in &collider_handles {
                    if let Some(collider) = self.physics.colliders.get_mut(*collider_handle) {
                        collider.set_density(density);
                    }
                }

                let body = self.physics.bodies.get_mut(handle)?;
                body.recompute_mass_properties_from_colliders(&self.physics.colliders);
            }
        }

        Some(handle)
    }

    fn remove_body(&mut self, id: u32) -> bool {
        match self.bodies.remove(&id) {
            Some(entry) => {
                self.physics.remove_body(entry.handle);
                true
            }
            None => false,
        }
    }

    fn body(&self, id: u32) -> Option<&RigidBody> {
        let entry = self.bodies.get(&id)?;
        self.physics.bodies.get(entry.handle)
    }

    fn body_mut(&mut self, id: u32) -> Option<&mut RigidBody> {
        let entry = self.bodies.get(&id)?;
        self.physics.bodies.get_mut(entry.handle)
    }

    fn state(&self, id: u32) -> Option<FrBodyState> {
        self.body(id).map(state_of)
    }

    fn set_state(&mut self, id: u32, state: &FrBodyState) -> bool {
        match self.body_mut(id) {
            Some(body) => {
                apply_state(body, state);
                true
            }
            None => false,
        }
    }

    fn set_kind(&mut self, id: u32, kind: u32) -> bool {
        match self.body_mut(id) {
            Some(body) => {
                body.set_body_type(body_type(kind), true);
                true
            }
            None => false,
        }
    }

    fn kind(&self, id: u32) -> Option<u32> {
        self.body(id).map(|body| kind_of(body.body_type()))
    }

    fn snapshot(&mut self) -> usize {
        let handles = self
            .bodies
            .iter()
            .map(|(id, entry)| {
                let (index, generation) = entry.handle.into_raw_parts();
                (*id, index, generation)
            })
            .collect();
        let snapshot = SnapshotRef { format: SNAPSHOT_FORMAT, world: &self.physics, handles };
        self.scratch = bincode::serialize(&snapshot).unwrap_or_default();
        self.scratch.len()
    }

    fn load(&mut self, bytes: &[u8]) -> bool {
        let snapshot: Snapshot = match bincode::deserialize(bytes) {
            Ok(snapshot) => snapshot,
            Err(_) => return false,
        };
        if snapshot.format != SNAPSHOT_FORMAT {
            return false;
        }

        let current: BTreeMap<u32, (FrBodyState, u32)> = self
            .bodies
            .keys()
            .filter_map(|id| Some((*id, (self.state(*id)?, self.kind(*id)?))))
            .collect();
        let saved: BTreeMap<u32, RigidBodyHandle> = snapshot
            .handles
            .iter()
            .map(|(id, index, generation)| (*id, RigidBodyHandle::from_raw_parts(*index, *generation)))
            .collect();
        self.physics = snapshot.world;
        let entries = std::mem::take(&mut self.bodies);
        let kept: BTreeSet<u32> = entries.keys().copied().collect();
        for (id, handle) in &saved {
            if !kept.contains(id) {
                self.physics.remove_body(*handle);
            }
        }

        for (id, entry) in entries {
            let (state, kind) = current.get(&id).copied().unwrap_or_default();
            match saved.get(&id) {
                Some(handle) => {
                    self.bodies.insert(id, BodyEntry { handle: *handle, recipe: entry.recipe, rewindable: entry.rewindable });
                    if !entry.rewindable {
                        if let Some(body) = self.body_mut(id) {
                            body.set_body_type(body_type(kind), true);
                            apply_state(body, &state);
                        }
                    }
                }
                None => {
                    if let Some(handle) = self.insert_body(id, &entry.recipe, state.position, state.rotation) {
                        self.bodies.insert(id, BodyEntry { handle, recipe: entry.recipe, rewindable: entry.rewindable });
                        if let Some(body) = self.body_mut(id) {
                            body.set_body_type(body_type(kind), true);
                            apply_state(body, &state);
                        }
                    }
                }
            }
        }

        true
    }

    fn hash(&self) -> u64 {
        let mut hasher = StableHasher::new();
        for (id, entry) in &self.bodies {
            if let Some(body) = self.physics.bodies.get(entry.handle) {
                hasher.write_u32(*id);
                hasher.write_u32(kind_of(body.body_type()));
                let state = state_of(body);
                for value in state.position.iter().chain(&state.rotation).chain(&state.velocity).chain(&state.angular_velocity) {
                    hasher.write_f32(*value);
                }
            }
        }

        hasher.finish()
    }

    fn raycast(&self, origin: Vector, direction: Vector, max_distance: f32) -> Option<FrRayHit> {
        let length = direction.length();
        if length == 0.0 {
            return None;
        }

        let ray = Ray::new(origin, direction / length);
        let mut best: Option<(ColliderHandle, RayIntersection)> = None;
        for (handle, collider) in self.physics.colliders.iter() {
            let pose = self.pose_of(collider);
            if let Some(hit) = collider.shape().cast_ray_and_get_normal(&pose, &ray, max_distance, true) {
                if best.map_or(true, |(_, found)| hit.time_of_impact < found.time_of_impact) {
                    best = Some((handle, hit));
                }
            }
        }

        let (collider, hit) = best?;
        let point = ray.point_at(hit.time_of_impact);
        Some(FrRayHit {
            body: self.owner_of(collider),
            point: [point.x, point.y, point.z],
            normal: [hit.normal.x, hit.normal.y, hit.normal.z],
            distance: hit.time_of_impact,
        })
    }

    fn overlap(&self, center: Vector, radius: f32) -> Vec<u32> {
        let ball = Ball::new(radius);
        let ball_pose = Pose::from_translation(center);
        let mut found = BTreeSet::new();
        for (handle, collider) in self.physics.colliders.iter() {
            let owner = self.owner_of(handle);
            if owner == 0 || found.contains(&owner) {
                continue;
            }

            let pose = self.pose_of(collider);
            if rapier3d::parry::query::intersection_test(&ball_pose, &ball, &pose, collider.shape()).is_ok_and(|hit| hit.intersecting) {
                found.insert(owner);
            }
        }

        found.into_iter().collect()
    }

    fn touching(&self, body: u32, index: u32) -> Vec<FrColliderRef> {
        let handle = match self.collider_of(body, index) {
            Some(handle) => handle,
            None => return Vec::new(),
        };
        let narrow = &self.physics.narrow_phase;
        let mut found = BTreeSet::new();
        if self.physics.colliders.get(handle).is_some_and(|collider| collider.is_sensor()) {
            for (first, second, intersecting) in narrow.intersection_pairs_with(handle) {
                if intersecting {
                    self.insert_other(handle, first, second, &mut found);
                }
            }
        } else {
            for pair in narrow.contact_pairs_with(handle) {
                if pair.has_any_active_contact() {
                    self.insert_other(handle, pair.collider1, pair.collider2, &mut found);
                }
            }
        }

        found.into_iter().collect()
    }

    fn collider_of(&self, body: u32, index: u32) -> Option<ColliderHandle> {
        if body == 0 {
            return self.statics.get(index as usize).copied();
        }

        let tag = collider_tag(body, index);
        self.body(body)?
            .colliders()
            .iter()
            .copied()
            .find(|handle| self.physics.colliders.get(*handle).is_some_and(|collider| collider.user_data == tag))
    }

    fn insert_other(&self, own: ColliderHandle, first: ColliderHandle, second: ColliderHandle, found: &mut BTreeSet<FrColliderRef>) {
        let other = if first == own { second } else { first };
        if let Some(collider) = self.physics.colliders.get(other) {
            found.insert(collider_ref(collider.user_data));
        }
    }

    fn pose_of(&self, collider: &Collider) -> Pose {
        match (collider.parent().and_then(|parent| self.physics.bodies.get(parent)), collider.position_wrt_parent()) {
            (Some(body), Some(local)) => *body.position() * *local,
            _ => *collider.position(),
        }
    }

    fn owner_of(&self, collider: ColliderHandle) -> u32 {
        self.physics
            .colliders
            .get(collider)
            .and_then(|collider| collider.parent())
            .and_then(|parent| self.physics.bodies.get(parent))
            .map(|body| body.user_data as u32)
            .unwrap_or(0)
    }
}

fn pose(position: [f32; 3], rotation: [f32; 4]) -> Pose {
    Pose::from_parts(
        Vector::new(position[0], position[1], position[2]),
        Rotation::from_xyzw(rotation[0], rotation[1], rotation[2], rotation[3]),
    )
}

fn body_type(kind: u32) -> RigidBodyType {
    match kind {
        KIND_DYNAMIC => RigidBodyType::Dynamic,
        KIND_KINEMATIC => RigidBodyType::KinematicPositionBased,
        _ => RigidBodyType::Fixed,
    }
}

fn kind_of(body_type: RigidBodyType) -> u32 {
    match body_type {
        RigidBodyType::Dynamic => KIND_DYNAMIC,
        RigidBodyType::Fixed => KIND_STATIC,
        _ => KIND_KINEMATIC,
    }
}

fn combine_rule(rule: u32) -> CoefficientCombineRule {
    match rule {
        1 => CoefficientCombineRule::Min,
        2 => CoefficientCombineRule::Multiply,
        3 => CoefficientCombineRule::Max,
        4 => CoefficientCombineRule::GeometricMean,
        _ => CoefficientCombineRule::Average,
    }
}

fn state_of(body: &RigidBody) -> FrBodyState {
    let translation = body.translation();
    let rotation = body.rotation();
    let velocity = body.linvel();
    let angular = body.angvel();
    FrBodyState {
        position: [translation.x, translation.y, translation.z],
        rotation: [rotation.x, rotation.y, rotation.z, rotation.w],
        velocity: [velocity.x, velocity.y, velocity.z],
        angular_velocity: [angular.x, angular.y, angular.z],
    }
}

fn apply_state(body: &mut RigidBody, state: &FrBodyState) {
    let translation = Vector::new(state.position[0], state.position[1], state.position[2]);
    let rotation = Rotation::from_xyzw(state.rotation[0], state.rotation[1], state.rotation[2], state.rotation[3]);
    body.set_translation(translation, true);
    body.set_rotation(rotation, true);
    if body.is_kinematic() {
        body.set_next_kinematic_translation(translation);
        body.set_next_kinematic_rotation(rotation);
    }

    body.set_linvel(Vector::new(state.velocity[0], state.velocity[1], state.velocity[2]), true);
    body.set_angvel(Vector::new(state.angular_velocity[0], state.angular_velocity[1], state.angular_velocity[2]), true);
}

fn build_collider(recipe: &ColliderRecipe, layers: &[u32; 32], density: f32, tag: u128) -> Option<Collider> {
    let builder = match &recipe.shape {
        ShapeRecipe::Box(half) => ColliderBuilder::cuboid(half[0], half[1], half[2]),
        ShapeRecipe::Sphere(radius) => ColliderBuilder::ball(*radius),
        ShapeRecipe::Capsule { radius, half_height } => ColliderBuilder::capsule_y(*half_height, *radius),
        ShapeRecipe::ConvexHull(points) => ColliderBuilder::convex_hull(points)?,
        ShapeRecipe::TriangleMesh(vertices, triangles) => ColliderBuilder::trimesh(vertices.clone(), triangles.clone()).ok()?,
    };
    let layer = recipe.layer.min(31) as usize;
    let groups = InteractionGroups::new(
        Group::from_bits_truncate(1u32 << layer),
        Group::from_bits_truncate(layers[layer]),
        InteractionTestMode::And,
    );
    Some(
        builder
            .position(pose(recipe.position, recipe.rotation))
            .friction(recipe.friction)
            .restitution(recipe.restitution)
            .friction_combine_rule(combine_rule(recipe.friction_combine))
            .restitution_combine_rule(combine_rule(recipe.restitution_combine))
            .collision_groups(groups)
            .sensor(recipe.is_trigger)
            .active_collision_types(ActiveCollisionTypes::all() - ActiveCollisionTypes::FIXED_FIXED)
            .density(density)
            .user_data(tag)
            .build(),
    )
}

unsafe fn read_colliders(colliders: *const FrCollider, count: u32) -> Option<Vec<ColliderRecipe>> {
    if count == 0 {
        return Some(Vec::new());
    }

    if colliders.is_null() {
        return None;
    }

    let mut recipes = Vec::with_capacity(count as usize);
    for collider in slice::from_raw_parts(colliders, count as usize) {
        let shape = match collider.shape {
            SHAPE_BOX => ShapeRecipe::Box(collider.half_extents),
            SHAPE_SPHERE => ShapeRecipe::Sphere(collider.radius),
            SHAPE_CAPSULE => ShapeRecipe::Capsule { radius: collider.radius, half_height: collider.half_height },
            SHAPE_CONVEX_HULL => ShapeRecipe::ConvexHull(read_points(collider.points, collider.point_count)?),
            SHAPE_TRIANGLE_MESH => ShapeRecipe::TriangleMesh(
                read_points(collider.points, collider.point_count)?,
                read_triangles(collider.triangles, collider.triangle_index_count)?,
            ),
            _ => return None,
        };
        recipes.push(ColliderRecipe {
            shape,
            position: collider.position,
            rotation: collider.rotation,
            friction: collider.friction,
            restitution: collider.restitution,
            friction_combine: collider.friction_combine,
            restitution_combine: collider.restitution_combine,
            layer: collider.layer,
            is_trigger: collider.is_trigger != 0,
        });
    }

    Some(recipes)
}

unsafe fn read_points(points: *const f32, count: u32) -> Option<Vec<Vector>> {
    if count == 0 || points.is_null() {
        return None;
    }

    let values = slice::from_raw_parts(points, count as usize * 3);
    Some(values.chunks_exact(3).map(|point| Vector::new(point[0], point[1], point[2])).collect())
}

unsafe fn read_triangles(indices: *const u32, count: u32) -> Option<Vec<[u32; 3]>> {
    if count == 0 || count % 3 != 0 || indices.is_null() {
        return None;
    }

    let values = slice::from_raw_parts(indices, count as usize);
    Some(values.chunks_exact(3).map(|triangle| [triangle[0], triangle[1], triangle[2]]).collect())
}

unsafe fn world<'a>(world: *mut World) -> Option<&'a mut World> {
    world.as_mut()
}

#[no_mangle]
pub extern "C" fn fr_world_create(gravity_x: f32, gravity_y: f32, gravity_z: f32) -> *mut World {
    Box::into_raw(Box::new(World::new(Vector::new(gravity_x, gravity_y, gravity_z))))
}

#[no_mangle]
pub unsafe extern "C" fn fr_world_destroy(world: *mut World) {
    if !world.is_null() {
        drop(Box::from_raw(world));
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_world_set_layers(world_ptr: *mut World, masks: *const u32) -> bool {
    match (world(world_ptr), masks.is_null()) {
        (Some(world), false) => {
            world.layers.copy_from_slice(slice::from_raw_parts(masks, 32));
            true
        }
        _ => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_world_step(world_ptr: *mut World, seconds: f32) {
    if let Some(world) = world(world_ptr) {
        world.step(seconds);
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_world_add_static(world_ptr: *mut World, colliders: *const FrCollider, count: u32) -> bool {
    match (world(world_ptr), read_colliders(colliders, count)) {
        (Some(world), Some(recipes)) => world.add_static(recipes),
        _ => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_body_create(
    world_ptr: *mut World,
    kind: u32,
    position: *const f32,
    rotation: *const f32,
    mass: f32,
    colliders: *const FrCollider,
    count: u32,
) -> u32 {
    let world = match world(world_ptr) {
        Some(world) => world,
        None => return 0,
    };
    if position.is_null() || rotation.is_null() || count == 0 {
        return 0;
    }

    let recipes = match read_colliders(colliders, count) {
        Some(recipes) => recipes,
        None => return 0,
    };
    let position = slice::from_raw_parts(position, 3);
    let rotation = slice::from_raw_parts(rotation, 4);
    world.create_body(
        BodyRecipe { kind: kind.min(KIND_STATIC), mass, colliders: recipes },
        [position[0], position[1], position[2]],
        [rotation[0], rotation[1], rotation[2], rotation[3]],
    )
}

#[no_mangle]
pub unsafe extern "C" fn fr_body_remove(world_ptr: *mut World, body: u32) -> bool {
    world(world_ptr).map_or(false, |world| world.remove_body(body))
}

#[no_mangle]
pub unsafe extern "C" fn fr_body_contains(world_ptr: *mut World, body: u32) -> bool {
    world(world_ptr).map_or(false, |world| world.body(body).is_some())
}

#[no_mangle]
pub unsafe extern "C" fn fr_body_get(world_ptr: *mut World, body: u32, state: *mut FrBodyState) -> bool {
    match (world(world_ptr).and_then(|world| world.state(body)), state.as_mut()) {
        (Some(found), Some(out)) => {
            *out = found;
            true
        }
        _ => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_body_set(world_ptr: *mut World, body: u32, state: *const FrBodyState) -> bool {
    match (world(world_ptr), state.as_ref()) {
        (Some(world), Some(state)) => world.set_state(body, state),
        _ => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_body_kind(world_ptr: *mut World, body: u32) -> i32 {
    world(world_ptr).and_then(|world| world.kind(body)).map_or(-1, |kind| kind as i32)
}

#[no_mangle]
pub unsafe extern "C" fn fr_body_set_kind(world_ptr: *mut World, body: u32, kind: u32) -> bool {
    world(world_ptr).map_or(false, |world| world.set_kind(body, kind))
}

#[no_mangle]
pub unsafe extern "C" fn fr_body_mass(world_ptr: *mut World, body: u32) -> f32 {
    world(world_ptr).and_then(|world| world.body(body)).map_or(0.0, |body| body.mass())
}

#[no_mangle]
pub unsafe extern "C" fn fr_body_set_rewindable(world_ptr: *mut World, body: u32, rewindable: bool) -> bool {
    match world(world_ptr).and_then(|world| world.bodies.get_mut(&body)) {
        Some(entry) => {
            entry.rewindable = rewindable;
            true
        }
        None => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_body_add_force(world_ptr: *mut World, body: u32, x: f32, y: f32, z: f32) -> bool {
    match world(world_ptr).and_then(|world| world.body_mut(body)) {
        Some(body) => {
            body.add_force(Vector::new(x, y, z), true);
            true
        }
        None => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_body_add_impulse(world_ptr: *mut World, body: u32, x: f32, y: f32, z: f32) -> bool {
    match world(world_ptr).and_then(|world| world.body_mut(body)) {
        Some(body) => {
            body.apply_impulse(Vector::new(x, y, z), true);
            true
        }
        None => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_world_raycast(
    world_ptr: *mut World,
    origin: *const f32,
    direction: *const f32,
    max_distance: f32,
    hit: *mut FrRayHit,
) -> bool {
    if origin.is_null() || direction.is_null() {
        return false;
    }

    let origin = slice::from_raw_parts(origin, 3);
    let direction = slice::from_raw_parts(direction, 3);
    let found = world(world_ptr).and_then(|world| {
        world.raycast(
            Vector::new(origin[0], origin[1], origin[2]),
            Vector::new(direction[0], direction[1], direction[2]),
            max_distance,
        )
    });
    match (found, hit.as_mut()) {
        (Some(found), Some(out)) => {
            *out = found;
            true
        }
        _ => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_world_overlap(world_ptr: *mut World, x: f32, y: f32, z: f32, radius: f32, bodies: *mut u32, capacity: u32) -> u32 {
    let found = match world(world_ptr) {
        Some(world) => world.overlap(Vector::new(x, y, z), radius),
        None => return 0,
    };
    let count = found.len().min(capacity as usize);
    if count > 0 && !bodies.is_null() {
        slice::from_raw_parts_mut(bodies, count).copy_from_slice(&found[..count]);
    }

    count as u32
}

#[no_mangle]
pub unsafe extern "C" fn fr_collider_touching(world_ptr: *mut World, body: u32, index: u32, out: *mut FrColliderRef, capacity: u32) -> u32 {
    match world(world_ptr) {
        Some(world) => write_refs(&world.touching(body, index), out, capacity),
        None => 0,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_world_snapshot(world_ptr: *mut World) -> u32 {
    world(world_ptr).map_or(0, |world| world.snapshot() as u32)
}

#[no_mangle]
pub unsafe extern "C" fn fr_world_snapshot_copy(world_ptr: *mut World, bytes: *mut u8, length: u32) -> bool {
    match world(world_ptr) {
        Some(world) if !bytes.is_null() && length as usize == world.scratch.len() => {
            slice::from_raw_parts_mut(bytes, length as usize).copy_from_slice(&world.scratch);
            true
        }
        _ => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_world_load(world_ptr: *mut World, bytes: *const u8, length: u32) -> bool {
    match world(world_ptr) {
        Some(world) if !bytes.is_null() && length > 0 => world.load(slice::from_raw_parts(bytes, length as usize)),
        _ => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_world_hash(world_ptr: *mut World) -> u64 {
    world(world_ptr).map_or(0, |world| world.hash())
}
```

`com.fomoxa.networking.rapier/Runtime/Rapier/Fomoxa.Networking.Rapier.asmdef`:

```json
{
    "name": "Fomoxa.Networking.Rapier",
    "rootNamespace": "Fomoxa.Networking.Rapier",
    "references": [
        "Fomoxa.Networking"
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
    "noEngineReferences": true
}
```

`com.fomoxa.networking.rapier/Runtime/Rapier/RapierPackage.cs`:

```csharp
using System;

namespace Fomoxa.Networking.Rapier
{
    public static class RapierPackage
    {
        public const string CoreVersion = "0.1.0";

        public static void CheckCore()
        {
            if (NetworkRuntime.Version != CoreVersion)
            {
                throw new InvalidOperationException($"com.fomoxa.networking.rapier needs com.fomoxa.networking {CoreVersion}; the project has {NetworkRuntime.Version}");
            }
        }
    }
}
```

`com.fomoxa.networking.rapier/Runtime/Rapier/RapierNative.cs` (3D và 2D):

```csharp
using System;
using System.Runtime.InteropServices;

namespace Fomoxa.Networking.Rapier
{
    internal static class RapierNative
    {
        private const string Library = "fomoxa_rapier";

        [StructLayout(LayoutKind.Sequential)]
        internal struct Collider
        {
            public uint Shape;
            public float PositionX;
            public float PositionY;
            public float PositionZ;
            public float RotationX;
            public float RotationY;
            public float RotationZ;
            public float RotationW;
            public float HalfExtentsX;
            public float HalfExtentsY;
            public float HalfExtentsZ;
            public float Radius;
            public float HalfHeight;
            public IntPtr Points;
            public uint PointCount;
            public IntPtr Triangles;
            public uint TriangleIndexCount;
            public float Friction;
            public float Restitution;
            public uint FrictionCombine;
            public uint RestitutionCombine;
            public uint Layer;
            public uint IsTrigger;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BodyState
        {
            public float PositionX;
            public float PositionY;
            public float PositionZ;
            public float RotationX;
            public float RotationY;
            public float RotationZ;
            public float RotationW;
            public float VelocityX;
            public float VelocityY;
            public float VelocityZ;
            public float AngularVelocityX;
            public float AngularVelocityY;
            public float AngularVelocityZ;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ColliderRef
        {
            public uint Body;
            public uint Index;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RayHit
        {
            public uint Body;
            public float PointX;
            public float PointY;
            public float PointZ;
            public float NormalX;
            public float NormalY;
            public float NormalZ;
            public float Distance;
        }

        [DllImport(Library)]
        internal static extern IntPtr fr_world_create(float gravityX, float gravityY, float gravityZ);

        [DllImport(Library)]
        internal static extern void fr_world_destroy(IntPtr world);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_world_set_layers(IntPtr world, uint[] masks);

        [DllImport(Library)]
        internal static extern void fr_world_step(IntPtr world, float seconds);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_world_add_static(IntPtr world, Collider[] colliders, uint count);

        [DllImport(Library)]
        internal static extern uint fr_body_create(IntPtr world, uint kind, float[] position, float[] rotation, float mass, Collider[] colliders, uint count);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_remove(IntPtr world, uint body);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_contains(IntPtr world, uint body);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_get(IntPtr world, uint body, out BodyState state);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_set(IntPtr world, uint body, in BodyState state);

        [DllImport(Library)]
        internal static extern int fr_body_kind(IntPtr world, uint body);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_set_kind(IntPtr world, uint body, uint kind);

        [DllImport(Library)]
        internal static extern float fr_body_mass(IntPtr world, uint body);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_set_rewindable(IntPtr world, uint body, [MarshalAs(UnmanagedType.U1)] bool rewindable);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_add_force(IntPtr world, uint body, float x, float y, float z);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_add_impulse(IntPtr world, uint body, float x, float y, float z);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_world_raycast(IntPtr world, float[] origin, float[] direction, float maxDistance, out RayHit hit);

        [DllImport(Library)]
        internal static extern uint fr_collider_touching(IntPtr world, uint body, uint index, [Out] ColliderRef[] touching, uint capacity);

        [DllImport(Library)]
        internal static extern uint fr_world_overlap(IntPtr world, float x, float y, float z, float radius, uint[] bodies, uint capacity);

        [DllImport(Library)]
        internal static extern uint fr_world_snapshot(IntPtr world);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_world_snapshot_copy(IntPtr world, byte[] bytes, uint length);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_world_load(IntPtr world, byte[] bytes, uint length);

        [DllImport(Library)]
        internal static extern ulong fr_world_hash(IntPtr world);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Collider2D
        {
            public uint Shape;
            public float PositionX;
            public float PositionY;
            public float Rotation;
            public float HalfExtentsX;
            public float HalfExtentsY;
            public float Radius;
            public float HalfHeight;
            public IntPtr Points;
            public uint PointCount;
            public float Friction;
            public float Restitution;
            public uint FrictionCombine;
            public uint RestitutionCombine;
            public uint Layer;
            public uint IsTrigger;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BodyState2D
        {
            public float PositionX;
            public float PositionY;
            public float Rotation;
            public float VelocityX;
            public float VelocityY;
            public float AngularVelocity;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RayHit2D
        {
            public uint Body;
            public float PointX;
            public float PointY;
            public float NormalX;
            public float NormalY;
            public float Distance;
        }

        [DllImport(Library)]
        internal static extern IntPtr fr2_world_create(float gravityX, float gravityY);

        [DllImport(Library)]
        internal static extern void fr2_world_destroy(IntPtr world);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_world_set_layers(IntPtr world, uint[] masks);

        [DllImport(Library)]
        internal static extern void fr2_world_step(IntPtr world, float seconds);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_world_add_static(IntPtr world, Collider2D[] colliders, uint count);

        [DllImport(Library)]
        internal static extern uint fr2_body_create(IntPtr world, uint kind, float x, float y, float rotation, float mass, Collider2D[] colliders, uint count);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_remove(IntPtr world, uint body);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_contains(IntPtr world, uint body);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_get(IntPtr world, uint body, out BodyState2D state);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_set(IntPtr world, uint body, in BodyState2D state);

        [DllImport(Library)]
        internal static extern int fr2_body_kind(IntPtr world, uint body);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_set_kind(IntPtr world, uint body, uint kind);

        [DllImport(Library)]
        internal static extern float fr2_body_mass(IntPtr world, uint body);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_set_rewindable(IntPtr world, uint body, [MarshalAs(UnmanagedType.U1)] bool rewindable);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_add_force(IntPtr world, uint body, float x, float y);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_add_impulse(IntPtr world, uint body, float x, float y);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_world_raycast(IntPtr world, float originX, float originY, float directionX, float directionY, float maxDistance, out RayHit2D hit);

        [DllImport(Library)]
        internal static extern uint fr2_collider_touching(IntPtr world, uint body, uint index, [Out] ColliderRef[] touching, uint capacity);

        [DllImport(Library)]
        internal static extern uint fr2_world_overlap(IntPtr world, float x, float y, float radius, uint[] bodies, uint capacity);

        [DllImport(Library)]
        internal static extern uint fr2_world_snapshot(IntPtr world);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_world_snapshot_copy(IntPtr world, byte[] bytes, uint length);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_world_load(IntPtr world, byte[] bytes, uint length);

        [DllImport(Library)]
        internal static extern ulong fr2_world_hash(IntPtr world);
    }
}
```

`com.fomoxa.networking.rapier/Runtime/Rapier/RapierColliders.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Rapier
{
    internal sealed class RapierColliders : IDisposable
    {
        private readonly List<GCHandle> pinned = new List<GCHandle>();

        public RapierColliders(IReadOnlyList<ColliderDesc> colliders)
        {
            Native = new RapierNative.Collider[colliders.Count];
            for (int index = 0; index < colliders.Count; index++)
            {
                Native[index] = Convert(colliders[index]);
            }
        }

        public RapierNative.Collider[] Native { get; }

        public void Dispose()
        {
            foreach (GCHandle handle in pinned)
            {
                handle.Free();
            }

            pinned.Clear();
        }

        private RapierNative.Collider Convert(in ColliderDesc collider)
        {
            BodyShape shape = collider.Shape;
            ColliderMaterial material = collider.Material;
            var native = new RapierNative.Collider
            {
                Shape = (uint)shape.Kind,
                PositionX = collider.Position.X,
                PositionY = collider.Position.Y,
                PositionZ = collider.Position.Z,
                RotationX = collider.Rotation.X,
                RotationY = collider.Rotation.Y,
                RotationZ = collider.Rotation.Z,
                RotationW = collider.Rotation.W,
                HalfExtentsX = shape.HalfExtents.X,
                HalfExtentsY = shape.HalfExtents.Y,
                HalfExtentsZ = shape.HalfExtents.Z,
                Radius = shape.Radius,
                HalfHeight = shape.HalfHeight,
                Friction = material.Friction,
                Restitution = material.Restitution,
                FrictionCombine = (uint)material.FrictionCombine,
                RestitutionCombine = (uint)material.RestitutionCombine,
                Layer = (uint)collider.Layer,
                IsTrigger = collider.IsTrigger ? 1u : 0u,
            };
            IReadOnlyList<Vector3> points = shape.Points;
            if (points.Count > 0)
            {
                var flat = new float[points.Count * 3];
                for (int index = 0; index < points.Count; index++)
                {
                    flat[index * 3] = points[index].X;
                    flat[index * 3 + 1] = points[index].Y;
                    flat[index * 3 + 2] = points[index].Z;
                }

                native.Points = Pin(flat);
                native.PointCount = (uint)points.Count;
            }

            IReadOnlyList<int> triangles = shape.Triangles;
            if (triangles.Count > 0)
            {
                var indices = new uint[triangles.Count];
                for (int index = 0; index < indices.Length; index++)
                {
                    indices[index] = (uint)triangles[index];
                }

                native.Triangles = Pin(indices);
                native.TriangleIndexCount = (uint)indices.Length;
            }

            return native;
        }

        private IntPtr Pin(object array)
        {
            GCHandle handle = GCHandle.Alloc(array, GCHandleType.Pinned);
            pinned.Add(handle);
            return handle.AddrOfPinnedObject();
        }
    }
}
```

`com.fomoxa.networking.rapier/Runtime/Rapier/IRapierBodies.cs`:

```csharp
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Rapier
{
    internal interface IRapierBodies : IPhysicsSimulation
    {
        bool IsDisposed { get; }

        bool Contains(BodyHandle body);

        bool RemoveBody(BodyHandle body);

        BodyKind GetKind(BodyHandle body);

        void SetKind(BodyHandle body, BodyKind kind);

        void SetRewindable(BodyHandle body, bool rewindable);
    }
}
```

`com.fomoxa.networking.rapier/Runtime/Rapier/RapierWorld.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Rapier
{
    public sealed class RapierWorld : IPhysicsWorld, IRapierBodies, IDisposable
    {
        private const int LayerCount = 32;

        private IntPtr world;
        private uint[] overlapped = new uint[64];
        private RapierNative.ColliderRef[] touched = new RapierNative.ColliderRef[16];

        public RapierWorld(Vector3 gravity)
        {
            world = RapierNative.fr_world_create(gravity.X, gravity.Y, gravity.Z);
        }

        public PhysicsBackend Backend => PhysicsBackend.Rapier;

        public bool IsDisposed => world == IntPtr.Zero;

        public ulong StateHash => RapierNative.fr_world_hash(world);

        public void SetLayerCollisions(IReadOnlyList<uint> masks)
        {
            if (masks == null)
            {
                throw new ArgumentNullException(nameof(masks));
            }

            if (masks.Count != LayerCount)
            {
                throw new ArgumentException($"a layer collision matrix has {LayerCount} masks, not {masks.Count}", nameof(masks));
            }

            var copy = new uint[LayerCount];
            for (int layer = 0; layer < LayerCount; layer++)
            {
                copy[layer] = masks[layer];
            }

            RapierNative.fr_world_set_layers(world, copy);
        }

        public void AddStatic(IReadOnlyList<ColliderDesc> colliders)
        {
            if (colliders == null)
            {
                throw new ArgumentNullException(nameof(colliders));
            }

            if (colliders.Count == 0)
            {
                return;
            }

            using (var native = new RapierColliders(colliders))
            {
                if (!RapierNative.fr_world_add_static(world, native.Native, (uint)native.Native.Length))
                {
                    throw new ArgumentException("Rapier refused a static collider (degenerate hull or mesh)", nameof(colliders));
                }
            }
        }

        public void Step(float seconds) => RapierNative.fr_world_step(world, seconds);

        public PhysicsSnapshot CreateSnapshot() => new RapierSnapshot();

        public void Save(PhysicsSnapshot into)
        {
            RapierSnapshot snapshot = Expect(into);
            uint length = RapierNative.fr_world_snapshot(world);
            snapshot.Resize((int)length);
            if (!RapierNative.fr_world_snapshot_copy(world, snapshot.Bytes, length))
            {
                throw new InvalidOperationException("the Rapier world could not copy its snapshot");
            }
        }

        public void Load(PhysicsSnapshot from)
        {
            RapierSnapshot snapshot = Expect(from);
            if (snapshot.Bytes.Length == 0 || !RapierNative.fr_world_load(world, snapshot.Bytes, (uint)snapshot.Bytes.Length))
            {
                throw new ArgumentException("the snapshot does not hold a Rapier world of this build", nameof(from));
            }
        }

        public BodyHandle CreateBody(in BodyDesc desc)
        {
            float[] position = { desc.Position.X, desc.Position.Y, desc.Position.Z };
            float[] rotation = { desc.Rotation.X, desc.Rotation.Y, desc.Rotation.Z, desc.Rotation.W };
            uint id;
            using (var native = new RapierColliders(desc.Colliders))
            {
                id = RapierNative.fr_body_create(world, (uint)desc.Kind, position, rotation, desc.Mass, native.Native, (uint)native.Native.Length);
            }

            if (id == 0)
            {
                throw new ArgumentException("Rapier refused the body (no collider, degenerate hull or mesh, or a disposed world)", nameof(desc));
            }

            return new BodyHandle((int)id);
        }

        public bool RemoveBody(BodyHandle body) => body.IsValid && RapierNative.fr_body_remove(world, (uint)body.Value);

        public bool Contains(BodyHandle body) => body.IsValid && RapierNative.fr_body_contains(world, (uint)body.Value);

        public BodyState GetBody(BodyHandle body)
        {
            if (!RapierNative.fr_body_get(world, Id(body), out RapierNative.BodyState state))
            {
                throw Missing(body);
            }

            return new BodyState
            {
                Position = new Vector3(state.PositionX, state.PositionY, state.PositionZ),
                Rotation = new Quaternion(state.RotationX, state.RotationY, state.RotationZ, state.RotationW),
                Velocity = new Vector3(state.VelocityX, state.VelocityY, state.VelocityZ),
                AngularVelocity = new Vector3(state.AngularVelocityX, state.AngularVelocityY, state.AngularVelocityZ),
            };
        }

        public BodyKind GetKind(BodyHandle body)
        {
            int kind = RapierNative.fr_body_kind(world, Id(body));
            if (kind < 0)
            {
                throw Missing(body);
            }

            return (BodyKind)kind;
        }

        public float GetMass(BodyHandle body)
        {
            Require(body);
            return RapierNative.fr_body_mass(world, (uint)body.Value);
        }

        public void SetBody(BodyHandle body, in BodyState state)
        {
            var native = new RapierNative.BodyState
            {
                PositionX = state.Position.X,
                PositionY = state.Position.Y,
                PositionZ = state.Position.Z,
                RotationX = state.Rotation.X,
                RotationY = state.Rotation.Y,
                RotationZ = state.Rotation.Z,
                RotationW = state.Rotation.W,
                VelocityX = state.Velocity.X,
                VelocityY = state.Velocity.Y,
                VelocityZ = state.Velocity.Z,
                AngularVelocityX = state.AngularVelocity.X,
                AngularVelocityY = state.AngularVelocity.Y,
                AngularVelocityZ = state.AngularVelocity.Z,
            };
            if (!RapierNative.fr_body_set(world, Id(body), native))
            {
                throw Missing(body);
            }
        }

        public void SetKind(BodyHandle body, BodyKind kind)
        {
            if (!RapierNative.fr_body_set_kind(world, Id(body), (uint)kind))
            {
                throw Missing(body);
            }
        }

        public void SetRewindable(BodyHandle body, bool rewindable)
        {
            if (!RapierNative.fr_body_set_rewindable(world, Id(body), rewindable))
            {
                throw Missing(body);
            }
        }

        public void AddForce(BodyHandle body, Vector3 force)
        {
            if (!RapierNative.fr_body_add_force(world, Id(body), force.X, force.Y, force.Z))
            {
                throw Missing(body);
            }
        }

        public void AddImpulse(BodyHandle body, Vector3 impulse)
        {
            if (!RapierNative.fr_body_add_impulse(world, Id(body), impulse.X, impulse.Y, impulse.Z))
            {
                throw Missing(body);
            }
        }

        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RayHit hit)
        {
            if (!RapierNative.fr_world_raycast(world, new[] { origin.X, origin.Y, origin.Z }, new[] { direction.X, direction.Y, direction.Z }, maxDistance, out RapierNative.RayHit found))
            {
                hit = default;
                return false;
            }

            hit = new RayHit(new BodyHandle((int)found.Body), new Vector3(found.PointX, found.PointY, found.PointZ), new Vector3(found.NormalX, found.NormalY, found.NormalZ), found.Distance);
            return true;
        }

        public int Overlap(Vector3 center, float radius, BodyHandle[] results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (overlapped.Length < results.Length)
            {
                overlapped = new uint[results.Length];
            }

            int count = (int)RapierNative.fr_world_overlap(world, center.X, center.Y, center.Z, radius, overlapped, (uint)results.Length);
            for (int index = 0; index < count; index++)
            {
                results[index] = new BodyHandle((int)overlapped[index]);
            }

            return count;
        }

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

        public void Dispose()
        {
            if (world != IntPtr.Zero)
            {
                RapierNative.fr_world_destroy(world);
                world = IntPtr.Zero;
            }
        }

        private static RapierSnapshot Expect(PhysicsSnapshot snapshot) =>
            snapshot as RapierSnapshot ?? throw new ArgumentException("the snapshot was not created by a Rapier world", nameof(snapshot));

        private static ArgumentException Missing(BodyHandle body) => new ArgumentException($"body {body.Value} is not in this world", nameof(body));

        private static uint Id(BodyHandle body) => body.IsValid ? (uint)body.Value : 0u;

        private void Require(BodyHandle body)
        {
            if (!Contains(body))
            {
                throw Missing(body);
            }
        }

        private sealed class RapierSnapshot : PhysicsSnapshot
        {
            public byte[] Bytes { get; private set; } = Array.Empty<byte>();

            public void Resize(int length)
            {
                if (Bytes.Length != length)
                {
                    Bytes = new byte[length];
                }
            }
        }
    }
}
```

`Tools/build-rapier.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$SCRIPT_DIR/.."
CRATE="$REPO/native/fomoxa-rapier"
PLUGINS="$REPO/com.fomoxa.networking.rapier/Runtime/Plugins"

build_linux() {
  cargo build --release --manifest-path "$CRATE/Cargo.toml" --target x86_64-unknown-linux-gnu
  mkdir -p "$PLUGINS/Linux/x86_64"
  cp "$CRATE/target/x86_64-unknown-linux-gnu/release/libfomoxa_rapier.so" "$PLUGINS/Linux/x86_64/"
}

build_windows() {
  local work
  work="$(mktemp -d /mnt/c/Users/Public/fomoxa-rapier-XXXXXX)"
  cp -r "$CRATE/Cargo.toml" "$CRATE/src" "$work/"
  if [ -f "$CRATE/Cargo.lock" ]; then
    cp "$CRATE/Cargo.lock" "$work/"
  fi
  (cd "$work" && cargo.exe build --release)
  mkdir -p "$PLUGINS/Windows/x86_64"
  cp "$work/target/release/fomoxa_rapier.dll" "$PLUGINS/Windows/x86_64/"
  rm -rf "$work"
}

case "${1:-linux}" in
  linux) build_linux ;;
  windows) build_windows ;;
  all) build_linux; build_windows ;;
  *) echo "usage: $0 [linux|windows|all]" >&2; exit 2 ;;
esac
```

---

## 3. Test

`tests/Fomoxa.Networking.Rapier.Tests.csproj` (biên dịch Core, fixture registry và backend console của `../../unity/com.fomoxa.networking` cùng `Runtime/Rapier`; chép binary của hai nền tảng ra thư mục chạy):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <IsPackable>false</IsPackable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <Unity>../../unity/com.fomoxa.networking</Unity>
    <Package>../com.fomoxa.networking.rapier</Package>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="$(Unity)/Runtime/Core/**/*.cs" />
    <Compile Include="$(Unity)/Tests/Fixtures/**/*.cs" />
    <Compile Include="$(Unity)/Standalone~/**/*.cs" />
    <Compile Include="$(Package)/Runtime/Rapier/**/*.cs" />
    <Compile Include="*.cs" />
  </ItemGroup>

  <ItemGroup>
    <None Include="$(Package)/Runtime/Plugins/Linux/x86_64/libfomoxa_rapier.so" Link="libfomoxa_rapier.so" CopyToOutputDirectory="PreserveNewest" />
    <None Include="$(Package)/Runtime/Plugins/Windows/x86_64/fomoxa_rapier.dll" Link="fomoxa_rapier.dll" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="Fomoxa.Attributes">
      <HintPath>$(Unity)/Runtime/Plugins/Fomoxa.Attributes.dll</HintPath>
    </Reference>
    <Reference Include="Fomoxa.Net">
      <HintPath>$(Unity)/Runtime/Plugins/Fomoxa.Net.dll</HintPath>
    </Reference>
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
  </ItemGroup>

</Project>
```

`tests/RapierWorldTest.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;

namespace Fomoxa.Networking.Rapier.Tests
{
    public sealed class RapierWorldTest
    {
        private const float Tick = 1f / 60f;
        private const ulong PileStateHash = 0xF1860E86303F3AEDUL;
        private static readonly Vector3 Gravity = new Vector3(0f, -9.81f, 0f);

        [Test]
        public void ADynamicBodyFallsOntoAStaticGroundAndHasTheGivenMass()
        {
            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { Ground(0) });
                BodyHandle ball = world.CreateBody(Ball(new Vector3(0f, 5f, 0f), 2f));

                Run(world, 300);

                Assert.AreEqual(0.5f, world.GetBody(ball).Position.Y, 0.05f);
                Assert.AreEqual(2f, world.GetMass(ball), 1e-4f);
                Assert.AreEqual(BodyKind.Dynamic, world.GetKind(ball));
            }
        }

        [Test]
        public void AForceLastsOneStepAndAnImpulseChangesTheVelocityAtOnce()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                BodyHandle ball = world.CreateBody(Ball(Vector3.Zero, 2f));

                world.AddImpulse(ball, new Vector3(4f, 0f, 0f));
                float afterImpulse = world.GetBody(ball).Velocity.X;
                world.AddForce(ball, new Vector3(120f, 0f, 0f));
                world.Step(Tick);
                float afterForce = world.GetBody(ball).Velocity.X;
                world.Step(Tick);

                Assert.AreEqual(2f, afterImpulse, 1e-4f);
                Assert.AreEqual(3f, afterForce, 1e-4f);
                Assert.AreEqual(3f, world.GetBody(ball).Velocity.X, 1e-4f);
            }
        }

        [Test]
        public void LoadingASnapshotReplaysTheSameSteps()
        {
            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { Ground(0) });
                for (int index = 0; index < 5; index++)
                {
                    world.CreateBody(Ball(new Vector3(index * 0.3f, 2f + index, 0f), 1f));
                }

                Run(world, 30);
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);
                Run(world, 60);
                ulong first = world.StateHash;

                world.Load(snapshot);
                Run(world, 60);

                Assert.AreEqual(first, world.StateHash);
            }
        }

        [Test]
        public void LoadKeepsBodiesCreatedAfterTheSnapshotAndDoesNotRecreateRemovedOnes()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                BodyHandle kept = world.CreateBody(Ball(Vector3.Zero, 1f));
                BodyHandle removed = world.CreateBody(Ball(new Vector3(5f, 0f, 0f), 1f));
                world.SetBody(kept, new BodyState { Position = Vector3.Zero, Rotation = Quaternion.Identity, Velocity = Vector3.UnitX });
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);
                world.Step(1f);
                world.RemoveBody(removed);
                BodyHandle added = world.CreateBody(Ball(new Vector3(-5f, 0f, 0f), 1f));
                world.SetBody(added, new BodyState { Position = new Vector3(-6f, 0f, 0f), Rotation = Quaternion.Identity });

                world.Load(snapshot);

                Assert.AreEqual(0f, world.GetBody(kept).Position.X, 1e-5f);
                Assert.IsFalse(world.Contains(removed));
                Assert.IsTrue(world.Contains(added));
                Assert.AreEqual(-6f, world.GetBody(added).Position.X, 1e-5f);
                Assert.AreNotEqual(kept.Value, added.Value);
                Assert.AreNotEqual(removed.Value, added.Value);
            }
        }

        [Test]
        public void ABodyThatIsNotRewindableKeepsItsCurrentStateOnLoad()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                BodyHandle proxy = world.CreateBody(Ball(Vector3.Zero, 1f));
                world.SetKind(proxy, BodyKind.Kinematic);
                world.SetRewindable(proxy, false);
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);
                world.SetBody(proxy, new BodyState { Position = new Vector3(3f, 0f, 0f), Rotation = Quaternion.Identity });

                world.Load(snapshot);

                Assert.AreEqual(3f, world.GetBody(proxy).Position.X, 1e-5f);
                Assert.AreEqual(BodyKind.Kinematic, world.GetKind(proxy));
            }
        }

        [Test]
        public void TwoWorldsGivenTheSameCommandsReachTheSameState()
        {
            using (var first = Pile())
            using (var second = Pile())
            {
                Run(first, 240);
                Run(second, 240);

                Assert.AreEqual(first.StateHash, second.StateHash);
            }
        }

        [Test]
        public void APileReachesTheSameStateOnEveryPlatform()
        {
            using (RapierWorld world = Pile())
            {
                Run(world, 240);

                TestContext.Out.WriteLine($"pile state hash 0x{world.StateHash:X16}");
                Assert.AreEqual(PileStateHash, world.StateHash);
            }
        }

        [Test]
        public void LayersThatDoNotCollideAndTriggersLetBodiesThrough()
        {
            using (var world = new RapierWorld(Gravity))
            {
                var masks = new uint[32];
                for (int layer = 0; layer < 32; layer++)
                {
                    masks[layer] = uint.MaxValue;
                }

                masks[1] &= ~(1u << 2);
                masks[2] &= ~(1u << 1);
                world.SetLayerCollisions(masks);
                world.AddStatic(new[] { Ground(1) });
                BodyHandle ghost = world.CreateBody(Ball(new Vector3(0f, 2f, 0f), 1f, layer: 2));
                BodyHandle solid = world.CreateBody(Ball(new Vector3(3f, 2f, 0f), 1f, layer: 3));
                world.AddStatic(new[] { new ColliderDesc(BodyShape.Box(new Vector3(1f, 0.5f, 1f)), new Vector3(30f, -0.5f, 0f), Quaternion.Identity, ColliderMaterial.Default, 0, true) });
                BodyHandle triggered = world.CreateBody(Ball(new Vector3(30f, 2f, 0f), 1f));

                Run(world, 120);

                Assert.Less(world.GetBody(ghost).Position.Y, -1f);
                Assert.AreEqual(0.5f, world.GetBody(solid).Position.Y, 0.05f);
                Assert.Less(world.GetBody(triggered).Position.Y, -1f);
            }
        }

        [Test]
        public void RaysAndOverlapsFindBodies()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                world.AddStatic(new[] { Ground(0) });
                BodyHandle ball = world.CreateBody(Ball(new Vector3(0f, 3f, 0f), 1f));
                BodyHandle other = world.CreateBody(Ball(new Vector3(0.6f, 3f, 0f), 1f));
                var found = new BodyHandle[4];

                Assert.IsTrue(world.Raycast(new Vector3(0f, 10f, 0f), new Vector3(0f, -2f, 0f), 100f, out RayHit hit));
                int count = world.Overlap(new Vector3(0.3f, 3f, 0f), 0.2f, found);

                Assert.AreEqual(ball.Value, hit.Body.Value);
                Assert.AreEqual(3.5f, hit.Point.Y, 1e-4f);
                Assert.AreEqual(6.5f, hit.Distance, 1e-4f);
                Assert.AreEqual(1f, hit.Normal.Y, 1e-4f);
                Assert.AreEqual(2, count);
                CollectionAssert.AreEqual(new[] { ball.Value, other.Value }, new[] { found[0].Value, found[1].Value });
                Assert.IsFalse(world.Raycast(new Vector3(0f, 10f, 0f), Vector3.UnitY, 100f, out _));
            }
        }

        [Test]
        public void HullsFallOntoTriangleMeshes()
        {
            using (var world = new RapierWorld(Gravity))
            {
                Vector3[] floor = { new Vector3(-10f, 0f, -10f), new Vector3(10f, 0f, -10f), new Vector3(10f, 0f, 10f), new Vector3(-10f, 0f, 10f) };
                world.AddStatic(new[] { new ColliderDesc(BodyShape.TriangleMesh(floor, new[] { 0, 2, 1, 0, 3, 2 }), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false) });
                Vector3[] cube = { new Vector3(-0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f) };
                BodyHandle box = world.CreateBody(new BodyDesc(BodyKind.Dynamic, BodyShape.ConvexHull(cube), new Vector3(0f, 3f, 0f), Quaternion.Identity, 1f));

                Run(world, 300);

                Assert.AreEqual(0.5f, world.GetBody(box).Position.Y, 0.05f);
            }
        }

        [Test]
        public void ADisposedWorldHasNoBodiesAndRefusesNewOnes()
        {
            var world = new RapierWorld(Gravity);
            BodyHandle ball = world.CreateBody(Ball(Vector3.Zero, 1f));

            world.Dispose();

            Assert.IsTrue(world.IsDisposed);
            Assert.IsFalse(world.Contains(ball));
            Assert.IsFalse(new PhysicsBody(world, ball).IsValid);
            Assert.Throws<ArgumentException>(() => world.CreateBody(Ball(Vector3.Zero, 1f)));
        }

        [Test]
        public void TheSnapshotOfASceneSizedWorldIsMeasured()
        {
            using (var world = new RapierWorld(Gravity))
            {
                const int cells = 100;
                var vertices = new Vector3[(cells + 1) * (cells + 1)];
                for (int z = 0; z <= cells; z++)
                {
                    for (int x = 0; x <= cells; x++)
                    {
                        vertices[z * (cells + 1) + x] = new Vector3(x, (x + z) % 3 * 0.1f, z);
                    }
                }

                var triangles = new int[cells * cells * 6];
                for (int z = 0, next = 0; z < cells; z++)
                {
                    for (int x = 0; x < cells; x++)
                    {
                        int corner = z * (cells + 1) + x;
                        triangles[next++] = corner;
                        triangles[next++] = corner + cells + 1;
                        triangles[next++] = corner + cells + 2;
                        triangles[next++] = corner;
                        triangles[next++] = corner + cells + 2;
                        triangles[next++] = corner + 1;
                    }
                }

                world.AddStatic(new[] { new ColliderDesc(BodyShape.TriangleMesh(vertices, triangles), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false) });
                for (int index = 0; index < 100; index++)
                {
                    world.CreateBody(Ball(new Vector3(index % 10 * 5f + 2f, 2f, index / 10 * 5f + 2f), 1f));
                }

                Run(world, 60);
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);
                var clock = Stopwatch.StartNew();
                const int rounds = 50;
                for (int round = 0; round < rounds; round++)
                {
                    world.Save(snapshot);
                }

                double saveMilliseconds = clock.Elapsed.TotalMilliseconds / rounds;
                clock.Restart();
                for (int round = 0; round < rounds; round++)
                {
                    world.Load(snapshot);
                }

                double loadMilliseconds = clock.Elapsed.TotalMilliseconds / rounds;
                clock.Restart();
                for (int round = 0; round < rounds; round++)
                {
                    world.Step(Tick);
                }

                double stepMilliseconds = clock.Elapsed.TotalMilliseconds / rounds;
                TestContext.Out.WriteLine($"triangles {triangles.Length / 3}, bodies 100: save {saveMilliseconds:F3} ms, load {loadMilliseconds:F3} ms, step {stepMilliseconds:F3} ms");
                Assert.Greater(saveMilliseconds, 0d);
            }
        }

        [Test]
        public void ABallRestingOnTheGroundTouchesTheStaticColliderOfItsIndex()
        {
            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { new ColliderDesc(BodyShape.Box(new Vector3(1f)), new Vector3(50f, 0f, 0f), Quaternion.Identity, ColliderMaterial.Default, 0, false), Ground(0) });
                BodyHandle ball = world.CreateBody(Ball(new Vector3(0f, 2f, 0f), 1f));

                Run(world, 120);

                CollectionAssert.AreEqual(new[] { RapierCollider.Static(1) }, Touching(world, RapierCollider.OfBody(ball, 0)));
                CollectionAssert.AreEqual(new[] { RapierCollider.OfBody(ball, 0) }, Touching(world, RapierCollider.Static(1)));
                CollectionAssert.IsEmpty(Touching(world, RapierCollider.Static(0)));
                CollectionAssert.IsEmpty(Touching(world, RapierCollider.OfBody(ball, 5)));
            }
        }

        [Test]
        public void ASensorFindsDynamicAndKinematicBodiesButNotLayersItIgnores()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                var masks = new uint[32];
                for (int layer = 0; layer < 32; layer++)
                {
                    masks[layer] = uint.MaxValue;
                }

                masks[1] &= ~(1u << 2);
                masks[2] &= ~(1u << 1);
                world.SetLayerCollisions(masks);
                world.AddStatic(new[] { new ColliderDesc(BodyShape.Box(new Vector3(2f)), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 1, true) });
                BodyHandle ball = world.CreateBody(Ball(new Vector3(-1f, 0f, 0f), 1f));
                BodyHandle carried = world.CreateBody(new BodyDesc(BodyKind.Kinematic, BodyShape.Sphere(0.5f), new Vector3(1f, 0f, 0f), Quaternion.Identity, 0f));
                world.CreateBody(Ball(new Vector3(0f, 1.2f, 0f), 1f, layer: 2));
                var pair = new[]
                {
                    new ColliderDesc(BodyShape.Sphere(0.25f), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false),
                    new ColliderDesc(BodyShape.Sphere(0.25f), new Vector3(10f, 0f, 0f), Quaternion.Identity, ColliderMaterial.Default, 0, false),
                };
                BodyHandle reaching = world.CreateBody(new BodyDesc(BodyKind.Dynamic, pair, new Vector3(-10f, -1.5f, 0f), Quaternion.Identity, 1f));

                world.Step(Tick);

                List<RapierCollider> zone = Touching(world, RapierCollider.Static(0));
                CollectionAssert.AreEquivalent(new[] { RapierCollider.OfBody(ball, 0), RapierCollider.OfBody(carried, 0), RapierCollider.OfBody(reaching, 1) }, zone);
                CollectionAssert.IsEmpty(Touching(world, RapierCollider.OfBody(ball, 0)));
            }
        }

        [Test]
        public void LoadBringsBackTheTouchingSetOfTheSavedTick()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                world.AddStatic(new[] { new ColliderDesc(BodyShape.Box(new Vector3(1f)), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, true) });
                BodyHandle ball = world.CreateBody(Ball(new Vector3(-3f, 0f, 0f), 1f));
                world.SetBody(ball, new BodyState { Position = new Vector3(-3f, 0f, 0f), Rotation = Quaternion.Identity, Velocity = new Vector3(6f, 0f, 0f) });
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                int ticks = 0;
                while (Touching(world, RapierCollider.Static(0)).Count == 0 && ticks < 120)
                {
                    world.Step(Tick);
                    ticks++;
                }

                world.Save(snapshot);
                while (Touching(world, RapierCollider.Static(0)).Count > 0 && ticks < 240)
                {
                    world.Step(Tick);
                    ticks++;
                }

                CollectionAssert.IsEmpty(Touching(world, RapierCollider.Static(0)));

                world.Load(snapshot);

                CollectionAssert.AreEqual(new[] { RapierCollider.OfBody(ball, 0) }, Touching(world, RapierCollider.Static(0)));
                Assert.Throws<ArgumentNullException>(() => world.Touching(RapierCollider.Static(0), null));
            }
        }

        private static List<RapierCollider> Touching(RapierWorld world, RapierCollider collider)
        {
            var touching = new List<RapierCollider>();
            world.Touching(collider, touching);
            return touching;
        }

        private static RapierWorld Pile()
        {
            var world = new RapierWorld(Gravity);
            world.AddStatic(new[] { Ground(0) });
            for (int index = 0; index < 20; index++)
            {
                world.CreateBody(new BodyDesc(BodyKind.Dynamic, BodyShape.Box(new Vector3(0.4f)), new Vector3(index % 4 * 0.5f, 1f + index * 0.9f, index % 3 * 0.3f), Quaternion.CreateFromYawPitchRoll(index * 0.3f, index * 0.2f, 0f), 1f));
            }

            return world;
        }

        private static void Run(RapierWorld world, int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                world.Step(Tick);
            }
        }

        private static ColliderDesc Ground(int layer) =>
            new ColliderDesc(BodyShape.Box(new Vector3(10f, 0.5f, 10f)), new Vector3(0f, -0.5f, 0f), Quaternion.Identity, ColliderMaterial.Default, layer, false);

        private static BodyDesc Ball(Vector3 position, float mass, int layer = 0) =>
            new BodyDesc(BodyKind.Dynamic, new[] { new ColliderDesc(BodyShape.Sphere(0.5f), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, layer, false) }, position, Quaternion.Identity, mass);
    }
}
```

| Test | Kiểm |
|---|---|
| `ADynamicBodyFallsOntoAStaticGroundAndHasTheGivenMass` | Cầu rơi xuống sàn tĩnh, dừng ở 0.5; khối lượng đúng `mass`; loại `Dynamic` |
| `AForceLastsOneStepAndAnImpulseChangesTheVelocityAtOnce` | Xung lực đổi vận tốc ngay; lực tác dụng đúng một bước |
| `LoadingASnapshotReplaysTheSameSteps` | Chụp, bước, khôi phục, bước lại cho cùng băm |
| `LoadKeepsBodiesCreatedAfterTheSnapshotAndDoesNotRecreateRemovedOnes` | Body tạo sau lúc chụp giữ trạng thái; body xóa sau lúc chụp không sống lại |
| `ABodyThatIsNotRewindableKeepsItsCurrentStateOnLoad` | Body không chạy lại được giữ trạng thái và loại hiện tại qua `Load` |
| `TwoWorldsGivenTheSameCommandsReachTheSameState` | Hai thế giới cùng chuỗi lệnh cho cùng băm |
| `APileReachesTheSameStateOnEveryPlatform` | Băm cố định giữa nền tảng |
| `LayersThatDoNotCollideAndTriggersLetBodiesThrough` | Ma trận layer chặn va chạm; trigger không sinh lực |
| `RaysAndOverlapsFindBodies` | `Raycast` thấy body vừa tạo chưa bước, trả điểm, pháp tuyến, khoảng cách của va chạm gần nhất; `Overlap` trả các body theo thứ tự id, không gồm collider tĩnh |
| `HullsFallOntoTriangleMeshes` | Bao lồi rơi lên lưới tam giác tĩnh |
| `ADisposedWorldHasNoBodiesAndRefusesNewOnes` | Thế giới đã hủy không có body, từ chối tạo |
| `TheSnapshotOfASceneSizedWorldIsMeasured` | Đo `Save`, `Load`, `Step` (số liệu ở mục 1) |
| `ABallRestingOnTheGroundTouchesTheStaticColliderOfItsIndex` | 08.15: tập chạm của bóng nằm yên và của collider tĩnh theo chỉ số |
| `ASensorFindsDynamicAndKinematicBodiesButNotLayersItIgnores` | 08.15: sensor thấy body động, kinematic, collider thứ hai của một body; không thấy layer bị chặn |
| `LoadBringsBackTheTouchingSetOfTheSavedTick` | 08.15: `Load` trả lại tập chạm của tick đã chụp |
