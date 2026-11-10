# Open work

What is left, by area, gathered on 2026-11-10 from NOTES.md's "Next", "Open after..." and "Left from..." lists and
the newest session-log entries (items the log shows as done since were dropped). Read the section your task touches.
When you finish or find something, add a session-log entry (`log/`) and update the line here.

## Not played yet

Nothing built since 2026-11-05 has had a full playtest, and the Godot UI is checked only by the self-test and
screenshots (the browser's 142 UI tests weren't ported). Played 2026-11-09: tree collisions feel good; the shadow and
anti-aliasing settings look nice; the far view works. To try:
- Character creation; windows (see through, smash, climb, board up); line of sight and sneaking (C), sneak attacks;
  the water and power going off, the helicopter, alarms; cars (E to get in, W/S/A/D, the boot, fuel cans; timed entry,
  2.5 s in and 2 s out); the map (M) with notes; neighbours' errands, caravans, alliances and feuds; colonists
  foraging; the colony panels (wanderers, traders, neighbours, a survivor's story).
- Space's jump, vault and hop (heights, durations and stamina costs were picked without the player: log "Space is
  jump"); the body's leaps; the held items; animals moving freely.
- The footsteps and sounds (listen: `%APPDATA%\Godot\app_userdata\LoonieRoo\sounds-preview\*.wav`, written by the
  self-test; `step_*.wav` for steps); the furniture models; the pz look; your body in first person; the far field (on
  at 500 m; the menu's Options page sets it, 0-1000 m) and the other graphics options (nobody has used them in play).
- Inventory and containers; the dead from day one; new worlds (rivers, lakes, woods' edges, towns in clusters) and
  the New game and Load pictures.
- Questions: does free movement and combat feel right; is solo Apocalypse survivable (the scripted player reaches
  day 5); is the colony's winter readable.

## Speed

- Saving a 512 world takes ~1 s (Savefile.cs on JSON nodes; ~10 ms in the browser): fix before big worlds.
- On 512 worlds the C# runs at about half V8's speed (190-250 ticks/s; 40 needed at 4x), colonist-heavy small worlds
  2-3x slower: profile with dotnet-trace (ask before installing it); likely the JS-style list helpers allocating.
- Saving a fully explored world regenerates every generated chunk to compare (5.6 s at 2048): keep a hash of each
  chunk as made (WorldInfo) to skip the unchanged ones.
- Loading holds the frame (about 5 s at 2048) behind the loading screen's picture: loading on a worker needs the
  statics it touches made thread-safe first (Worldplan's cache, Heights, Time).
- Chunk generation is 1.6 ms a chunk at 2048 (0.9 before organic generation: more river curves per 64-tile bucket);
  a smaller bucket or a squared-distance early-out would win it back. The 10,240 preview takes ~20 s for its towns.
- Far-field regions built from the plan take 5 ms each on the main thread (~5.6 s of build time to fill 1 km): a
  worker would hide it.
- Shadows, resolution and anti-aliasing options aren't benchmarked; the furniture's draw cost was counted (214-257
  pieces in range in town) but not timed.
- The sim on its own thread (it's plain C#: Godot's main thread only needs a snapshot to draw).

## Scale and big worlds

- The grid lives whole in memory (3.6 GB at 10,000): the grid in chunks, loaded near survivors and kept as its changes
  elsewhere, is the next big job (docs/worldplan-regions.md; docs/design.md "Big worlds").
- Bug reports still copy the whole starting state through JSON (Replay), which doesn't fit at 10,000: give them the
  chunked form. Saves from before schema 77 don't load.
- The stress harness exists (a 1024 world with 200 colonists and 1,500 zombies ran at 3.8 ms a tick in the browser).
  Left: zombies moving and crowding, A* for long paths. Storeys, basements and slopes as data are still to decide,
  after a very large stress test.

## World generation (log/2026-11-10-02-organic-world-generation.md)

- The 3D ground: Heights' `relief()` is still 0; feed it Terrain's elevation so rivers sit in valleys and the start on
  its rise (Heights is read-only, render only: no sim change).
- Towns: streets are still axis-aligned grids (bending them touches lots, buildings and the street-end road links);
  thinner bands of towns along region borders; still three settlements at any world size.
- Water: rivers and lakes are the same water as ponds (thirst, sickness). 3 thirst deaths in the balance run where
  there were none (2 colony, 1 solo of 29 runs): watch it, and look at where the dying drank from (the start's river
  is now 38-68 tiles off, kept two lattice points in from a small map's edge).
- Small worlds (128): the start's rise covers most of the map, so lakes are rare; forest varies more by seed at 256
  (12-28%).
- The preview draws lots whole (yards and all), not their buildings; big worlds (over 2 x 2 regions) show towns only,
  no roads.

## First person and the look

- Left from the first-person start (2026-11-08): clothes and hair on the dead (all wear the base shirt); cars as models
  (Bodies.cs boxes); action clips for colonists' work; lighting (lamps, the power, torches); the world's look
  (textures; roofs and a gentle heightmap are in); what line of sight and fog of war mean in first person; the car's
  size (the sim's is a 0.43 m circle, drawn 1 x 1.9 m; the model is 4.2 m).
- Trees: colonists on tile paths and cars still pass through trunks; one trunk radius for every species (the species
  is drawing-only).
- Body leaps: the library's jump clip flings the arms wide; no real vault clip (a tuck and a hand on the sill); the eye
  doesn't dip on landing; the push-off and landing are the step sounds pitched, not their own.
- Held items: one sleeve colour (clothes are painted maps), not your own clothes'; the body's own hands on the grip
  (hand IK: held items drawn full size on the body) not done; the bob isn't from your footfalls; the axe's wind-up
  forearm is long across the view; no aim-down-sights or reload; things carried in both hands have no hands; holds
  not keyed to the sim's action progress; no knife or torch (the sim has neither).
- Footsteps: walking plays the jog clip (your walking speed is just over People.cs's 2.2 m/s walk limit); crawling is
  silent (the crawl clip has no footfalls); a step falling in a 0.25 s clip blend is skipped.
- Furniture: chairs aren't pieces of their own (tables have two built in); a searched or emptied container looks the
  same; far places (the factory, camp cots) only checked in `--lineup=furniture`.
- Item models: no LOD (measured: not needed, experiments/item-bench); Godot's automatic mesh LODs never kick in on the
  MultiMeshes (their AABB is the whole 40 m round you).

## Sound (log/2026-11-09-11-sound-two-muffling-your-steps-the.md)

- Tuning by ear (levels, falloff, the muffle amounts, step stride); sound round corners (paths, not lines); others'
  footsteps (the dead shuffling, colonists); doors opening and shutting, items picked up, eating; indoor reverb; sound
  on the Colony tab and the map, if wanted; the rest of Noise's kinds (gunfire, glass, alarms, engines); the startup
  cost of making the sounds.
- A CC0 pack can replace the made sounds (`res://assets/sounds/<kind>_<n>.ogg`), with the player's OK.

## Inventory (log/2026-11-09-12-the-inventory-s-outstanding-points-names.md)

- Names are only flavour (one model per kind; `Items.variant` could pick a model: a machete is drawn as a knife); a
  mixed stack moves its first item, whichever name; no drag from the world into the bag (pick-up stays E); in the
  Colony tab the dim covers the HUD while the bag is open (Tab/Esc close it).

## The dead and animals

- From log/2026-11-09-09: the 2D view and the minimap don't show the dormant (fog hides them anyway); lying ones lie
  at a random heading with no "get up" clip (the blend is 0.25 s); a dormant one's look is lost if it wakes outside
  the 40 m model range; the solo bot doesn't shut itself in (hiding indoors only counts as out of sight).
- The solo bot has no pack sense (it fights three at once, flees in a straight line).
- Animals off the grid (2026-11-09): a colony (world balance seed 11) fell to the dead on day 12 (was day 21), and
  none of the 13 lived to day 25 (was 1): check whether it's noise; solo deaths by zombies 3 -> 7 of 16 seeds; how
  often prey falls wasn't counted.

## Characters (3D)

The plan and decisions are docs/characters.md (read it first); each step's details are in the session log of
2026-11-08. **Built:** a Blender pipeline run headless (art/scripts, art/README.md: the rig from Godot's humanoid
profile, a validator, a scripted mannequin with body regions, stump caps, a face with shape keys and 27 clips, nine
garments by region); a character lab (godot/Characters/Lab.tscn, godot/README.md: movement and stances, body states
and missing limbs, clothes, ground, crowd and LOD, hands); foot IK (kerb, hill, stairs); hand IK (pistol, rifle,
knife, bat, ready and aimed at eye level; a door); a car to get into and out of (seat, wheel and pedal IK);
retargeted CC0 clips (art/scripts/retarget.py: 43 Quaternius clips in clips_ual.glb; the download lives in
~/Downloads/anim-libs, outside the repo); the mid tier dressed (OutfitMerge.cs, one merged mesh per outfit); a
ragdoll (LabRagdoll.cs on Jolt: knockdown, hit, get-up blend); a VAT horde benchmark (20,000 in 10.8 ms,
HordeBench.tscn); weapon actions (LabActions.cs: swing, stab, recoil, reload); foot locking (FootIK.cs toe ray and
lock); the sim side (PoseIntents.cs, lostLimbs with side and level, startledAt, VehicleActions.cs); the look
(art/scripts/sculpt.py, paint.py, build_lookdev.py: "pz" 2.5k and "plus" 30k LOD ladders; experiments/look-lab).
Tests: `--iktest`, `--handtest`, `--cartest`, `--ragtest`, `--walktest` (experiments/ik-test); benchmark
godot/Characters/Bench.tscn (experiments/anim-bench).

Blender 5.2 is installed; the Blender MCP server is in .mcp.json and approved locally (.claude/settings.local.json,
not in git): Blender must be open (its add-on listens on localhost:9876).

**Next (not started): clothing.** Pinned or knotted sleeves and trouser legs at a stump (lostLimbs says which side and
where), hair (low-poly caps, a short under-hat variant, colour by shader), real modelled garments in place of the
scripted shells.

Left from the steps, any time:
- Library locomotion in the lab's blend space (phase-align the cycles); a second clip library to prove the bone-map
  step.
- A far LOD mesh (500-800 tris: the horde is GPU-bound on triangles); VAT as the lab crowd's far tier; the ragdoll
  budget and freezing bodies.
- The slow walk: below ~1.2 m/s the lab blends idle into walk and a locked foot still slides ~21 cm a step (a
  slow-walk clip or stride warping); the mannequin walk's short stance; real clips for the weapon actions (they're
  keyed holds).
- Approaches (quiet/fast) as sim data, paired actions, zombies losing limbs in fights, anchors for doors and beds.
- VRAM-compressed textures (32 MB a plus character as imported); a real sculpt or CC0 base over the blockout (plain
  face, mitten hands); the zombie's face driven by the sim's noticing (Sight); the first-person arms rig.

**Traps hit:** importing a glTF through the Blender MCP (`execute_blender_code`) closed Blender outright (no crash
log); the same import headless takes 0.7 s, so do imports in art/scripts and open the result for inspection.
Skeleton3D restores its poses after its modifiers (read final poses inside a modifier). Godot reserves a block of
global shader-buffer slots for every mesh with instance uniforms (near-tier characters top out near 300; the buffer
is raised in project.godot). The glTF exporter keeps every bone's channels unless told not to. Shell heredocs here can
turn `\n` into a newline: write patch scripts to files.

## Smaller

- The 21 `/*TODO ...*/` markers the translator left in cs/Sim (each works; most are types it couldn't name).
