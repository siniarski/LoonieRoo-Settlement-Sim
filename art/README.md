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

Retargeting a clip library onto the rig (the library is a download, kept outside the repo; only the script, the bone
map and the exported clips are committed):

```bash
# Quaternius's Universal Animation Library, Standard (CC0): opengameart.org/content/universal-animation-library,
# universal_animation_librarystandard.zip (14.5 MB); unzipped here to ~/Downloads/anim-libs/ual1/ual_standard
"$B" --background --factory-startup --python art/scripts/retarget.py -- \
    --src ~/Downloads/anim-libs/ual1/ual_standard/Godot/AnimationLibrary_Godot_Standard.glb \
    --map art/scripts/bonemaps/ual.json --prefix ual \
    --glb godot/assets/characters/clips_ual.glb --blend art/blend/retarget_ual.blend
```

It prints each clip with how far our ankles stray from the source's (scaled to our legs). The .blend has both rigs
side by side (the source 1.2 m to our left) with the source clips kept, for a look in Blender.

Then in Godot: `--headless --path godot --import` (re-import), and the lab:
`--path godot res://Characters/Lab.tscn -- --check` (skeleton vs the humanoid profile) or `-- --shot=out.png`.

- `scripts/humanoid_profile.json`: Godot 4.6's SkeletonProfileHumanoid (names, parents, reference poses), dumped by
  `dump_profile.gd` (`Godot --headless --script art/scripts/dump_profile.gd -- out.json`). The rig is built from it.
- `scripts/rig.py`: the shared rig. Profile bone names, parents and rest orientations; our proportions (1.75 m).
- `scripts/validate.py`: the rules every character file meets (profile skeleton, applied transforms, at most
  4 weights per vertex and normalised, no unweighted vertices, triangle budget, clip names, no root motion).
- `scripts/build_test_mannequin.py`: the segmented mannequin (regions, stumps, a face, 27 clips), all by script; with
  `--garments godot/assets/characters/garments.glb` it also exports the garments (and garments.json beside them).
- `scripts/retarget.py`: a clip library onto our rig, baked (rest-relative world rotations, swung to the source
  bones' directions; hips placed by the hip joints, scaled by leg length); `scripts/bonemaps/*.json` the bone maps.
- `scripts/garments.py`: the garments, by body region, with the hiding rules; `scripts/shapes.py`: mesh primitives.
- `blend/`: generated .blend files, not in git (the scripts are the source). **No Git LFS:** a hand-made .blend
  goes in git only if small (compressed, a few MB at most); bigger ones live outside the repo.
