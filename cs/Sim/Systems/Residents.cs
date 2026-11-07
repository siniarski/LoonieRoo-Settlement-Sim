// Ported from src/sim/residents.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Residents: a neighbouring settlement's people, once its compound has unfolded (colonies.ts).
//
// One resident per head (synced at unfolding and every dawn: newcomers appear at the
// door, the dead and the departed vanish). They potter about inside their walls. If
// their settlement counts you an enemy, they come out at your people within
// HOSTILE_SIGHT and hit them; hitting one of a peaceful settlement's makes it an enemy
// on the spot. A resident who dies is one fewer at home, and leaves a body.
// In person you can talk to them (warms their settlement, a little, once a day), trade
// (hand over what you carry for food from their stores), and, when their settlement
// thinks well of you, ask one to come with you.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Residents
{

    public const int RESIDENT_HEALTH = 100;

    public const int RESIDENT_TICKS_PER_TILE = 5;

    public const int HOSTILE_SIGHT = 8;

    public const int RESIDENT_DAMAGE = 25;

    public const int RESIDENT_HIT_EVERY = 12;

    /// <summary>How far under HOSTILE their settlement's opinion of you drops when you hit one of them.</summary>
    public const int ATTACKED_BELOW_HOSTILE = 20;

    public const int TALK_REP = 2;

    /// <summary>Food from their stores for each kind of thing you hand over.</summary>
    public static readonly JsRecord<double> TRADE_FOOD = new() { ["medicine"] = 3, ["ammo"] = 2, ["gun"] = 4, ["weapon"] = 2, ["bandage"] = 1, ["coat"] = 2, ["armour"] = 3, ["boots"] = 1, ["book"] = 1, ["scrap"] = 1, ["fuel"] = 1, ["wood"] = 1, ["cloth"] = 1 };

    public static readonly List<string> NAMES = ["Ada", "Bram", "Cora", "Dov", "Esme", "Fitz", "Greta", "Hank", "Ines", "Jules", "Kai", "Lotte", "Mabel", "Ned", "Olga", "Pim"];

    public static List<Resident> residentsOf(SimState state, Settlement n)
    {
        return state.residents.filter((r) => r.settlementId == n.id);
    }

    public static Resident? residentAt(SimState state, Point p)
    {
        return state.residents.find((r) => (r.pos.x == p.x) && (r.pos.y == p.y));
    }

    /// <summary>One resident a head: newcomers at the door, and the last ones gone when there are fewer.</summary>
    public static void syncResidents(SimState state, Settlement n)
    {
        var here = residentsOf(state, n);
        var want = (n.unfolded && !Colonies.isFallen(n)) ? n.people : 0;
        foreach (var r in Js.Iter(here.slice(want)))
        {
            state.residents.splice(state.residents.indexOf(r), 1);
        }
        var box = Colonies.compound(n);
        for (var i = here.length; i < want; i++)
        {
            var id = $"r{Js.Str(state.nextId++)}";
            Point spot = new Point { x = Js.I((box.x0 + 1) + (i % JsMath.max(1, (box.x1 - box.x0) - 1))), y = Js.I((box.y1 - 1) - (JsMath.floor(((double)(i) / (JsMath.max(1, (box.x1 - box.x0) - 1)))) % 3)) };
            var where = Grids.inBounds(state.grid, spot.x, spot.y) ? spot : new Point { x = Js.I(n.x), y = Js.I(n.y) };
            state.residents.push(new() { id = id, settlementId = n.id, name = Js.NN(Js.Get(NAMES, Hash.idNumber(id) % NAMES.length)), pos = where, move = null, path = [], health = RESIDENT_HEALTH, cooldown = 0, nextThinkTick = 0, at = Motion.placedAt(where).at, was = Motion.placedAt(where).was });
        }
    }

    public static void updateResidents(SimState state)
    {
        foreach (var r in Js.Iter((state.residents).ToList()))
        {
            var n = Settlements.getSettlement(state, r.settlementId);
            if (!Js.T(n))
            {
                state.residents.splice(state.residents.indexOf(r), 1);
                continue;
            }
            r.cooldown = JsMath.max(0, r.cooldown - 1);
            if (Js.T(r.move))
            {
                r.move.progress += 1;
                if (r.move.progress < r.move.duration)
                {
                    continue;
                }
                r.pos = r.move.to;
                r.move = null;
            }
            if (Settlements.isHostile(n) && attack(state, r, n))
            {
                continue;
            }
            if ((state.tick >= r.nextThinkTick) && (r.path.length == 0))
            {
                r.nextThinkTick = (state.tick + 40) + JsMath.floor(((double)(Hash.hashRoll(Hash.idNumber(r.id), state.tick)) * (60)));
                r.path = potter(state, r, n);
            }
            step(r);
        }
    }

    public static void step(Resident r)
    {
        var next = r.path.shift();
        if (Js.T(next))
        {
            r.move = new Movement { from = Js.Clone(r.pos), to = next, progress = 0, duration = RESIDENT_TICKS_PER_TILE };
        }
    }

    /// <summary>A short walk to somewhere else inside the walls.</summary>
    public static List<Point> potter(SimState state, Resident r, Settlement n)
    {
        var box = Colonies.compound(n);
        double roll(double salt)
        {
            return Hash.hashRoll(Hash.idNumber(r.id), state.tick, salt);
        }
        Point to = new Point { x = Js.I((box.x0 + 1) + JsMath.floor(((double)(roll(1)) * (((box.x1 - box.x0) - 1))))), y = Js.I((box.y0 + 2) + JsMath.floor(((double)(roll(2)) * (JsMath.max(1, (box.y1 - box.y0) - 2))))) };
        if (state.residents.some((o) => ((o != r) && (o.pos.x == to.x)) && (o.pos.y == to.y)))
        {
            return (List<Point>)[];
        }
        return (Pathfinding.findPath(state.grid, r.pos, to) ?? []).slice(0, 8);
    }

    /// <summary>An enemy's resident goes for the nearest of your people in sight. Returns true if it took the tick.</summary>
    public static bool attack(SimState state, Resident r, Settlement n)
    {
        double near(Survivor s)
        {
            return JsMath.max(JsMath.abs(s.pos.x - r.pos.x), JsMath.abs(s.pos.y - r.pos.y));
        }
        var target = Js.Get(state.survivors.filter((s) => near(s) <= HOSTILE_SIGHT).sort((a, b) => near(a) - near(b)), 0);
        if (!Js.T(target))
        {
            return false;
        }
        if (Motion.inReach(target, r, Motion.REACH, state.grid))
        {
            if (r.cooldown > 0)
            {
                return true;
            }
            r.cooldown = RESIDENT_HIT_EVERY;
            target.health = JsMath.max(0, target.health - Clothing.armourDamage(state, target, RESIDENT_DAMAGE));
            target.lastHurtBy = $"someone from {Js.Str(n.name)}";
            target.hurtTick = state.tick;
            return true;
        }
        if ((state.tick >= r.nextThinkTick) || (r.path.length == 0))
        {
            r.nextThinkTick = state.tick + 10;
            r.path = (Pathfinding.findPathToAny(state.grid, r.pos, Grids.neighbours4(state.grid, target.pos.x, target.pos.y)) ?? []).slice(0, 6);
        }
        step(r);
        return true;
    }

    /// <summary>One of yours hits a resident. Peaceful places turn on you. Returns true if it died.</summary>
    public static bool hitResident(SimState state, Resident r, double damage, Survivor by)
    {
        var n = Settlements.getSettlement(state, r.settlementId);
        if (Js.T(n) && !Settlements.isHostile(n))
        {
            Settlements.adjustReputation(state, n, (Settlements.HOSTILE - ATTACKED_BELOW_HOSTILE) - n.reputation);
            Events.logEvent(state, $"{Js.Str(by.name)} attacked {Js.Str(r.name)} of {Js.Str(n.name)}. They'll fight back now.");
        }
        r.health -= damage;
        if (r.health > 0)
        {
            return false;
        }
        state.residents.splice(state.residents.indexOf(r), 1);
        if (Js.T(n))
        {
            n.people = JsMath.max(0, n.people - 1);
        }
        Corpses.leaveCorpse(state, r.pos, r.name);
        Events.logEvent(state, $"{Js.Str(by.name)} killed {Js.Str(r.name)}{Js.Str(Js.T(n) ? Js.Str($" of {Js.Str(n.name)}") : Js.Str(""))}");
        return true;
    }

    /// <summary>Talking to a resident in person: hand over what you carry for food, or ask them to come with you if their settlement thinks well of you, or just pass the time. Returns null if something happened, or why not.</summary>
    public static string? meetResident(SimState state, Survivor you, Resident r)
    {
        var n = Settlements.getSettlement(state, r.settlementId);
        if (!Js.T(n))
        {
            return "they have nowhere to be from";
        }
        if (Settlements.isHostile(n))
        {
            return $"{Js.Str(r.name)} won't talk to you";
        }
        var carried = Js.T(you.carrying) ? Items.getItem(state, you.carrying) : null;
        var worth = Js.T(carried) ? (Js.Q(Js.Get(TRADE_FOOD, carried.kind), 0)) : 0;
        if (Js.T(carried) && (worth > 0))
        {
            if (n.food < worth)
            {
                return $"{Js.Str(n.name)} can't spare {Js.Str(worth)} food";
            }
            you.carrying = null;
            Items.removeItem(state, carried);
            n.food -= worth;
            for (var i = 0; i < worth; i++)
            {
                Items.spawnItem(state, "food", you.pos.x, you.pos.y);
            }
            Settlements.adjustReputation(state, n, 3);
            Events.logEvent(state, $"{Js.Str(r.name)} of {Js.Str(n.name)} took the {Js.Str(carried.kind)} for {Js.Str(worth)} food");
            return null;
        }
        if ((n.reputation >= Settlements.FRIENDLY) && (n.people > 3))
        {
            state.residents.splice(state.residents.indexOf(r), 1);
            n.people -= 1;
            var s = Survivors.addSurvivor(state, r.pos.x, r.pos.y, r.name);
            s.order = new Order { kind = "follow" };
            Events.logEvent(state, $"{Js.Str(r.name)} left {Js.Str(n.name)} to come with you");
            return null;
        }
        if (n.lastTalk == Time.dayOf(state.tick))
        {
            return $"{Js.Str(r.name)} has said all they want to today";
        }
        n.lastTalk = Time.dayOf(state.tick);
        Settlements.adjustReputation(state, n, TALK_REP);
        if (Errands.maybeAsk(state, n, Hash.idNumber(r.id)) != null)
        {
            return null;
        }
        Events.logEvent(state, $"{Js.Str(r.name)} of {Js.Str(n.name)} talked with {Js.Str(you.name)} a while ({Js.Str(n.name)} needs {Js.Str((n.food < ((double)(n.people) * (2))) ? Js.Str("food") : Js.Str("nothing much"))})");
        return null;
    }
}
