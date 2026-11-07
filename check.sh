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
  dotnet build godot/LoonieRoo.csproj -v q -nologo
  out=$(timeout 180 "$GODOT" --path godot -- --selftest 2>&1 || true)
  echo "$out" | grep -E '^(PASS|FAIL|selftest)' || { echo "no self-test output: is Godot at $GODOT?"; exit 1; }
  echo "$out" | grep -q '^selftest: all passed' || exit 1
fi
echo "check: green"
