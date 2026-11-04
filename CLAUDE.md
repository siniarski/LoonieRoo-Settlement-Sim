# CLAUDE.md

## Project

A 2D grid-based survival sim. It began as a RimWorld-style colony sim (claim buildings, designate rooms, survivors take jobs by priority) and is becoming the player's vision: **Project Zomboid-style survival where you control one survivor directly, with an optional colony sim** when you settle, other settlements living their own lives, and survivors with stories and requests.

**Read NOTES.md "Start here" first**: current state, controls, a map of every module, the harness, what's next, and the player's working agreements (push only when asked, every time; commit only on a green check).

This is a **learning and exploration project**. Favour fast iteration, readable code, and throwaway experiments over polish. If the game proves fun, it may later be ported to another engine, so the architecture below exists to keep that port cheap.

## Stack

- TypeScript (strict), Vite, Vitest
- Canvas 2D for rendering (PixiJS is a possible later swap)
- Playwright for driving the running game when needed
- No game framework. No Phaser.
- Node 22 LTS or newer, npm

## Commands

- `npm run dev` starts the Vite dev server
- `npm test` runs Vitest once
- `npm run test:watch` runs Vitest in watch mode
- `npm run typecheck` runs `tsc --noEmit`
- `npm run check` runs typecheck and tests. **Run this before every commit.**
- `npm run harness -- <name>` runs a tool from harness/ (colony, solo, bench, profile): scripted balance players, a benchmark, a profiler. Not part of `npm test`; output in harness/out/.

## Architecture rules (non-negotiable)

1. **The sim is pure and deterministic.** Everything in `src/sim/` is plain data plus functions. Same seed plus same inputs must produce the same state, always.
2. **No rendering, DOM, or browser imports in `src/sim/`.** The sim must run in Node and Vitest with no environment shims.
3. **No `Math.random()` or `Date.now()` in the sim.** Use the seeded RNG held in sim state. Time is measured in ticks.
4. **All sim coordinates are grid coordinates** (integer tile x, y). Projection to pixels (top-down now, maybe isometric later) is purely the renderer's job.
5. **The renderer only reads state.** Input handlers translate clicks into sim *commands* (`placeWall`, `designateRoom`, `setPriority`) which are applied through the sim, never by mutating state directly.
6. **Fixed timestep.** The sim advances in discrete ticks (default 10/sec). The renderer may interpolate. Speed controls (pause, 1x, 2x, 4x) change how many ticks run per frame, not tick length.
7. **State must be serialisable** (plain JSON-compatible objects, no class instances with hidden state, no functions). This enables save/load, scenario files, and debugging dumps.
8. **The RNG state lives in sim state as a plain number.** `rng.ts` exposes pure functions that take state and return a value plus the next state (or advance `state.rng` in place). Saving and loading restores the RNG exactly.
9. **Iteration order must be deterministic.** Store collections as arrays or as records with string keys. Never let the result depend on `Set`/`Map` insertion order across save/load, or on unsorted object keys with mixed numeric/string names.
10. **The world is on the grid; movers have precise positions** (motion.ts). Every mover has `at` (its centre in fixed-point SUB = 256 units per tile: integers, never floats) and `was` (at the tick's start, for the renderer to blend), plus `pos`, the tile under it, which most of the sim uses. Free movers (the survivor you control, and zombies) own `at` and move continuously, colliding with walls as circles (`slide`); other movers walk tile paths (`move`: from, to, progress) and `at` follows their step. Reach and arcs are distances between `at`s. Use `Math.sqrt`, never `Math.hypot` (exact everywhere), and integer maths where it decides outcomes.
11. **Chances that mustn't shift the RNG stream use a stateless hash** (`hash.ts` hashRoll of ids and ticks), so adding a new chance doesn't change every other random outcome. The sim RNG is for the core rolls that were always there.
12. **Never compute a module-level constant from another module's constant** (`export const X = OTHER * 2`). Import cycles can read it before it exists (it broke the page once), and day-length-derived values must follow the clock: derive them in a function, or register with `onClock` in time.ts.
13. **Day length is a world setting** (`settings.dayScale`). Durations come from time.ts (`DAY_LENGTH`, `TICKS_PER_HOUR`: live bindings) and recompute through `onClock`; things that happen at a rate per tick (needs, crops, work, timed actions) run only on `slowTick` ticks. tick() sets the scale from state each tick.
14. **Keep every per-tick system's cost bounded.** Budget expensive work per tick (zombies plan at most PLANS_PER_TICK paths), keep far-off things abstract (dormant zombies, herds, settlements), and never flood the whole map per entity per tick. Profile with `npm run harness -- profile` and the debug panel's tick ms.

## Replays and the command log

Rule 1 gives us replays for free: **seed + initial state + `{tick, command}[]` = the full game.** Every command applied through `commands.ts` is appended to a command log. A bug report is then just a file: load the starting state, replay the log, and the bug reproduces. A scenario file is a starting state plus an optional command log.

## Layout

```
src/
  sim/          # pure game logic (no browser APIs): ~75 focused modules, one system each
  render/       # canvas view, camera, palette, interpolation (reads state only)
  ui/           # menu, HUD panels, input, build tools, saves
  debug/        # window.game hooks, ASCII text renderer
tests/          # Vitest: one file per system, plus architecture and invariant tests
harness/        # balance players, benchmark, profiler (npm run harness)
NOTES.md        # Start here, decisions, design direction, open questions, session log
```

NOTES.md "Start here" maps every sim module to what it does. Each module opens with a comment stating its rules.

## Core design model

**Designations -> jobs -> workers**

- Player (or later, an auto-planner) creates *designations*: build wall, place bed, designate room, clean tile, scavenge building.
- Designations become *jobs* in a shared queue, each with a type (build, haul, clean, scavenge, guard).
- Each survivor has a priority per job type (1-4, or off). When idle, a survivor picks the highest-priority job type they can do, then the nearest available job of that type.
- Jobs are **reserved** when claimed so two survivors never take the same one.
- Jobs can become **invalid mid-execution** (target walled off, item removed, room changed). Always handle this by releasing the reservation and returning the survivor to idle. Never leave a survivor stuck.

**Rooms**

- Rooms are found by flood fill over walls and doors, and recomputed only when walls or doors change.
- A room has a player-designated purpose with requirements (e.g. housing needs N beds). Unmet requirements produce a visible "incomplete" state.
- The auto-planner (later) is just another source of designations. It must go through the same command path as the player.

**Pathfinding**

- A* on the grid. Cache paths and invalidate only when the walkability map changes.
- Use a single `walkabilityVersion` counter in state, bumped whenever a wall or door changes. Cached paths and rooms record the version they were computed at and are stale if it differs. This is the whole invalidation scheme until it proves too coarse.

**Doors (milestone 1 decision, revised in milestone 7)**

- Survivors can always walk through doors (and barricades). Zombies can't walk through them: to a moving zombie a door is a wall. But a zombie that can't reach anyone batters doors (and walls) down; see breach.ts. Pathfinding takes a `mover` (`'survivor' | 'zombie' | 'breacher' | 'raider' | 'crawler'`) for this. Raiders open doors unless they're locked.
- For flood fill, doors are room boundaries: they split rooms the way walls do, but survivors can path through them.

## Debugging and inspection conventions

Build these in from the start. They are how the agent (and you) can see what the game is doing.

1. **`window.game` hooks:**
   - `getState()` returns the full serialisable sim state
   - `step(n)` advances n ticks
   - `load(scenario)` loads a scenario
   - `command(cmd)` applies any sim command
   - `dumpText()` returns the ASCII map
2. **Text renderer** (`src/debug/text.ts`): `#` wall, `D` door, `.` floor, `s` survivor, `b` bed, etc. Use it in test failure output so the map is visible in the terminal.
3. **Scenarios:** JSON files describing a map, seed, survivors, and designations. Tests load a scenario, run N ticks, and assert on the result (e.g. nobody is stuck, the room completed).
4. **Debug overlay** (the backtick key): job queue, each survivor's current job and why it was chosen, reserved tiles, room boundaries, path previews, and frame costs (tick, sim and draw ms). "Why is she just standing there?" should be answerable in the overlay.
5. **Console errors** from the browser are forwarded to the terminal when running Playwright.

## Testing

- Write tests for sim logic before or alongside the code: pathfinding, flood fill, job reservation, priority selection, invalid-job recovery.
- Add a **stuck-state invariant test**: after running a scenario for N ticks, no survivor is idle with available jobs they could do, and none has been stuck on the same job for too long. Starting thresholds (tune as needed, record changes in NOTES.md):
  - no survivor stays on the same job for more than **600 ticks** (60s at 1x)
  - no survivor stays idle for more than **20 ticks** while a reachable, unreserved job exists of a type they have enabled
- Run `npm run check` (typecheck + tests) before every commit. A red suite means stop and fix.
- Prefer headless sim runs over screenshots to verify behaviour. Use screenshots only for rendering questions, but do check anything visible in the browser before calling it done.
- Balance questions go to the harness (scripted players over several seeds, run in parallel), and the results go in the NOTES.md session log.
- The sim's clock scale (time.ts) is module state: a test that changes `settings.dayScale` must reset it (`setDayScale(1)`) afterwards.

## Workflow

- **Start tiny.** Get the smallest thing working, play with it, then extend.
- **Commit at each working milestone** with a clear message so experiments can be rolled back.
- **Keep `NOTES.md`** as a decisions log. After any non-trivial experiment, record: what was tried, what happened, what was decided. Read it at the start of each session.
- **Rewrites are fine.** If a system is fighting you, say so and propose a rewrite rather than piling on workarounds.
- Ask before adding dependencies. Prefer fewer. The stack above (TypeScript, Vite, Vitest, Playwright) is pre-approved.
- Keep functions small and files focused. Prefer plain data and functions over deep class hierarchies in `src/sim/`.

## Milestones

The original eight (grid and text renderer; canvas and tools; a survivor with A*; jobs with reservation and priorities; rooms; needs; night zombies and guards; the auto-planner) are all done, along with a long list of systems since (see NOTES.md). The plan now follows NOTES.md "Design direction" and "Start here": **next is the player's playtest**, then whatever it says about movement and combat feel, inventory, and the visual target.

## Known pitfalls

- Hauling multiplies job types and edge cases. Start with a simplified version (one item at a time, no stacks).
- Survivors blocked by newly placed walls or doors. Re-validate jobs and paths when walkability changes.
- Recomputing everything every tick. Recompute rooms and paths only on change.
- Letting render concerns leak into the sim (sprite sizes, pixel positions, animation state). Don't.
