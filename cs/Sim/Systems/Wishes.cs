// Ported from src/sim/wishes.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Wishes: small everyday wants (like Sims 3's), one at a time per survivor.
//
// Now and then (a hash of who and the hour, never the sim RNG) someone without a wish
// picks one that fits how they are: a hot meal if there's a stove, a real bed after a
// night on the floor, a coat when it turns cold, to see someone they like, a grave to
// visit, a quiet day after a death. Each is checked against things the sim already
// tracks (mostly thoughts gained since the wish began), and lasts WISH_TICKS.
//   Met: a small lift (mood, resolve).
//   Promised (you chose to see it done) and met: a bigger lift, and they think better of you.
//   Promised and not met in time: let down (mood, resolve, and their opinion of you).
// Nobody under your control has wishes of their own.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Wishes
{
    static Wishes()
    {
        Time.onClock(() => WISH_TICKS = Time.DAY_LENGTH);
    }

    public static double WISH_TICKS = Time.DAY_LENGTH;

    /// <summary>Each hour, the chance someone without a wish gets one.</summary>
    public const double WISH_CHANCE = 0.2;

    public const int WISH_RESOLVE = 2;

    public const int PROMISE_RESOLVE = 8;

    public const int BROKEN_RESOLVE = 5;

    public const int PROMISE_OPINION = 10;

    /// <summary>Within this many tiles (Chebyshev), both awake, counts as seeing someone.</summary>
    public const int SEE_RANGE = 2;

    /// <summary>Did they gain this thought since the wish began (that tick included)? (A thought's start is its end less its length.)</summary>
    public static bool gotThought(Survivor s, string kind, double since)
    {
        return s.thoughts.some((t) => (t.kind == kind) && ((t.until - Js.Get(Mood.THOUGHTS, kind).duration) >= since));
    }

    public static bool near(Survivor a, Survivor b)
    {
        return JsMath.max(JsMath.abs(a.pos.x - b.pos.x), JsMath.abs(a.pos.y - b.pos.y)) <= SEE_RANGE;
    }

    /// <summary>The wishes that fit this survivor right now, in a fixed order.</summary>
    public static List<CandidatesResult> candidates(SimState state, Survivor s)
    {
        List<CandidatesResult> @out = [];
        bool has(string kind)
        {
            return s.thoughts.some((t) => t.kind == kind);
        }
        if (FurnitureMod.ofKind(state, "stove").length > 0)
        {
            @out.push(new() { kind = "hotMeal", targetId = null });
        }
        if (has("sleptOnFloor") || (s.comfort < 50))
        {
            @out.push(new() { kind = "bed", targetId = null });
        }
        if (!Js.T(s.body) && (((Seasons.seasonOf(state.tick) == "autumn") || (Seasons.seasonOf(state.tick) == "winter")) || state.settings.winter))
        {
            @out.push(new() { kind = "coat", targetId = null });
        }
        if (!Js.T(s.weapon) && (s.priorities["guard"] > 0))
        {
            @out.push(new() { kind = "weapon", targetId = null });
        }
        var friend = Js.Get(state.survivors.filter((o) => (o != s) && ((Js.Q(Js.Get(s.opinions, o.id), 0)) >= 10)).sort((a, b) => (((Js.Q(Js.Get(s.opinions, b.id), 0)) - (Js.Q(Js.Get(s.opinions, a.id), 0))) is var __or0 && Js.T(__or0) ? __or0 : a.id.localeCompare(b.id))), 0);
        var you = ControlMod.controlled(state);
        var wanted = ((Js.T(you) && (you != s)) && ((Js.Q(Js.Get(s.opinions, you.id), 0)) >= 0)) ? you : friend;
        if (Js.T(wanted) && !near(s, wanted))
        {
            @out.push(new() { kind = "see", targetId = wanted.id });
        }
        if ((has("lostFriend") || has("sawDeath")) && state.roomDesignations.some((d) => d.purpose == "graveyard"))
        {
            @out.push(new() { kind = "grave", targetId = null });
        }
        if (has("sawDeath") || has("attacked"))
        {
            @out.push(new() { kind = "quiet", targetId = null });
        }
        if ((s.kills >= 3) || s.traits.includes("hardened"))
        {
            @out.push(new() { kind = "kill", targetId = null });
        }
        if (s.social < 40)
        {
            @out.push(new() { kind = "company", targetId = null });
        }
        if (s.comfort < 40)
        {
            @out.push(new() { kind = "comfort", targetId = null });
        }
        return @out;
    }

    /// <summary>Met? (For 'quiet', met means the time ran out with nothing bad happening.)</summary>
    public static bool met(SimState state, Survivor s, Wish w)
    {
        switch (w.kind)
        {
            case "hotMeal":
                {
                    return gotThought(s, "hotMeal", w.since);
                }
            case "bed":
                {
                    return gotThought(s, "sleptInBed", w.since);
                }
            case "coat":
                {
                    return s.body != null;
                }
            case "weapon":
                {
                    return s.weapon != null;
                }
            case "see":
                {
                    {
                    var o = state.survivors.find((x) => x.id == w.targetId);
                    return (((o != null) && !Js.T(o.sleep?.asleep)) && !Js.T(s.sleep?.asleep)) && near(s, o);
                }
                }
            case "grave":
                {
                    return gotThought(s, "paidRespects", w.since);
                }
            case "quiet":
                {
                    return (((state.tick >= w.until) && !gotThought(s, "sawDeath", w.since)) && !gotThought(s, "attacked", w.since)) && !gotThought(s, "lostFriend", w.since);
                }
            case "kill":
                {
                    return gotThought(s, "bloodUp", w.since);
                }
            case "company":
                {
                    return s.social >= 60;
                }
            case "comfort":
                {
                    return s.comfort >= 60;
                }
        }
        throw new InvalidOperationException("unreachable: every case returns");
    }

    public static string describeWish(SimState state, Wish w)
    {
        var who = Js.T(w.targetId) ? state.survivors.find((x) => x.id == w.targetId) : null;
        return new JsRecord<string> { ["hotMeal"] = "a hot meal", ["bed"] = "a night in a real bed", ["coat"] = "a coat", ["weapon"] = "a weapon", ["see"] = (Js.T(who) && (state.control?.survivorId == who.id)) ? "a word with you" : $"to see {Js.Str(who?.name ?? "someone")}", ["grave"] = "to visit the graves", ["quiet"] = "a quiet day (no deaths, no attacks)", ["kill"] = "to put one of them down", ["company"] = "some company", ["comfort"] = "somewhere decent to sleep" }[Js.Str(w.kind)];
    }

    /// <summary>Every 10 ticks: check wishes; on the hour, maybe make new ones.</summary>
    public static void updateWishes(SimState state)
    {
        if ((state.tick % 10) != 0)
        {
            return;
        }
        foreach (var s in Js.Iter(state.survivors))
        {
            if (state.control?.survivorId == s.id)
            {
                s.wish = null;
                continue;
            }
            var w = s.wish;
            if (Js.T(w))
            {
                if (met(state, s, w))
                {
                    grant(state, s, w);
                }
                else if (state.tick >= w.until)
                {
                    expire(state, s, w);
                }
                continue;
            }
            if (((state.tick % Time.TICKS_PER_HOUR) != 0) || (s.childUntil != null))
            {
                continue;
            }
            if (Hash.hashRoll(Hash.idNumber(s.id), state.tick, 0x3a5) >= WISH_CHANCE)
            {
                continue;
            }
            var options = candidates(state, s);
            var pick = Js.Get(options, JsMath.floor(((double)(Hash.hashRoll(Hash.idNumber(s.id), state.tick, 0x3a6)) * (options.length))));
            if (Js.T(pick))
            {
                s.wish = Js.With(Js.CopyAs<Wish>(pick), __s1 => { __s1.since = state.tick; __s1.until = state.tick + WISH_TICKS; __s1.promised = false; });
            }
        }
    }

    public static void grant(SimState state, Survivor s, Wish w)
    {
        s.wish = null;
        Mood.addThought(state, s, w.promised ? "promiseKept" : "wishMet");
        Resolve.steel(s, w.promised ? PROMISE_RESOLVE : WISH_RESOLVE);
        var you = ControlMod.controlled(state);
        if ((Js.T(w.promised && Js.T(you))) && (you != s))
        {
            s.opinions[Js.Idx(you.id)] = JsMath.min(100, (Js.Q(Js.Get(s.opinions, you.id), 0)) + PROMISE_OPINION);
        }
        if (w.promised)
        {
            Events.logEvent(state, $"A promise kept: {Js.Str(s.name)} got {Js.Str(describeWish(state, w))}");
        }
    }

    public static void expire(SimState state, Survivor s, Wish w)
    {
        s.wish = null;
        if (!w.promised)
        {
            return;
        }
        Mood.addThought(state, s, "promiseBroken");
        Resolve.steel(s, -BROKEN_RESOLVE);
        var you = ControlMod.controlled(state);
        if (Js.T(you) && (you != s))
        {
            s.opinions[Js.Idx(you.id)] = JsMath.max(-100, (Js.Q(Js.Get(s.opinions, you.id), 0)) - PROMISE_OPINION);
        }
        Events.logEvent(state, $"A promise broken: {Js.Str(s.name)} never got {Js.Str(describeWish(state, w))}");
    }

    /// <summary>You promise to see someone's wish done. Returns an error, or null.</summary>
    public static string? promise(SimState state, Survivor s)
    {
        if (!Js.T(s.wish))
        {
            return $"{Js.Str(s.name)} doesn't want anything just now";
        }
        if (s.wish.promised)
        {
            return "already promised";
        }
        s.wish.promised = true;
        Events.logEvent(state, $"You promised {Js.Str(s.name)} {Js.Str(describeWish(state, s.wish))}");
        return null;
    }
}
