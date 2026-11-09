use std::ptr;

use fomoxa_rapier::*;

const STEPS: u32 = 900;
const REPORT_EVERY: u32 = 150;
const SNAPSHOT_AT: u32 = 450;
const SECONDS: f32 = 1.0 / 30.0;
const IDENTITY: [f32; 4] = [0.0, 0.0, 0.0, 1.0];
const TURNED: [f32; 4] = [0.6, 0.0, 0.0, 0.8];
const FORCE_STEPS: u32 = 60;
const CCD_STEPS: u32 = 3;
const JOINT_STEPS: u32 = 120;
const JOINT_SNAPSHOT_AT: u32 = 60;
const JOINT_RETUNE_AT: u32 = 70;
const CHARACTER_STEPS: u32 = 120;
const CHARACTER_SNAPSHOT_AT: u32 = 60;
const CHARACTER_LAYER: u32 = 1;
const SKIPPED_LAYER: u32 = 2;
const ALL: FrQueryFilter = FrQueryFilter { layer_mask: u32::MAX, exclude_body: 0, include_triggers: 1 };

fn collider(shape: u32, half_extents: [f32; 3], radius: f32, half_height: f32, points: &[f32], triangles: &[u32]) -> FrCollider {
    FrCollider {
        shape,
        position: [0.0; 3],
        rotation: IDENTITY,
        half_extents,
        radius,
        half_height,
        points: if points.is_empty() { ptr::null() } else { points.as_ptr() },
        point_count: (points.len() / 3) as u32,
        triangles: if triangles.is_empty() { ptr::null() } else { triangles.as_ptr() },
        triangle_index_count: triangles.len() as u32,
        friction: 0.6,
        restitution: 0.1,
        friction_combine: 0,
        restitution_combine: 0,
        layer: 0,
        is_trigger: 0,
    }
}

fn collider_2d(shape: u32, half_extents: [f32; 2], radius: f32, half_height: f32, points: &[f32]) -> FrCollider2D {
    FrCollider2D {
        shape,
        position: [0.0; 2],
        rotation: 0.0,
        half_extents,
        radius,
        half_height,
        points: if points.is_empty() { ptr::null() } else { points.as_ptr() },
        point_count: (points.len() / 2) as u32,
        friction: 0.6,
        restitution: 0.1,
        friction_combine: 0,
        restitution_combine: 0,
        layer: 0,
        is_trigger: 0,
    }
}

unsafe fn snapshot_3d(world: *mut World) -> Vec<u8> {
    let mut bytes = vec![0u8; fr_world_snapshot(world) as usize];
    assert!(fr_world_snapshot_copy(world, bytes.as_mut_ptr(), bytes.len() as u32));
    bytes
}

unsafe fn snapshot_2d(world: *mut World2D) -> Vec<u8> {
    let mut bytes = vec![0u8; fr2_world_snapshot(world) as usize];
    assert!(fr2_world_snapshot_copy(world, bytes.as_mut_ptr(), bytes.len() as u32));
    bytes
}

unsafe fn step_3d(world: *mut World, bodies: &[u32], tick: u32) {
    let count = bodies.len();
    if tick % 37 == 0 {
        fr_body_add_impulse(world, bodies[tick as usize % count], 1.5, 3.25, -0.75);
    }

    fr_body_add_force(world, bodies[tick as usize * 3 % count], 4.0, 0.0, 2.5);
    fr_world_step(world, SECONDS);
}

unsafe fn step_2d(world: *mut World2D, bodies: &[u32], tick: u32) {
    let count = bodies.len();
    if tick % 37 == 0 {
        fr2_body_add_impulse(world, bodies[tick as usize % count], 1.5, 3.25);
    }

    fr2_body_add_force(world, bodies[tick as usize * 3 % count], 4.0, 0.0);
    fr2_world_step(world, SECONDS);
}

fn joint_3d(kind: u32, position1: [f32; 3], position2: [f32; 3]) -> FrJoint {
    FrJoint {
        kind,
        position1,
        rotation1: IDENTITY,
        position2,
        rotation2: IDENTITY,
        limits: [FrJointLimit::default(); 3],
        motor: FrJointMotor::default(),
        length: 0.0,
        stiffness: 0.0,
        damping: 0.0,
        softness: FrJointSoftness::default(),
        contacts_enabled: 0,
    }
}

fn joint_2d(kind: u32, position1: [f32; 2], position2: [f32; 2]) -> FrJoint2D {
    FrJoint2D {
        kind,
        position1,
        rotation1: 0.0,
        position2,
        rotation2: 0.0,
        limit: FrJointLimit::default(),
        motor: FrJointMotor::default(),
        length: 0.0,
        stiffness: 0.0,
        damping: 0.0,
        softness: FrJointSoftness::default(),
        contacts_enabled: 0,
    }
}

fn limit(min: f32, max: f32) -> FrJointLimit {
    FrJointLimit { enabled: 1, min, max }
}

fn velocity_motor(target_velocity: f32) -> FrJointMotor {
    FrJointMotor { enabled: 1, target_position: 0.0, target_velocity, stiffness: 0.0, damping: 50.0, max_force: 20.0 }
}

fn position_motor(target_position: f32) -> FrJointMotor {
    FrJointMotor { enabled: 1, target_position, target_velocity: 0.0, stiffness: 40.0, damping: 4.0, max_force: 100.0 }
}

fn ids(found: &[u32]) -> String {
    found.iter().map(|id| id.to_string()).collect::<Vec<_>>().join(",")
}

unsafe fn ray_3d(world: *mut World, label: &str, origin: [f32; 3], direction: [f32; 3], filter: &FrQueryFilter) -> FrRayHit {
    let mut hit = FrRayHit::default();
    let found = fr_world_raycast(world, origin.as_ptr(), direction.as_ptr(), 100.0, filter, &mut hit);
    println!("3d {} {} {} {:08x}", label, found, hit.body, hit.distance.to_bits());
    hit
}

unsafe fn queries_3d(world: *mut World, moved: u32) {
    let origin = [0.5, 40.0, 0.25];
    let direction = [0.02, -1.0, 0.01];
    let hit = ray_3d(world, "ray", origin, direction, &ALL);
    ray_3d(world, "ray-skip", origin, direction, &FrQueryFilter { exclude_body: hit.body, ..ALL });
    ray_3d(world, "ray-layer", origin, direction, &FrQueryFilter { layer_mask: 1 << 1, ..ALL });

    let box_shape = FrQueryShape { shape: 0, half_extents: [0.25, 0.125, 0.375], radius: 0.0, half_height: 0.0 };
    let mut cast = FrRayHit::default();
    let found = fr_world_shape_cast(world, &box_shape, origin.as_ptr(), TURNED.as_ptr(), direction.as_ptr(), 100.0, &ALL, &mut cast);
    println!("3d cast {} {} {:08x}", found, cast.body, cast.distance.to_bits());

    let capsule = FrQueryShape { shape: 2, half_extents: [0.0; 3], radius: 1.5, half_height: 1.0 };
    let center = [0.0, 2.0, 0.0];
    let mut overlapped = [0u32; 64];
    let count = fr_world_overlap(world, &capsule, center.as_ptr(), TURNED.as_ptr(), &ALL, overlapped.as_mut_ptr(), 64);
    println!("3d overlap {} {}", count, ids(&overlapped[..count as usize]));

    let state = FrBodyState { position: [20.0, 30.0, 20.0], rotation: IDENTITY, velocity: [0.0; 3], angular_velocity: [0.0; 3] };
    assert!(fr_body_set(world, moved, &state));
    ray_3d(world, "moved", [20.0, 40.0, 20.0], [0.0, -1.0, 0.0], &ALL);
    ray_3d(world, "inside", [20.0, 30.0, 20.0], [0.0, -1.0, 0.0], &ALL);
}

unsafe fn ray_2d(world: *mut World2D, label: &str, origin: [f32; 2], direction: [f32; 2], filter: &FrQueryFilter) -> FrRayHit2D {
    let mut hit = FrRayHit2D::default();
    let found = fr2_world_raycast(world, origin[0], origin[1], direction[0], direction[1], 100.0, filter, &mut hit);
    println!("2d {} {} {} {:08x}", label, found, hit.body, hit.distance.to_bits());
    hit
}

unsafe fn queries_2d(world: *mut World2D, moved: u32) {
    let origin = [0.5, 40.0];
    let direction = [0.02, -1.0];
    let hit = ray_2d(world, "ray", origin, direction, &ALL);
    ray_2d(world, "ray-skip", origin, direction, &FrQueryFilter { exclude_body: hit.body, ..ALL });
    ray_2d(world, "ray-layer", origin, direction, &FrQueryFilter { layer_mask: 1 << 1, ..ALL });

    let circle = FrQueryShape2D { shape: 1, half_extents: [0.0; 2], radius: 0.375, half_height: 0.0 };
    let mut cast = FrRayHit2D::default();
    let found = fr2_world_shape_cast(world, &circle, origin[0], origin[1], 0.0, direction[0], direction[1], 100.0, &ALL, &mut cast);
    println!("2d cast {} {} {:08x}", found, cast.body, cast.distance.to_bits());

    let box_shape = FrQueryShape2D { shape: 0, half_extents: [2.0, 1.0], radius: 0.0, half_height: 0.0 };
    let mut overlapped = [0u32; 64];
    let count = fr2_world_overlap(world, &box_shape, 0.0, 2.0, 0.5, &ALL, overlapped.as_mut_ptr(), 64);
    println!("2d overlap {} {}", count, ids(&overlapped[..count as usize]));

    let state = FrBodyState2D { position: [20.0, 30.0], rotation: 0.0, velocity: [0.0; 2], angular_velocity: 0.0 };
    assert!(fr2_body_set(world, moved, &state));
    ray_2d(world, "moved", [20.0, 40.0], [0.0, -1.0], &ALL);
    ray_2d(world, "inside", [20.0, 30.0], [0.0, -1.0], &ALL);
}

unsafe fn position_3d(world: *mut World, body: u32) -> [f32; 3] {
    let mut state = FrBodyState { position: [0.0; 3], rotation: IDENTITY, velocity: [0.0; 3], angular_velocity: [0.0; 3] };
    assert!(fr_body_get(world, body, &mut state));
    state.position
}

unsafe fn forces_3d(world: *mut World, bodies: &[u32]) {
    let count = bodies.len();
    for tick in 1..=FORCE_STEPS as usize {
        fr_body_add_torque(world, bodies[tick % count], 0.5, -1.25, 0.75);
        let pushed = bodies[tick * 5 % count];
        let at = position_3d(world, pushed);
        fr_body_add_force_at_point(world, pushed, 3.0, 0.0, -1.5, at[0] + 0.3, at[1] + 0.2, at[2] + 0.1);
        if tick % 10 == 0 {
            fr_body_add_torque_impulse(world, bodies[tick * 7 % count], 0.25, 0.5, -0.125);
            let struck = bodies[tick * 11 % count];
            let at = position_3d(world, struck);
            fr_body_add_impulse_at_point(world, struck, 0.5, 1.0, 0.0, at[0] - 0.2, at[1], at[2] + 0.3);
        }

        fr_world_step(world, SECONDS);
    }

    println!("3d forces {:016x}", fr_world_hash(world));
}

unsafe fn ccd_ball_3d(world: *mut World, z: f32, ccd: bool) -> u32 {
    let ball = collider(1, [0.0; 3], 0.1, 0.0, &[], &[]);
    let position = [200.0, 50.0, z];
    let body = fr_body_create(world, 0, position.as_ptr(), IDENTITY.as_ptr(), 1.0, 0, 0.0, 0.0, 0.0, ccd, &ball, 1);
    fr_body_add_impulse(world, body, 300.0, 0.0, 0.0);
    body
}

unsafe fn ccd_3d(world: *mut World) {
    let wall = collider(0, [0.05, 2.0, 2.0], 0.0, 0.0, &[], &[]);
    let at = [205.0, 50.0, 0.0];
    fr_body_create(world, 1, at.as_ptr(), IDENTITY.as_ptr(), 0.0, 0, 0.0, 0.0, 0.0, false, &wall, 1);
    let continuous = ccd_ball_3d(world, -1.0, true);
    let discrete = ccd_ball_3d(world, 1.0, false);
    for _ in 0..CCD_STEPS {
        fr_world_step(world, SECONDS);
    }

    println!("3d ccd {:08x} {:08x}", position_3d(world, continuous)[0].to_bits(), position_3d(world, discrete)[0].to_bits());
}

unsafe fn contacts_3d(world: *mut World, bodies: &[u32]) {
    let mut points = [FrContactPoint::default(); 64];
    let mut total = 0;
    let mut depth = 0.0f32;
    for body in bodies {
        let count = fr_body_contact_points(world, *body, 0, &ALL, points.as_mut_ptr(), 64) as usize;
        for point in &points[..count.min(64)] {
            depth += point.depth;
        }

        total += count;
    }

    println!("3d contacts {} {:08x}", total, depth.to_bits());
}

unsafe fn joints_3d() {
    let world = fr_world_create(0.0, -9.81, 0.0);
    let link = collider(0, [0.4, 0.1, 0.1], 0.0, 0.0, &[], &[]);
    let mut links = Vec::new();
    for index in 0..6u32 {
        let position = [0.5 + index as f32, 10.0, 0.0];
        links.push(fr_body_create(world, 0, position.as_ptr(), IDENTITY.as_ptr(), 1.0, 0, 1.0, 0.0, 0.0, false, &link, 1));
    }

    let mut hinge = joint_3d(1, [-0.5, 0.0, 0.0], [0.0, 10.0, 0.0]);
    hinge.rotation1 = [0.0, -0.707_106_77, 0.0, 0.707_106_77];
    hinge.rotation2 = hinge.rotation1;
    hinge.limits[0] = limit(-1.2, 1.2);
    hinge.motor = velocity_motor(0.5);
    let hinge_id = fr_joint_create(world, links[0], 0, &hinge);
    let mut ball = joint_3d(3, [0.5, 0.0, 0.0], [-0.5, 0.0, 0.0]);
    ball.limits = [limit(-0.3, 0.3), limit(-0.6, 0.6), limit(-0.6, 0.6)];
    fr_joint_create(world, links[0], links[1], &ball);
    let mut slider = joint_3d(2, [0.5, 0.0, 0.0], [-0.5, 0.0, 0.0]);
    slider.limits[0] = limit(-0.2, 0.2);
    slider.motor = position_motor(0.1);
    fr_joint_create(world, links[1], links[2], &slider);
    let mut weld = joint_3d(0, [0.5, 0.0, 0.0], [-0.5, 0.0, 0.0]);
    weld.softness = FrJointSoftness { frequency: 10.0, damping_ratio: 1.0 };
    fr_joint_create(world, links[2], links[3], &weld);
    let mut rope = joint_3d(4, [0.5, 0.0, 0.0], [-0.5, 0.0, 0.0]);
    rope.length = 1.5;
    let rope_id = fr_joint_create(world, links[3], links[4], &rope);
    let mut spring = joint_3d(5, [0.5, 0.0, 0.0], [-0.5, 0.0, 0.0]);
    spring.length = 1.0;
    spring.stiffness = 30.0;
    spring.damping = 2.0;
    fr_joint_create(world, links[4], links[5], &spring);

    let mut saved = Vec::new();
    let mut late = 0;
    let mut at_snapshot = 0;
    for tick in 1..=JOINT_STEPS {
        if tick == JOINT_SNAPSHOT_AT + 1 {
            let mut tether = joint_3d(4, [0.5, 0.0, 0.0], [8.0, 12.0, 0.0]);
            tether.length = 3.0;
            late = fr_joint_create(world, links[5], 0, &tether);
        }

        if tick == JOINT_RETUNE_AT {
            hinge.motor = velocity_motor(-1.0);
            assert!(fr_joint_set(world, hinge_id, &hinge));
        }

        fr_world_step(world, SECONDS);
        if tick == JOINT_SNAPSHOT_AT {
            at_snapshot = fr_world_hash(world);
            saved = snapshot_3d(world);
        }
    }

    let at_end = fr_world_hash(world);
    println!("3d joints {:016x} {:016x}", at_snapshot, at_end);
    assert!(fr_world_load(world, saved.as_ptr(), saved.len() as u32));
    for tick in JOINT_SNAPSHOT_AT + 1..=JOINT_STEPS {
        if tick == JOINT_RETUNE_AT {
            assert!(fr_joint_set(world, hinge_id, &hinge));
        }

        fr_world_step(world, SECONDS);
    }

    println!("3d joints replay {}", fr_world_hash(world) == at_end);
    assert!(fr_joint_remove(world, rope_id));
    assert!(fr_world_load(world, saved.as_ptr(), saved.len() as u32));
    println!("3d joints load {} {}", fr_joint_contains(world, late), fr_joint_contains(world, rope_id));
    fr_world_destroy(world);
}

fn length(value: f32) -> FrCharacterLength {
    FrCharacterLength { value, relative: 1 }
}

fn autostep() -> FrCharacterAutostep {
    FrCharacterAutostep { enabled: 1, max_height: length(0.25), min_width: length(0.5), include_dynamic_bodies: 0 }
}

fn character_layers() -> [u32; 32] {
    let mut layers = [u32::MAX; 32];
    layers[CHARACTER_LAYER as usize] = !(1 << SKIPPED_LAYER);
    layers[SKIPPED_LAYER as usize] = !(1 << CHARACTER_LAYER);
    layers
}

fn character_settings_3d() -> FrCharacterSettings {
    FrCharacterSettings {
        up: [0.0, 1.0, 0.0],
        offset: length(0.01),
        slide: 1,
        autostep: autostep(),
        max_slope_climb_angle: std::f32::consts::FRAC_PI_4,
        min_slope_slide_angle: std::f32::consts::FRAC_PI_4,
        snap_to_ground: 1,
        snap_distance: length(0.2),
        normal_nudge_factor: 1.0e-4,
        push_mass: 2.0,
    }
}

unsafe fn static_box_3d(world: *mut World, half_extents: [f32; 3], position: [f32; 3], layer: u32, is_trigger: bool) {
    let mut shape = collider(0, half_extents, 0.0, 0.0, &[], &[]);
    shape.position = position;
    shape.layer = layer;
    shape.is_trigger = is_trigger as u32;
    let mut first = 0;
    fr_world_add_static(world, &shape, 1, &mut first);
}

unsafe fn character_tick_3d(world: *mut World, character: u32, gate: u32, tick: u32) -> u32 {
    if (40..=50).contains(&tick) {
        let mut at = FrBodyState::default();
        assert!(fr_body_get(world, character, &mut at));
        let state = FrBodyState { position: [at.position[0] + 0.7, 1.0, 0.0], rotation: IDENTITY, ..Default::default() };
        assert!(fr_body_set(world, gate, &state));
    } else if tick == 51 {
        let state = FrBodyState { position: [0.0, 1.0, 10.0], rotation: IDENTITY, ..Default::default() };
        assert!(fr_body_set(world, gate, &state));
    }

    let desired = [0.1, -0.02, 0.0];
    let mut moved = FrCharacterMove::default();
    assert!(fr_character_move(world, character, desired.as_ptr(), SECONDS, &character_settings_3d(), &ALL, &mut moved));
    fr_world_step(world, SECONDS);
    moved.grounded
}

unsafe fn character_3d() {
    let world = fr_world_create(0.0, -9.81, 0.0);
    assert!(fr_world_set_layers(world, character_layers().as_ptr()));
    static_box_3d(world, [20.0, 0.5, 5.0], [8.0, -0.5, 0.0], 0, false);
    static_box_3d(world, [0.3, 1.0, 1.0], [2.0, 1.0, 0.0], 0, true);
    static_box_3d(world, [1.0, 0.15, 2.0], [4.0, 0.15, 0.0], 0, false);
    static_box_3d(world, [0.1, 1.5, 2.0], [9.0, 1.5, 0.0], SKIPPED_LAYER, false);

    let mut capsule = collider(2, [0.0; 3], 0.3, 0.5, &[], &[]);
    capsule.layer = CHARACTER_LAYER;
    let start = [0.0, 0.81, 0.0];
    let character = fr_body_create(world, 1, start.as_ptr(), IDENTITY.as_ptr(), 1.0, 0, 1.0, 0.0, 0.0, false, &capsule, 1);
    let plate = collider(0, [0.1, 1.0, 1.0], 0.0, 0.0, &[], &[]);
    let away = [0.0, 1.0, 10.0];
    let gate = fr_body_create(world, 1, away.as_ptr(), IDENTITY.as_ptr(), 1.0, 0, 1.0, 0.0, 0.0, false, &plate, 1);
    let crate_box = collider(0, [0.3, 0.3, 0.3], 0.0, 0.0, &[], &[]);
    let crate_at = [10.5, 0.3, 0.0];
    fr_body_create(world, 0, crate_at.as_ptr(), IDENTITY.as_ptr(), 1.0, 0, 1.0, 0.0, 0.0, false, &crate_box, 1);

    let mut saved = Vec::new();
    let mut at_snapshot = 0;
    let mut grounded = 0;
    for tick in 1..=CHARACTER_STEPS {
        grounded += character_tick_3d(world, character, gate, tick);
        if tick == CHARACTER_SNAPSHOT_AT {
            at_snapshot = fr_world_hash(world);
            saved = snapshot_3d(world);
        }
    }

    let at_end = fr_world_hash(world);
    println!("3d character {:016x} {:016x} {:08x} {}", at_snapshot, at_end, position_3d(world, character)[0].to_bits(), grounded);
    assert!(fr_world_load(world, saved.as_ptr(), saved.len() as u32));
    for tick in CHARACTER_SNAPSHOT_AT + 1..=CHARACTER_STEPS {
        character_tick_3d(world, character, gate, tick);
    }

    println!("3d character replay {}", fr_world_hash(world) == at_end);
    fr_world_destroy(world);
}

unsafe fn run_3d() {
    let world = fr_world_create(0.0, -9.81, 0.0);
    let ground_points = [-50.0, 0.0, -50.0, 50.0, 2.0, -50.0, 50.0, 0.0, 50.0, -50.0, 3.0, 50.0];
    let ground_triangles = [0, 2, 1, 0, 3, 2];
    let ground = collider(4, [0.0; 3], 0.0, 0.0, &ground_points, &ground_triangles);
    let mut first = 0;
    fr_world_add_static(world, &ground, 1, &mut first);

    let hull_points = [0.4, 0.0, 0.0, -0.4, 0.0, 0.0, 0.0, 0.5, 0.0, 0.0, -0.5, 0.1, 0.0, 0.0, 0.6, 0.1, 0.1, -0.6];
    let mut bodies = Vec::new();
    for index in 0..60u32 {
        let position = [(index % 7) as f32 * 1.125 - 3.0, 6.0 + index as f32 * 0.875, (index % 5) as f32 * 0.875 - 2.0];
        let rotation = [0.1, 0.2, 0.3, 0.927_361_85];
        let shape = match index % 4 {
            0 => collider(0, [0.4, 0.3, 0.5], 0.0, 0.0, &[], &[]),
            1 => collider(1, [0.0; 3], 0.45, 0.0, &[], &[]),
            2 => collider(2, [0.0; 3], 0.3, 0.4, &[], &[]),
            _ => collider(3, [0.0; 3], 0.0, 0.0, &hull_points, &[]),
        };
        let mass = 1.0 + index as f32 * 0.0625;
        bodies.push(fr_body_create(world, 0, position.as_ptr(), rotation.as_ptr(), mass, 0, 1.0, 0.02, 0.03, false, &shape, 1));
    }

    let mut saved = Vec::new();
    let mut at_end = 0;
    for tick in 1..=STEPS {
        step_3d(world, &bodies, tick);
        if tick % REPORT_EVERY == 0 {
            println!("3d {} {:016x}", tick, fr_world_hash(world));
        }

        if tick == SNAPSHOT_AT {
            saved = snapshot_3d(world);
        }

        if tick == STEPS {
            at_end = fr_world_hash(world);
        }
    }

    assert!(fr_world_load(world, saved.as_ptr(), saved.len() as u32));
    for tick in SNAPSHOT_AT + 1..=STEPS {
        step_3d(world, &bodies, tick);
    }

    println!("3d replay {}", fr_world_hash(world) == at_end);

    queries_3d(world, bodies[0]);
    forces_3d(world, &bodies);
    ccd_3d(world);
    contacts_3d(world, &bodies);
    fr_world_destroy(world);
    joints_3d();
    character_3d();
}

unsafe fn position_2d(world: *mut World2D, body: u32) -> [f32; 2] {
    let mut state = FrBodyState2D { position: [0.0; 2], rotation: 0.0, velocity: [0.0; 2], angular_velocity: 0.0 };
    assert!(fr2_body_get(world, body, &mut state));
    state.position
}

unsafe fn forces_2d(world: *mut World2D, bodies: &[u32]) {
    let count = bodies.len();
    for tick in 1..=FORCE_STEPS as usize {
        fr2_body_add_torque(world, bodies[tick % count], -0.75);
        let pushed = bodies[tick * 5 % count];
        let at = position_2d(world, pushed);
        fr2_body_add_force_at_point(world, pushed, 3.0, -1.5, at[0] + 0.3, at[1] + 0.2);
        if tick % 10 == 0 {
            fr2_body_add_torque_impulse(world, bodies[tick * 7 % count], 0.25);
            let struck = bodies[tick * 11 % count];
            let at = position_2d(world, struck);
            fr2_body_add_impulse_at_point(world, struck, 0.5, 1.0, at[0] - 0.2, at[1] + 0.3);
        }

        fr2_world_step(world, SECONDS);
    }

    println!("2d forces {:016x}", fr2_world_hash(world));
}

unsafe fn ccd_ball_2d(world: *mut World2D, y: f32, ccd: bool) -> u32 {
    let ball = collider_2d(1, [0.0; 2], 0.1, 0.0, &[]);
    let body = fr2_body_create(world, 0, 200.0, y, 0.0, 1.0, 0, 0.0, 0.0, 0.0, ccd, &ball, 1);
    fr2_body_add_impulse(world, body, 300.0, 0.0);
    body
}

unsafe fn ccd_2d(world: *mut World2D) {
    let wall = collider_2d(0, [0.05, 2.0], 0.0, 0.0, &[]);
    fr2_body_create(world, 1, 205.0, 50.0, 0.0, 0.0, 0, 0.0, 0.0, 0.0, false, &wall, 1);
    let continuous = ccd_ball_2d(world, 49.0, true);
    let discrete = ccd_ball_2d(world, 51.0, false);
    for _ in 0..CCD_STEPS {
        fr2_world_step(world, SECONDS);
    }

    println!("2d ccd {:08x} {:08x}", position_2d(world, continuous)[0].to_bits(), position_2d(world, discrete)[0].to_bits());
}

unsafe fn contacts_2d(world: *mut World2D, bodies: &[u32]) {
    let mut points = [FrContactPoint2D::default(); 64];
    let mut total = 0;
    let mut depth = 0.0f32;
    for body in bodies {
        let count = fr2_body_contact_points(world, *body, 0, &ALL, points.as_mut_ptr(), 64) as usize;
        for point in &points[..count.min(64)] {
            depth += point.depth;
        }

        total += count;
    }

    println!("2d contacts {} {:08x}", total, depth.to_bits());
}

fn character_settings_2d() -> FrCharacterSettings2D {
    FrCharacterSettings2D {
        up: [0.0, 1.0],
        offset: length(0.01),
        slide: 1,
        autostep: autostep(),
        max_slope_climb_angle: std::f32::consts::FRAC_PI_4,
        min_slope_slide_angle: std::f32::consts::FRAC_PI_4,
        snap_to_ground: 1,
        snap_distance: length(0.2),
        normal_nudge_factor: 1.0e-4,
        push_mass: 2.0,
    }
}

unsafe fn static_box_2d(world: *mut World2D, half_extents: [f32; 2], position: [f32; 2], layer: u32, is_trigger: bool) {
    let mut shape = collider_2d(0, half_extents, 0.0, 0.0, &[]);
    shape.position = position;
    shape.layer = layer;
    shape.is_trigger = is_trigger as u32;
    let mut first = 0;
    fr2_world_add_static(world, &shape, 1, &mut first);
}

unsafe fn character_tick_2d(world: *mut World2D, character: u32, gate: u32, tick: u32) -> u32 {
    if (40..=50).contains(&tick) {
        let mut at = FrBodyState2D::default();
        assert!(fr2_body_get(world, character, &mut at));
        let state = FrBodyState2D { position: [at.position[0] + 0.7, 1.0], ..Default::default() };
        assert!(fr2_body_set(world, gate, &state));
    } else if tick == 51 {
        let state = FrBodyState2D { position: [0.0, 10.0], ..Default::default() };
        assert!(fr2_body_set(world, gate, &state));
    }

    let mut moved = FrCharacterMove2D::default();
    assert!(fr2_character_move(world, character, 0.1, -0.02, SECONDS, &character_settings_2d(), &ALL, &mut moved));
    fr2_world_step(world, SECONDS);
    moved.grounded
}

unsafe fn character_2d() {
    let world = fr2_world_create(0.0, -9.81);
    assert!(fr2_world_set_layers(world, character_layers().as_ptr()));
    static_box_2d(world, [20.0, 0.5], [8.0, -0.5], 0, false);
    static_box_2d(world, [0.3, 1.0], [2.0, 1.0], 0, true);
    static_box_2d(world, [1.0, 0.15], [4.0, 0.15], 0, false);
    static_box_2d(world, [0.1, 1.5], [9.0, 1.5], SKIPPED_LAYER, false);

    let mut capsule = collider_2d(2, [0.0; 2], 0.3, 0.5, &[]);
    capsule.layer = CHARACTER_LAYER;
    let character = fr2_body_create(world, 1, 0.0, 0.81, 0.0, 1.0, 0, 1.0, 0.0, 0.0, false, &capsule, 1);
    let plate = collider_2d(0, [0.1, 1.0], 0.0, 0.0, &[]);
    let gate = fr2_body_create(world, 1, 0.0, 10.0, 0.0, 1.0, 0, 1.0, 0.0, 0.0, false, &plate, 1);
    let crate_box = collider_2d(0, [0.3, 0.3], 0.0, 0.0, &[]);
    fr2_body_create(world, 0, 10.5, 0.3, 0.0, 1.0, 0, 1.0, 0.0, 0.0, false, &crate_box, 1);

    let mut saved = Vec::new();
    let mut at_snapshot = 0;
    let mut grounded = 0;
    for tick in 1..=CHARACTER_STEPS {
        grounded += character_tick_2d(world, character, gate, tick);
        if tick == CHARACTER_SNAPSHOT_AT {
            at_snapshot = fr2_world_hash(world);
            saved = snapshot_2d(world);
        }
    }

    let at_end = fr2_world_hash(world);
    println!("2d character {:016x} {:016x} {:08x} {}", at_snapshot, at_end, position_2d(world, character)[0].to_bits(), grounded);
    assert!(fr2_world_load(world, saved.as_ptr(), saved.len() as u32));
    for tick in CHARACTER_SNAPSHOT_AT + 1..=CHARACTER_STEPS {
        character_tick_2d(world, character, gate, tick);
    }

    println!("2d character replay {}", fr2_world_hash(world) == at_end);
    fr2_world_destroy(world);
}

unsafe fn run_2d() {
    let world = fr2_world_create(0.0, -9.81);
    let ground_points = [-50.0, 3.0, -10.0, 0.0, 10.0, 0.5, 50.0, 2.0];
    let ground = collider_2d(4, [0.0; 2], 0.0, 0.0, &ground_points);
    let mut first = 0;
    fr2_world_add_static(world, &ground, 1, &mut first);

    let polygon_points = [-0.4, -0.3, 0.4, -0.3, 0.5, 0.2, 0.0, 0.5, -0.5, 0.2];
    let mut bodies = Vec::new();
    for index in 0..60u32 {
        let x = (index % 9) as f32 * 1.125 - 4.0;
        let y = 6.0 + index as f32 * 0.875;
        let shape = match index % 4 {
            0 => collider_2d(0, [0.4, 0.3], 0.0, 0.0, &[]),
            1 => collider_2d(1, [0.0; 2], 0.45, 0.0, &[]),
            2 => collider_2d(2, [0.0; 2], 0.3, 0.4, &[]),
            _ => collider_2d(3, [0.0; 2], 0.0, 0.0, &polygon_points),
        };
        let mass = 1.0 + index as f32 * 0.0625;
        bodies.push(fr2_body_create(world, 0, x, y, index as f32 * 0.125, mass, 0, 1.0, 0.02, 0.03, false, &shape, 1));
    }

    let mut saved = Vec::new();
    let mut at_end = 0;
    for tick in 1..=STEPS {
        step_2d(world, &bodies, tick);
        if tick % REPORT_EVERY == 0 {
            println!("2d {} {:016x}", tick, fr2_world_hash(world));
        }

        if tick == SNAPSHOT_AT {
            saved = snapshot_2d(world);
        }

        if tick == STEPS {
            at_end = fr2_world_hash(world);
        }
    }

    assert!(fr2_world_load(world, saved.as_ptr(), saved.len() as u32));
    for tick in SNAPSHOT_AT + 1..=STEPS {
        step_2d(world, &bodies, tick);
    }

    println!("2d replay {}", fr2_world_hash(world) == at_end);
    queries_2d(world, bodies[0]);
    forces_2d(world, &bodies);
    ccd_2d(world);
    contacts_2d(world, &bodies);
    fr2_world_destroy(world);
    joints_2d();
    character_2d();
}

unsafe fn joints_2d() {
    let world = fr2_world_create(0.0, -9.81);
    let link = collider_2d(0, [0.4, 0.1], 0.0, 0.0, &[]);
    let mut links = Vec::new();
    for index in 0..5u32 {
        links.push(fr2_body_create(world, 0, 0.5 + index as f32, 10.0, 0.0, 1.0, 0, 1.0, 0.0, 0.0, false, &link, 1));
    }

    let mut hinge = joint_2d(1, [-0.5, 0.0], [0.0, 10.0]);
    hinge.limit = limit(-1.2, 1.2);
    hinge.motor = velocity_motor(0.5);
    let hinge_id = fr2_joint_create(world, links[0], 0, &hinge);
    let mut slider = joint_2d(2, [0.5, 0.0], [-0.5, 0.0]);
    slider.limit = limit(-0.2, 0.2);
    slider.motor = position_motor(0.1);
    fr2_joint_create(world, links[0], links[1], &slider);
    let mut weld = joint_2d(0, [0.5, 0.0], [-0.5, 0.0]);
    weld.softness = FrJointSoftness { frequency: 10.0, damping_ratio: 1.0 };
    fr2_joint_create(world, links[1], links[2], &weld);
    let mut rope = joint_2d(4, [0.5, 0.0], [-0.5, 0.0]);
    rope.length = 1.5;
    let rope_id = fr2_joint_create(world, links[2], links[3], &rope);
    let mut spring = joint_2d(5, [0.5, 0.0], [-0.5, 0.0]);
    spring.length = 1.0;
    spring.stiffness = 30.0;
    spring.damping = 2.0;
    fr2_joint_create(world, links[3], links[4], &spring);

    let mut saved = Vec::new();
    let mut late = 0;
    let mut at_snapshot = 0;
    for tick in 1..=JOINT_STEPS {
        if tick == JOINT_SNAPSHOT_AT + 1 {
            let mut tether = joint_2d(4, [0.5, 0.0], [7.0, 12.0]);
            tether.length = 3.0;
            late = fr2_joint_create(world, links[4], 0, &tether);
        }

        if tick == JOINT_RETUNE_AT {
            hinge.motor = velocity_motor(-1.0);
            assert!(fr2_joint_set(world, hinge_id, &hinge));
        }

        fr2_world_step(world, SECONDS);
        if tick == JOINT_SNAPSHOT_AT {
            at_snapshot = fr2_world_hash(world);
            saved = snapshot_2d(world);
        }
    }

    let at_end = fr2_world_hash(world);
    println!("2d joints {:016x} {:016x}", at_snapshot, at_end);
    assert!(fr2_world_load(world, saved.as_ptr(), saved.len() as u32));
    for tick in JOINT_SNAPSHOT_AT + 1..=JOINT_STEPS {
        if tick == JOINT_RETUNE_AT {
            assert!(fr2_joint_set(world, hinge_id, &hinge));
        }

        fr2_world_step(world, SECONDS);
    }

    println!("2d joints replay {}", fr2_world_hash(world) == at_end);
    assert!(fr2_joint_remove(world, rope_id));
    assert!(fr2_world_load(world, saved.as_ptr(), saved.len() as u32));
    println!("2d joints load {} {}", fr2_joint_contains(world, late), fr2_joint_contains(world, rope_id));
    fr2_world_destroy(world);
}

fn main() {
    unsafe {
        run_3d();
        run_2d();
    }
}
