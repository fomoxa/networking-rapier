#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$SCRIPT_DIR/.."
PLUGINS="$REPO/com.fomoxa.networking.rapier/Runtime/Plugins"

if [ "$#" -ne 2 ]; then
  echo "usage: $0 <native-libraries.zip> <commit of the workflow run>" >&2
  exit 2
fi

unzip -o "$1" -d "$PLUGINS"
echo "$2" > "$REPO/native/fomoxa-rapier/built-from.txt"
git -C "$REPO" status --short com.fomoxa.networking.rapier/Runtime/Plugins native/fomoxa-rapier/built-from.txt
