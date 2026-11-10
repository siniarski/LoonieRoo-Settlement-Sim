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

- `Game.cs`: the loop (10 ticks a second times the speed, fixed timestep, interpolated between ticks), input turned into sim commands, the camera, saves and autosave, the command line.
- **The views.** Playing a survivor, the game is **first person** (`World3D/`). **M** is the map screen, top-down, with two tabs (`Ui/ScreenTabs.cs`): **Map** (`Ui/WorldMap.cs`) and **Colony** (the colony's management: build, priorities, the overview, neighbours, drawn by `Render/` in 2D top-down). With nobody under your control, the Colony view is the game. There is no isometric view.
- `World3D/`: first person. `FirstPerson.cs` (the camera at your eyes, the sun, sky, fog and night; chunks of the world built near you), `WorldMesh.cs` (a 16x16-tile chunk as one mesh: ground, tile and edge walls, doors open or shut, windows, boards, barricades, a ceiling over each indoor tile, furniture as boxes; rebuilt only when what it shows changes, 4 a frame at most), `Roofs.cs` (what's indoors and the roofs over it: a generated building's footprint from the world plan while at least half its outer walls stand, else it's a ruin with neither; rooms you wall in yourself; per building its own node, gable or hip for houses, barns, sheds and churches, flat with a parapet for shops, civic buildings and warehouses; flat boards over your rooms; less ambient light under a roof), `People.cs` (the nearest 40 people within 40 m as the look lab's "plus" character models, animated from the sim's pose intents: idle, walk, jog, sprint, crouch, lying, driving, the dead's attack and stagger, their face angry once they're after someone; LOD by height on screen; pooled by look), `Animals.cs` (deer, rabbits, wolves, bears and dogs as their own models: art/scripts/build_animals.py), `Items.cs` (things lying on the ground as their own models, one MultiMesh per model, the nearest 300 within 40 m, on the ground and tilted to its slope, a variant per item by a hash of its id: art/scripts/build_items.py; no LOD, measured by `ItemBench.tscn`), `Held.cs` (what you wield, your tool and what you carry, in first person with the same models and gloved hands and sleeves; a rifle and an axe in both hands; a swing with wind-up and follow-through, a gun's kick, a walking bob, a breath, a lag behind the look, lowered running, a raise on a change), `Bodies.cs` (cars and people past the models' budget, as placeholder capsules and boxes), `Trees.cs` (trees and undergrowth by chunk as MultiMeshes: five species by a hash of the tile, three LODs, sway; docs/trees.md), `Undergrowth.cs` (grass tufts, weeds, bushes, saplings and the cracks and tufts on old roads, by the sim's growth stage, MultiMeshes by chunk within 2 chunks of you; docs/trees.md "Growth"), `Terrain.cs` (the ground's height, the one hook everything stands on: it reads the sim's `Heights` field, gentle rolling ground with level building plots, roads that follow it and water under its banks; hand-made maps are flat). The ground is a quad per tile on shared corner heights (no seams), casting no shadows; walls, doors and windows stand on their tile's floor with a footing down to the lowest ground; your eye's height over the ground is eased. 1 tile = 1 m.
- `Render/`: the 2D top-down drawing (the Colony tab). `Ctx.cs` is a small Canvas 2D-style context over Godot's CanvasItem calls (the renderer was ported from the browser's canvas code); `Camera.cs` holds the top-down projection; `Draw.cs`, `DrawThings.cs`, `DrawPeople.cs`; `Palette.cs`.
- `Ui/`: the HUD and its panels (`Hud.cs`, `Card.cs`), the inventory screen (`Inventory.cs`: Tab in first person or in the Colony tab; the bag, what you wear, a container beside you; things by name, moved by click, shift-click or drag: across, onto a slot to wear, out onto the world to drop), the build tools (`Tools.cs`), the menu (`Menu.cs`), saves (`Saves.cs`), the minimap and the map (`Minimap.cs`, `WorldMap.cs`), the view state (`View.cs`), and a small panel kit (`Ui.cs`).
- `SelfTest.cs`: `--selftest` drives the running game with real input events and checks what they did (its inventory steps are `SelfTestInventory.cs`, the Options page's `SelfTestOptions.cs`).
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
- `--day=D`: start at noon of day D: only the clock moves (nothing is played), so the world looks as D days of growth (Growth.cs: grass, weeds, bushes, saplings now trees, cracked roads) and that season. Pictures of the same place over time: `for d in 1 30 300; do ./shot.sh --seed=7 --size=256 --day=$d --at=122,129 --look=45 --shot=street-day$d.png --frames=40 --shot-width=800; done` (experiments/growth/)
- `--arm=KIND[:ROUNDS]`: put that weapon (`pistol`, `shotgun:3`, `knife`...) in your hand, a gun with ROUNDS in it (else 60% of its magazine) and a box of its ammunition in your bag, to see the HUD's ammunition readout and the muzzle flash in a `--shot` (not a command, so not in the log)
- `--lineup`: one of every item kind on the ground in front of you, three rows of seven eastward (to look at the item models; not a command); `--lineup=variants` the kinds with variants instead, a kind to a row (north to south), its models eastward; `--lineup=furniture` one of every furniture kind and look east of you instead (rows of eight, the vehicles, tower and silo in a row beyond; the land round you generated first)
- `--held=MODEL --heldtool=MODEL --swingat=P`: draw that item model (`gun`, `axe`, `weapon_v2`...) in your right or left hand whatever you hold, and hold a swing (or a gun's kick) at P (0..1), for screenshots; `--raiseat=P` holds the raise of an item coming into your hand
- The map overviews: `Godot --path godot res://World3D/MapShot.tscn -- [--size=2000] [--worlds=mainland:1,island:4] [--out=DIR]` (whole generated worlds, a pixel a tile: docs/worldgen-speed.md)
- The draw bench: `Godot --path godot res://World3D/DrawBench.tscn -- [--size=2048] [--runs=0,250,500,1000,1000:lod2,1000:simple,1000:notrees] [--lazy]` (the far field at each range, its trees as the game has them, the far LOD all the way, crowns all the way, or none: docs/draw-distance.md)
- The item bench: `Godot --path godot res://World3D/ItemBench.tscn -- [--runs=none@0,all@300,box@300,all@3000] [--frames=360] [--out=bench.md]` (items on every open tile within 60 m, a full turn; experiments/item-bench)
- `--hold=w,d`: those keys held down from the start (w, a, s, d, shift, space), to see walking, strafing or backing off in a `--shot` (with `--eye` to watch yourself from outside); `space` jumps again on each landing (a jump, a vault or a hop: the sim's leaps)
- `--view=M`: first person draws the far field (FarField.cs: coarse ground, buildings as blocks, far trees, crowns past 350 m) out to M metres, with the fog thinned to match (0: off, the near ~56 m); `--detail=N` the near chunks' radius (chunks of 16 tiles; 3, ~56 m). Both are for the run only, over the Options page's choice, not saved
- `--eye=X,Y,H`: first person from tile point (X, Y), H m over the ground there, instead of your eyes (a debug view: roofs from outside or above, a street, a room)
- `--flat`: first person on flat ground, as before the heightmap (to compare; the sim's heights are untouched)
- `--arena=one|crowd`: a debug arena (the dead round you); `--play`: on a group start, play its first survivor (to see the others in first person)
- `--animals=N` or `--animals=wolf:3,deer`: animals spawned round you (the `spawnAnimal` command): N of them, the kinds in turn (deer, rabbit, wolf, bear, dog), or kinds with counts; `--animal-range=M` how far out the nearest stand (3.5 m; at 1.6 a wolf or bear bites at once). With `--paused` they hold still for a look: `--animals=deer,wolf,bear,dog,rabbit --paused --look=137`
- `--trace[=K] --trace-near=R --textmap=R --textmap-at=T,..`: the run as text (above)
- `--shot-at=T,.. --shot-width=W --shot-scale=F`: a picture per tick, smaller pictures (above)
- `--shot=out.png --frames=N`: save a screenshot after N frames and quit. The picture is taken even if frames throw (a half-made change): then it says how many did and exits 1, rather than hanging until your timeout. Assets not imported: it says so and quits
- `STEP_TRACE=1` in the environment: print each locomotion clip's footfall marks and each of your steps (clip, playhead, rate, the ankles' heights); `STEP_SHOT=prefix`: save a picture at your 4th to 6th steps (with `--hold=w --eye=...` to watch your feet)
- `LEAP_SHOT=prefix` in the environment: save pictures of your first two leaps (jump, vault, hop) at a quarter, half and three quarters of the way, `prefix-<leap>-<25|50|75>.png` (with `--hold=space --eye=...` to watch yourself; `--at` next to a broken window to vault it)
- `--selftest`: the self-test (62 checks; saves go to a separate folder, never your slots; `SELFTEST_SHOT=prefix` in the environment saves a picture of each new game it starts from the menu, prefix-group.png and prefix-solo.png, and of the New game and Load screens, prefix-new.png and prefix-load.png); `./check.sh --all` runs it, importing the assets first (`--headless --import`: a fresh checkout or worktree has none imported). Run by hand, it needs the assets imported, else it fails at once naming what isn't. Only its own mouse events count (the real mouse can move during a run), and a step that throws fails the run instead of hanging it
- `--invshot=x.png` (with `--selftest`): also save pictures of the inventory screen mid-test: x.png (beside a shelf, first person) and x-colony.png (in the Colony tab)

Logs: `%APPDATA%\Godot\app_userdata\LoonieRoo\logs`. Saves: `...\LoonieRoo\saves`. Exports and bug reports: `Documents\LoonieRoo`.

## The character lab

`Characters/Lab.tscn` (Lab.cs and its parts: LabIk.cs, LabCrowd.cs, LabHands.cs; FootIK.cs, HandIK.cs) is the 3D
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
has a run of each. The car from the command line: `--car=in|out|seated --carat=0.54 --steer=30 --throttle=0.5 --brake= --roof`, `--lid=boot|hood [--lidopen] --lidat=0.5`.

## The look lab

`Characters/LookLab.tscn` (LookLab.cs) compares the candidate looks in first person (eye height 1.65 m, 60 degrees
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

`Characters/Bench.tscn` is the animation benchmark and `Characters/HordeBench.tscn` the horde one (vertex animation textures on MultiMesh); experiments/anim-bench/README.md has how to run them and the results.
