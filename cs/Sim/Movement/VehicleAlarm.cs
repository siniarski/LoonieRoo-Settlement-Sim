// Car alarms (part of Vehicles; 2026-11-12): some cars have one, and it brings the dead.
//
//   which      a sedan whose id rolls under ALARM_SHARE (Hash.hashRoll of its id number: stateless, so a world's
//              cars have the same alarms however it is played); never the pickup or the expedition truck.
//   set off    getting in the first time (it was never set off before: you are breaking in), any dent (the dead
//              battering it, a ram, a crash: dent()), one of the dead starting to climb over it (Crossings.begin's
//              hook), and your swing at it on foot (ControlMod.melee: a blow that dents it a little): a lure.
//   ringing    ALARM_TICKS (a minute, in ticks whatever the day length, like a car's handling), a noise "alarm"
//              (Noise.cs: radius 25, the dead come to look) at the car every ALARM_NOISE_EVERY ticks. Set off again
//              while ringing, it rings on from then. `alarmUntil` is the tick it stops (null: never rang).
//   heard      the game plays the house alarm's bell at the car (godot/Audio/Loops.cs).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Vehicles
{
    /// <summary>The share of sedans with an alarm.</summary>
    public const double ALARM_SHARE = 0.3;

    /// <summary>How long an alarm rings once set off (ticks: a minute).</summary>
    public const int ALARM_TICKS = 600;

    /// <summary>It is a noise this often while ringing (ticks), as a house alarm (TimelineMod).</summary>
    public const int ALARM_NOISE_EVERY = 20;

    /// <summary>What your blow does to a car's body.</summary>
    public const int BLOW_DAMAGE = 5;

    /// <summary>Does this car have an alarm?</summary>
    public static bool hasAlarm(Vehicle v) => (v.kind ?? "car") == "car" && Hash.hashRoll(Hash.idNumber(v.id), 0xa1a2) < ALARM_SHARE;

    /// <summary>Is its alarm ringing now?</summary>
    public static bool ringing(SimState state, Vehicle v) => v.alarmUntil != null && state.tick < v.alarmUntil;

    /// <summary>Set its alarm off, if it has one. True if it did.</summary>
    public static bool setOffAlarm(SimState state, Vehicle v)
    {
        if (!hasAlarm(v)) return false;
        if (!ringing(state, v))
        {
            Events.logEvent(state, $"A car alarm went off at ({Js.Str(v.pos.x)}, {Js.Str(v.pos.y)})!");
            Noise.makeNoise(state, v.pos, "alarm");
        }
        v.alarmUntil = state.tick + ALARM_TICKS;
        return true;
    }

    /// <summary>One of the dead starts over whatever is on this tile (Crossings.begin): a parked car's alarm goes off.</summary>
    public static void climbedOver(SimState state, Point tile)
    {
        if (parkedOver(state, tile.x, tile.y) is { } v) setOffAlarm(state, v);
    }

    /// <summary>Every tick (updateVehicles): a ringing alarm is a noise every ALARM_NOISE_EVERY ticks; one run out is quiet.</summary>
    static void soundAlarm(SimState state, Vehicle v)
    {
        if (v.alarmUntil == null) return;
        if (ringing(state, v))
        {
            if ((state.tick % ALARM_NOISE_EVERY) == 0) Noise.makeNoise(state, v.pos, "alarm");
        }
    }

    /// <summary>Your swing on foot meets a parked car within reach ahead: a dent and its alarm, if it has one.
    /// Returns what happened, or null if there was no car there.</summary>
    public static string? strikeParked(SimState state, Survivor s, Point dir, double reach)
    {
        var v = vehicleNear(state, s.at, reach);  // the reach from your centre to its side: your arm (its end tiles keep you off it)
        if (v == null || !isParked(v)) return null;
        if ((double)(v.at.x - s.at.x) * dir.x + (double)(v.at.y - s.at.y) * dir.y <= 0) return null;
        bool rang = ringing(state, v);
        dent(state, v, BLOW_DAMAGE);
        return (ringing(state, v) && !rang) ? "hit the car: its alarm is going off" : "hit the car";
    }
}
