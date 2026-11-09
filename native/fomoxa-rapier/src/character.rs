#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct FrCharacterLength {
    pub value: f32,
    pub relative: u32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct FrCharacterAutostep {
    pub enabled: u32,
    pub max_height: FrCharacterLength,
    pub min_width: FrCharacterLength,
    pub include_dynamic_bodies: u32,
}

impl FrCharacterLength {
    pub(crate) fn is_valid(&self) -> bool {
        self.value.is_finite() && self.value >= 0.0
    }
}

pub(crate) fn valid_angle(angle: f32) -> bool {
    angle.is_finite() && angle >= 0.0
}

pub(crate) fn valid_amount(amount: f32) -> bool {
    amount.is_finite() && amount >= 0.0
}

pub(crate) fn slides_on(up_dot_normal: f32, min_slope_slide_angle: f32) -> bool {
    up_dot_normal > 0.0 && libm::acosf(up_dot_normal.min(1.0)) >= min_slope_slide_angle
}
