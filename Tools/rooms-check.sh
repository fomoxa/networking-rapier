#!/usr/bin/env bash
set -uo pipefail

UNITY_VERSION="6000.5.7f1"
WIN_USER="admin"
PORT=7794
CLIENTS=4
MODE="${1:-all}"
INSTANCES="${ROOMS_INSTANCES:-1 2 4 8 16 32}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$SCRIPT_DIR/.."
CORE="${FOMOXA_CORE:-$REPO/../unity/com.fomoxa.networking}"
DST="/mnt/c/Users/$WIN_USER/unity-check-rapier-rooms"
WIN_ROOT="C:\\Users\\$WIN_USER\\unity-check-rapier-rooms"
PROJECT="$WIN_ROOT\\networking-rapier\\checks\\rooms"
UNITY="/mnt/c/Program Files/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity.exe"
PLAYER="$DST/networking-rapier/checks/rooms/Builds/Rooms/Rooms.exe"
LOGS="$REPO/checks/rooms/Logs"

case "$MODE" in
  all|check|measure) ;;
  *)
    echo "usage: $0 [all|check|measure]" >&2
    exit 2
    ;;
esac

if [ ! -d "/mnt/c/Program Files/Unity/Hub/Editor/$UNITY_VERSION/Editor/Data/PlaybackEngines/WindowsStandaloneSupport" ]; then
  echo "Windows Build Support is not installed for Unity $UNITY_VERSION"
  exit 2
fi

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
  --exclude='checks/player' \
  --exclude='checks/rooms/Library' \
  --exclude='checks/rooms/Logs' \
  --exclude='checks/rooms/Temp' \
  --exclude='checks/rooms/UserSettings' \
  --exclude='checks/rooms/Builds' \
  --exclude='checks/rooms/Assets/Models' \
  --exclude='checks/rooms/Assets/Models.meta' \
  "$REPO/" "$DST/networking-rapier/"
rsync -a --delete "$CORE/" "$DST/unity/com.fomoxa.networking/"
rm -f "$DST"/rooms-*.txt "$DST"/rooms-*.log "$DST"/generate.log "$DST"/build.log

unity_step() {
  local name="$1" method="$2" code
  (
    cd "$DST"
    "$UNITY" -batchmode -nographics -quit -buildTarget Win64 \
      -projectPath "$PROJECT" \
      -executeMethod "$method" \
      -logFile "$WIN_ROOT\\$name.log" < /dev/null > /dev/null 2>&1
  )
  code=$?
  cp "$DST/$name.log" "$LOGS/rooms-check-$name.log" 2>/dev/null || true
  if [ "$code" -ne 0 ]; then
    grep -n "error CS\|failed\|Error building\|BuildFailedException\|\[Fomoxa\]" "$LOGS/rooms-check-$name.log" | head -20
    echo "rooms-check: $name failed with exit code $code, log: $LOGS/rooms-check-$name.log"
    exit 1
  fi
}

mkdir -p "$DST/networking-rapier/checks/rooms/Assets/Models"
unity_step generate Fomoxa.Unity.Editor.FomoxaHook.GenerateFromMenu
if [ ! -f "$DST/networking-rapier/checks/rooms/Assets/Generated/FomoxaAdapters.cs" ]; then
  grep -n "\[Fomoxa\]" "$LOGS/rooms-check-generate.log" | head -10
  echo "rooms-check: the Fomoxa code generation wrote no adapters, log: $LOGS/rooms-check-generate.log"
  exit 1
fi
unity_step build FomoxaChecks.Editor.RoomsCheckBuild.BuildAll
if [ ! -f "$PLAYER" ]; then
  echo "rooms-check: the build wrote no player, log: $LOGS/rooms-check-build.log"
  exit 1
fi

start_player() {
  local name="$1"
  shift
  "$PLAYER" -batchmode -nographics -logFile "$WIN_ROOT\\rooms-$name.log" "$@" < /dev/null > /dev/null 2>&1 &
}

stop_players() {
  powershell.exe -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { \$_.Name -eq 'Rooms.exe' -and \$_.CommandLine -like '*unity-check-rapier-rooms*' } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force }" > /dev/null 2>&1 || true
}

wait_for() {
  local seconds="$1"
  shift
  for second in $(seq 1 "$seconds"); do
    local missing=0
    for file in "$@"; do
      [ -f "$file" ] || missing=1
    done
    [ $missing -eq 0 ] && return 0
    sleep 1
  done
  return 1
}

result_of() {
  cat "$1" 2>/dev/null || echo "no result (timed out)"
}

run_check() {
  local results=("$DST/rooms-server.txt") failed=0
  start_player server -fomoxa-rooms-server $PORT "$WIN_ROOT\\rooms-server.txt"
  for client in $(seq 1 $CLIENTS); do
    start_player "client-$client" -fomoxa-rooms-client $PORT "$WIN_ROOT\\rooms-client-$client.txt"
    results+=("$DST/rooms-client-$client.txt")
  done

  wait_for 300 "${results[@]}"
  stop_players
  wait

  echo "rooms-check: server: $(result_of "$DST/rooms-server.txt")"
  grep -q "^PASS" "$DST/rooms-server.txt" 2>/dev/null || failed=1
  for client in $(seq 1 $CLIENTS); do
    echo "rooms-check: client $client: $(result_of "$DST/rooms-client-$client.txt")"
    grep -q "^PASS" "$DST/rooms-client-$client.txt" 2>/dev/null || failed=1
  done

  cp "$DST"/rooms-server.log "$DST"/rooms-client-*.log "$LOGS/" 2>/dev/null || true
  return $failed
}

run_measure() {
  local failed=0 summary="$LOGS/rooms-check-measure.txt"
  : > "$summary"
  for count in $INSTANCES; do
    local result="$DST/rooms-measure-$count.txt"
    start_player "measure-$count" -fomoxa-rooms-measure $PORT "$count" "$WIN_ROOT\\rooms-measure-$count.txt"
    wait_for 300 "$result"
    stop_players
    wait
    echo "rooms-check: $(result_of "$result")" | tee -a "$summary"
    grep -q "^PASS" "$result" 2>/dev/null || failed=1
    cp "$DST/rooms-measure-$count.log" "$LOGS/" 2>/dev/null || true
  done

  return $failed
}

CHECK=0
MEASURE=0
if [ "$MODE" != "measure" ]; then
  run_check
  CHECK=$?
fi

if [ "$MODE" != "check" ]; then
  run_measure
  MEASURE=$?
fi

echo "rooms-check: logs in $LOGS"
[ $CHECK -eq 0 ] && [ $MEASURE -eq 0 ]
