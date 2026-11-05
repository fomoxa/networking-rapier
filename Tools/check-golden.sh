#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CRATE="$SCRIPT_DIR/../native/fomoxa-rapier"
GOLDEN="$CRATE/tests/golden.txt"

build_example() {
  (cd "$CRATE" && rustup target add "$1" && cargo build --release --locked --example golden --target "$1")
}

example() {
  echo "$CRATE/target/$1/release/examples/$2"
}

compare() {
  diff --strip-trailing-cr "$GOLDEN" - && echo "golden: $1 matches"
}

run_native() {
  build_example "$1"
  "$(example "$1" golden)" | compare "$1"
}

run_ios_simulator() {
  local device
  device="$(xcrun simctl list devices available -j | python3 -c '
import json, sys
runtimes = json.load(sys.stdin)["devices"]
print(next(d["udid"] for name, ds in runtimes.items() if "iOS" in name for d in ds if d["name"].startswith("iPhone")))
')"
  build_example aarch64-apple-ios-sim
  xcrun simctl boot "$device"
  xcrun simctl spawn "$device" "$(example aarch64-apple-ios-sim golden)" | compare aarch64-apple-ios-sim
}

run_android_emulator() {
  local bin="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/linux-x86_64/bin"
  export CARGO_TARGET_X86_64_LINUX_ANDROID_LINKER="$bin/x86_64-linux-android26-clang"
  build_example x86_64-linux-android
  adb push "$(example x86_64-linux-android golden)" /data/local/tmp/golden
  adb shell chmod 755 /data/local/tmp/golden
  adb shell /data/local/tmp/golden | compare x86_64-linux-android
}

run_armv7() {
  export CARGO_TARGET_ARMV7_UNKNOWN_LINUX_MUSLEABIHF_RUSTFLAGS="-C linker=rust-lld -C target-feature=+crt-static"
  build_example armv7-unknown-linux-musleabihf
  qemu-arm "$(example armv7-unknown-linux-musleabihf golden)" | compare armv7-unknown-linux-musleabihf
}

run_web() {
  export CARGO_TARGET_WASM32_UNKNOWN_EMSCRIPTEN_RUSTFLAGS="-C link-args=-sENVIRONMENT=node -C link-args=-sALLOW_MEMORY_GROWTH"
  build_example wasm32-unknown-emscripten
  node "$(example wasm32-unknown-emscripten golden.js)" | compare wasm32-unknown-emscripten
}

case "$1" in
  linux-x64) run_native x86_64-unknown-linux-gnu ;;
  linux-arm64) run_native aarch64-unknown-linux-gnu ;;
  windows-x64) run_native x86_64-pc-windows-msvc ;;
  windows-arm64) run_native aarch64-pc-windows-msvc ;;
  macos-arm64) run_native aarch64-apple-darwin ;;
  macos-x64) run_native x86_64-apple-darwin ;;
  ios-simulator) run_ios_simulator ;;
  android-emulator) run_android_emulator ;;
  armv7) run_armv7 ;;
  web) run_web ;;
  *) echo "usage: $0 linux-x64|linux-arm64|windows-x64|windows-arm64|macos-arm64|macos-x64|ios-simulator|android-emulator|armv7|web" >&2; exit 2 ;;
esac
