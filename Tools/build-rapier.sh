#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$SCRIPT_DIR/.."
CRATE="$REPO/native/fomoxa-rapier"
PLUGINS="$REPO/com.fomoxa.networking.rapier/Runtime/Plugins"

build_linux() {
  cargo build --release --manifest-path "$CRATE/Cargo.toml" --target x86_64-unknown-linux-gnu
  mkdir -p "$PLUGINS/Linux/x86_64"
  cp "$CRATE/target/x86_64-unknown-linux-gnu/release/libfomoxa_rapier.so" "$PLUGINS/Linux/x86_64/"
}

build_windows() {
  local work
  work="$(mktemp -d /mnt/c/Users/Public/fomoxa-rapier-XXXXXX)"
  cp -r "$CRATE/Cargo.toml" "$CRATE/src" "$work/"
  if [ -f "$CRATE/Cargo.lock" ]; then
    cp "$CRATE/Cargo.lock" "$work/"
  fi
  (cd "$work" && cargo.exe build --release)
  mkdir -p "$PLUGINS/Windows/x86_64"
  cp "$work/target/release/fomoxa_rapier.dll" "$PLUGINS/Windows/x86_64/"
  rm -rf "$work"
}

case "${1:-linux}" in
  linux) build_linux ;;
  windows) build_windows ;;
  all) build_linux; build_windows ;;
  *) echo "usage: $0 [linux|windows|all]" >&2; exit 2 ;;
esac
