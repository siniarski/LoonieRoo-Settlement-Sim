#!/usr/bin/env bash
# Take "before" pictures after the change: put main's model files and model shaders back for the run, then this
# branch's again (from the repository's root):  experiments/textures/models/as-before.sh VIEW...  (or --run COMMAND...)
set -uo pipefail
GODOT="${GODOT:-/c/Program Files/Godot_v4.6.1-stable_mono_win64/Godot_v4.6.1-stable_mono_win64_console.exe}"
BASE="${BASE:-c481538}"
files="godot/assets/furniture/furniture.glb godot/assets/items/items.glb godot/assets/world/world.glb godot/assets/cars/cabins.glb godot/World3D/ModelSet.cs godot/World3D/Furniture.cs godot/World3D/Items.cs"
keep=.check/as-before
mkdir -p $keep
for f in $files; do cp "$f" "$keep/$(basename $f)"; git show "$BASE:$f" > "$f"; done
"$GODOT" --headless --path godot --import > /dev/null 2>&1
if [ "${1:-}" = "--run" ]; then shift; "$@"; else bash experiments/textures/models/shots.sh before "$@"; fi
for f in $files; do cp "$keep/$(basename $f)" "$f"; done
"$GODOT" --headless --path godot --import > /dev/null 2>&1
echo "restored"
