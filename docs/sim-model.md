# The sim's core model

How the colony side of the sim works: designations, jobs, rooms, paths and doors. Moved from CLAUDE.md on 2026-11-10;
the architecture rules stay in CLAUDE.md. Read this before touching jobs, rooms, pathfinding or doors. The entries
behind each choice are in docs/decisions.md ("Jobs, building and hauling", "Rooms and furniture", "Movement and
pathfinding").

## Designations -> jobs -> workers

- The player (or the auto-planner) creates *designations*: build wall, place bed, designate room, clean tile,
  scavenge building.
- Designations become *jobs* in a shared queue (Jobs.reconcileJobs, derived from the world each tick), each with a
  type (build, haul, clean, scavenge, guard, ...).
- Each survivor has a priority per job type (1-4, or off). When idle, a survivor picks the highest-priority job type
  they can do, then the nearest available job of that type.
- Jobs are **reserved** when claimed so two survivors never take the same one.
- Jobs can become **invalid mid-execution** (target walled off, item removed, room changed). Always handle this by
  releasing the reservation and returning the survivor to idle. Never leave a survivor stuck.

## Rooms

- Rooms are found by flood fill over walls and doors, and recomputed only when walls or doors change.
- A room has a player-designated purpose with requirements (e.g. housing needs N beds). Unmet requirements produce a
  visible "incomplete" state.
- The auto-planner (Planner) is just another source of designations. It goes through the same command path as the
  player.

## Pathfinding

- A* on the grid (Pathfinding.cs). Cache paths and invalidate only when the walkability map changes.
- A single `walkabilityVersion` counter in state, bumped whenever a wall or door changes. Cached paths and rooms record
  the version they were computed at and are stale if it differs.

## Doors

- Survivors can always walk through doors (and barricades). Zombies can't walk through them: to a moving zombie a door
  is a wall. But a zombie that can't reach anyone batters doors (and walls) down (Breach). Pathfinding takes a `mover`
  (`survivor`, `zombie`, `breacher`, `raider`, `crawler`, `free`) for this. Raiders open doors unless they're locked.
- For flood fill, doors are room boundaries: they split rooms the way walls do, but survivors can path through them.

## Walls and doors on tile edges

- Walls and doors also stand on the border between two tiles (`grid.edges`: each tile owns its north and west side).
  Generated buildings and the Wall and Door tools use edges; tile walls remain (barricades, the planner, old maps).
- Crossing between neighbours goes through `crossable` (Pathfinding.cs) with the same door rules; reach attacks go
  through `openBetween` (nobody bites or hits through an edge wall); movers' circles collide with edge lines (Motion).
- Reachability labels are keyed on walkabilityVersion and, for movers that need doors open, on which doors are open
  (doors held open for someone passing don't bump the version).

## Replays and the command log

Rule 1 gives replays for free: **seed + initial state + `{tick, command}[]` = the full game.** Every command applied
through `Commands.applyCommand` is appended to `state.commandLog` (only commands that changed state). A bug report
(the menu's "Report a bug", `Savefile.toBugReport`) is a file with the starting state and the log: load it (`--load=`
or the menu's file load) and the game replays to where it was. A scenario file is a starting state plus an optional
command log.

## Known pitfalls

- Hauling multiplies job types and edge cases. Start with a simplified version (one item at a time, no stacks).
- Survivors blocked by newly placed walls or doors: re-validate jobs and paths when walkability changes.
- Recomputing everything every tick: recompute rooms and paths only on change.
