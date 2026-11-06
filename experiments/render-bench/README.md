# Rendering benchmark: the browser vs Godot, with the stress test moving (2026-11-07)

The city-1024 stress scenario (harness/stressScenarios.ts: a fully generated 1024 world, about 180 colonists
and 1,500 zombies, herds and caravans), run 100 ticks so everything's moving, then drawn in both:

- **Browser**: the game itself, in a real Chrome window (Playwright with the installed Chrome) at 1280x720, playing
  the saved state at 1x. Each frame's costs come from the app (window.view.frameSamples): drawing the world (the
  canvas renderer, top-down), the HUD, the sim's ticks, and the time between frames.
- **Godot 4.6** (GL compatibility renderer, vsync off) at 1280x720: the same map isometric (TileMapLayers, as
  experiments/godot-bench/main.gd), and every mover replayed from the recording (600 ticks, up to 1,543 movers)
  as a Sprite2D, blended between ticks, moved by GDScript each frame. No sim runs in Godot: it's drawing only.

Views: "play" is the browser's zoom 1 (24 px tiles, about 53x30 tiles on screen) and Godot zoom 0.75, which shows
about as many tiles; "zoomed out" is the browser's furthest (0.25: about 213x120 tiles) and Godot 0.1875.

| Per frame (ms; mean, p99) | Browser, play | Browser, zoomed out | Godot, play | Godot, zoomed out |
|---|---|---|---|---|
| Drawing the world and the movers | 2.3, 7.5 (canvas, on the CPU) | 5.8, 11.1 | 2.0, 2.8 (moving sprites 0.7 + render on the CPU 1.3; on the GPU 0.15) | 3.8, 4.6 (0.8 + 3.0; GPU 0.4) |
| HUD (panels, lists) | 3.9, 8.5 | 4.1, 8.4 | (none) | (none) |
| Sim (ticks that frame) | 0.6, 5.0 | 0.7, 4.7 | (none: replayed) | (none) |
| Whole frame | 17.6 (56.7 fps: the display's refresh) | 17.6 (56.7 fps) | 2.2 (456 fps, uncapped) | 4.0 (250 fps) |

Caveats, both ways:
- The browser draws only explored ground (most of this world is black) and culls movers off screen; Godot draws the
  whole map and moves all 1,543 sprites every frame, on screen or not.
- The browser draws more per mover (health bars, task marks, selection rings, wall edges); Godot draws a dot.
- Canvas time is the CPU side only; the browser's own compositing and GPU time aren't in "draw".
- Godot's frame is everything it does; the browser frame is capped by vsync, and its real work (draw + HUD + sim,
  about 7 ms at play zoom, 11 ms zoomed out) leaves room for well over 60 fps.

Read: both draw this comfortably inside a 60 fps frame. Godot's isometric view of the same scene costs about the same
as the browser's top-down canvas at play zoom and about two thirds zoomed out, with the graphics card nearly idle,
so it has far more headroom (lighting, 3D, effects). The browser's next costs are the HUD (as much as the drawing:
the panels and lists rebuilt on a busy colony) and the canvas drawing as the view grows.

Run it:
1. `npm run harness -- renderbench` (SCENARIO, TICKS) writes data/: the save, the map for Godot, the movers' track.
2. With `npm run dev` running: `node experiments/render-bench/browser-bench.mjs` -> out/browser-city-1024.json.
3. `godot --path experiments/godot-bench res://stress.tscn -- --data=<abs path to experiments/render-bench/data> --zoom=0.75`
   (and `--zoom=0.1875`; `--shot=<png>` saves a frame).
