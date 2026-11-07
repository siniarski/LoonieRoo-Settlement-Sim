// Ported from src/sim/succession.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// The death rule: when the survivor you control dies, you choose who carries on.
//   an heir   someone in your group with the resolve to step up (STEP_UP or more; no children)
//   anew      a new survivor, alone, somewhere far off in the same world, with a solo kit
//   none      nobody: watch the colony get on without you
// Until you choose, state.succession says who died, and where (the UI asks, paused).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class SuccessionMod
{

    public const int STEP_UP = 40;

    /// <summary>How far off a new start is.</summary>
    public const int ANEW_MIN = 60;

    public const int ANEW_SPREAD = 30;

    /// <summary>Who could take over now, most resolved first.</summary>
    public static List<Survivor> heirs(SimState state)
    {
        return state.survivors.filter((s) => (s.childUntil == null) && (s.resolve >= STEP_UP)).sort((a, b) => ((b.resolve - a.resolve) is var __or0 && Js.T(__or0) ? __or0 : a.id.localeCompare(b.id)));
    }

    /// <summary>Called as the survivor you control dies.</summary>
    public static void beginSuccession(SimState state, Survivor s, string cause)
    {
        state.succession = new Succession { name = s.name, cause = cause, x = Js.I(s.pos.x), y = Js.I(s.pos.y) };
    }

    /// <summary>Choose who carries on. Returns an error, or null when done.</summary>
    public static string? succeed(SimState state, string choice)
    {
        var was = state.succession;
        if (!Js.T(was))
        {
            return "nobody needs replacing";
        }
        if (choice == "none")
        {
            state.succession = null;
            Events.logEvent(state, $"Nobody takes {Js.Str(was.name)}'s place. The others carry on as they can.");
            return null;
        }
        if (choice == "anew")
        {
            var s = startAnew(state, was);
            if (!Js.T(s))
            {
                return "nowhere to start";
            }
            state.succession = null;
            Events.logEvent(state, $"Far from where {Js.Str(was.name)} fell, {Js.Str(s.name)} sets out alone.");
            return null;
        }
        var heir = heirs(state).find((s) => s.id == choice);
        if (!Js.T(heir))
        {
            return $"they don't have it in them to take over";
        }
        take(state, heir);
        state.succession = null;
        Events.logEvent(state, $"{Js.Str(heir.name)} takes up where {Js.Str(was.name)} left off.");
        return null;
    }

    public static void take(SimState state, Survivor s)
    {
        state.control = new Control { survivorId = s.id, dx = 0, dy = 0, facing = new Point { x = Js.I(0), y = Js.I(1) }, nextSwing = 0, busy = null };
        s.order = null;
        s.goal = null;
        s.path = [];
        s.note = "carrying on";
    }

    /// <summary>A new survivor far off, with something to swing and a bandage.</summary>
    public static Survivor? startAnew(SimState state, Point near)
    {
        var __d1 = state.grid;
        var width = __d1.width;
        var height = __d1.height;
        var angle = ((double)(((double)(Hash.hashRoll(state.tick, near.x, near.y)) * (JsMath.PI))) * (2));
        var dist = ANEW_MIN + ((double)(Hash.hashRoll(state.tick, near.y, near.x)) * (ANEW_SPREAD));
        Point aim = new Point { x = Js.I(JsMath.max(2, JsMath.min(width - 3, JsMath.round(near.x + ((double)(JsMath.cos(angle)) * (dist)))))), y = Js.I(JsMath.max(2, JsMath.min(height - 3, JsMath.round(near.y + ((double)(JsMath.sin(angle)) * (dist)))))) };
        Worldgen.ensureGeneratedAround(state, aim);
        Point? spot = null;
        for (var r = 0; (r <= 20) && !Js.T(spot); r++)
        {
            for (var dy = -r; (dy <= r) && !Js.T(spot); dy++)
            {
                for (var dx = -r; (dx <= r) && !Js.T(spot); dx++)
                {
                    if (JsMath.max(JsMath.abs(dx), JsMath.abs(dy)) != r)
                    {
                        continue;
                    }
                    var x = aim.x + dx;
                    var y = aim.y + dy;
                    if ((((((x >= 0) && (y >= 0)) && (x < width)) && (y < height)) && (Grids.getTile(state.grid, x, y) == "floor")) && Grids.isWalkable(state.grid, x, y))
                    {
                        spot = new Point { x = Js.I(x), y = Js.I(y) };
                    }
                }
            }
        }
        if (!Js.T(spot))
        {
            return null;
        }
        var s = Survivors.addSurvivor(state, spot.x, spot.y);
        s.traits = Skills.rollTraits(s.id);
        take(state, s);
        var weapon = Items.spawnItem(state, "weapon", spot.x, spot.y);
        weapon.pos = null;
        weapon.carriedBy = s.id;
        s.weapon = weapon.id;
        Items.spawnItem(state, "bandage", spot.x, spot.y);
        for (var y = spot.y - 6; y <= (spot.y + 6); y++)
        {
            for (var x = spot.x - 6; x <= (spot.x + 6); x++)
            {
                if ((((x >= 0) && (y >= 0)) && (x < width)) && (y < height))
                {
                    state.grid.explored[Js.Idx(((double)(y) * (width)) + x)] = 1;
                }
            }
        }
        return s;
    }
}
