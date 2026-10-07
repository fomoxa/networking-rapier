#!/bin/bash
set -e

UNITY_VERSION="6000.5.7f1"
WIN_USER="admin"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$SCRIPT_DIR/.."
CORE="${FOMOXA_CORE:-$REPO/../unity/com.fomoxa.networking}"
WIN_ROOT="C:\\Users\\$WIN_USER\\unity-check-rapier"
DST="/mnt/c/Users/$WIN_USER/unity-check-rapier"
UNITY="/mnt/c/Program Files/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity.exe"
LOG_DIR="$REPO/test-project/Logs"
LOG="$LOG_DIR/unity-windows-check.log"
RESULTS="$LOG_DIR/editmode-results.xml"

mkdir -p "$LOG_DIR" "$DST/unity" "$DST/networking-rapier"

rsync -a --delete \
  --exclude='.git' \
  --exclude='test-project/Library' \
  --exclude='test-project/Logs' \
  --exclude='test-project/Temp' \
  --exclude='test-project/UserSettings' \
  --exclude='tests/bin' \
  --exclude='tests/obj' \
  --exclude='native/fomoxa-rapier/target' \
  "$REPO/" "$DST/networking-rapier/"
rsync -a --delete "$CORE/" "$DST/unity/com.fomoxa.networking/"

rm -f "$DST/unity.log" "$DST/editmode-results.xml"

set +e
(
  cd "$DST"
  "$UNITY" -batchmode -nographics \
    -projectPath "$WIN_ROOT\\networking-rapier\\test-project" \
    -runTests -testPlatform EditMode -assemblyNames "Fomoxa.Unity.Rapier.Tests" ${TEST_FILTER:+-testFilter "$TEST_FILTER"} \
    -testResults "$WIN_ROOT\\editmode-results.xml" \
    -logFile "$WIN_ROOT\\unity.log" < /dev/null > /dev/null 2>&1
)
EXIT_CODE=$?
set -e

cp "$DST/unity.log" "$LOG" 2>/dev/null || true
cp "$DST/editmode-results.xml" "$RESULTS" 2>/dev/null || true

rsync -a --include='*/' --include='*.meta' --exclude='*' "$DST/networking-rapier/com.fomoxa.networking.rapier/" "$REPO/com.fomoxa.networking.rapier/"
rsync -a "$DST/networking-rapier/test-project/ProjectSettings/" "$REPO/test-project/ProjectSettings/"
cp "$DST/networking-rapier/test-project/Packages/packages-lock.json" "$REPO/test-project/Packages/" 2>/dev/null || true

echo "=== exit code: $EXIT_CODE ==="
echo "=== compile errors ==="
grep -n "error CS\|Aborting batchmode\|Fatal Error" "$LOG" || echo "none found"
echo "=== test summary ==="
grep -o '<test-run [^>]*>' "$RESULTS" 2>/dev/null | grep -o 'result="[^"]*"\|total="[^"]*"\|passed="[^"]*"\|failed="[^"]*"' || echo "no results file"
echo "=== full log: $LOG ==="
