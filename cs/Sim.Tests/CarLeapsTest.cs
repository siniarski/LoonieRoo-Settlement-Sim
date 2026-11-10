// Leaps and parked cars (CarLeaps.cs): Space over a car, onto it, walking on it, down off it, and what happens to you
// on top when the car goes.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class CarLeapsTest
{
    /// <summary>15 x 9 of open floor, you under control at the centre of (x, y); rows of `wall` from `wallRow` down.</summary>
    static (SimState state, Survivor s) start(int x, int y, int wallRow = -1)
    {
        var map = new List<string>();
        for (int r = 0; r < 9; r++) map.Add(new string(r >= wallRow && wallRow >= 0 ? '#' : '.', 15));
        map[0] = "s" + map[0].Substring(1);
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = map });
        var s = state.survivors[0];
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        Staging.place(s, x, y);
        s.endurance = BodyMod.BODY_MAX;
        return (state, s);
    }

    /// <summary>Hold a direction until you stop (at the car), then keep holding it.</summary>
    static void walkInto(SimState state, Survivor s, int dx, int dy)
    {
        Commands.applyCommand(state, new Command { type = "steer", dx = dx, dy = dy });
        for (int i = 0; i < 60; i++)
        {
            var was = s.at;
            Tick.tick(state);
            if (s.at.x == was.x && s.at.y == was.y) break;
        }
    }

    static void jump(SimState state) => expect(Commands.applyCommand(state, new Command { type = "jump" }).ok).toBe(true);

    static void land(SimState state)
    {
        for (int i = 0; i < 40 && Leaps.airborne(state.control); i++) Tick.tick(state);
        expect(Leaps.airborne(state.control)).toBe(false);
    }

    static string where(SimState state, Survivor s) => $"at {s.at.x},{s.at.y} pos {s.pos.x},{s.pos.y} onCar {state.control?.onCar} note {s.note}\n{DebugText.dumpText(state)}";

    /// <summary>A sedan along row 4 (tiles 5-9, heading east), you two rows north of it at column x.</summary>
    static (SimState state, Survivor s, Vehicle v) sedan(int x, string kind = "car", int wallRow = -1)
    {
        var (state, s) = start(x, 1, wallRow);
        var v = Vehicles.addVehicle(state, 7, 4, 0, kind: kind);
        return (state, s, v);
    }

    [Fact(DisplayName = "car leaps > over a sedan side to side: through the middle is over the roof, at the ends a slide over the bonnet or the boot")]
    public void OverSideToSide()
    {
        foreach (var (x, note, ticks) in new[] { (7, "scrambled over the roof", Leaps.ROOF_TICKS), (9, "slid over the bonnet", Leaps.SLIDE_TICKS), (5, "slid over the boot", Leaps.SLIDE_TICKS) })
        {
            var (state, s, v) = sedan(x);
            walkInto(state, s, 0, 1);
            expect(Vehicles.gap(v, s.at), where(state, s)).toBeLessThan(Leaps.CAR_REACH);
            double before = s.endurance;
            jump(state);
            expect(state.control.busy.what, note).toBe("over");
            expect(s.note).toBe(note);
            expect(state.control.busy.duration, note).toBeGreaterThanOrEqual(ticks);
            expect(before - s.endurance, note).toBeGreaterThan(Leaps.VAULT_COST);   // dearer than a barricade
            land(state);
            expect(s.at.y, where(state, s)).toBeGreaterThan(v.at.y + Vehicles.CAR.halfWidth);
            expect(Leaps.standable(state, s.at), where(state, s)).toBe(true);
            expect(state.control.onCar).toBe(null);
            expect(s.pos.x == Motion.tileOf(s.at).x && s.pos.y == Motion.tileOf(s.at).y).toBe(true);
        }
    }

    [Fact(DisplayName = "car leaps > over a sedan nose to tail is over the roof, and takes longer")]
    public void OverNoseToTail()
    {
        var (state, s) = start(2, 4);
        var v = Vehicles.addVehicle(state, 7, 4, 0);
        walkInto(state, s, 1, 0);
        jump(state);
        expect(s.note, where(state, s)).toBe("scrambled over the roof");
        expect(state.control.busy.duration).toBeGreaterThan(Leaps.ROOF_TICKS);
        land(state);
        expect(s.at.x, where(state, s)).toBeGreaterThan(v.at.x + Vehicles.CAR.halfLength);
        expect(Leaps.standable(state, s.at)).toBe(true);
    }

    [Fact(DisplayName = "car leaps > over the truck: behind its cab is over the bed, through the cab over the roof")]
    public void OverTruck()
    {
        foreach (var (x, note) in new[] { (5, "climbed over the bed"), (7, "scrambled over the roof") })
        {
            var (state, s, v) = sedan(x, "truck");
            walkInto(state, s, 0, 1);
            jump(state);
            expect(s.note, where(state, s)).toBe(note);
            land(state);
            expect(s.at.y, where(state, s)).toBeGreaterThan(v.at.y + Vehicles.TRUCK.halfWidth);
            expect(Leaps.standable(state, s.at)).toBe(true);
        }
    }

    [Fact(DisplayName = "car leaps > a wall on the far side: no way over, so you climb onto it; winded, neither")]
    public void FarSideWall()
    {
        var (state, s, v) = sedan(7, "car", 6);
        walkInto(state, s, 0, 1);
        s.endurance = BodyMod.WINDED - 1;
        expect(Commands.applyCommand(state, new Command { type = "jump" }).ok).toBe(false);
        s.endurance = BodyMod.BODY_MAX;
        jump(state);
        expect(state.control.busy.what, where(state, s)).toBe("climb");
        land(state);
        expect(state.control.onCar).toBe(v.id);
    }

    [Fact(DisplayName = "car leaps > standing still facing it, you climb onto it; on top you walk without falling through or being pushed off, and step down off its edge")]
    public void OntoAndWalk()
    {
        var (state, s, v) = sedan(7);
        walkInto(state, s, 0, 1);
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 0 });
        Commands.applyCommand(state, new Command { type = "aim", x = 0, y = 1 });
        jump(state);
        expect(state.control.busy.what).toBe("climb");
        expect(s.note).toBe("climbed onto the car");
        Tick.tick(state);
        expect(PoseIntents.ofSurvivor(state, s).lift).toBeGreaterThan(0);
        land(state);
        expect(state.control.onCar, where(state, s)).toBe(v.id);
        expect(Leaps.height(state, s, 1)).toBeCloseTo(1.28, 2);   // on the roof
        // Along the roof to the bonnet: on it all the way, lower there.
        Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
        for (int i = 0; i < 6; i++)
        {
            Tick.tick(state);
            expect(state.control.onCar, where(state, s)).toBe(v.id);
            expect(Vehicles.gap(v, s.at)).toBe(0);
            expect(s.pos.x == Motion.tileOf(s.at).x && s.pos.y == Motion.tileOf(s.at).y).toBe(true);
        }
        expect(s.at.x).toBeGreaterThan(v.at.x + 300);
        expect(Leaps.height(state, s, 1)).toBeCloseTo(0.78, 2);   // the bonnet
        // Off the side: a step down, nothing spent, onto the ground clear of the car.
        double e = s.endurance;
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = -1 });
        for (int i = 0; i < 20 && !Leaps.airborne(state.control); i++) Tick.tick(state);
        expect(state.control.busy.what, where(state, s)).toBe("down");
        expect(s.note).toBe("stepped down");
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 0 });
        land(state);
        expect(state.control.onCar).toBe(null);
        expect(Leaps.standable(state, s.at), where(state, s)).toBe(true);
        expect(s.at.y).toBeLessThan(v.at.y - Vehicles.CAR.halfWidth);
        expect(s.endurance).toBeGreaterThanOrEqual(e - 1);
        expect(Leaps.height(state, s, 1)).toBe(0);
    }

    [Fact(DisplayName = "car leaps > on top, Space jumps down on your way; against a wall it jumps where you stand and you stay on")]
    public void JumpDown()
    {
        var (state, s, v) = sedan(7, "car", 6);
        expect(Leaps.standOn(state, s, v)).toBe(true);
        Commands.applyCommand(state, new Command { type = "aim", x = 0, y = 1 });   // the wall's side
        jump(state);
        expect(state.control.busy.what).toBe("jump");
        land(state);
        expect(state.control.onCar).toBe(v.id);
        expect(s.at.x == v.at.x && s.at.y == v.at.y).toBe(true);
        Commands.applyCommand(state, new Command { type = "aim", x = 0, y = -1 });
        jump(state);
        expect(state.control.busy.what).toBe("down");
        expect(s.note).toBe("jumped down");
        land(state);
        expect(state.control.onCar).toBe(null);
        expect(Leaps.standable(state, s.at), where(state, s)).toBe(true);
    }

    [Fact(DisplayName = "car leaps > the car driven away, got into or taken out of the world under you leaves you where a body fits")]
    public void CarGoes()
    {
        foreach (var how in new[] { "driven", "entered", "removed" })
        {
            var (state, s, v) = sedan(7);
            expect(Leaps.standOn(state, s, v)).toBe(true);
            if (how == "driven") { Vehicles.unpark(state, v); v.driverId = "x:run"; }
            else if (how == "entered") Vehicles.enterVehicle(state, s, v);
            else Vehicles.remove(state, v);
            Tick.tick(state);
            expect(state.control.onCar, how).toBe(null);
            if (how == "entered")
            {
                expect(Vehicles.vehicleOf(state, s), how).toBe(v);
                Vehicles.leaveVehicle(state, s);
                expect(Leaps.standable(state, s.at), how).toBe(true);
                continue;
            }
            expect(Leaps.standable(state, s.at) || how == "driven" && Vehicles.gap(v, s.at) == 0, $"{how} {where(state, s)}").toBe(true);
            expect(Leaps.height(state, s, 1), how).toBe(0);
        }
        // Mid-climb the car goes: the landing puts you on the ground.
        var (st, sv, car) = sedan(7);
        walkInto(st, sv, 0, 1);
        Commands.applyCommand(st, new Command { type = "steer", dx = 0, dy = 0 });
        Commands.applyCommand(st, new Command { type = "aim", x = 0, y = 1 });
        jump(st);
        Tick.tick(st);
        Vehicles.remove(st, car);
        land(st);
        expect(st.control.onCar).toBe(null);
        expect(Leaps.standable(st, sv.at), where(st, sv)).toBe(true);
    }

    [Fact(DisplayName = "car leaps > letting go of control on a roof puts you on the ground")]
    public void LetGo()
    {
        var (state, s, v) = sedan(7);
        expect(Leaps.standOn(state, s, v)).toBe(true);
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = null });
        expect(Leaps.standable(state, s.at), where(state, s)).toBe(true);
    }

    [Fact(DisplayName = "car leaps > a save taken in the air over a car, and one taken on its roof, read back and play on identically")]
    public void Saves()
    {
        foreach (var onRoof in new[] { false, true })
        {
            var (state, s, v) = sedan(7);
            walkInto(state, s, 0, 1);
            if (onRoof)
            {
                Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 0 });
                Commands.applyCommand(state, new Command { type = "aim", x = 0, y = 1 });
                jump(state);
                land(state);
                expect(state.control.onCar).toBe(v.id);
                Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
            }
            else
            {
                jump(state);
                Tick.tick(state);
                Tick.tick(state);
                expect(Leaps.airborne(state.control)).toBe(true);
            }
            var loaded = Savefile.loadFile(Savefile.toSaveFile(state));
            expect(loaded.ok).toBe(true);
            var copy = loaded.state;
            for (int i = 0; i < 30; i++)
            {
                Tick.tick(state);
                Tick.tick(copy);
            }
            var a = copy.survivors[0];
            expect($"{a.at.x},{a.at.y},{copy.control.onCar}", onRoof ? "roof" : "air").toBe($"{s.at.x},{s.at.y},{state.control.onCar}");
            expect(Savefile.toSaveFile(copy)).toBe(Savefile.toSaveFile(state));
        }
    }

    [Fact(DisplayName = "car leaps > the dead reach you on a roof by distance, as on the ground: from beside the car")]
    public void DeadReach()
    {
        var (state, s, v) = sedan(7);
        expect(Leaps.standOn(state, s, v)).toBe(true);
        var beside = new Zombie { at = Motion.centre(new Point { x = 7, y = 3 }) };
        expect(Motion.inReach(s, beside, Motion.REACH, state.grid)).toBe(true);
    }

    [Fact(DisplayName = "car leaps > a plain jump into a car's flank (on a slant past it) ends at its body, not in it")]
    public void JumpIntoFlank()
    {
        var (state, s, v) = sedan(7);
        s.at = new Point { x = s.at.x, y = 700 };
        s.pos = Motion.tileOf(s.at);
        state.control.running = true;
        Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 1 });   // east of the four sides: no car that way
        jump(state);
        expect(state.control.busy.what).toBe("jump");
        land(state);
        expect(s.at.y, where(state, s)).toBeGreaterThan(840);   // up against it
        expect(Vehicles.gap(v, s.at), where(state, s)).toBeGreaterThanOrEqual(Motion.BODY_RADIUS - 1);
    }

    [Fact(DisplayName = "car leaps > a jump on open ground and a vault are as they were with a car in the world")]
    public void OthersUnchanged()
    {
        var (state, s, v) = sedan(2);
        Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
        jump(state);
        expect(state.control.busy.what).toBe("jump");
        expect(state.control.busy.targetId.Split(',').Length).toBe(2);
        land(state);
        expect(Leaps.height(state, s, 1)).toBe(0);
    }
}
