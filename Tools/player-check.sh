#!/usr/bin/env bash
set -euo pipefail

UNITY_VERSION="6000.5.7f1"
WIN_USER="admin"
PRODUCT="RapierPlayerCheck"
ANDROID_IDENTIFIER="com.fomoxa.rapier.playercheck"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$SCRIPT_DIR/.."
CORE="${FOMOXA_CORE:-$REPO/../unity/com.fomoxa.networking}"
DST="/mnt/c/Users/$WIN_USER/unity-check-rapier-player"
WIN_ROOT="C:\\Users\\$WIN_USER\\unity-check-rapier-player"
EDITOR="/mnt/c/Program Files/Unity/Hub/Editor/$UNITY_VERSION/Editor"
UNITY="$EDITOR/Unity.exe"
ADB="$EDITOR/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
LOGS="$REPO/checks/player/Logs"

sync_sources() {
  mkdir -p "$DST/unity" "$DST/networking-rapier" "$LOGS"
  rsync -a --delete \
    --exclude='.git' \
    --exclude='test-project' \
    --exclude='tests/bin' \
    --exclude='tests/obj' \
    --exclude='native/fomoxa-rapier/target' \
    --exclude='checks/two-sided/bin' \
    --exclude='checks/two-sided/obj' \
    --exclude='checks/two-sided/out' \
    --exclude='checks/player/Library' \
    --exclude='checks/player/Logs' \
    --exclude='checks/player/Temp' \
    --exclude='checks/player/UserSettings' \
    --exclude='checks/player/ProjectSettings' \
    "$REPO/" "$DST/networking-rapier/"
  rsync -a --delete "$CORE/" "$DST/unity/com.fomoxa.networking/"
}

keep_meta_files() {
  rsync -rt --chmod=D755,F644 --exclude='Generated/' --exclude='Generated.meta' --include='*/' --include='*.meta' --exclude='*' \
    "$DST/networking-rapier/checks/player/Assets/" "$REPO/checks/player/Assets/"
}

build() {
  local name="$1" build_target="$2" code
  rm -rf "$DST/builds/$name"
  set +e
  (
    cd "$DST"
    "$UNITY" -batchmode -nographics -buildTarget "$build_target" \
      -projectPath "$WIN_ROOT\\networking-rapier\\checks\\player" \
      -executeMethod Fomoxa.Rapier.PlayerCheck.Editor.PlayerCheckBuild.Run \
      -checkTarget "$name" \
      -checkOutput "$WIN_ROOT\\builds\\$name" \
      -checkGolden "$WIN_ROOT\\networking-rapier\\native\\fomoxa-rapier\\tests\\golden.txt" \
      -logFile "$WIN_ROOT\\build-$name.log" < /dev/null > /dev/null 2>&1
  )
  code=$?
  set -e
  cp "$DST/build-$name.log" "$LOGS/build-$name.log" 2>/dev/null || true
  keep_meta_files
  if [ "$code" -ne 0 ]; then
    grep -n "error CS\|Error\|\[player-check-build\]" "$LOGS/build-$name.log" | tail -20 || true
    echo "player-check: $name: the build failed with exit code $code, log: $LOGS/build-$name.log"
    exit 1
  fi
}

expect_windows_library() {
  local name="$1" machine="$2" found kind
  found="$(find "$DST/builds/$name" -name fomoxa_rapier.dll)"
  kind="$(file -b "$found" 2>/dev/null || true)"
  if [ "$(printf '%s\n' "$found" | grep -c .)" -ne 1 ] || [[ "$kind" != *"$machine"* ]]; then
    echo "player-check: $name: expected one $machine fomoxa_rapier.dll, found: ${found:-none}"
    exit 1
  fi
  echo "player-check: $name: ${found#"$DST/builds/$name/"} is $machine"
}

expect_android_library() {
  local name="$1" abi="$2" libraries
  libraries="$(python3 -c '
import sys, zipfile
print("\n".join(n for n in zipfile.ZipFile(sys.argv[1]).namelist() if n.endswith("/libfomoxa_rapier.so")))
' "$DST/builds/$name/$PRODUCT.apk")"
  if [ "$libraries" != "lib/$abi/libfomoxa_rapier.so" ]; then
    echo "player-check: $name: expected only lib/$abi/libfomoxa_rapier.so in the APK, found: ${libraries:-none}"
    exit 1
  fi
  echo "player-check: $name: the APK holds $libraries"
}

verdict() {
  local name="$1" code="$2" log="$3" lines
  lines="$(tr -d '\r' < "$log" | grep -o '\[player-check\] .*' || true)"
  echo "$lines"
  if [ "$code" -eq 0 ] && [[ "$lines" == *"[player-check] PASS"* ]]; then
    echo "player-check: $name: PASS"
  else
    echo "player-check: $name: FAIL, log: $log"
    exit 1
  fi
}

run_windows() {
  local name="$1" code
  set +e
  (
    cd "$DST"
    "$DST/builds/$name/$PRODUCT.exe" -batchmode -nographics -logFile "$WIN_ROOT\\run-$name.log" < /dev/null > /dev/null 2>&1
  )
  code=$?
  set -e
  cp "$DST/run-$name.log" "$LOGS/run-$name.log"
  verdict "$name" "$code" "$LOGS/run-$name.log"
}

run_android() {
  local name="$1" abi="$2" deadline devices abis log
  devices="$("$ADB" devices | tr -d '\r')"
  if ! grep -q $'\tdevice$' <<< "$devices"; then
    echo "player-check: $name: built; no Android device is connected, so it did not run"
    return
  fi

  abis="$("$ADB" shell getprop ro.product.cpu.abilist | tr -d '\r' | tr ',' '\n')"
  if ! grep -qx "$abi" <<< "$abis"; then
    echo "player-check: $name: built; the connected device does not run $abi"
    return
  fi

  "$ADB" install -r "$WIN_ROOT\\builds\\$name\\$PRODUCT.apk"
  "$ADB" logcat -c
  "$ADB" shell monkey -p "$ANDROID_IDENTIFIER" -c android.intent.category.LAUNCHER 1 > /dev/null
  deadline=$((SECONDS + 120))
  while [ "$SECONDS" -lt "$deadline" ]; do
    log="$("$ADB" logcat -d -s Unity | tr -d '\r')"
    if grep -q '\[player-check\] \(PASS\|FAIL\)' <<< "$log"; then
      break
    fi
    sleep 2
  done

  "$ADB" logcat -d -s Unity | tr -d '\r' > "$LOGS/run-$name.log"
  "$ADB" shell am force-stop "$ANDROID_IDENTIFIER"
  "$ADB" uninstall "$ANDROID_IDENTIFIER" > /dev/null
  verdict "$name" 0 "$LOGS/run-$name.log"
}

serve_web() {
  echo "player-check: webgl: built. Open http://localhost:8000 in a browser: the page shows PASS or FAIL and the hashes. Ctrl+C stops the server."
  python3 -m http.server 8000 --directory "$DST/builds/webgl"
}

case "${1:-}" in
  windows-mono|windows-il2cpp)
    sync_sources
    build "$1" Win64
    expect_windows_library "$1" x86-64
    run_windows "$1"
    ;;
  windows-arm64)
    sync_sources
    build "$1" Win64
    expect_windows_library "$1" ARM64
    echo "player-check: $1: built; this machine cannot run an ARM64 player"
    ;;
  android-arm64)
    sync_sources
    build "$1" Android
    expect_android_library "$1" arm64-v8a
    run_android "$1" arm64-v8a
    ;;
  android-armv7)
    sync_sources
    build "$1" Android
    expect_android_library "$1" armeabi-v7a
    run_android "$1" armeabi-v7a
    ;;
  webgl)
    sync_sources
    build "$1" WebGL
    serve_web
    ;;
  *)
    echo "usage: $0 windows-mono|windows-il2cpp|windows-arm64|android-arm64|android-armv7|webgl" >&2
    exit 2
    ;;
esac
