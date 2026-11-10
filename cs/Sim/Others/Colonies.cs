// Ported from src/sim/colonies.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Neighbours living their own lives, off screen (simulation level of detail, like herds).
//
// Each settlement is a few numbers: people, food (in days of eating), defence (0-5).
// Every dawn, from a hash of its id and the day (never the sim RNG):
//   food    they farm (less in autumn, little in winter, less again in a nuclear winter), eat one a head,
//           and a little of the stores spoils
//   hunger  with nothing left, about one in seven starve; a starving one turns on you a little
//   growth  well fed, now and then someone new turns up
//   walls   a surplus goes into defences, now and then
//   the dead  a night attack can take one or two, less likely behind good walls
//   help    a fed one sends food to a starving neighbour by caravan (caravans.ts; the two warm
//           to each other when it arrives); allies send to a merely hungry one too
//   feuds   feuding neighbours raid each other now and then, starving or not
//   raids   a starving one raids the nearest neighbour for food, and both lose people
//   splits  past SPLIT_AT people, a would-be leader leaves with a few to found their own
//   falls   at none left it falls, and its compound is a ruin with the dead still in it
// When anyone of yours comes within UNFOLD_RADIUS, the settlement unfolds into a
// real compound: walls with a door, a bed a head, tables, and its people (residents.ts).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Colonies
{

    /// <summary>Food grown a head a day, by season.</summary>
    public static readonly JsRecord<double> GROWN = new() { ["spring"] = 1.25, ["summer"] = 1.4, ["autumn"] = 1.0, ["winter"] = 0.4 };

    /// <summary>Share of the stores that spoils each day.</summary>
    public const double SPOIL_SHARE = 0.04;

    public static readonly double STARVE_SHARE = ((double)(1) / (7));

    public const double GROW_CHANCE = 0.25;

    /// <summary>Food (in days) at which they count themselves well fed.</summary>
    public const int WELL_FED_DAYS = 4;

    public const int MAX_DEFENCE = 5;

    public const int SPLIT_AT = 16;

    public const int SPLIT_PEOPLE = 5;

    public const int MAX_SETTLEMENTS = 6;

    public const int UNFOLD_RADIUS = 24;

    /// <summary>What a starving neighbour thinks of you, each day.</summary>
    public const double DESPERATE_REP = -3;

    public static readonly List<string> SPLIT_NAMES = ["New Hope", "Ashford", "The Mill", "Kestrel Farm", "Lowgate", "Stillwater"];

    public static bool isFallen(Settlement n)
    {
        return n.people <= 0;
    }

    public static string hungerOf(Settlement n)
    {
        return (n.food <= 0) ? "starving" : ((n.food < ((double)(n.people) * (2))) ? "hungry" : "fed");
    }

    /// <summary>Runs every tick: the dawn round of off-screen life, and unfolding near your people.</summary>
    public static void updateColonies(SimState state)
    {
        if (state.settlements.length == 0)
        {
            return;
        }
        if ((state.tick > 0) && ((state.tick % Time.DAY_LENGTH) == 0))
        {
            dawn(state);
        }
        if ((state.tick % 50) == 0)
        {
            foreach (var n in Js.Iter(state.settlements))
            {
                if (!n.unfolded && someoneNear(state, n))
                {
                    unfold(state, n);
                }
            }
        }
    }

    public static void dawn(SimState state)
    {
        var day = Time.dayOf(state.tick);
        var season = Seasons.seasonOf(state.tick);
        foreach (var n in Js.Iter((state.settlements).ToList()))
        {
            if (isFallen(n))
            {
                continue;
            }
            var id = Hash.idNumber(n.id);
            double roll(double salt)
            {
                return Hash.hashRoll(id, day, salt);
            }
            var grown = ((double)(Js.Get(GROWN, season)) * ((state.settings.winter ? 0.5 : 1)));
            n.food = ((double)(JsMath.round(((double)(((((double)(n.food) * ((1 - SPOIL_SHARE))) + ((double)(n.people) * (grown))) - n.people)) * (10)))) / (10));
            if (n.food < 0)
            {
                n.food = 0;
                var lost = JsMath.max(1, JsMath.round(((double)(n.people) * (STARVE_SHARE))));
                n.people -= lost;
                news(state, n, $"{Js.Str(lost)} starved at {Js.Str(n.name)}");
                if (!Settlements.isHostile(n))
                {
                    Settlements.adjustReputation(state, n, DESPERATE_REP);
                }
            }
            else if ((n.food >= ((double)(n.people) * (WELL_FED_DAYS))) && (roll(1) < GROW_CHANCE))
            {
                n.people += 1;
                if ((n.defence < MAX_DEFENCE) && (roll(2) < 0.3))
                {
                    n.defence += 1;
                }
            }
            if (roll(3) < (((double)(((double)((0.06 + ((double)(day) * (0.002)))) * (state.settings.zombies))) / (100)) - ((double)(n.defence) * (0.015))))
            {
                var dead = (roll(4) < 0.3) ? 2 : 1;
                n.people -= dead;
                news(state, n, $"The dead got into {Js.Str(n.name)} in the night: {Js.Str(dead)} lost");
            }
            if (isFallen(n))
            {
                n.people = 0;
                Events.logEvent(state, $"{Js.Str(n.name)} has fallen. Nobody is left.");
                continue;
            }
            if (hungerOf(n) == "starving")
            {
                raidOrBeg(state, n, (__f0) => roll(__f0));
            }
            else if (hungerOf(n) == "hungry")
            {
                allyHelps(state, n, (__f0) => roll(__f0));
            }
            feud(state, n, (__f0) => roll(__f0));
            if ((n.people >= SPLIT_AT) && (state.settlements.length < MAX_SETTLEMENTS))
            {
                split(state, n, (__f0) => roll(__f0));
            }
        }
        foreach (var n in Js.Iter(state.settlements))
        {
            Residents.syncResidents(state, n);
        }
    }

    /// <summary>Raid a neighbour with your armed people (settled off screen, from their numbers): win and half their food comes home; lose and one of yours is badly hurt. Either way they count you an enemy. Once a day each. Returns an error, or null.</summary>
    public static string? raidSettlement(SimState state, string id)
    {
        var n = Settlements.getSettlement(state, id);
        if (!Js.T(n))
        {
            return "no such settlement";
        }
        if (isFallen(n))
        {
            return "nobody is left there";
        }
        if ((Time.dayOf(n.lastRaid) == Time.dayOf(state.tick)) && (n.lastRaid >= 0))
        {
            return "already raided today";
        }
        var armed = state.survivors.filter((s) => (Js.T(s.weapon) && (s.childUntil == null)) && !Js.T(s.sleep?.asleep));
        if (armed.length == 0)
        {
            return "nobody armed to send";
        }
        n.lastRaid = state.tick;
        var guns = armed.filter((s) => Weapons.heldGun(state, s) != null).length;
        var ours = ((double)(armed.length) * (2)) + ((double)(guns) * (2));
        var theirs = n.people + ((double)(n.defence) * (2));
        var roll = Hash.hashRoll(Hash.idNumber(n.id), state.tick, 0xa1);
        Settlements.adjustReputation(state, n, -60);
        if (((double)(ours) * ((0.5 + roll))) > theirs)
        {
            var took = JsMath.ceil(((double)(n.food) / (2)));
            n.food -= took;
            var killed = 1 + ((roll > 0.8) ? 1 : 0);
            n.people = JsMath.max(0, n.people - killed);
            var home = Js.NN(Js.Get(armed, 0)).pos;
            for (var i = 0; i < JsMath.min(10, took); i++)
            {
                Items.spawnItem(state, "food", home.x, home.y);
            }
            Events.logEvent(state, $"Your raid on {Js.Str(n.name)} worked: {Js.Str(JsMath.min(10, took))} food brought home, {Js.Str(killed)} of theirs dead");
        }
        else
        {
            var hurt = Js.NN(Js.Get(armed, JsMath.floor(((double)(roll) * (armed.length)))));
            hurt.health = JsMath.max(1, hurt.health - 150);
            hurt.lastHurtBy = "a raid gone wrong";
            hurt.hurtTick = state.tick;
            Events.logEvent(state, $"Your raid on {Js.Str(n.name)} failed. {Js.Str(hurt.name)} came back badly hurt");
        }
        if (isFallen(n))
        {
            Events.logEvent(state, $"{Js.Str(n.name)} has fallen. Nobody is left.");
        }
        Residents.syncResidents(state, n);
        return null;
    }

    /// <summary>A starving settlement: a fed neighbour may help; otherwise it may raid the nearest one with food.</summary>
    public static void raidOrBeg(SimState state, Settlement n, Func<double, double> roll)
    {
        var others = state.settlements.filter((o) => ((o != n) && !isFallen(o)) && (o.food > 0)).sort((a, b) => (JsMath.abs(a.x - n.x) + JsMath.abs(a.y - n.y)) - (JsMath.abs(b.x - n.x) + JsMath.abs(b.y - n.y)));
        var giver = others.find((o) => (o.food >= ((double)(o.people) * (WELL_FED_DAYS))) && ((Js.Q(Js.Get(o.ties, n.id), 0)) >= 0));
        if ((Js.T(giver) && (roll(5) < 0.5)) && !Caravans.caravansOf(state).some((c) => c.to == n.id))
        {
            var sent = JsMath.floor(((double)(giver.food) / (3)));
            giver.food -= sent;
            Caravans.sendCaravan(state, giver.id, n.id, new Point { x = Js.I(n.x), y = Js.I(n.y) }, sent);
            tie(state, giver, n, 10);
            news(state, n, $"{Js.Str(giver.name)} sent a caravan of food to a starving {Js.Str(n.name)}");
            return;
        }
        var target = Js.Get(others, 0);
        if (!Js.T(target) || (roll(6) > 0.4))
        {
            return;
        }
        var took = JsMath.ceil(((double)(target.food) / (2)));
        target.food -= took;
        n.food += took;
        n.people = JsMath.max(0, n.people - 1);
        target.people = JsMath.max(0, target.people - ((roll(7) < 0.5) ? 1 : 0));
        tie(state, target, n, -40);
        news(state, n, $"{Js.Str(n.name)} raided {Js.Str(target.name)} for food");
    }

    /// <summary>A hungry settlement's well-fed ally sends a little, by caravan (once one's on the way, no more).</summary>
    public static void allyHelps(SimState state, Settlement n, Func<double, double> roll)
    {
        if (Caravans.caravansOf(state).some((c) => c.to == n.id) || (roll(10) >= 0.5))
        {
            return;
        }
        var ally = state.settlements.find((o) => (((o != n) && !isFallen(o)) && (Settlements.relation(o, n) == "allies")) && (o.food >= ((double)(o.people) * (WELL_FED_DAYS))));
        if (!Js.T(ally))
        {
            return;
        }
        var sent = JsMath.floor(((double)(ally.food) / (4)));
        ally.food -= sent;
        Caravans.sendCaravan(state, ally.id, n.id, new Point { x = Js.I(n.x), y = Js.I(n.y) }, sent);
        news(state, n, $"{Js.Str(ally.name)} sent a caravan to its ally {Js.Str(n.name)}");
    }

    /// <summary>Feuding neighbours raid each other now and then (FEUD_RAID_CHANCE a day), fed or not.</summary>
    public const double FEUD_RAID_CHANCE = 0.08;

    public static void feud(SimState state, Settlement n, Func<double, double> roll)
    {
        var foe = state.settlements.find((o) => ((o != n) && !isFallen(o)) && (Settlements.relation(n, o) == "feud"));
        if (!Js.T(foe) || (roll(11) >= FEUD_RAID_CHANCE))
        {
            return;
        }
        var took = JsMath.ceil(((double)(foe.food) / (3)));
        foe.food -= took;
        n.food += took;
        var win = roll(12) < 0.5;
        (win ? foe : n).people = JsMath.max(0, (win ? foe : n).people - 1);
        tie(state, n, foe, -10);
        news(state, n, $"{Js.Str(n.name)} raided {Js.Str(foe.name)}, their old enemy: {Js.Str(took)} food taken, one of {Js.Str(win ? Js.Str(foe.name) : Js.Str(n.name))} dead");
    }

    /// <summary>Ties between two settlements, with the news when they become allies or fall out for good.</summary>
    public static void tie(SimState state, Settlement a, Settlement b, int by)
    {
        var before = Settlements.tieSettlements(a, b, by);
        var after = Settlements.relation(a, b);
        if (after == before)
        {
            return;
        }
        if (after == "allies")
        {
            Events.logEvent(state, $"{Js.Str(a.name)} and {Js.Str(b.name)} are allies now");
        }
        else if (after == "feud")
        {
            Events.logEvent(state, $"{Js.Str(a.name)} and {Js.Str(b.name)} have fallen out: a feud");
        }
        else
        {
            Events.logEvent(state, (before == "allies") ? $"{Js.Str(a.name)} and {Js.Str(b.name)} are no longer allies" : $"The feud between {Js.Str(a.name)} and {Js.Str(b.name)} has cooled");
        }
    }
    public static void tie(SimState state, Settlement a, Settlement b, double by) => tie(state, a, b, Js.I(by));

    public static void split(SimState state, Settlement n, Func<double, double> roll)
    {
        var angle = ((double)(((double)(roll(8)) * (JsMath.PI))) * (2));
        var __d0 = state.grid;
        var width = __d0.width;
        var height = __d0.height;
        var x = JsMath.max(8, JsMath.min(width - 9, JsMath.round(n.x + ((double)(JsMath.cos(angle)) * (30)))));
        var y = JsMath.max(8, JsMath.min(height - 9, JsMath.round(n.y + ((double)(JsMath.sin(angle)) * (30)))));
        var name = Js.NN(Js.Get(SPLIT_NAMES, state.settlements.length % SPLIT_NAMES.length));
        n.people -= SPLIT_PEOPLE;
        var food = JsMath.floor(((double)(n.food) / (3)));
        n.food -= food;
        var child = Settlements.newSettlement(state, name, x, y, SPLIT_PEOPLE, food, 0);
        child.reputation = n.reputation;
        tie(state, n, child, -10);
        Events.logEvent(state, $"A falling-out at {Js.Str(n.name)}: {Js.Str(SPLIT_PEOPLE)} left to found {Js.Str(name)}, near ({Js.Str(x)}, {Js.Str(y)})");
    }

    /// <summary>Only news you'd hear: from a neighbour that isn't hostile to you.</summary>
    public static void news(SimState state, Settlement n, string text)
    {
        if (!Settlements.isHostile(n))
        {
            Events.logEvent(state, text);
        }
    }

    public static bool someoneNear(SimState state, Settlement n)
    {
        return state.survivors.some((s) => JsMath.max(JsMath.abs(s.pos.x - n.x), JsMath.abs(s.pos.y - n.y)) <= UNFOLD_RADIUS);
    }

    /// <summary>The compound's box: wider for more people.</summary>
    public static HordeBox compound(Settlement n)
    {
        var w = JsMath.max(7, JsMath.min(15, 5 + JsMath.ceil(((double)(n.people) / (2)))));
        return new HordeBox { x0 = Js.I(n.x - JsMath.floor(((double)(w) / (2)))), y0 = Js.I(n.y - 3), x1 = Js.I(((n.x - JsMath.floor(((double)(w) / (2)))) + w) - 1), y1 = Js.I(n.y + 3) };
    }

    /// <summary>Builds the real thing: walls and a door, beds, food, and residents (or a ruin, if it fell).</summary>
    public static void unfold(SimState state, Settlement n)
    {
        n.unfolded = true;
        Worldgen.ensureGeneratedAround(state, new Point { x = Js.I(n.x), y = Js.I(n.y) });
        var box = compound(n);
        var __d1 = state;
        var grid = __d1.grid;
        var fallen = isFallen(n);
        Point door = new Point { x = Js.I(n.x), y = Js.I(box.y1) };
        for (var y = box.y0; y <= box.y1; y++)
        {
            for (var x = box.x0; x <= box.x1; x++)
            {
                if (!Grids.inBounds(grid, x, y))
                {
                    continue;
                }
                var old = FurnitureMod.furnitureAt(state, x, y);
                if (Js.T(old))
                {
                    FurnitureMod.removeFurniture(state, old);
                }
                state.items = state.items.filter((i) => (!Js.T(i.pos) || (i.pos.x != x)) || (i.pos.y != y));
                Grids.setGround(grid, Grids.tileIndex(grid, x, y), Grids.GROUNDS.indexOf("dirt"));
                var edge = (((x == box.x0) || (x == box.x1)) || (y == box.y0)) || (y == box.y1);
                var broken = fallen && (Hash.hashRoll(Hash.idNumber(n.id), x, y) < 0.25);
                Grids.setTile(grid, x, y, (Js.T(edge) && !Js.T(broken)) ? (((x == door.x) && (y == door.y)) ? "door" : "wall") : "floor");
                if (Js.T(edge))
                {
                    Zombies.evictZombies(state, new Point { x = Js.I(x), y = Js.I(y) });
                }
            }
        }
        state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
        List<Point> inside = [];
        for (var y = box.y0 + 1; y < box.y1; y++)
        {
            for (var x = box.x0 + 1; x < box.x1; x++)
            {
                if (Grids.inBounds(grid, x, y))
                {
                    inside.push(new Point { x = Js.I(x), y = Js.I(y) });
                }
            }
        }
        var top = inside.filter((p) => p.y == (box.y0 + 1));
        var beds = JsMath.min(top.length, JsMath.max(n.people, fallen ? 3 : 1));
        foreach (var p in Js.Iter(top.slice(0, beds)))
        {
            FurnitureMod.addFurniture(state, "bed", p.x, p.y);
        }
        var rest = inside.filter((p) => p.y > (box.y0 + 1));
        if (fallen)
        {
            foreach (var p in Js.Iter(rest.slice(0, 2)))
            {
                FurnitureMod.addFurniture(state, "crate", p.x, p.y).loot = 3;
            }
            foreach (var p in Js.Iter(rest.slice(-3)))
            {
                Dormant.add(state, new() { id = $"d{Js.Str(state.nextId++)}", x = Js.I(p.x), y = Js.I(p.y) });
            }
            return;
        }
        foreach (var p in Js.Iter(rest.slice(0, 2)))
        {
            FurnitureMod.addFurniture(state, "table", p.x, p.y);
        }
        Residents.syncResidents(state, n);
        Events.logEvent(state, $"You can see {Js.Str(n.name)}: {Js.Str(n.people)} people behind {Js.Str((n.defence >= 3) ? Js.Str("good") : Js.Str("thin"))} walls");
    }
}
