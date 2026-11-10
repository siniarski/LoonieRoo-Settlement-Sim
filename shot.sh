#!/usr/bin/env bash
# Take a picture of the game as it is NOW: build the game's Debug config (what Godot runs), then run Godot on it.
#   ./shot.sh --seed=42 --start=solo --ticks=20 --shot=out.png
# Every argument goes to the game, after `--path godot --` (the options: godot/README.md). A window opens for a few
# seconds. Output is short: the build's errors if it failed, the game's own `shot:` line, any exception lines (and their
# first stack lines); nothing else. The whole output is in .check/shot.log.
# Exits non-zero if the build failed, the game timed out or exited with an error, or --shot= was given and no picture
# came out (a stale file of that name is deleted first, so one that exists is new).
#   SHOT_TIMEOUT=300 ./shot.sh ...   the time limit in seconds (default 120)
# Godot is $GODOT, else the usual Windows install. A fresh checkout or worktree has no imported assets: the first run
# imports them (~9 s). Run from anywhere: a relative --shot= or --load= path is relative to where you ran it.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="$ROOT/.check"
mkdir -p "$OUT"
LOG="$OUT/shot.log"
GODOT="${GODOT:-/c/Program Files/Godot_v4.6.1-stable_mono_win64/Godot_v4.6.1-stable_mono_win64_console.exe}"
LIMIT="${SHOT_TIMEOUT:-120}"
[ -x "$GODOT" ] || { echo "shot: no Godot at $GODOT (set GODOT)"; exit 1; }
# The project path as Godot (a Windows program) wants it.
PROJ="$(cd "$ROOT/godot" && { pwd -W 2>/dev/null || pwd; })"

# Godot runs with godot/ as its working directory: a relative --shot= or --load= path would land there, so make them
# absolute (relative to where this was run, as for any command). `shot` is the picture asked for, if any.
shot=""; args=()
for a in "$@"; do
  case "$a" in
    --shot=*|--load=*)
      p="${a#*=}"; case "$p" in /*|[A-Za-z]:*) ;; *) p="$PWD/$p";; esac
      p="$(cygpath -m "$p" 2>/dev/null || echo "$p")"
      a="${a%%=*}=$p"
      case "$a" in --shot=*) shot="$p";; esac;;
  esac
  args+=("$a")
done
[ -n "$shot" ] && rm -f "$shot"

# --- Build (Debug: the binaries Godot runs) ---
if ! dotnet build "$ROOT/godot/LoonieRoo.csproj" -v q -nologo > "$OUT/shot-build.log" 2>&1; then
  grep -E ' error ' "$OUT/shot-build.log" | sed 's/^ *//' | sort -u | head -30
  echo "shot: the Debug build failed ($OUT/shot-build.log)"; exit 1
fi

# --- Import the assets if this checkout has none yet ---
if [ ! -d "$ROOT/godot/.godot/imported" ]; then
  timeout 600 "$GODOT" --headless --path "$PROJ" --import > "$OUT/shot-import.log" 2>&1 \
    || { echo "shot: importing the assets failed ($OUT/shot-import.log)"; exit 1; }
fi

# --- Run ---
code=0
timeout "$LIMIT" "$GODOT" --path "$PROJ" -- "${args[@]}" > "$LOG" 2>&1 || code=$?
grep -E '^shot|Exception|^ERROR|^SCRIPT ERROR|^ +at ' "$LOG" | head -40
if [ "$code" = 124 ]; then echo "shot: timed out after $LIMIT s (SHOT_TIMEOUT; the full output: $LOG)"; exit 1; fi
if [ -n "$shot" ] && [ ! -s "$shot" ]; then echo "shot: no picture at $shot (the full output: $LOG)"; exit 1; fi
if [ "$code" != 0 ]; then echo "shot: the game exited with $code (the full output: $LOG)"; exit 1; fi
exit 0
