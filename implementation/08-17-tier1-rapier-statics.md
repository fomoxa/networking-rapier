# 08.17 — Tier 1: static groups, unowned bodies and body motion in the crate and `RapierScenes`

> The tier 1 part of sub-step 08.17 in this repository; the plan and the contract are in the section "Kế hoạch đề xuất của 08.17, 08.18" (proposed plan of 08.17 and 08.18) of `implementation/08-prediction-physics.md` in the `unity` repository, and the Core types (`StaticGroup`, `BodyMotion`, `BodyMotion2D`, the new members of `IPhysicsScenes`) are described in its `implementation/08-17-tier1-runtime-statics.md`. Built and run on a copy of both repositories (`prototypes/0817-api`): `dotnet test` 41/41 on Linux (.NET 8) and Windows (.NET 9); the two-sided check passes with the Linux and the Windows server. Applied to the repository: the libraries were rebuilt there (the Linux library is byte for byte the prototype's, the Windows one differs only as a new build); `dotnet test` 41/41 on Linux and Windows; Unity EditMode 9/9; the two-sided check passes with both servers.

| | Work | Status |
|---|---|:---:|
| 1 | Crate: static colliders kept per slot with their group and recipe; `fr_world_add_static`, `fr2_world_add_static` return a group id and the first index; `fr_world_remove_static`, `fr2_world_remove_static` | ✅ |
| 2 | Crate: `load` puts back static groups added after the snapshot and removes groups removed since; snapshot format 2 | ✅ |
| 3 | Crate: `fr_body_create`, `fr2_body_create` take axis locks, gravity scale and damping; setting a state drops the velocity of locked axes | ✅ |
| 4 | `RapierWorld`, `RapierWorld2D`: `AddStatic` returns `StaticGroup`; `RemoveStatic`; `CreateBody` passes `desc.Motion` | ✅ |
| 5 | `RapierScenes`: the seven new members of `IPhysicsScenes` | ✅ |
| 6 | Libraries rebuilt (Linux x64, Windows x64) | ✅ |
| 7 | `dotnet` tests | ✅ |

**Rules:** P27 (Q167 (1) A, (2) A, (4) A; Q168 (1) A, (2) A).

---

## 1. Overview

### Static groups in the crate

- A world keeps one slot per static collider it was ever given: the group id, the collider recipe and the current `ColliderHandle` (`None` once removed). A slot's position is the static index that `RapierCollider.Static(index)` and the contact tags use, so indices are never reused; the scene file's colliders are the first group.
- `add_static` builds every collider first and inserts nothing if one is refused (degenerate polygon or mesh), then gives the group the next id (from 1) and returns `(group, first index)`. An empty list still gets a group id. `remove_static` removes the colliders of the group from the world and empties their slots; it returns `false` for an unknown or already removed group.
- The snapshot (format 2) stores `(static index, handle)` for every slot that holds a collider. `load` restores the saved world, then:
  - removes the colliders of slots that are empty now but were saved (groups removed since the snapshot);
  - for slots that hold a collider now: takes the saved handle if the slot was saved, otherwise rebuilds the collider from its recipe (groups added since the snapshot).
  Bodies are reconciled afterwards, as before.

### Body motion in the crate

- `BodyRecipe` holds `locks`, `gravity_scale`, `linear_damping`, `angular_damping`; `insert_body` passes them to `RigidBodyBuilder` (`locked_axes`, `gravity_scale`, `linear_damping`, `angular_damping`), so a body put back by `load` keeps them.
- Lock bits are those of `BodyLocks2D` (`1` X, `2` Y, `4` rotation) and `BodyLocks` (`1`, `2`, `4` position X, Y, Z; `8`, `16`, `32` rotation X, Y, Z), mapped to Rapier's `LockedAxes`. 3D gravity is a scale of 1 or 0 from `UseGravity`.
- Rapier's locked axes stop forces and contacts but keep a velocity that is set directly. Unity's frozen axes do not move at all, so `apply_state` (used by `SetBody`, `load` and proxies) sets the velocity of locked axes to zero.

### C#

- `RapierWorld.AddStatic`, `RapierWorld2D.AddStatic` return a `StaticGroup` with `SceneId` 0, the crate's group id, the first index and the count; they throw `ArgumentException` when the crate refuses a collider. `RemoveStatic(group)` removes the group by id.
- `RapierScenes`:
  - `AddStatic(sceneId, …)`, `AddStatic2D(sceneId, …)` create the world of the scene if needed, as `LoadScene` does, and return a `StaticGroup` with the scene id and an id of `RapierScenes` (3D and 2D groups share one sequence, so `RemoveStatic` knows the world).
  - `RemoveStatic` removes the group from its world; an unknown group, a group removed already or a group of an unloaded scene is ignored.
  - `AddBody(sceneId, …)`, `AddBody2D(sceneId, …)` create a body that belongs to no entity: `EntityOf` returns `null` for it and it is not in `WorldsOf(entity)`; `RemoveBody`, `RemoveBody2D` find it by its `PhysicsBody` value. `UnloadScene` forgets the scene's groups and unowned bodies with its worlds.

---

## 2. Code

`native/fomoxa-rapier/src/world2d.rs`, changed parts (`world3d.rs` has the same changes for 3D, shown after):

```rust
const SNAPSHOT_FORMAT: u32 = 2;

const LOCK_POSITION_X: u32 = 1;
const LOCK_POSITION_Y: u32 = 2;
const LOCK_ROTATION: u32 = 4;

struct BodyRecipe {
    kind: u32,
    mass: f32,
    locks: u32,
    gravity_scale: f32,
    linear_damping: f32,
    angular_damping: f32,
    colliders: Vec<ColliderRecipe>,
}

struct StaticSlot {
    group: u32,
    recipe: ColliderRecipe,
    handle: Option<ColliderHandle>,
}

struct SnapshotRef<'a> {
    format: u32,
    world: &'a PhysicsWorld,
    handles: Vec<(u32, u32, u32)>,
    statics: Vec<(u32, u32, u32)>,
}

struct Snapshot {
    format: u32,
    world: PhysicsWorld,
    handles: Vec<(u32, u32, u32)>,
    statics: Vec<(u32, u32, u32)>,
}

pub struct World2D {
    physics: PhysicsWorld,
    bodies: BTreeMap<u32, BodyEntry>,
    next_id: u32,
    layers: [u32; 32],
    statics: Vec<StaticSlot>,
    next_group: u32,
    scratch: Vec<u8>,
}
```

```rust
fn add_static(&mut self, colliders: Vec<ColliderRecipe>) -> Option<(u32, u32)> {
    let first = self.statics.len() as u32;
    let mut built = Vec::with_capacity(colliders.len());
    for (offset, recipe) in colliders.iter().enumerate() {
        built.push(build_collider(recipe, &self.layers, 1.0, collider_tag(0, first + offset as u32))?);
    }

    let group = self.next_group;
    self.next_group += 1;
    for (collider, recipe) in built.into_iter().zip(colliders) {
        let handle = self.physics.insert_collider(collider, None);
        self.statics.push(StaticSlot { group, recipe, handle: Some(handle) });
    }

    Some((group, first))
}

fn remove_static(&mut self, group: u32) -> bool {
    let mut removed = Vec::new();
    for slot in self.statics.iter_mut().filter(|slot| slot.group == group) {
        if let Some(handle) = slot.handle.take() {
            removed.push(handle);
        }
    }

    for handle in &removed {
        self.physics.remove_collider(*handle);
    }

    !removed.is_empty()
}

fn restore_statics(&mut self, saved: &[(u32, u32, u32)]) {
    let saved: BTreeMap<u32, ColliderHandle> = saved
        .iter()
        .map(|(index, raw, generation)| (*index, ColliderHandle::from_raw_parts(*raw, *generation)))
        .collect();
    for (index, handle) in &saved {
        if self.statics.get(*index as usize).map_or(true, |slot| slot.handle.is_none()) {
            self.physics.remove_collider(*handle);
        }
    }

    for index in 0..self.statics.len() {
        if self.statics[index].handle.is_none() {
            continue;
        }

        match saved.get(&(index as u32)) {
            Some(handle) => self.statics[index].handle = Some(*handle),
            None => {
                if let Some(collider) = build_collider(&self.statics[index].recipe, &self.layers, 1.0, collider_tag(0, index as u32)) {
                    self.statics[index].handle = Some(self.physics.insert_collider(collider, None));
                }
            }
        }
    }
}
```

In `insert_body`, after the body type is chosen:

```rust
let builder = builder
    .locked_axes(locked_axes(recipe.locks))
    .gravity_scale(recipe.gravity_scale)
    .linear_damping(recipe.linear_damping)
    .angular_damping(recipe.angular_damping);
```

```rust
fn snapshot(&mut self) -> usize {
    let handles = self
        .bodies
        .iter()
        .map(|(id, entry)| {
            let (index, generation) = entry.handle.into_raw_parts();
            (*id, index, generation)
        })
        .collect();
    let statics = self
        .statics
        .iter()
        .enumerate()
        .filter_map(|(index, slot)| {
            slot.handle.map(|handle| {
                let (raw, generation) = handle.into_raw_parts();
                (index as u32, raw, generation)
            })
        })
        .collect();
    let snapshot = SnapshotRef { format: SNAPSHOT_FORMAT, world: &self.physics, handles, statics };
    self.scratch = bincode::serialize(&snapshot).unwrap_or_default();
    self.scratch.len()
}
```

In `load`:

```rust
self.physics = snapshot.world;
self.restore_statics(&snapshot.statics);
```

In `collider_of`:

```rust
if body == 0 {
    return self.statics.get(index as usize).and_then(|slot| slot.handle);
}
```

```rust
fn apply_state(body: &mut RigidBody, state: &FrBodyState2D) {
    let translation = Vector::new(state.position[0], state.position[1]);
    let rotation = Rotation::new(state.rotation);
    body.set_translation(translation, true);
    body.set_rotation(rotation, true);
    if body.is_kinematic() {
        body.set_next_kinematic_translation(translation);
        body.set_next_kinematic_rotation(rotation);
    }

    let locked = body.locked_axes();
    let mut velocity = Vector::new(state.velocity[0], state.velocity[1]);
    if locked.contains(LockedAxes::TRANSLATION_LOCKED_X) {
        velocity.x = 0.0;
    }

    if locked.contains(LockedAxes::TRANSLATION_LOCKED_Y) {
        velocity.y = 0.0;
    }

    let angular_velocity = if locked.contains(LockedAxes::ROTATION_LOCKED_Z) { 0.0 } else { state.angular_velocity };
    body.set_linvel(velocity, true);
    body.set_angvel(angular_velocity, true);
}

fn locked_axes(locks: u32) -> LockedAxes {
    let mut axes = LockedAxes::empty();
    axes.set(LockedAxes::TRANSLATION_LOCKED_X, locks & LOCK_POSITION_X != 0);
    axes.set(LockedAxes::TRANSLATION_LOCKED_Y, locks & LOCK_POSITION_Y != 0);
    axes.set(LockedAxes::ROTATION_LOCKED_Z, locks & LOCK_ROTATION != 0);
    axes
}
```

```rust
pub unsafe extern "C" fn fr2_world_add_static(world_ptr: *mut World2D, colliders: *const FrCollider2D, count: u32, first: *mut u32) -> u32 {
    let added = match (world(world_ptr), read_colliders(colliders, count)) {
        (Some(world), Some(recipes)) => world.add_static(recipes),
        _ => None,
    };
    match (added, first.as_mut()) {
        (Some((group, index)), Some(out)) => {
            *out = index;
            group
        }
        _ => 0,
    }
}

pub unsafe extern "C" fn fr2_world_remove_static(world_ptr: *mut World2D, group: u32) -> bool {
    world(world_ptr).map_or(false, |world| world.remove_static(group))
}

pub unsafe extern "C" fn fr2_body_create(
    world_ptr: *mut World2D,
    kind: u32,
    x: f32,
    y: f32,
    rotation: f32,
    mass: f32,
    locks: u32,
    gravity_scale: f32,
    linear_damping: f32,
    angular_damping: f32,
    colliders: *const FrCollider2D,
    count: u32,
) -> u32 {
    let world = match world(world_ptr) {
        Some(world) => world,
        None => return 0,
    };
    if count == 0 {
        return 0;
    }

    match read_colliders(colliders, count) {
        Some(recipes) => world.create_body(BodyRecipe { kind: kind.min(KIND_STATIC), mass, locks, gravity_scale, linear_damping, angular_damping, colliders: recipes }, [x, y], rotation),
        None => 0,
    }
}
```

`native/fomoxa-rapier/src/world3d.rs`, the parts that differ from 2D:

```rust
const LOCK_POSITION_X: u32 = 1;
const LOCK_POSITION_Y: u32 = 2;
const LOCK_POSITION_Z: u32 = 4;
const LOCK_ROTATION_X: u32 = 8;
const LOCK_ROTATION_Y: u32 = 16;
const LOCK_ROTATION_Z: u32 = 32;

fn apply_state(body: &mut RigidBody, state: &FrBodyState) {
    let translation = Vector::new(state.position[0], state.position[1], state.position[2]);
    let rotation = Rotation::from_xyzw(state.rotation[0], state.rotation[1], state.rotation[2], state.rotation[3]);
    body.set_translation(translation, true);
    body.set_rotation(rotation, true);
    if body.is_kinematic() {
        body.set_next_kinematic_translation(translation);
        body.set_next_kinematic_rotation(rotation);
    }

    let locked = body.locked_axes();
    let mut velocity = Vector::new(state.velocity[0], state.velocity[1], state.velocity[2]);
    let mut angular_velocity = Vector::new(state.angular_velocity[0], state.angular_velocity[1], state.angular_velocity[2]);
    if locked.contains(LockedAxes::TRANSLATION_LOCKED_X) {
        velocity.x = 0.0;
    }

    if locked.contains(LockedAxes::TRANSLATION_LOCKED_Y) {
        velocity.y = 0.0;
    }

    if locked.contains(LockedAxes::TRANSLATION_LOCKED_Z) {
        velocity.z = 0.0;
    }

    if locked.contains(LockedAxes::ROTATION_LOCKED_X) {
        angular_velocity.x = 0.0;
    }

    if locked.contains(LockedAxes::ROTATION_LOCKED_Y) {
        angular_velocity.y = 0.0;
    }

    if locked.contains(LockedAxes::ROTATION_LOCKED_Z) {
        angular_velocity.z = 0.0;
    }

    body.set_linvel(velocity, true);
    body.set_angvel(angular_velocity, true);
}

fn locked_axes(locks: u32) -> LockedAxes {
    let mut axes = LockedAxes::empty();
    axes.set(LockedAxes::TRANSLATION_LOCKED_X, locks & LOCK_POSITION_X != 0);
    axes.set(LockedAxes::TRANSLATION_LOCKED_Y, locks & LOCK_POSITION_Y != 0);
    axes.set(LockedAxes::TRANSLATION_LOCKED_Z, locks & LOCK_POSITION_Z != 0);
    axes.set(LockedAxes::ROTATION_LOCKED_X, locks & LOCK_ROTATION_X != 0);
    axes.set(LockedAxes::ROTATION_LOCKED_Y, locks & LOCK_ROTATION_Y != 0);
    axes.set(LockedAxes::ROTATION_LOCKED_Z, locks & LOCK_ROTATION_Z != 0);
    axes
}

pub unsafe extern "C" fn fr_world_add_static(world_ptr: *mut World, colliders: *const FrCollider, count: u32, first: *mut u32) -> u32 {
    let added = match (world(world_ptr), read_colliders(colliders, count)) {
        (Some(world), Some(recipes)) => world.add_static(recipes),
        _ => None,
    };
    match (added, first.as_mut()) {
        (Some((group, index)), Some(out)) => {
            *out = index;
            group
        }
        _ => 0,
    }
}

pub unsafe extern "C" fn fr_world_remove_static(world_ptr: *mut World, group: u32) -> bool {
    world(world_ptr).map_or(false, |world| world.remove_static(group))
}

pub unsafe extern "C" fn fr_body_create(
    world_ptr: *mut World,
    kind: u32,
    position: *const f32,
    rotation: *const f32,
    mass: f32,
    locks: u32,
    gravity_scale: f32,
    linear_damping: f32,
    angular_damping: f32,
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
        BodyRecipe { kind: kind.min(KIND_STATIC), mass, locks, gravity_scale, linear_damping, angular_damping, colliders: recipes },
        [position[0], position[1], position[2]],
        [rotation[0], rotation[1], rotation[2], rotation[3]],
    )
}
```

`com.fomoxa.networking.rapier/Runtime/Rapier/RapierNative.cs`, changed declarations:

```csharp
[DllImport(Library)]
internal static extern uint fr_world_add_static(IntPtr world, Collider[] colliders, uint count, out uint first);

[DllImport(Library)]
[return: MarshalAs(UnmanagedType.U1)]
internal static extern bool fr_world_remove_static(IntPtr world, uint group);

[DllImport(Library)]
internal static extern uint fr_body_create(IntPtr world, uint kind, float[] position, float[] rotation, float mass, uint locks, float gravityScale, float linearDamping, float angularDamping, Collider[] colliders, uint count);

[DllImport(Library)]
internal static extern uint fr2_world_add_static(IntPtr world, Collider2D[] colliders, uint count, out uint first);

[DllImport(Library)]
[return: MarshalAs(UnmanagedType.U1)]
internal static extern bool fr2_world_remove_static(IntPtr world, uint group);

[DllImport(Library)]
internal static extern uint fr2_body_create(IntPtr world, uint kind, float x, float y, float rotation, float mass, uint locks, float gravityScale, float linearDamping, float angularDamping, Collider2D[] colliders, uint count);
```

`RapierWorld.cs`, changed members:

```csharp
public StaticGroup AddStatic(IReadOnlyList<ColliderDesc> colliders)
{
    if (colliders == null)
    {
        throw new ArgumentNullException(nameof(colliders));
    }

    uint group;
    uint first;
    using (var native = new RapierColliders(colliders))
    {
        group = RapierNative.fr_world_add_static(world, native.Native, (uint)native.Native.Length, out first);
    }

    if (group == 0)
    {
        throw new ArgumentException("Rapier refused a static collider (degenerate hull or mesh)", nameof(colliders));
    }

    return new StaticGroup(0, (int)group, (int)first, colliders.Count);
}

public void RemoveStatic(StaticGroup group) => RapierNative.fr_world_remove_static(world, (uint)group.Id);

public BodyHandle CreateBody(in BodyDesc desc)
{
    float[] position = { desc.Position.X, desc.Position.Y, desc.Position.Z };
    float[] rotation = { desc.Rotation.X, desc.Rotation.Y, desc.Rotation.Z, desc.Rotation.W };
    uint id;
    using (var native = new RapierColliders(desc.Colliders))
    {
        BodyMotion motion = desc.Motion;
        id = RapierNative.fr_body_create(world, (uint)desc.Kind, position, rotation, desc.Mass, (uint)motion.Locks, motion.UseGravity ? 1f : 0f, motion.LinearDamping, motion.AngularDamping, native.Native, (uint)native.Native.Length);
    }

    if (id == 0)
    {
        throw new ArgumentException("Rapier refused the body (no collider, degenerate hull or mesh, or a disposed world)", nameof(desc));
    }

    return new BodyHandle((int)id);
}
```

`RapierWorld2D.cs`, changed members:

```csharp
public StaticGroup AddStatic(IReadOnlyList<ColliderDesc2D> colliders)
{
    if (colliders == null)
    {
        throw new ArgumentNullException(nameof(colliders));
    }

    uint group;
    uint first;
    using (var native = new RapierColliders2D(colliders))
    {
        group = RapierNative.fr2_world_add_static(world, native.Native, (uint)native.Native.Length, out first);
    }

    if (group == 0)
    {
        throw new ArgumentException("Rapier refused a static 2D collider (degenerate polygon or polyline)", nameof(colliders));
    }

    return new StaticGroup(0, (int)group, (int)first, colliders.Count);
}

public void RemoveStatic(StaticGroup group) => RapierNative.fr2_world_remove_static(world, (uint)group.Id);

public BodyHandle CreateBody(in BodyDesc2D desc)
{
    uint id;
    using (var native = new RapierColliders2D(desc.Colliders))
    {
        BodyMotion2D motion = desc.Motion;
        id = RapierNative.fr2_body_create(world, (uint)desc.Kind, desc.Position.X, desc.Position.Y, desc.Rotation, desc.Mass, (uint)motion.Locks, motion.GravityScale, motion.LinearDamping, motion.AngularDamping, native.Native, (uint)native.Native.Length);
    }

    if (id == 0)
    {
        throw new ArgumentException("Rapier refused the 2D body (no collider, degenerate polygon, or a disposed world)", nameof(desc));
    }

    return new BodyHandle((int)id);
}
```

`RapierScenes.cs`, new fields and members:

```csharp
private readonly Dictionary<int, StaticEntry> statics = new Dictionary<int, StaticEntry>();
private readonly List<int> leavingStatics = new List<int>();
private readonly List<UnownedBody> unowned = new List<UnownedBody>();
private readonly List<UnownedBody2D> unowned2D = new List<UnownedBody2D>();
private int nextStatic = 1;

public void UnloadScene(uint sceneId)
{
    layers.Remove(sceneId);
    layers2D.Remove(sceneId);
    if (worlds.Remove(sceneId, out RapierWorld world))
    {
        histories.Remove(world);
        ForgetOwners(world);
        unowned.RemoveAll(entry => ReferenceEquals(entry.World, world));
        world.Dispose();
    }

    if (worlds2D.Remove(sceneId, out RapierWorld2D world2D))
    {
        histories.Remove(world2D);
        ForgetOwners(world2D);
        unowned2D.RemoveAll(entry => ReferenceEquals(entry.World, world2D));
        world2D.Dispose();
    }

    ForgetStatics(sceneId);
}

public StaticGroup AddStatic(uint sceneId, IReadOnlyList<ColliderDesc> colliders)
{
    RapierWorld world = WorldOf(sceneId);
    return Remember(new StaticEntry(sceneId, world, null, world.AddStatic(colliders)));
}

public StaticGroup AddStatic2D(uint sceneId, IReadOnlyList<ColliderDesc2D> colliders)
{
    RapierWorld2D world = WorldOf2D(sceneId);
    return Remember(new StaticEntry(sceneId, null, world, world.AddStatic(colliders)));
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

public PhysicsBody AddBody(uint sceneId, in BodyDesc body)
{
    RapierWorld world = WorldOf(sceneId);
    BodyHandle handle = world.CreateBody(body);
    var created = new PhysicsBody(world, handle);
    unowned.Add(new UnownedBody(world, handle, created));
    return created;
}

public PhysicsBody2D AddBody2D(uint sceneId, in BodyDesc2D body)
{
    RapierWorld2D world = WorldOf2D(sceneId);
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

private StaticGroup Remember(StaticEntry entry)
{
    int id = nextStatic++;
    statics.Add(id, entry);
    return new StaticGroup(entry.SceneId, id, entry.Inner.FirstIndex, entry.Inner.Count);
}

private void ForgetStatics(uint sceneId)
{
    leavingStatics.Clear();
    foreach (KeyValuePair<int, StaticEntry> entry in statics)
    {
        if (entry.Value.SceneId == sceneId)
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

private readonly struct StaticEntry
{
    public StaticEntry(uint sceneId, RapierWorld world, RapierWorld2D world2D, StaticGroup inner)
    {
        SceneId = sceneId;
        World = world;
        World2D = world2D;
        Inner = inner;
    }

    public uint SceneId { get; }

    public RapierWorld World { get; }

    public RapierWorld2D World2D { get; }

    public StaticGroup Inner { get; }
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

// Dispose also clears statics, unowned and unowned2D.
```

Libraries: `Tools/build-rapier.sh all` rebuilds `Runtime/Plugins/Linux/x86_64/libfomoxa_rapier.so` and `Runtime/Plugins/Windows/x86_64/fomoxa_rapier.dll`. The dependency tree is unchanged, so `Third Party Notices.md` stays as it is.

---

## 3. Tests

`tests/RapierWorld2DTest.cs`, added:

```csharp
[Test]
public void AStaticGroupHoldsABallUntilItIsRemovedAndItsIndicesAreNotReused()
{
    using (var world = new RapierWorld2D(Gravity))
    {
        StaticGroup ground = world.AddStatic(new[] { Ground() });
        BodyHandle ball = world.CreateBody(Circle(new Vector2(0f, 2f), 1f));

        Run(world, 120);

        Assert.IsTrue(ground.IsValid);
        Assert.AreEqual((0, 1), (ground.FirstIndex, ground.Count));
        Assert.AreEqual(0.5f, world.GetBody(ball).Position.Y, 0.05f);
        CollectionAssert.AreEqual(new[] { RapierCollider.Static(0) }, Touching(world, RapierCollider.OfBody(ball, 0)));

        world.RemoveStatic(ground);
        world.RemoveStatic(ground);
        Run(world, 60);

        Assert.Less(world.GetBody(ball).Position.Y, -1f);
        StaticGroup again = world.AddStatic(new[] { Ground() });
        StaticGroup empty = world.AddStatic(Array.Empty<ColliderDesc2D>());
        Assert.AreEqual((1, 1), (again.FirstIndex, again.Count));
        Assert.AreEqual((2, 0), (empty.FirstIndex, empty.Count));
        Assert.AreNotEqual(again.Id, ground.Id);
        Assert.AreNotEqual(empty.Id, again.Id);
    }
}

[Test]
public void LoadKeepsAStaticGroupAddedSinceTheSnapshotAndDropsOneRemovedSince()
{
    using (var world = new RapierWorld2D(Gravity))
    {
        PhysicsSnapshot before = world.CreateSnapshot();
        world.Save(before);
        StaticGroup ground = world.AddStatic(new[] { Ground() });

        world.Load(before);

        Assert.IsTrue(world.Raycast(new Vector2(0f, 5f), new Vector2(0f, -1f), 10f, out RayHit2D kept));
        Assert.AreEqual(0f, kept.Point.Y, 1e-4f);
        BodyHandle ball = world.CreateBody(Circle(new Vector2(0f, 2f), 1f));
        Run(world, 120);
        CollectionAssert.AreEqual(new[] { RapierCollider.Static(0) }, Touching(world, RapierCollider.OfBody(ball, 0)));

        PhysicsSnapshot withGround = world.CreateSnapshot();
        world.Save(withGround);
        world.RemoveStatic(ground);
        world.Load(withGround);

        Assert.IsFalse(world.Raycast(new Vector2(0f, 5f), new Vector2(0f, -10f), 3f, out _));
        Run(world, 60);
        Assert.Less(world.GetBody(ball).Position.Y, -1f);
    }
}

[Test]
public void TheMotionOfABodyLocksScalesGravityAndDamps()
{
    using (var world = new RapierWorld2D(Gravity))
    {
        BodyHandle free = world.CreateBody(Box(new Vector2(0f, 0f), BodyMotion2D.Default));
        BodyHandle heavy = world.CreateBody(Box(new Vector2(10f, 0f), new BodyMotion2D(BodyLocks2D.None, 1.5f, 0f, 0f)));
        BodyHandle upright = world.CreateBody(Box(new Vector2(20f, 0f), new BodyMotion2D(BodyLocks2D.Rotation | BodyLocks2D.PositionX, 1f, 0f, 0f)));
        BodyHandle damped = world.CreateBody(Box(new Vector2(30f, 0f), new BodyMotion2D(BodyLocks2D.None, 0f, 1f, 0f)));
        world.SetBody(upright, new BodyState2D { Position = new Vector2(20f, 0f), Velocity = new Vector2(5f, 0f), AngularVelocity = 4f });
        world.SetBody(free, new BodyState2D { AngularVelocity = 4f });
        world.SetBody(damped, new BodyState2D { Position = new Vector2(30f, 0f), Velocity = new Vector2(6f, 0f) });

        Run(world, 30);

        Assert.AreEqual(1.5f, world.GetBody(heavy).Position.Y / world.GetBody(free).Position.Y, 1e-3f);
        Assert.AreEqual(2f, world.GetBody(free).Rotation, 0.05f);
        Assert.AreEqual((20f, 0f), (world.GetBody(upright).Position.X, world.GetBody(upright).Rotation));
        Assert.AreEqual(world.GetBody(free).Position.Y, world.GetBody(upright).Position.Y, 1e-4f);
        Assert.AreEqual(0f, world.GetBody(damped).Position.Y);
        Assert.Less(world.GetBody(damped).Velocity.X, 6f * 0.65f);
        Assert.Greater(world.GetBody(damped).Velocity.X, 6f * 0.55f);
    }
}

[Test]
public void ABodyCreatedAfterTheSnapshotKeepsItsMotionWhenLoadPutsItBack()
{
    using (var world = new RapierWorld2D(Gravity))
    {
        PhysicsSnapshot before = world.CreateSnapshot();
        world.Save(before);
        BodyHandle upright = world.CreateBody(Box(Vector2.Zero, new BodyMotion2D(BodyLocks2D.Rotation, 0f, 0f, 0f)));
        world.SetBody(upright, new BodyState2D { AngularVelocity = 4f });

        world.Load(before);
        world.SetBody(upright, new BodyState2D { AngularVelocity = 4f });
        Run(world, 30);

        BodyState2D state = world.GetBody(upright);
        Assert.AreEqual((0f, 0f), (state.Rotation, state.Position.Y));
    }
}

private static ColliderDesc2D Ground() =>
    new ColliderDesc2D(BodyShape2D.Box(new Vector2(10f, 0.5f)), new Vector2(0f, -0.5f), 0f, ColliderMaterial.Default, 0, false);

private static BodyDesc2D Box(Vector2 position, BodyMotion2D motion) =>
    new BodyDesc2D(BodyKind.Dynamic, new[] { new ColliderDesc2D(BodyShape2D.Box(new Vector2(0.5f, 0.5f)), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false) }, position, 0f, 1f, motion);
```

`tests/RapierWorldTest.cs`, added:

```csharp
[Test]
public void AStaticGroupComesAndGoesAndLoadFollowsIt()
{
    using (var world = new RapierWorld(Gravity))
    {
        PhysicsSnapshot before = world.CreateSnapshot();
        world.Save(before);
        StaticGroup ground = world.AddStatic(new[] { Ground(0) });
        world.Load(before);
        BodyHandle ball = world.CreateBody(Ball(new Vector3(0f, 2f, 0f), 1f));

        Run(world, 120);

        Assert.AreEqual((0, 1), (ground.FirstIndex, ground.Count));
        Assert.AreEqual(0.5f, world.GetBody(ball).Position.Y, 0.05f);
        PhysicsSnapshot resting = world.CreateSnapshot();
        world.Save(resting);
        world.RemoveStatic(ground);
        world.Load(resting);
        Run(world, 60);

        Assert.Less(world.GetBody(ball).Position.Y, -1f);
        Assert.AreEqual(1, world.AddStatic(new[] { Ground(0) }).FirstIndex);
    }
}

[Test]
public void TheMotionOfABodyLocksAxesTurnsGravityOffAndDamps()
{
    using (var world = new RapierWorld(Gravity))
    {
        BodyHandle floating = world.CreateBody(Cube(new Vector3(0f, 0f, 0f), new BodyMotion(BodyLocks.None, false, 0f, 0f)));
        BodyHandle locked = world.CreateBody(Cube(new Vector3(10f, 0f, 0f), new BodyMotion(BodyLocks.PositionX | BodyLocks.RotationX | BodyLocks.RotationZ, true, 0f, 0f)));
        BodyHandle damped = world.CreateBody(Cube(new Vector3(20f, 0f, 0f), new BodyMotion(BodyLocks.None, false, 1f, 0f)));
        world.SetBody(locked, new BodyState { Position = new Vector3(10f, 0f, 0f), Rotation = Quaternion.Identity, Velocity = new Vector3(5f, 0f, 0f), AngularVelocity = new Vector3(3f, 3f, 3f) });
        world.SetBody(damped, new BodyState { Position = new Vector3(20f, 0f, 0f), Rotation = Quaternion.Identity, Velocity = new Vector3(6f, 0f, 0f) });

        Run(world, 30);

        Assert.AreEqual(Vector3.Zero, world.GetBody(floating).Position);
        BodyState state = world.GetBody(locked);
        Assert.AreEqual(10f, state.Position.X);
        Assert.Less(state.Position.Y, -1f);
        Vector3 axis = Vector3.Transform(Vector3.UnitY, state.Rotation);
        Assert.AreEqual(1f, axis.Y, 1e-4f);
        Assert.Less(world.GetBody(damped).Velocity.X, 6f * 0.65f);
        Assert.Greater(world.GetBody(damped).Velocity.X, 6f * 0.55f);
    }
}

private static BodyDesc Cube(Vector3 position, BodyMotion motion) =>
    new BodyDesc(BodyKind.Dynamic, new[] { new ColliderDesc(BodyShape.Box(new Vector3(0.5f)), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false) }, position, Quaternion.Identity, 1f, motion);
```

`tests/RapierScenesTest.cs`, added:

```csharp
[Test]
public void StaticGroupsContinueTheStaticIndicesOfTheSceneFileAndCanBeRemoved()
{
    using (var scenes = new RapierScenes())
    {
        scenes.LoadScene(Flatland(ArenaSceneId));
        StaticGroup ledge = scenes.AddStatic2D(ArenaSceneId, new[] { new ColliderDesc2D(BodyShape2D.Box(new Vector2(1f, 0.5f)), new Vector2(20f, 2f), 0f, ColliderMaterial.Default, 0, false) });
        StaticGroup floor = scenes.AddStatic(LobbySceneId, new[] { new ColliderDesc(BodyShape.Box(new Vector3(10f, 0.5f, 10f)), new Vector3(0f, -0.5f, 0f), Quaternion.Identity, ColliderMaterial.Default, 0, false) });
        PhysicsBody2D crate = scenes.AddBody2D(ArenaSceneId, new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Box(new Vector2(0.5f, 0.5f)), new Vector2(20f, 5f), 0f, 1f));

        Run(scenes, 120);

        Assert.AreEqual((ArenaSceneId, 1, 1), (ledge.SceneId, ledge.FirstIndex, ledge.Count));
        Assert.AreEqual((LobbySceneId, 0, 1), (floor.SceneId, floor.FirstIndex, floor.Count));
        Assert.AreNotEqual(ledge.Id, floor.Id);
        Assert.AreEqual(3f, crate.Position.Y, 0.05f);
        var touching = new List<RapierCollider>();
        scenes.WorldOf2D(ArenaSceneId).Touching(RapierCollider.Static(ledge.FirstIndex), touching);
        Assert.AreEqual(1, touching.Count);

        scenes.RemoveStatic(ledge);
        Run(scenes, 120);

        Assert.Less(crate.Position.Y, 1f);
    }
}

[Test]
public void UnownedBodiesBelongToNoEntityAndLeaveWithTheirScene()
{
    using (var scenes = new RapierScenes())
    {
        scenes.LoadScene(Flatland(ArenaSceneId));
        StaticGroup ledge = scenes.AddStatic2D(ArenaSceneId, new[] { new ColliderDesc2D(BodyShape2D.Box(Vector2.One), new Vector2(5f, 5f), 0f, ColliderMaterial.Default, 0, false) });
        PhysicsBody2D lift = scenes.AddBody2D(ArenaSceneId, new BodyDesc2D(BodyKind.Kinematic, BodyShape2D.Box(Vector2.One), new Vector2(-5f, 1f), 0f, 0f));
        PhysicsBody2D crate = scenes.AddBody2D(ArenaSceneId, new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Box(new Vector2(0.5f, 0.5f)), new Vector2(-8f, 1f), 0f, 1f));
        PhysicsBody ball = scenes.AddBody(LobbySceneId, Ball(Vector3.Zero));
        StandaloneEntity entity = Entity();
        scenes.AddBody(entity, ArenaSceneId, Ball(Vector3.Zero));
        var worlds = new List<IPhysicsSimulation>();
        var stepping = new List<IPhysicsSimulation>();

        scenes.WorldsOf(entity, worlds);
        scenes.WorldsToStep(stepping);
        lift.Position = new Vector2(-6f, 1f);
        scenes.RemoveBody2D(lift);
        scenes.RemoveBody2D(lift);

        CollectionAssert.AreEqual(new IPhysicsSimulation[] { scenes.WorldOf(ArenaSceneId) }, worlds);
        CollectionAssert.Contains(stepping, scenes.WorldOf(LobbySceneId));
        Assert.IsFalse(lift.IsValid);
        Assert.IsTrue(crate.IsValid);
        Assert.IsTrue(ball.IsValid);

        scenes.UnloadScene(ArenaSceneId);

        Assert.IsFalse(crate.IsValid);
        Assert.DoesNotThrow(() => scenes.RemoveStatic(ledge));
        Assert.DoesNotThrow(() => scenes.RemoveBody2D(crate));
        Assert.IsTrue(ball.IsValid);
    }
}
```

The standard state hashes of `APileReachesTheSameStateOnEveryPlatform` (3D and 2D) are unchanged: bodies without motion are built as before.
