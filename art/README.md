# art/: character assets and the scripts that make them

The plan is docs/characters.md. Blender 5.2 LTS (`C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`);
the Blender MCP server is in the project's `.mcp.json` for interactive work. Anything repeatable is a script here,
run headless:

```bash
B="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
# the Phase 0 test mannequin: build, validate, save the .blend, render previews, export to Godot
"$B" --background --factory-startup --python art/scripts/build_test_mannequin.py -- \
    --blend art/blend/test_mannequin.blend --render preview.png --glb godot/assets/characters/test_mannequin.glb
# validate any character .blend
"$B" art/blend/test_mannequin.blend --background --python art/scripts/validate.py
```

Then in Godot: `--headless --path godot --import` (re-import), and the lab:
`--path godot res://Characters/Lab.tscn -- --check` (skeleton vs the humanoid profile) or `-- --shot=out.png`.

- `scripts/humanoid_profile.json`: Godot 4.6's SkeletonProfileHumanoid (names, parents, reference poses), dumped by
  `dump_profile.gd` (`Godot --headless --script art/scripts/dump_profile.gd -- out.json`). The rig is built from it.
- `scripts/rig.py`: the shared rig. Profile bone names, parents and rest orientations; our proportions (1.75 m).
- `scripts/validate.py`: the rules every character file meets (profile skeleton, applied transforms, at most
  4 weights per vertex and normalised, no unweighted vertices, triangle budget, clip names, no root motion).
- `scripts/build_test_mannequin.py`: the segmented mannequin (regions, stumps, a face, 27 clips), all by script; with
  `--garments godot/assets/characters/garments.glb` it also exports the garments (and garments.json beside them).
- `scripts/garments.py`: the garments, by body region, with the hiding rules; `scripts/shapes.py`: mesh primitives.
- `blend/`: generated .blend files, not in git (the scripts are the source). **No Git LFS:** a hand-made .blend
  goes in git only if small (compressed, a few MB at most); bigger ones live outside the repo.
