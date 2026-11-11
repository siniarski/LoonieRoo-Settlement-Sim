// Ported from src/sim/wellbeing.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Higher needs, above survival: Social and Comfort, 0..100 each. Neither kills anyone;
// both move mood (Mood.cs), and so, day by day, resolve (Resolve.cs).
//
//   Social   rises while awake near someone else (within COMPANY tiles) and with every
//            talk; sinks while alone. Under LOW: "lonely". Over HIGH: "good company".
//   Comfort  set each time they wake, from where they slept: a bed, indoors, a finished
//            bedroom (housing or barracks), warm, clean. It moves halfway towards each
//            night's score, so one bad night doesn't undo a week of good ones.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Wellbeing
{

    public const int NEED_MAX = 100;

    public const int LOW = 25;

    public const int HIGH = 75;

    /// <summary>Social is updated this often, by one point up (in company) or down (alone).</summary>
    public const int SOCIAL_EVERY = 40;

    /// <summary>Within this many tiles (Chebyshev) of someone awake counts as company.</summary>
    public const int COMPANY = 4;

    public const int TALK_SOCIAL = 20;

    /// <summary>Mood while lonely / in good company / uncomfortable / comfortable.</summary>
    public const double LONELY_MOOD = -10;

    public const int COMPANY_MOOD = 5;

    public const double UNCOMFORTABLE_MOOD = -8;

    public const int COMFORTABLE_MOOD = 8;

    public static double clamp(double n)
    {
        return JsMath.max(0, JsMath.min(NEED_MAX, n));
    }

    public static void updateSocial(SimState state)
    {
        if ((state.tick % SOCIAL_EVERY) != 0)
        {
            return;
        }
        var awake = state.survivors.filter((s) => !Js.T(s.sleep?.asleep));
        foreach (var s in Js.Iter(state.survivors))
        {
            if (Js.T(s.sleep?.asleep))
            {
                continue;
            }
            var company = awake.some((o) => (o != s) && (JsMath.max(JsMath.abs(o.pos.x - s.pos.x), JsMath.abs(o.pos.y - s.pos.y)) <= COMPANY));
            s.social = clamp(s.social + (company ? 1 : -1));
        }
    }

    public static void socialise(Survivor s, int by)
    {
        s.social = clamp(s.social + by);
    }
    public static void socialise(Survivor s, double by) => socialise(s, Js.I(by));

    /// <summary>How good a night's sleep was, where they lie now (bedId: the bed, or null for the floor).</summary>
    public static double nightScore(SimState state, Survivor s, string? bedId)
    {
        var bed = Js.T(bedId) ? FurnitureMod.furnitureAt(state, s.pos.x, s.pos.y) : null;
        var inBed = (bed?.kind == "bed") && (bed.id == bedId);
        var room = Rooms.roomAt(state, s.pos.x, s.pos.y);
        var purpose = Js.T(room) ? Rooms.designationOf(state, room)?.purpose : null;
        var bedroom = (Js.T(room) && ((purpose == "housing") || (purpose == "barracks"))) && Rooms.evaluateRoom(state, room, purpose).complete;
        var warm = Power.isWarmed(state, s.pos) || ((Seasons.seasonOf(state.tick) != "winter") && !state.settings.winter);
        var dirty = Js.T(room) ? room.tiles.filter((i) => Grids.isDirty(state.grid, i % state.grid.width, JsMath.floor(((double)(i) / (state.grid.width))))).length : 0;
        return clamp(((((15 + (Js.T(inBed) ? 30 : 0)) + (Js.T(room) ? 20 : 0)) + (Js.T(bedroom) ? 15 : 0)) + (Js.T(warm) ? 15 : 0)) - JsMath.min(15, ((double)(dirty) * (5))));
    }

    /// <summary>On waking: comfort moves halfway to the night's score.</summary>
    public static void wakeComfort(SimState state, Survivor s, string? bedId)
    {
        s.comfort = JsMath.round(((double)((s.comfort + nightScore(state, s, bedId))) / (2)));
    }

    /// <summary>Mood factors from the higher needs, as [label, effect] pairs.</summary>
    public static List<(string, double)> wellbeingFactors(Survivor s)
    {
        List<(string, double)> @out = [];
        if (s.social < LOW)
        {
            @out.push(("lonely", LONELY_MOOD));
        }
        else if (s.social > HIGH)
        {
            @out.push(("good company", COMPANY_MOOD));
        }
        if (s.comfort < LOW)
        {
            @out.push(("uncomfortable", UNCOMFORTABLE_MOOD));
        }
        else if (s.comfort > HIGH)
        {
            @out.push(("comfortable", COMFORTABLE_MOOD));
        }
        return @out;
    }
}
