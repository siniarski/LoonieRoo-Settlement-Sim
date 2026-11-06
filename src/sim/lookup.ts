// Finding things by id in the state's lists, without scanning the list each time.
//
// Each list gets a derived map from id to index, kept outside sim state (a WeakMap keyed on the
// array itself) and never trusted blindly: a hit is checked (the thing at that index still has
// that id), and a miss or a stale entry rebuilds the map from the list. Ids are unique, so the
// answer is always what a scan (list.find) would give; only the cost changes. A list that churns
// (things added or removed between lookups) costs a rebuild on the next miss, no worse than the
// scan it replaces. (The stress test, 2026-11-06: with 1,800 zombies each guard job's check
// scanned the zombie list, every tick.)

const maps = new WeakMap<readonly { id: string }[], Map<string, number>>();

export function findById<T extends { id: string }>(list: readonly T[], id: string): T | undefined {
  const map = maps.get(list);
  const at = map?.get(id);
  if (at !== undefined) {
    const hit = list[at];
    if (hit && hit.id === id) return hit;
  }
  // A miss, or out of date: rebuild, then look again.
  const fresh = new Map<string, number>();
  for (let i = 0; i < list.length; i++) if (!fresh.has(list[i]!.id)) fresh.set(list[i]!.id, i);
  maps.set(list, fresh);
  const i = fresh.get(id);
  return i === undefined ? undefined : list[i];
}
