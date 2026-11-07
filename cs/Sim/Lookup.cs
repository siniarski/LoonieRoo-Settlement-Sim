// Finding things by id in the state's lists without scanning (lookup.ts). Each list gets a derived map from
// id to index, kept outside sim state and checked on every hit; a miss first indexes what was appended
// since, and a stale hit or a real miss rebuilds. The answer is always what a scan would give.
using System.Runtime.CompilerServices;

namespace LoonieRoo.Sim;

public interface IHasId
{
    string id { get; set; }
}

public static class Lookup
{
    sealed class IdMap
    {
        public Dictionary<string, int> ids = new();
        /// <summary>How much of the list the map covers.</summary>
        public int covered;
    }

    static readonly ConditionalWeakTable<object, IdMap> maps = new();

    static IdMap Rebuild<T>(List<T> list) where T : IHasId
    {
        var ids = new Dictionary<string, int>();
        for (int i = 0; i < list.Count; i++) ids.TryAdd(list[i].id, i);
        var m = new IdMap { ids = ids, covered = list.Count };
        maps.AddOrUpdate(list, m);
        return m;
    }

    public static T? findById<T>(List<T> list, string id) where T : class, IHasId
    {
        var m = maps.TryGetValue(list, out var found) ? found : Rebuild(list);
        if (m.ids.TryGetValue(id, out int at))
        {
            if (at < list.Count && list[at].id == id) return list[at];
            m = Rebuild(list);
            return m.ids.TryGetValue(id, out at) ? list[at] : null;
        }
        if (list.Count > m.covered)
        {
            for (int i = m.covered; i < list.Count; i++) m.ids.TryAdd(list[i].id, i);
            m.covered = list.Count;
            if (m.ids.TryGetValue(id, out at) && at < list.Count && list[at].id == id) return list[at];
        }
        m = Rebuild(list);
        return m.ids.TryGetValue(id, out at) ? list[at] : null;
    }
}
