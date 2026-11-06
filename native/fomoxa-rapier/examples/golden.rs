use std::ptr;

use fomoxa_rapier::*;

const STEPS: u32 = 900;
const REPORT_EVERY: u32 = 150;
const SNAPSHOT_AT: u32 = 450;
const SECONDS: f32 = 1.0 / 30.0;
const IDENTITY: [f32; 4] = [0.0, 0.0, 0.0, 1.0];

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
        bodies.push(fr_body_create(world, 0, position.as_ptr(), rotation.as_ptr(), mass, 0, 1.0, 0.02, 0.03, &shape, 1));
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

    let mut hit = FrRayHit::default();
    let origin = [0.5, 40.0, 0.25];
    let direction = [0.02, -1.0, 0.01];
    let found = fr_world_raycast(world, origin.as_ptr(), direction.as_ptr(), 100.0, &mut hit);
    println!("3d ray {} {} {:08x}", found, hit.body, hit.distance.to_bits());
    fr_world_destroy(world);
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
        bodies.push(fr2_body_create(world, 0, x, y, index as f32 * 0.125, mass, 0, 1.0, 0.02, 0.03, &shape, 1));
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
    fr2_world_destroy(world);
}

fn main() {
    unsafe {
        run_3d();
        run_2d();
    }
}
