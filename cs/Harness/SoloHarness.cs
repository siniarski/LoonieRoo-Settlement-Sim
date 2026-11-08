// Ported from harness/solo.harness.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Harness.Node;
namespace LoonieRoo.Harness;

public partial class SoloHarness
{
    static SoloHarness()
    {
        mkdirSync("cs/Harness/out", new() { recursive = true });
    }

    public static CommandResult cmd(SimState s, Command c)
    {
        return Commands.applyCommand(s, c);
    }

    public static readonly List<string> @out = [];

    public static readonly string OUT = process.env.OUT ?? "cs/Harness/out/solo.txt";

    public static /*TODO return type*/object flush()
    {
        return writeFileSync(OUT, @out.join("\n") + "\n");
    }

    public static readonly double BUDGET_MS = Js.Num(Js.Q(process.env.BUDGET_MS, 300000));

    public static readonly Settings SETTINGS = Js.Get(Difficulty.PRESETS, process.env.PRESET ?? "apocalypse").settings;

    public static double dist(Point a, Point b)
    {
        return JsMath.max(JsMath.abs(a.x - b.x), JsMath.abs(a.y - b.y));
    }

    /// <summary>Walk a path to a tile next to (or onto) a target, and use it on arrival. You move freely (motion.ts): steer toward the centre of the next tile on the path, opening a closed door that's next in the way (a colonist's path goes through doors; you have to open them).</summary>
    public static void goUse(SimState state, Survivor s, Bot bot, string key, Point target, bool onto = false, string act = "use")
    {
        var c = Js.NN(state.control);
        void stop()
        {
            if ((c.dx != 0) || (c.dy != 0))
            {
                cmd(state, new Command { type = "steer", dx = 0, dy = 0 });
            }
        }
        var at = s.pos;
        var here = Motion.centre(at);
        var settled = (JsMath.abs(s.at.x - here.x) <= ((double)(Motion.SUB) / (4))) && (JsMath.abs(s.at.y - here.y) <= ((double)(Motion.SUB) / (4)));
        if (Js.T(Js.T(onto) ? ((at.x == target.x) && (at.y == target.y) ? (object?)(settled) : false) : ((JsMath.abs(at.x - target.x) + JsMath.abs(at.y - target.y)) <= 1)))
        {
            stop();
            if (!Js.T(c.busy))
            {
                cmd(state, new Command { type = act, x = Js.I(target.x), y = Js.I(target.y) });
            }
            return;
        }
        if (((bot.pathFor != key) || (bot.path.length == 0)) || ((state.tick % 40) == 0))
        {
            bot.path = Pathfinding.findPathToAny(state.grid, at, Js.T(onto) ? [target] : Grids.neighbours4(state.grid, target.x, target.y)) ?? [];
            bot.pathFor = key;
        }
        var i = bot.path.findIndex((p) => (p.x == at.x) && (p.y == at.y));
        if (i >= 0)
        {
            bot.path.splice(0, i + 1);
        }
        var next = Js.Get(bot.path, 0);
        if (!Js.T(next))
        {
            { stop(); return; }
        }
        var edge = Grids.edgeBetween(state.grid, at.x, at.y, next.x, next.y);
        var shutEdgeDoor = ((edge >= 0) && (Js.Get(state.grid.edges, edge) == Grids.EDGE_DOOR)) && !Grids.edgeIsOpen(state.grid, edge);
        if (Js.T(Js.T(shutEdgeDoor) || ((Grids.getTile(state.grid, next.x, next.y) == "door") && !Grids.isOpen(state.grid, next.x, next.y))))
        {
            stop();
            cmd(state, new Command { type = "use", x = Js.I(next.x), y = Js.I(next.y) });
            return;
        }
        var goal = Motion.centre(next);
        var dx = (JsMath.abs(goal.x - s.at.x) > ((double)(Motion.SUB) / (10))) ? JsMath.sign(goal.x - s.at.x) : 0;
        var dy = (JsMath.abs(goal.y - s.at.y) > ((double)(Motion.SUB) / (10))) ? JsMath.sign(goal.y - s.at.y) : 0;
        if ((c.dx != dx) || (c.dy != dy))
        {
            cmd(state, new Command { type = "steer", dx = dx, dy = dy });
        }
    }

    public static void decide(SimState state, Survivor s, Bot bot, Point home, Point bed)
    {
        var c = Js.NN(state.control);
        if ((Js.T(Js.T(s.move) && Js.T(bot.goal))) && !new List<string> { "fight", "flee", "idle" }.includes(bot.goal))
        {
            var key = bot.pathFor;
            if (Js.T(bot.path.length))
            {
                var at = s.move.to;
                var i = bot.path.findIndex((p) => (p.x == at.x) && (p.y == at.y));
                if (i >= 0)
                {
                    bot.path.splice(0, i + 1);
                }
                var next = Js.Get(bot.path, 0);
                var dx = Js.T(next) ? JsMath.sign(next.x - at.x) : 0;
                var dy = Js.T(next) ? JsMath.sign(next.y - at.y) : 0;
                if ((c.dx != dx) || (c.dy != dy))
                {
                    cmd(state, new Command { type = "steer", dx = dx, dy = dy });
                }
            }
            return;
        }
        if (Js.T(s.move))
        {
            return;
        }
        if (Js.T(c.busy) && Vehicles.isCarAction(c.busy.what))
        {
            return;  // getting in or out takes a moment (vehicleActions.ts)
        }
        var car = Vehicles.vehicleOf(state, s);
        if (Js.T(car))
        {
            { drive(state, s, bot, car); return; }
        }
        var zs = state.zombies.map((q) => new { id = q.id, pos = Motion.tileOf(q.at), body = q }).filter((q) => dist(q.pos, s.pos) <= 6).sort((a, b) => dist(a.pos, s.pos) - dist(b.pos, s.pos));
        var z = Js.Get(zs, 0);
        var sneak = state.zombies.some((q) => dist(q.pos, s.pos) <= 10) && !(Js.T(z) && (dist(z.pos, s.pos) <= 1));
        if ((c.sneaking == true) != sneak)
        {
            cmd(state, new Command { type = "sneak", on = sneak });
            if (Js.T(sneak))
            {
                bot.sneaked++;
            }
        }
        var close = Js.Get(state.zombies.filter((q) => Motion.inReach(s, q, Motion.SWING_REACH, state.grid)).sort((a, b) => Motion.distance2(a.at, s.at) - Motion.distance2(b.at, s.at)), 0);
        if (Js.T(close) && (s.health > ((double)(Health.HEALTH_MAX) * (0.3))))
        {
            var zt = Motion.tileOf(close.at);
            if (Js.T(Js.T(c.dx) || Js.T(c.dy)))
            {
                cmd(state, new Command { type = "steer", dx = 0, dy = 0 });
            }
            cmd(state, new Command { type = "aim", x = Js.I((JsMath.max(-64, JsMath.min(64, JsMath.round(((double)((close.at.x - s.at.x)) / (4))))) is var __or0 && Js.T(__or0) ? __or0 : 1)), y = Js.I(JsMath.max(-64, JsMath.min(64, JsMath.round(((double)((close.at.y - s.at.y)) / (4)))))) });
            cmd(state, new Command { type = "swing", x = Js.I(zt.x), y = Js.I(zt.y) });
            bot.goal = "fight";
            return;
        }
        if (Js.T(z) && (s.health <= ((double)(Health.HEALTH_MAX) * (0.3))))
        {
            bot.fled++;
            var dx = (JsMath.sign(s.pos.x - z.pos.x) is var __or1 && Js.T(__or1) ? __or1 : 1);
            var dy = JsMath.sign(s.pos.y - z.pos.y);
            if ((c.dx != dx) || (c.dy != dy))
            {
                cmd(state, new Command { type = "steer", dx = dx, dy = dy });
            }
            bot.goal = "flee";
            return;
        }
        if (Js.T(c.busy))
        {
            if ((Js.T((c.busy.what == "sleep") && Js.T(z))) && (dist(z.pos, s.pos) <= 3))
            {
                cmd(state, new Command { type = "steer", dx = 0, dy = 0 });
            }
            return;
        }
        List<Item> items(string kind)
        {
            return state.items.filter((i) => (Js.T(i.pos) && (i.kind == kind)) && !Js.T(i.reservedBy)).sort((a, b) => dist(Js.NN(a.pos), s.pos) - dist(Js.NN(b.pos), s.pos));
        }
        var full = state.furniture.find((f) => ((f.kind == "crate") && (dist(f, s.pos) <= 1)) && state.items.some((i) => i.inside == f.id));
        if (Js.T(full))
        {
            cmd(state, new Command { type = "takeAll", containerId = full.id });
        }
        Item? inBag(List<string> kinds)
        {
            return s.pack.map((id) => state.items.find((i) => i.id == id)).find((i) => Js.T(i) && kinds.includes(i.kind));
        }
        var needs = Medicine.remediesFor(s);
        var bagged = (needs.length > 0) ? inBag(needs) : null;
        if ((Js.T(Js.T(bagged) && (Js.T((Js.T(Js.T(c.dx) || Js.T(c.dy))) ? cmd(state, new Command { type = "steer", dx = 0, dy = 0 }) : true)))) && cmd(state, new Command { type = "useItem", itemId = bagged.id }).ok)
        {
            bot.treated++;
            bot.goal = "treat";
            return;
        }
        if (needs.length > 0)
        {
            var carried = Js.T(s.carrying) ? state.items.find((i) => i.id == s.carrying) : null;
            if (Js.T(carried) && needs.includes(carried.kind))
            {
                bot.treated++;
                bot.goal = "treat";
                cmd(state, new Command { type = "use", x = Js.I(s.pos.x), y = Js.I(s.pos.y) });
                return;
            }
            var near = needs.map((k) => Js.Get(items(k), 0)).find((i) => Js.T(i) && (dist(Js.NN(i.pos), s.pos) < 40));
            if (Js.T(near))
            {
                bot.goal = "treat";
                if (dist(Js.NN(near.pos), s.pos) <= 1)
                {
                    bot.treated++;
                }
                { goUse(state, s, bot, $"r{Js.Str(near.id)}", Js.NN(near.pos)); return; }
            }
            if ((s.infection != null) && Wounds.canAmputate(s))
            {
                bot.amputated++;
                cmd(state, new Command { type = "amputate", survivorId = s.id });
            }
        }
        if (!Js.T(s.carrying) && !Time.isNight(state.tick))
        {
            var med = Js.Get(items("medicine"), 0);
            if (Js.T(med) && (dist(Js.NN(med.pos), s.pos) < 12))
            {
                bot.goal = "pocket";
                { goUse(state, s, bot, $"m{Js.Str(med.id)}", Js.NN(med.pos)); return; }
            }
        }
        if (state.harsh && (s.hydration < Water.THIRSTY))
        {
            var water = Js.Get(state.furniture.filter((f) => ((f.kind == "well") || ((f.kind == "collector") && (f.loot > 0))) || (TimelineMod.isTap(state, f) && (dist(f, s.pos) < 40))).sort((a, b) => dist(a, s.pos) - dist(b, s.pos)), 0);
            if (Js.T(water))
            {
                bot.goal = "drink";
                if ((dist(water, s.pos) <= 1) && !Js.T(c.busy))
                {
                    bot.drank++;
                }
                { goUse(state, s, bot, $"water{Js.Str(water.id)}", water); return; }
            }
        }
        if (s.satiety < Food.HUNGRY)
        {
            var packed = inBag(["food", "meal"]);
            if ((Js.T(Js.T(packed) && (Js.T((Js.T(Js.T(c.dx) || Js.T(c.dy))) ? cmd(state, new Command { type = "steer", dx = 0, dy = 0 }) : true)))) && cmd(state, new Command { type = "useItem", itemId = packed.id }).ok)
            {
                bot.ate++;
                bot.goal = "eat";
                return;
            }
            var food = Js.At(items("food"), 0, Js.Get(items("meal"), 0));
            if (Js.T(food))
            {
                bot.goal = "eat";
                if (dist(Js.NN(food.pos), s.pos) <= 1)
                {
                    bot.ate++;
                }
                { goUse(state, s, bot, $"food{Js.Str(food.id)}", Js.NN(food.pos)); return; }
            }
            if (!Time.isNight(state.tick))
            {
                var prey = Js.Get(state.animals.filter((a) => ((a.kind == "deer") || (a.kind == "rabbit")) && (dist(a.pos, s.pos) <= 20)).sort((a, b) => dist(a.pos, s.pos) - dist(b.pos, s.pos)), 0);
                if (Js.T(prey))
                {
                    bot.goal = "hunt";
                    { goSwing(state, s, bot, prey); return; }
                }
            }
            if (Forage.forageTime(state))
            {
                if (Handiwork.canForage(state, s) == null)
                {
                    if (Js.T(Js.T(c.dx) || Js.T(c.dy)))
                    {
                        cmd(state, new Command { type = "steer", dx = 0, dy = 0 });
                    }
                    if (cmd(state, new Command { type = "use", x = Js.I(s.pos.x), y = Js.I(s.pos.y) }).ok)
                    {
                        bot.foraged++;
                    }
                    bot.goal = "forage";
                    return;
                }
                var spot = Space.nearestTile(state.grid, s.pos, (x, y) =>
        {
            var g = Js.Get(Grids.GROUNDS, Js.NN(Js.Get(state.grid.ground, ((double)(y) * (state.grid.width)) + x)));
            return ((((g == "grass") || (g == "field")) && Grids.isWalkable(state.grid, x, y)) && !Rooms.isIndoors(state, x, y)) && (Js.Get(state.foraged, Js.Str(((double)(y) * (state.grid.width)) + x)) != Time.dayOf(state.tick));
        }, 2000);
                if (Js.T(spot))
                {
                    bot.goal = "forage";
                    { goUse(state, s, bot, $"forage{Js.Str(spot.x)},{Js.Str(spot.y)}", spot, true); return; }
                }
            }
        }
        if (!Js.T(s.weapon))
        {
            var w = Js.At(items("weapon"), 0, Js.Get(items("gun"), 0));
            if (Js.T(w) && (dist(Js.NN(w.pos), s.pos) < 30))
            {
                bot.goal = "arm";
                { goUse(state, s, bot, $"w{Js.Str(w.id)}", Js.NN(w.pos)); return; }
            }
        }
        if ((Js.T((!Js.T(process.env.NO_BARRICADE) && !Time.isNight(state.tick)) && Js.T(bot.door))) && (Grids.getTile(state.grid, bot.door.x, bot.door.y) == "door"))
        {
            var door = bot.door;
            var wood = inBag(["wood"]) ?? (Js.T(s.carrying) ? state.items.find((i) => (i.id == s.carrying) && (i.kind == "wood")) : null);
            if (Js.T(wood))
            {
                bot.goal = "barricade";
                if (((JsMath.abs(s.pos.x - door.x) + JsMath.abs(s.pos.y - door.y)) == 1) && !Js.T(c.busy))
                {
                    if (Js.T(Js.T(c.dx) || Js.T(c.dy)))
                    {
                        cmd(state, new Command { type = "steer", dx = 0, dy = 0 });
                    }
                    if (cmd(state, new Command { type = "work", x = Js.I(door.x), y = Js.I(door.y) }).ok)
                    {
                        bot.barricaded++;
                    }
                    return;
                }
                { goUse(state, s, bot, "door", door, false, "work"); return; }
            }
            var lying = Js.Get(items("wood"), 0);
            if (Js.T(lying) && (dist(Js.NN(lying.pos), s.pos) < 25))
            {
                bot.goal = "get wood";
                { goUse(state, s, bot, $"wood{Js.Str(lying.id)}", Js.NN(lying.pos)); return; }
            }
            var tree = Js.Get(state.furniture.filter((f) => ((f.kind == "tree") && (f.loot > 0)) && (dist(f, home) < 15)).sort((a, b) => dist(a, s.pos) - dist(b, s.pos)), 0);
            if (Js.T(tree))
            {
                bot.goal = "chop";
                { goUse(state, s, bot, $"tree{Js.Str(tree.id)}", tree); return; }
            }
        }
        if ((s.rest < Rest.TIRED) || (Time.isNight(state.tick) && (s.rest < 1800)))
        {
            bot.goal = "sleep";
            if ((s.pos.x == bed.x) && (s.pos.y == bed.y))
            {
                bot.slept++;
            }
            { goUse(state, s, bot, "bed", bed, true); return; }
        }
        if (Time.isNight(state.tick))
        {
            bot.goal = "home";
            { goUse(state, s, bot, "bed", bed, true); return; }
        }
        var crate = Js.Get(state.furniture.filter((f) => ((f.kind == "crate") && (f.loot > 0)) && (dist(f, home) < 90)).sort((a, b) => dist(a, s.pos) - dist(b, s.pos)), 0);
        if (Js.T(crate))
        {
            var ride = ((dist(crate, s.pos) > 30) && !Time.isNight(state.tick)) ? Vehicles.vehiclesOf(state).find((v) => (((v.driverId == null) && (v.fuel > 300)) && (v.health > 0)) && (dist(v.pos, s.pos) <= 1)) : null;
            if (Js.T(((Js.T(Js.T(ride) && (Js.T((Js.T(Js.T(c.dx) || Js.T(c.dy))) ? cmd(state, new Command { type = "steer", dx = 0, dy = 0 }) : true)))) && cmd(state, new Command { type = "use", x = Js.I(ride.pos.x), y = Js.I(ride.pos.y) }).ok) && (Js.T(Vehicles.vehicleOf(state, s)) || c.busy?.what == "getIn")))
            {
                bot.drove++;
                bot.driveTo = new Point { x = Js.I(crate.x), y = Js.I(crate.y) };
                bot.stuck = 0;
                bot.goal = "drive";
                return;
            }
            var near = ((dist(crate, s.pos) > 30) && !Time.isNight(state.tick)) ? Js.Get(Vehicles.vehiclesOf(state).filter((v) => (((v.driverId == null) && (v.fuel > 300)) && (v.health > 0)) && (dist(v.pos, s.pos) <= 15)).sort((p, q) => dist(p.pos, s.pos) - dist(q.pos, s.pos)), 0) : null;
            if (Js.T(near) && (dist(near.pos, s.pos) > 1))
            {
                bot.goal = "to car";
                { goUse(state, s, bot, $"car{Js.Str(near.id)}", near.pos); return; }
            }
            bot.goal = "search";
            if (dist(crate, s.pos) <= 1)
            {
                bot.searched++;
            }
            { goUse(state, s, bot, $"crate{Js.Str(crate.id)}", crate); return; }
        }
        bot.goal = "idle";
        if (Js.T(Js.T(c.dx) || Js.T(c.dy)))
        {
            cmd(state, new Command { type = "steer", dx = 0, dy = 0 });
        }
    }

    /// <summary>After an animal: a shot if there's a loaded gun and a clear line, a swing once beside it, else close in.</summary>
    public static void goSwing(SimState state, Survivor s, Bot bot, Animal a)
    {
        var c = Js.NN(state.control);
        var shot = (Js.T(Weapons.loadedGun(state, s)) && ((JsMath.abs(a.pos.x - s.pos.x) + JsMath.abs(a.pos.y - s.pos.y)) <= Weapons.GUN_RANGE)) && Work.clearShot(state.grid, s.pos, a.pos);
        if (Js.T(Js.T(shot) || (dist(a.pos, s.pos) <= 1)))
        {
            if (Js.T(Js.T(c.dx) || Js.T(c.dy)))
            {
                cmd(state, new Command { type = "steer", dx = 0, dy = 0 });
            }
            if (cmd(state, new Command { type = "swing", x = Js.I(a.pos.x), y = Js.I(a.pos.y) }).ok)
            {
                bot.hunted++;
            }
            return;
        }
        goUse(state, s, bot, $"hunt{Js.Str(a.id)}@{Js.Str(a.pos.x)},{Js.Str(a.pos.y)}", a.pos);
    }

    /// <summary>Steer a car toward bot.driveTo: turn toward it, go, brake near it, and get out (or when stuck, or the dead are at it).</summary>
    public static void drive(SimState state, Survivor s, Bot bot, Vehicle car)
    {
        var c = Js.NN(state.control);
        var to = bot.driveTo;
        void @out()
        {
            if (car.speed != 0)
            {
                if ((c.dx != 0) || (c.dy != 1))
                {
                    cmd(state, new Command { type = "steer", dx = 0, dy = 1 });
                }
                return;
            }
            if (Js.T(Js.T(c.dx) || Js.T(c.dy)))
            {
                cmd(state, new Command { type = "steer", dx = 0, dy = 0 });
            }
            cmd(state, new Command { type = "use", x = Js.I(s.pos.x), y = Js.I(s.pos.y) });
            bot.driveTo = null;
            bot.goal = "parked";
        }
        if ((((!Js.T(to) || (dist(car.pos, to) <= 3)) || (car.fuel <= 0)) || (car.health <= 0)) || Time.isNight(state.tick))
        {
            { @out(); return; }
        }
        bot.stuck = (JsMath.abs(car.speed) < 8) ? (bot.stuck + 1) : 0;
        if (bot.stuck > 60)
        {
            { @out(); return; }
        }
        var want = ((JsMath.round(((double)(((double)(JsMath.atan2(to.y - car.pos.y, to.x - car.pos.x)) / (((double)(2) * (JsMath.PI))))) * (Vehicles.HEADINGS))) % Vehicles.HEADINGS) + Vehicles.HEADINGS) % Vehicles.HEADINGS;
        var diff = (((want - car.heading) + ((double)(Vehicles.HEADINGS) * (1.5))) % Vehicles.HEADINGS) - ((double)(Vehicles.HEADINGS) / (2));
        var dx = (diff == 0) ? 0 : JsMath.sign(diff);
        var dy = ((JsMath.abs(diff) > 6) && (car.speed > 60)) ? 0 : -1;
        if ((c.dx != dx) || (c.dy != dy))
        {
            cmd(state, new Command { type = "steer", dx = dx, dy = dy });
        }
        bot.goal = "drive";
    }

    public static void run(double seed, double days)
    {
        var began = performance.now();
        var state = Worldgen.createWorld(seed, settings: SETTINGS);
        var s0 = Js.NN(ControlMod.controlled(state));
        var bedF = Js.NN(state.furniture.find((f) => f.kind == "bed"));
        Point bed = new Point { x = Js.I(bedF.x), y = Js.I(bedF.y) };
        var home = Js.Clone(s0.pos);
        var door = Space.nearestTile(state.grid, bed, (x, y) => Grids.getTile(state.grid, x, y) == "door", 400);
        Bot bot = new Bot { door = door, hunted = 0, barricaded = 0, goal = "", path = [], pathFor = "", searched = 0, ate = 0, drank = 0, slept = 0, fled = 0, foraged = 0, sneaked = 0, drove = 0, driveTo = null, stuck = 0, treated = 0, amputated = 0 };
        void log(string why)
        {
            var s = ControlMod.controlled(state);
            @out.push($"  day {Js.Str(Time.dayOf(state.tick))} {Js.Str(why)}: {Js.Str(Js.T(s) ? Js.Str($"hp {Js.Str(s.health)} food {Js.Str(s.satiety)} water {Js.Str(s.hydration)} rest {Js.Str(s.rest)} weapon {Js.Str(Js.T(s.weapon) ? Js.Str("y") : Js.Str("n"))} kills {Js.Str(s.kills)} goal {Js.Str(bot.goal)}") : Js.Str("dead"))} | items food {Js.Str(state.items.filter((i) => i.kind == "food").length)} zombies {Js.Str(state.zombies.length)} | searched {Js.Str(bot.searched)} ate {Js.Str(bot.ate)} drank {Js.Str(bot.drank)} slept {Js.Str(bot.slept)} fled {Js.Str(bot.fled)} foraged {Js.Str(bot.foraged)} sneaked {Js.Str(bot.sneaked)} drove {Js.Str(bot.drove)} treated {Js.Str(bot.treated)} amputated {Js.Str(bot.amputated)} hunted {Js.Str(bot.hunted)} barricaded {Js.Str(bot.barricaded)} infected {(s?.infection is double __inf ? Js.Str(__inf) : "-")}");
        }
        @out.push($"seed {Js.Str(seed)} ({Js.Str(process.env.PRESET ?? "apocalypse")})");
        for (var t = 0; t < ((double)(days) * (Time.DAY_LENGTH)); t++)
        {
            var s = ControlMod.controlled(state);
            if (!Js.T(s))
            {
                var ev = state.events.filter((e) => Js.T(new JsRegExp("died|killed", "").test(e.message))).slice(-2).map((e) => e.message).join(" / ");
                @out.push($"  DEAD on day {Js.Str(Time.dayOf(state.tick))} at tick {Js.Str(state.tick)}: {Js.Str(ev)}");
                log("end");
                flush();
                return;
            }
            decide(state, s, bot, home, bed);
            if (Js.T(process.env.TRACE) && ((state.tick % Js.Num(Js.Q(process.env.TRACE_EVERY, 150))) == 0))
            {
                @out.push($"   t{Js.Str(state.tick)} z {Js.Str(state.zombies.filter((q) => dist(Motion.tileOf(q.at), s.pos) <= 3).map((q) => $"{Js.Str(Motion.tileOf(q.at).x)},{Js.Str(Motion.tileOf(q.at).y)}{Js.Str(Js.T(q.windup) ? Js.Str("w") : Js.Str(""))}").join(" "))} pos {Js.Str(s.pos.x)},{Js.Str(s.pos.y)} goal {Js.Str(bot.goal)} sat {Js.Str(s.satiety)} hp {Js.Str(s.health)} busy {Js.Str(Js.NN(state.control).busy?.what ?? "-")} steer {Js.Str(Js.NN(state.control).dx)},{Js.Str(Js.NN(state.control).dy)} path {Js.Str(bot.path.length)} note {Js.Str(s.note)}");
            }
            Tick.tick(state);
            if (Js.T(process.env.LOG) && (((state.tick % Time.DAY_LENGTH) == 0) || (state.tick == Js.Num(process.env.LOG_AT))))
            {
                writeFileSync($"{Js.Str(process.env.LOG)}-s{Js.Str(seed)}-t{Js.Str(state.tick)}.json", Js.Json_stringify(state));
            }
            if ((state.tick % Time.DAY_LENGTH) == 0)
            {
                log("dawn");
            }
            if ((performance.now() - began) > BUDGET_MS)
            {
                @out.push($"  out of time on day {Js.Str(Time.dayOf(state.tick))}");
                flush();
                return;
            }
        }
        @out.push($"  SURVIVED {Js.Str(days)} days");
        log("end");
        flush();
    }

    [Fact(DisplayName = "solo balance")]
    public void T001_solo_balance()
    {
        foreach (var seed in Js.Iter((process.env.SEEDS ?? "1,2,3").split(",").map((__v) => Js.Num(__v))))
        {
            run(seed, Js.Num(Js.Q(process.env.DAYS, 25)));
        }
    }
}

public partial class Bot
{
    public string goal { get; set; } = "";
    public Point? door { get; set; }
    public double hunted { get; set; }
    public double barricaded { get; set; }
    public List<Point> path { get; set; } = new();
    public string pathFor { get; set; } = "";
    public double searched { get; set; }
    public double ate { get; set; }
    public double drank { get; set; }
    public double slept { get; set; }
    public double fled { get; set; }
    public double foraged { get; set; }
    public double sneaked { get; set; }
    public double drove { get; set; }
    public Point? driveTo { get; set; }
    public double stuck { get; set; }
    public double treated { get; set; }
    public double amputated { get; set; }
}
