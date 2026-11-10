# Design direction

The player's vision and the big decisions about the game's shape. Moved from NOTES.md ("Design direction", its two
"Decisions" parts and "Big worlds") on 2026-11-10, wording kept. **Where it stands now** (2026-11-10): the planned
order below is built through step 6, with inventory in; the look is decided (3D, first person: the 2026-11-08
decisions, which overrule the isometric wording of 2026-11-03 and 2026-11-04); the open work is docs/open-work.md.

## The vision (2026-11-03)

Where the game is heading: **Project Zomboid-style survival, with world generation and an optional colony sim.**

- **Start alone.** You control one survivor directly (Zomboid-like controls) in a harsh, generated world. How harsh is
  a difficulty setting.
- **Meet others and form a group.** People you meet can join you. They follow you as you loot and survive, or you
  designate an area to settle, and the colony sim takes over there. You keep control of your own character inside the
  colony; everyone else runs on jobs and orders.
- **Talk to survivors.** Each has a short generated backstory (home, people, job, a regret, a pride). Talking to them
  gives tasks that come from it: find their old home, pet, photo or car; bury their dead or put down a turned relative;
  burn the place of a bad memory; clear their old street; get medicine to a friend elsewhere; call a sister's town from
  a radio tower. Finishing one gives lasting opinion, mood and loyalty, maybe a trait or skill. Also: survivors
  recognising each other, confessions that shift others' opinions, a would-be leader splitting off to found their own
  colony. Tasks are templates filled from world data, not hand-written branches; dialogue is a short menu (the past,
  skills, orders, items).
- **A living world.** Other groups and colonies exist and survive on their own: they grow, starve, trade and raid off
  screen, as a few numbers plus a small seed of buildings, leaders and needs (the same simulation-LOD idea as herds and
  dormant zombies), unfolding into real buildings and people when you get near. You can find them and help or fight
  them.

What already mapped onto it (2026-11-03):
- `state.harsh` is the start of difficulty presets.
- Survivors' needs, skills, traits, opinions, leader and shifts are already there.
- "Follow me" would be a job; "settle here" switches the colony sim on for an area.
- `state.settlements` already has the right shape for self-running colonies.
- The radio, points of interest, expeditions, graveyards, burning and corpses are task building blocks.

Concerns, most serious first:
1. **Movement and combat feel.** Tile-to-tile movement at 4 ticks a tile and "adjacent, hit every N ticks" won't feel
   like Zomboid under direct control. Probably needs smooth or sub-tile positions in the sim (or fine sub-steps),
   facing, vision cones, real attack timing. The control prototype decides whether the current model stretches or
   needs a rewrite. (Since 2026-11-05: continuous movement, rule 10 in CLAUDE.md.)
2. **Tech base.** Zomboid-like play wants isometric rendering, lighting and line of sight shading, many animated
   characters, a real inventory UI. Pick the visual target (top-down or isometric) before investing much in rendering.
   The sim/renderer split keeps an engine port possible. (Since 2026-11-08: Godot, first person.)
3. **Scope.** Three games in one (survival action, colony sim, living world). Grow by vertical slices: one town, an
   hour of fun solo survival, one recruit, one task, a small "settle here".
4. **Inventory.** One carried item per survivor and no stacks then; Zomboid needs weight, containers, a backpack, item
   condition. A big but contained rework. (Since 2026-11-05: Inventory.cs.)
5. **Companion behaviour.** Simple, readable orders (follow, hold here, loot this room, stay close) over clever AI.
6. **Self-running colonies** must look believable when they unfold: keep a seed of their own, not just numbers.
7. **Balance per difficulty.** Balance one "normal", derive the rest by multipliers.

Planned order:
1. A playtest of the current build (the player, first).
2. A control prototype: take over any survivor, WASD, click to attack or pick up, the camera following, their jobs
   off. Decides the movement and combat question.
3. A solo start, and difficulty presets.
4. Follow, hold and settle-here orders for people met.
5. Backstories, talking to survivors, the first task types (find the home, bury the dead, find the pet).
6. Self-running settlements that grow, fail, trade and fight off screen.
7. Inventory rework and the visual target, once the feel is proven.

## Decisions (2026-11-04)

1. **View.** Isometric is preferred for solo play; top-down may suit colony management. Plan for a renderer that can
   show both (the sim doesn't care). **Multi-storey buildings are wanted later**, so the sim needs a z-level (floor) in
   its coordinates before much more building code goes in. Decide this with the control prototype.
2. **Movement.** Real-time (smooth) movement is the likely end goal. Settled by the control prototype.
3. **Engine.** A desktop build is needed eventually; **Godot** is the likely target, chosen on performance and cost.
   Keep the TypeScript build for prototyping until the fun is proven; don't polish the Canvas renderer.
4. **Death.** If your character dies you choose: take over someone in your group, or start a new character far away
   in the same world. Each colonist has a **commitment** that rises and falls with how happy they are with you and the
   colony (food, safety, mood, opinion of you). Low commitment: they won't follow orders, leave, or can't be taken over.
5. **Win.** Endless, with optional goals. Some modes allow a cure or an escape; after either you can keep playing.
   Escape: arrange transport, choose to send others out ahead, then go looking for more people to bring. Escaping
   lands you on a new map with your gear (a new-game-plus).
6. **Time.** Days get longer so you can explore a fair while before you must rest. Actions other than walking
   (looting, building, crafting) scale up with the day.
7. **Control.** Both: light "group leader" orders (follow, hold, guard here, loot that) by default; the full colony
   tools once you settle somewhere.
8. **Map.** The player picks: island, island chain, large mainland, or a bordered map. Continuous or region-based
   depending on the choice.
9. **Multiplayer.** Co-op is a goal far down the line. Keep the sim deterministic (lockstep stays possible); no
   shortcuts that break it.
10. **Tone.** Grim, no humour. Amputation, executing prisoners, putting down turned family (rare, extreme cases; it
    costs heavy mood, which has its own penalties for your character).
11. **Difficulty.** Presets plus sandbox settings. Axes: weather intensity (regions of extreme heat or cold); **how far
    the apocalypse has progressed** (less to find, and worse condition); zombie count; nuclear winter / cloud cover
    (weaker crops and foliage, radioactive areas).

## Decisions (2026-11-08): characters, animation and the view

**The game is first person** (the player, later that day: "our game will be first person"). The map (M) is top-down
and **colony management is a tab under M**, in the 2D top-down renderer (godot/Render) for now; **no isometric view
anywhere**. Characters are "Zomboid-plus" (Zomboid's proportions and worn look, with the detail first person needs:
~10-15k triangles near, a normal map, real faces); zombies' faces are neutral or angry (it has seen you), near LODs
only. What follows was the morning's plan, still the plan where it doesn't clash.

The look will be **3D**: third-person / isometric for solo play, bird's-eye for colony management; first-person later,
not closed off. Characters are **stylised low-poly** (~3-8k tris dressed). Assets come from Blender (MCP for
interactive work, headless Python scripts for anything repeatable) as glTF. The sim owns the truth: movement is
code-driven (no root motion), ragdoll is cosmetic, actions and their approaches (quiet/fast/forceful) are sim data the
animation follows by progress. LOD is planned from the start (near skeleton, mid merged mesh, far VAT crowds). The
whole plan, with the order of work: **docs/characters.md**. Decided as the work went: no Git LFS ever, our own base
meshes, CC0 clip libraries allowed, animation update rate by LOD tier.

## Big worlds

Written when the world was 256x256 (before 2026-11-09); the code is shaped so it can grow towards 10,000x10,000
without rewrites. **Since then** (docs/worldgen-speed.md, docs/worldplan-regions.md): the plan is made by region,
chunks are made again from the seed, and saves keep only what changed (schema 75); the grid in chunks is the next big
job (docs/open-work.md "Scale and big worlds"). The scale target is Zomboid-sized: ~100k zombies on ~10,000 x 10,000.

- **Done then:** generation is a pure function of (seed, position) using a stateless hash (never the sim RNG), run
  chunk by chunk (32x32), with buildings that never cross a chunk border, so chunks can be generated in any order
  (tested). Nothing depends on the map edge (outdoors = touches water or over 600 tiles; night zombies and wanderers
  come from the explored frontier). Dirt and damage are index lists, not whole-map scans. Furniture has a cached tile
  and id index. Rendering skips off-screen and unexplored things. World zombies use simulation LOD: dormant zombies
  and herds are cheap markers until survivors are near.
- **Still to do for a much bigger world:** chunked storage (state still holds full-size arrays; lazy generation only
  decides *when* chunks are filled in); saves as seed plus changed chunks (done since); a real active region
  (simulate only near survivors); hierarchical pathfinding (HPA*) with per-chunk walkability versions instead of the
  single counter; bounded room flood fill (the 600-tile cap already defines the answer, but the fill still runs to
  completion); spatial indexes for items and survivors; a chunk render cache and a world map. Entity ids come from
  `nextId`, so they depend on generation order; ids are only labels, but lazy generation should keep that in mind.
- Measured at 256x256 with a scripted player for 14 days: 0.1-0.5 ms per tick, mostly growing with the number of
  loose items (item lookups are still linear). Fully zoomed out the browser's renderer held 60 fps.
- Before storeys, slopes or big-world pathfinding decisions: run a very large stress test first (docs/harness.md).
