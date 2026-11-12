# LoonieRoo in Godot

The game: the sim (`cs/Sim`, pure C#, no Godot) drawn and played in Godot 4.6 .NET. The controls are in docs/controls.md, the map of every module in docs/modules.md.

## Which to use when

To check behaviour or layout, read text first. A picture costs a launch
(~3 s) and a big image to read; text costs a second and a few lines.

1. **Where is it, what is next to it, what happened tick by tick:** the text
   trace. No window, no picture.
2. **What is around me, is the wall there:** the text map.
3. **How the layout looks (what is where on screen, is the HUD in the way):**
   a small picture, `--shot-width=640`.
4. **Light, materials, models, animation, anything about how it looks:** a
   full-size picture (no scale; 1280x720).
5. **Several moments:** one launch with `--shot-at=5,10,20`, not one each.

```bash
GODOT="/c/Program Files/Godot_v4.6.1-stable_mono_win64/Godot_v4.6.1-stable_mono_win64_console.exe"
dotnet build godot/LoonieRoo.csproj   # the game runs the Debug build: build it first
"$GODOT" --headless --path godot -- --seed=7 --ticks=12 --hold=w --look=0 --trace=3 --trace-near=6 --textmap=6
```

(`./shot.sh <game args>` builds Debug and runs the game for you, for runs with a window: it does not pass `--headless`.)

**Text trace** (`GameText.cs`; works under `--headless`):
- `--trace` one line per tick for the survivor you control; `--trace=K` every
  Kth tick (the first and the last tick are always shown).
  `t=12 @67.50,65.50 tile 67,65 face 0 stand v2.5 hp 1000 steer 64,0`:
  the tick, the exact position in tiles, the tile, facing in degrees (0 east,
  90 south), the stance and speed (m/s), health, and `run`, `airborne`, the
  steer and the action in progress (`act swing 0.35`) when there are some.
- `--trace-near=R` adds a line for each mover within R tiles, nearest first
  (12 at most): `  wolf w1 @68.21,64.27 d3.9 hp 150`. Zombies (`z`, `z-crawl`),
  animals by kind, `dog`, other survivors `s`, residents `res`.
- It covers the ticks `--ticks=N` runs, one by one. With `--trace` or
  `--textmap`, `--at`, `--animals` and `--hold` take effect before those
  ticks (without them, `--at` comes after). `--hold` keys are applied as the
  game's frames would. `--look=DEG` sets the way a held key walks.
- Without `--shot`, the game quits when the text is printed: no frames, no
  window needed (a headless run of 12 ticks takes ~1.5 s). With `--shot`, the
  picture follows, and the ticks to `--shot-at` are traced too.

**Text map:**
- `--textmap=R` prints the sim's `DebugText` map of the (2R+1) tiles square
  round you at the end of the run; `--textmap-at=5,20` also at those ticks.
  `@` is you. Where the world has edge walls and a doorway or wall stands in
  the square, it prints the map with the borders between tiles (see its
  header line for the characters). The whole world's map is made first, so a
  huge world is slow.

**Pictures** (they need a window, so not `--headless`; it says so and fails):
- `--shot=out.png --frames=N` as ever (after N frames, quit).
- `--shot-at=5,10,20` with `--shot=out.png`: one picture per listed tick,
  `out-5.png`, `out-10.png`, `out-20.png`, from one launch. The sim runs only
  to those ticks, one tick at a time (no real-time pacing, so it is exact and
  repeatable), 3 frames are given to catch up, and the picture shows the tick
  itself, not a blend with the last. Ticks are the sim's (absolute). The first
  picture waits for `--frames`.
- `--shot-width=640` or `--shot-scale=0.5`: save the picture smaller (width in
  pixels, or a fraction of 1280x720). Each shot prints its path, size and
  bytes. What an image costs the model follows its pixels, not its bytes:
  640x360 is about a quarter of a full picture's.
  - ~640 wide: enough for layout (is it on screen, where, how big, the HUD,
    the sky and ground, the room's shape). 512 is the least; the HUD's small
    text is already hard to read.
  - Full size (1280x720): to judge a model, its edges, a texture, a shadow,
    text on screen, or a small thing far away.

- `App/`: the game itself, one class (`Game`) in parts by concern. `Game.cs` (what the game is made of, and the frame: 10 ticks a second times the speed, fixed timestep, interpolated between ticks; then the views), `GameArgs.cs` (the command line: which world, the debug flags, the view to start with; the flags that set a scene call the sim's `Staging`, so the game never writes state itself), `GameSession.cs` (new, load, save, import, autosave's log, bug reports, the menu's actions), `GameInput.cs` (keys and mouse into sim commands, the top-down camera), `GameUse.cs` (E or right-click: the tiles the look passes over within arm's reach, nearest first, then in front, then underfoot; an open boot behind a car), `GameShots.cs` (`--shot`), `GameText.cs` (`--trace`, `--textmap`); and two helpers for the whole game, `Nodes.cs` (find the nodes of a type in a scene) and `IdHash.cs` (picks by a sim id: a body's hair, a car's model, an item's variant, the same every run).
- **The views.** Playing a survivor, the game is **first person** (`World3D/`). **M** is the map screen, top-down, with two tabs (`Ui/ScreenTabs.cs`): **Map** (`Ui/WorldMap.cs`) and **Colony** (the colony's management: build, priorities, the overview, neighbours, drawn by `Render/` in 2D top-down). With nobody under your control, the Colony view is the game. There is no isometric view.
- `World3D/`: first person. `FirstPerson.cs` (the camera at your eyes, the sun, sky, fog and night; chunks of the world built near you), `WorldMesh.cs` (a 16x16-tile chunk as one mesh: ground, tile and edge walls, doors open or shut, windows, boards, barricades, a ceiling over each indoor tile, furniture as boxes; rebuilt only when what it shows changes, 4 a frame at most; in parts: `.Ground.cs` a tile's ground, kerbs, lane lines, road corners, water and the water under a bridge, `.Smooth.cs` the smooth banks and edges between kinds of ground, `.Walls.cs` what stands on a tile's edges), `Roofs.cs` (what's indoors and the roofs over it: a generated building's footprint from the world plan while at least half its outer walls stand, else it's a ruin with neither; rooms you wall in yourself; per building its own node, gable or hip for houses, barns, sheds and churches, flat with a parapet for shops, civic buildings and warehouses; flat boards over your rooms; less ambient light under a roof), `People.cs` (the nearest 40 people within 40 m as the look lab's "pz" character models, animated from the sim's pose intents on one AnimationPlayer each with the derived clips (assets/characters/clips_more.glb, art/scripts/derive_clips.py): planted gaits picked and rated by speed (Characters/Gaits.cs), sneaking, a shuffle turning on the spot, stops; the dead's gaits by a hash of the id, crawling on planted hands, lying and getting up when woken, hit from a side, lunging, falling when killed; flinching, busy actions, colonists at work, carrying; a hop for a lost leg, a held stump for a lost arm; foot IK on the sim's heights for you and the nearest six within 8 m, the near dead's heads turned to their target (HeadLook.cs); LOD by height on screen; pooled by look; People.Leap.cs: your body through a jump, vault or hop, and the eye's dip on landing), `Animals.cs` (deer, rabbits, wolves, bears and dogs as their own models: art/scripts/build_animals.py), `Items.cs` (things lying on the ground as their own models, one MultiMesh per model, the nearest 300 within 40 m, on the ground and tilted to its slope, a variant per item by a hash of its id: art/scripts/build_items.py; no LOD, measured by `ItemBench.tscn`), `SwimHands.cs` (your arms making a breast stroke while you swim, the weapon put away), `Held.cs` (what you wield, your tool and what you carry, in first person with the same models and gloved hands and sleeves; a rifle and an axe in both hands; a swing with wind-up and follow-through, a gun's kick (only when it fired: an empty gun's click is a pistol-whip or a butt stroke, as the sim swings it), a punch with bare hands, the sights (the right mouse held: the gun at the eye line, the view narrowed), a walking bob, a breath, a lag behind the look, lowered running, a raise on a change), `Bodies.cs` (cars and people past the models' budget, as placeholder capsules and boxes), `Trees.cs` (trees and undergrowth by chunk as MultiMeshes: five species by a hash of the tile, three LODs, sway; docs/trees.md), `Undergrowth.cs` (grass tufts, weeds, bushes, saplings and the cracks and tufts on old roads, by the sim's growth stage, MultiMeshes by chunk within 2 chunks of you; docs/trees.md "Growth"), `Fires.cs` (burning tiles near you as flames sized by what burns (grass, undergrowth, a tree, a wall), smoke columns, embers, dark scorch patches on every burnt tile and charred stumps where trees stood, in five nodes whatever burns: instanced, the shaders do the motion, no lights: the lighting pass lights fires from `state.fires`; created in Game.cs and updated after `FirstPerson.Update`; budgets in the file's header), `Weather.cs` (`WeatherLayer`: rain and snow falling round the camera as two particle systems, not under a roof; a layer of snow on open ground by chunk that follows the sim's `Snow` depth, with trodden trails tinted and dipped and a car's ruts cut in; the overcast sky, dimmer sun and nearer fog on bad days, through the one hook FirstPerson calls last in `Update`; the global shader parameters `snow_cover` and `weather_wet`, with `assets/shaders/snow_cover.gdshaderinc` for a material to adopt them; `WEATHER_STATS=1` prints its cost, `WEATHER_OFF=1` draws no weather), `Terrain.cs` (the ground's height, the one hook everything stands on: it reads the sim's `Heights` field, gentle rolling ground with level building plots, roads that follow it and water under its banks; hand-made maps are flat; `Height` is what stands at a point stands on, so a street's asphalt is a kerb's height under its sidewalks: `Sunk`, `Kerb`, and `WorldMesh` draws the kerb's face and bevels a slanted road's stepped edge). The ground is a quad per tile on shared corner heights (no seams), casting no shadows; walls, doors and windows stand on their tile's floor with a footing down to the lowest ground; your eye's height over the ground is eased. 1 tile = 1 m.
- **What first person draws since 2026-11-11** (all in `World3D/`, each reads state only): `ModelSet.cs` (a file of low-poly models and the drawing of many of them, one MultiMesh a model; the newer renderers share it), `Fields.cs` (farm plots: the bare bed, the crop by stage), `Sites.cs` (blueprints: pegs and tape, a stud frame rising with the work, joists, the planks delivered), `Cars.cs` (the cars you can drive, as the sedan and pickup models; `Bodies.cs` drew a box), `Aircraft.cs` (the timeline's helicopter overhead, its rotor turning), `Others.cs` (wanderers, traders, prisoners, caravans and scavenging parties: the sim keeps each as a tile, so the view makes stand-in bodies that `People.cs` draws; a trader's stall and a caravan's handcart). Their models are `assets/world/world.glb` (art/scripts/build_world.py) and the furniture's cars.
- **Country roads** (2026-11-12, docs/roads-off-grid.md): `World3D/RoadRibbons.cs` draws each country road as a smooth strip along the world plan's line for it (asphalt, a dashed centre line, solid edge lines), on the ground's heights; the road tiles under it are drawn as the verge beside them (`WorldMesh.Nearest`) and still decide what the sim does. Town streets keep their tiles, kerbs and `WorldMesh.LaneMark` lines.
- `World3D/MeshBuf.cs`: SurfaceTool's calls (SetColor, SetUV, SetUV2, SetNormal, AddVertex, Commit) in C# lists, handed to Godot once a surface; the chunks (WorldMesh) and roofs build with it (SurfaceTool is an engine call per attribute per vertex). The far field and road ribbons still use SurfaceTool.
- **Textures** (2026-11-11, docs/textures.md): `World3D/Surfaces.cs` loads the detail textures (`assets/surfaces/`: twenty tileable 256 px pictures made by art/scripts/build_surfaces.py, each a multiplier that averages to no change) into one texture array, the global `surface_layers`; a shader lays one over its flat colour with `assets/shaders/surface_detail.gdshaderinc` (by world position for the static world, by object position for what moves, one sample a fragment, faded out by 48 m), and a mesh says which layer each face wants in its UV. `World3D/WorldLook.cs` says what each face of the world is, from what the sim already knows: the ground by its kind (grass, forest litter, earth, asphalt, paving, floorboards, tiles, concrete) in colours of its own, the same in the far view; a generated building's walls by its plan's kind and style and a roll of its lot (siding in five paints, brick, render, block, corrugated sheet outside; plaster inside, bare boards or sheet in barns, sheds, cabins and industrial buildings), trim round doors and windows, grime on glass, a concrete footing on a slope; roofs in shingles, sheet or tar and gravel (`Roofs.cs`, which keeps each building's style by tile for the chunks), with snow settling on them; what you build yourself is bare boards. A chunk is still one surface and one draw: the layer is a number on the vertex. Pictures and frame times: experiments/textures/, log/2026-11-11-12-textures-surfaces.md.
- **The driver's seat** (2026-11-11): `World3D/Cockpit.cs` draws the car you drive from the inside (the cabin model round the camera, the steering wheel turning with the keys, the car's outside as a shadow only) and reads the sim's car onto `World3D/GaugeCluster.cs`, a 2D drawing in a small viewport shown on a panel in the binnacle: speedometer (km/h), fuel, temperature (the car's health), the OIL, BATT, ENGINE, BRAKE, FUEL and LIGHTS lamps, P R N D; the tachometer and the odometer are worked out in the view. The eye is at the driver's seat (left of the car's centre), and the view turns with the car (`FirstPerson.CarTurn`).
- `Render/`: the 2D top-down drawing (the Colony tab). `Ctx.cs` is a small Canvas 2D-style context over Godot's CanvasItem calls (the renderer was ported from the browser's canvas code); `Camera.cs` holds the top-down projection; `Draw.cs`, `DrawThings.cs`, `DrawPeople.cs`; `Palette.cs`.
- `Ui/`: the HUD and its panels (`Hud.cs`; `DeathScreen.cs`, when the one you played has died: who carries on, through the `succeed` command; `HudColony.cs`, part of it: the colony's panels and the survivor table; `Card.cs`), the inventory screen (`Inventory.cs`, its controls in `InventoryTiles.cs`: Tab in first person or in the Colony tab; grids of cells for the bag, pockets, the container beside you and the ground, a column kept for the character sheet; drag with a ghost and R to turn, shift/ctrl-click across, right-click menu, keyboard cursor; docs/controls.md), the hotbar (`Hotbar.cs`: the quick slots along the bottom in first person, the one in hand lit), the build tools (`Tools.cs`), the menu (`Menu.cs`), saves (`Saves.cs`), the minimap and the map (`Minimap.cs`, `WorldMap.cs`), the view state (`View.cs`), and a small panel kit (`Ui.cs`).
- `SelfTest/`: `--selftest` drives the running game with real input events and checks what they did (`SelfTest.cs`; its inventory steps are `SelfTestInventory.cs`, the Options page's `SelfTestOptions.cs`, E and Space at cars, drawers, windows and fences `SelfTestInteract.cs` (`SELFTEST_ONLY=interact` runs just those, `SELFTEST_ONLY=cars` the car tricks on foot of `SelfTestCarTricks.cs`: push, alarm, under a pickup; with `LEAP_SHOT=prefix`, pictures of the vaults from the side), and so on by feature). Its fixtures (an item in your hand, a fire lit) are set straight on the state: it is a test, not the game.
- `Audio/`: every sound is made in code and played from what changes in the state: the synthesisers (`Synth.cs`, `SynthWorld.cs`, `SynthSteps.cs`) fill the bank of streams (`Bank.cs`); `Sounds.cs` plays them from events and changes; `Loops.cs` (generators, fires, engines), `WorldNoises.cs` (turrets, alarms, far-off noise), `Yours.cs` (your own steps and breath) and `Ambience.cs` (wind, rain, indoors) are the ongoing ones; `Muffle.cs` dulls what is behind walls; `Volume.cs` is the options' volume.
- Not described one by one above; each says what it is in its first lines: `World3D/Furniture.cs` (furniture as models in batches), `OwnBody.cs` (your own body under the camera), `Footfalls.cs` (where feet land), `People.Climb.cs` and `People.Leap.cs` (parts of People: the dead over cars, your leaps; `People.Clips.cs` choosing each body's clip, `People.You.cs` your own body, `People.Hold.cs` what a body holds in its hand, `People.Swim.cs` your body swimming seen from outside); `Characters/Wardrobe.cs`, `OutfitMerge.cs` and `Vat.cs` (used by the labs only), `StumpHold.cs`; `Ui/CharacterSheet.cs` (the Tab screen's sheet), `Ui/WorldPreviewView.cs` (a world's small picture on the New game and Load screens); `SelfTest/SelfTest*.cs`, a file per feature.
- `Labs/`: scenes of their own, run by path, never part of the game: the character lab (`Lab*.cs`), the look lab, the style lineup, the animation test, and the benches (`Bench`, `HordeBench`, `DrawBench`, `ItemBench`, `TreeBench`) and `MapShot`.
- **The options** (`Ui/Graphics.cs`, the menu's Options page): view distance (near only, 250, 500 (the default), 750, 1000 m), detail distance (the near chunks: 40-88 m), far trees (off, simple, detailed), shadows (off, low 24 m, medium 40 m, high 80 m), character models (the nearest 10-60 people and animals), 3D resolution (50-100%, FSR below 100), anti-aliasing (off, FXAA, MSAA 4x). Saved in `user://settings.cfg` (`[graphics]`, beside the volume's `[audio]`), applied at once. A `--shot` and `--selftest` use the defaults (and their flags), never the player's file.

## Build and run

Open this folder (`godot/`) in Godot 4.6 .NET and press Play. Or:

```bash
dotnet build godot/LoonieRoo.csproj
```

```bash
"/c/Program Files/Godot_v4.6.1-stable_mono_win64/Godot_v4.6.1-stable_mono_win64_console.exe" --path godot
```

Without options it opens on the main menu. Options go after `--` and skip the menu:

- `--seed=N --size=N --start=solo|group`: a new world (a solo start, 128 tiles, a random seed by default); `--load=save.json` a save file instead
- `--ticks=N`: run N ticks first (with `--trace` or `--textmap`, one at a time, each seen: above)
- `--zoom=Z --paused --debug --map --colony --overview --build=category --menu=main|new|character|load|howto|options`: the view to start in (`--map` and `--colony` open the M screen's tabs)
- `--look=DEG --pitch=DEG`: first person, which way you look at the start (0 east, 90 south, 270 north; pitch up positive)
- `--at=X,Y`: put the survivor you control on tile X, Y first (a debug start for screenshots; not a command, so not in the log)
- `--arm=KIND[:ROUNDS]`: put that weapon (`pistol`, `shotgun:3`, `knife`...) in your hand, a gun with ROUNDS in it (else 60% of its magazine) and a box of its ammunition in your bag, to see the HUD's ammunition readout and the muzzle flash in a `--shot` (not a command, so not in the log)
- `--clock=TICK --burn=dx,dy,dx,dy,..` (with `--weather=KIND`): a debug start for pictures of fire (not commands): the clock set (a summer afternoon is `--clock=30000`, dusk `31560`), the weather (`heatwave` is what lets grass fire run), and tiles alight at those offsets from you; with `--shot-at=` ticks to watch it spread and burn out (World3D/Fires.cs; pictures in experiments/wildfire/)
- `--lineup`: one of every item kind on the ground in front of you, three rows of seven eastward (to look at the item models; not a command); `--lineup=variants` the kinds with variants instead, a kind to a row (north to south), its models eastward; `--lineup=weapons` the weapons and their ammunition, two rows of ten; `--lineup=furniture` one of every furniture kind and look east of you instead (rows of eight, the vehicles, tower and silo in a row beyond; the land round you generated first)
- `--stage=farm,sites,visitors,heli,climb,crossings`: scenes east of you for pictures of what only the Colony tab used to draw (the sim's `Staging`; not commands): a farm with every crop at every stage, building sites at each step, a wanderer, a trader, a prisoner, a caravan and a scavenging party, and the helicopter overhead; `climb`: the dead getting over a sedan, a pickup and a barricade, each part-way through its crossing (open ground wanted; `--paused` holds them there: `--seed=42 --size=256 --at=136,89 --stage=climb --paused --day=1.3 --eye=133.5,88.6,3.2 --look=0 --pitch=-14`); `crossings`: animals held part way over and under a sedan and a pickup (AnimalCars.cs; with `--paused`)
- `--oncar` (after `--car`): you standing on the middle of the nearest car, for pictures from a roof (`--pitch=-55` looks down at it; `--eye=...` to see yourself on it)
- `--car=X,Y,H[,truck] --drive[=FUEL,HEALTH,KMH]`: a car (or a pickup) at a tile on a heading, and you behind its wheel, its tank and health at those percentages and going that fast: pictures of the dashboard (with `--hold=w,d` to drive and steer, `--pitch=-20` to look down at it, `--hour=22` for the lit panel). A straight road in `--seed=42 --size=256`: `--at=150,90 --car=152,92,0`
- `--arm=none`: your hand emptied into the bag (bare hands: the punch with `--swingat`); `--hold=aim`: the right mouse held (down the sights with a gun)
- `--held=MODEL --heldtool=MODEL --swingat=P`: draw that item model (`gun`, `axe`, `weapon_v2`...) in your right or left hand whatever you hold, and hold a swing (or a gun's kick) at P (0..1), for screenshots; `--raiseat=P` holds the raise of an item coming into your hand
- The map overviews: `Godot --path godot res://Labs/MapShot.tscn -- [--size=2000] [--worlds=mainland:1,island:4] [--out=DIR]` (whole generated worlds, a pixel a tile: docs/worldgen-speed.md)
- The draw bench: `Godot --path godot res://Labs/DrawBench.tscn -- [--size=2048] [--runs=0,250,500,1000,1000:lod2,1000:simple,1000:notrees] [--lazy]` (the far field at each range, its trees as the game has them, the far LOD all the way, crowns all the way, or none: docs/draw-distance.md)
- The whole game's bench: `experiments/perf/bench.sh [runs] [places]` (`OPT=1` on an optimized build): per place (a street, a furnished house, the country, a river and its bridge, a crowd of the dead), near chunks only and the far view at 500 m, the light bench's frame, GPU and render CPU ms, the chunk builds over the run (cold, JIT included) and the chunk bench after it (every chunk in range built three times, warm, by part: the tile loop and its ground, banks, edges, furniture, commits; `FirstPerson.ChunkBench`, printed with `--lightbench`). log/2026-11-12-17-performance.md
- The item bench: `Godot --path godot res://Labs/ItemBench.tscn -- [--runs=none@0,all@300,box@300,all@3000] [--frames=360] [--out=bench.md]` (items on every open tile within 60 m, a full turn; experiments/item-bench)
- `--hold=w,d`: those keys held down from the start (w, a, s, d, shift, space), to see walking, strafing or backing off in a `--shot` (with `--eye` to watch yourself from outside); `space` jumps again on each landing (a jump, a vault or a hop: the sim's leaps)
- `--view=M`: first person draws the far field (FarField.cs: coarse ground, buildings as blocks, far trees, crowns past 350 m) out to M metres, with the fog thinned to match (0: off, the near ~56 m); `--detail=N` the near chunks' radius (chunks of 16 tiles; 3, ~56 m). Both are for the run only, over the Options page's choice, not saved
- `--eye=X,Y,H`: first person from tile point (X, Y), H m over the ground there, instead of your eyes (a debug view: roofs from outside or above, a street, a room)
- `--day=N`: start on day N at 06:00 (17 is the second day of winter; a fraction counts from 06:00, 17.33 is 14:00; `--hour=H` picks the hour). Only the clock moves (nothing is played), and the world is grown as of that day (Growth.cs: grass, weeds, bushes, saplings now trees, cracked roads). The same place over time: `for d in 1 30 300; do ./shot.sh --seed=7 --size=256 --day=$d.25 --at=122,129 --look=45 --shot=street-day$d.png --frames=40 --shot-width=800; done` (experiments/growth/). `--weather=W`: that day's weather (clear, rain, storm, heatwave, snow, blizzard; it holds until the next dawn roll). `--snow=CM`: CM cm of snow on open ground in every region. For pictures of snow: `--seed=3 --day=17.33 --weather=snow --snow=22 --at=27,102 --ticks=3 --shot=out.png` (a solo start that is not at its shelter dies on the way in some minutes: shoot early)
- `--flat`: first person on flat ground, as before the heightmap (to compare; the sim's heights are untouched)
- `--arena=one|crowd`: a debug arena (the dead round you); `--play`: on a group start, play its first survivor (to see the others in first person)
- `--animals=N` or `--animals=wolf:3,deer`: animals spawned round you (the `spawnAnimal` command): N of them, the kinds in turn (deer, rabbit, wolf, bear, dog), or kinds with counts; `--animal-range=M` how far out the nearest stand (3.5 m; at 1.6 a wolf or bear bites at once). With `--paused` they hold still for a look: `--animals=deer,wolf,bear,dog,rabbit --paused --look=137`
- `--trace[=K] --trace-near=R --textmap=R --textmap-at=T,..`: the run as text (above)
- `--shot-at=T,.. --shot-width=W --shot-scale=F`: a picture per tick, smaller pictures (above)
- `--shot=out.png --frames=N`: save a screenshot after N frames and quit. The picture is taken even if frames throw (a half-made change): then it says how many did and exits 1, rather than hanging until your timeout. Assets not imported: it says so and quits
- `STEP_TRACE=1` in the environment: print each locomotion clip's footfall marks and each of your steps (clip, playhead, rate, the ankles' heights); `STEP_SHOT=prefix`: save a picture at your 4th to 6th steps (with `--hold=w --eye=...` to watch your feet)
- `LEAP_SHOT=prefix` in the environment: save pictures of your first two leaps (jump, vault, hop) at a quarter, half and three quarters of the way, `prefix-<leap>-<25|50|75>.png` (with `--hold=space --eye=...` to watch yourself; `--at` next to a broken window to vault it)
- `--hour=H --mains=off --switch=X,Y --give=flashlight:on --car=X,Y,H --fire=X,Y --stove=X,Y --heater=X,Y`: lights for pictures (App/GameArgs.cs; not commands): the clock jumped to H o'clock without running the day, the mains gone, a room's switch flipped, a flashlight on in your bag, a car with its headlights on, a fire, a stove being cooked at, a lit heater; `--lights=N --light-shadows=K` the renderer's light budget (24, 4 shadowed; -1 none at all) and `--lightbench=0,8,16,32` (or `N:K`) frame times at each budget, printed, then quit (World3D/Lights.cs)
- `--selftest`: the self-test (over a hundred checks; saves go to a separate folder, never your slots; `SELFTEST_SHOT=prefix` in the environment saves a picture of each new game it starts from the menu, prefix-group.png and prefix-solo.png, and of the New game and Load screens, prefix-new.png and prefix-load.png); `./check.sh --all` runs it, importing the assets first (`--headless --import`: a fresh checkout or worktree has none imported). Run by hand, it needs the assets imported, else it fails at once naming what isn't. Only its own mouse events count (the real mouse can move during a run), and a step that throws fails the run instead of hanging it
- `--invshot=x.png` (with `--selftest`): also save pictures of the inventory screen mid-test: x.png (a full bag beside a stocked shelf, first person), x-drag.png (holding a hatchet over the bag: the ghost) and x-colony.png (in the Colony tab); `--shot-width` scales them

Logs: `%APPDATA%\Godot\app_userdata\LoonieRoo\logs`. Saves: `...\LoonieRoo\saves`. Exports and bug reports: `Documents\LoonieRoo`.

## The character lab

`Labs/Lab.tscn` (Lab.cs and its parts: LabIk.cs, LabCrowd.cs, LabHands.cs; FootIK.cs, HandIK.cs) is the 3D
character test bed (docs/characters.md): the test mannequin on a 1 m grid, driven through an AnimationTree. In the
editor: open the scene and press F6 (Run Current Scene). The panel's tabs:
- **Move:** gaits, speed, acceleration, crouch, prone and crawl (army to zombie), rest on the back, crossfade, slow
  motion, move round a circle or in place, the Demo (every transition in turn).
- **Body:** survivor states (panic, fatigue, cold, pain sliders; Startle; Zombie skips them), a face view, and
  missing limbs (a cut per limb).
- **Clothes:** a garment and colour per slot (shirt, jacket, legs, feet, hands, head, back), dirt, blood, torn,
  wet, a random outfit; Mid twin shows the same outfit merged into one mesh beside the character (OutfitMerge.cs).
- **Ground:** flat, a 15 cm kerb, a hill (slope slider), stairs (18 cm rise); foot IK on or off.
- **Crowd:** up to 2,000 more humanoids in random outfits; LOD near (14 meshes + clothes + AnimationTree), mid (1 merged mesh + AnimationPlayer at a
  chosen rate: every frame, 30, 20 or 15 fps) or auto (near within a radius up to a budget, mid beyond); frame time
  and draw calls.
- **Hands:** a weapon (pistol, rifle, knife, bat) held by hand IK; guns ready or aimed at eye level (F), aim pitch,
  raise time; actions (swing, stab, fire, reload); a door to open quietly or fast.
- **Car:** a mock-up car (LabCar.cs): Get in, Get out, Sit in it, Boot, Bonnet (open or shut: LabCarLids.cs), action
  time, a scrub slider (holds the action at a progress), steer, throttle, brake, the roof (hidden to see in).
- **Ragdoll:** knock back or forward (impulse slider), a hit (upper body), get up (or by itself once at rest).
- **Library:** the clips retargeted from a third-party library onto our rig (assets/characters/clips_ual.glb, made
  by art/scripts/retarget.py; LabLibrary.cs): pick one (or Prev/Next), clip speed; it plays over everything, in place.

Below the tabs: camera follow, views and zoom, and the readout. Keys: 1-4 idle/walk/jog/run, C crouch, X prone,
Z rest on the back, F aim, right-drag orbit, wheel zoom. From the command line (after `--`): `--check` (skeleton vs the
humanoid profile), `--shot=out.png --frames=N`, `--speed= --crouch= --stance=rest|prone --style= --view=`,
`--lose=armLeft:elbow,legRight:knee`, `--ground=hill --slope=25 --noik`, `--crowd=1000 --lod=auto --rate=20`,
`--weapon=rifle --aim=10 --raised --door=quiet --nohandik`, `--panic= --fatigue= --cold= --pain= --startle --zombie --view=face`,
`--outfit=shirt,jacket,trousers,boots --dirt= --blood= --torn= --wet= --randomoutfit=SEED --twin` (a mid-tier copy beside the character), `--midbare` (the crowd's mid tier undressed), `--clip=ual_walk --clipat=0.4` (a library clip, held at 0.4 s), `--ragdoll=back|front|hit --noautoup`, `--action=swing|stab|fire|reload --actionat=P` (a weapon action, held at P), `--notoe --nolock` (the foot IK's toe ray and foot locking off), `--tab=Crowd --zoom=30 --novsync --trace`, and the tests
`--iktest=DIR` (feet on the kerb, hill and stairs, facing round, IK on and off), `--walktest=DIR` (a real walk: how far a planted foot slides, lock off and on) and `--handtest=DIR` (every weapon, its actions
and the door) `--cartest=DIR` (getting in, seated with steering and pedals, getting out) and `--ragtest=DIR` (knocked down both ways, up again, a hit); experiments/ik-test
has a run of each. `--actioncurve` prints the swing's and the stab's speed profile (peak, at contact) and quits
(experiments/animation/actioncurve-*.md). The car from the command line: `--car=in|out|seated --carat=0.54 --steer=30 --throttle=0.5 --brake= --roof`, `--lid=boot|hood [--lidopen] --lidat=0.5`.

### The world's animation test (`Labs/AnimTest.tscn`)

The pz model played the way People.cs plays it in first person (Gaits.cs picks the clip and rate, People.LivingClip
the stops and turns), moved along the ground at a speed, its drawn feet measured: for each step, how far the planted
foot slides along the ground while down (the lower of heel and ball within 2.5 cm), and the planted sole's height on
slopes, with and without the near tier's foot IK. Cases: flat at 0.8 to 5 m/s, stopping, starting, turning on the
spot, sneaking, 8 deg up and down, the dead by variant and the crawl (its hands), a lost leg hopping and standing, a
lost arm's stump, the dead's head turning to a target. Headless works.

```bash
"$GODOT" --headless --path godot res://Labs/AnimTest.tscn -- --logic=after --out=experiments/animation [--cases=flat,dead]
```

- `--logic=before` plays People.cs's logic before 2026-11-10 (the library's walk, jog and sprint at assumed speeds).
- `--strip=N,EVERY` (with a window): N pictures of each case EVERY frames apart, from the side on a 25 cm checker,
  into `strip-<case>.png`; `--clips=more/dead_lunge,more/eat` plays each clip once in place instead (a strip across it);
  `--model=res://assets/characters/look/look_pz_zombie.glb` for the dead's look.
- `--calibrate`: each derived gait's ground speed as drawn, against Gaits.Measure's (which People uses).
- `--bench=N [--frames=F] [--noik]` (with a window): N models as People draws them, walking on the spot at mixed
  speeds, the nearest six with foot IK (`--noik`: none); the frame's mean and p95 into `bench-world.md`.
- `ANIMTRACE=1`: the left foot's heel and ball, height and speed, every frame.

## The look lab

`Labs/LookLab.tscn` (LookLab.cs) compares the candidate looks in first person (eye height 1.65 m, 60 degrees
vertical field of view) at the distances that matter: a subject (zombie or survivor) as the mannequin, `pz`
(Zomboid-like, 2.5k) or `plus` (Zomboid-plus, 30k down to 600), or the four side by side (lineup), the five zombie face candidates side by side (faces), or the survivor painted,
in a jacket and in a hoodie (clothes), or every lost limb the sim can say (stumps: an arm below the elbow and a leg below the knee, either side, on the survivor in a jacket and a hoodie and on the dead); a Zombie face, Clothes, Hair, Hair colour, Beard and Missing (lost limbs) pickers (and a "hair" lineup of every style; "hats", every style under the beanie as the game draws them; "people", survivors dressed as the world dresses them: Characters/Looks.cs picks hair, colour, beard and hat by a hash of the id; "dead", the dead dressed as the world dresses them: an outfit (the tee with one of the five faces, a long-sleeved shirt, a jacket, a hoodie), its top and trousers recoloured from a small worn palette through the file's recolour mask (recolour.gdshader, a shared material per file and colour pair, no instance uniforms), hair, beards and hats; with `--crowd=N` the crowd is dressed the same, a street of them); Turn (T); LOD auto by height
on screen or forced; the zombie's face neutral, angry or auto (angry within sight range); idle, walk or lunge (walks at
you from 8 m); day, dusk or night (a torch); wireframe; a crowd of up to 2,000. The readout has the character's
triangles and its textures' memory, what's on screen, and CPU and GPU frame times. Command line (after `--`):
`--subject= --cand= --lod= --dist= --face= --move= --light= --crowd= --crowdlod= --wire --nopanel --noaim`,
`--zface= --clothes= --turn= --hair= --haircolour= --beard --hat=beanie --hatcolour= --jaw=V` (hold the jaw open)
`--lose=armLeft:elbow,legRight:knee` (lost limbs, the sim's lostLimbs: Characters/StumpCut.cs cuts the body and its clothes at load and caps each cut: a living
sleeve folded up the back of the arm and pinned, a trouser leg knotted, the dead's torn with raw flesh inside) `--aimat=H` (look at height H on the subject, m) `--tracehair` (print each character's hair triangles by LOD, and what a stump cut cost), `--crowdbench=FILE` (the dead's crowd as it was before 2026-11-10's outfits and as the world dresses them now, 40 and 200 walking: frame, GPU and render CPU ms, draw calls, materials; experiments/look-lab/dead-crowd.md). The pz candidate wears the hair ladder's 3rd to 5th levels, as the game does
`--traceface --nonormal --noalbedo` (the body's maps off, to tell a texture mark from a shape),
`--shot=out.png`, `--shots=DIR` (the comparison set), `--report=DIR` (the budget sweep: experiments/look-lab/look.md).
Keys: wheel or [ ] distance, 1-6 presets, F face, L LOD, V wireframe, right-drag to look round.

`Labs/Bench.tscn` is the animation benchmark and `Labs/HordeBench.tscn` the horde one (vertex animation textures on MultiMesh); experiments/anim-bench/README.md has how to run them and the results.
