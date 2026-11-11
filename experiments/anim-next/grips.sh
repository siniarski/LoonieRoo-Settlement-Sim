#!/usr/bin/env bash
# Grip sheets: each class of thing in hand, idle, walk and run, from the side and the front (AnimTest --held --sheet).
#   experiments/anim-next/grips.sh before|after
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
GODOT="${GODOT:-/c/Program Files/Godot_v4.6.1-stable_mono_win64/Godot_v4.6.1-stable_mono_win64_console.exe}"
OUT="$(cygpath -m "$ROOT/experiments/anim-next/${1:-after}")"
dotnet build "$ROOT/godot/LoonieRoo.csproj" -v q -nologo > "$ROOT/.check/grips-build.log" 2>&1 || { tail -20 "$ROOT/.check/grips-build.log"; exit 1; }
[ -d "$ROOT/godot/.godot/imported" ] || timeout 600 "$GODOT" --headless --path "$ROOT/godot" --import > /dev/null 2>&1
for kind in ${KINDS:-bat knife axe pistol rifle wood}; do
  timeout 120 "$GODOT" --path "$ROOT/godot" res://Labs/AnimTest.tscn -- --strip=4 --clips=${CLIPS:-more/idle,more/walk,more/run} --views=side,front \
    --held=$kind --sheet=grip-$kind.png --out="$OUT" 2>&1 | grep -E "animtest: right|ERROR|rror" | head -5
done
ls "$OUT"
