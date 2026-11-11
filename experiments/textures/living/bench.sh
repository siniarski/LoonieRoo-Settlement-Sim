#!/usr/bin/env bash
# Frame times with a crowd of the dead in view and in a wood, for the living things' textures:
#   experiments/textures/living/bench.sh before|after     (run from the repository's root, twice each)
# The crowd: the look lab's --crowdbench (the dead dressed as the world dresses them, 40 and 200 walking; vsync off).
# The wood: the tree bench's game LODs at the game's radius (a forest over the whole world, a full turn; vsync off).
set -uo pipefail
n="${1:-after}"
d=experiments/textures/living
G="${GODOT:-/c/Program Files/Godot_v4.6.1-stable_mono_win64/Godot_v4.6.1-stable_mono_win64_console.exe}"
dotnet build godot/LoonieRoo.csproj -v q -nologo > .check/shot-build.log 2>&1 || { grep ' error ' .check/shot-build.log | head; exit 1; }
abs() { cygpath -m "$PWD/$1"; }
for k in 1 2; do
  timeout 300 "$G" --path godot res://Labs/LookLab.tscn -- --crowdbench="$(abs "$d/bench-$n-crowd-$k.md")" > .check/bench.log 2>&1 || echo "crowd $k failed"
  timeout 300 "$G" --path godot res://Labs/TreeBench.tscn -- --runs=lods@3 --frames=360 --out="$(abs "$d/bench-$n-wood-$k.md")" > .check/bench.log 2>&1 || echo "wood $k failed"
done
