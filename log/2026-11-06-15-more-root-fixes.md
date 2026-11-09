- **2026-11-06, more root fixes (branch root-fixes).** Player: "Fix more core/root issues". Found by profiling total and self time over 400 busy ticks of city-1024, and by profiling only its slowest ticks.
  - **Furniture scans by kind.** Every per-tick or per-decision scan of all furniture for one kind now goes through the by-kind lists, in list order:
    - generators and wells for alerts, lures, turrets, coops, mess-hall tables;
    - crates for loot orders, heaters, fridges, room requirement counts, water sources (taps only while the water's on);
    - beds: finding a free bed, owners on death, outposts, bed assignment and the way home. Only beds have owners.
  - **Zombie reach.** Survivors are bucketed by the tile under them once a tick (survivorsNear), and each zombie looks only at the 5x5 tiles round it (a bite reaches 1.1 tiles). Same list, same order; if the survivor list changes mid-loop it falls back to the whole list.
  - **The zombie list** is filtered only when a herd zombie is actually leaving. A new list every tick threw away the id-lookup cache every tick.
  - **Panic.** It counted every zombie per survivor. zombiesWithin buckets the dead by tile once a tick (and again after any are added or removed, tracked by addZombie and removeZombie), searches two tiles further than asked, and checks each where it stands now, so a zombie knocked back mid-tick is still counted exactly.
  - **nearestTile** allocated a byte per map tile every call (a megabyte on 1024), and it's called constantly. It now reuses stamped scratch arrays, one set per nesting depth in case a callback searches too, with an integer queue. Same search order.
  - **visibleNow** (for drawing) reuses its mask and clears only what it set last time, instead of a new megabyte a tick.
  - **findById**, on a miss, first indexes what was appended since, and rebuilds only when indices moved. A randomized test checks it against list.find.
  - **Warm-up.** The first time raiders or crawlers turned up, their reachability was built from scratch mid-play (20-30 ms on 1024). warmReach builds every mover's reachability up front; the app calls it on a new or loaded game while paused (about 150 ms on 1024). The stress harness does the same and reports the warm-up on its own.
  - Outcomes unchanged throughout (solo dies on the same ticks; colony the same days, people and food).
  - city-1024 busy average 6.4 → 4.4 ms. No tick over 18 ms in 320 (there were five).

| Scenario | mean (ms) | p99 | max (before → after) |
|---|---|---|---|
| colony-512 | 2.5 → 1.9 | 9.6 → 4.0 | 11.8 → 5.2 |
| horde-512 | 2.4 → 2.3 | 4.7 → 3.7 | 5.0 → 4.5 |
| spread-1024 | 1.8 → 1.6 | 7.5 → 5.3 | 8.8 → 6.3 |
| city-1024 | 7.0 → 3.8 | 23.6 → 9.7 | 32.6 → 12.1 |

  - 760 tests.
