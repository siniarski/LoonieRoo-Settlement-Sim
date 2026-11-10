// World lighting (Lights.cs): room lights and their switches, porches, street lamps, headlights, the flashlight,
// fire; what is lit, and that it replays and saves.
#nullable disable warnings
using System.Text.Json;
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class LightsTest
{
    const double SEED = 5;
    const int SIZE = 256;

    /// <summary>A generated world with you in control.</summary>
    static (SimState state, Survivor you) World()
    {
        var state = Worldgen.createWorld(SEED, SIZE);
        var you = state.survivors[0];
        foreach (var k in Js.ObjectKeys(you.priorities)) you.priorities[k] = 0;
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = you.id });
        return (state, you);
    }

    /// <summary>Every house in the world's plan (lot, building index), in plan order.</summary>
    static List<(Lot lot, int bi)> Houses(SimState state)
    {
        var plan = Worldplan.planWorld(state.world.seed, state.world.size, state.settings.map);
        var list = new List<(Lot, int)>();
        foreach (var lot in plan.lots)
        {
            var layout = Lots.layoutLot(state.world.seed, lot);
            for (int bi = 0; bi < layout.buildings.Count; bi++)
                if (layout.buildings[bi].plan.kind == "house") list.Add((lot, bi));
        }
        return list;
    }

    /// <summary>A house with at least two rooms, its land generated.</summary>
    static (Lot lot, List<Lights.RoomLight> rooms) AHouse(SimState state)
    {
        foreach (var (lot, bi) in Houses(state))
        {
            var rooms = Lights.buildingRooms(state, lot, bi);
            if (rooms.Count < 2) continue;
            Worldgen.ensureGeneratedAround(state, new Point { x = rooms[0].ax, y = rooms[0].ay });
            return (lot, rooms);
        }
        throw new Exception("no house in the world");
    }

    static void Put(SimState state, Survivor you, int x, int y)
    {
        you.pos = new Point { x = x, y = y };
        you.at = Motion.centre(you.pos);
        you.was = you.at;
        you.move = null;
        you.path = [];
    }

    static void Night(SimState state)
    {
        while (!Time.isNight(state.tick)) state.tick += 10;
    }

    static void Day(SimState state)
    {
        while (Time.isNight(state.tick)) state.tick += 10;
    }

    static Point P(int x, int y) => new Point { x = x, y = y };

    static string Whole(SimState s) => JsonSerializer.Serialize(s, StateJson.Options);

    [Fact(DisplayName = "lights > a generated building's rooms follow its walls: where two rooms meet, a wall, door or window stands")]
    public void rooms_match_walls()
    {
        // Walls fall in ruins (Lots.drawBuilding's decay), so a few gaps are fine; a room turned or mirrored wrong would
        // put half its borders where no wall is.
        var (state, _) = World();
        var g = state.grid;
        int borders = 0, gaps = 0, houses = 0;
        foreach (var (lot, bi) in Houses(state).Take(12))
        {
            var rooms = Lights.buildingRooms(state, lot, bi);
            Worldgen.ensureGeneratedAround(state, new Point { x = rooms[0].ax, y = rooms[0].ay });
            houses++;
            foreach (var r in rooms)
                for (int y = r.y0; y <= r.y1; y++)
                    for (int x = r.x0; x <= r.x1; x++)
                    {
                        if (Lights.roomAt(state, x, y)?.key != r.key) continue;
                        foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
                        {
                            if ((Lights.roomAt(state, x + dx, y + dy)?.key ?? Lights.OUT) == r.key) continue;
                            borders++;
                            if (Grids.edgeKind(g, Grids.edgeBetween(g, x, y, x + dx, y + dy)) == Grids.EDGE_NONE && Grids.getTile(g, x + dx, y + dy) != "wall") gaps++;
                        }
                    }
        }
        expect(houses).toBeGreaterThan(5);
        expect(borders).toBeGreaterThan(200);
        expect((double)gaps / borders).toBeLessThan(0.15);
    }

    [Fact(DisplayName = "lights > by default a quarter of homes have one room left on, and every other switch is off")]
    public void default_switches()
    {
        var (state, _) = World();
        int homes = 0, leftOn = 0;
        foreach (var (lot, bi) in Houses(state))
        {
            var on = Lights.buildingRooms(state, lot, bi).Count((r) => Lights.switchedOn(state, r));
            expect(on).toBeLessThanOrEqual(1);
            homes++;
            leftOn += on;
        }
        expect(homes).toBeGreaterThan(10);
        expect((double)leftOn / homes).toBeGreaterThan(0.1);
        expect((double)leftOn / homes).toBeLessThan(0.45);
        var plan = Worldplan.planWorld(state.world.seed, state.world.size, state.settings.map);
        foreach (var lot in plan.lots)
        {
            var layout = Lots.layoutLot(state.world.seed, lot);
            for (int bi = 0; bi < layout.buildings.Count; bi++)
                if (layout.buildings[bi].plan.kind != "house")
                    expect(Lights.buildingRooms(state, lot, bi).Any((r) => Lights.switchedOn(state, r))).toBe(false);
        }
    }

    [Fact(DisplayName = "lights > a room is lit only at night, switched on and powered (the mains, or a generator in reach)")]
    public void room_lit_when_on_and_powered()
    {
        var (state, you) = World();
        var (_, rooms) = AHouse(state);
        var r = rooms.First((x) => !x.defaultOn);
        var inside = P(r.ax, r.ay);
        Put(state, you, r.ax, r.ay);
        Night(state);
        expect(Lights.roomLit(state, inside)).toBe(false);
        expect(Sight.sightRadius(state, you)).toBe(Sight.NIGHT_SIGHT);
        expect(Commands.applyCommand(state, new Command { type = "lightSwitch", x = r.ax, y = r.ay }).ok).toBe(true);
        expect(Lights.roomLit(state, inside)).toBe(true);
        expect(Lights.lightAt(state, inside).by.kind).toBe(Lights.ROOM);
        expect(Sight.sightRadius(state, you)).toBe(Sight.SIGHT_RADIUS);
        expect(Power.inLight(state, inside)).toBe(true);
        // Every tile of the room is lit by it; the other rooms aren't.
        foreach (var other in rooms.Where((x) => x.key != r.key && !x.defaultOn))
            expect(Lights.lightAt(state, P(other.ax, other.ay)).by?.space == r.key).toBe(false);
        // The mains go: dark, until a fuelled generator stands in reach.
        state.timeline.powerOffDay = 1;
        expect(Lights.roomLit(state, inside)).toBe(false);
        var gen = FurnitureMod.addFurniture(state, "generator", r.ax, r.ay + Power.POWER_RADIUS);
        expect(Lights.roomLit(state, inside)).toBe(false);
        gen.stored = 2;
        expect(Lights.roomLit(state, inside)).toBe(true);
        // Burning fuel for it.
        expect(Lights.loadsGenerator(state, gen)).toBe(true);
        Power.updatePower(state);
        expect(gen.loot).toBe(1);
        // By day nothing needs a light.
        Day(state);
        expect(Lights.roomLit(state, inside)).toBe(false);
        expect(Lights.lightAt(state, inside).day).toBe(true);
    }

    [Fact(DisplayName = "lights > the switch works from in the room or its doorway, is sparse, and is logged")]
    public void switch_rules()
    {
        var (state, you) = World();
        var (_, rooms) = AHouse(state);
        var r = rooms.First((x) => !x.defaultOn);
        // Far off: no.
        Put(state, you, r.ax + 20, r.ay + 20);
        var far = Commands.applyCommand(state, new Command { type = "lightSwitch", x = r.ax, y = r.ay });
        expect(far.ok).toBe(false);
        expect(far.reason).toBe("not in that room");
        // In a doorway of the room: yes. Find a tile outside the room across a door edge.
        var g = state.grid;
        Point door = null;
        for (int y = r.y0; y <= r.y1 && door == null; y++)
            for (int x = r.x0; x <= r.x1 && door == null; x++)
            {
                if (Lights.roomAt(state, x, y)?.key != r.key) continue;
                foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
                    if (Lights.roomAt(state, x + dx, y + dy)?.key != r.key && Grids.edgeKind(g, Grids.edgeBetween(g, x, y, x + dx, y + dy)) == Grids.EDGE_DOOR)
                    { door = P(x + dx, y + dy); break; }
            }
        expect(door != null).toBe(true);
        Put(state, you, door.x, door.y);
        var log = state.commandLog.Count;
        expect(Commands.applyCommand(state, new Command { type = "lightSwitch", x = r.ax, y = r.ay }).ok).toBe(true);
        expect(state.commandLog.Count).toBe(log + 1);
        expect(state.lightSwitches[r.key]).toBe(true);
        // Back to its default: nothing kept.
        expect(Commands.applyCommand(state, new Command { type = "lightSwitch", x = r.ax, y = r.ay }).ok).toBe(true);
        expect(state.lightSwitches).toBeNull();
        // Outdoors there's no switch.
        var open = Space.nearestTile(state.grid, P(r.ax, r.ay), (x, y) => Lights.roomAt(state, x, y) == null && Grids.getTile(g, x, y) == "floor");
        expect(Commands.applyCommand(state, new Command { type = "lightSwitch", x = open.x, y = open.y }).reason).toBe("no light switch there");
    }

    [Fact(DisplayName = "lights > a room you wall in yourself has a switch too (a generator powers it)")]
    public void walled_in_room()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["#####", "#.s.#", "#...#", "#####", "....."] });
        var you = state.survivors[0];
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = you.id });
        FurnitureMod.addFurniture(state, "generator", 0, 4).stored = 3;
        Night(state);
        expect(Lights.roomLit(state, P(2, 1))).toBe(false);
        expect(Commands.applyCommand(state, new Command { type = "lightSwitch", x = 2, y = 2 }).ok).toBe(true);
        expect(Lights.roomLit(state, P(1, 2))).toBe(true);
        expect(Sight.sightRadius(state, you)).toBe(Sight.SIGHT_RADIUS);
        // Outside the wall it's dark.
        expect(Lights.isLit(state, P(2, 4))).toBe(false);
        expect(Lights.near(state, 2.5, 2.5, 8).Count((s) => s.kind == Lights.ROOM)).toBe(1);
    }

    [Fact(DisplayName = "lights > porch lights and street lamps are on at night while the mains are on, outdoors only")]
    public void porches_and_street_lamps()
    {
        var (state, _) = World();
        var plan = Worldplan.planWorld(state.world.seed, state.world.size, state.settings.map);
        expect(plan.streets.Count).toBeGreaterThan(0);
        var st = plan.streets[0];
        double mx = st.eastWest ? (st.x0 + st.x1) / 2.0 : st.x0 + 1, my = st.eastWest ? st.y0 + 1 : (st.y0 + st.y1) / 2.0;
        Night(state);
        var near = Lights.near(state, mx, my, 40);
        var lamps = near.Where((s) => s.kind == Lights.STREET).ToList();
        expect(lamps.Count).toBeGreaterThan(0);
        expect(near.Any((s) => s.kind == Lights.PORCH)).toBe(true);
        var regions = Worldplan.regions(state.world.seed, state.world.size, state.settings.map);
        foreach (var l in lamps)
        {
            expect(regions.IsRoad((int)l.x, (int)l.y)).toBe(false);
            expect(l.space).toBe(Lights.OUT);
        }
        // Nearest first.
        for (int i = 1; i < near.Count; i++) expect(near[i].dist).toBeGreaterThanOrEqual(near[i - 1].dist);
        // A tile under a lamp is lit, by it.
        var lamp = lamps[0];
        Worldgen.ensureGeneratedAround(state, P((int)lamp.x, (int)lamp.y));
        var under = Lights.lightAt(state, P((int)lamp.x, (int)lamp.y));
        expect(under.level).toBeGreaterThanOrEqual(Lights.LIT);
        // By day, and once the mains are gone, none.
        state.timeline.powerOffDay = 1;
        expect(Lights.near(state, mx, my, 40).Any((s) => s.kind is Lights.STREET or Lights.PORCH)).toBe(false);
    }

    [Fact(DisplayName = "lights > headlights: the driver switches them; they light ahead, only with fuel and a sound car")]
    public void headlights()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [new string('.', 29) + "s", .. Enumerable.Repeat(new string('.', 30), 8)] });
        var you = state.survivors[0];
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = you.id });
        expect(Commands.applyCommand(state, new Command { type = "headlights" }).reason).toBe("not driving");
        var car = Vehicles.addVehicle(state, 10, 4, 0, 500, Vehicles.VEHICLE_HEALTH, "car");
        car.driverId = you.id;
        Night(state);
        expect(Commands.applyCommand(state, new Command { type = "headlights" }).ok).toBe(true);
        expect(car.lights).toBe(true);
        expect(Lights.isLit(state, P(18, 4))).toBe(true);
        expect(Lights.isLit(state, P(4, 4))).toBe(false);
        expect(Lights.isLit(state, P(14, 8))).toBe(false);
        car.fuel = 0;
        expect(Lights.isLit(state, P(18, 4))).toBe(false);
        car.fuel = 10;
        car.health = 0;
        expect(Lights.isLit(state, P(18, 4))).toBe(false);
        car.health = 100;
        expect(Commands.applyCommand(state, new Command { type = "headlights" }).ok).toBe(true);
        expect(car.lights).toBeNull();
        expect(Lights.isLit(state, P(18, 4))).toBe(false);
    }

    [Fact(DisplayName = "lights > a flashlight: switched on from the bag, a beam the way you aim; you fight better in it")]
    public void flashlight()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [new string('.', 29) + "s", .. Enumerable.Repeat(new string('.', 30), 8)] });
        var you = state.survivors[0];
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = you.id });
        Put(state, you, 5, 4);
        Night(state);
        expect(Commands.applyCommand(state, new Command { type = "flashlight" }).reason).toBe("no flashlight in your bag");
        var torch = Items.spawnItem(state, Lights.FLASHLIGHT, 5, 4);
        expect(Inventory.stow(state, you, torch)).toBeNull();
        Commands.applyCommand(state, new Command { type = "aim", x = 64, y = 0 });
        expect(Lights.isLit(state, P(10, 4))).toBe(false);
        expect(Commands.applyCommand(state, new Command { type = "useItem", itemId = torch.id }).ok).toBe(true);
        expect(torch.lit).toBe(true);
        expect(Lights.lightAt(state, P(10, 4)).by.kind).toBe(Lights.FLASHLIGHT);
        expect(Lights.isLit(state, P(1, 4))).toBe(false);
        expect(Power.inLight(state, you.pos)).toBe(true);
        // Turn round: the beam follows.
        Commands.applyCommand(state, new Command { type = "aim", x = -64, y = 0 });
        expect(Lights.isLit(state, P(1, 4))).toBe(true);
        expect(Lights.isLit(state, P(10, 4))).toBe(false);
        // The F-key command switches it off.
        expect(Commands.applyCommand(state, new Command { type = "flashlight" }).ok).toBe(true);
        expect(torch.lit).toBeNull();
        expect(Lights.isLit(state, P(1, 4))).toBe(false);
    }

    [Fact(DisplayName = "lights > flashlights turn up in some containers you search (a hash, no RNG)")]
    public void flashlight_finds()
    {
        int found = 0;
        for (int i = 0; i < 1000; i++)
            if (Lights.flashlightIn(new Furniture { id = $"f{i}", kind = "crate", table = "garage" })) found++;
        expect(found).toBeGreaterThan(60);
        expect(found).toBeLessThan(200);
        expect(Lights.flashlightIn(new Furniture { id = "f1", kind = "crate", table = "bathroom" })).toBe(false);
        var crate = Enumerable.Range(0, 1000).Select((i) => new Furniture { id = $"f{i}", kind = "crate", table = "garage" }).First(Lights.flashlightIn);
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.."] });
        var you = state.survivors[0];
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = you.id });
        var box = FurnitureMod.addFurniture(state, "crate", 1, 0);
        box.id = crate.id;
        box.table = "garage";
        box.loot = 1;
        state.control.busy = new ControlBusy { what = "search", targetId = box.id, progress = 0, duration = 1 };
        while (!Time.slowTick(state.tick)) state.tick++;
        ControlMod.updateBusy(state, you, state.control);
        expect(state.items.Any((i) => i.kind == Lights.FLASHLIGHT && i.inside == box.id)).toBe(true);
    }

    [Fact(DisplayName = "lights > stoves while cooking, burning heaters and fires give light, in their own space")]
    public void fire_light()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["#####......", "#...#......", "#...#......", "#####......", "..........."] });
        Night(state);
        var stove = FurnitureMod.addFurniture(state, "stove", 2, 1);
        expect(Lights.isLit(state, P(2, 2))).toBe(false);
        state.jobs.Add(new Job { id = "j1", type = "cook", x = 2, y = 1, furnitureId = stove.id, reservedBy = "s1", progress = 3 });
        expect(Lights.lightAt(state, P(2, 2)).by.kind).toBe(Lights.STOVE);
        expect(Lights.isLit(state, P(5, 2))).toBe(false);
        state.fires.Add(new Fire { x = 8, y = 2, left = 50 });
        expect(Lights.lightAt(state, P(9, 3)).by.kind).toBe(Lights.FIRE);
        expect(Lights.isLit(state, P(3, 2)) && Lights.lightAt(state, P(3, 2)).by.kind == Lights.FIRE).toBe(false);
        var heater = FurnitureMod.addFurniture(state, "heater", 8, 4);
        heater.stored = 2;
        state.weather = "blizzard";
        expect(Lights.near(state, 8.5, 4.5, 2).Any((s) => s.kind == Lights.HEATER)).toBe(true);
        state.weather = "clear";
        expect(Lights.heaterLit(state, heater)).toBe(Seasons.seasonOf(state.tick) == "winter");
    }

    [Fact(DisplayName = "lights > near() is capped and sorted; its cost follows the radius, not the map")]
    public void near_is_bounded()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = Enumerable.Repeat(new string('.', 40), 40).ToList() });
        Night(state);
        for (int i = 0; i < 100; i++) state.fires.Add(new Fire { x = i % 40, y = i / 40 * 3, left = 50 });
        var all = Lights.near(state, 20, 3, 100);
        expect(all.Count).toBe(Lights.MAX_NEAR);
        for (int i = 1; i < all.Count; i++) expect(all[i].dist).toBeGreaterThanOrEqual(all[i - 1].dist);
        expect(Lights.near(state, 20, 3, 2).Count).toBeLessThan(10);
    }

    [Fact(DisplayName = "lights > switches, the flashlight and headlights replay from the command log")]
    public void replays()
    {
        var (state, you) = World();
        var (_, rooms) = AHouse(state);
        var r = rooms.First((x) => !x.defaultOn);
        Put(state, you, r.ax, r.ay);
        var torch = Items.spawnItem(state, Lights.FLASHLIGHT, r.ax, r.ay);
        Inventory.stow(state, you, torch);
        var initial = States.cloneState(state);
        Commands.applyCommand(state, new Command { type = "lightSwitch", x = r.ax, y = r.ay });
        Tick.step(state, 3);
        Commands.applyCommand(state, new Command { type = "flashlight" });
        Tick.step(state, 2);
        Commands.applyCommand(state, new Command { type = "useItem", itemId = torch.id });
        Commands.applyCommand(state, new Command { type = "lightSwitch", x = r.ax, y = r.ay });
        Commands.applyCommand(state, new Command { type = "lightSwitch", x = r.ax, y = r.ay });
        Tick.step(state, 2);
        var replayed = Replay.replay(initial, state.commandLog, state.tick);
        expect(replayed.lightSwitches[r.key]).toBe(true);
        expect(replayed.items.First((i) => i.id == torch.id).lit).toBeNull();
        expect(Whole(replayed) == Whole(state)).toBe(true);
    }

    [Fact(DisplayName = "lights > a saved game with lights on loads the same; one with none saves no light fields")]
    public void saves()
    {
        var (state, you) = World();
        var (_, rooms) = AHouse(state);
        var r = rooms.First((x) => !x.defaultOn);
        var plain = Whole(state);
        expect(plain.Contains("lightSwitches")).toBe(false);
        expect(plain.Contains("\"lit\"")).toBe(false);
        expect(plain.Contains("\"lights\"")).toBe(false);
        Put(state, you, r.ax, r.ay);
        Commands.applyCommand(state, new Command { type = "lightSwitch", x = r.ax, y = r.ay });
        var torch = Items.spawnItem(state, Lights.FLASHLIGHT, r.ax, r.ay);
        Inventory.stow(state, you, torch);
        Commands.applyCommand(state, new Command { type = "flashlight" });
        var car = Vehicles.vehiclesOf(state)[0];
        car.lights = true;
        var back = Savefile.loadFile(Savefile.toSaveFile(state));
        expect(back.ok).toBe(true);
        state.commandLog = []; // a save keeps no command log (a bug report does)
        expect(Whole(back.state) == Whole(state)).toBe(true);
        expect(back.state.lightSwitches[r.key]).toBe(true);
        expect(back.state.items.First((i) => i.id == torch.id).lit).toBe(true);
        expect(Vehicles.vehiclesOf(back.state)[0].lights).toBe(true);
        Night(back.state);
        expect(Lights.roomLit(back.state, P(r.ax, r.ay))).toBe(true);
    }
}
