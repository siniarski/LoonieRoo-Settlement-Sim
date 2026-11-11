#!/usr/bin/env bash
# Rebuild the four model files from their Blender scripts and re-import them in Godot (from the repository's root):
#   experiments/textures/models/rebuild.sh [furniture items world cabins]
set -uo pipefail
B="${BLENDER:-/c/Program Files/Blender Foundation/Blender 5.2/blender.exe}"
GODOT="${GODOT:-/c/Program Files/Godot_v4.6.1-stable_mono_win64/Godot_v4.6.1-stable_mono_win64_console.exe}"
for n in ${*:-furniture items world cabins}; do
  case "$n" in
    furniture) out=godot/assets/furniture/furniture.glb;;
    items)     out=godot/assets/items/items.glb;;
    world)     out=godot/assets/world/world.glb;;
    cabins)    out=godot/assets/cars/cabins.glb;;
    *) echo "no model file $n"; continue;;
  esac
  "$B" --background --factory-startup --python art/scripts/build_$n.py -- --glb $out > .check/blender-$n.log 2>&1
  grep -E "Error|Traceback| models, " .check/blender-$n.log | head -5
done
"$GODOT" --headless --path godot --import > .check/import.log 2>&1
grep -iE "^ERROR" .check/import.log | head -5
echo "rebuilt and imported"
