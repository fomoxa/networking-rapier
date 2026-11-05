fn main() {
    let os = std::env::var("CARGO_CFG_TARGET_OS").unwrap_or_default();
    if os == "linux" || os == "android" {
        println!("cargo:rustc-cdylib-link-arg=-Wl,-Bsymbolic-functions");
    }

    if os == "android" {
        println!("cargo:rustc-cdylib-link-arg=-Wl,-z,max-page-size=16384");
    }
}
