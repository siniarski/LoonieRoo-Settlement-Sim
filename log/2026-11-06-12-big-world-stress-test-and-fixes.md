- **2026-11-06, big-world stress test and fixes (branch stress).** Player: "Big world performance and stress test first".
  - New harness/stress.harness.ts with six scenarios:
    - gen-512 and gen-1024: whole worlds generated, 3 colonists;
    - colony-512: 100 colonists, 500 zombies round them;
    - horde-512: 30 colonists, 2,000 zombies all over, 6 herds, 20 caravans;
    - spread-1024: 40 colonists in the four corners, 1,000 zombies;
    - city-1024: 200 colonists, 1,500 zombies, 10 herds, 30 caravans.
    It reports build time, tick mean/p99/max, heap, save size, and a CPU profile of each.
  - What it found, and what changed (outcomes unchanged throughout: the solo harness dies on the same ticks, and the colony harness gives the same days, people and food):
    - **A* allocated three map-sized arrays per search** (about 13 MB on 1024, filled every time), up to half of all time plus 14% in garbage collection. It now reuses scratch arrays with a generation stamp (pathfinding.ts scratchFor), so a search costs what it visits.
    - **The job sweep scanned all furniture every tick** for the few pieces marked for scavenging (84% of an empty 1024 world's tick: 88,000 pieces). The furniture index keeps the marked list (markedForScavenge), invalidated through setScavenge, which all six places that change the mark now use.
    - **Lookups by id scanned their lists:** getZombie, getJob, getItem, getSurvivor. A guard job per zombie made checking jobs O(zombies squared). New lookup.ts findById keeps a derived id-to-index map per list, checks every hit, and rebuilds on a miss, so answers are what a scan gives.
    - **Zombies pushing out of people** built a combined list of every survivor, resident, dog and animal per zombie per tick. They're now bucketed by tile once a tick, and kept in their old order. Zombie buckets use numeric keys.
    - **A zombie's who-can-I-bite check** called a closure per survivor; now a loop with a cheap box test first. Closing in finds its target by id.
    - **Saves:** furniture goes out as rows of values in a fixed field order, trailing defaults dropped (savefile.ts packFurniture). Pieces with any other shape stay objects, so saves still round-trip exactly (a new test on a generated world). Old saves load.

| Scenario | Tick mean before → after (ms) | p99 | max | Save before → after |
|---|---|---|---|---|
| gen-512 | 0.21 → 0.07 | 0.9 → 0.7 | 1.4 → 1.1 | 3.5 → 1.1 MB |
| gen-1024 | 0.60 → 0.07 | 2.2 → 1.5 | 2.5 → 2.1 | 14.7 → 4.5 MB |
| colony-512 | 8.8 → 2.7 | 36 → 12 | 76 → 23 | 0.8 → 0.5 MB |
| horde-512 | 8.7 → 2.6 | 25 → 10 | 26 → 18 | 4.5 → 2.1 MB |
| spread-1024 | 12.0 → 3.7 | 54 → 29 | 69 → 33 | 3.4 → 1.5 MB |
| city-1024 | 39.4 → 7.3 | 263 → 32 | 648 → 64 | 15.7 → 5.6 MB |

  - Memory: a fully generated 1024 world is about 105 MB of heap (88,000 furniture objects), and 135 MB with city-1024's load. It's flat over a run.
  - Bench: everything quicker. Busy 128 0.66 → 0.63 ms, busy 256 0.61-0.65 → 0.52 ms, quiet worlds 2-5x quicker (0.016 ms on 256).
  - Left at scale:
    - the full-map reachability flood when walls change or new chunks generate (each mover type floods a million tiles on 1024: the 30-60 ms spikes);
    - A* itself for long paths (hierarchical paths would help);
    - zombie updates in general (22% of city-1024);
    - 1024 saves near the 5 MB browser storage limit (1024 isn't on the menu; 512 is fine at 1.1 MB).
  - 757 tests.
