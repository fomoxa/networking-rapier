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
