// The car you sit in is solid (2026-11-12, VehicleCabin.cs): the dead and animals stop at its body and batter it,
// not you, till it is a wreck.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class VehicleCabinTest
{
    static void run(SimState state, int n)
    {
        for (int i = 0; i < n; i++) Tick.tick(state);
    }

    /// <summary>You in the car at (3, 3) in the open (facing east), a walker at (4, 7) already after you.</summary>
    static (SimState state, Survivor s, Vehicle car, Zombie z) seated()
    {
        var f = Ported.VehiclesTest.field(Js.ArrayFrom(10, (int _i) => Ported.VehiclesTest.ROW));
        Ported.VehiclesTest.getIn(f.state);
        expect(Vehicles.vehicleOf(f.state, f.s)).toBe(f.car);
        var z = Zombies.addZombie(f.state, 4, 7, 200, "static");
        z.targetId = f.s.id;
        return (f.state, f.s, f.car, z);
    }

    [Fact(DisplayName = "car cabin > the dead stop at the body of the car you sit in and batter it, not you")]
    public void Batter()
    {
        var (state, s, car, z) = seated();
        double closest = double.PositiveInfinity;
        for (int i = 0; i < 300; i++)
        {
            Tick.tick(state);
            closest = JsMath.min(closest, Vehicles.gap(car, z.at));
        }
        // Never inside the body (a body's width of slack for the tick it walks in before it is put out).
        expect(Vehicles.gap(car, z.at)).toBeGreaterThanOrEqual(Motion.BODY_RADIUS - 2);
        expect(car.health).toBeLessThan(Vehicles.VEHICLE_HEALTH);
        expect(s.health).toBe(Health.HEALTH_MAX);
    }

    [Fact(DisplayName = "car cabin > once the car is a wreck, the blows reach you")]
    public void Wreck()
    {
        var (state, s, car, z) = seated();
        car.health = 1;
        run(state, 400);
        expect(car.health).toBe(0);
        expect(s.health).toBeLessThan(Health.HEALTH_MAX);
        expect(Vehicles.gap(car, z.at)).toBeGreaterThanOrEqual(Motion.BODY_RADIUS - 2);
    }

    [Fact(DisplayName = "car cabin > a wolf after you stops at the body and bites the car")]
    public void Wolf()
    {
        var (state, s, car, z) = seated();
        state.zombies.Clear();
        Zombies.edited(state.zombies);
        var w = Animals.addAnimal(state, "wolf", 4, 8);
        w.angryAt = s.id;
        run(state, 300);
        expect(Vehicles.gap(car, w.at)).toBeGreaterThanOrEqual(Motion.BODY_RADIUS - 2);
        expect(s.health).toBe(Health.HEALTH_MAX);
        expect(car.health).toBeLessThan(Vehicles.VEHICLE_HEALTH);
    }

    [Fact(DisplayName = "car cabin > nobody at the wheel: no cabins, and nothing is moved")]
    public void Empty()
    {
        var f = Ported.VehiclesTest.field();
        expect(Vehicles.cabins(f.state).Count).toBe(0);
        Ported.VehiclesTest.getIn(f.state);
        expect(Vehicles.cabins(f.state).Count).toBe(1);
    }
}
