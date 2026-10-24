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
| Rule enforcement | `tsconfig.sim.json` (no DOM lib) + `tests/architecture.test.ts` (source scan) | Rules 2 and 3 fail the build instead of relying on review |
| Toolchain | Node 24 LTS, TypeScript 7, Vite 8, Vitest 5 | Current versions as of 2026-10-24 |
| Vitest `fsModuleCache` | Disabled | It doesn't refresh `import.meta.glob` when files are added, so the architecture scan missed new sim files. Suite runs in ~200ms, so the cache isn't needed |

## Open questions

- **Scale: "macro" vs colony.** The concept says macro-scale (SimCity), but the systems are colony-scale (individual survivors doing per-tile A*, like RimWorld). Plain A* is fine for 5-30 survivors. Hundreds of survivors on a town-sized map would need hierarchical pathfinding or flow fields. What is the target map size and survivor count?
- Does the tick rate (10/sec) feel right once survivors are walking around? Starting guess: 3-5 ticks per tile. Judge at milestone 3.

## Session log

- **2026-10-24:** Moved from Claude chat to Claude Code. Reviewed CLAUDE.md, added commands, RNG/iteration/movement rules, command-log replays, `walkabilityVersion` invalidation, door semantics, and concrete stuck-state thresholds. Resolved the hauling and room-purpose open questions (see table). Started milestone 1.
- **2026-10-24, milestone 1 done:** grid (floor/wall/door), commands with a command log, seeded RNG, fixed tick, replay, ASCII scenarios that round-trip through the text renderer, `window.game` hooks, a page that shows the ASCII map. 38 tests. Found that Vitest 5's persistent module cache hid newly added sim files from the architecture scan; disabled it (see table). Next: milestone 2 (canvas view and placement tools).
- **2026-10-24, milestone 2 done:** canvas top-down view with culling and DPR scaling, camera (pan, zoom at cursor), wall/door/clear tools via commands, HUD with tool palette, pause and 1x/2x/4x, debug panel, 48x32 sandbox scenario, hot reload that keeps state. Checked in the browser pane: painting, door placement, zoom, pan, pause (0 ticks while paused), 4x (80 ticks in 2s), and an HMR update kept tick/map/log with no doubled listeners. Found `setPointerCapture` throws for synthetic pointer events, which aborted the handler; now wrapped. 59 tests. Next: milestone 3 (one survivor, A* to a clicked tile) and then **stop and play**.
