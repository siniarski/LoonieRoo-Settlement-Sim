#!/usr/bin/env bash
# The check: run it before every commit (a red check means stop and fix).
#   ./check.sh         build everything (the sim, its tests, the Godot game) and run the sim's tests
#   ./check.sh --all   and the Godot self-test: real input through the running game (a window opens for a few seconds)
# Godot is $GODOT, else the usual Windows install.
set -euo pipefail
cd "$(dirname "$0")"

dotnet build cs/LoonieRoo.slnx -c Release -v q -nologo
dotnet test cs/Sim.Tests -c Release --no-build -v q -nologo

if [ "${1:-}" = "--all" ]; then
  GODOT="${GODOT:-/c/Program Files/Godot_v4.6.1-stable_mono_win64/Godot_v4.6.1-stable_mono_win64_console.exe}"
  [ -x "$GODOT" ] || { echo "no Godot at $GODOT (set GODOT)"; exit 1; }
  dotnet build godot/LoonieRoo.csproj -v q -nologo
  # Import the assets first: a fresh checkout or worktree has no godot/.godot/imported, and new models
  # from a merge need importing too. ~9 s the first time, ~3 s when there's nothing new.
  timeout 600 "$GODOT" --headless --path godot --import >/dev/null 2>&1 || { echo "importing the assets failed: run $GODOT --headless --path godot --import to see why"; exit 1; }
  code=0
  out=$(timeout 180 "$GODOT" --path godot -- --selftest 2>&1) || code=$?
  echo "$out" | grep -E '^(PASS|FAIL|selftest)' || true
  [ "$code" = 124 ] && { echo "self-test timed out after 180 s (the last PASS/FAIL above is where it stopped)"; exit 1; }
  echo "$out" | grep -q '^selftest: all passed' || { echo "$out" | grep -q '^selftest' || echo "no self-test result (exit $code): see the Godot log"; exit 1; }
fi
echo "check: green"
