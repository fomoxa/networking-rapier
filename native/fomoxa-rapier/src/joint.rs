use serde::{Deserialize, Serialize};

pub const JOINT_FIXED: u32 = 0;
pub const JOINT_REVOLUTE: u32 = 1;
pub const JOINT_PRISMATIC: u32 = 2;
pub const JOINT_SPHERICAL: u32 = 3;
pub const JOINT_ROPE: u32 = 4;
pub const JOINT_SPRING: u32 = 5;

#[repr(C)]
#[derive(Clone, Copy, Default, Serialize, Deserialize)]
pub struct FrJointLimit {
    pub enabled: u32,
    pub min: f32,
    pub max: f32,
}

#[repr(C)]
#[derive(Clone, Copy, Default, Serialize, Deserialize)]
pub struct FrJointMotor {
    pub enabled: u32,
    pub target_position: f32,
    pub target_velocity: f32,
    pub stiffness: f32,
    pub damping: f32,
    pub max_force: f32,
}

#[repr(C)]
#[derive(Clone, Copy, Default, Serialize, Deserialize)]
pub struct FrJointSoftness {
    pub frequency: f32,
    pub damping_ratio: f32,
}
