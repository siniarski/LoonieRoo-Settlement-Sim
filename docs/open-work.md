# Open work

What is left, by area. Gathered on 2026-11-10 from NOTES.md and the session log, and rewritten on 2026-11-11 at the
merge of that day's sessions (log/2026-11-11-01-the-big-merge.md lists them; each has its own entry in `log/` with
the details behind a line here). Read the section your task touches. When you finish or find something, add a
session-log entry (`log/`) and update the line here.

## For the player to decide

Options the sessions found and did not take (each session's log entry has the numbers):
- **Thirst** (balance-check): keep needs as they are (recommended: thirst is ~1% of colony deaths, all among the badly
  wounded); or relabel the death ("died of their wounds (dehydrated)"); or drink before eating (thirst 8 -> 5 but
  starvation 31 -> 42 per 30 colonies). **Deer hunting never feeds anyone** (5-12 of ~480 deer seen in 30 colonies,
  none in 160 solo runs): is it meant to?
- **Light** (lighting): should light draw the dead, and should it change who sees whom (Sight)? Neither is built.
- **Snow** (weather): tracks the dead or raiders could follow; cold from snow and wet; `Snow.slowsWalking` is on (deep
  snow slows walking up to 30%: colonies fell about a day earlier over 4 seeds, within noise).
- **Fire** (wildfire): survivors don't avoid fire or cut firebreaks; `settings.wildfire` (0 off, 1 default, 2 double).
- **Weapons**: jamming and condition, attachments, aiming down sights, recovering bolts, a bow, friendly fire, shots
  breaking glass, cover from furniture. The first colony runs lost ~1.9 days to the new weapons before tuning; after
  it, +0.04 days colony and +0.13 solo over 150 seeds.
- **Inventory**: timed identification of finds, a backpack item or bag sizes (loot tables would need reweighting),
  icons rendered from the item models, bodies holding their kit.
- **Tools**: installing dotnet-trace for the next round of tick profiling (ask first; only worth it before big worlds).
- **Keys picked at the merge**: Z is the light switch (lighting had X, which weapons took for reload), G the
  flashlight or headlights, X reload, Q next weapon.

## Not played yet

Nothing built since 2026-11-05 has had a full playtest, and the Godot UI is checked only by the self-test and
screenshots (the browser's 142 UI tests weren't ported). Played 2026-11-09: tree collisions feel good; the shadow and
anti-aliasing settings look nice; the far view works. To try:
- **New on 2026-11-10/11:** the Tab screen (the character sheet on the left, the bag as a grid: drag, R to turn,
  shift- and ctrl-click, right-click menu, quick slots 1-9; tile text is small at 30 px cells); weapons (each kind's
  feel, pistol, revolver, shotgun, rifle, crossbow; X reload, Q switch); lights at night (Z switches, G flashlight,
  headlights, street lamps while the mains last, stoves and fires glowing); rain and snow (it lies, you tread a
  trail, it melts); a grass fire in a heatwave; a town after 30 and 300 days of growth (`--day=30`); the ground's
  relief (rivers in valleys); the dead in clothes, hair and hats; the new walks, shambles, crawlers and get-ups.
- Character creation; windows (see through, smash, climb, board up); line of sight and sneaking (C), sneak attacks;
  the water and power going off, the helicopter, alarms; cars (E to get in, W/S/A/D, the boot, fuel cans; timed entry,
  2.5 s in and 2 s out); the map (M) with notes; neighbours' errands, caravans, alliances and feuds; colonists
  foraging; the colony panels (wanderers, traders, neighbours, a survivor's story).
- Space's jump, vault and hop (heights, durations and stamina costs were picked without the player: log "Space is
  jump"); the body's leaps; the held items; animals moving freely.
- The footsteps and sounds (listen: `%APPDATA%\Godot\app_userdata\LoonieRoo\sounds-preview\*.wav`, written by the
  self-test; `step_*.wav` for steps); the furniture models; the pz look; your body in first person; the far field (on
  at 500 m; the menu's Options page sets it, 0-1000 m) and the other graphics options (nobody has used them in play).
- The dead from day one; new worlds (rivers, lakes, woods' edges, towns in clusters) and the New game and Load
  pictures.
- Questions: does free movement and combat feel right; is solo Apocalypse survivable (the scripted player reaches
  day 5); is the colony's winter readable.

## Speed

Done 2026-11-10: saves are written straight to text and skip chunks unchanged since generation (512 explored: 537 ->
10 ms; 2048: 9.3 s -> 79 ms; schema 78, 77 still loads); the tick is 1.7-2.4x faster with 3-6x less allocation
(`cs/Harness/Profile.cs`, `tick dumps`). Left:
- Loading is generation: every generated chunk is made again (2048 fully explored: ~6 of its 7.5 s) and it holds the
  frame behind the loading picture. Faster chunk generation, or chunks made on a worker (the statics in Worldplan,
  Heights and Time must be thread-safe first). A game that changed many chunks pays their remaking at each save: a
  per-session cache of made chunks would remove it.
- The tick's profile now: Zombies.updateZombies 45-70% (pushApart's bookkeeping, followPath, Motion.slide at ~270
  bytes a call), reconcileJobs 19% on hordes (guard-job zombie lookups), a new Point per mover per tick (`was`),
  BodyMod.updateBody 18% and Needs.updateHealth 11.5% on small colonies. Point being a class is the largest
  allocation left. The 1024 and 2048 stress scenes weren't profiled.
- Chunk generation is 1.6 ms a chunk at 2048 (0.9 before organic generation: more river curves per 64-tile bucket);
  a smaller bucket or a squared-distance early-out would win it back. The 10,240 preview takes ~20 s for its towns.
- Far-field regions built from the plan take 5 ms each on the main thread (~5.6 s of build time to fill 1 km): a
  worker would hide it.
- Shadows, resolution and anti-aliasing options aren't benchmarked; the furniture's draw cost was counted (214-257
  pieces in range in town) but not timed. World lights have a budget (`--lights=N`, `--lightbench`).
- The sim on its own thread (it's plain C#: Godot's main thread only needs a snapshot to draw).

## Scale and big worlds

Done 2026-11-10 (log grid-chunks; docs/worldplan-regions.md): the grid is stored in chunks behind the `Grids`
accessors (a new 10,000 world is 28 MB, not 4.4 GB; dumps and saves byte-identical); bug reports start from the
packed state. Left:
- **Unloading** (stage 5, a written plan only): a generated chunk is 51 KB, 48 KB of it furniture and its index. It
  needs furniture per chunk with a history-free order and an active region; trees out of `Furniture` (four fifths of
  the pieces) would cut a chunk to ~12 KB.
- At 10,000 a region's plan (~1.5 s) is made inside a tick the first time the sim asks beyond the start's region:
  the sim's askers should use `PlanSoon`, or `planStart` should reach further.
- Reachability unites every chunk's components at each walkability change (0.33 ms for six movers at 512; grows with
  the chunk count): per-chunk versions or a hierarchy. Room indexes renumber when an early room is built.
- A save hashes every generated chunk (~60 ms at 2048; perhaps 1.5 s at a fully explored 10,000): a per-chunk
  "touched since made" mark needs furniture's writes to go through one place.
- The map screen and minimap build an image of the whole map (`godot/Ui/WorldMap.cs`, `Minimap.cs`): 100 million
  pixels at 10,000.
- The stress harness exists. Left: zombies moving and crowding, A* for long paths. Storeys, basements and slopes as
  data are still to decide, after a very large stress test.

## World generation (log/2026-11-10-02-organic-world-generation.md)

- The 3D ground has relief now (`Heights.relief()` = 7 m x Terrain's elevation, render only). Left: a lot is levelled
  on its mean, so big lots on a slope are cut or filled up to half a metre at the edge (~1 m south of seed 7's
  start): level to the street side or terrace; a river on a cross slope tilts (its edge up to 0.14 m over the land
  downhill); the far field's coarse ground differs from the near by p95 0.4 m (max 1.6 m at the seam).
- Buildings: see "Buildings" below.
- Towns: streets are still axis-aligned grids (bending them touches lots, buildings and the street-end road links);
  thinner bands of towns along region borders; still three settlements at any world size.
- Water: rivers and lakes are the same water as ponds (thirst, sickness). The thirst worry was checked and is noise
  (log balance-check); two solo thirst deaths in 40 seeds remain, both mid-fight.
- Small worlds (128): the start's rise covers most of the map, so lakes are rare; forest varies more by seed at 256
  (12-28%).
- The preview draws lots whole (yards and all), not their buildings; big worlds (over 2 x 2 regions) show towns only,
  no roads.

## Buildings

Rewritten 2026-11-10/11 (docs/buildings.md; log buildings): plans are rooms with walls, doors and windows on edges;
dwellings by style (cottage, bungalow, family house, farmhouse, cabin, trailer), shops, bar, diner, workshop,
office, police, clinic, school, church, warehouse, factory, barn, shed; lots with paths, porches, driveways, plank
fences and sheds. **Worlds saved before it are refused** (`Worldgen.GEN_VERSION = 2`, saved in `WorldInfo.gen`: bump
it whenever what a chunk is made of changes). Left: chairs and the other pieces in docs/buildings.md's list; real
fence and gate edge kinds (fences are barricade edges with a gap); police cells only 1-2 when the corridor's last
room is short; side windows dropped on narrow lots leave long blank walls; a garage's 3-wide door is drawn as three
doors; FarField calls a lot's `yard(x, y)` with map coordinates where Lots uses the lot's (u, v) (an old bug);
storeys and basements (after the stress test).

## Growth, fire and weather

- **Growth** (Growth.cs, `state.cleared`, Undergrowth.cs): grass, weeds, bushes, saplings that become trees, cracked
  roads, by the days gone. Left: the balance players weren't re-run with it (the forage bonus and new trees shift a
  run slightly after day 1); far trees on land not yet generated don't grow.
- **Wildfire** (FireMod.cs, `state.burnt`, Fires.cs): fire through grass, undergrowth and trees, by dryness. A burnt
  tile's growth starts again (`Growth.clear`, wired at the merge) and grass under lying snow doesn't catch. Left: fire
  sound; Trees still draws ferns on a scar until the tile regrows; survivors don't avoid fire; balanced on two
  scripted seeds only; `FireMod.dryness` is a table of season and weather (weather's temperature could replace it).
- **Weather** (Snow.cs, `state.snow`, Weather.cs): temperature, rain, snow that lies by climate region, trodden
  trails and car ruts, melt. Left: wet ground is one global parameter (`weather_wet`); roofs and trees don't adopt
  `snow_cover` yet (assets/shaders/snow_cover.gdshaderinc); grass tufts poke green through deep snow.
- Debug flags were folded at the merge: one `--day=N` (06:00, fractions count, growth brought up to it), `--hour=H`,
  `--weather=W`, `--snow=CM`, `--clock=TICK`, `--burn=dx,dy,..` (wildfire's offsets), `--fire=X,Y` (one fire at a
  tile). godot/README.md has them.

## Lighting (log/2026-11-10-05-lighting.md)

Room lights with switches, outdoor house lights, street lamps, car headlights, the flashlight, stoves, heaters and
fires (Lights.cs; WorldLights.cs with a budget). Left: glows and lit windows are flat (no bloom); headlights are one
spot; a lit room is one ceiling point (big halls get one light); the flashlight's beam follows the camera's pitch but
the sim's is yaw only; ovens don't glow while you cook; colonists never touch switches (only the one you control).

## First person and the look

- Left from the first-person start (2026-11-08): the world's look (textures); what line of sight and fog of war mean
  in first person.
- Cars (log/2026-11-11-04-car-body.md): a car's body in the sim is the size of the car drawn (VehicleBody.cs).
  A parked car is in the way of everyone on foot (VehicleParking.cs, log/2026-11-11-06-parked-cars.md). Left: **a
  parked car is a barricade the dead can't break or climb** (they go round, or stand at it if there is no way
  round: a car across a gate seals it; Zomboid plays the same, but it is the player's call); the car you are
  sitting in is in nobody's way (the dead walk into it to get at you, as before); the dead and animals stop at
  the tiles under a car, so one can stand a third of a metre into its flank (you stop at the body); you can't
  jump onto or over a car; animals go under or over one by kind (AnimalCars.cs, log/2026-11-11-09-animals-cars.md;
  left there: meat from an animal killed mid-crossing drops on a car's tile, out of a colonist's
  reach; a crossing is a straight line, so a car on the slant is crossed square to the path, not to the car;
  animals have no climbing or crouching clips: they are lifted, and squashed to fit under; the corner fix in
  `Walkers.walkTo` makes a body caught on a corner inch round it, a few ticks slow); a wanderer, a trader or a dropped thing can still turn up on a car's tile; a wall doesn't
  turn a car sliding along it; the top speed is 22 km/h on a road, 11 off it (160 and 80 SUB a tick: the player's
  call whether cars should be faster now that they are car-sized).
- The driver's seat (log/2026-11-11-05-drivers-seat.md): Left: no hands on the wheel and no body in the seat; the
  mirrors are grey glass (nothing reflects); no windscreen glass, rain on it or wipers working; doors don't open
  as you get in; the tachometer and odometer are the view's (a trip is forgotten on a reload); the speedometer's
  dial goes to 60 and the car to 22; passengers aren't a thing; the cabin doesn't pitch or roll on a slope.
- Models added 2026-11-11 (log/2026-11-11-03-models.md): farm plots and crops, building sites, cars, the
  helicopter, the flashlight, and the people the sim keeps as a tile (wanderers, traders, prisoners, caravans,
  scavenging parties: `Others.cs`'s stand-ins). Left: a
  prisoner stands like anyone (no bound pose, no cell); a caravan or a run draws four people at most; a party
  walks straight at its next tile (through a wall if the sim's step crosses one); stand-ins have no names over
  them and can't be looked at for a card; ferns near trees still poke through a farm plot (Trees.cs draws them
  whatever the tile holds); the helicopter has no searchlight and its sound isn't placed at the model; a wall
  tile's blueprint is pegs only (no frame, as an edge wall has); the furniture docs/buildings.md still wants
  (chairs and the rest) needs sim looks first, then models.
- Trees: tile walkers and cars go round trunks now (`Motion.syncStep`, Trunks); in a one-tile corridor they still
  pass through; one trunk radius for every species (the species is drawing-only).
- Body leaps: no real vault clip (a tuck and a hand on the sill); the push-off and landing are the step sounds
  pitched, not their own.
- Held items: one sleeve colour (clothes are painted maps), not your own clothes'; the body's own hands on the grip
  (hand IK) not done; the bob isn't from your footfalls; things carried in both hands have no hands; holds not keyed
  to the sim's action progress; no aim-down-sights.
- Footsteps: crawling is silent (the crawl clip has no footfalls); a step falling in a 0.25 s clip blend is skipped.
- Furniture: chairs aren't pieces of their own (tables have two built in); a searched or emptied container looks the
  same; far places (the factory, camp cots) only checked in `--lineup=furniture`.
- Item models: no LOD (measured: not needed, experiments/item-bench); Godot's automatic mesh LODs never kick in on the
  MultiMeshes (their AABB is the whole 40 m round you).

## Sound (log/2026-11-09-11-sound-two-muffling-your-steps-the.md)

- Tuning by ear (levels, falloff, the muffle amounts, step stride); sound round corners (paths, not lines); others'
  footsteps (the dead shuffling, colonists); doors opening and shutting, items picked up, eating; indoor reverb; sound
  on the Colony tab and the map, if wanted; the rest of Noise's kinds (glass, alarms, engines); the startup cost of
  making the sounds.
- Weapons: a reload, a pump or bolt, a dry click, a crossbow's twang (it plays the swing's whoosh), a report for each
  gun (all four use one gunshot), blades and clubs on flesh. Fire and rain have no sound.
- A CC0 pack can replace the made sounds (`res://assets/sounds/<kind>_<n>.ogg`), with the player's OK.

## Weapons (log/2026-11-10-06-weapons.md)

Each weapon is a kind of its own (WeaponTable.cs: knife, hatchet, machete, crowbar, bat, pipe, sledge, spear, plank;
pistol, revolver, shotgun, rifle, crossbow; boxes ammo, ammo38, shells, ammo308, bolts), shots are lines through the
grid (Shooting.cs), with reload, switch and fire commands. The old `weapon` reads as a crowbar and `gun` as a pistol.
Left: the options above; the sounds above; cover (furniture doesn't stop a shot); your body doesn't swing weapons in
the world (Held's arms do).

## Inventory and the Tab screen

- The bag is a grid (InventoryGrid.cs; `Inventory.ITEM_SIZE` holds every kind's footprint, the new weapons' and the
  flashlight's added at the merge; quick slots take any wieldable weapon and the flashlight). Left: colonists still
  haul one item at a time and never use grids; a coat a colonist wears hides its pockets' contents until dropped;
  ammunition boxes both stack in a cell (10) and merge their rounds when stowed: pick one; the self-test's picture mode
  (`--invshot`) leaves a full bag, which fails the two weapon checks that follow it (the plain self-test is green).
- The character sheet (CharacterSheet.cs, the left 320 px column): nothing about wounds by place or pain exists in the
  sim to show; not tuned under 1280x720 (the column scrolls); it covers the left end of the key-hint strip.
- Names are only flavour (one model per kind; `Items.variant` could pick a model); a mixed stack moves its first
  item, whichever name; in the Colony tab the dim covers the HUD while the bag is open (Tab/Esc close it).

## The dead and animals

- From log/2026-11-09-09: the 2D view and the minimap don't show the dormant (fog hides them anyway); a dormant one's
  look is lost if it wakes outside the 40 m model range; the solo bot doesn't shut itself in (hiding indoors only
  counts as out of sight).
- The solo bot has no pack sense (it fights three at once, flees in a straight line).
- Animals off the grid: checked, the colony result was noise (log balance-check). Deer hunting: see the decisions.

## Characters (3D)

The plan and decisions are docs/characters.md (read it first); each step's details are in the session log of
2026-11-08. **Built:** a Blender pipeline run headless (art/scripts, art/README.md: the rig from Godot's humanoid
profile, a validator, a scripted mannequin with body regions, stump caps, a face with shape keys and 27 clips, nine
garments by region); a character lab (godot/Labs/Lab.tscn, godot/README.md: movement and stances, body states
and missing limbs, clothes, ground, crowd and LOD, hands); foot IK (kerb, hill, stairs); hand IK (pistol, rifle,
knife, bat, ready and aimed at eye level; a door); a car to get into and out of (seat, wheel and pedal IK);
retargeted CC0 clips (art/scripts/retarget.py: 43 Quaternius clips in clips_ual.glb; the download lives in
~/Downloads/anim-libs, outside the repo); the mid tier dressed (OutfitMerge.cs, one merged mesh per outfit); a
ragdoll (LabRagdoll.cs on Jolt: knockdown, hit, get-up blend); a VAT horde benchmark (20,000 in 10.8 ms,
HordeBench.tscn); weapon actions (LabActions.cs: swing, stab, recoil, reload); foot locking (FootIK.cs toe ray and
lock); the sim side (PoseIntents.cs, lostLimbs with side and level, startledAt, VehicleActions.cs); the look
(art/scripts/sculpt.py, paint.py, build_lookdev.py: "pz" 2.5k and "plus" 30k LOD ladders; experiments/look-lab).
**2026-11-10:** clothes end at a stump (StumpCut.cs); hair, beards and hats in the world (Looks.cs); the dead dressed
in recoloured outfits; derived clips (art/scripts/derive_clips.py, clips_more.glb): planted gaits picked and rated by
speed (Gaits.cs: foot slide 37-46 cm a step before, 0.7-5.7 now), varied shambles by a hash of the id, crawlers,
getting up when woken, hit reactions, falls, a hop for a lost leg, a held stump, colonists at work, the eye's dip on
landing, heads turned to a target (HeadLook.cs); `Labs/AnimTest.tscn` measures foot slide.
Tests: `--iktest`, `--handtest`, `--cartest`, `--ragtest`, `--walktest` (experiments/ik-test); benchmark
godot/Labs/Bench.tscn (experiments/anim-bench).

Blender 5.2 is installed; the Blender MCP server is in .mcp.json and approved locally (.claude/settings.local.json,
not in git): Blender must be open (its add-on listens on localhost:9876).

Left, any time:
- Animation: the mid tier still slides at stops and on-the-spot turns; no ragdoll in the world (the dead fall by
  clip, then the corpse item takes over with a pop); the weapon actions are better curves over keyed holds, not
  clips; sim durations that hold animation back (the walk 2.5 m/s, 3.3 on roads so it jogs there; a lost leg's
  2 m/s; the crawler's 1.45; VAULT 0.4 s; no wake delay).
- Clothing: real modelled garments over the scripted shells need an artist (folds, retopology, a shared atlas,
  weights, a recolour mask); the pz jacket and hoodie have blue texels bleeding onto the face; under a beanie the
  hair's edge at the nape is patchy; shoes aren't recoloured; the lab (Wardrobe.cs) has no pinned sleeve; zombies
  don't lose limbs in fights (only crawlers' legs).
- Library locomotion in the lab's blend space (phase-align the cycles); a second clip library to prove the bone-map
  step.
- A far LOD mesh (500-800 tris: the horde is GPU-bound on triangles); VAT as the lab crowd's far tier; the ragdoll
  budget and freezing bodies.
- Approaches (quiet/fast) as sim data, paired actions, anchors for doors and beds.
- VRAM-compressed textures (32 MB a plus character as imported); a real sculpt or CC0 base over the blockout (plain
  face, mitten hands); the zombie's face driven by the sim's noticing (Sight); the first-person arms rig.

**Traps hit:** importing a glTF through the Blender MCP (`execute_blender_code`) closed Blender outright (no crash
log); the same import headless takes 0.7 s, so do imports in art/scripts and open the result for inspection.
Skeleton3D restores its poses after its modifiers (read final poses inside a modifier). Godot reserves a block of
global shader-buffer slots for every mesh with instance uniforms (near-tier characters top out near 300; the buffer
is raised in project.godot). The glTF exporter keeps every bone's channels unless told not to. Shell heredocs here can
turn `\n` into a newline: write patch scripts to files.

## Code structure (log/2026-11-11-02-structure.md)

Done: area folders in the sim, the game class in parts, labs apart, `Staging`, `ArchitectureTest`, a README for
reviewing. Left, each long but one concern (split only with a reason): `Commands.execute` (a 780-line chain of
`if (command.type == ...)`: a method per group of commands would read better; every branch returns, so the order is
free), `Items/InventoryGrid.cs` (1,290 lines, in marked sections), `Dead/Zombies.cs` (1,200), `Colony/Work.cs`
(960), `godot/Ui/Inventory.cs` (1,020), `godot/Ui/Hud.cs` (920: each panel could be its own Control),
`godot/World3D/People.cs` (840). `State/StateTypes.cs` (1,360) is the schema and stays one file. The self-test sets
its fixtures straight on the state (allowed: it is a test); `Staging` could hold them too. The tests ported from
TypeScript are grab-bags under old names (docs/modules.md lists them): regroup when one is next touched.

## Smaller

- The 21 `/*TODO ...*/` markers the translator left in cs/Sim (each works; most are types it couldn't name).
