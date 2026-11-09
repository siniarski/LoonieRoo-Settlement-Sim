- **2026-11-06, optimisation pass (branch player-map, after the map).** Player: "do an optimisation pass and make any improvements to reduce memory and performance leaks, then do benchmark runs".
  - A real leak:
    - Every accepted command (each key change, each turn of the aim) went into state.commandLog, which was never trimmed. It stayed in memory all session and was written into every save, so autosaves grew all game (about 50 bytes a command; a player moving and aiming makes thousands a day).
    - Saves are now snapshots without the log (savefile.ts). At each dawn autosave the app starts a fresh log from that state (app.ts), so a bug report replays from the last dawn.
  - Otherwise no leaks: twelve game days on a 256 world with a player steering and aiming kept the heap flat at about 38 MB after collection. All the module caches are WeakMaps keyed on the state, grid or list, or bounded (the plan cache keeps six).
  - Reachability (pathfinding.ts), the worst tick:
    - The labels for movers that need doors open were re-flooded over the whole map every time a door swung, and doors swing all the time without bumping the walkability version.
    - Now the map is flooded once per version with every door shut, and the doors open now join the pieces with a small union-find. A door swinging re-joins instead of re-flooding.
    - The flood itself works out passability once per tile into an array, walks neighbours by index, and only looks at edge rules where there's an edge.
    - tests/reach.test.ts checks 960 random pairs, over four movers and six random door states on a generated town, against a plain flood.
  - Furniture by kind (furniture.ts ofKind, ofKinds): the index now also lists each kind in list order, which is id order (furniture is only appended).
    - About 20 scans of every piece of furniture each tick became lookups: the generator, lamps, heaters, towers, radios, beds, the truck, tables. A 256 world has 5,000 pieces, mostly trees and fittings.
    - Also: the generator's load check, the fuel, station and lab job loops, and a bed by id.
    - The bag panel found the container beside you by scanning all furniture every frame; now it looks at the nine tiles round you.
  - Outcomes unchanged: the solo and colony harnesses give exactly the same results as before (the same death ticks; colony days 20, 25 with 4 alive and the same food, 21).
  - Bench, before → after (two runs each, mean ms a tick):

| Scene | Before | After |
|---|---|---|
| 128, 3 colonists | 0.08 | 0.035 |
| 128, 20 colonists | 0.125 | 0.085 |
| 128, 100 colonists | 0.21 | 0.17 |
| 128, 43 zombies | 0.25 | 0.175 |
| 128, busy (26 colonists, 93 zombies) | 0.755 | 0.66 |
| 256, quiet | 0.08-0.10 | 0.026 |
| 256, busy | 0.76-0.79 | 0.61-0.65 |
| 512, quiet | 0.07 | 0.03 |

    - On 256, busy: p99 5.3-5.6 → 4.0, worst 13 → 10.6 ms.
    - Ten days on 128 took 4.4 s, now 3.1 s.
    - A busy 256 colony under the CPU profiler: 0.72 → 0.58 ms. What's left at the top is A* itself (findPathToAny), reconcileJobs' per-tick sweeps, and zombies pushing apart.
  - 738 tests.
