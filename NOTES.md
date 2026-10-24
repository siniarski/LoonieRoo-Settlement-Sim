# NOTES.md

Decisions log. Read this at the start of each session. After any non-trivial experiment, add: what was tried, what happened, what was decided.

## Project context (from initial planning)

- **Goal:** a learning and exploration project, not a shipping product. If it proves fun, port to another engine later for better platform support. Optimise for iteration speed and a good developer feedback loop.
- **Developer background:** a decade of software engineering, very little game dev (one Unity game, one simple Godot POC).
- **Game concept:** a macro-scale, 2D grid settlement survival sim. Inspirations: SimCity (emergent cities), Project Zomboid (emergent disasters), State of Decay (base and survivor management), Prison Architect and RimWorld (designation and job systems).
- **POC focus:** find or claim houses, designate rooms and their purposes (housing, mess hall, barracks, weapon storage), and either let the settlement self-organise (auto-planner) or micro-manage placement and cleanup. Each survivor has per-job-type priorities (scavenging, cleaning, zombie defence, etc.).
- **Core tension to preserve:** bigger settlement produces more, but a bigger perimeter is harder to defend. Tight fortress vs. risky expansion.

## Decisions so far

| Decision | Choice | Reasoning |
|---|---|---|
| Platform | Web | Fastest feedback loop, easy for an agent to run and inspect |
| Language / tooling | TypeScript, Vite, Vitest | Agents are strong in TS; HMR for instant feedback; headless sim tests |
| Renderer | Canvas 2D, top-down | Readability matters more than prettiness when debugging job queues. PixiJS is a possible later swap |
| Isometric / 3D | Deferred | Projection is purely a renderer concern; sim stays in grid coordinates |
| Engine (Godot etc.) | Deferred | Port later if the game earns it; the sim test suite becomes the port spec |
| Sim model | Pure, deterministic, fixed tick (default 10/sec) | Enables headless runs, replayable bugs, trivial speed controls |
| Automation model | Auto mode is a planner that emits the same designations the player does | Build manual first; auto is a layer on top, not a separate system |
| Zombies | Late milestone, crude at first | The base-building loop must be fun on its own |
| Replays | Seed + initial state + `{tick, command}[]` log | Falls out of determinism; bug reports become files |
| RNG | mulberry32, state is a single uint32 in sim state | Tiny, fast, trivially serialisable |
| Path/room invalidation | One `walkabilityVersion` counter | Simplest thing that works; refine if too coarse |
| Doors | Always walkable; act as room boundaries for flood fill | Locking/factions deferred |
| Hauling v1 | One item, one carrier, no stacks, no stockpile filters | Hauling multiplies edge cases |
| First room purposes | Housing (N beds), storage (designated area only) | Enough to exercise the requirements loop; mess hall waits for needs |
| Stuck-state thresholds | Max 600 ticks on one job; max 20 idle ticks with a doable job | Starting values, tune later |
| State mutation | Sim functions mutate the state they are given; `cloneState` (JSON round trip) when a copy is needed | Simpler than immutable updates; determinism doesn't need immutability. Replays clone the initial state first |
| Command log contents | Only commands that changed state are logged (not rejected ones, not no-ops) | Drag-painting over existing walls would otherwise flood the log; skipped entries change nothing on replay |
| View state | Camera, tool, hover, speed, pause live in `ui/view.ts`, never in sim state | Saves and replays stay free of UI concerns |
| Hot reload | `main.ts` self-accepts; on dispose it stashes sim + view state in `import.meta.hot.data` and tears down the app (RAF, listeners, HUD) | Editing any module (sim, render, ui) swaps code without losing the map you're building |
| Controls | Left-drag paints (Bresenham fills gaps), right/middle-drag pans, wheel zooms at cursor, WASD/arrows pan, 1/2/3 tools, Space pause, `` ` `` debug panel, Home recentre | Prison Architect-ish defaults; revisit after playing |
| Agent hooks | `window.view.tileToScreen(x, y)` and `getView()` alongside `window.game` | Lets Playwright/agents click a specific tile without camera maths |
| Movement | 4-way, `TICKS_PER_TILE = 4` (0.4s/tile at 1x). Next step starts on the same tick the previous one ends | No per-tile stutter. Confirmed by playtest: 0.4s/tile feels right, no diagonals for now |
| A* tie-breaking | Lower f, then lower h, then insertion order; neighbours N, E, S, W | Deterministic paths, needed for replays |
| Path cache | Per-survivor `path` + `pathVersion`; checked at every tile boundary, full repath on any walkability change | Simplest correct scheme. Fine for a handful of survivors; revisit with many survivors or big maps |
| Unreachable goals | Survivor drops the goal, goes idle, `note` says why | Never stuck; the debug panel answers "why is she standing there" |
| Walls on survivors | `placeWall` is rejected on a tile a survivor is on or stepping onto | Survivors can never end up inside a wall. Doors are still allowed |
| Schema version | `SimState.schemaVersion`; hot reload discards state with a different version | Milestone 2's hot state crashed milestone 3 code (`survivors` missing). Bump it whenever SimState's shape changes |
| Frame loop | Next frame is scheduled before the work; errors are caught and logged once each | One bad frame (often a half-finished edit) used to kill the loop permanently |
| Rule enforcement | `tsconfig.sim.json` (no DOM lib) + `tests/architecture.test.ts` (source scan) | Rules 2 and 3 fail the build instead of relying on review |
| Toolchain | Node 24 LTS, TypeScript 7, Vite 8, Vitest 5 | Current versions as of 2026-10-24 |
| Vitest `fsModuleCache` | Disabled | It doesn't refresh `import.meta.glob` when files are added, so the architecture scan missed new sim files. Suite runs in ~200ms, so the cache isn't needed |

## Open questions

- **Scale: "macro" vs colony.** The concept says macro-scale (SimCity), but the systems are colony-scale (individual survivors doing per-tile A*, like RimWorld). Plain A* is fine for 5-30 survivors. Hundreds of survivors on a town-sized map would need hierarchical pathfinding or flow fields. What is the target map size and survivor count?

## Dev gotchas

- **Vite's watcher can miss rapid successive edits on Windows.** Symptom: the browser keeps running old code (a `?t=` URL serves stale content) even after a page reload. Fix: re-save the file, or restart the dev server.
- **Don't fetch `/src/...` modules with made-up query strings while debugging.** Vite adds them to its module graph as separate modules with no importers, which makes hot reload fall back to full page reloads for everything they import. Restart the dev server to clear it.
- **Edits across several files are hot-reloaded one file at a time.** In-between states can throw (missing imports and so on); the frame loop now survives that. If hot reload fails outright ("Failed to reload main.ts"), refresh the page.

## Session log

- **2026-10-24:** Moved from Claude chat to Claude Code. Reviewed CLAUDE.md, added commands, RNG/iteration/movement rules, command-log replays, `walkabilityVersion` invalidation, door semantics, and concrete stuck-state thresholds. Resolved the hauling and room-purpose open questions (see table). Started milestone 1.
- **2026-10-24, milestone 1 done:** grid (floor/wall/door), commands with a command log, seeded RNG, fixed tick, replay, ASCII scenarios that round-trip through the text renderer, `window.game` hooks, a page that shows the ASCII map. 38 tests. Found that Vitest 5's persistent module cache hid newly added sim files from the architecture scan; disabled it (see table). Next: milestone 2 (canvas view and placement tools).
- **2026-10-24, milestone 2 done:** canvas top-down view with culling and DPR scaling, camera (pan, zoom at cursor), wall/door/clear tools via commands, HUD with tool palette, pause and 1x/2x/4x, debug panel, 48x32 sandbox scenario, hot reload that keeps state. Checked in the browser pane: painting, door placement, zoom, pan, pause (0 ticks while paused), 4x (80 ticks in 2s), and an HMR update kept tick/map/log with no doubled listeners. Found `setPointerCapture` throws for synthetic pointer events, which aborted the handler; now wrapped. 59 tests. Next: milestone 3 (one survivor, A* to a clicked tile) and then **stop and play**.
- **2026-10-24, milestone 3 done:** A* pathfinding, survivors with tile-to-tile movement and cached paths, repath on wall changes, give-up-with-reason on unreachable goals, `spawnSurvivor`/`moveSurvivor` commands, `s` in scenario maps, Move tool (4) and click-to-select, interpolated rendering, path previews and per-survivor status in the debug panel. A fuzz test runs random orders and wall edits with the stuck-state invariant plus full replay equality; breaking path invalidation fails 7 tests. Checked in the browser pane: Ash walks through doors to a clicked tile, and repaths live from the west door to the top door when walls change mid-walk. Found and fixed: frame loop dying on one exception, hot state of an old shape crashing new code (schema version), debug panel overlapping the toolbar. 87 tests. **Now: stop and play (per CLAUDE.md) before milestone 4.** Questions to answer by playing: does 0.4s/tile at 1x feel right? Is 4-way movement OK or do we want diagonals?
- **2026-10-24, milestone 3 playtest:** works well. 10 ticks/sec with 4 ticks per tile (0.4s/tile) feels right; staying 4-way, no diagonals for now. Tick-rate question closed. Next: milestone 4 (job system: build, haul, clean, reservation, priority panel).
