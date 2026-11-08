# Trees in first person

Until 2026-11-08 a tree (furniture kind `tree`) was a brown box under a green box (`WorldMesh.Thing`). This is
how trees are drawn now, and why.

## What a tree has to be

- **The look: "Zomboid-plus".** Grounded, muted, worn, never cartoon-bright. Seen from 1.65 m, sometimes from
  arm's length (you chop them, you hide behind them), mostly as a forest: trunks in rows, crowns overhead, fog
  at about 50 m (density 0.018). Forest ground is ~30% tree tiles, so a forest is dense: 1 to 3 m between trunks.
- **The cost.** Chunks within 3 of yours are built (49 at most, 7x7x16 m), so a wooded area is a few thousand
  trees in view range. They have to be instanced (MultiMesh), few draw calls per chunk, cheap shadows, LOD.
- **Sway** in a shader, not on the CPU.
- **Determinism.** The look of a tile is a hash of the tile (no RNG, nothing saved): the same world shows the
  same trees each time, and the sim isn't touched.
- **The sim's tree is a tile.** You and the dead collide with the tile, so the trunk must stand inside it.
- **The art pipeline** (art/README.md, docs/characters.md): everything repeatable is a headless Blender script
  in art/scripts; downloads need the player's OK; no Git LFS, so assets stay small.
- **A heightmap is coming**: trees stand on one ground-height function (`Terrain.Height`), not y = 0.

## The options

| | Look | Cost | Pipeline | Verdict |
|---|---|---|---|---|
| **(a) Modelled low-poly trees, Blender script** | Solid trunks and crown masses; reads well at every range; crowns can look like lumps if left bare | ~1k triangles near, ~150-500 mid, ~50 far; 3 materials | Fits art/scripts exactly (headless, seeded, rebuilt in 4 s) | **Base of the pick** |
| **(b) (a) plus alpha-cut leaf cards** | Soft, broken crown edges; needle sprays and fern fronds look like plants at arm's length | Overdraw and alpha scissor only where cards are; one 1024 atlas | Same script, the atlas painted by numpy | **Picked, near LOD only** |
| **(c) Impostors / billboards far, meshes near** | Best for kilometres of forest; flat and swimmy when you walk past them at eye height | Cheapest far; needs baking (per species, per angle) and a shader | A bake step and an atlas per species | **Not now**: fog ends the view at ~50 m, where 50-triangle meshes already cost nothing. Revisit if the view distance grows |
| **(d) A CC0 tree pack** | Depends on the pack; most are bright stylised or photoreal, either way off-tone; hard to keep consistent with our characters | Varies, usually heavier | A download (the player's OK), licence notes, re-tinting, our LODs anyway | **Not taken**: no download needed for this result; worth a look later for a realistic bark texture |
| **(e) Procedural at runtime in Godot** | The same shapes as (a) are possible | Generation on load, no asset | Outside the art pipeline; harder to look at (no Blender lineup), one more mesh-building codebase | **Not taken**: (a) gives the same freedom with better tools and a file to look at |

## The pick: (a) + (b), LOD by chunk

**art/scripts/build_trees.py** (headless Blender, about 4 s) writes `godot/assets/trees/trees.glb` and
`leaves.png`:

- **Five species, three LODs each:** spruce (drooping tiers with needle-spray cards; dead sticks below the
  crown), pine (a tall bare trunk, clumped crown high up, stubs), broadleaf (a trunk splitting into limbs, a
  crown of bumped masses with leaf cards), birch (a thin white trunk with dark marks, a narrow crown with small
  leaf cards), a dead snag (broken top and limbs; the grim note in a stand). Triangles near / mid / far:
  spruce 1086 / 184 / 86, pine 1045 / 404 / 50, broadleaf 1350 / 488 / 50, birch 1064 / 398 / 56, snag 263 / 94 / 10.
- **Undergrowth, one LOD:** a bush (384) and a fern (36).
- **Crown normals point out of the crown**, so a crown is lit as a mass (soft) rather than as facets; cards keep
  that normal on both sides. Vertex colours carry the colour and a cheap ambient occlusion (darker underneath
  and towards the crown's heart, dirty at the trunk's foot).
- **The atlas** (1024, four tiles: broadleaf twig, needle spray, fern frond, birch twig) is painted by numpy,
  in grey with a little hue variation, and tinted by the vertex colour; clear texels carry the leaves' mean colour
  so mipmaps don't halo.

**godot/World3D/Trees.cs** draws them:

- **By chunk** (WorldMesh's 16 x 16 tiles, the same 3-chunk radius), **one MultiMesh per chunk, species and LOD**.
  The LOD is each node's visibility range to the chunk's centre: near 0-24 m, mid 24-44 m, far 44 m on (2 m
  margins). A chunk swaps whole, so the near LOD always covers what's within ~12 m of you. Built 4 chunks a frame
  at most, rebuilt when the chunk's tree tiles or forest ground change (checked every 30 frames).
- **Species by hash, in stands:** a ragged 6 x 6-tile patch leans to one species (60% of its trees), the rest
  mixed by the ground's odds (forest: spruce 36%, pine 24%, broadleaf 20%, birch 14%, snag 6%; yards and verges
  mostly broadleaf and birch). Size 0.7-1.2 of the model, a turn, a tint (lighter/darker, warmer/cooler), and a
  sway phase, all from the hash of the tile.
- **The trunk is the tile:** jittered at most 12 cm off the tile's centre; the thickest trunk is ~0.26 m
  radius at the largest size, so it stays inside the tile the sim collides with.
- **Sway** (the shader): the whole tree bends with the square of the height above its foot (about 14 cm at
  10 m), two slow sines with the tree's own phase, the wind's direction the same for every tree; crown masses
  and cards flutter a little more along their normals. Shadows sway with them (same vertex shader).
- **Bark** has furrows from a 3D noise stretched up the trunk (no texture, no UVs needed).
- **Shadows:** near and mid LODs cast; far and undergrowth don't (the sun's shadow distance is 40 m anyway).
- **Undergrowth:** forest ground with no tree gets a fern (26% of those tiles) or a bush (10%), knee-high or
  less, purely visual, drawn to 30 m. **No scattered small trees on open tiles:** in first person a sapling you
  walk straight through reads as a bug, and the sim's trees are the trees you can bump into, hide behind and chop.

## Numbers (RTX 5070, 1280 x 720, `--debug`, noon)

| Where | Trees in range | 3D ms/frame | Render CPU | Render GPU |
|---|---|---|---|---|
| The start, looking at the forest edge, box trees (before) | 797 | 0.05 | 0.20 ms | 0.24 ms |
| The same, these trees | 797 | 0.08 | 0.29 ms | 0.33 ms |
| Inside the forest (seed 42, 52,64) | 934 | 0.06 | 0.31 ms | 0.44 ms |
| At arm's length to a pine, birch, ferns (30,40; 9 people as models too) | 677 | 0.16 | 0.37 ms | 0.62 ms |
| Deep forest, a 512 world (400,400) | 1287 | 0.06 | 0.45 ms | 0.74 ms |

Assets: trees.glb 0.3 MB, leaves.png 0.2 MB (VRAM-compressed with mipmaps on import).

## Benchmark: is the LOD worth it, and do far trees need more?

`godot/World3D/TreeBench.tscn` (`Godot --path godot res://World3D/TreeBench.tscn -- [--runs=lods@3,near@3,...]
[--frames=360] [--out=bench.md]`): a 256 world where every open tile is forest ground with a tree on 30% of them
(the worst case: every chunk in range is forest), first person in the middle at noon, the camera turning a full
circle, vsync off. Modes: `lods` (the game's), `noshadow` (the mid LOD casts no shadow), `near` / `mid` / `far`
(one LOD everywhere); `@R` is the trees' chunk radius (3 is the game's, ~56 m; 7 is ~120 m).

RTX 5070, Core Ultra 7 265K, 2026-11-08:

| mode | radius | trees | frame ms | p95 | render cpu | render gpu | draw calls | primitives |
|---|--:|--:|--:|--:|--:|--:|--:|--:|
| lods | 3 | 3585 | 1.02 | 1.27 | 0.24 | 0.73 | 527 | 2.13M |
| near (no LOD) | 3 | 3585 | 1.57 | 1.90 | 0.31 | 1.28 | 1002 | 5.93M |
| noshadow | 3 | 3585 | 0.94 | 1.23 | 0.22 | 0.66 | 472 | 1.80M |
| mid everywhere | 3 | 3585 | 0.92 | 1.21 | 0.26 | 0.63 | 699 | 1.84M |
| far everywhere | 3 | 3585 | 0.58 | 0.89 | 0.17 | 0.27 | 295 | 0.20M |
| lods | 5 | 9167 | 1.06 | 1.48 | 0.39 | 0.76 | 746 | 2.22M |
| near (no LOD) | 5 | 9167 | 2.03 | 2.46 | 0.43 | 1.73 | 1396 | 8.92M |
| lods | 7 | 17112 | 1.10 | 1.54 | 0.48 | 0.79 | 1021 | 2.34M |
| lods, 1080p | 3 | 3585 | 1.21 | 1.48 | 0.25 | 0.91 | 534 | 2.13M |
| near, 1080p | 3 | 3585 | 1.80 | 2.12 | 0.35 | 1.50 | 1018 | 6.02M |
| lods, 1080p | 7 | 17112 | 1.29 | 1.74 | 0.50 | 0.99 | 1024 | 2.34M |

(Primitives include the shadow passes.) **Read:** the LODs halve the trees' GPU time and draw calls and cut the
triangles to a third (0.73 against 1.28 ms GPU at the game's radius; 0.76 against 1.73 at radius 5). **Far trees
need nothing more:** going from 3,585 to 17,112 trees (radius 3 to 7) adds 0.06 ms of GPU and 0.24 ms of CPU,
because the far LOD is 10-86 triangles and casts no shadow, so impostors would save almost nothing. What's left
is the near LOD (0-24 m to a chunk's centre, so trees up to ~35 m away) and its shadows: if trees ever need to
be cheaper (the low-end goal), the levers are a finer LOD grid (8 x 8-tile cells, so fewer trees get the near
LOD), a mid-LOD shadow proxy, or a lighter near LOD; dropping the mid LOD's shadows saves only 0.07 ms.

## Left for later

- **Seasons:** the crowns don't change with the season yet (autumn colour, broadleaf bare in winter: a tint and
  a "bare" LOD from the same script).
- **Felled and bare trees:** a tree with no wood left (`loot` 0) still looks whole; a stump when the sim
  removes one.
- **The forest floor** (WorldMesh's ground colour) is pale, which makes the forest look like it stands on
  snow; the world's look should give it litter and darker soil.
- **A bark texture** (a CC0 one, or one baked by the script) for arm's length.
- **Impostors** if the view distance ever goes well past the fog's 50 m.
