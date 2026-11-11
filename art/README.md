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
look_pz_<subject>.glb (2.5k / 1.2k / 600, a 512 painted colour map), for the look lab (godot/Labs/LookLab.tscn).

The dead the game draws are pz builds (~18 s each; four at once are fine): the tee with each face, and three more
outfits, worn dirty and bloodied (`--outfit longsleeve`: a painted work shirt and trousers, paint.OUTFIT_VARIANTS;
`--clothes jacket|hoodie` on the zombie: the garment meshes, clothes.albedo's `dead`). Each zombie pz build also writes
`<file>_mask.png` (red the top's clean cloth, green the trousers'; blood, dirt and tears left out) and
`<file>_mask.json` (the colours painted, the cloth's mean luminance): the game recolours the dead's clothes from them
(godot/Characters/Looks.cs). After a rebuild, Godot must re-extract each file's colour map (`<file>_albedo_pz.jpg`):
delete that .jpg and its .import with the file's entries in `godot/.godot/imported`, import, then restore the
committed .jpg.import (it keeps the texture's uid) and import again.

```bash
for v in "" "--face slack" "--face gaunt" "--face bloated" "--face torn" "--outfit longsleeve" "--clothes jacket" "--face slack --clothes hoodie"; do
  "$B" --background --factory-startup --python art/scripts/build_lookdev.py -- --subject zombie --only pz --out godot/assets/characters/look $v
done
```

Then in Godot: `--headless --path godot --import` (re-import), and the lab:
`--path godot res://Labs/Lab.tscn -- --check` (skeleton vs the humanoid profile) or `-- --shot=out.png`.

- `scripts/humanoid_profile.json`: Godot 4.6's SkeletonProfileHumanoid (names, parents, reference poses), dumped by
  `dump_profile.gd` (`Godot --headless --script art/scripts/dump_profile.gd -- out.json`). The rig is built from it.
- `scripts/rig.py`: the shared rig. Profile bone names, parents and rest orientations; our proportions (1.75 m).
- `scripts/validate.py`: the rules every character file meets (on a look build's .blend it also checks the build's own
  intermediates, the high-poly sources and the weight source, which fail them: the build validates each exported LOD; profile skeleton, applied transforms, at most
  4 weights per vertex and normalised, no unweighted vertices, triangle budget, clip names, no root motion).
- `scripts/build_test_mannequin.py`: the segmented mannequin (regions, stumps, a face, 27 clips), all by script; with
  `--garments godot/assets/characters/garments.glb` it also exports the garments (and garments.json beside them).
- `scripts/retarget.py`: a clip library onto our rig, baked (rest-relative world rotations, swung to the source
  bones' directions; hips placed by the hip joints, scaled by leg length); `scripts/bonemaps/*.json` the bone maps.
- `scripts/derive_clips.py` (2026-11-10): the clips the game's people play, derived from `clips_ual.glb` (no download
  needed) and the mannequin's pose numbers, into `godot/assets/characters/clips_more.glb` and `.json` (47 clips, 1.4 MB;
  ~20 s): `"$B" --background --factory-startup --python art/scripts/derive_clips.py -- --src
  godot/assets/characters/clips_ual.glb --glb godot/assets/characters/clips_more.glb [--only walk,dead_lunge]
  [--measure ual_walk]`, then re-import in Godot. The tools: stride warping (each ankle's forward offset from the hips
  scaled, the legs solved again by two-bone IK in the clip's knee plane, the hips dropped where a longer reach needs
  it), bake-time planting (each foot pinned to the ground while down, travelling back at the clip's own pace, so the
  rate Gaits.cs asks for keeps it still), layers (turns on the spine and head, arm poses relative to the chest, hands
  placed by IK) and poses from build_test_mannequin's numbers. It also fixes the library's feet: retargeted, they
  pointed 31 deg toes-down (the ball ~5 cm under the ground). It prints each clip's speed and stride, and where a
  leg fell short of its pin.
- `scripts/sculpt.py`: the look-development body: metaball masses -> voxel remesh -> the face pushed in by brushes
  (pure functions of the rest position); the zombie's face keys (jaw_open, angry); eyes and teeth.
- `scripts/paint.py`: colours and cloth relief as numpy functions of the rest position (outfits by region, and the
  recolour mask of the clean cloth); the build
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
- `scripts/build_trees.py`: the trees and undergrowth for first person (docs/trees.md): five species in three
  LODs, a bush and a fern, vertex colours, alpha-cut leaf cards near, the leaf atlas painted by numpy (~4 s):
  `"$B" --background --factory-startup --python art/scripts/build_trees.py -- --glb godot/assets/trees/trees.glb
  --tex godot/assets/trees/leaves.png [--preview lineup.png]`, then re-import in Godot.
- `scripts/build_items.py`: the things lying on the ground in first person, one low-poly model per item kind (the
  sim's 21: wood, food, meal, bandage, medicine, medkit, weapon, cloth, coat, armour, boots, gun, ammo, corpse, book,
  cart, axe, hammer, sample, scrap, fuel), 36-252 triangles each, boxes and cylinders in vertex colours (alpha is
  metalness), one mesh per kind named after it, variants of the common kinds as `<kind>_v1`, `_v2`... (20 of them:
  food, wood, medicine, weapon, cloth, book, scrap, bandage, coat, boots, ammo, meal; the game picks one per item by
  a hash of its id), and `hand` (the gloved fist and sleeve that holds things in first person) (~1 s):
  `"$B" --background --factory-startup --python
  art/scripts/build_items.py -- --glb godot/assets/items/items.glb [--preview lineup.png]`, then re-import in Godot.
  A new item kind wants a function here (until then it's a grey box). The weapons' kinds and the flashlight are here too.
- `scripts/build_world.py` (2026-11-11): what first person had no model for, 20 models, 48-1,488 triangles each
  (~2 s): a farm plot's tilled bed (`soil`) and each crop at three stages on its bed (`potato_1`..`3`, `wheat_1`..`3`,
  `herb_1`..`3`); building sites (`site` and `site_edge` pegged out, `frame` and `frame_door` going up, `joists`,
  `planks` delivered); the helicopter (`heli`, and `heli_rotor` apart so the game spins it); a caravan's handcart
  (`wagon`) and a trader's goods on a blanket (`stall`):
  `"$B" --background --factory-startup --python art/scripts/build_world.py -- --glb godot/assets/world/world.glb
  [--preview lineup.png]`, then re-import in Godot. A new crop wants `<crop>_1`, `_2`, `_3` here (until then its
  plot shows the bare bed).
- `scripts/build_furniture.py`: the furniture, and the car and the pickup, as low-poly models in one file
  (`--glb godot/assets/furniture/furniture.glb [--preview lineup.png]`), drawn by `godot/World3D/Furniture.cs` and
  `Cars.cs`. **The car's and the pickup's sizes are also numbers in the sim**, which must be changed with the models:
  their length and width (`cs/Sim/Movement/VehicleBody.cs`: `CAR`, `TRUCK`), and their heights (the underside, the
  boot, roof, bonnet and bed, and where the cabin begins and ends: `cs/Sim/Movement/VehicleHeights.cs`).
- `scripts/build_surfaces.py` (2026-11-11): the world's detail textures, twenty tileable 256 px pictures (grass,
  earth, asphalt, brick, planks, shingles, metal, cloth, bark, fur...) and their index, made with numpy alone and
  seeded by name (~3 s): `"$B" --background --factory-startup --python art/scripts/build_surfaces.py --
  [--sheet experiments/textures/kit.png] [--renormalise]`, then re-import in Godot. They are multipliers that average
  to no change, laid over the flat colours by `godot/assets/shaders/surface_detail.gdshaderinc`; a model picks a
  layer per face in its UV map. docs/textures.md is the page: the layers, the UV convention (glTF flips V), adding
  or replacing one.
- `scripts/lowpoly.py`: the kit build_world.py and build_cabins.py are made with (boxes, cylinders and cones as
  coloured faces, one mesh per model, the glTF export, a preview). build_items.py and build_furniture.py carry older
  copies of the shape helpers, and take mul, the materials and write_surfaces from it. **A model's detail texture
  comes from its colours' materials:** `mat("wood", lin(...))`, `mat("cloth", ...)`, `mat("body", ...)` (a car's
  paint), `plain(...)` for glass, chrome and screens; a plain colour with metalness wears painted metal, without
  none. lowpoly.MATERIALS has the materials (layer and repeats per metre); write_surfaces writes the layer and each
  face's coordinates in metres as two UV maps. docs/textures.md "The models" is the page.
- `scripts/build_cabins.py` (2026-11-11): a car from the driver's seat, for the car you drive (~1 s): `cabin` and
  `cabin_v1` (the sedan's inside, with the red and the blue bonnet), `cabin_truck` (the pickup's: the same cab
  further forward and higher, a wall and window behind the seats, the load bed), `steeringwheel` (about its hub, so
  the game turns it): `"$B" --background --factory-startup --python art/scripts/build_cabins.py -- --glb
  godot/assets/cars/cabins.glb [--preview lineup.png]`, then re-import in Godot. Its EYE, HUB and PANEL are the
  numbers godot/World3D/Cockpit.cs has: change both. **A model's name must not end in `_wheel`, `-col`, `-rigid`,
  `-navmesh`, `-vehicle`, `-occ` or `-noimp`** (with `-` or `_`): Godot's importer reads those as hints and makes
  the node something else under another name.
- `blend/`: generated .blend files, not in git (the scripts are the source). **No Git LFS:** a hand-made .blend
  goes in git only if small (compressed, a few MB at most); bigger ones live outside the repo.
