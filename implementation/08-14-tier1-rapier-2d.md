# 08.14 Tier 1: the Rapier 2D world and `RapierWorld2D`

> Sub-step 08.14; the plan is in the 8b section of `implementation/08-prediction-physics.md` in the `unity` repository. The 2D part shares the crate, `lib.rs`, `hash.rs`, `contact.rs` and `RapierNative.cs` with 08.13; the listings of those files are in [`08-13-tier1-rapier-3d.md`](08-13-tier1-rapier-3d.md). The listings in this file show the current code, so they include the contact part (08.15). Tests: `dotnet test` 33/33 on Linux (.NET 8) and Windows (.NET 9).

| | Work | Status |
|---|---|:---:|
| 1 | `world2d.rs`: `rapier2d =0.36.0`, C ABI `fr2_*` built the same way as 3D | ✅ |
| 2 | 2D shapes: box, circle, capsule, convex polygon, polyline | ✅ |
| 3 | `RapierWorld2D : IPhysicsWorld2D` | ✅ |
| 4 | dotnet tests, 2D determinism hash | ✅ |

Rules: P27 (Q163 (4) A, (7) A; Q165 (1) A).

---

## 1. Overview

- `world2d.rs` has the same structure as `world3d.rs`, with 2D types. `FrCollider2D` carries a local pose made of a 2D position and an `f32` angle, and has no triangle indices. `FrBodyState2D` holds the position, the angle, the velocity and a scalar angular velocity; a ray result is an `FrRayHit2D`. Everything else matches 3D: bodies and their recipes, mass through density, clearing forces after a step, `bincode` snapshots with a format number, per-body `Load`, the hash, queries that go through every collider, collider `user_data` and `ActiveCollisionTypes`.
- Shape codes follow the order of the Core's `ShapeKind2D`. `Box` is built with `cuboid` and `Circle` with `ball`. `Capsule` is built with `capsule_y` along the vertical axis; for a horizontal capsule, the `Fomoxa.Unity` converter adds a quarter turn. `ConvexPolygon` is the `convex_hull` of the points, and a degenerate point set is refused. `Polyline` joins consecutive points and is only used on static bodies; for a static `PolygonCollider2D`, the converter closes the path by repeating the first point.
- 2D angles are radians around the Z axis, in the same direction as Unity.
- `RapierWorld2D` reports errors like `RapierWorld` does when the crate refuses something, when the world is disposed and when a snapshot belongs to another backend, but uses `BodyState2D`, `RayHit2D` and `Vector2` forces and impulses. It also has `StateHash`, `SetLayerCollisions`, `AddStatic`, `SetKind`, `IsDisposed` and `Touching` (08.15).
- The fixed hash of the 2D pile is `0x8DC0037A8661A2C5`, the same on Linux and Windows.

---

## 2. Code

`native/fomoxa-rapier/src/world2d.rs`:

```rust
use std::collections::{BTreeMap, BTreeSet};
use std::slice;

use rapier2d::prelude::*;
use serde::{Deserialize, Serialize};

use crate::contact::{collider_ref, collider_tag, write_refs, FrColliderRef};
use crate::hash::StableHasher;

const SNAPSHOT_FORMAT: u32 = 1;
const ALL_LAYERS: u32 = u32::MAX;

const KIND_DYNAMIC: u32 = 0;
const KIND_KINEMATIC: u32 = 1;
const KIND_STATIC: u32 = 2;

const SHAPE_BOX: u32 = 0;
const SHAPE_CIRCLE: u32 = 1;
const SHAPE_CAPSULE: u32 = 2;
const SHAPE_CONVEX_POLYGON: u32 = 3;
const SHAPE_POLYLINE: u32 = 4;

#[repr(C)]
#[derive(Clone, Copy)]
pub struct FrCollider2D {
    pub shape: u32,
    pub position: [f32; 2],
    pub rotation: f32,
    pub half_extents: [f32; 2],
    pub radius: f32,
    pub half_height: f32,
    pub points: *const f32,
    pub point_count: u32,
    pub friction: f32,
    pub restitution: f32,
    pub friction_combine: u32,
    pub restitution_combine: u32,
    pub layer: u32,
    pub is_trigger: u32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct FrBodyState2D {
    pub position: [f32; 2],
    pub rotation: f32,
    pub velocity: [f32; 2],
    pub angular_velocity: f32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct FrRayHit2D {
    pub body: u32,
    pub point: [f32; 2],
    pub normal: [f32; 2],
    pub distance: f32,
}

#[derive(Clone)]
enum ShapeRecipe {
    Box([f32; 2]),
    Circle(f32),
    Capsule { radius: f32, half_height: f32 },
    ConvexPolygon(Vec<Vector>),
    Polyline(Vec<Vector>),
}

#[derive(Clone)]
struct ColliderRecipe {
    shape: ShapeRecipe,
    position: [f32; 2],
    rotation: f32,
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

pub struct World2D {
    physics: PhysicsWorld,
    bodies: BTreeMap<u32, BodyEntry>,
    next_id: u32,
    layers: [u32; 32],
    statics: Vec<ColliderHandle>,
    scratch: Vec<u8>,
}

impl World2D {
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

    fn create_body(&mut self, recipe: BodyRecipe, position: [f32; 2], rotation: f32) -> u32 {
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

    fn insert_body(&mut self, id: u32, recipe: &BodyRecipe, position: [f32; 2], rotation: f32) -> Option<RigidBodyHandle> {
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

    fn state(&self, id: u32) -> Option<FrBodyState2D> {
        self.body(id).map(state_of)
    }

    fn set_state(&mut self, id: u32, state: &FrBodyState2D) -> bool {
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

        let current: BTreeMap<u32, (FrBodyState2D, u32)> = self
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
                for value in state.position.iter().chain(&[state.rotation]).chain(&state.velocity).chain(&[state.angular_velocity]) {
                    hasher.write_f32(*value);
                }
            }
        }

        hasher.finish()
    }

    fn raycast(&self, origin: Vector, direction: Vector, max_distance: f32) -> Option<FrRayHit2D> {
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
        Some(FrRayHit2D {
            body: self.owner_of(collider),
            point: [point.x, point.y],
            normal: [hit.normal.x, hit.normal.y],
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
            if rapier2d::parry::query::intersection_test(&ball_pose, &ball, &pose, collider.shape()).is_ok_and(|hit| hit.intersecting) {
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

fn pose(position: [f32; 2], rotation: f32) -> Pose {
    Pose::from_parts(Vector::new(position[0], position[1]), Rotation::new(rotation))
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

fn state_of(body: &RigidBody) -> FrBodyState2D {
    let translation = body.translation();
    let velocity = body.linvel();
    FrBodyState2D {
        position: [translation.x, translation.y],
        rotation: body.rotation().angle(),
        velocity: [velocity.x, velocity.y],
        angular_velocity: body.angvel(),
    }
}

fn apply_state(body: &mut RigidBody, state: &FrBodyState2D) {
    let translation = Vector::new(state.position[0], state.position[1]);
    let rotation = Rotation::new(state.rotation);
    body.set_translation(translation, true);
    body.set_rotation(rotation, true);
    if body.is_kinematic() {
        body.set_next_kinematic_translation(translation);
        body.set_next_kinematic_rotation(rotation);
    }

    body.set_linvel(Vector::new(state.velocity[0], state.velocity[1]), true);
    body.set_angvel(state.angular_velocity, true);
}

fn build_collider(recipe: &ColliderRecipe, layers: &[u32; 32], density: f32, tag: u128) -> Option<Collider> {
    let builder = match &recipe.shape {
        ShapeRecipe::Box(half) => ColliderBuilder::cuboid(half[0], half[1]),
        ShapeRecipe::Circle(radius) => ColliderBuilder::ball(*radius),
        ShapeRecipe::Capsule { radius, half_height } => ColliderBuilder::capsule_y(*half_height, *radius),
        ShapeRecipe::ConvexPolygon(points) => ColliderBuilder::convex_hull(points)?,
        ShapeRecipe::Polyline(points) => ColliderBuilder::polyline(points.clone(), None),
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

unsafe fn read_colliders(colliders: *const FrCollider2D, count: u32) -> Option<Vec<ColliderRecipe>> {
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
            SHAPE_CIRCLE => ShapeRecipe::Circle(collider.radius),
            SHAPE_CAPSULE => ShapeRecipe::Capsule { radius: collider.radius, half_height: collider.half_height },
            SHAPE_CONVEX_POLYGON => ShapeRecipe::ConvexPolygon(read_points(collider.points, collider.point_count, 3)?),
            SHAPE_POLYLINE => ShapeRecipe::Polyline(read_points(collider.points, collider.point_count, 2)?),
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

unsafe fn read_points(points: *const f32, count: u32, minimum: u32) -> Option<Vec<Vector>> {
    if count < minimum || points.is_null() {
        return None;
    }

    let values = slice::from_raw_parts(points, count as usize * 2);
    Some(values.chunks_exact(2).map(|point| Vector::new(point[0], point[1])).collect())
}

unsafe fn world<'a>(world: *mut World2D) -> Option<&'a mut World2D> {
    world.as_mut()
}

#[no_mangle]
pub extern "C" fn fr2_world_create(gravity_x: f32, gravity_y: f32) -> *mut World2D {
    Box::into_raw(Box::new(World2D::new(Vector::new(gravity_x, gravity_y))))
}

#[no_mangle]
pub unsafe extern "C" fn fr2_world_destroy(world: *mut World2D) {
    if !world.is_null() {
        drop(Box::from_raw(world));
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr2_world_set_layers(world_ptr: *mut World2D, masks: *const u32) -> bool {
    match (world(world_ptr), masks.is_null()) {
        (Some(world), false) => {
            world.layers.copy_from_slice(slice::from_raw_parts(masks, 32));
            true
        }
        _ => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr2_world_step(world_ptr: *mut World2D, seconds: f32) {
    if let Some(world) = world(world_ptr) {
        world.step(seconds);
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr2_world_add_static(world_ptr: *mut World2D, colliders: *const FrCollider2D, count: u32) -> bool {
    match (world(world_ptr), read_colliders(colliders, count)) {
        (Some(world), Some(recipes)) => world.add_static(recipes),
        _ => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr2_body_create(
    world_ptr: *mut World2D,
    kind: u32,
    x: f32,
    y: f32,
    rotation: f32,
    mass: f32,
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
        Some(recipes) => world.create_body(BodyRecipe { kind: kind.min(KIND_STATIC), mass, colliders: recipes }, [x, y], rotation),
        None => 0,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr2_body_remove(world_ptr: *mut World2D, body: u32) -> bool {
    world(world_ptr).map_or(false, |world| world.remove_body(body))
}

#[no_mangle]
pub unsafe extern "C" fn fr2_body_contains(world_ptr: *mut World2D, body: u32) -> bool {
    world(world_ptr).map_or(false, |world| world.body(body).is_some())
}

#[no_mangle]
pub unsafe extern "C" fn fr2_body_get(world_ptr: *mut World2D, body: u32, state: *mut FrBodyState2D) -> bool {
    match (world(world_ptr).and_then(|world| world.state(body)), state.as_mut()) {
        (Some(found), Some(out)) => {
            *out = found;
            true
        }
        _ => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr2_body_set(world_ptr: *mut World2D, body: u32, state: *const FrBodyState2D) -> bool {
    match (world(world_ptr), state.as_ref()) {
        (Some(world), Some(state)) => world.set_state(body, state),
        _ => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr2_body_kind(world_ptr: *mut World2D, body: u32) -> i32 {
    world(world_ptr).and_then(|world| world.kind(body)).map_or(-1, |kind| kind as i32)
}

#[no_mangle]
pub unsafe extern "C" fn fr2_body_set_kind(world_ptr: *mut World2D, body: u32, kind: u32) -> bool {
    world(world_ptr).map_or(false, |world| world.set_kind(body, kind))
}

#[no_mangle]
pub unsafe extern "C" fn fr2_body_mass(world_ptr: *mut World2D, body: u32) -> f32 {
    world(world_ptr).and_then(|world| world.body(body)).map_or(0.0, |body| body.mass())
}

#[no_mangle]
pub unsafe extern "C" fn fr2_body_set_rewindable(world_ptr: *mut World2D, body: u32, rewindable: bool) -> bool {
    match world(world_ptr).and_then(|world| world.bodies.get_mut(&body)) {
        Some(entry) => {
            entry.rewindable = rewindable;
            true
        }
        None => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr2_body_add_force(world_ptr: *mut World2D, body: u32, x: f32, y: f32) -> bool {
    match world(world_ptr).and_then(|world| world.body_mut(body)) {
        Some(body) => {
            body.add_force(Vector::new(x, y), true);
            true
        }
        None => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr2_body_add_impulse(world_ptr: *mut World2D, body: u32, x: f32, y: f32) -> bool {
    match world(world_ptr).and_then(|world| world.body_mut(body)) {
        Some(body) => {
            body.apply_impulse(Vector::new(x, y), true);
            true
        }
        None => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr2_world_raycast(
    world_ptr: *mut World2D,
    origin_x: f32,
    origin_y: f32,
    direction_x: f32,
    direction_y: f32,
    max_distance: f32,
    hit: *mut FrRayHit2D,
) -> bool {
    let found = world(world_ptr)
        .and_then(|world| world.raycast(Vector::new(origin_x, origin_y), Vector::new(direction_x, direction_y), max_distance));
    match (found, hit.as_mut()) {
        (Some(found), Some(out)) => {
            *out = found;
            true
        }
        _ => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr2_world_overlap(world_ptr: *mut World2D, x: f32, y: f32, radius: f32, bodies: *mut u32, capacity: u32) -> u32 {
    let found = match world(world_ptr) {
        Some(world) => world.overlap(Vector::new(x, y), radius),
        None => return 0,
    };
    let count = found.len().min(capacity as usize);
    if count > 0 && !bodies.is_null() {
        slice::from_raw_parts_mut(bodies, count).copy_from_slice(&found[..count]);
    }

    count as u32
}

#[no_mangle]
pub unsafe extern "C" fn fr2_collider_touching(world_ptr: *mut World2D, body: u32, index: u32, out: *mut FrColliderRef, capacity: u32) -> u32 {
    match world(world_ptr) {
        Some(world) => write_refs(&world.touching(body, index), out, capacity),
        None => 0,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr2_world_snapshot(world_ptr: *mut World2D) -> u32 {
    world(world_ptr).map_or(0, |world| world.snapshot() as u32)
}

#[no_mangle]
pub unsafe extern "C" fn fr2_world_snapshot_copy(world_ptr: *mut World2D, bytes: *mut u8, length: u32) -> bool {
    match world(world_ptr) {
        Some(world) if !bytes.is_null() && length as usize == world.scratch.len() => {
            slice::from_raw_parts_mut(bytes, length as usize).copy_from_slice(&world.scratch);
            true
        }
        _ => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr2_world_load(world_ptr: *mut World2D, bytes: *const u8, length: u32) -> bool {
    match world(world_ptr) {
        Some(world) if !bytes.is_null() && length > 0 => world.load(slice::from_raw_parts(bytes, length as usize)),
        _ => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr2_world_hash(world_ptr: *mut World2D) -> u64 {
    world(world_ptr).map_or(0, |world| world.hash())
}
```

`com.fomoxa.networking.rapier/Runtime/Rapier/RapierColliders2D.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Rapier
{
    internal sealed class RapierColliders2D : IDisposable
    {
        private readonly List<GCHandle> pinned = new List<GCHandle>();

        public RapierColliders2D(IReadOnlyList<ColliderDesc2D> colliders)
        {
            Native = new RapierNative.Collider2D[colliders.Count];
            for (int index = 0; index < colliders.Count; index++)
            {
                Native[index] = Convert(colliders[index]);
            }
        }

        public RapierNative.Collider2D[] Native { get; }

        public void Dispose()
        {
            foreach (GCHandle handle in pinned)
            {
                handle.Free();
            }

            pinned.Clear();
        }

        private RapierNative.Collider2D Convert(in ColliderDesc2D collider)
        {
            BodyShape2D shape = collider.Shape;
            ColliderMaterial material = collider.Material;
            var native = new RapierNative.Collider2D
            {
                Shape = (uint)shape.Kind,
                PositionX = collider.Position.X,
                PositionY = collider.Position.Y,
                Rotation = collider.Rotation,
                HalfExtentsX = shape.HalfExtents.X,
                HalfExtentsY = shape.HalfExtents.Y,
                Radius = shape.Radius,
                HalfHeight = shape.HalfHeight,
                Friction = material.Friction,
                Restitution = material.Restitution,
                FrictionCombine = (uint)material.FrictionCombine,
                RestitutionCombine = (uint)material.RestitutionCombine,
                Layer = (uint)collider.Layer,
                IsTrigger = collider.IsTrigger ? 1u : 0u,
            };
            IReadOnlyList<Vector2> points = shape.Points;
            if (points.Count > 0)
            {
                var flat = new float[points.Count * 2];
                for (int index = 0; index < points.Count; index++)
                {
                    flat[index * 2] = points[index].X;
                    flat[index * 2 + 1] = points[index].Y;
                }

                GCHandle handle = GCHandle.Alloc(flat, GCHandleType.Pinned);
                pinned.Add(handle);
                native.Points = handle.AddrOfPinnedObject();
                native.PointCount = (uint)points.Count;
            }

            return native;
        }
    }
}
```

`com.fomoxa.networking.rapier/Runtime/Rapier/RapierWorld2D.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Rapier
{
    public sealed class RapierWorld2D : IPhysicsWorld2D, IRapierBodies, IDisposable
    {
        private const int LayerCount = 32;

        private IntPtr world;
        private uint[] overlapped = new uint[64];
        private RapierNative.ColliderRef[] touched = new RapierNative.ColliderRef[16];

        public RapierWorld2D(Vector2 gravity)
        {
            world = RapierNative.fr2_world_create(gravity.X, gravity.Y);
        }

        public PhysicsBackend Backend => PhysicsBackend.Rapier;

        public bool IsDisposed => world == IntPtr.Zero;

        public ulong StateHash => RapierNative.fr2_world_hash(world);

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

            RapierNative.fr2_world_set_layers(world, copy);
        }

        public void AddStatic(IReadOnlyList<ColliderDesc2D> colliders)
        {
            if (colliders == null)
            {
                throw new ArgumentNullException(nameof(colliders));
            }

            if (colliders.Count == 0)
            {
                return;
            }

            using (var native = new RapierColliders2D(colliders))
            {
                if (!RapierNative.fr2_world_add_static(world, native.Native, (uint)native.Native.Length))
                {
                    throw new ArgumentException("Rapier refused a static 2D collider (degenerate polygon or polyline)", nameof(colliders));
                }
            }
        }

        public void Step(float seconds) => RapierNative.fr2_world_step(world, seconds);

        public PhysicsSnapshot CreateSnapshot() => new RapierSnapshot();

        public void Save(PhysicsSnapshot into)
        {
            RapierSnapshot snapshot = Expect(into);
            uint length = RapierNative.fr2_world_snapshot(world);
            snapshot.Resize((int)length);
            if (!RapierNative.fr2_world_snapshot_copy(world, snapshot.Bytes, length))
            {
                throw new InvalidOperationException("the 2D Rapier world could not copy its snapshot");
            }
        }

        public void Load(PhysicsSnapshot from)
        {
            RapierSnapshot snapshot = Expect(from);
            if (snapshot.Bytes.Length == 0 || !RapierNative.fr2_world_load(world, snapshot.Bytes, (uint)snapshot.Bytes.Length))
            {
                throw new ArgumentException("the snapshot does not hold a 2D Rapier world of this build", nameof(from));
            }
        }

        public BodyHandle CreateBody(in BodyDesc2D desc)
        {
            uint id;
            using (var native = new RapierColliders2D(desc.Colliders))
            {
                id = RapierNative.fr2_body_create(world, (uint)desc.Kind, desc.Position.X, desc.Position.Y, desc.Rotation, desc.Mass, native.Native, (uint)native.Native.Length);
            }

            if (id == 0)
            {
                throw new ArgumentException("Rapier refused the 2D body (no collider, degenerate polygon, or a disposed world)", nameof(desc));
            }

            return new BodyHandle((int)id);
        }

        public bool RemoveBody(BodyHandle body) => body.IsValid && RapierNative.fr2_body_remove(world, (uint)body.Value);

        public bool Contains(BodyHandle body) => body.IsValid && RapierNative.fr2_body_contains(world, (uint)body.Value);

        public BodyState2D GetBody(BodyHandle body)
        {
            if (!RapierNative.fr2_body_get(world, Id(body), out RapierNative.BodyState2D state))
            {
                throw Missing(body);
            }

            return new BodyState2D
            {
                Position = new Vector2(state.PositionX, state.PositionY),
                Rotation = state.Rotation,
                Velocity = new Vector2(state.VelocityX, state.VelocityY),
                AngularVelocity = state.AngularVelocity,
            };
        }

        public BodyKind GetKind(BodyHandle body)
        {
            int kind = RapierNative.fr2_body_kind(world, Id(body));
            if (kind < 0)
            {
                throw Missing(body);
            }

            return (BodyKind)kind;
        }

        public float GetMass(BodyHandle body)
        {
            Require(body);
            return RapierNative.fr2_body_mass(world, (uint)body.Value);
        }

        public void SetBody(BodyHandle body, in BodyState2D state)
        {
            var native = new RapierNative.BodyState2D
            {
                PositionX = state.Position.X,
                PositionY = state.Position.Y,
                Rotation = state.Rotation,
                VelocityX = state.Velocity.X,
                VelocityY = state.Velocity.Y,
                AngularVelocity = state.AngularVelocity,
            };
            if (!RapierNative.fr2_body_set(world, Id(body), native))
            {
                throw Missing(body);
            }
        }

        public void SetKind(BodyHandle body, BodyKind kind)
        {
            if (!RapierNative.fr2_body_set_kind(world, Id(body), (uint)kind))
            {
                throw Missing(body);
            }
        }

        public void SetRewindable(BodyHandle body, bool rewindable)
        {
            if (!RapierNative.fr2_body_set_rewindable(world, Id(body), rewindable))
            {
                throw Missing(body);
            }
        }

        public void AddForce(BodyHandle body, Vector2 force)
        {
            if (!RapierNative.fr2_body_add_force(world, Id(body), force.X, force.Y))
            {
                throw Missing(body);
            }
        }

        public void AddImpulse(BodyHandle body, Vector2 impulse)
        {
            if (!RapierNative.fr2_body_add_impulse(world, Id(body), impulse.X, impulse.Y))
            {
                throw Missing(body);
            }
        }

        public bool Raycast(Vector2 origin, Vector2 direction, float maxDistance, out RayHit2D hit)
        {
            if (!RapierNative.fr2_world_raycast(world, origin.X, origin.Y, direction.X, direction.Y, maxDistance, out RapierNative.RayHit2D found))
            {
                hit = default;
                return false;
            }

            hit = new RayHit2D(new BodyHandle((int)found.Body), new Vector2(found.PointX, found.PointY), new Vector2(found.NormalX, found.NormalY), found.Distance);
            return true;
        }

        public int Overlap(Vector2 center, float radius, BodyHandle[] results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (overlapped.Length < results.Length)
            {
                overlapped = new uint[results.Length];
            }

            int count = (int)RapierNative.fr2_world_overlap(world, center.X, center.Y, radius, overlapped, (uint)results.Length);
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

            uint count = RapierNative.fr2_collider_touching(world, Id(collider.Body), (uint)collider.Index, touched, (uint)touched.Length);
            if (count > touched.Length)
            {
                touched = new RapierNative.ColliderRef[count];
                count = RapierNative.fr2_collider_touching(world, Id(collider.Body), (uint)collider.Index, touched, (uint)touched.Length);
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
                RapierNative.fr2_world_destroy(world);
                world = IntPtr.Zero;
            }
        }

        private static RapierSnapshot Expect(PhysicsSnapshot snapshot) =>
            snapshot as RapierSnapshot ?? throw new ArgumentException("the snapshot was not created by a 2D Rapier world", nameof(snapshot));

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

---

## 3. Tests

`tests/RapierWorld2DTest.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;

namespace Fomoxa.Networking.Rapier.Tests
{
    public sealed class RapierWorld2DTest
    {
        private const float Tick = 1f / 60f;
        private const ulong PileStateHash = 0x8DC0037A8661A2C5UL;
        private static readonly Vector2 Gravity = new Vector2(0f, -9.81f);

        [Test]
        public void ACircleFallsOntoAPolylineAndHasTheGivenMass()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                world.AddStatic(new[] { Line(0) });
                BodyHandle ball = world.CreateBody(Circle(new Vector2(0f, 5f), 3f));

                Run(world, 300);

                Assert.AreEqual(0.5f, world.GetBody(ball).Position.Y, 0.05f);
                Assert.AreEqual(3f, world.GetMass(ball), 1e-4f);
                Assert.AreEqual(BodyKind.Dynamic, world.GetKind(ball));
            }
        }

        [Test]
        public void ABodyOfConvexPiecesLandsOnABox()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                world.AddStatic(new[] { new ColliderDesc2D(BodyShape2D.Box(new Vector2(10f, 0.5f)), new Vector2(0f, -0.5f), 0f, ColliderMaterial.Default, 0, false) });
                ColliderDesc2D[] pieces =
                {
                    new ColliderDesc2D(BodyShape2D.ConvexPolygon(new[] { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 1f), new Vector2(0f, 1f) }), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false),
                    new ColliderDesc2D(BodyShape2D.ConvexPolygon(new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 2f), new Vector2(0f, 2f) }), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false),
                };
                BodyHandle shape = world.CreateBody(new BodyDesc2D(BodyKind.Dynamic, pieces, new Vector2(-1f, 3f), 0f, 3f));

                Run(world, 300);

                BodyState2D state = world.GetBody(shape);
                Assert.AreEqual(0f, state.Position.Y, 0.05f);
                Assert.AreEqual(0f, state.Rotation, 1e-3f);
                Assert.AreEqual(3f, world.GetMass(shape), 1e-4f);
            }
        }

        [Test]
        public void AForceLastsOneStepAndAnImpulseAndASpinChangeTheBodyAtOnce()
        {
            using (var world = new RapierWorld2D(Vector2.Zero))
            {
                BodyHandle ball = world.CreateBody(Circle(Vector2.Zero, 2f));

                world.AddImpulse(ball, new Vector2(4f, 0f));
                float afterImpulse = world.GetBody(ball).Velocity.X;
                world.AddForce(ball, new Vector2(120f, 0f));
                world.Step(Tick);
                float afterForce = world.GetBody(ball).Velocity.X;
                world.SetBody(ball, new BodyState2D { Position = Vector2.Zero, Rotation = 0f, AngularVelocity = 6f });
                Run(world, 15);

                Assert.AreEqual(2f, afterImpulse, 1e-4f);
                Assert.AreEqual(3f, afterForce, 1e-4f);
                Assert.AreEqual(1.5f, world.GetBody(ball).Rotation, 1e-3f);
            }
        }

        [Test]
        public void LoadingASnapshotReplaysTheSameStepsAndKeepsBodiesCreatedSince()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                world.AddStatic(new[] { Line(0) });
                BodyHandle removed = default;
                for (int index = 0; index < 5; index++)
                {
                    removed = world.CreateBody(Circle(new Vector2(index * 0.3f, 2f + index), 1f));
                }

                Run(world, 30);
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);
                Run(world, 60);
                ulong first = world.StateHash;
                world.Load(snapshot);
                Run(world, 60);
                ulong replayed = world.StateHash;
                world.RemoveBody(removed);
                BodyHandle added = world.CreateBody(Circle(new Vector2(8f, 1f), 1f));

                world.Load(snapshot);

                Assert.AreEqual(first, replayed);
                Assert.IsFalse(world.Contains(removed));
                Assert.IsTrue(world.Contains(added));
                Assert.AreEqual(8f, world.GetBody(added).Position.X, 1e-4f);
            }
        }

        [Test]
        public void APileReachesTheSameStateOnEveryPlatform()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                world.AddStatic(new[] { Line(0) });
                for (int index = 0; index < 20; index++)
                {
                    world.CreateBody(new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Box(new Vector2(0.4f)), new Vector2(index % 4 * 0.5f, 1f + index * 0.9f), index * 0.3f, 1f));
                }

                Run(world, 240);

                TestContext.Out.WriteLine($"2D pile state hash 0x{world.StateHash:X16}");
                Assert.AreEqual(PileStateHash, world.StateHash);
            }
        }

        [Test]
        public void LayersAndTriggersLetBodiesThroughAndQueriesFindBodies()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                var masks = new uint[32];
                for (int layer = 0; layer < 32; layer++)
                {
                    masks[layer] = uint.MaxValue;
                }

                masks[1] &= ~(1u << 2);
                masks[2] &= ~(1u << 1);
                world.SetLayerCollisions(masks);
                world.AddStatic(new[] { Line(1), new ColliderDesc2D(BodyShape2D.Box(new Vector2(1f, 0.5f)), new Vector2(30f, -0.5f), 0f, ColliderMaterial.Default, 0, true) });
                BodyHandle ghost = world.CreateBody(Circle(new Vector2(0f, 2f), 1f, layer: 2));
                BodyHandle solid = world.CreateBody(Circle(new Vector2(3f, 2f), 1f, layer: 3));
                BodyHandle triggered = world.CreateBody(Circle(new Vector2(30f, 2f), 1f));
                var found = new BodyHandle[4];

                Assert.IsTrue(world.Raycast(new Vector2(3f, 10f), new Vector2(0f, -1f), 100f, out RayHit2D hit));
                int count = world.Overlap(new Vector2(3f, 2f), 0.1f, found);
                Run(world, 120);

                Assert.AreEqual((solid.Value, 2.5f), (hit.Body.Value, hit.Point.Y));
                Assert.AreEqual(1, count);
                Assert.AreEqual(solid.Value, found[0].Value);
                Assert.Less(world.GetBody(ghost).Position.Y, -1f);
                Assert.AreEqual(0.5f, world.GetBody(solid).Position.Y, 0.05f);
                Assert.Less(world.GetBody(triggered).Position.Y, -1f);
            }
        }

        [Test]
        public void ADisposedWorldHasNoBodiesAndRefusesNewOnes()
        {
            var world = new RapierWorld2D(Gravity);
            BodyHandle ball = world.CreateBody(Circle(Vector2.Zero, 1f));

            world.Dispose();

            Assert.IsFalse(world.Contains(ball));
            Assert.IsFalse(new PhysicsBody2D(world, ball).IsValid);
            Assert.Throws<ArgumentException>(() => world.CreateBody(Circle(Vector2.Zero, 1f)));
        }

        [Test]
        public void TouchingSetsFollowContactsSensorsKinematicBodiesAndLoad()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                world.AddStatic(new[] { Line(0), new ColliderDesc2D(BodyShape2D.Box(new Vector2(1f, 1f)), new Vector2(20f, 5f), 0f, ColliderMaterial.Default, 0, true) });
                BodyHandle ball = world.CreateBody(Circle(new Vector2(0f, 2f), 1f));
                BodyHandle carried = world.CreateBody(new BodyDesc2D(BodyKind.Kinematic, BodyShape2D.Circle(0.5f), new Vector2(20.5f, 5f), 0f, 0f));

                Run(world, 120);

                CollectionAssert.AreEqual(new[] { RapierCollider.Static(0) }, Touching(world, RapierCollider.OfBody(ball, 0)));
                CollectionAssert.AreEqual(new[] { RapierCollider.OfBody(carried, 0) }, Touching(world, RapierCollider.Static(1)));

                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);
                world.SetBody(carried, new BodyState2D { Position = new Vector2(40f, 5f) });
                Run(world, 2);

                CollectionAssert.IsEmpty(Touching(world, RapierCollider.Static(1)));

                world.Load(snapshot);

                CollectionAssert.AreEqual(new[] { RapierCollider.OfBody(carried, 0) }, Touching(world, RapierCollider.Static(1)));
            }
        }

        private static List<RapierCollider> Touching(RapierWorld2D world, RapierCollider collider)
        {
            var touching = new List<RapierCollider>();
            world.Touching(collider, touching);
            return touching;
        }

        private static void Run(RapierWorld2D world, int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                world.Step(Tick);
            }
        }

        private static ColliderDesc2D Line(int layer) =>
            new ColliderDesc2D(BodyShape2D.Polyline(new[] { new Vector2(-10f, 0f), new Vector2(10f, 0f) }), Vector2.Zero, 0f, ColliderMaterial.Default, layer, false);

        private static BodyDesc2D Circle(Vector2 position, float mass, int layer = 0) =>
            new BodyDesc2D(BodyKind.Dynamic, new[] { new ColliderDesc2D(BodyShape2D.Circle(0.5f), Vector2.Zero, 0f, ColliderMaterial.Default, layer, false) }, position, 0f, mass);
    }
}
```

| Test | Checks |
|---|---|
| `ACircleFallsOntoAPolylineAndHasTheGivenMass` | A circle falls onto a static polyline; its mass is right |
| `ABodyOfConvexPiecesLandsOnABox` | A body of several convex polygons lands on a box and rests level; its mass is right |
| `AForceLastsOneStepAndAnImpulseAndASpinChangeTheBodyAtOnce` | A force lasts one step; an impulse and an angular velocity change the body at once; the angle after 15 ticks |
| `LoadingASnapshotReplaysTheSameStepsAndKeepsBodiesCreatedSince` | A replay gives the same hash; a body removed after the snapshot does not come back; a body created after it keeps its state |
| `APileReachesTheSameStateOnEveryPlatform` | A fixed hash across platforms |
| `LayersAndTriggersLetBodiesThroughAndQueriesFindBodies` | Layer matrix, triggers, `Raycast`, `Overlap` |
| `ADisposedWorldHasNoBodiesAndRefusesNewOnes` | A disposed world |
| `TouchingSetsFollowContactsSensorsKinematicBodiesAndLoad` | 08.15: the contact set of a circle resting on a polyline and of a kinematic body inside a static sensor; `Load` brings the contact set back |
