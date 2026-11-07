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
