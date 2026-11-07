# LoonieRoo in Godot

The C# sim (`cs/Sim`, pure C#, no Godot) drawn and played in Godot 4.6 (.NET).

- `Game.cs`: the loop (10 ticks a second times the speed, fixed timestep), input turned into sim commands, the camera, the HUD.
- `Render/`: the browser's canvas renderer (src/render) ported nearly line for line. `Ctx.cs` is a small Canvas 2D-style context over Godot's CanvasItem drawing calls, so the drawing code reads like the TypeScript; `Camera.cs` holds the projection (top-down now, swap it for isometric later).

## Build and run

```bash
dotnet build godot/LoonieRoo.csproj
```

```bash
"/c/Program Files/Godot_v4.6.1-stable_mono_win64/Godot_v4.6.1-stable_mono_win64_console.exe" --path godot
```

Options go after `--`: `--seed=N --size=N --start=solo|group --ticks=N --zoom=Z`, and `--shot=out.png --frames=N` to save a screenshot after N frames and quit.

## Keys (so far)

WASD/arrows walk (Shift runs) under direct control, else pan · the mouse aims · left click swings (or selects) · right click uses (or moves the selected survivor), right/middle drag pans · wheel zooms · E use · R work · C sneak · T talk · V/H shout follow/hold · L shout loot · 1-9 the bag · F take/hand back control · Space pause · +/- speed · ` debug · Home recentre.
