// Ported from src/sim/control.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Direct control (the control prototype): the player takes over one survivor.
//
// While controlled, a survivor does nothing on their own: no jobs, meals, sleep
// or leisure. Their needs still drain, zombies still go for them, and they still
// bleed and heal. Input arrives as commands like everything else (rule 5):
//   steer   the held direction (sent when the keys change, not every frame)
//   swing   attack towards a tile: the zombie in front, else a shot with a loaded gun
//   work    repair, barricade, reinforce or build at the tile in front (handiwork.ts)
//   use     pick up, put on, eat, search, treat yourself (medicine, a bandage), drink (a well,
//           a rain collector, the river) or
//           sleep (a bed; your own tile with nothing on it is the floor) at a tile next to you
// Movement is free (motion.ts): the held direction moves your centre a little each tick, at
// your tile pace (roads, a bad leg...), the same speed any way you go, sliding along walls.
// Your tile (pos) is whatever tile you're over.
// Searching, eating, drinking and sleeping take time; moving or swinging interrupts them,
// and so does being hurt while asleep.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class ControlMod
{

    /// <summary>Ticks to cook a hot meal at a house's oven.</summary>
    public const int COOK_TICKS = 40;

    /// <summary>Rest back a slow tick asleep on a sofa: between the floor and a bed.</summary>
    public const int SOFA_REGEN = 3;

    /// <summary>You drink from a tap you use only if you're this far from full (else you search the counter).</summary>
    public const int TAP_THIRST = 200;

    /// <summary>Sneaking: this much of your pace (percent).</summary>
    public const int SNEAK_PCT = 50;

    /// <summary>A blow to one of the dead that isn't after you (sneaking up, or it's busy with someone else) does this much (percent).</summary>
    public const int SNEAK_ATTACK_PCT = 300;

    /// <summary>Each zombie within reach slows you this much (percent), to no less than CLUTCHED_MIN.</summary>
    public const int CLUTCH_PCT = 20;

    public const int CLUTCHED_MIN = 40;

    /// <summary>How many of the dead are within reach of someone, clutching at them (not staggered ones).</summary>
    public static double clutchedBy(SimState state, Survivor s)
    {
        double n = 0;
        foreach (var z in Js.Iter(state.zombies))
        {
            if (((z.kind != "raider") && (state.tick >= z.staggerUntil)) && Motion.inReach(s, z, Motion.REACH, state.grid))
            {
                n++;
            }
        }
        return n;
    }

    /// <summary>The largest part of an aim direction (a circle of radius AIM_MAX, in 32 steps from the UI).</summary>
    public const int AIM_MAX = 64;

    /// <summary>Look this way: sets the aim, and the eight-way facing nearest it. Returns null if changed, 'same', or why not.</summary>
    public static string? aim(Control c, int x, int y)
    {
        if ((((!JsNumber.isInteger(x) || !JsNumber.isInteger(y)) || (JsMath.abs(x) > AIM_MAX)) || (JsMath.abs(y) > AIM_MAX)) || ((x == 0) && (y == 0)))
        {
            return "aim takes a direction";
        }
        if ((Js.T(c.aim) && (c.aim.x == x)) && (c.aim.y == y))
        {
            return "same";
        }
        c.aim = new Point { x = Js.I(x), y = Js.I(y) };
        c.facing = eightWay(x, y);
        return null;
    }

    /// <summary>The eight-way direction nearest (x, y), which isn't (0, 0).</summary>
    public static Point eightWay(double x, double y)
    {
        var big = JsMath.max(JsMath.abs(x), JsMath.abs(y));
        return new Point { x = Js.I((((double)(12) * (JsMath.abs(x))) >= ((double)(5) * (big))) ? JsMath.sign(x) : 0), y = Js.I((((double)(12) * (JsMath.abs(y))) >= ((double)(5) * (big))) ? JsMath.sign(y) : 0) };
    }
    public static string? aim(Control c, double x, double y) => aim(c, Js.I(x), Js.I(y));

    /// <summary>Which way a swing goes: the aim, else the facing.</summary>
    public static Point swingDir(Control c)
    {
        return c.aim ?? c.facing;
    }

    public static Survivor? controlled(SimState state)
    {
        return Js.T(state.control) ? Survivors.getSurvivor(state, state.control.survivorId) : null;
    }

    public static bool isControlled(SimState state, Survivor s)
    {
        return state.control?.survivorId == s.id;
    }

    /// <summary>How far (SUB) you move a tick, steering: your tile pace, a run, a window's frame (slow), the dead clutching, sneaking.</summary>
    public static double stepLength(SimState state, Survivor s, Control c, bool run)
    {
        var climb = Motion.inWindowFrame(state.grid, s.at);
        var pct = ((double)(((double)(((double)(((double)(((double)(JsMath.max(CLUTCHED_MIN, 100 - ((double)(CLUTCH_PCT) * (clutchedBy(state, s))))) * (((run && !climb) ? BodyMod.RUN_PCT : 100)))) * ((climb ? 2 : 5)))) / (5))) * ((Js.T(c.sneaking) ? SNEAK_PCT : 100)))) / (100));
        var walk = JsMath.round(((double)(Motion.SUB) / (Survivors.stepPace(state, s, s.pos))));
        return JsMath.round(((double)(((double)(walk) * (pct))) / (10000)));
    }

    /// <summary>Called once a tick for the controlled survivor, in place of their own behaviour.</summary>
    public static void updateControlled(SimState state, Survivor s)
    {
        var c = Js.NN(state.control);
        if (Js.T(Vehicles.vehicleOf(state, s)))
        {
            if (c.busy?.what == "getOut")
            {
                // Getting out: the car rolls to a stop meanwhile (vehicleActions.ts).
                Vehicles.drive(state, s, 0, 0);
                s.note = Vehicles.carActionTick(state, s, c) ?? s.note;
                return;
            }
            c.busy = null;
            Vehicles.drive(state, s, JsMath.sign(c.dx), JsMath.sign(c.dy));
            return;
        }
        s.move = null;
        if (Leaps.airborne(c))
        {
            Leaps.tick(state, s, c);  // in the air: steering waits for the landing
            return;
        }
        Motion.followTile(s);
        if ((c.dx != 0) || (c.dy != 0))
        {
            c.busy = null;
            if (!Js.T(c.aim))
            {
                c.facing = eightWay(c.dx, c.dy);
            }
            var run = ((c.running == true) && !Js.T(c.sneaking)) && !BodyMod.isWinded(s);
            if (Js.T(run))
            {
                BodyMod.exert(s, BodyMod.RUN_TICK_COST);
            }
            var v = Motion.toward(c.dx, c.dy, stepLength(state, s, c, Js.T(run)));
            s.at = Motion.slide(state, s.at, v.x, v.y, Motion.fits(state.grid, s.at, "free") ? "free" : "survivor");
            var tile = Motion.tileOf(s.at);
            if ((tile.x != s.pos.x) || (tile.y != s.pos.y))
            {
                s.pos = tile;
                Survivors.enterTile(state, s);
            }
            return;
        }
        if (Js.T(c.busy))
        {
            updateBusy(state, s, c);
        }
    }

    public static void updateBusy(SimState state, Survivor s, Control c)
    {
        var busy = Js.NN(c.busy);
        if (Vehicles.isCarAction(busy.what))
        {
            // Seconds of handling, counted in ticks whatever the day length (vehicleActions.ts).
            s.note = Vehicles.carActionTick(state, s, c) ?? s.note;
            return;
        }
        if (busy.what == "sleep")
        {
            { sleepTick(state, s, c, busy); return; }
        }
        if (busy.what == "reload")
        {
            // Counted in ticks whatever the day length (like a car's handling).
            { reloadTick(state, s, c, busy); return; }
        }
        if (!Time.slowTick(state.tick))
        {
            return;
        }
        busy.progress += 1;
        if (busy.what == "repair")
        {
            if (!Handiwork.repairTick(state, busy.targetId))
            {
                return;
            }
            c.busy = null;
            s.note = "repaired it";
            return;
        }
        if (busy.progress < busy.duration)
        {
            return;
        }
        c.busy = null;
        if (busy.what == "build")
        {
            s.note = Handiwork.finishBuild(state, s, busy.targetId);
            return;
        }
        if (busy.what == "forage")
        {
            s.note = Handiwork.finishForage(state, s);
            return;
        }
        if (busy.what == "eat")
        {
            var item = Items.getItem(state, busy.targetId);
            if (!Js.T(item) || ((item.carriedBy != s.id) && !Js.T(item.pos)))
            {
                return;
            }
            s.satiety = JsMath.min(Food.SATIETY_MAX, s.satiety + ((item.kind == "meal") ? Food.HOT_MEAL_VALUE : Food.MEAL_VALUE));
            if (s.carrying == item.id)
            {
                s.carrying = null;
            }
            Inventory.unpack(s, item.id);
            Items.removeItem(state, item);
            s.note = $"ate {Js.Str((item.kind == "meal") ? Js.Str("a hot meal") : Js.Str("some food"))}";
            return;
        }
        if (busy.what == "treat")
        {
            var item = Items.getItem(state, busy.targetId);
            if (!Js.T(item) || ((item.carriedBy != s.id) && !Js.T(item.pos)))
            {
                return;
            }
            Medicine.applyRemedy(s, item.kind);
            if (s.carrying == item.id)
            {
                s.carrying = null;
            }
            Inventory.unpack(s, item.id);
            Items.removeItem(state, item);
            s.note = (item.kind == "medicine") ? "took the medicine" : "bandaged up";
            return;
        }
        if (busy.what == "drink")
        {
            var (x, y) = busy.targetId.split(",").map((__v) => Js.Num(__v));
            var source = Water.sourceAt(state, x, y);
            if (!Js.T(source))
            {
                return;
            }
            s.hydration = Water.HYDRATION_MAX;
            Water.drankFrom(state, source, x, y);
            s.note = "had a drink";
            return;
        }
        if (busy.what == "cook")
        {
            var food = Items.getItem(state, busy.targetId);
            if ((!Js.T(food) || (food.kind != "food")) || !s.pack.includes(food.id))
            {
                return;
            }
            food.kind = "meal";
            food.wear = 0;
            s.note = "cooked a hot meal";
            return;
        }
        var crate = FurnitureMod.getFurniture(state, busy.targetId);
        if ((!Js.T(crate) || (crate.loot <= 0)) || !nextTo(s.pos, crate))
        {
            return;
        }
        if (crate.kind == "tree")
        {
            Difficulty.spawnFind(state, "wood", s.pos.x, s.pos.y);
            crate.loot -= 1;
            s.note = "chopped some wood";
            return;
        }
        List<string> found = [];
        while (crate.loot > 0)
        {
            var item = Difficulty.spawnFind(state, Work.scavengeFind(Rng.random(state), crate.table), s.pos.x, s.pos.y);
            item.pos = null;
            item.inside = crate.id;
            crate.loot -= 1;
            found.push(item.kind);
        }
        FurnitureMod.setScavenge(state, crate, false);
        s.note = Js.T(found.length) ? $"found {Js.Str(found.join(", "))}" : "nothing in it";
    }

    /// <summary>Asleep: rest comes back (faster in a bed) until full, or until something hurts you.</summary>
    public static void sleepTick(SimState state, Survivor s, Control c, ControlBusy busy)
    {
        if (s.hurtTick == state.tick)
        {
            c.busy = null;
            s.note = "woken by an attack!";
            return;
        }
        if (!Time.slowTick(state.tick))
        {
            return;
        }
        var sofa = (busy.targetId != "floor") && (FurnitureMod.getFurniture(state, busy.targetId)?.look == "sofa");
        s.rest = JsMath.min(Rest.REST_MAX, (s.rest + Rest.REST_DRAIN) + ((busy.targetId == "floor") ? Rest.FLOOR_REGEN : (Js.T(sofa) ? SOFA_REGEN : Rest.BED_REGEN)));
        busy.progress = s.rest;
        if (s.rest < Rest.REST_MAX)
        {
            return;
        }
        c.busy = null;
        Mood.addThought(state, s, (busy.targetId == "floor") ? "sleptOnFloor" : "sleptInBed");
        if (busy.targetId != "floor")
        {
            Mood.addThought(state, s, "wellRested");
        }
        Wellbeing.wakeComfort(state, s, (busy.targetId == "floor") ? null : busy.targetId);
        s.note = "woke up rested";
    }

    /// <summary>Within one tile, diagonals included.</summary>
    public static bool nextTo(Point a, Point b)
    {
        return JsMath.max(JsMath.abs(a.x - b.x), JsMath.abs(a.y - b.y)) <= 1;
    }

    /// <summary>The facing direction (a step of -1..1 each way) from a tile towards another.</summary>
    public static Point? towards(Point from, Point to)
    {
        var dx = JsMath.sign(to.x - from.x);
        var dy = JsMath.sign(to.y - from.y);
        return ((dx == 0) && (dy == 0)) ? null : new Point { x = Js.I(dx), y = Js.I(dy) };
    }

    /// <summary>Swing (or shoot) towards a tile. A loaded gun shoots along the line to the tile's centre; anything else (a gun with nothing in it too) swings by the weapon's own reach, arc and speed. Returns what happened, or null if nothing could be done.</summary>
    public static string? swing(SimState state, Survivor s, Point target)
    {
        var c = Js.NN(state.control);
        if (Js.T((Js.T((state.tick < c.nextSwing) || Js.T(s.move))) || Js.T(Vehicles.vehicleOf(state, s))))
        {
            return null;
        }
        var face = towards(s.pos, target);
        if (Js.T(face))
        {
            c.facing = face;
        }
        c.busy = null;
        var gun = Weapons.loadedGun(state, s);
        if (gun != null)
        {
            var aimed = swingDir(c);
            double ax = (((double)(target.x) * Motion.SUB) + Motion.HALF) - s.at.x, ay = (((double)(target.y) * Motion.SUB) + Motion.HALF) - s.at.y;
            bool same = (ax == 0) && (ay == 0);
            return shoot(state, s, c, gun, same ? aimed.x : ax, same ? aimed.y : ay);
        }
        return melee(state, s, c, target);
    }

    /// <summary>Fire the gun in hand along an aim (a direction: whole numbers up to Shooting.AIM_VECTOR_MAX; first person sends its look). Returns what happened, or null if nothing could be done (no loaded gun, not ready).</summary>
    public static string? fire(SimState state, Survivor s, double ax, double ay)
    {
        var c = Js.NN(state.control);
        var gun = Weapons.loadedGun(state, s);
        if (((gun == null) || (state.tick < c.nextSwing)) || Js.T(s.move) || Js.T(Vehicles.vehicleOf(state, s)) || ((ax == 0) && (ay == 0)))
        {
            return null;
        }
        c.busy = null;
        return shoot(state, s, c, gun, ax, ay);
    }

    /// <summary>One shot from a loaded gun (the caller has checked it): held fire if one of yours is in the line.</summary>
    static string shoot(SimState state, Survivor s, Control c, Item gun, double ax, double ay)
    {
        var w = Weapons.specOf(gun)!;
        var friend = Shooting.friendInLine(state, s, w, ax, ay);
        if (friend != null)
        {
            return $"held fire: {Js.Str(friend.name)} is in the way";
        }
        c.nextSwing = state.tick + Moodlets.swingWait(s, w.swing);
        c.swungTick = state.tick;
        var moving = ((c.dx != 0) || (c.dy != 0));
        var o = Shooting.fire(state, s, gun, ax, ay, moving);
        c.facing = eightWay(ax, ay);  // the facing follows the shot (the arc of a later swing)
        return o.note;
    }

    /// <summary>A blow: the weapon's reach, arc, damage, speed, endurance and noise (WeaponTable.cs).</summary>
    static string? melee(SimState state, Survivor s, Control c, Point target)
    {
        var spec = Weapons.meleeSpec(state, s);
        var near = Motion.nearestInArc(state.zombies, s.at, swingDir(c), spec.reach, state.grid, spec.arcCos2);
        var wait = Moodlets.swingWait(s, spec.swing) + (BodyMod.isWinded(s) ? BodyMod.WINDED_WAIT : 0);
        if (Js.T(near) && Resolve.freezes(state, s))
        {
            c.nextSwing = state.tick + wait;
            return "froze";
        }
        if (Js.T(near) && BodyMod.panicMiss(state, s))
        {
            c.nextSwing = state.tick + wait;
            c.swungTick = state.tick;
            BodyMod.exert(s, spec.stamina);
            return "missed: your hands are shaking";
        }
        if (Js.T(near))
        {
            c.nextSwing = state.tick + wait;
            c.swungTick = state.tick;
            BodyMod.exert(s, spec.stamina);
            Noise.makeNoise(state, s.pos, "fighting", spec.noise);
            var full = Moodlets.boostDamage(s, (Weapons.blowDamage(state, s, Jobs.GUARD_DAMAGE) + ((double)(Skills.GUARD_DAMAGE_PER_LEVEL) * (Skills.skillLevel(s, "guard")))) + (Skills.hasTrait(s, "hardened") ? Skills.HARDENED_DAMAGE : 0));
            var winded = BodyMod.isWinded(s) ? JsMath.round(((double)(((double)(full) * (BodyMod.WINDED_DAMAGE_PCT))) / (100))) : full;
            var unaware = (near.kind != "raider") && (near.targetId != s.id);
            var blow = Js.T(unaware) ? JsMath.round(((double)(((double)(winded) * (spec.sneak))) / (100))) : winded;
            var killed = Zombies.damageZombie(state, near, Power.inLight(state, s.pos) ? JsMath.round(((double)(((double)(blow) * (Power.LIGHT_DAMAGE_PCT))) / (100))) : blow, s);
            if (killed)
            {
                Weapons.wearWeapon(state, s);
            }
            else
            {
                Zombies.knockBack(state, near, s.at);
            }
            return killed ? (Js.T(unaware) ? "killed it before it knew you were there" : "killed it") : (Js.T(unaware) ? "hit it from behind" : "hit it");
        }
        var person = Motion.nearestInArc(state.residents, s.at, swingDir(c), spec.reach, state.grid, spec.arcCos2);
        if (Js.T(person))
        {
            c.nextSwing = state.tick + spec.swing;
            c.swungTick = state.tick;
            Noise.makeNoise(state, s.pos, "fighting", spec.noise);
            var blow = Weapons.blowDamage(state, s, Jobs.GUARD_DAMAGE) + ((double)(Skills.GUARD_DAMAGE_PER_LEVEL) * (Skills.skillLevel(s, "guard")));
            return Residents.hitResident(state, person, blow, s) ? $"killed {Js.Str(person.name)}" : $"hit {Js.Str(person.name)}";
        }
        var deer = Motion.nearestInArc(state.animals, s.at, swingDir(c), spec.reach, state.grid, spec.arcCos2);
        if (Js.T(deer))
        {
            c.nextSwing = state.tick + Moodlets.swingWait(s, spec.swing);
            return Handiwork.hitAnimal(state, s, deer.id, Weapons.blowDamage(state, s, Jobs.GUARD_DAMAGE) + ((double)(Skills.GUARD_DAMAGE_PER_LEVEL) * (Skills.skillLevel(s, "guard"))));
        }
        var pane = paneInFront(state, s.pos, target, c.facing);
        if (Js.T(pane))
        {
            c.nextSwing = state.tick + wait;
            c.swungTick = state.tick;
            BodyMod.exert(s, spec.stamina);
            Breach.smashGlass(state, pane.edge);
            TimelineMod.maybeAlarm(state, pane.to, pane.edge);
            return "smashed the window";
        }
        c.nextSwing = state.tick + wait;
        c.swungTick = state.tick;
        BodyMod.exert(s, spec.stamina);
        var held = Weapons.specOf(Weapons.wielded(state, s));
        return ((held != null) && held.twoHanded && !Weapons.handsFree(s)) ? "swung at nothing (your hands are full)" : "swung at nothing";
    }

    /// <summary>Reload the gun in hand from the bag: a whole magazine in one go, or a round at a time for a revolver, a shotgun, a rifle (shooting or walking off stops it). Returns why not, or null if it began.</summary>
    public static string? reload(SimState state, Survivor s)
    {
        var c = Js.NN(state.control);
        var gun = Weapons.heldGun(state, s);
        if (gun == null)
        {
            return "no gun in hand";
        }
        var w = Weapons.specOf(gun)!;
        if (gun.wear >= w.magazine)
        {
            return "it is full";
        }
        if (packAmmo(state, s, w) == null)
        {
            return "no ammunition in your bag";
        }
        c.busy = new ControlBusy { what = "reload", targetId = gun.id, progress = 0, duration = w.reload };
        s.note = "reloading";
        return null;
    }

    /// <summary>The first box in the bag of the gun's ammunition.</summary>
    public static Item? packAmmo(SimState state, Survivor s, WeaponSpec w)
    {
        foreach (var id in Js.Iter(s.pack))
        {
            var item = Items.getItem(state, id);
            if (item != null && item.kind == w.ammo) return item;
        }
        return null;
    }

    /// <summary>One tick of a reload: at its end the rounds go in; a gun loaded a round at a time goes on to the next.</summary>
    static void reloadTick(SimState state, Survivor s, Control c, ControlBusy busy)
    {
        busy.progress += 1;
        if (busy.progress < busy.duration)
        {
            return;
        }
        var gun = Weapons.heldGun(state, s);
        if (gun == null || gun.id != busy.targetId)
        {
            c.busy = null;
            return;
        }
        var w = Weapons.specOf(gun)!;
        var box = packAmmo(state, s, w);
        if (box == null)
        {
            c.busy = null;
            s.note = "out of ammunition";
            return;
        }
        Weapons.loadRounds(state, gun, box, w.perRound ? 1 : double.PositiveInfinity);
        if (w.perRound && (gun.wear < w.magazine) && (packAmmo(state, s, w) != null))
        {
            busy.progress = 0;
            s.note = "reloading";
            return;
        }
        c.busy = null;
        s.note = (gun.wear >= w.magazine) ? "reloaded" : "loaded what you had";
    }

    /// <summary>Take up another weapon from the bag (the one named, else the next): what was in hand goes into the bag in its place. Returns why not, or null.</summary>
    public static string? switchWeapon(SimState state, Survivor s, string? itemId)
    {
        var c = Js.NN(state.control);
        var next = Js.T(itemId) ? Items.getItem(state, itemId!) : s.pack.map((id) => Items.getItem(state, id)).find((i) => (i != null) && Weapons.isWieldKind(i.kind));
        if (next == null || !s.pack.includes(next.id) || !Weapons.isWieldKind(next.kind))
        {
            return Js.T(itemId) ? "that isn't a weapon in your bag" : "no other weapon in your bag";
        }
        c.busy = null;
        s.note = Inventory.equipFromPack(state, s, next, s.pos);
        return null;
    }

    /// <summary>An unboarded window on the edge between you and the tile you swung at (if it's next to you), else the tile you face; facing a corner, either side of it.</summary>
    public static PaneInFrontResult? paneInFront(SimState state, Point from, Point target, Point facing)
    {
        var face = nextTo(from, target) ? (towards(from, target) ?? facing) : facing;
        List<Point> steps = ((face.x != 0) && (face.y != 0)) ? ((List<Point>)new List<Point> { new Point { x = Js.I(face.x), y = Js.I(0) }, new Point { x = Js.I(0), y = Js.I(face.y) } }) : ((List<Point>)[face]);
        foreach (var d in Js.Iter(steps))
        {
            Point to = new Point { x = Js.I(from.x + d.x), y = Js.I(from.y + d.y) };
            var e = Grids.edgeBetween(state.grid, from.x, from.y, to.x, to.y);
            if (((e >= 0) && (Js.Get(state.grid.edges, e) == Grids.EDGE_WINDOW)) && !state.grid.edgeBoarded.includes(e))
            {
                return new PaneInFrontResult { edge = e, to = to };
            }
        }
        return null;
    }

    /// <summary>A resident (residents.ts) in the arc in front of you, within reach.</summary>
    public static Resident? residentToHit(SimState state, Point from, Point facing)
    {
        return Motion.nearestInArc(state.residents, from, facing, Motion.SWING_REACH, state.grid);
    }

    /// <summary>Someone of yours standing between you and a tile (Bresenham, like clearShot).</summary>
    public static Survivor? inTheWay(SimState state, Survivor s, Point to)
    {
        var __d0 = s.pos;
        var x = __d0.x;
        var y = __d0.y;
        var dx = JsMath.abs(to.x - x);
        var dy = -JsMath.abs(to.y - y);
        var sx = (x < to.x) ? 1 : -1;
        var sy = (y < to.y) ? 1 : -1;
        double err = dx + dy;
        for (; ; )
        {
            var e2 = ((double)(2) * (err));
            if (e2 >= dy)
            {
                err += dy;
                x += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                y += sy;
            }
            if ((x == to.x) && (y == to.y))
            {
                return null;
            }
            var o = state.survivors.find((v) => ((v != s) && (v.pos.x == x)) && (v.pos.y == y));
            if (Js.T(o))
            {
                return o;
            }
        }
    }

    /// <summary>The zombie a swing lands on: the nearest in the arc in front of you, within reach (motion.ts).</summary>
    public static Zombie? zombieToHit(SimState state, Point from, Point facing)
    {
        return Motion.nearestInArc(state.zombies, from, facing, Motion.SWING_REACH, state.grid);
    }

    /// <summary>Use what's on a tile next to (or under) you. Returns what happened, or null if there was nothing to do.</summary>
    public static string? use(SimState state, Survivor s, Point target)
    {
        var c = Js.NN(state.control);
        var inCar = Vehicles.vehicleOf(state, s);
        if (Js.T(inCar))
        {
            return Vehicles.startCarAction(state, s, inCar, "getOut");
        }
        if (Js.T(Js.T(s.move) || !nextTo(s.pos, target)))
        {
            return null;
        }
        var car = Vehicles.vehiclesOf(state).find((v) => (Motion.distance2(v.at, Motion.centre(target)) <= (JsMath.pow((Vehicles.VEHICLE_RADIUS + ((double)(Motion.SUB) / (2))), 2))) && (Motion.distance2(v.at, s.at) <= (JsMath.pow(Vehicles.ENTER_REACH, 2))));
        if (Js.T(car))
        {
            c.busy = null;
            if (Js.T(car.driverId))
            {
                return "someone is driving it";
            }
            return Vehicles.fillFromCan(state, s, car) ?? Vehicles.startCarAction(state, s, car, "getIn");
        }
        var face = towards(s.pos, target);
        if (Js.T(face))
        {
            c.facing = face;
        }
        var person = Residents.residentAt(state, target);
        if (Js.T(person))
        {
            return Residents.meetResident(state, s, person) ?? $"talked with {Js.Str(person.name)}";
        }
        var edgeDoor = Doors.toggleEdgeDoor(state, s.pos, target);
        if (Js.T(edgeDoor))
        {
            return edgeDoor;
        }
        var pane = Grids.edgeBetween(state.grid, s.pos.x, s.pos.y, target.x, target.y);
        if (((pane >= 0) && (Js.Get(state.grid.edges, pane) == Grids.EDGE_WINDOW)) && !state.grid.edgeBoarded.includes(pane))
        {
            Breach.smashGlass(state, pane);
            TimelineMod.maybeAlarm(state, target, pane);
            return "smashed the window";
        }
        if (Grids.getTile(state.grid, target.x, target.y) == "door")
        {
            var was = Grids.isOpen(state.grid, target.x, target.y);
            return Commands.toggleDoor(state, target.x, target.y) ?? (was ? "closed the door" : "opened the door");
        }
        var item = Items.itemAt(state, target.x, target.y);
        var carriedRemedy = (Js.T(s.carrying) && Space.samePoint(target, s.pos)) ? Items.getItem(state, s.carrying) : null;
        var remedy = new[] { item, carriedRemedy }.ToList().find((i) => (Js.T(i) && !Js.T(i.reservedBy)) && Medicine.remediesFor(s).includes(i.kind));
        if (Js.T(remedy))
        {
            c.busy = new ControlBusy { what = "treat", targetId = remedy.id, progress = 0, duration = Health.TREAT_TICKS };
            return (remedy.kind == "medicine") ? "taking medicine" : "bandaging";
        }
        if (Js.T(item) && !Js.T(item.reservedBy))
        {
            return take(state, s, c, item);
        }
        if ((Water.sourceAt(state, target.x, target.y) == "tap") && (s.hydration < (Water.HYDRATION_MAX - TAP_THIRST)))
        {
            c.busy = new ControlBusy { what = "drink", targetId = $"{Js.Str(target.x)},{Js.Str(target.y)}", progress = 0, duration = Water.DRINK_TICKS };
            return "drinking from the tap";
        }
        var crate = state.furniture.find((f) => ((f.x == target.x) && (f.y == target.y)) && ((f.kind == "crate") || (f.kind == "tree")));
        if (Js.T(crate))
        {
            return search(s, c, crate);
        }
        var source = Water.sourceAt(state, target.x, target.y);
        if (Js.T(source))
        {
            c.busy = new ControlBusy { what = "drink", targetId = $"{Js.Str(target.x)},{Js.Str(target.y)}", progress = 0, duration = Water.DRINK_TICKS };
            return "drinking";
        }
        var bed = FurnitureMod.furnitureAt(state, target.x, target.y);
        if ((bed?.kind == "decor") && (bed.look == "oven"))
        {
            if (!TimelineMod.powerOn(state))
            {
                return "the oven's dead: no power";
            }
            var food = s.pack.map((id) => Items.getItem(state, id)).find((i) => i?.kind == "food");
            if (!Js.T(food))
            {
                return "nothing in your bag to cook";
            }
            c.busy = new ControlBusy { what = "cook", targetId = food.id, progress = 0, duration = COOK_TICKS };
            return "cooking";
        }
        var sleepable = (bed?.kind == "bed") || ((bed?.kind == "decor") && (((bed.look == "bed") || (bed.look == "cot")) || (bed.look == "sofa")));
        if ((Js.T(Js.T(bed) && Js.T(sleepable))) && Space.samePoint(bed, s.pos))
        {
            return lieDown(s, c, bed.id);
        }
        if (Js.T(Js.T(bed) && Js.T(sleepable)))
        {
            s.pos = new Point { x = Js.I(bed.x), y = Js.I(bed.y) };
            s.at = Motion.centre(s.pos);
            return "getting into bed";
        }
        var carried = Js.T(s.carrying) ? Items.getItem(state, s.carrying) : null;
        if (Js.T(carried))
        {
            s.carrying = null;
            Items.dropItem(state, carried, target);
            return $"put down the {Js.Str(carried.kind)}";
        }
        if (Space.samePoint(target, s.pos))
        {
            var why = Handiwork.canForage(state, s);
            if (Time.isNight(state.tick) || ((s.rest < Rest.TIRED) && ((why != null) || (s.satiety >= Food.HUNGRY))))
            {
                return lieDown(s, c, "floor");
            }
            if (Js.T(why))
            {
                return why;
            }
            c.busy = new ControlBusy { what = "forage", targetId = "here", progress = 0, duration = Handiwork.FORAGE_TICKS };
            return "foraging";
        }
        return null;
    }

    /// <summary>Work the tile in front: repair, barricade, reinforce or build (handiwork.ts). Returns what happened, or why not (null: no).</summary>
    public static string work(SimState state, Survivor s, Point target)
    {
        var c = Js.NN(state.control);
        if (Js.T(s.move))
        {
            return "not while moving";
        }
        if (!nextTo(s.pos, target))
        {
            return "too far to reach";
        }
        var face = towards(s.pos, target);
        if (Js.T(face))
        {
            c.facing = face;
        }
        var started = Handiwork.startWork(state, s, target);
        if (started is string why)
        {
            return why;
        }
        var job = (WorkStart)started;
        c.busy = new ControlBusy { what = job.what, targetId = job.targetId, duration = job.duration, progress = 0 };
        return (job.what == "repair") ? "repairing" : "building";
    }

    public static string lieDown(Survivor s, Control c, string where)
    {
        if (s.rest >= Rest.REST_MAX)
        {
            return "not tired";
        }
        c.busy = new ControlBusy { what = "sleep", targetId = where, progress = s.rest, duration = Rest.REST_MAX };
        return (where == "floor") ? "sleeping on the floor" : "asleep in bed";
    }

    public static string take(SimState state, Survivor s, Control c, Item item)
    {
        if ((item.kind == "food") || (item.kind == "meal"))
        {
            c.busy = new ControlBusy { what = "eat", targetId = item.id, progress = 0, duration = Food.EAT_TICKS };
            return "eating";
        }
        var slot = Weapons.isWieldKind(item.kind) ? "weapon" : (Clothing.isGarment(item.kind) ? Js.Get(Clothing.SLOT_OF, item.kind) : ((item.kind == "cart") ? "cart" : ((/*TODO in*/Js.In(item.kind, Skills.TOOL_FOR)) ? "tool" : "carrying")));
        if ((slot == "carrying") && Inventory.fitsInPack(state, s, item))
        {
            Inventory.stow(state, s, item);
            return $"put the {Items.nameOf(item)} in your bag";
        }
        var old = Js.T(Js.GetProp<string?>(s, slot)) ? Items.getItem(state, Js.GetProp<string?>(s, slot)!) : null;
        var where = Js.NN(item.pos);
        item.pos = null;
        item.carriedBy = s.id;
        Js.SetProp(s, slot, item.id);
        if (Js.T(old))
        {
            Items.dropItem(state, old, where);
        }
        return (slot == "carrying") ? $"picked up the {Items.nameOf(item)}" : $"took the {Items.nameOf(item)}";
    }

    public static string? search(Survivor s, Control c, Furniture crate)
    {
        if (crate.loot <= 0)
        {
            return (crate.kind == "tree") ? null : "searched: what's left is in the loot list";
        }
        c.busy = new ControlBusy { what = "search", targetId = crate.id, progress = 0, duration = Jobs.SCAVENGE_WORK };
        s.note = (crate.kind == "tree") ? "chopping" : "searching";
        return s.note;
    }

    /// <summary>The controlled survivor died or left: control ends.</summary>
    public static void reconcileControl(SimState state)
    {
        if (Js.T(state.control) && !Js.T(controlled(state)))
        {
            if (!Js.T(state.succession))
            {
                Events.logEvent(state, "You are no longer in control of anyone");
            }
            state.control = null;
        }
    }
}
