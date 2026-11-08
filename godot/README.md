# LoonieRoo in Godot

The game: the sim (`cs/Sim`, pure C#, no Godot) drawn and played in Godot 4.6 .NET. NOTES.md "Start here" has the controls and the map of everything.

- `Game.cs`: the loop (10 ticks a second times the speed, fixed timestep, interpolated between ticks), input turned into sim commands, the camera, saves and autosave, the command line.
- **The views.** Playing a survivor, the game is **first person** (`World3D/`). **M** is the map screen, top-down, with two tabs (`Ui/ScreenTabs.cs`): **Map** (`Ui/WorldMap.cs`) and **Colony** (the colony's management: build, priorities, the overview, neighbours, drawn by `Render/` in 2D top-down). With nobody under your control, the Colony view is the game. There is no isometric view.
- `World3D/`: first person. `FirstPerson.cs` (the camera at your eyes, the sun, sky, fog and night; chunks of the world built near you), `WorldMesh.cs` (a 16x16-tile chunk as one mesh: ground, tile and edge walls, doors open or shut, windows, boards, barricades, a ceiling over each indoor tile, furniture as boxes; rebuilt only when what it shows changes, 4 a frame at most), `Roofs.cs` (what's indoors and the roofs over it: a generated building's footprint from the world plan while at least half its outer walls stand, else it's a ruin with neither; rooms you wall in yourself; per building its own node, gable or hip for houses, barns, sheds and churches, flat with a parapet for shops, civic buildings and warehouses; flat boards over your rooms; less ambient light under a roof), `People.cs` (the nearest 40 people within 40 m as the look lab's "plus" character models, animated from the sim's pose intents: idle, walk, jog, sprint, crouch, lying, driving, the dead's attack and stagger, their face angry once they're after someone; LOD by height on screen; pooled by look), `Animals.cs` (deer, rabbits, wolves, bears and dogs as their own models: art/scripts/build_animals.py), `Bodies.cs` (cars, things on the ground, and people past the models' budget, as placeholder capsules and boxes), `Trees.cs` (trees and undergrowth by chunk as MultiMeshes: five species by a hash of the tile, three LODs, sway; docs/trees.md), `Terrain.cs` (the ground's height, flat for now). 1 tile = 1 m.
- `Render/`: the 2D top-down drawing (the Colony tab). `Ctx.cs` is a small Canvas 2D-style context over Godot's CanvasItem calls (the renderer was ported from the browser's canvas code); `Camera.cs` holds the top-down projection; `Draw.cs`, `DrawThings.cs`, `DrawPeople.cs`; `Palette.cs`.
- `Ui/`: the HUD and its panels (`Hud.cs`, `Card.cs`), the build tools (`Tools.cs`), the menu (`Menu.cs`), saves (`Saves.cs`), the minimap and the map (`Minimap.cs`, `WorldMap.cs`), the view state (`View.cs`), and a small panel kit (`Ui.cs`).
- `SelfTest.cs`: `--selftest` drives the running game with real input events and checks what they did.

## Build and run

Open this folder (`godot/`, not `experiments/godot-bench`) in Godot 4.6 .NET and press Play. Or:

```bash
dotnet build godot/LoonieRoo.csproj
```

```bash
"/c/Program Files/Godot_v4.6.1-stable_mono_win64/Godot_v4.6.1-stable_mono_win64_console.exe" --path godot
```

Without options it opens on the main menu. Options go after `--` and skip the menu:

- `--seed=N --size=N --start=solo|group`: a new world (a solo start, 128 tiles, a random seed by default); `--load=save.json` a save file instead
- `--ticks=N`: run N ticks first
- `--zoom=Z --paused --debug --map --colony --overview --build=category --menu=main|new|character|load|howto`: the view to start in (`--map` and `--colony` open the M screen's tabs)
- `--look=DEG --pitch=DEG`: first person, which way you look at the start (0 east, 90 south, 270 north; pitch up positive)
- `--at=X,Y`: put the survivor you control on tile X, Y first (a debug start for screenshots; not a command, so not in the log)
- `--eye=X,Y,H`: first person from tile point (X, Y), H m up, instead of your eyes (a debug view: roofs from outside or above, a street, a room)
- `--arena=one|crowd`: a debug arena (the dead round you); `--play`: on a group start, play its first survivor (to see the others in first person)
- `--animals=N` or `--animals=wolf:3,deer`: animals spawned round you (the `spawnAnimal` command): N of them, the kinds in turn (deer, rabbit, wolf, bear, dog), or kinds with counts; `--animal-range=M` how far out the nearest stand (3.5 m; at 1.6 a wolf or bear bites at once). With `--paused` they hold still for a look: `--animals=deer,wolf,bear,dog,rabbit --paused --look=137`
- `--shot=out.png --frames=N`: save a screenshot after N frames and quit
- `--selftest`: the self-test (22 checks; saves go to a separate folder, never your slots); `./check.sh --all` runs it

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
in a jacket and in a hoodie (clothes); a Zombie face, Clothes, Hair, Hair colour and Beard pickers (and a "hair" lineup of every style); Turn (T); LOD auto by height
on screen or forced; the zombie's face neutral, angry or auto (angry within sight range); idle, walk or lunge (walks at
you from 8 m); day, dusk or night (a torch); wireframe; a crowd of up to 2,000. The readout has the character's
triangles and its textures' memory, what's on screen, and CPU and GPU frame times. Command line (after `--`):
`--subject= --cand= --lod= --dist= --face= --move= --light= --crowd= --crowdlod= --wire --nopanel --noaim`,
`--zface= --clothes= --turn= --hair= --haircolour= --beard --hat=beanie --hatcolour= --jaw=V` (hold the jaw open)
`--traceface --nonormal --noalbedo` (the body's maps off, to tell a texture mark from a shape),
`--shot=out.png`, `--shots=DIR` (the comparison set), `--report=DIR` (the budget sweep: experiments/look-lab/look.md).
Keys: wheel or [ ] distance, 1-6 presets, F face, L LOD, V wireframe, right-drag to look round.

`Characters/Bench.tscn` is the animation benchmark and `Characters/HordeBench.tscn` the horde one (vertex animation textures on MultiMesh); experiments/anim-bench/README.md has how to run them and the results.
