// Car actions, lost limbs, startles and the pose intent (vehicleActions.ts, wounds.ts, body.ts,
// poseIntents.ts): what the renderer needs from the sim (docs/characters.md sections 1 and 5).
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
using LoonieRoo.Sim.Tests.Ported;
namespace LoonieRoo.Sim.Tests;

public class PoseTest
{
    static void run(SimState state, double n) => VehiclesTest.run(state, n);

    [Fact(DisplayName = "car actions > getting in takes a moment, with progress and the driver's door as its anchor")]
    public void GettingInTakesTime()
    {
        var f = VehiclesTest.field();
        expect(Commands.applyCommand(f.state, new Command { type = "use", x = 3, y = 3 }).ok).toBe(true);
        expect(Vehicles.vehicleOf(f.state, f.s)).toBeUndefined();
        run(f.state, Vehicles.GET_IN_TICKS / 2);
        var p = PoseIntents.ofSurvivor(f.state, f.s);
        expect(p.action).toBe("getIn");
        expect(p.anchor).toBe("driverDoor");
        expect(p.target).toBe(f.car.id);
        expect(p.progress).toBeGreaterThan(0.4);
        expect(p.progress).toBeLessThan(0.6);
        run(f.state, Vehicles.GET_IN_TICKS);
        expect(Vehicles.vehicleOf(f.state, f.s)).toBe(f.car);
        var seated = PoseIntents.ofSurvivor(f.state, f.s);
        expect(seated.stance).toBe("sit");
        expect(seated.action).toBeUndefined();
    }

    [Fact(DisplayName = "car actions > walking off stops you getting in")]
    public void WalkingOffCancels()
    {
        var f = VehiclesTest.field();
        Commands.applyCommand(f.state, new Command { type = "use", x = 3, y = 3 });
        run(f.state, 5);
        Commands.applyCommand(f.state, new Command { type = "steer", dx = -1, dy = 0 });
        run(f.state, Vehicles.GET_IN_TICKS);
        expect(Vehicles.vehicleOf(f.state, f.s)).toBeUndefined();
        expect(f.state.control.busy).toBeNull();
    }

    [Fact(DisplayName = "car actions > the boot and the bonnet open and shut, each taking its time")]
    public void BootAndBonnet()
    {
        var f = VehiclesTest.field();
        expect(Commands.applyCommand(f.state, new Command { type = "boot" }).ok).toBe(true);
        expect(PoseIntents.ofSurvivor(f.state, f.s).anchor).toBe("boot");
        expect(f.car.bootOpen).toBe(false);
        run(f.state, Vehicles.BOOT_TICKS);
        expect(f.car.bootOpen).toBe(true);
        Commands.applyCommand(f.state, new Command { type = "hood" });
        run(f.state, Vehicles.HOOD_TICKS);
        expect(f.car.hoodOpen).toBe(true);
        Commands.applyCommand(f.state, new Command { type = "boot" });
        expect(f.state.control.busy.what).toBe("closeBoot");
        run(f.state, Vehicles.BOOT_TICKS);
        expect(f.car.bootOpen).toBe(false);
        // Nowhere near a car: refused.
        f.s.at = Motion.centre(new Point { x = 30, y = 6 });
        f.s.pos = new Point { x = 30, y = 6 };
        expect(Commands.applyCommand(f.state, new Command { type = "boot" }).ok).toBe(false);
    }

    [Fact(DisplayName = "car actions > anchors stand round the car: the door on its left, the boot behind, the bonnet ahead")]
    public void Anchors()
    {
        var f = VehiclesTest.field();  // facing east (heading 0)
        var door = Vehicles.anchorAt(f.car, "driverDoor");
        var boot = Vehicles.anchorAt(f.car, "boot");
        var hood = Vehicles.anchorAt(f.car, "hood");
        expect(door.at.y).toBeLessThan(f.car.at.y);  // north: an east-facing car's left
        expect(boot.at.x).toBeLessThan(f.car.at.x);
        expect(hood.at.x).toBeGreaterThan(f.car.at.x);
        expect(door.facing).toBe(8);   // facing south, at the car
        expect(boot.facing).toBe(0);
        expect(hood.facing).toBe(16);
        foreach (var a in new[] { door, boot, hood })
            expect(Vehicles.within(f.car, a.at, Vehicles.ENTER_REACH)).toBe(true);   // within reach of its body, and clear of it
        foreach (var a in new[] { door, boot, hood })
            expect(Vehicles.gap(f.car, a.at)).toBeGreaterThanOrEqual(Motion.BODY_RADIUS);
    }

    [Fact(DisplayName = "lost limbs > an amputation says which side and where, and the pose limps on it")]
    public void LostLimbs()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s"] });
        var s = Js.NN(Js.Get(state.survivors, 0));
        s.infection = 10;
        s.bitten = "leg";
        expect(Wounds.amputate(state, s)).toBeNull();
        expect(s.lostLimbs.length).toBe(1);
        expect(s.lostLimbs[0]).toMatch(new JsRegExp("^leg(Left|Right):knee$", ""));
        var p = PoseIntents.ofSurvivor(state, s);
        expect(p.lostLimbs).toEqual(s.lostLimbs);
        expect(s.lostLimbs[0].Contains("Left") ? p.limpLeft : p.limpRight).toBe(1);
        // The other leg, if the same side comes up again.
        var again = Wounds.lostLimb(state, s, "leg");
        expect(again.Contains(s.lostLimbs[0].Contains("Left") ? "Right" : "Left")).toBe(true);
    }

    [Fact(DisplayName = "startle > a blow startles, and the flinch fades in a second")]
    public void Startle()
    {
        var f = VehiclesTest.field();
        expect(PoseIntents.ofSurvivor(f.state, f.s).startle).toBe(0);
        var z = Zombies.addZombie(f.state, 1, 3, 200, "static");
        z.at = Motion.centre(z.pos);
        double? at = null;
        for (int i = 0; i < 200 && at == null; i++) { Tick.tick(f.state); at = f.s.startledAt; }
        expect(at).not.toBeNull();
        expect(PoseIntents.ofSurvivor(f.state, f.s).startle).toBeGreaterThan(0.8);
        f.state.zombies.Clear();
        run(f.state, PoseIntents.STARTLE_TICKS + 1);
        expect(PoseIntents.ofSurvivor(f.state, f.s).startle).toBe(0);
    }

    [Fact(DisplayName = "pose intent > walking has a speed in metres a second and a heading; a crawler crawls on no legs")]
    public void Locomotion()
    {
        var f = VehiclesTest.field();
        // Out in the open, away from the car (a parked car is in the way: you'd slide along it).
        f.s.pos = new Point { x = 12, y = 1 };
        f.s.at = Motion.centre(f.s.pos);
        Commands.applyCommand(f.state, new Command { type = "steer", dx = 1, dy = 1 });
        run(f.state, 5);
        var p = PoseIntents.ofSurvivor(f.state, f.s);
        expect(p.speed).toBeGreaterThan(0.5);
        expect(p.speed).toBeLessThan(3);
        expect(p.heading).toBeGreaterThan(0.6);  // down and to the right: ~45 deg
        expect(p.heading).toBeLessThan(1.0);
        var z = Zombies.addZombie(f.state, 20, 5);
        z.variant = "crawler";
        var zp = PoseIntents.ofZombie(f.state, z);
        expect(zp.stance).toBe("crawl");
        expect(zp.lostLimbs.length).toBe(2);
    }

    [Fact(DisplayName = "pose intent > running is said, the dead look at what they're after, one standing at its job works")]
    public void RunningLookingWorking()
    {
        var f = VehiclesTest.field();
        Commands.applyCommand(f.state, new Command { type = "steer", dx = 1, dy = 0, run = true });
        run(f.state, 3);
        expect(PoseIntents.ofSurvivor(f.state, f.s).running).toBe(true);
        Commands.applyCommand(f.state, new Command { type = "steer", dx = 1, dy = 0 });
        run(f.state, 1);
        expect(PoseIntents.ofSurvivor(f.state, f.s).running).toBe(false);
        // One of the dead after you: its look is where you are.
        var z = Zombies.addZombie(f.state, 20, 5);
        z.targetId = f.s.id;
        var zp = PoseIntents.ofZombie(f.state, z);
        expect(zp.lookAt.x).toBe(f.s.at.x);
        expect(zp.lookAt.y).toBe(f.s.at.y);
        z.targetId = null;
        expect(PoseIntents.ofZombie(f.state, z).lookAt).toBeNull();
        // Someone else at a job they hold, standing by its tile: the job's type is their action; walking off, none.
        f.state.control = null;
        var job = Jobs.addJob(f.state, "build", f.s.pos.x, f.s.pos.y, null, null);
        job.reservedBy = f.s.id;
        f.s.jobId = job.id;
        f.s.was = new Point { x = f.s.at.x, y = f.s.at.y };
        var p = PoseIntents.ofSurvivor(f.state, f.s);
        expect(p.action).toBe("build");
        expect(p.target).toBe(job.id);
        f.s.was = new Point { x = f.s.at.x - 40, y = f.s.at.y };
        expect(PoseIntents.ofSurvivor(f.state, f.s).action).toBeUndefined();
    }
}
