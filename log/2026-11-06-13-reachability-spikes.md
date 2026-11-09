- **2026-11-06, reachability spikes (branch reach-chunks).** Player: "Yes work on reachability spikes".
  - Reachability is chunked (pathfinding.ts):
    - Each 32x32 chunk is flooded into its own pieces. Pieces touching across a border are joined by a union-find over all pieces, from each chunk's cached border links (borderLinks: deduplicated (piece, piece) pairs to its east and south neighbours).
    - When walls, doors, locks or edges change, only the changed chunks are flooded again, and only their links (and the west and north neighbours' links into them) are redone. Then the cached links are replayed.
    - Door movers still flood with every door shut and join through the open ones.
  - Change log: grid.ts keeps a second log of changes, of chunk indices with positions (reachCursor, reachChangesSince). Each mover type's labels remember how far they've read. It holds up to 100,000 entries; past that, or with no log (the first build, a loaded grid), everything is flooded.
  - Locking or unlocking a door (tile or edge) changed raiders' paths without noting the change. It now notes it (grid.ts noteChange is exported).
  - Proof: tests/reach.test.ts now also builds and breaks walls, edges, doors and locks between questions, plus a boxed-in room each round (some across a chunk border) asked about from inside, for five movers, against a plain whole-map flood (1,872 answers). Breaking the re-flood, or the relinking, on purpose fails it.
  - Profiling only the slow ticks of city-1024 (find them, replay to them, profile just those) found two more:
    - Job choice sorted every open job (one per zombie: 1,500) with a comparator for each idle colonist. Now each distance is worked out once and folded with its queue position into one number, sorted natively. Same stable order.
    - Adding or removing a piece of furniture threw away the whole furniture index (88,000 pieces) to be rebuilt. Now the index is updated in place: the tile goes to the next piece there in list order, and the kind lists and the marked list too. That also made generation itself faster: a whole 1024 world in 1.4 s instead of 3.5 s. tests/furniture-index.test.ts checks 400 random adds, removes and marks against a fresh index.
  - Rooms only recompute in full on the first build (checked); the rest is incremental.

| Scenario | p99 before → after (ms) | max before → after (ms) |
|---|---|---|
| colony-512 | 12.4 → 9.6 | 21.0 → 11.8 |
| horde-512 | 9.7 → 4.7 | 18.3 → 5.0 |
| spread-1024 | 28.3 → 7.5 | 30.1 → 8.8 |
| city-1024 | 30.2 → 23.6 | 59.3 → 32.6 |

  - Means barely moved (2-7 ms); the spikes were the point. Outcomes unchanged: solo dies on the same ticks; colony the same days, people and food.
  - The odd slow tick left in city-1024 (35 ms, once in 320) is herds' marker updates and a few chunk floods at once.
  - 759 tests.
