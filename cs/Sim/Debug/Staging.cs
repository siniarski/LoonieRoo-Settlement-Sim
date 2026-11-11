// Scenes set by hand: what the game's debug flags (godot/App/GameArgs.cs), benches and pictures ask for.
//
// None of this is a command: nothing here goes to state.commandLog, so a bug report doesn't replay it. It is for a
// start that was never played (a picture of snow at night, a fire next to you, a gun in your hand), set before the
// first tick is shown. It lives in the sim so the game never writes state itself (rule 5): the game turns a flag into
// one call here, and everything that plays is still a command.
//
// Everything is a pure function of the state and its arguments; no RNG is drawn (ids come from state.nextId, as
// for anything spawned).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static class Staging
{
    // --- the clock and the sky ---

    /// <summary>Start on day `day` at 06:00 (17 is the second day of winter; 17.33 is 14:00: a day's fraction counts
    /// from 06:00). Only the clock moves: no ticks run, and what has grown by then is grown (Growth.cs).</summary>
    public static void day(SimState state, double day)
    {
        Time.setDayScale(state.settings.dayScale);
        state.tick = (int)Math.Round((day - 1) * Time.DAY_LENGTH) + 1;
        Growth.update(state);
    }

    /// <summary>The clock jumped to the next `hour` o'clock (no ticks run: a night without a day before it).</summary>
    public static void hour(SimState state, int hour)
    {
        int guard = 0;
        while ((int)Math.Floor(Time.hourOf(state.tick)) != hour && guard++ < Time.DAY_LENGTH) state.tick++;
    }

    /// <summary>The clock set to a tick.</summary>
    public static void clock(SimState state, int tick) => state.tick = tick;

    /// <summary>The day's weather (clear, rain, storm, heatwave, snow, blizzard).</summary>
    public static void weather(SimState state, string weather) => state.weather = weather;

    /// <summary>That much snow (cm) on open ground in every climate region.</summary>
    public static void snow(SimState state, double cm) =>
        state.snow = new SnowState { depth = [cm, cm, cm], fallen = [0, 0, 0], melted = [0, 0, 0] };

    /// <summary>The mains gone (no porches, street lamps or mains-lit rooms).</summary>
    public static void mainsOff(SimState state)
    {
        if (state.timeline != null) state.timeline.powerOffDay = 0;
    }

    // --- you ---

    /// <summary>Put a survivor on a tile, standing still.</summary>
    public static void place(Survivor s, int x, int y)
    {
        s.pos = new Point(x, y);
        s.at = Motion.centre(s.pos);
        s.was = Motion.centre(s.pos);
    }

    /// <summary>A weapon in their hand: a gun with `rounds` in it (else 60% of its magazine) and a box of its
    /// ammunition in their bag. Null when the kind is no weapon to wield.</summary>
    public static Item? arm(SimState state, Survivor s, string kind, double? rounds = null)
    {
        var weapon = Items.spawnItem(state, kind, s.pos.x, s.pos.y);
        if (Weapons.specOf(weapon) is not { } spec || !Weapons.isWieldKind(weapon.kind)) return null;
        weapon.pos = null;
        weapon.carriedBy = s.id;
        s.weapon = weapon.id;
        if (spec.isFirearm)
        {
            weapon.wear = rounds ?? Math.Ceiling(spec.magazine * 0.6);
            Inventory.stow(state, s, Items.spawnItem(state, spec.ammo!, s.pos.x, s.pos.y));
        }
        return weapon;
    }

    /// <summary>A flashlight in their bag, switched on or not.</summary>
    public static Item giveFlashlight(SimState state, Survivor s, bool on)
    {
        var item = Items.spawnItem(state, Lights.FLASHLIGHT, s.pos.x, s.pos.y);
        Inventory.stow(state, s, item);
        if (on) item.lit = true;
        return item;
    }

    // --- lights and fire ---

    /// <summary>Flip the light switch of the room at a tile. False when there is no room there.</summary>
    public static bool flipSwitch(SimState state, int x, int y)
    {
        var room = Lights.roomAt(state, x, y);
        if (room == null) return false;
        state.lightSwitches ??= new JsRecord<bool>();
        state.lightSwitches[room.key] = !Lights.switchedOn(state, room);
        return true;
    }

    /// <summary>A car (or a "truck") at a tile, heading 0-31 (0 east, 8 south), headlights on, a full tank.</summary>
    public static Vehicle car(SimState state, int x, int y, int heading = 0, string kind = "car")
    {
        var v = Vehicles.addVehicle(state, x, y, heading, Vehicles.FUEL_MAX, Vehicles.VEHICLE_HEALTH, kind);
        v.lights = true;
        return v;
    }

    /// <summary>Someone behind the wheel of the nearest car (within a few tiles), without the getting in. Its tank
    /// and its health at those percentages and its speed in km/h, where given. Null when there is no car near.</summary>
    public static Vehicle? seat(SimState state, Survivor s, double? fuel = null, double? health = null, double? kmh = null)
    {
        var v = Vehicles.vehicleNear(state, s.at, 8 * Motion.SUB);
        if (v == null) return null;
        Vehicles.enterVehicle(state, s, v);
        if (fuel != null) v.fuel = Math.Round(Vehicles.FUEL_MAX * fuel.Value / 100);
        if (health != null) v.health = Math.Round(Vehicles.VEHICLE_HEALTH * health.Value / 100);
        if (kmh != null) v.speed = Math.Round(kmh.Value / 3.6 * Motion.SUB / Tick.TICKS_PER_SECOND);
        return v;
    }

    /// <summary>Someone standing on top of the nearest car (within a few tiles), on its middle: pictures from a roof.
    /// Null when there is no car near, or no body fits on it.</summary>
    public static Vehicle? onRoof(SimState state, Survivor s)
    {
        var v = Vehicles.vehicleNear(state, s.at, 8 * Motion.SUB);
        return v != null && Leaps.standOn(state, s, v) ? v : null;
    }

    /// <summary>A fire burning at a tile, whatever the ground: wildfire where it would catch, else a plain fire.</summary>
    public static void fire(SimState state, int x, int y)
    {
        if (!FireMod.ignite(state, x, y)) state.fires.Add(new Fire { x = x, y = y, left = 600 });
    }

    /// <summary>Tiles alight at offsets (dx, dy pairs) from someone: a line of grass fire is 4,-3, 4,-2, 4,-1, 4,0.</summary>
    public static void burn(SimState state, Survivor from, IReadOnlyList<int> offsets)
    {
        for (int i = 0; i + 1 < offsets.Count; i += 2) FireMod.ignite(state, from.pos.x + offsets[i], from.pos.y + offsets[i + 1]);
    }

    /// <summary>A stove at a tile with `cook` cooking at it (so it glows).</summary>
    public static void stove(SimState state, Survivor cook, int x, int y)
    {
        var f = FurnitureMod.addFurniture(state, "stove", x, y);
        state.jobs.Add(new Job { id = $"j{state.nextId++}", type = "cook", x = x, y = y, furnitureId = f.id, reservedBy = cook.id, progress = 1 });
    }

    /// <summary>A fuelled heater at a tile, and a blizzard for it to burn in.</summary>
    public static void heater(SimState state, int x, int y)
    {
        FurnitureMod.addFurniture(state, "heater", x, y).stored = 3;
        state.weather = "blizzard";
    }

    // --- lineups: one of everything, in rows, for pictures of the models ---

    /// <summary>The kinds --lineup lays out, one of each.</summary>
    public static readonly string[] LINEUP_ITEMS = ["wood", "food", "meal", "bandage", "medicine", "medkit", "weapon", "cloth", "coat", "armour", "boots",
        "gun", "ammo", "corpse", "book", "cart", "axe", "hammer", "sample", "scrap", "fuel", "knife", "hatchet", "machete", "crowbar", "bat", "pipe", "sledge",
        "spear", "plank", "pistol", "revolver", "shotgun", "rifle", "crossbow", "ammo38", "shells", "ammo308", "bolts", "flashlight"];

    /// <summary>The kinds with several models (--lineup=variants): a kind to a row, its first four models eastward.</summary>
    public static readonly string[] LINEUP_VARIANTS = ["food", "wood", "medicine", "bandage", "weapon", "cloth", "book", "scrap", "coat", "boots", "ammo", "meal"];

    /// <summary>The weapons and their ammunition (--lineup=weapons): melee first, then guns, then boxes.</summary>
    public static readonly string[] LINEUP_WEAPONS = ["knife", "hatchet", "machete", "crowbar", "bat", "pipe", "sledge", "spear", "plank", "hammer",
        "axe", "pistol", "revolver", "shotgun", "rifle", "crossbow", "ammo", "ammo38", "shells", "bolts"];

    /// <summary>Items on the ground east of someone, for a picture of their models. `which` is "" (one of every kind,
    /// rows of three southward), "variants" (the kinds with several models, a kind to a row and four of each) or
    /// "weapons" (two rows of ten). Each comes with the model variant it should be shown as (the renderer picks by
    /// hash otherwise, and a lineup wants all of them seen).</summary>
    public static List<(Item item, int variant)> itemLineup(SimState state, Survivor you, string which)
    {
        var shown = new List<(Item, int)>();
        if (which == "variants")
        {
            for (int k = 0; k < LINEUP_VARIANTS.Length; k++)
                for (int v = 0; v < 4; v++)
                    shown.Add((Items.spawnItem(state, LINEUP_VARIANTS[k], you.pos.x + 2 + v, you.pos.y - LINEUP_VARIANTS.Length / 2 + k), v));
        }
        else if (which == "weapons")
        {
            for (int k = 0; k < LINEUP_WEAPONS.Length; k++)
                shown.Add((Items.spawnItem(state, LINEUP_WEAPONS[k], you.pos.x + 2 + k % 10, you.pos.y - 1 + k / 10 * 2), 0));
        }
        else
        {
            for (int k = 0; k < LINEUP_ITEMS.Length; k++)
                shown.Add((Items.spawnItem(state, LINEUP_ITEMS[k], you.pos.x + 2 + k / 3, you.pos.y - 1 + k % 3), 0));
        }
        return shown;
    }

    /// <summary>One of every furniture kind and look in the open east of someone (the land round them generated
    /// first, so they can stand anywhere): rows of eight, three tiles apart, the big ones (vehicles, the silo, the
    /// tower) in a row of their own beyond.</summary>
    public static void furnitureLineup(SimState state, Survivor you)
    {
        Worldgen.ensureGeneratedAround(state, you.pos);
        string[] kinds = ["bed", "table", "workbench", "stove", "trap", "well", "collector", "generator", "lamp", "fridge",
            "heater", "coop", "radio", "lab", "beacon", "turret", "lure", "pit", "crate"];
        var looks = new List<(string kind, string? look)>();
        foreach (var k in kinds) looks.Add((k, null));
        foreach (var l in FurnitureMod.CONTAINER_LOOKS) if (l != "crate") looks.Add(("crate", l));
        foreach (var l in FurnitureMod.DECOR_LOOKS) if (l is not ("bed" or "table" or "car" or "tractor" or "silo")) looks.Add(("decor", l));
        for (int k = 0; k < looks.Count; k++)
        {
            var f = FurnitureMod.addFurniture(state, looks[k].kind, you.pos.x + 3 + 3 * (k % 8), you.pos.y - 6 + 3 * (k / 8));
            f.look = looks[k].look;
        }
        int y = you.pos.y - 6 + 3 * ((looks.Count + 7) / 8) + 2;
        (string kind, string? look, int dx)[] big = [("truck", null, 3), ("decor", "car", 10), ("decor", "tractor", 16), ("tower", null, 21), ("decor", "silo", 25)];
        foreach (var (kind, look, dx) in big)
        {
            int n = look == "silo" ? 2 : 1;
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                    FurnitureMod.addFurniture(state, kind, you.pos.x + dx + a, y + b).look = look;
        }
    }

    // --- scenes of the things only the Colony tab used to draw (for pictures of their models) ---

    /// <summary>A farm east of someone: a row for each crop (potato, wheat, herb), from a bare bed through sprouting
    /// and growing to ripe, two plots of each.</summary>
    public static void farm(SimState state, Survivor you)
    {
        string[] crops = ["potato", "wheat", "herb"];
        for (int row = 0; row < crops.Length; row++)
            for (int k = 0; k < 8; k++)
            {
                var plot = Farming.addPlot(state, you.pos.x + 2 + k, you.pos.y - 1 + row, crops[row]);
                int stage = k / 2; // 0 bare, 1 sprouting, 2 growing, 3 ripe
                plot.planted = stage > 0;
                plot.growth = stage == 0 ? 0 : Farming.growTicks(plot) * (stage == 1 ? 0.15 : stage == 2 ? 0.7 : 1);
            }
    }

    /// <summary>Building sites east of someone: a bed, a floor and a wall tile planned; then, a tile further each, a
    /// wall, a door and a barricade on a border, each planned, with its material delivered, a third done and nearly
    /// done.</summary>
    public static void sites(SimState state, Survivor you)
    {
        int x = you.pos.x + 2, y = you.pos.y;
        Designations.addBlueprint(state, x, y - 2, "bed");
        Designations.addBlueprint(state, x + 1, y - 2, "floor").workDone = 5;
        Designations.addBlueprint(state, x + 2, y - 2, "floor");
        Designations.addBlueprint(state, x + 3, y - 2, "workbench").delivered = true;
        string[] targets = ["wall", "door", "barricade"];
        for (int row = 0; row < targets.Length; row++)
            for (int k = 0; k < 4; k++)
            {
                var bp = Designations.addBlueprint(state, x + k, y + row, targets[row], row == 2 ? "w" : "n");
                bp.delivered = k >= 1;
                bp.workDone = bp.workRequired * (k == 2 ? 0.33 : k == 3 ? 0.9 : 0);
            }
    }

    /// <summary>Those who come by, east of someone: a wanderer, a trader with goods laid out, a prisoner, a caravan
    /// with its handcart and a neighbours' scavenging party. They stay as long as the sim lets them (a caravan and a
    /// run head for where they were going on the next step).</summary>
    public static void visitors(SimState state, Survivor you)
    {
        int x = you.pos.x + 3, y = you.pos.y;
        double far = state.tick + 100000;
        state.wanderers.Add(new Wanderer { id = $"w{state.nextId++}", name = "Wanderer", x = x, y = y - 3, leavesTick = far });
        state.traders.Add(new Trader { id = $"t{state.nextId++}", x = x, y = y - 1, leavesTick = far });
        state.prisoners.Add(new Prisoner { id = $"q{state.nextId++}", name = "Prisoner", x = x, y = y + 1, since = state.tick });
        state.caravans ??= [];
        state.caravans.Add(new Caravan { id = $"c{state.nextId++}", from = "", to = "you", x = x + 2, y = y - 2, dest = new Point(x + 2, y - 2), people = 3, nextStepTick = far });
        state.runs ??= [];
        state.runs.Add(new ScavRun { id = $"sr{state.nextId++}", from = "", x = x + 2, y = y + 2, home = new Point(x + 2, y + 2), ruin = new Point(x + 2, y + 2), people = 3, phase = "out", nextStepTick = far, untilTick = far });
    }

    /// <summary>The dead getting over things (Crossings.cs), four tiles east of someone looking east: a sedan and a
    /// pickup side on (noses south), a barricade on the border between them, and the dead coming over from the far
    /// side, each put part-way through its crossing: on the sedan a walker coming down off the bonnet, a screamer
    /// on the roof and a crawler hauled onto the boot; a runner in the air over the barricade; on the pickup a
    /// brute on the cab and a walker on the bed, and a crawler going in under its nose. They go on from there
    /// (hold them with --paused). Wants open ground (nothing is cleared: one whose tiles aren't open is left out).</summary>
    public static void climb(SimState state, Survivor you)
    {
        Worldgen.ensureGeneratedAround(state, you.pos);
        var grid = state.grid;
        int x = you.pos.x + 4, y = you.pos.y;
        car(state, x, y - 3, 8).lights = null;
        car(state, x, y + 3, 8, "truck").lights = null;
        Grids.setEdge(grid, x + 1, y, "w", Grids.EDGE_BARRICADE);
        state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
        void one(string variant, int row, int ticks)
        {
            if (!Pathfinding.isPassable(grid, x + 1, y + row, Pathfinding.ZOMBIE) || !Pathfinding.isPassable(grid, x - 1, y + row, Pathfinding.ZOMBIE)) return;
            var z = Zombies.addZombie(state, x + 1, y + row, Zombies.ZOMBIE_HEALTH, "static");
            z.variant = variant;
            // Its way is given, not planned: straight over, whatever round would cost.
            z.path = [new Point(x, y + row), new Point(x - 1, y + row)];
            z.nextThinkTick = state.tick + 100000;
            Crossings.stage(state, z, ticks);
        }
        one("walker", -2, 30);
        one("screamer", -4, 16);
        one("crawler", -5, 30);
        one("runner", 0, 4);
        one("brute", 3, 30);
        one("walker", 1, 12);
        one("crawler", 5, 3);
    }

    /// <summary>The helicopter hanging over a tile a little way off, staying (the timeline's own comes on a rolled day).</summary>
    public static void helicopter(SimState state, int x, int y)
    {
        if (state.timeline == null) return; // a world with no timeline (a scenario) has no helicopter
        state.timeline.heli = new TimelineHeli { x = x, y = y, leaveTick = state.tick + 100000, exit = new Point(x, y) };
    }
}
