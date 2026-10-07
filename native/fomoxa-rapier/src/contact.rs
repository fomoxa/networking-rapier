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
