// Carrying things (part of Work): a haul job (an item to storage, or material to a blueprint), a fuel job (fuel or
// ammunition to what burns it), the walk to storage with a cart loaded on the way, and which storage tile to make for.
// freeItemFor and pickStorageTile are also what JobChoice asks when it weighs a job nobody has taken yet.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Work
{
    /// <summary>Nearest free item of a kind that the survivor can reach and carry on to the site.</summary>
    public static Item? freeItemFor(SimState state, string kind, Point from, List<Point> site)
    {
        var __d0 = state;
        var grid = __d0.grid;
        var v = __d0.walkabilityVersion;
        var free = state.items.filter((i) => ((i.pos != null) && (i.reservedBy == null)) && (i.kind == kind)).sort((a, b) => Space.manhattan(from, a.pos) - Space.manhattan(from, b.pos));
        return free.slice(0, Jobs.MAX_CANDIDATES).find((i) => Pathfinding.canReach(grid, v, from, [i.pos]) && Pathfinding.canReach(grid, v, i.pos, site));
    }

    public static void workHaul(SimState state, Survivor s, Job job)
    {
        if (Jobs.isFuelJob(job))
        {
            { workFuel(state, s, job); return; }
        }
        var item = Js.T(job.itemId) ? Items.getItem(state, job.itemId) : null;
        if (!Js.T(item))
        {
            { Jobs.releaseSurvivor(state, s, "item gone"); return; }
        }
        if (s.carrying != item.id)
        {
            if (!Js.T(item.pos))
            {
                { Jobs.releaseSurvivor(state, s, "item taken by someone else"); return; }
            }
            if (!Space.samePoint(s.pos, item.pos))
            {
                { goTo(state, s, job, [item.pos]); return; }
            }
            item.pos = null;
            item.carriedBy = s.id;
            s.carrying = item.id;
            s.note = $"picked up {Js.Str(item.kind)}";
            if (Js.T(Jobs.isStoreJob(job) && Js.T(s.cart)))
            {
                loadCart(state, s, item);
            }
        }
        if (Jobs.isStoreJob(job))
        {
            { carryToStorage(state, s, job, item); return; }
        }
        var bp = Js.T(job.blueprintId) ? Designations.getBlueprint(state, job.blueprintId) : null;
        if (!Js.T(bp))
        {
            { Jobs.releaseSurvivor(state, s, "designation cancelled"); return; }
        }
        if (Space.manhattan(s.pos, bp) != 1)
        {
            { goTo(state, s, job, Grids.neighbours4(state.grid, bp.x, bp.y)); return; }
        }
        Items.removeItem(state, item);
        s.carrying = null;
        Designations.deliver(bp);
        finishJob(state, s, job, $"delivered {Js.Str(item.kind)} to ({Js.Str(bp.x)}, {Js.Str(bp.y)})");
    }

    /// <summary>Carry a piece of wood to a generator and feed it in.</summary>
    public static void workFuel(SimState state, Survivor s, Job job)
    {
        var g = Js.T(job.furnitureId) ? FurnitureMod.getFurniture(state, job.furnitureId) : null;
        if (!Js.T(g) || !(Js.In(g.kind, Jobs.FUEL)))
        {
            { Jobs.releaseSurvivor(state, s, $"{Js.Str(g?.kind ?? "generator")} gone"); return; }
        }
        var fuel = Js.Get(Jobs.FUEL, g.kind);
        var site = Grids.neighbours4(state.grid, g.x, g.y);
        var item = Js.T(job.itemId) ? Items.getItem(state, job.itemId) : null;
        if (!Js.T(item))
        {
            item = freeItemFor(state, fuel, s.pos, site);
            if (!Js.T(item))
            {
                { Jobs.releaseSurvivor(state, s, $"no {Js.Str(fuel)} for the {Js.Str(g.kind)}"); return; }
            }
            job.itemId = item.id;
            item.reservedBy = s.id;
        }
        if (s.carrying != item.id)
        {
            if (!Js.T(item.pos))
            {
                { Jobs.releaseSurvivor(state, s, "item taken by someone else"); return; }
            }
            if (!Space.samePoint(s.pos, item.pos))
            {
                { goTo(state, s, job, [item.pos]); return; }
            }
            item.pos = null;
            item.carriedBy = s.id;
            s.carrying = item.id;
        }
        if (Space.manhattan(s.pos, g) != 1)
        {
            { goTo(state, s, job, site); return; }
        }
        Items.removeItem(state, item);
        s.carrying = null;
        g.stored += 1;
        g.storedKind = fuel;
        finishJob(state, s, job, $"{Js.Str((fuel == "ammo") ? Js.Str("loaded") : Js.Str("fuelled"))} the {Js.Str(g.kind)} at ({Js.Str(g.x)}, {Js.Str(g.y)})");
    }

    public static void carryToStorage(SimState state, Survivor s, Job job, Item item)
    {
        var home = Rooms.homeFor(Rooms.completeStoreRooms(state), item.kind);
        if (!Js.T(job.dest) || !storageTileFree(state, job, job.dest, home))
        {
            job.dest = pickStorageTile(state, s.pos, job, home);
            if (!Js.T(job.dest))
            {
                { Jobs.releaseSurvivor(state, s, "no free storage tile"); return; }
            }
        }
        if (!Space.samePoint(s.pos, job.dest))
        {
            { goTo(state, s, job, [job.dest]); return; }
        }
        item.pos = Js.CopyAs<Point>(job.dest);
        item.carriedBy = null;
        item.reservedBy = null;
        s.carrying = null;
        var extra = s.cartLoad.length;
        foreach (var id in Js.Iter(s.cartLoad))
        {
            var load = Items.getItem(state, id);
            if (!Js.T(load))
            {
                continue;
            }
            var spot = pickStorageTile(state, s.pos, job, Rooms.homeFor(Rooms.completeStoreRooms(state), load.kind));
            load.carriedBy = null;
            load.reservedBy = null;
            if (Js.T(spot))
            {
                load.pos = spot;
            }
            else
            {
                Items.dropItem(state, load, s.pos);
            }
        }
        s.cartLoad = [];
        finishJob(state, s, job, $"stored {Js.Str(item.kind)}{Js.Str(Js.T(extra) ? Js.Str($" and {Js.Str(extra)} more from the cart") : Js.Str(""))} at ({Js.Str(job.dest.x)}, {Js.Str(job.dest.y)})");
    }

    /// <summary>With a cart: take up to CART_LOAD - 1 more loose things from round here that belong in some store room.</summary>
    public static void loadCart(SimState state, Survivor s, Item first)
    {
        var stores = Rooms.completeStoreRooms(state);
        bool astray(Item i)
        {
            var home = Rooms.homeFor(stores, i.kind);
            return (home.size > 0) && !home.has(Rooms.labelAt(state, i.pos.x, i.pos.y));
        }
        var extra = state.items.filter((i) => (((i.pos != null) && (i.reservedBy == null)) && (Space.manhattan(i.pos, s.pos) <= Jobs.CART_REACH)) && astray(i)).sort((a, b) => Space.manhattan(a.pos, s.pos) - Space.manhattan(b.pos, s.pos)).slice(0, Jobs.CART_LOAD - 1);
        foreach (var i in Js.Iter(extra))
        {
            i.pos = null;
            i.carriedBy = s.id;
            s.cartLoad.push(i.id);
        }
        if (extra.length > 0)
        {
            s.note = $"loaded {Js.Str(first.kind)} and {Js.Str(extra.length)} more into the cart";
        }
    }

    /// <summary>A storage tile is free if it can hold an item and no other active store job has reserved it.</summary>
    public static bool storageTileFree(SimState state, Job job, Point p, JsSet<double> storage)
    {
        return (storage.has(Rooms.labelAt(state, p.x, p.y)) && Items.canHoldItem(state, p.x, p.y)) && !state.jobs.some((j) => (((j != job) && (j.reservedBy != null)) && (j.dest != null)) && Space.samePoint(j.dest, p));
    }

    /// <summary>Nearest free, reachable tile in any of the given complete rooms.</summary>
    public static Point? pickStorageTile(SimState state, Point from, Job job, JsSet<double> storage)
    {
        var __d1 = Rooms.getRoomMap(state);
        var rooms = __d1.rooms;
        var __d2 = state.grid;
        var width = __d2.width;
        List<Point> candidates = [];
        foreach (var index in Js.Iter(Js.ListOf(storage).sort((a, b) => a - b)))
        {
            foreach (var i in Js.Iter(Js.NN(Js.Get(rooms, index)).tiles))
            {
                Point p = new Point { x = Js.I(i % width), y = Js.I(JsMath.floor(((double)(i) / (width)))) };
                if (storageTileFree(state, job, p, storage))
                {
                    candidates.push(p);
                }
            }
        }
        candidates.sort((a, b) => Space.manhattan(from, a) - Space.manhattan(from, b));
        return candidates.find((p) => Pathfinding.canReach(state.grid, state.walkabilityVersion, from, [p])) ?? null;
    }
}
