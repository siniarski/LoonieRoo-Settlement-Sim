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
- **save compat** (SaveCompat.cs): DUMP=dir writes saves, bug reports and states of a few played worlds; CHECK=dir
  loads another build's and compares (a file of this schema must also be written again byte for byte).

**PROBE=path** (both players, Probe.cs): a read-only observer that writes path.txt (a summary line per run: deaths by
cause from every event, prey caught and meat, drinks by source, how thirst met the other needs; each death with the
dying one's last notes, and for thirst where the water was) and path.events-s<seed>.txt (every event; the state keeps
only the last 50). It changes nothing: runs with and without it are the same.

Run several seeds as separate background processes (one SEEDS value each) to stay inside a few minutes. For a CPU
profile, run under dotnet-trace (not installed: ask first).

**Comparing two runs:** LOG= and LOG_AT= dump whole states; with determinism, two runs that should agree can be
bisected tick by tick to the first difference (how the port's bugs were found).
