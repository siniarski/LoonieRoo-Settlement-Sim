# CLAUDE.md

## Project

A 2D grid-based settlement survival sim (Prison Architect / RimWorld / Project Zomboid flavour). The player claims buildings, designates rooms (housing, mess hall, barracks, armory), and manages survivors who pick up jobs by priority. Zombies are a late-stage threat layer.

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
10. **Movement is tile to tile.** An entity in motion stores `from`, `to`, and `progress`/`duration` in ticks. The renderer interpolates from these. Pixel or sub-tile positions never enter the sim.

## Replays and the command log

Rule 1 gives us replays for free: **seed + initial state + `{tick, command}[]` = the full game.** Every command applied through `commands.ts` is appended to a command log. A bug report is then just a file: load the starting state, replay the log, and the bug reproduces. A scenario file is a starting state plus an optional command log.

## Suggested layout

```
src/
  sim/          # pure game logic (no browser APIs)
    state.ts        # state types and initial state
    tick.ts         # the tick function
    rng.ts          # seeded RNG
    grid.ts         # tiles, walls, doors
    rooms.ts        # flood fill, room purposes, requirements
    pathfinding.ts  # A* on the grid
    jobs.ts         # designations -> job queue, reservation, assignment
    survivors.ts    # needs, priorities, behaviour
    commands.ts     # the only way to change the sim from outside
  render/       # canvas view, camera, overlays (reads state only)
  ui/           # panels, tool palette, priority editor
  debug/        # window.game hooks, overlay, text renderer
  scenarios/    # JSON scenario files for tests and repro
tests/
NOTES.md
```

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

- Survivors can always walk through doors. Zombies can't walk through them: to a moving zombie a door is a wall. But a zombie that can't reach anyone batters doors (and walls) down; see breach.ts. Pathfinding takes a `mover` (`'survivor' | 'zombie'`) for this. Locking and other faction rules come later.
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
4. **Debug overlay** (toggle with a key): job queue, each survivor's current job and why it was chosen, reserved tiles, room boundaries, path previews. "Why is she just standing there?" should be answerable in the overlay.
5. **Console errors** from the browser are forwarded to the terminal when running Playwright.

## Testing

- Write tests for sim logic before or alongside the code: pathfinding, flood fill, job reservation, priority selection, invalid-job recovery.
- Add a **stuck-state invariant test**: after running a scenario for N ticks, no survivor is idle with available jobs they could do, and none has been stuck on the same job for too long. Starting thresholds (tune as needed, record changes in NOTES.md):
  - no survivor stays on the same job for more than **600 ticks** (60s at 1x)
  - no survivor stays idle for more than **20 ticks** while a reachable, unreserved job exists of a type they have enabled
- Run `npm run check` (typecheck + tests) before every commit. A red suite means stop and fix.
- Prefer headless sim runs over screenshots to verify behaviour. Use screenshots only for rendering questions.

## Workflow

- **Start tiny.** Get the smallest thing working, play with it, then extend.
- **Commit at each working milestone** with a clear message so experiments can be rolled back.
- **Keep `NOTES.md`** as a decisions log. After any non-trivial experiment, record: what was tried, what happened, what was decided. Read it at the start of each session.
- **Rewrites are fine.** If a system is fighting you, say so and propose a rewrite rather than piling on workarounds.
- Ask before adding dependencies. Prefer fewer. The stack above (TypeScript, Vite, Vitest, Playwright) is pre-approved.
- Keep functions small and files focused. Prefer plain data and functions over deep class hierarchies in `src/sim/`.

## Milestones

1. Grid, walls, doors, text renderer, tests
2. Canvas view and placement tools, hot reload working
3. One survivor with A* to a clicked tile
4. Job system: build, haul, clean, with reservation and the priority panel
5. Rooms: flood fill, designation, requirements, status indicators
6. Needs: hunger and sleep, beds and food as items
7. Threat: simple night zombie pressure, guards respond (keep crude)
8. Auto-planner: "furnish this room for me"

**Stop after milestone 3 and play with it before continuing.** Milestones 1-5 are the core proof of concept. Zombies come last, because the base-building loop has to be fun on its own.

## Known pitfalls

- Hauling multiplies job types and edge cases. Start with a simplified version (one item at a time, no stacks).
- Survivors blocked by newly placed walls or doors. Re-validate jobs and paths when walkability changes.
- Recomputing everything every tick. Recompute rooms and paths only on change.
- Letting render concerns leak into the sim (sprite sizes, pixel positions, animation state). Don't.
