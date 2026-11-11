# Textures: the detail kit

The world is flat colour (vertex colours); a **detail layer** gives a surface grain and scale without changing its
colour and without the mesh being unwrapped. One kit for the world, the models and the living things, so it has one look.

**The layers** (`godot/assets/surfaces/<name>.png`, 256 x 256, tileable; `layers.json` has the order and sizes; all
twenty on one sheet: `experiments/textures/kit.png`). A texel is a multiplier stored halved, and every channel of
every layer averages 0.5: a surface keeps its colour, and far off (small mipmaps, the far view) it is flat again.
Up in the picture is up on a wall; boards, courses and rows run across it; ribs, furrows and strands run down it.

| # | name | repeat | | # | name | repeat | |
|---|---|---|---|---|---|---|---|
| 0 | grass | 2 m | tufts | 10 | floorboards | 2 m | 12.5 cm boards |
| 1 | litter | 2 m | leaves, twigs: forest floor | 11 | tiles | 2 m | 33 cm floor tiles |
| 2 | earth | 2 m | soil, clods, pebbles | 12 | shingles | 2 m | roof courses, butts down |
| 3 | gravel | 1 m | small stones | 13 | sheetmetal | 2 m | corrugated, rust runs |
| 4 | asphalt | 2 m | speckle, chips | 14 | metal | 1 m | painted: scratches, chips |
| 5 | paving | 2 m | 50 cm slabs | 15 | rubber | 0.5 m | matt, ribbed |
| 6 | concrete | 2 m | pores, a joint every 2 m | 16 | cloth | 0.5 m | coarse weave |
| 7 | plaster | 2 m | faint trowel, damp | 17 | grime | 2 m | smears, runs: glass |
| 8 | brick | 2 m | 25 x 8 cm, for a brick-red base | 18 | bark | 1 m | plates, furrows |
| 9 | planks | 2 m | 17 cm lapped boards | 19 | fur | 0.5 m | strands, clumps |

**A shader** adopts it with `godot/assets/shaders/surface_detail.gdshaderinc` (its header has all of this):

```glsl
#include "res://assets/shaders/surface_detail.gdshaderinc"
void vertex()   { SURFACE_BY_WORLD }                      // static world; SURFACE_BY_OBJECT for a thing that moves
void fragment() { ALBEDO = COLOR.rgb * SURFACE_DETAIL; }  // then snowy(), wetter(), metalness... as before
```

The macro goes first in `vertex()` (before a sway moves VERTEX). One texture sample a fragment; the detail fades out
between 26 and 48 m. Make the shader with `Surfaces.Shader(code)` (godot/World3D/Surfaces.cs), or call
`Surfaces.Ensure()` first: it loads the set once and declares the global `surface_layers` the include reads, which
must exist before a shader naming it is compiled. Nothing is set per material.

**A mesh says which layer** each face wants in its **UV** (all corners of a face alike):
- `UV.x`: `0` no detail (a mesh without UVs draws as before); `layer + 1` laid by position (from above on what faces
  up or down, from the side on the rest); `-(layer + 1)` laid by the mesh's own coordinates in **UV2**, in metres
  (a roof's slope; a **skinned** body, whose vertices move under a position-laid texture).
- `UV.y`: repeats per metre (1 / the layer's repeat; more for finer grain on a small thing); negative turns the
  texture a quarter (upright boards).
- In C#: `st.SetUV(Surfaces.What("brick"))`, `Surfaces.Turned("planks")`, `Surfaces.Mapped("shingles")`,
  `Surfaces.None`; `Surfaces.Layer(name)` is the number. In a Blender script: read `layers.json` (or
  `from build_surfaces import LAYERS`) and write a UV map of `(layer + 1, 1 - repeats)`: **glTF flips V**, so Godot
  sees `1 - v`. A shader with real UVs of its own calls `surface_detail(p, n, vec2(layer + 1.0, repeats), UV2, dist)`
  or `surface_sample(uv, layer)` itself.

**Adding or replacing a texture.** The script is `art/scripts/build_surfaces.py` (numpy only; seeded by the layer's
name, so the same files every run):

```bash
"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --factory-startup --python art/scripts/build_surfaces.py -- --sheet experiments/textures/kit.png
```

then re-import in Godot (`--headless --path godot --import`). A new layer: a function and a line at the **end** of
`LAYERS` (meshes carry layer numbers). A better picture for an existing layer (painted, photographed): save a
tileable 256 x 256 PNG over it and run the script with `--renormalise`, which only puts each file's channel means
back on 0.5. All layers share one size and sit in one texture array (20 layers, ~7 MB of video memory with mipmaps).

**The world's use of it** (godot/World3D/WorldLook.cs: which colour and layer each face gets) is in that file's
header and in log/2026-11-11-12-textures-surfaces.md.

**Living things and plants** (log/2026-11-11-15-textures-living.md; how a body picks its layer: docs/characters.md
"Detail on bodies"):
- **People and the dead**: cloth on their clothes, rubber on shoes and boots, by the look model's own atlas UV and a
  detail map beside each file (`<file>_detail.png`, red cloth, green shoe; art/scripts/build_detail_maps.py), cloth
  repeated every 0.4 m, faded out from 2 to 5 m. Skin, hair, eyes plain. Shader: Characters/recolour.gdshaderinc
  (calls `surface_sample` itself; made after `Surfaces.Ensure()`).
- **Animals**: fur on the coat by UV2 in metres (build_animals.py `detail_uvs`), the kit's own fade; eyes and antlers none.
- **Trees** (and the saplings and bushes that wear the trees' bark material): bark laid by the tree's unswayed object
  position, 1.5 repeats a metre, in place of the two 3D noise furrows the shader had (cheaper: one sample for sixteen
  hashes), times 0.9 so a trunk's tone is unchanged near and far. Leaves keep their own texture.
- **Left plain**: grass tufts and weeds (blades a few pixels wide: no layer helps), the foliage masses, the burnt
  stumps (Fires.cs: near black, a multiplier shows nothing). The world has no felled stumps or logs to texture.
