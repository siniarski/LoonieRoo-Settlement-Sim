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

The look-development characters (docs/characters.md "The look"; ~45 s a subject, both subjects in parallel are fine):

```bash
"$B" --background --factory-startup --python art/scripts/build_lookdev.py --     --subject zombie --out godot/assets/characters/look [--only plus|pz] [--preview DIR] [--blend art/blend/look_zombie.blend]
```

It writes look_plus_<subject>.glb (30k / 12k / 6k / 3k / 1.2k / 600 triangles, 2048 colour and normal maps) and
look_pz_<subject>.glb (2.5k / 1.2k / 600, a 512 painted colour map), for the look lab (godot/Characters/LookLab.tscn).

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
- `scripts/sculpt.py`: the look-development body: metaball masses -> voxel remesh -> the face pushed in by brushes
  (pure functions of the rest position); the zombie's face keys (jaw_open, angry); eyes and teeth.
- `scripts/paint.py`: colours and cloth relief as numpy functions of the rest position (outfits by region); the build
  bakes each texel's rest position, so the same functions paint the texture and displace the high-poly mesh whose
  normals are baked. `scripts/build_lookdev.py`: the pipeline (sculpt, decimate the ladder, unwrap, bake position, AO
  and normals in Cycles, paint, skin with auto weights on the body bones, face keys, validate, export).
- `models/base_skin.blend`: the stored base skin (body and head with the eyes and mouth open), the source the
  look builds load: edit it in Blender and save; `scripts/make_skin.py` made it once (rerunning it starts over).
- `scripts/edit_skin.py`: scripted edits to the stored skin (`blender art/models/base_skin.blend --background
  --python art/scripts/edit_skin.py -- --edits cheeks,lips`), recorded on the object so they apply once.
- `scripts/animals.py`, `scripts/build_animals.py`: the wild animals and dogs (deer, rabbit, wolf, bear, dog) for
  first person: a quadruped rig, metaball bodies, a 3-step LOD ladder, vertex colours, idle / walk / run clips and an
  attack for the biters; `<kind>.glb` and `<kind>.json` (clip lengths and ground speeds) into
  `godot/assets/characters/animals/` (all five in ~8 s: `"$B" --background --factory-startup --python
  art/scripts/build_animals.py -- --out godot/assets/characters/animals [--kinds wolf] [--preview DIR]`).
- `scripts/hair.py`, `scripts/build_hair.py`: hair styles as their own meshes (buzz, crop, sidepart, long,
  receding, ponytail, matted, beard) with strand cards, their under-hat versions, and the beanie, each exported
  alone to `godot/assets/characters/look/hair/`.
- `scripts/clothes.py`: garments with volume for the look-development survivor (`build_lookdev.py --clothes
  jacket|hoodie`): shells cut from the body, draped, lifted by a thickness profile, extra pieces (collar, hood,
  pocket, drawstrings), rims; their relief and colour. Face variants: `--face gaunt|bloated|torn|feral`.
- `scripts/garments.py`: the garments, by body region, with the hiding rules; `scripts/shapes.py`: mesh primitives.
- `blend/`: generated .blend files, not in git (the scripts are the source). **No Git LFS:** a hand-made .blend
  goes in git only if small (compressed, a few MB at most); bigger ones live outside the repo.
