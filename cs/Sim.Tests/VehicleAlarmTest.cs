// Car alarms (2026-11-12, VehicleAlarm.cs): some sedans have one; getting in, hitting it or the dead climbing it
// sets it off for a minute, a noise the dead come to.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
using static LoonieRoo.Sim.Tests.TestKit;
namespace LoonieRoo.Sim.Tests;

public class VehicleAlarmTest
{
    const int SUB = Motion.SUB;

    static void run(SimState state, int n) => tickN(state, n);

    /// <summary>An id with an alarm (or without), from `from` up.</summary>
    static string alarmId(bool with, int from = 900000)
    {
        for (int i = from; ; i++)
        {
            var id = $"v{i}";
            if (Vehicles.hasAlarm(new Vehicle { id = id, kind = "car" }) == with) return id;
        }
    }

    [Fact(DisplayName = "car alarms > about three sedans in ten have one, by their id alone; no pickup does")]
    public void Share()
    {
        int n = 0;
        for (int i = 0; i < 2000; i++)
        {
            if (Vehicles.hasAlarm(new Vehicle { id = $"v{i}", kind = "car" })) n++;
            expect(Vehicles.hasAlarm(new Vehicle { id = $"v{i}", kind = "pickup" })).toBe(false);
            expect(Vehicles.hasAlarm(new Vehicle { id = $"v{i}", kind = "truck" })).toBe(false);
        }
        expect(n).toBeGreaterThan(500);
        expect(n).toBeLessThan(700);
    }

    [Fact(DisplayName = "car alarms > getting in sets it off: a minute of noise that draws the dead, then quiet")]
    public void Getting_in()
    {
        var f = Ported.VehiclesTest.field();
        var (state, s, car) = (f.state, f.s, f.car);
        car.id = alarmId(true);
        var z = Zombies.addZombie(state, 20, 3, 200, "static");
        expect(Ported.VehiclesTest.getIn(state)).toBe(true);
        expect(Vehicles.vehicleOf(state, s)).toBe(car);
        expect(Vehicles.ringing(state, car)).toBe(true);
        expect(z.heading?.x).toBe(car.pos.x);
        expect(state.events.Any((e) => e.message.Contains("car alarm"))).toBe(true);
        // (It would come and batter the car, and every blow sets the alarm off again.)
        state.zombies.Clear();
        Zombies.edited(state.zombies);
        run(state, Vehicles.ALARM_TICKS);
        expect(Vehicles.ringing(state, car)).toBe(false);
        // Out and in again: it was already set off once; you aren't breaking in now.
        Commands.applyCommand(state, new Command { type = "use", x = car.pos.x, y = car.pos.y });
        run(state, Vehicles.GET_OUT_TICKS + 2);
        expect(Vehicles.vehicleOf(state, s)).toBeNull();
        expect(Ported.VehiclesTest.getIn(state)).toBe(true);
        expect(Vehicles.ringing(state, car)).toBe(false);
    }

    [Fact(DisplayName = "car alarms > a car without one stays quiet")]
    public void Quiet()
    {
        var f = Ported.VehiclesTest.field();
        f.car.id = alarmId(false);
        Ported.VehiclesTest.getIn(f.state);
        expect(Vehicles.ringing(f.state, f.car)).toBe(false);
        Vehicles.dent(f.state, f.car, 10);
        expect(Vehicles.ringing(f.state, f.car)).toBe(false);
        expect(f.car.alarmUntil).toBeNull();
    }

    [Fact(DisplayName = "car alarms > your swing at a parked car dents it and sets it off: a lure")]
    public void Swing()
    {
        var f = Ported.VehiclesTest.field();
        var (state, s, car) = (f.state, f.s, f.car);
        Vehicles.remove(state, car);
        car = Vehicles.addVehicle(state, 10, 3);
        car.id = alarmId(true);
        s.at = new Point { x = car.at.x - Vehicles.CAR.halfLength - Motion.BODY_RADIUS - 20, y = car.at.y };
        s.pos = Motion.tileOf(s.at);
        var health = car.health;
        Commands.applyCommand(state, new Command { type = "aim", x = 100, y = 0 });
        var r = Commands.applyCommand(state, new Command { type = "swing", x = car.pos.x, y = car.pos.y });
        run(state, 1);
        expect(s.note, r.reason ?? "").toBe("hit the car: its alarm is going off");
        expect(car.health).toBe(health - Vehicles.BLOW_DAMAGE);
        expect(Vehicles.ringing(state, car)).toBe(true);
    }

    [Fact(DisplayName = "car alarms > one of the dead climbing over a car with an alarm sets it off")]
    public void Climbed()
    {
        var rows = new List<string>();
        for (int y = 0; y < 12; y++) rows.Add(y == 6 ? "#######..#######" : y == 2 ? ".......s........" : "................");
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = rows });
        var s = state.survivors[0];
        foreach (var k in Js.Iter(Js.ObjectKeys(s.priorities))) s.priorities[Js.Idx(k)] = 0;
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        // A sedan nose to the north plugging the gap (as CrossingsTest's plug).
        var car = Vehicles.addVehicle(state, 7, 6, 24);
        Vehicles.unpark(state, car);
        car.at = new Point(8 * SUB, 6 * SUB + SUB / 2);
        car.was = car.at.Copy();
        Vehicles.park(state, car);
        car.id = alarmId(true);
        var z = Zombies.addZombie(state, 7, 10, 200, "static");
        z.targetId = s.id;
        bool climbed = false;
        for (int t = 0; t < 400 && !Vehicles.ringing(state, car); t++)
        {
            Tick.tick(state);
            climbed |= z.crossing != null;
        }
        expect(climbed).toBe(true);
        expect(Vehicles.ringing(state, car)).toBe(true);
    }
}
