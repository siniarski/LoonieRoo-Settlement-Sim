# LoonieRoo in Godot

The game: the sim (`cs/Sim`, pure C#, no Godot) drawn and played in Godot 4.6 .NET. NOTES.md "Start here" has the controls and the map of everything.

- `Game.cs`: the loop (10 ticks a second times the speed, fixed timestep, interpolated between ticks), input turned into sim commands, the camera, saves and autosave, the command line.
- `Render/`: the drawing. `Ctx.cs` is a small Canvas 2D-style context over Godot's CanvasItem calls (the renderer was ported from the browser's canvas code); `Camera.cs` holds the projection (top-down now: swap it for isometric); `Draw.cs`, `DrawThings.cs`, `DrawPeople.cs`; `Palette.cs`.
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
- `--zoom=Z --paused --debug --map --overview --build=category --menu=main|new|character|load|howto`: the view to start in
- `--shot=out.png --frames=N`: save a screenshot after N frames and quit
- `--selftest`: the self-test (15 checks; saves go to a separate folder, never your slots); `./check.sh --all` runs it

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
  wet, a random outfit (the near crowd is dressed at random too).
- **Ground:** flat, a 15 cm kerb, a hill (slope slider), stairs (18 cm rise); foot IK on or off.
- **Crowd:** up to 2,000 more humanoids; LOD near (14 meshes + AnimationTree), mid (1 mesh + AnimationPlayer at a
  chosen rate: every frame, 30, 20 or 15 fps) or auto (near within a radius up to a budget, mid beyond); frame time
  and draw calls.
- **Hands:** a weapon (pistol, rifle, knife, bat) held by hand IK; guns ready or aimed at eye level (F), aim pitch,
  raise time; a door to open quietly or fast.

Below the tabs: camera follow, views and zoom, and the readout. Keys: 1-4 idle/walk/jog/run, C crouch, X prone,
Z rest on the back, F aim, right-drag orbit, wheel zoom. From the command line (after `--`): `--check` (skeleton vs the
humanoid profile), `--shot=out.png --frames=N`, `--speed= --crouch= --stance=rest|prone --style= --view=`,
`--lose=armLeft:elbow,legRight:knee`, `--ground=hill --slope=25 --noik`, `--crowd=1000 --lod=auto --rate=20`,
`--weapon=rifle --aim=10 --raised --door=quiet --nohandik`, `--panic= --fatigue= --cold= --pain= --startle --zombie --view=face`,
`--outfit=shirt,jacket,trousers,boots --dirt= --blood= --torn= --wet= --randomoutfit=SEED`, `--tab=Crowd --zoom=30 --novsync --trace`, and the tests
`--iktest=DIR` (feet on the kerb, hill and stairs, facing round, IK on and off) and `--handtest=DIR` (every weapon
and the door); experiments/ik-test has a run of each.

`Characters/Bench.tscn` is the animation benchmark (experiments/anim-bench/README.md has how to run it and the results).
