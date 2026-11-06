#[repr(C)]
#[derive(Clone, Copy)]
pub struct FrQueryFilter {
    pub layer_mask: u32,
    pub exclude_body: u32,
    pub include_triggers: u32,
}
