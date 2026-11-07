use std::collections::{BTreeMap, BTreeSet};
use std::slice;

use rapier3d::parry::query::{self, ShapeCastOptions};
use rapier3d::prelude::*;
use serde::{Deserialize, Serialize};

use crate::contact::{collider_ref, collider_tag, write_items, write_refs, FrColliderRef};
use crate::hash::StableHasher;
use crate::query::FrQueryFilter;

const SNAPSHOT_FORMAT: u32 = 3;
const ALL_LAYERS: u32 = u32::MAX;

const LOCK_POSITION_X: u32 = 1;
const LOCK_POSITION_Y: u32 = 2;
const LOCK_POSITION_Z: u32 = 4;
const LOCK_ROTATION_X: u32 = 8;
const LOCK_ROTATION_Y: u32 = 16;
const LOCK_ROTATION_Z: u32 = 32;

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
pub struct FrContactPoint {
    pub point: [f32; 3],
    pub normal: [f32; 3],
    pub depth: f32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct FrRayHit {
    pub body: u32,
    pub point: [f32; 3],
    pub normal: [f32; 3],
    pub distance: f32,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct FrQueryShape {
    pub shape: u32,
    pub half_extents: [f32; 3],
    pub radius: f32,
    pub half_height: f32,
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
    locks: u32,
    gravity_scale: f32,
    linear_damping: f32,
    angular_damping: f32,
    ccd: bool,
    colliders: Vec<ColliderRecipe>,
}

struct StaticSlot {
    group: u32,
    recipe: ColliderRecipe,
    handle: Option<ColliderHandle>,
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
    statics: Vec<(u32, u32, u32)>,
    dirty: Vec<(u32, u32)>,
}

#[derive(Deserialize)]
struct Snapshot {
    format: u32,
    world: PhysicsWorld,
    handles: Vec<(u32, u32, u32)>,
    statics: Vec<(u32, u32, u32)>,
    dirty: Vec<(u32, u32)>,
}

pub struct World {
    physics: PhysicsWorld,
    bodies: BTreeMap<u32, BodyEntry>,
    next_id: u32,
    layers: [u32; 32],
    statics: Vec<StaticSlot>,
    next_group: u32,
    scratch: Vec<u8>,
    written: BTreeSet<u32>,
    moved: Vec<u32>,
    dirty: BTreeSet<(u32, u32)>,
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
            next_group: 1,
            scratch: Vec::new(),
            written: BTreeSet::new(),
            moved: Vec::new(),
            dirty: BTreeSet::new(),
        }
    }

    fn step(&mut self, seconds: f32) {
        self.physics.integration_parameters.dt = seconds;
        self.physics.step();
        self.dirty.clear();
        for entry in self.bodies.values() {
            if let Some(body) = self.physics.bodies.get_mut(entry.handle) {
                body.reset_forces(false);
                body.reset_torques(false);
            }
        }
    }

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
            self.dirty.insert(handle.into_raw_parts());
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
                        let handle = self.physics.insert_collider(collider, None);
                        self.dirty.insert(handle.into_raw_parts());
                        self.statics[index].handle = Some(handle);
                    }
                }
            }
        }
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
        let builder = builder
            .locked_axes(locked_axes(recipe.locks))
            .gravity_scale(recipe.gravity_scale)
            .linear_damping(recipe.linear_damping)
            .angular_damping(recipe.angular_damping)
            .ccd_enabled(recipe.ccd);
        let handle = self.physics.insert_body(builder.pose(pose(position, rotation)).user_data(u128::from(id)).build());
        let mut collider_handles = Vec::with_capacity(built.len());
        for collider in built {
            let collider_handle = self.physics.insert_collider(collider, Some(handle));
            self.dirty.insert(collider_handle.into_raw_parts());
            collider_handles.push(collider_handle);
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
                self.written.insert(id);
                self.mark_dirty(id);
                true
            }
            None => false,
        }
    }

    fn set_kind(&mut self, id: u32, kind: u32) -> bool {
        match self.body_mut(id) {
            Some(body) => {
                body.set_body_type(body_type(kind), true);
                self.written.insert(id);
                true
            }
            None => false,
        }
    }

    fn mark_dirty(&mut self, id: u32) {
        let colliders: Vec<(u32, u32)> = match self.body(id) {
            Some(body) => body.colliders().iter().map(|handle| handle.into_raw_parts()).collect(),
            None => return,
        };
        self.dirty.extend(colliders);
    }

    fn kind(&self, id: u32) -> Option<u32> {
        self.body(id).map(|body| kind_of(body.body_type()))
    }

    fn collect_moved(&mut self) -> usize {
        let mut moved: BTreeSet<u32> = std::mem::take(&mut self.written);
        for (_, body) in self.physics.active_bodies() {
            moved.insert(body.user_data as u32);
        }
        self.moved = moved.into_iter().filter(|id| self.bodies.contains_key(id)).collect();
        self.moved.len()
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
        let dirty = self.dirty.iter().copied().collect();
        let snapshot = SnapshotRef { format: SNAPSHOT_FORMAT, world: &self.physics, handles, statics, dirty };
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

        self.written.extend(self.bodies.keys().copied());

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
        self.dirty = snapshot.dirty.into_iter().collect();
        self.restore_statics(&snapshot.statics);
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

                        self.mark_dirty(id);
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

    fn accepts(&self, collider: &Collider, filter: &FrQueryFilter) -> bool {
        (filter.include_triggers != 0 || !collider.is_sensor())
            && collider.collision_groups().memberships.bits() & filter.layer_mask != 0
            && (filter.exclude_body == 0 || self.owner_of_collider(collider) != filter.exclude_body)
    }

    fn with_pipeline<T>(&self, filter: &FrQueryFilter, bodies_only: bool, run: impl FnOnce(&QueryPipeline) -> T) -> T {
        let predicate = |handle: ColliderHandle, collider: &Collider| {
            !self.dirty.contains(&handle.into_raw_parts())
                && self.accepts(collider, filter)
                && (!bodies_only || self.owner_of_collider(collider) != 0)
        };
        let pipeline = self.physics.broad_phase.as_query_pipeline(
            self.physics.narrow_phase.query_dispatcher(),
            &self.physics.bodies,
            &self.physics.colliders,
            QueryFilter::new().predicate(&predicate),
        );
        run(&pipeline)
    }

    fn dirty_colliders<'a>(&'a self, filter: &'a FrQueryFilter, bodies_only: bool) -> impl Iterator<Item = (ColliderHandle, &'a Collider, Pose)> + 'a {
        self.dirty.iter().filter_map(move |(index, generation)| {
            let handle = ColliderHandle::from_raw_parts(*index, *generation);
            let collider = self.physics.colliders.get(handle)?;
            if self.accepts(collider, filter) && (!bodies_only || self.owner_of_collider(collider) != 0) {
                Some((handle, collider, self.pose_of(collider)))
            } else {
                None
            }
        })
    }

    fn raycast(&self, origin: Vector, direction: Vector, max_distance: f32, filter: &FrQueryFilter) -> Option<FrRayHit> {
        let length = direction.length();
        if length == 0.0 {
            return None;
        }

        let ray = Ray::new(origin, direction / length);
        let mut best = self.with_pipeline(filter, false, |pipeline| pipeline.cast_ray_and_get_normal(&ray, max_distance, true));
        for (handle, collider, pose) in self.dirty_colliders(filter, false) {
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

    fn shape_cast(&self, shape: &dyn Shape, shape_pose: Pose, direction: Vector, max_distance: f32, filter: &FrQueryFilter) -> Option<FrRayHit> {
        let length = direction.length();
        if length == 0.0 {
            return None;
        }

        let velocity = direction / length;
        let options = ShapeCastOptions {
            max_time_of_impact: max_distance,
            target_distance: 0.0,
            stop_at_penetration: true,
            compute_impact_geometry_on_penetration: true,
        };
        let mut best = self.with_pipeline(filter, false, |pipeline| {
            pipeline
                .cast_shape(&shape_pose, velocity, shape, options)
                .map(|(handle, hit)| (handle, hit.time_of_impact, hit.witness1, hit.normal1))
        });
        for (handle, collider, pose) in self.dirty_colliders(filter, false) {
            if let Ok(Some(hit)) = query::cast_shapes(&pose, Vector::ZERO, collider.shape(), &shape_pose, velocity, shape, options) {
                if best.map_or(true, |(_, found, _, _)| hit.time_of_impact < found) {
                    best = Some((handle, hit.time_of_impact, pose * hit.witness1, pose.rotation * hit.normal1));
                }
            }
        }

        let (collider, distance, point, normal) = best?;
        Some(FrRayHit {
            body: self.owner_of(collider),
            point: [point.x, point.y, point.z],
            normal: [normal.x, normal.y, normal.z],
            distance,
        })
    }

    fn overlap(&self, shape: &dyn Shape, shape_pose: Pose, filter: &FrQueryFilter) -> Vec<u32> {
        let mut found = BTreeSet::new();
        self.with_pipeline(filter, true, |pipeline| {
            for (handle, _) in pipeline.intersect_shape(shape_pose, shape) {
                found.insert(self.owner_of(handle));
            }
        });
        for (handle, collider, pose) in self.dirty_colliders(filter, true) {
            if query::intersection_test(&shape_pose, shape, &pose, collider.shape()).is_ok_and(|hit| hit.intersecting) {
                found.insert(self.owner_of(handle));
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

    fn body_contact_points(&self, body: u32, other: u32, filter: &FrQueryFilter) -> Vec<FrContactPoint> {
        let mut found = Vec::new();
        if let Some(rigid_body) = self.body(body) {
            for handle in rigid_body.colliders() {
                self.contact_points(*handle, |_, collider| self.owner_of_collider(collider) == other && self.accepts(collider, filter), &mut found);
            }
        }

        found
    }

    fn collider_contact_points(&self, body: u32, index: u32, other_body: u32, other_index: u32) -> Vec<FrContactPoint> {
        let mut found = Vec::new();
        if let (Some(own), Some(other)) = (self.collider_of(body, index), self.collider_of(other_body, other_index)) {
            self.contact_points(own, |handle, _| handle == other, &mut found);
        }

        found
    }

    fn contact_points(&self, own: ColliderHandle, accepts: impl Fn(ColliderHandle, &Collider) -> bool, found: &mut Vec<FrContactPoint>) {
        let reach = self.physics.integration_parameters.prediction_distance();
        for pair in self.physics.narrow_phase.contact_pairs_with(own) {
            if !pair.has_any_active_contact() {
                continue;
            }

            let own_first = pair.collider1 == own;
            let other = if own_first { pair.collider2 } else { pair.collider1 };
            let (first, second) = match (self.physics.colliders.get(pair.collider1), self.physics.colliders.get(pair.collider2)) {
                (Some(first), Some(second)) => (first, second),
                _ => continue,
            };
            if !accepts(other, if own_first { second } else { first }) {
                continue;
            }

            for manifold in pair.manifolds() {
                let pose1 = manifold.subshape_pos1().map_or(*first.position(), |sub| *first.position() * *sub);
                let pose2 = manifold.subshape_pos2().map_or(*second.position(), |sub| *second.position() * *sub);
                let outward = pose1.rotation * manifold.local_n1;
                let normal = if own_first { -outward } else { outward };
                for contact in manifold.points.iter() {
                    let on_first = pose1 * contact.local_p1;
                    let on_second = pose2 * contact.local_p2;
                    let depth = (on_first - on_second).dot(outward);
                    if depth <= -reach {
                        continue;
                    }

                    let point = (on_first + on_second) * 0.5;
                    found.push(FrContactPoint { point: [point.x, point.y, point.z], normal: [normal.x, normal.y, normal.z], depth });
                }
            }
        }
    }

    fn collider_of(&self, body: u32, index: u32) -> Option<ColliderHandle> {
        if body == 0 {
            return self.statics.get(index as usize).and_then(|slot| slot.handle);
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
        self.physics.colliders.get(collider).map_or(0, |collider| self.owner_of_collider(collider))
    }

    fn owner_of_collider(&self, collider: &Collider) -> u32 {
        collider
            .parent()
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

#[no_mangle]
pub unsafe extern "C" fn fr_world_remove_static(world_ptr: *mut World, group: u32) -> bool {
    world(world_ptr).map_or(false, |world| world.remove_static(group))
}

#[no_mangle]
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
    ccd: bool,
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
        BodyRecipe { kind: kind.min(KIND_STATIC), mass, locks, gravity_scale, linear_damping, angular_damping, ccd, colliders: recipes },
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
pub unsafe extern "C" fn fr_body_add_torque(world_ptr: *mut World, body: u32, x: f32, y: f32, z: f32) -> bool {
    match world(world_ptr).and_then(|world| world.body_mut(body)) {
        Some(body) => {
            body.add_torque(Vector::new(x, y, z), true);
            true
        }
        None => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_body_add_torque_impulse(world_ptr: *mut World, body: u32, x: f32, y: f32, z: f32) -> bool {
    match world(world_ptr).and_then(|world| world.body_mut(body)) {
        Some(body) => {
            body.apply_torque_impulse(Vector::new(x, y, z), true);
            true
        }
        None => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_body_add_force_at_point(world_ptr: *mut World, body: u32, fx: f32, fy: f32, fz: f32, px: f32, py: f32, pz: f32) -> bool {
    match world(world_ptr).and_then(|world| world.body_mut(body)) {
        Some(body) => {
            body.add_force_at_point(Vector::new(fx, fy, fz), Vector::new(px, py, pz), true);
            true
        }
        None => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_body_add_impulse_at_point(world_ptr: *mut World, body: u32, ix: f32, iy: f32, iz: f32, px: f32, py: f32, pz: f32) -> bool {
    match world(world_ptr).and_then(|world| world.body_mut(body)) {
        Some(body) => {
            body.apply_impulse_at_point(Vector::new(ix, iy, iz), Vector::new(px, py, pz), true);
            true
        }
        None => false,
    }
}

fn query_shape(shape: &FrQueryShape) -> Option<SharedShape> {
    match shape.shape {
        SHAPE_BOX => Some(SharedShape::cuboid(shape.half_extents[0], shape.half_extents[1], shape.half_extents[2])),
        SHAPE_SPHERE => Some(SharedShape::ball(shape.radius)),
        SHAPE_CAPSULE => Some(SharedShape::capsule_y(shape.half_height, shape.radius)),
        _ => None,
    }
}

unsafe fn read_vector(values: *const f32) -> Option<Vector> {
    if values.is_null() {
        return None;
    }

    let values = slice::from_raw_parts(values, 3);
    Some(Vector::new(values[0], values[1], values[2]))
}

unsafe fn read_pose(position: *const f32, rotation: *const f32) -> Option<Pose> {
    if position.is_null() || rotation.is_null() {
        return None;
    }

    let position = slice::from_raw_parts(position, 3);
    let rotation = slice::from_raw_parts(rotation, 4);
    Some(pose([position[0], position[1], position[2]], [rotation[0], rotation[1], rotation[2], rotation[3]]))
}

unsafe fn write_hit(found: Option<FrRayHit>, hit: *mut FrRayHit) -> bool {
    match (found, hit.as_mut()) {
        (Some(found), Some(out)) => {
            *out = found;
            true
        }
        _ => false,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_world_raycast(
    world_ptr: *mut World,
    origin: *const f32,
    direction: *const f32,
    max_distance: f32,
    filter: *const FrQueryFilter,
    hit: *mut FrRayHit,
) -> bool {
    let found = match (world(world_ptr), read_vector(origin), read_vector(direction), filter.as_ref()) {
        (Some(world), Some(origin), Some(direction), Some(filter)) => world.raycast(origin, direction, max_distance, filter),
        _ => None,
    };
    write_hit(found, hit)
}

#[no_mangle]
pub unsafe extern "C" fn fr_world_shape_cast(
    world_ptr: *mut World,
    shape: *const FrQueryShape,
    position: *const f32,
    rotation: *const f32,
    direction: *const f32,
    max_distance: f32,
    filter: *const FrQueryFilter,
    hit: *mut FrRayHit,
) -> bool {
    let shape = match shape.as_ref().and_then(query_shape) {
        Some(shape) => shape,
        None => return false,
    };
    let found = match (world(world_ptr), read_pose(position, rotation), read_vector(direction), filter.as_ref()) {
        (Some(world), Some(shape_pose), Some(direction), Some(filter)) => world.shape_cast(&*shape, shape_pose, direction, max_distance, filter),
        _ => None,
    };
    write_hit(found, hit)
}

#[no_mangle]
pub unsafe extern "C" fn fr_world_overlap(
    world_ptr: *mut World,
    shape: *const FrQueryShape,
    position: *const f32,
    rotation: *const f32,
    filter: *const FrQueryFilter,
    bodies: *mut u32,
    capacity: u32,
) -> u32 {
    let shape = match shape.as_ref().and_then(query_shape) {
        Some(shape) => shape,
        None => return 0,
    };
    let found = match (world(world_ptr), read_pose(position, rotation), filter.as_ref()) {
        (Some(world), Some(shape_pose), Some(filter)) => world.overlap(&*shape, shape_pose, filter),
        _ => return 0,
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
pub unsafe extern "C" fn fr_body_contact_points(world_ptr: *mut World, body: u32, other: u32, filter: &FrQueryFilter, out: *mut FrContactPoint, capacity: u32) -> u32 {
    match world(world_ptr) {
        Some(world) => write_items(&world.body_contact_points(body, other, filter), out, capacity),
        None => 0,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_collider_contact_points(
    world_ptr: *mut World,
    body: u32,
    index: u32,
    other_body: u32,
    other_index: u32,
    out: *mut FrContactPoint,
    capacity: u32,
) -> u32 {
    match world(world_ptr) {
        Some(world) => write_items(&world.collider_contact_points(body, index, other_body, other_index), out, capacity),
        None => 0,
    }
}

#[no_mangle]
pub unsafe extern "C" fn fr_world_moved(world_ptr: *mut World) -> u32 {
    world(world_ptr).map_or(0, |world| world.collect_moved() as u32)
}

#[no_mangle]
pub unsafe extern "C" fn fr_world_moved_copy(world_ptr: *mut World, bodies: *mut u32, capacity: u32) -> u32 {
    let world = match world(world_ptr) {
        Some(world) => world,
        None => return 0,
    };
    let count = world.moved.len().min(capacity as usize);
    if count > 0 && !bodies.is_null() {
        slice::from_raw_parts_mut(bodies, count).copy_from_slice(&world.moved[..count]);
    }

    count as u32
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

