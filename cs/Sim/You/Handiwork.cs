// Ported from src/sim/handiwork.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Handiwork: what the survivor you control does with their own hands (control.ts), so
// going solo doesn't mean handing over to the colony to get a door nailed shut.
//
//   work (R) the tile in front:
//     damaged wall, door or barricade  repair it (no materials), until it's whole
//     door, carrying wood              barricade it (still yours to walk through; tougher)
//     wall or barricade, carrying wood reinforce it (2.5x the battering, as with scrap)
//     bare floor, carrying wood        put up a wall
//   forage (E underfoot, outdoors on grass, by day, spring to autumn): a chance of food,
//     once a day per tile
//   hunt: swing at a deer next to you (or shoot one); a hit may bring it down for venison
// Building takes BUILD_WORK ticks (as a colonist would); moving stops it.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Handiwork
{

    public const int FORAGE_TICKS = 60;

    public const double FORAGE_CHANCE = 0.35;

    /// <summary>A swing (or shot) at a deer next to you brings it down this often.</summary>
    public const double HUNT_CHANCE = 0.5;

    /// <summary>Wood in your hands, else in your bag (picked-up wood goes in the bag when it fits).</summary>
    public static Item? carryingWood(SimState state, Survivor s)
    {
        var item = Js.T(s.carrying) ? Items.getItem(state, s.carrying) : null;
        if (item?.kind == "wood")
        {
            return item;
        }
        foreach (var id in Js.Iter(s.pack ?? []))
        {
            var packed = Items.getItem(state, id);
            if (packed?.kind == "wood")
            {
                return packed;
            }
        }
        return null;
    }

    /// <summary>What working this tile would do, or why not.</summary>
    public static object plan(SimState state, Survivor s, Point p)
    {
        var tile = Grids.getTile(state.grid, p.x, p.y);
        if (Breach.isStructure(tile) && (Breach.structureDamage(state, p.x, p.y) > 0))
        {
            return new WorkPlan { what = "repair" };
        }
        var wood = carryingWood(state, s);
        if (tile == "door")
        {
            return Js.T(wood) ? (object)new WorkPlan { what = "build", target = "barricade" } : "you need wood to barricade a door";
        }
        if ((tile == "wall") || (tile == "barricade"))
        {
            if (Defences.isReinforced(state, p.x, p.y))
            {
                return "already as strong as you can make it";
            }
            return Js.T(wood) ? (object)new WorkPlan { what = "build", target = "reinforce" } : "you need wood to reinforce it";
        }
        if (tile != "floor")
        {
            return "nothing to work on there";
        }
        if (!Js.T(wood))
        {
            return "you need wood to put up a wall";
        }
        if (Js.T(Js.T(FurnitureMod.furnitureAt(state, p.x, p.y)) || Js.T(Items.itemAt(state, p.x, p.y))))
        {
            return "something is in the way";
        }
        if (state.survivors.some((o) => (o.pos.x == p.x) && (o.pos.y == p.y)) || state.zombies.some((z) => (z.pos.x == p.x) && (z.pos.y == p.y)))
        {
            return "someone is in the way";
        }
        return (object)new WorkPlan { what = "build", target = "wall" };
    }

    /// <summary>Start working a tile next to you. Returns the busy action, or why not.</summary>
    public static object startWork(SimState state, Survivor s, Point p)
    {
        if ((p.x == s.pos.x) && (p.y == s.pos.y))
        {
            return "you can't build where you stand";
        }
        var planned = plan(state, s, p);
        if (planned is string why)
        {
            return why;
        }
        var job = (WorkPlan)planned;
        if (job.what == "repair")
        {
            return new WorkStart { what = "repair", targetId = $"{Js.Str(p.x)},{Js.Str(p.y)}", duration = JsMath.ceil(((double)(Breach.structureDamage(state, p.x, p.y)) / (Breach.REPAIR_PER_TICK))) };
        }
        return new WorkStart { what = "build", targetId = $"{Js.Str(p.x)},{Js.Str(p.y)},{Js.Str(job.target)}", duration = Js.Get(Designations.BUILD_WORK, job.target) };
    }

    /// <summary>A tick of repairing. Returns true when done (or no longer needed).</summary>
    public static bool repairTick(SimState state, string targetId)
    {
        var (x, y) = targetId.split(",").map((__v) => Js.Num(__v));
        if (!Breach.isStructure(Grids.getTile(state.grid, x, y)))
        {
            return true;
        }
        Breach.repairStructure(state, x, y, Breach.REPAIR_PER_TICK);
        return Breach.structureDamage(state, x, y) == 0;
    }

    /// <summary>The build is done: spend the wood and make it so (if it still makes sense). Returns what happened.</summary>
    public static string finishBuild(SimState state, Survivor s, string targetId)
    {
        var (xs, ys, target) = targetId.split(",");
        Point p = new Point { x = Js.I(Js.Num(xs)), y = Js.I(Js.Num(ys)) };
        var now = plan(state, s, p);
        if (now is string why)
        {
            return why;
        }
        if ((((WorkPlan)now).what != "build") || (((WorkPlan)now).target != target))
        {
            return "it changed while you worked";
        }
        var wood = Js.NN(carryingWood(state, s));
        if (s.carrying == wood.id)
        {
            s.carrying = null;
        }
        Inventory.unpack(s, wood.id);
        Items.removeItem(state, wood);
        Blueprint bp = new Blueprint { id = $"h{Js.Str(state.nextId++)}", x = Js.I(p.x), y = Js.I(p.y), target = target, needsMaterial = false, delivered = true, workDone = 0, workRequired = 0 };
        Designations.completeBlueprint(state, bp);
        return (target == "wall") ? "put up a wall" : ((target == "barricade") ? "barricaded the door" : "reinforced it");
    }

    /// <summary>Foraging needs open ground, daylight and a growing season; and each tile gives once a day.</summary>
    public static string? canForage(SimState state, Survivor s)
    {
        var __d0 = s.pos;
        var x = __d0.x;
        var y = __d0.y;
        var g = Js.Get(Grids.GROUNDS, Js.NN(Grids.groundAt(state.grid, Grids.tileIndex(state.grid, x, y))));
        if (Rooms.isIndoors(state, x, y) || ((g != "grass") && (g != "field")))
        {
            return "nothing to forage here: find open grass, or a field";
        }
        if (Time.isNight(state.tick))
        {
            return "too dark to forage";
        }
        if (Seasons.seasonOf(state.tick) == "winter")
        {
            return "nothing grows in winter";
        }
        if (Js.Get(state.foraged, Js.Str(Grids.tileIndex(state.grid, x, y))) == Time.dayOf(state.tick))
        {
            return "already picked over today";
        }
        return null;
    }

    /// <summary>Foraging done: maybe some food (less in a nuclear winter).</summary>
    public static string finishForage(SimState state, Survivor s)
    {
        var i = Grids.tileIndex(state.grid, s.pos.x, s.pos.y);
        state.foraged[Js.Idx(Js.Str(i))] = Time.dayOf(state.tick);
        var field = (Js.Get(Grids.GROUNDS, Js.NN(Grids.groundAt(state.grid, i))) == "field") ? 2 : 1;
        var chance = ((double)(((double)(FORAGE_CHANCE) * (field))) * ((state.settings.winter ? 0.5 : 1))) * Growth.forageBonus(state, s.pos.x, s.pos.y);
        if (Hash.hashRoll(Hash.idNumber(s.id), i, state.tick) >= chance)
        {
            return "found nothing worth eating";
        }
        Items.spawnItem(state, "food", s.pos.x, s.pos.y);
        return "found something to eat";
    }

    /// <summary>Forget foraged tiles from before today (at dawn).</summary>
    public static void forgetForaging(SimState state)
    {
        var today = Time.dayOf(state.tick);
        foreach (var k in Js.Iter(Js.ObjectKeys(state.foraged)))
        {
            if (Js.Get(state.foraged, k) != today)
            {
                state.foraged.Remove(Js.Str(k));
            }
        }
    }

    /// <summary>A swing or shot at an animal. A deer drops to a lucky blow (HUNT_CHANCE) or bolts; the rest take `damage` from their health, and a wolf or bear that's hit comes for whoever hit it (its pack too). Brought down: MEAT by kind where it fell. Returns what happened.</summary>
    public static string hitAnimal(SimState state, Survivor s, string id, double damage)
    {
        var a = state.animals.find((q) => q.id == id);
        if (!Js.T(a))
        {
            return "it got away";
        }
        if (a.kind == "deer")
        {
            if (Hash.hashRoll(Hash.idNumber(a.id), state.tick, 3566.0) >= HUNT_CHANCE)
            {
                a.leavesTick = JsMath.min(a.leavesTick, state.tick + 50);
                return "missed: the deer bolts";
            }
        }
        else
        {
            a.health = (Js.Q(a.health, 0)) - damage;
            if (a.health > 0)
            {
                Animals.provoke(state, a, s);
                return $"hit the {Js.Str(a.kind)}";
            }
        }
        Animals.butcher(state, a);
        Events.logEvent(state, $"{Js.Str(s.name)} brought down a {Js.Str(a.kind)}");
        return "brought it down";
    }
}
