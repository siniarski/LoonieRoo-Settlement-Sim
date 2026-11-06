// Finding things by id in the state's lists, without scanning the list each time.
//
// Each list gets a derived map from id to index, kept outside sim state (a WeakMap keyed on the
// array itself) and never trusted blindly: a hit is checked (the thing at that index still has
// that id). On a miss, entries appended since the map was made are added first (lists mostly grow
// at the end); a stale hit, or a miss that's still a miss, rebuilds the map from the list. Ids are
// unique, so the answer is always what a scan (list.find) would give; only the cost changes. (The
// stress test, 2026-11-06: with 1,800 zombies each guard job's check scanned the zombie list,
// every tick; then rebuilding the map for every job added was the next cost.)

interface IdMap {
  ids: Map<string, number>;
  /** How much of the list the map covers. */
  covered: number;
}
const maps = new WeakMap<readonly { id: string }[], IdMap>();

function rebuild<T extends { id: string }>(list: readonly T[]): IdMap {
  const ids = new Map<string, number>();
  for (let i = 0; i < list.length; i++) if (!ids.has(list[i]!.id)) ids.set(list[i]!.id, i);
  const m = { ids, covered: list.length };
  maps.set(list, m);
  return m;
}

export function findById<T extends { id: string }>(list: readonly T[], id: string): T | undefined {
  let m = maps.get(list) ?? rebuild(list);
  let at = m.ids.get(id);
  if (at !== undefined) {
    const hit = list[at];
    if (hit && hit.id === id) return hit;
    // Out of date (things removed, so indices moved): start again.
    m = rebuild(list);
    at = m.ids.get(id);
    return at === undefined ? undefined : list[at];
  }
  // Not in the map: maybe added at the end since.
  if (list.length > m.covered) {
    for (let i = m.covered; i < list.length; i++) if (!m.ids.has(list[i]!.id)) m.ids.set(list[i]!.id, i);
    m.covered = list.length;
    at = m.ids.get(id);
    if (at !== undefined) {
      const hit = list[at];
      if (hit && hit.id === id) return hit;
    }
  }
  // Still not found (or found out of date): the map may be stale after removals, so rebuild once.
  m = rebuild(list);
  at = m.ids.get(id);
  return at === undefined ? undefined : list[at];
}
