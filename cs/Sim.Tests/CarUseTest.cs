// Getting into a parked car with E (ControlMod.use, VehicleBody.cs): from any side you can walk up to, looking at it.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class CarUseTest
{
    /// <summary>21 x 21 of open floor, you under control, a car of `kind` at (10, 10) on a heading.</summary>
    static (SimState state, Survivor s, Vehicle v) start(double heading, string kind)
    {
        var map = new List<string>();
        for (int r = 0; r < 21; r++) map.Add(new string('.', 21));
        map[0] = "s" + map[0].Substring(1);
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = map });
        var s = state.survivors[0];
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        var v = Vehicles.addVehicle(state, 10, 10, Js.I(heading), kind: kind);
        return (state, s, v);
    }

    /// <summary>Walk at the car's middle from `angle` (radians) four tiles out until its body stops you. False if
    /// you never got near.</summary>
    static bool walkUp(SimState state, Survivor s, Vehicle v, double angle)
    {
        var from = new Point { x = Js.I(v.at.x + JsMath.round(Math.Cos(angle) * 4 * Motion.SUB)), y = Js.I(v.at.y + JsMath.round(Math.Sin(angle) * 4 * Motion.SUB)) };
        Staging.place(s, Motion.tileOf(from).x, Motion.tileOf(from).y);
        s.at = from;
        s.was = from;
        int still = 0;
        for (int i = 0; i < 120 && still < 5; i++)
        {
            int dx = Js.I(JsMath.round((double)(v.at.x - s.at.x) * ControlMod.AIM_MAX / JsMath.max(1, JsMath.sqrt(Motion.distance2(v.at, s.at)))));
            int dy = Js.I(JsMath.round((double)(v.at.y - s.at.y) * ControlMod.AIM_MAX / JsMath.max(1, JsMath.sqrt(Motion.distance2(v.at, s.at)))));
            Commands.applyCommand(state, new Command { type = "steer", dx = dx, dy = dy });
            var was = s.at;
            Tick.tick(state);
            still = (s.at.x == was.x && s.at.y == was.y) ? still + 1 : 0;
        }
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 0 });
        // Stopped by it: its body (VehicleParking.cs pushOut) or, at the ends, the tiles under it, a little further out.
        return still >= 5;
    }

    /// <summary>The game's fallback for E (GameInput.FrontTile): the tile 0.8 m, else 1.2 m, ahead of the eye.</summary>
    static Point frontTile(Survivor s, double yaw)
    {
        foreach (double d in new[] { 0.8, 1.2 })
        {
            var t = new Point { x = Js.I(Math.Floor(s.at.x / (double)Motion.SUB + Math.Cos(yaw) * d)), y = Js.I(Math.Floor(s.at.y / (double)Motion.SUB + Math.Sin(yaw) * d)) };
            if (t.x != s.pos.x || t.y != s.pos.y) return t;
        }
        return s.pos;
    }

    [Fact(DisplayName = "car use > walked up to a parked car from any side and looking at it, E gets you in")]
    public void FromAnySide()
    {
        var misses = new List<string>();
        int tried = 0;
        foreach (var kind in new[] { "car", "truck" })
        {
            for (int h = 0; h < Vehicles.HEADINGS; h += 3)
            {
                for (int a = 0; a < 16; a++)
                {
                    var (state, s, v) = start(h, kind);
                    double angle = a * Math.PI / 8;
                    walkUp(state, s, v, angle);
                    tried++;
                    double yaw = Math.Atan2(v.at.y - s.at.y, v.at.x - s.at.x);
                    var t = frontTile(s, yaw);
                    var r = Commands.applyCommand(state, new Command { type = "use", x = t.x, y = t.y });
                    if (!r.ok || state.control.busy?.what != "getIn")
                        misses.Add($"{kind} h{h} a{a}: you {s.at.x},{s.at.y} (pos {s.pos.x},{s.pos.y}, {Vehicles.gap(v, s.at):0} from the body), E at {t.x},{t.y} ({Vehicles.gap(v, Motion.centre(t)):0}): {r.reason ?? s.note}");
                }
            }
        }
        expect(string.Join("\n", misses.Take(40)) + $"\n{misses.Count} of {tried}").toBe($"\n0 of {tried}");
    }

    [Fact(DisplayName = "car use > W still held against the car, getting in goes on; walking off along it stops it")]
    public void PushingAgainstIt()
    {
        foreach (var (dx, dy, getsIn) in new[] { (0, 1, true), (1, 0, false) })
        {
            var (state, s, v) = start(0, "car");
            walkUp(state, s, v, -Math.PI / 2);   // from the north, up against its left side
            var t = frontTile(s, Math.PI / 2);
            expect(Commands.applyCommand(state, new Command { type = "use", x = t.x, y = t.y }).ok).toBe(true);
            expect(state.control.busy?.what).toBe("getIn");
            Commands.applyCommand(state, new Command { type = "steer", dx = dx * ControlMod.AIM_MAX, dy = dy * ControlMod.AIM_MAX });
            for (int i = 0; i < Vehicles.GET_IN_TICKS + 2; i++) Tick.tick(state);
            expect(Vehicles.vehicleOf(state, s) == v, $"steering {dx},{dy}: {s.note}").toBe(getsIn);
        }
    }

    [Fact(DisplayName = "car use > W held at the boot, it opens")]
    public void BootWithWHeld()
    {
        var (state, s, v) = start(0, "car");
        walkUp(state, s, v, Math.PI);   // from the west: behind it
        Commands.applyCommand(state, new Command { type = "steer", dx = ControlMod.AIM_MAX, dy = 0 });
        expect(Commands.applyCommand(state, new Command { type = "boot" }).ok).toBe(true);
        for (int i = 0; i < Vehicles.BOOT_TICKS + 2; i++) Tick.tick(state);
        expect(v.bootOpen).toBe(true);
    }
}
