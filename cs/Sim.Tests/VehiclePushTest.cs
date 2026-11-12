// Pushing a car (2026-11-12, VehiclePush.cs): the push key held, walking into a parked car rolls it along its length.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
using static LoonieRoo.Sim.Tests.TestKit;
namespace LoonieRoo.Sim.Tests;

public class VehiclePushTest
{
    static void run(SimState state, int n) => tickN(state, n);

    /// <summary>A sedan facing east with its middle on (10, 3), you just behind its boot. `wallAt`: a wall column there.</summary>
    static (SimState state, Survivor s, Vehicle car) behind(int wallAt = -1)
    {
        var rows = Js.ArrayFrom(7, (int _i) => wallAt < 0 ? Ported.VehiclesTest.ROW : Ported.VehiclesTest.ROW.slice(0, wallAt) + "#" + Ported.VehiclesTest.ROW.slice(wallAt + 1));
        var f = Ported.VehiclesTest.field(rows);
        Vehicles.remove(f.state, f.car);
        var car = Vehicles.addVehicle(f.state, 10, 3);
        f.s.at = new Point { x = car.at.x - Vehicles.CAR.halfLength - Motion.BODY_RADIUS - 4, y = car.at.y };
        f.s.pos = Motion.tileOf(f.s.at);
        return (f.state, f.s, car);
    }

    [Fact(DisplayName = "pushing a car > held, walking into its boot rolls it forward about a metre a second, its tiles with it, for endurance")]
    public void Rolls()
    {
        var (state, s, car) = behind();
        double x0 = car.at.x, e0 = s.endurance;
        Commands.applyCommand(state, new Command { type = "push", on = true });
        Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
        run(state, 40);
        double moved = car.at.x - x0;
        expect(moved).toBeGreaterThan(Vehicles.PUSH_STEP * 30);
        expect(moved).toBeLessThanOrEqual(Vehicles.PUSH_STEP * 40);
        expect(car.at.y).toBe(Motion.centre(new Point(10, 3)).y);
        // Its tiles moved: the tile its nose is over now is in the way, the one its boot was over is free.
        expect(Grids.isParked(state.grid, 13, 3) || Grids.isParked(state.grid, 14, 3)).toBe(true);
        expect(Grids.isParked(state.grid, 8, 3)).toBe(false);
        expect(Vehicles.gap(car, s.at)).toBeLessThan(Vehicles.PUSH_REACH);
        expect(s.endurance).toBeLessThan(e0);
    }

    [Fact(DisplayName = "pushing a car > without the key, or against its side, it stays put")]
    public void Stays()
    {
        var (state, s, car) = behind();
        double x0 = car.at.x;
        Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
        run(state, 20);
        expect(car.at.x).toBe(x0);
        // From its side, pushing across it.
        s.at = new Point { x = car.at.x, y = car.at.y - Vehicles.CAR.halfWidth - Motion.BODY_RADIUS - 4 };
        s.pos = Motion.tileOf(s.at);
        Commands.applyCommand(state, new Command { type = "push", on = true });
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 1 });
        run(state, 20);
        expect(car.at.x).toBe(x0);
        expect(car.at.y).toBe(Motion.centre(new Point(10, 3)).y);
    }

    [Fact(DisplayName = "pushing a car > a wall stops it at its bumper")]
    public void Wall()
    {
        var (state, s, car) = behind(16);
        Commands.applyCommand(state, new Command { type = "push", on = true });
        Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
        run(state, 200);
        // Its nose is at the wall's face (x = 16 tiles), not into it.
        double nose = car.at.x + Vehicles.CAR.halfLength;
        expect(nose).toBeLessThanOrEqual(16 * Motion.SUB);
        expect(nose).toBeGreaterThan(16 * Motion.SUB - 40);
        expect(Grids.isParked(state.grid, 16, 3)).toBe(false);
    }

    [Fact(DisplayName = "pushing a car > the dead in its way are moved off its tiles, you are not")]
    public void Dead()
    {
        var (state, s, car) = behind();
        var z = Zombies.addZombie(state, 15, 3, 200, "static");
        z.at = Motion.centre(z.pos);
        Commands.applyCommand(state, new Command { type = "push", on = true });
        Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
        run(state, 60);
        expect(Grids.isParked(state.grid, z.pos.x, z.pos.y)).toBe(false);
        expect(Vehicles.gap(car, s.at)).toBeLessThan(Vehicles.PUSH_REACH);
    }
}
