# The harness

`cs/Harness`: xUnit, not in the solution or the check; reports go to `cs/Harness/out/`. Balance questions come here
(scripted players over several seeds); the results go in a session-log entry (`log/`).

Run one:

```bash
dotnet test cs/Harness -c Release --filter "DisplayName~solo balance"
```

The others: `DisplayName~world balance` (the colony player), `DisplayName=bench`, `DisplayName=stress`. Set the env
vars below for each.

- **world balance** (ColonyHarness.cs): a scripted colony player on generated worlds. SEEDS, DAYS, BUDGET_MS,
  LOG=path to dump each day's state. It walls in, farms, hunts, scavenges, builds coops from day 6, takes in wanderers
  and answers radio requests.
- **solo balance** (SoloHarness.cs): a scripted solo player under direct control. PRESET (a solo start); TRACE=1,
  TRACE_EVERY; NO_BARRICADE=1; LOG=path and LOG_AT=tick to dump states. It fights by real reach, sneaks, forages,
  hunts deer and rabbits when hungry, drinks from taps, barricades the shelter's door, and drives to far crates.
- **bench** (Bench.cs): tick cost as colonists and zombies are added (after a warm-up round for the JIT); save size
  and memory over 10 days.
- **stress** (Bench.cs): very large worlds (fully generated 512 and 1024; up to 200 colonists, 2,000 zombies, herds
  and caravans): build time, tick mean/p99/max, managed heap, save size and time (ONLY=<scenario>, TICKS).
- **big world** (BigWorld.cs): one new world made, ticked, walked a few chunks, saved and loaded, with the time and
  the managed heap after each step (SIZES=2048,10000, TICKS=200; BOUND_MB=n fails if the heap after createWorld at
  the last size is over n: the grid in chunks is held to 64 MB at 4096). **chunk cost** (same file, SIZE=1024): what a
  generated chunk costs in memory by part (the grid's layers, furniture, the dormant, what play derives).
- **profile** (Profile.cs): the tick by system, with allocations (SCENES, TICKS, WARM). The big new worlds
  `new-2048`, `new-4096` and `new-10000` run only when named in SCENES. **tick dumps** (same file, DUMP=dir): states
  of fixed scenes as JSON, to compare two builds byte for byte.
- **micro** (Micro.cs; SCENE=colony-512, ROUNDS=7): the grid's readers one at a time on one fixed state (tile and
  edge reads, room labels, sight, paths, reachability, and each after a wall change), the best of several rounds in
  nanoseconds an operation. For comparing two builds when whole ticks are too noisy to call a few percent: build it
  against each and read the two reports side by side.
- **save compat** (SaveCompat.cs; written for the move from schema 77 to 78 and only meaningful for it): DUMP=dir writes saves, bug reports and states of a few played worlds; CHECK=dir
  loads another build's and compares (a file of this schema must also be written again byte for byte).

- **dormant** (Bench.cs): what waking the dormant dead costs as their number grows (a 1024 world, 3 or 200 survivors).
- **save speed** (SaveSpeed.cs; REPS, SAVES=size:start|all,..., WHOLE_MAX): how long saving and loading take, chunked
  and whole, and where the time goes.
- **gen profile**, **gen profile create**, **gen profile big**, **gen profile saves**, **gen profile roads**, **gen plan
  hashes** (GenProfile.cs; SIZES or SIZE, WORLD_SIZES, SAVES, PLAN=0, STATE=0, ALL=1, WHOLE=0, DIAG=x,y): world
  generation by phase, at size, and the plans' hashes to compare two builds.
- **road widths** (RoadWidths.cs; SEEDS, SIZE=512, CARS=0, MAP=x,y,r): per seed, the plan's towns, streets and lots, a
  street's cross-section (the road mask across it), the country roads' widths, and in the generated world each parked
  car on the road with the clear road beside it; MAP adds the plan round a point as text (roads, sidewalks, lots,
  trees, cars).
- **building loot**, **building sites** (BuildingLoot.cs; SEEDS, SIZE, SEED): what generated buildings hold; where
  they stand (for pictures).
- **census**, **census noise** (Census.cs; SEEDS, PRESET): the dead in a fresh world (how many, how near the start);
  how many sleepers each weapon's noise would wake.
- **jobs probe** (Profile.cs; SCENES, PROBE_REPS): the cost of `reconcileJobs` on one scene.
- **harness >** (HarnessTest.cs): tests of the balance players themselves.

More env vars: **OUT**=path overrides where a report is written (every fact); world balance also reads WILDFIRE and
SNOW_SLOW; profile reads DEEP=zombies (the dead's update, step by step); tick dumps read DUMP_AT=ticks and SEEDS.

**One at a time.** The harness runs one test class at a time (as the sim's tests do: the sim has process-wide
state), but always name the fact you want with `--filter`: a bare `dotnet test cs/Harness` runs everything, for hours.
The harness is not in the solution or the check, so a change to the sim can break its build unseen: `dotnet build
cs/Harness` after changing a sim function it calls.

**PROBE=path** (both players, Probe.cs): a read-only observer that writes path.txt (a summary line per run: deaths by
cause from every event, prey caught and meat, drinks by source, how thirst met the other needs; each death with the
dying one's last notes, and for thirst where the water was) and path.events-s<seed>.txt (every event; the state keeps
only the last 50). It changes nothing: runs with and without it are the same.

Run several seeds as separate background processes (one SEEDS value each) to stay inside a few minutes. For a CPU
profile, run under dotnet-trace (not installed: ask first).

**The grid inventory (2026-11-10)** left the solo player's runs unchanged, seed for seed (`SEEDS=1` and `SEEDS=2`,
before and after the change): seed 1 dies of starvation on day 5 at tick 39306 (searched 107, ate 8, kills 25), seed 2
of thirst on day 4 at tick 30421 (searched 92, ate 10, kills 16), every daily line identical. Its `takeAll` goes through
first-fit, and 12 kg of what it carries always fits the 8 x 6 bag by area.

**Comparing two runs:** LOG= and LOG_AT= dump whole states; with determinism, two runs that should agree can be
bisected tick by tick to the first difference (how the port's bugs were found).
