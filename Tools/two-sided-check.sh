#!/bin/bash
set -e

MODE="${1:-linux}"
WIN_USER="admin"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
PORT=7790
CLIENT_SECONDS="${CLIENT_SECONDS:-25}"
WIN_DIR="C:\\Users\\$WIN_USER\\unity-check-rapier\\two-sided"
DIR="/mnt/c/Users/$WIN_USER/unity-check-rapier/two-sided"
SERVER_WIN_ROOT="C:\\Users\\$WIN_USER\\rapier-two-sided"
SERVER_DST="/mnt/c/Users/$WIN_USER/rapier-two-sided"
DOTNET_WIN="/mnt/c/Program Files/dotnet/dotnet.exe"
export LANG=C.UTF-8 LC_ALL=C.UTF-8 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

rm -rf "$DIR"
mkdir -p "$DIR"

case "$MODE" in
  linux)
    HOST="$(hostname -I | awk '{print $1}')"
    dotnet build -c Release "$REPO/checks/two-sided/Fomoxa.Rapier.TwoSided.csproj" > "$DIR/server-build.txt" 2>&1 || { grep -E " error " "$DIR/server-build.txt" | sort -u; exit 1; }
    SERVER_OUT="$REPO/checks/two-sided/out"
    rm -rf "$SERVER_OUT"
    dotnet "$REPO/checks/two-sided/bin/Release/net8.0/Fomoxa.Rapier.TwoSided.dll" --port "$PORT" --out "$SERVER_OUT" > "$DIR/server-console.txt" 2>&1 &
    SERVER_PID=$!
    ;;
  windows)
    HOST="127.0.0.1"
    mkdir -p "$SERVER_DST/unity" "$SERVER_DST/networking-rapier"
    rsync -a --delete --exclude='test-project' --exclude='.git' "$REPO/../unity/com.fomoxa.networking/" "$SERVER_DST/unity/com.fomoxa.networking/"
    rsync -a --delete --exclude='.git' --exclude='test-project' --exclude='native/fomoxa-rapier/target' --exclude='tests' \
      --exclude='checks/two-sided/bin' --exclude='checks/two-sided/obj' --exclude='checks/two-sided/out' "$REPO/" "$SERVER_DST/networking-rapier/"
    sed -i 's|<TargetFramework>net8.0</TargetFramework>|<TargetFramework>net9.0</TargetFramework>|' "$SERVER_DST/networking-rapier/checks/two-sided/Fomoxa.Rapier.TwoSided.csproj"
    "$DOTNET_WIN" build -c Release "$SERVER_WIN_ROOT\\networking-rapier\\checks\\two-sided\\Fomoxa.Rapier.TwoSided.csproj" > "$DIR/server-build.txt" 2>&1 || { grep -E " error " "$DIR/server-build.txt" | sort -u; exit 1; }
    SERVER_OUT="$SERVER_DST/networking-rapier/checks/two-sided/out"
    rm -rf "$SERVER_OUT"
    "$DOTNET_WIN" "$SERVER_WIN_ROOT\\networking-rapier\\checks\\two-sided\\bin\\Release\\net9.0\\Fomoxa.Rapier.TwoSided.dll" --port "$PORT" --out "$SERVER_WIN_ROOT\\networking-rapier\\checks\\two-sided\\out" > "$DIR/server-console.txt" 2>&1 &
    SERVER_PID=$!
    ;;
  *)
    echo "usage: $0 [linux|windows]"
    exit 2
    ;;
esac

for attempt in $(seq 1 100); do
  [ -f "$SERVER_OUT/two-sided.fomoxascene" ] && break
  sleep 0.2
done
cp "$SERVER_OUT/two-sided.fomoxascene" "$DIR/"

export FOMOXA_TWO_SIDED_HOST="$HOST" FOMOXA_TWO_SIDED_PORT="$PORT" FOMOXA_TWO_SIDED_DIR="$WIN_DIR" FOMOXA_TWO_SIDED_SECONDS="$CLIENT_SECONDS"
export WSLENV="FOMOXA_TWO_SIDED_HOST:FOMOXA_TWO_SIDED_PORT:FOMOXA_TWO_SIDED_DIR:FOMOXA_TWO_SIDED_SECONDS${WSLENV:+:$WSLENV}"
TEST_FILTER="Fomoxa.Unity.Rapier.Tests.TwoSidedClientCheck.TheClientPredictsTheBallsOfATwoSidedServer" bash "$SCRIPT_DIR/unity-windows-check.sh" | tail -6

wait "$SERVER_PID" || true
cp "$SERVER_OUT/server.log" "$DIR/server.log"
cat "$DIR/server-console.txt"
grep "\[two-sided\]" "$REPO/test-project/Logs/unity-windows-check.log" || true

python3 - "$DIR/server.log" "$DIR/client.log" <<'PY'
import sys

def read(path):
    firsts, hashes, values = {}, {}, {}
    for line in open(path):
        parts = line.split()
        if parts[0] == "first":
            firsts[int(parts[1])] = int(parts[2])
        elif parts[0] == "unbodied":
            values.setdefault("unbodied", []).append((int(parts[1]), int(parts[2])))
        elif parts[0] in ("mismatches", "applies"):
            values[parts[0]] = int(parts[1])
        else:
            hashes[int(parts[0])] = parts[1]
    return firsts, hashes, values

server_firsts, server, server_values = read(sys.argv[1])
client_firsts, client, client_values = read(sys.argv[2])
print("first applied tick per ball: server", server_firsts, "client", client_firsts)
common = sorted(set(server) & set(client))
different = [tick for tick in common if server[tick] != client[tick]]
start = max(list(server_firsts.values()) + list(client_firsts.values()) or [0])
moving = [tick for tick in common if tick >= start]
print(f"ticks: server {len(server)}, client {len(client)}, common {len(common)}, common after the start {len(moving)}")
print(f"client reconcile mismatches: {client_values.get('mismatches')}")
print(f"applies without a body: server {server_values.get('unbodied', [])} client {client_values.get('unbodied', [])}")
if different:
    print(f"DIFFERENT hashes at {len(different)} ticks, first at {different[0]}: server {server[different[0]]} client {client[different[0]]}")
    sys.exit(1)
if len(moving) < 1000 or client_values.get("mismatches", 1) != 0:
    print("NOT ENOUGH: fewer than 1000 common moving ticks or mismatched reconciles")
    sys.exit(1)
print("PASS: every common tick has the same world hash on both sides")
PY
