#[no_mangle]
pub extern "C" fn acosf(x: f32) -> f32 {
    libm::acosf(x)
}

#[no_mangle]
pub extern "C" fn atan2f(y: f32, x: f32) -> f32 {
    libm::atan2f(y, x)
}

#[no_mangle]
pub extern "C" fn cosf(x: f32) -> f32 {
    libm::cosf(x)
}

#[no_mangle]
pub extern "C" fn sinf(x: f32) -> f32 {
    libm::sinf(x)
}

#[no_mangle]
pub unsafe extern "C" fn sincosf(x: f32, sin: *mut f32, cos: *mut f32) {
    let (s, c) = libm::sincosf(x);
    *sin = s;
    *cos = c;
}

#[no_mangle]
pub extern "C" fn expf(x: f32) -> f32 {
    libm::expf(x)
}

#[no_mangle]
pub extern "C" fn log2f(x: f32) -> f32 {
    libm::log2f(x)
}

#[no_mangle]
pub extern "C" fn powf(x: f32, y: f32) -> f32 {
    libm::powf(x, y)
}
