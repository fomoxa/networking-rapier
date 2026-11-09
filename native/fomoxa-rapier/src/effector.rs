use rapier2d::prelude::*;
use serde::{Deserialize, Serialize};

pub const EFFECTOR_PLATFORM: u32 = 1;
pub const EFFECTOR_SURFACE: u32 = 2;
pub const EFFECTOR_AREA: u32 = 3;
pub const EFFECTOR_BUOYANCY: u32 = 4;
pub const EFFECTOR_POINT: u32 = 5;

pub const SELECT_COLLIDER: u32 = 1;

pub const FORCE_INVERSE_LINEAR: u32 = 1;
pub const FORCE_INVERSE_SQUARED: u32 = 2;

pub const CONTACT_UNKNOWN: u32 = 0;
pub const CONTACT_ALLOWED: u32 = 1;
pub const CONTACT_FORBIDDEN: u32 = 2;

#[repr(C)]
#[derive(Clone, Copy, Default, Serialize, Deserialize)]
pub struct FrEffector2D {
    pub kind: u32,
    pub use_collider_mask: u32,
    pub collider_mask: u32,
    pub rotation: f32,
    pub origin: [f32; 2],
    pub one_way: u32,
    pub one_way_grouping: u32,
    pub rotational_offset: f32,
    pub surface_arc: f32,
    pub side_friction: u32,
    pub side_bounce: u32,
    pub side_arc: f32,
    pub speed: f32,
    pub force_scale: f32,
    pub contact_force: u32,
    pub friction: u32,
    pub bounce: u32,
    pub force_angle: f32,
    pub global_angle: u32,
    pub force_magnitude: f32,
    pub force_source: u32,
    pub force_target: u32,
    pub force_mode: u32,
    pub distance_scale: f32,
    pub linear_damping: f32,
    pub angular_damping: f32,
    pub density: f32,
    pub surface_level: f32,
    pub flow_angle: f32,
    pub flow_magnitude: f32,
}

pub fn valid_effector(effector: &FrEffector2D) -> bool {
    let finite = [
        effector.rotation,
        effector.origin[0],
        effector.origin[1],
        effector.rotational_offset,
        effector.surface_arc,
        effector.side_arc,
        effector.speed,
        effector.force_scale,
        effector.force_angle,
        effector.force_magnitude,
        effector.distance_scale,
        effector.linear_damping,
        effector.angular_damping,
        effector.density,
        effector.surface_level,
        effector.flow_angle,
        effector.flow_magnitude,
    ]
    .iter()
    .all(|value| value.is_finite());
    finite
        && (EFFECTOR_PLATFORM..=EFFECTOR_POINT).contains(&effector.kind)
        && effector.force_source <= SELECT_COLLIDER
        && effector.force_target <= SELECT_COLLIDER
        && effector.force_mode <= FORCE_INVERSE_SQUARED
        && effector.linear_damping >= 0.0
        && effector.angular_damping >= 0.0
        && effector.density >= 0.0
        && effector.distance_scale >= 0.0
}

pub fn uses_contact_hooks(effector: &FrEffector2D) -> bool {
    effector.kind == EFFECTOR_PLATFORM || effector.kind == EFFECTOR_SURFACE
}

pub fn direction(angle: f32) -> Vector {
    Vector::new(libm::cosf(angle), libm::sinf(angle))
}

pub fn up_of(angle: f32) -> Vector {
    Vector::new(-libm::sinf(angle), libm::cosf(angle))
}

pub fn within_arc(normal: Vector, axis: Vector, arc: f32) -> bool {
    normal.dot(axis) >= libm::cosf(arc * 0.5)
}

pub fn falloff(mode: u32, distance: f32) -> f32 {
    let scaled = distance.max(1.0);
    match mode {
        FORCE_INVERSE_LINEAR => 1.0 / scaled,
        FORCE_INVERSE_SQUARED => 1.0 / (scaled * scaled),
        _ => 1.0,
    }
}

pub fn submerged(shape: &dyn Shape, pose: &Pose, surface: f32) -> Option<(f32, Vector)> {
    let found = match shape.shape_type() {
        ShapeType::Ball => {
            let radius = shape.as_ball()?.radius;
            submerged_disc(pose.translation, radius, surface)
        }
        ShapeType::Cuboid => {
            let half = shape.as_cuboid()?.half_extents;
            submerged_box(pose, half, surface)
        }
        ShapeType::Capsule => {
            let capsule = shape.as_capsule()?;
            let half_height = capsule.half_height();
            submerged_box(pose, Vector::new(capsule.radius, half_height + capsule.radius), surface)
        }
        ShapeType::ConvexPolygon => {
            let points: Vec<Vector> = shape.as_convex_polygon()?.points().iter().map(|point| pose * *point).collect();
            submerged_polygon(&points, surface)
        }
        _ => None,
    };
    found.filter(|(area, _)| *area > 0.0)
}

fn submerged_disc(center: Vector, radius: f32, surface: f32) -> Option<(f32, Vector)> {
    let depth = surface - center.y;
    if depth <= -radius {
        return None;
    }

    if depth >= radius {
        return Some((core::f32::consts::PI * radius * radius, center));
    }

    let squared = radius * radius;
    let chord = libm::sqrtf(squared - depth * depth);
    let area = squared * libm::acosf(-depth / radius) + depth * chord;
    let below = 2.0 / 3.0 * chord * chord * chord / area;
    Some((area, Vector::new(center.x, center.y - below)))
}

fn submerged_box(pose: &Pose, half: Vector, surface: f32) -> Option<(f32, Vector)> {
    let corners = [
        pose * Vector::new(-half.x, -half.y),
        pose * Vector::new(half.x, -half.y),
        pose * Vector::new(half.x, half.y),
        pose * Vector::new(-half.x, half.y),
    ];
    submerged_polygon(&corners, surface)
}

fn submerged_polygon(points: &[Vector], surface: f32) -> Option<(f32, Vector)> {
    if points.len() <= 16 {
        clip_below(points, surface, &mut [Vector::ZERO; 18])
    } else {
        clip_below(points, surface, &mut vec![Vector::ZERO; points.len() + 2])
    }
}

fn clip_below(points: &[Vector], surface: f32, clipped: &mut [Vector]) -> Option<(f32, Vector)> {
    let mut count = 0;
    for index in 0..points.len() {
        let current = points[index];
        let next = points[(index + 1) % points.len()];
        let current_inside = current.y <= surface;
        let next_inside = next.y <= surface;
        if current_inside {
            clipped[count] = current;
            count += 1;
        }

        if current_inside != next_inside {
            let t = (surface - current.y) / (next.y - current.y);
            clipped[count] = Vector::new(current.x + (next.x - current.x) * t, surface);
            count += 1;
        }
    }

    if count < 3 {
        return None;
    }

    let origin = clipped[0];
    let mut area = 0.0;
    let mut centroid = Vector::ZERO;
    for index in 1..count - 1 {
        let first = clipped[index] - origin;
        let second = clipped[index + 1] - origin;
        let triangle = 0.5 * (first.x * second.y - first.y * second.x);
        area += triangle;
        centroid += (first + second) * (triangle / 3.0);
    }

    if area.abs() <= f32::EPSILON {
        return None;
    }

    Some((area.abs(), origin + centroid / area))
}
