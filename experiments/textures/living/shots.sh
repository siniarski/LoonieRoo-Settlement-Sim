#!/usr/bin/env bash
# The living things' pictures, the same before and after:  experiments/textures/living/shots.sh before|after [which]
# (run from the repository's root; pictures land in experiments/textures/living/<name>-<view>.png).
# which: all (default), people, animals, trees.
set -uo pipefail
n="${1:-after}"
which="${2:-all}"
d=experiments/textures/living
G="${GODOT:-/c/Program Files/Godot_v4.6.1-stable_mono_win64/Godot_v4.6.1-stable_mono_win64_console.exe}"
dotnet build godot/LoonieRoo.csproj -v q -nologo > .check/shot-build.log 2>&1 || { grep ' error ' .check/shot-build.log | head; exit 1; }
abs() { cygpath -m "$PWD/$1"; }
lab() { out="$1"; shift; timeout 120 "$G" --path godot res://Labs/LookLab.tscn -- --cand=pz --nopanel --light=day --frames=40 --shot="$(abs "$d/$n-$out.png")" "$@" > .check/lab.log 2>&1 || echo "lab $out failed"; }
if [ "$which" = all ] || [ "$which" = people ]; then
  lab survivor-close --subject=survivor --clothes=jacket --dist=1.2 --aimat=1.2
  lab survivor-5m --subject=survivor --clothes=jacket --dist=5
  lab dead-close --subject=zombie --dist=1.2 --aimat=1.2
  lab dead-5m --subject=zombie --dist=5
  lab dead-lineup --cand=dead --dist=3
  lab people-lineup --cand=people --dist=3
  # Two poses of the same body (walking at you; the cloth's grain must stay put on it).
  lab dead-pose1 --subject=zombie --move=walk --frames=30 --dist=1.5 --aimat=1.0
  lab dead-pose2 --subject=zombie --move=walk --frames=48 --dist=1.5 --aimat=1.0
fi
if [ "$which" = all ] || [ "$which" = animals ]; then
  # One of each round you, held still (where they stand: --trace=1 --trace-near=8), each seen from 2-3 m south of it.
  a="--seed=42 --size=256 --start=solo --at=150,60 --day=1.3 --animals=deer,wolf,bear,dog,rabbit --animal-range=3 --paused --frames=70 --look=270"
  ./shot.sh $a --eye=152.5,62.0,1.3 --pitch=-18 --shot=$d/$n-deer.png
  ./shot.sh $a --eye=146.5,64.6,1.2 --pitch=-18 --shot=$d/$n-wolf.png
  ./shot.sh $a --eye=150.5,57.2,1.5 --pitch=-12 --shot=$d/$n-bear.png
  ./shot.sh $a --eye=151.5,63.5,1.0 --pitch=-22 --shot=$d/$n-dog.png
  ./shot.sh $a --eye=144.5,59.8,0.6 --pitch=-22 --shot=$d/$n-rabbit.png
fi
if [ "$which" = all ] || [ "$which" = trees ]; then
  ./shot.sh --seed=42 --size=256 --start=solo --at=150,60 --look=300 --pitch=-8 --day=1.3 --frames=70 --shot=$d/$n-trees-close.png
  ./shot.sh --seed=42 --size=256 --start=solo --at=150,60 --look=330 --pitch=2 --day=1.3 --frames=70 --shot=$d/$n-trees-far.png
fi
