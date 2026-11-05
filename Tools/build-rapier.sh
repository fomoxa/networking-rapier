#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$SCRIPT_DIR/.."
CRATE="$REPO/native/fomoxa-rapier"
PLUGINS="${FOMOXA_PLUGINS_OUT:-$REPO/com.fomoxa.networking.rapier/Runtime/Plugins}"
ANDROID_API=26

compile() {
  local target="$1" crate_type="$2"
  (cd "$CRATE" && rustup target add "$target" && cargo rustc --release --locked --target "$target" --crate-type "$crate_type")
}

built() {
  echo "$CRATE/target/$1/release/$2"
}

install_file() {
  mkdir -p "$PLUGINS/$2"
  cp "$1" "$PLUGINS/$2/"
}

use_android_linkers() {
  local bin="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/linux-x86_64/bin"
  export CARGO_TARGET_AARCH64_LINUX_ANDROID_LINKER="$bin/aarch64-linux-android$ANDROID_API-clang"
  export CARGO_TARGET_ARMV7_LINUX_ANDROIDEABI_LINKER="$bin/armv7a-linux-androideabi$ANDROID_API-clang"
}

build_windows_from_wsl() {
  local work
  work="$(mktemp -d /mnt/c/Users/Public/fomoxa-rapier-XXXXXX)"
  cp -r "$CRATE/Cargo.toml" "$CRATE/Cargo.lock" "$CRATE/rust-toolchain.toml" "$CRATE/build.rs" "$CRATE/src" "$work/"
  (cd "$work" && cargo.exe build --release --locked)
  install_file "$work/target/release/fomoxa_rapier.dll" Windows/x86_64
  rm -rf "$work"
}

build_linux() {
  compile "$1" cdylib
  install_file "$(built "$1" libfomoxa_rapier.so)" "Linux/$2"
}

build_windows() {
  compile "$1" cdylib
  install_file "$(built "$1" fomoxa_rapier.dll)" "Windows/$2"
}

build_macos() {
  compile aarch64-apple-darwin cdylib
  compile x86_64-apple-darwin cdylib
  mkdir -p "$PLUGINS/macOS"
  lipo -create \
    "$(built aarch64-apple-darwin libfomoxa_rapier.dylib)" \
    "$(built x86_64-apple-darwin libfomoxa_rapier.dylib)" \
    -output "$PLUGINS/macOS/libfomoxa_rapier.dylib"
}

build_ios() {
  compile aarch64-apple-ios staticlib
  compile aarch64-apple-ios-sim staticlib
  rm -rf "$PLUGINS/iOS/libfomoxa_rapier.xcframework"
  mkdir -p "$PLUGINS/iOS"
  xcodebuild -create-xcframework \
    -library "$(built aarch64-apple-ios libfomoxa_rapier.a)" \
    -library "$(built aarch64-apple-ios-sim libfomoxa_rapier.a)" \
    -output "$PLUGINS/iOS/libfomoxa_rapier.xcframework"
}

build_android() {
  use_android_linkers
  compile aarch64-linux-android cdylib
  compile armv7-linux-androideabi cdylib
  install_file "$(built aarch64-linux-android libfomoxa_rapier.so)" Android/arm64-v8a
  install_file "$(built armv7-linux-androideabi libfomoxa_rapier.so)" Android/armeabi-v7a
}

build_web() {
  compile wasm32-unknown-emscripten staticlib
  install_file "$(built wasm32-unknown-emscripten libfomoxa_rapier.a)" WebGL
}

case "${1:-linux}" in
  linux) build_linux x86_64-unknown-linux-gnu x86_64 ;;
  windows) build_windows_from_wsl ;;
  all) build_linux x86_64-unknown-linux-gnu x86_64; build_windows_from_wsl ;;
  linux-x64) build_linux x86_64-unknown-linux-gnu x86_64 ;;
  linux-arm64) build_linux aarch64-unknown-linux-gnu arm64 ;;
  windows-x64) build_windows x86_64-pc-windows-msvc x86_64 ;;
  windows-arm64) build_windows aarch64-pc-windows-msvc arm64 ;;
  macos) build_macos ;;
  ios) build_ios ;;
  android) build_android ;;
  web) build_web ;;
  *) echo "usage: $0 [linux|windows|all|linux-x64|linux-arm64|windows-x64|windows-arm64|macos|ios|android|web]" >&2; exit 2 ;;
esac
