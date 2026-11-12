// Under a pickup (2026-11-12, VehicleHide.cs): crouched, E gets you under a parked pickup, out of sight of the dead
// that didn't see you go; crawlers still reach you; a move key crawls you out on the side you steer to.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
using static LoonieRoo.Sim.Tests.TestKit;
namespace LoonieRoo.Sim.Tests;

public class VehicleHideTest
{
    static void run(SimState state, int n) => tickN(state, n);

    /// <summary>A field 40 x 11, a pickup facing east with its middle on (10, 5), you crouched just south of its side.</summary>
    static (SimState state, Survivor s, Vehicle truck) beside(string kind = "truck")
    {
        var f = Ported.VehiclesTest.field(Js.ArrayFrom(11, (int _i) => Ported.VehiclesTest.ROW));
        Vehicles.remove(f.state, f.car);
        var truck = Vehicles.addVehicle(f.state, 10, 5, 0, kind: kind);
        f.s.at = new Point { x = truck.at.x, y = truck.at.y + Vehicles.shapeOf(truck).halfWidth + Motion.BODY_RADIUS + 10 };
        f.s.pos = Motion.tileOf(f.s.at);
        Commands.applyCommand(f.state, new Command { type = "sneak", on = true });
        return (f.state, f.s, truck);
    }

    static void under(SimState state, Vehicle truck)
    {
        var r = Commands.applyCommand(state, new Command { type = "use", x = truck.pos.x, y = truck.pos.y });
        expect(r.ok, r.reason ?? "").toBe(true);
        run(state, 1);
    }

    [Fact(DisplayName = "under a pickup > crouched, E gets you under its middle; standing, E gets you in; a sedan has no room")]
    public void Getting_under()
    {
        var (state, s, truck) = beside();
        under(state, truck);
        expect(Vehicles.underOf(state, s)).toBe(truck);
        expect(s.at.x).toBe(truck.at.x);
        expect(s.at.y).toBe(truck.at.y);
        expect(PoseIntents.ofSurvivor(state, s).stance).toBe("lie");
        // No swinging, shooting, jumping or using there.
        expect(ControlMod.swing(state, s, truck.pos)).toBeNull();
        expect(Leaps.leap(state, s)).toBe("not under a car");
        var (state2, s2, sedan) = beside("car");
        under(state2, sedan);
        expect(Vehicles.underOf(state2, s2)).toBeNull();
        expect(state2.control.busy?.what).toBe("getIn");
    }

    [Fact(DisplayName = "under a pickup > a move key crawls you out on that side of it")]
    public void Crawl_out()
    {
        var (state, s, truck) = beside();
        under(state, truck);
        // North is its left (it faces east): out on the north side, clear of its body.
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = -1 });
        run(state, 1);
        expect(Vehicles.underOf(state, s)).toBeNull();
        expect(s.at.y).toBeLessThan(truck.at.y - Vehicles.TRUCK.halfWidth);
        expect(Vehicles.gap(truck, s.at)).toBeGreaterThanOrEqual(Motion.BODY_RADIUS);
        // Under again, out of the back.
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 0 });
        s.at = new Point { x = truck.at.x, y = truck.at.y - Vehicles.TRUCK.halfWidth - Motion.BODY_RADIUS - 10 };
        s.pos = Motion.tileOf(s.at);
        under(state, truck);
        Commands.applyCommand(state, new Command { type = "steer", dx = -1, dy = 0 });
        run(state, 1);
        expect(s.at.x).toBeLessThan(truck.at.x - Vehicles.TRUCK.halfLength);
    }

    [Fact(DisplayName = "under a pickup > the dead that didn't see you go lose you; one that saw you can't get at you")]
    public void Hidden()
    {
        var (state, s, truck) = beside();
        // Far (out of SEE_UNDER), after you: it loses you. Near and looking: it saw you go.
        var far = Zombies.addZombie(state, 30, 5, 200, "static");
        far.targetId = s.id;
        var near = Zombies.addZombie(state, 10, 9, 200, "static");
        near.targetId = s.id;
        under(state, truck);
        expect(state.control.sawUnder).toEqual(Js.Arr([near.id]));
        run(state, 300);
        expect(far.targetId).toBeNull();
        expect(Space.manhattan(far.pos, s.pos)).toBeGreaterThan(8);
        expect(s.health).toBe(Health.HEALTH_MAX);
        expect(Vehicles.underOf(state, s)).toBe(truck);
    }

    [Fact(DisplayName = "under a pickup > a crawler comes under and gets at you")]
    public void Crawler()
    {
        var (state, s, truck) = beside();
        under(state, truck);
        var z = Zombies.addZombie(state, 10, 9, 200, "static");
        z.variant = "crawler";
        z.targetId = s.id;
        run(state, 400);
        expect(s.health, DebugText.dumpText(state)).toBeLessThan(Health.HEALTH_MAX);
    }
}
