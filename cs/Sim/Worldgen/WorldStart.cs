// Starting a world, part of Worldgen (Worldgen.cs): createWorld makes the state, generates the chunks round the
// start, builds the start camp (startCamp: a shelter, beds, the survivors at its door, wood, food, a rain
// collector), sets a few of the dead walking near it (startStrays), rolls the timeline and the settlements, parks
// the truck and reveals the start.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Worldgen
{
    /// <summary>A new generated world; `you`, if given, becomes the first survivor (CharacterMod.cs).</summary>
    public static SimState createWorld(double seed, double size = WORLD_SIZE, Settings? settings = null, Character? you = default)
    {
        settings ??= Difficulty.NORMAL;
        var state = States.createState(new NewStateOptions { seed = seed, width = Js.I(size), height = Js.I(size) });
        state.settings = Js.CopyAs<Settings>(settings);
        var l = layout(seed, size, settings.map);
        var chunks = JsMath.ceil(((double)(size) / (CHUNK)));
        state.world = new WorldInfo { seed = seed, size = size, chunks = chunks, generated = Js.Filled<double>(((double)(chunks) * (chunks)), 0), gen = GEN_VERSION };
        ensureGeneratedAround(state, l.start);
        startCamp(state, l);
        startStrays(state, l);
        if (Js.T(you) && (CharacterMod.characterProblem(you) == null))
        {
            CharacterMod.applyCharacter(state, Js.NN(Js.Get(state.survivors, 0)), you);
        }
        state.harsh = true;
        TimelineMod.rollTimeline(state, seed);
        Settlements.placeSettlements(state, l.start, seed);
        var angle = ((double)(((double)(Hash.hashRoll(seed, 0x7a0c)) * (JsMath.PI))) * (2));
        Point near = new Point { x = Js.I(JsMath.round(l.start.x + ((double)(JsMath.cos(angle)) * (35)))), y = Js.I(JsMath.round(l.start.y + ((double)(JsMath.sin(angle)) * (35)))) };
        Point clamped = new Point { x = Js.I(JsMath.max(1, JsMath.min(size - 2, near.x))), y = Js.I(JsMath.max(1, JsMath.min(size - 2, near.y))) };
        var spot = Space.nearestTile(state.grid, clamped, (x, y) => ((Grids.getTile(state.grid, x, y) == "floor") && !Js.T(FurnitureMod.furnitureAt(state, x, y))) && !Js.T(Items.itemAt(state, x, y)));
        // The truck stands where its whole body fits on the heading it was left at (VehicleBody.cs), within a short walk of the spot.
        var truckHeading = JsMath.floor(((double)(Hash.hashRoll(seed, 0x7a0d)) * (32)));
        var park = (Js.T(spot) ? (Vehicles.parkable(state, spot, truckHeading, "truck") ? spot : Space.nearestTile(state.grid, spot, (x, y) => Vehicles.parkable(state, new Point { x = Js.I(x), y = Js.I(y) }, truckHeading, "truck"), 2000)) : null);
        if (Js.T(park))
        {
            Vehicles.addVehicle(state, park.x, park.y, truckHeading, ((double)(Vehicles.FUEL_MAX) / (2)), 700, "truck");
        }
        Grids.fillExplored(state.grid, 0);
        for (var y = l.start.y - START_REVEAL_RADIUS; y <= (l.start.y + START_REVEAL_RADIUS); y++)
        {
            for (var x = l.start.x - START_REVEAL_RADIUS; x <= (l.start.x + START_REVEAL_RADIUS); x++)
            {
                if (Grids.inBounds(state.grid, x, y) && NatureMod.nearStart(l, x, y, START_REVEAL_RADIUS + 1))
                {
                    Grids.setExplored(state.grid, Grids.tileIndex(state.grid, x, y), 1);
                }
            }
        }
        return state;
    }

    /// <summary>The start: an intact shelter (edge walls, a door, windows, a little furniture) with three beds (one for a
    /// solo start), the survivors outside its door, and some wood and food.</summary>
    public static void startCamp(SimState state, Layout l)
    {
        var __d1 = l.start;
        var sx = __d1.x;
        var sy = __d1.y;
        var x0 = sx - 4;
        var y0 = sy - 7;
        // The shelter: an ordinary edge-walled building (Buildings.startPlan), whole (no decay), its containers empty.
        var plan = Buildings.startPlan();
        for (var y = 0; y < plan.h; y++)
        {
            for (var x = 0; x < plan.w; x++)
            {
                ground(state, x0 + x, y0 + y, plan.ground[y * plan.w + x] ?? "boards");
            }
        }
        foreach (var e in plan.edges)
        {
            var (ox, oy, side) = Lots.edgeOwner(new Point(x0 + e.ax, y0 + e.ay), new Point(x0 + e.bx, y0 + e.by));
            Grids.setEdge(state.grid, ox, oy, side, e.window == true ? Grids.EDGE_WINDOW : e.door ? Grids.EDGE_DOOR : Grids.EDGE_WALL);
        }
        foreach (var f in plan.furniture)
        {
            var piece = FurnitureMod.addFurniture(state, Buildings.isContainerLook(f.look) ? "crate" : "decor", x0 + f.x, y0 + f.y);
            piece.look = f.look;
            piece.table = f.table;
            piece.loot = 0;
        }
        var solo = state.settings.start == "solo";
        foreach (var dx in Js.Iter(solo ? ((List<double>)[3]) : ((List<double>)[1, 3, 5])))
        {
            FurnitureMod.addFurniture(state, "bed", x0 + dx, y0);
        }
        foreach (var dx in Js.Iter(solo ? ((List<double>)[0]) : ((List<double>)[-2, 0, 2])))
        {
            var s = Survivors.addSurvivor(state, sx + dx, sy + 1);
            s.traits = Skills.rollTraits(s.id);
        }
        if (solo)
        {
            var you = Js.NN(Js.Get(state.survivors, 0));
            state.control = new Control { survivorId = you.id, dx = 0, dy = 0, facing = new Point { x = Js.I(0), y = Js.I(1) }, nextSwing = 0, busy = null };
            var weapon = Items.spawnItem(state, "weapon", you.pos.x, you.pos.y);
            weapon.pos = null;
            weapon.carriedBy = you.id;
            you.weapon = weapon.id;
            Inventory.readyQuick(state, you);  // on the hotbar from the start
            Items.spawnItem(state, "bandage", you.pos.x, you.pos.y);
        }
        for (var i = 0; i < (solo ? 4 : 12); i++)
        {
            Items.spawnItem(state, "wood", (sx - 6) + (i % 4), (sy + 3) + JsMath.floor(((double)(i) / (4))));
        }
        for (var i = 0; i < (solo ? 4 : 10); i++)
        {
            Items.spawnItem(state, "food", (sx + 3) + (i % 4), (sy + 3) + JsMath.floor(((double)(i) / (4))));
        }
        var c = Space.nearestTile(state.grid, new Point { x = Js.I(sx + 5), y = Js.I(sy + 1) }, (x, y) => ((Grids.getTile(state.grid, x, y) == "floor") && !Js.T(FurnitureMod.furnitureAt(state, x, y))) && !Js.T(Items.itemAt(state, x, y)));
        if (Js.T(c))
        {
            FurnitureMod.addFurniture(state, "collector", c.x, c.y).loot = START_WATER;
        }
    }

    /// <summary>Water in the start camp's rain collector.</summary>
    public const int START_WATER = 12;

    /// <summary>The dead already up and about when the world is made, at 100% zombies: within sight of the start but outside its calm (START_STRAY_NEAR to START_STRAY_FAR tiles).</summary>
    public const int START_STRAYS = 3;

    public const int START_STRAY_NEAR = 22;

    public const int START_STRAY_FAR = 34;

    /// <summary>Awake static zombies round the start (stateless rolls from the seed), on open floor out of the start's calm. They wander till they notice someone.</summary>
    public static void startStrays(SimState state, Layout l)
    {
        var n = Difficulty.zombieScale(state, START_STRAYS);
        for (var k = 0; k < n; k++)
        {
            var angle = ((double)(((double)(Hash.hashRoll(l.seed, 0x7a20, k)) * (JsMath.PI))) * (2));
            var r = START_STRAY_NEAR + ((double)(Hash.hashRoll(l.seed, 0x7a21, k)) * (START_STRAY_FAR - START_STRAY_NEAR));
            Point aim = new Point { x = Js.I(JsMath.round(l.start.x + ((double)(JsMath.cos(angle)) * (r)))), y = Js.I(JsMath.round(l.start.y + ((double)(JsMath.sin(angle)) * (r)))) };
            if (!Grids.inBounds(state.grid, aim.x, aim.y))
            {
                continue;
            }
            var p = Space.nearestTile(state.grid, aim, (x, y) => ((Grids.getTile(state.grid, x, y) == "floor") && !Js.T(FurnitureMod.furnitureAt(state, x, y))) && !NatureMod.nearStart(l, x, y, START_STRAY_NEAR), 200);
            if (!Js.T(p))
            {
                continue;
            }
            ZombieKinds.giveVariant(Zombies.addZombie(state, p.x, p.y, Zombies.ZOMBIE_HEALTH, "static"), 1);
        }
    }
}
