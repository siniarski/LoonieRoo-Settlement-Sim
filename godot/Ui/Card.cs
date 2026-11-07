// The character card's plain functions of state (src/ui/card.ts): a survivor's conditions, need
// bars, mood tooltip and activity. The panels that show them are in Hud.cs.

using System;
using System.Collections.Generic;
using System.Linq;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Ui;

public sealed record Condition(string id, string label, string glyph, string tone, string about);

public sealed record Bar(string label, int pct, string kind, double low, double crit);

public static class Card
{
    /// <summary>Everything notable about a survivor right now, worst first.</summary>
    public static List<Condition> conditionsOf(SimState state, Survivor s)
    {
        var @out = new List<Condition>();
        void add(string id, string label, string glyph, string tone, string about) => @out.Add(new Condition(id, label, glyph, tone, about));
        if (s.bleeding) add("bleeding", "Bleeding", "✚", "bad", "Losing blood until a bandage is on it.");
        if (s.bitten != null) add("bitten", "Bitten", "☣", "bad", $"Bitten on the {s.bitten}. Taking it off might still save them.");
        else if (s.infection != null) add("infected", "Infected", "☣", "bad", "The fever is in them. It ends one way.");
        if (Health.isCritical(s.health)) add("critical", "Badly hurt", "♥", "bad", "Close to death: slow, and needs rest and treatment.");
        else if (Health.isHurt(s.health)) add("hurt", "Hurt", "♥", "mild", "Walks and works slower until healed.");
        if (s.wound != null) add("wound", $"Hurt {s.wound.limb}", "✚", "mild", $"A wounded {s.wound.limb}, healing.");
        if (s.sick != null) add("sick", "Sick", "☁", "mild", "Something they ate or drank. It will pass.");
        double clutching = ControlMod.clutchedBy(state, s);
        if (clutching > 0)
        {
            double slowed = Math.Min(100 - ControlMod.CLUTCHED_MIN, ControlMod.CLUTCH_PCT * clutching);
            add("grabbed", clutching > 1 ? $"Grabbed ({clutching})" : "Grabbed", "✋", clutching > 1 ? "bad" : "mild", $"The dead within reach drag at you: {slowed}% slower. Hit them to break free.");
        }
        if (s.panic > BodyMod.PANICKING) add("panicking", "Panicking", "!", "bad", "Some swings miss, and it weighs on mood. Ebbs when the dead are gone.");
        if (s.satiety == 0) add("starving", "Starving", "◒", "bad", "Losing health for want of food.");
        else if (s.satiety < Food.HUNGRY) add("hungry", "Hungry", "◒", "mild", "Needs to eat soon.");
        if (state.harsh && s.hydration == 0) add("dehydrated", "Dehydrated", "◊", "bad", "Losing health for want of water.");
        else if (state.harsh && s.hydration < Water.THIRSTY) add("thirsty", "Thirsty", "◊", "mild", "Needs a drink, and it weighs on mood.");
        if (s.bodyHeat < BodyMod.FREEZING) add("freezing", "Freezing", "❄", "bad", "Losing health to the cold. Get indoors, by a fire, into a coat.");
        else if (s.bodyHeat < BodyMod.COLD) add("cold", "Cold", "❄", "mild", "Walks slower, and it weighs on mood.");
        if (s.wetness > BodyMod.SOAKED) add("soaked", "Soaked", "≈", "mild", "Wet through: cold comes quicker at night. Dries indoors or by warmth.");
        if (s.rest < Rest.TIRED) add("tired", s.rest == 0 ? "Exhausted" : "Tired", "z", s.rest == 0 ? "bad" : "mild", "Needs sleep.");
        if (s.endurance < BodyMod.WINDED) add("winded", "Winded", "~", "mild", "No running; swings come later and land softer. Comes back at rest.");
        if (Resolve.isBreaking(s)) add("breaking", "Breaking", "▼", "bad", "Close to giving up: they may walk away at dawn.");
        else if (Resolve.isSteeled(s)) add("steeled", "Steeled", "▲", "good", "Hard to shake: panic comes slower.");
        if (s.thoughts.Any((t) => t.kind == "irradiated")) add("irradiated", "Irradiated", "☢", "bad", Mood.THOUGHTS["irradiated"].label);
        if (Mood.isMiserable(state, s)) add("miserable", "Miserable", "▽", "mild", "Works slower until their mood lifts.");
        if (s.social < Wellbeing.LOW) add("lonely", "Lonely", "…", "mild", "Needs company and someone to talk to.");
        if (s.comfort < Wellbeing.LOW) add("uncomfortable", "Uncomfortable", "□", "mild", "Needs somewhere decent to sleep and sit.");
        foreach (var t in s.thoughts)
        {
            if (!Moodlets.BOOSTS.ContainsKey(t.kind) || @out.Any((c) => c.id == t.kind)) continue;
            var info = Mood.THOUGHTS[t.kind];
            var name = System.Text.RegularExpressions.Regex.Replace(info.label, @" \(.*\)$", "");
            add(t.kind, char.ToUpperInvariant(name[0]) + name[1..], info.effect > 0 ? "▲" : "▽", info.effect > 0 ? "good" : "mild", info.label);
        }
        int rank(string tone) => tone == "bad" ? 0 : tone == "mild" ? 1 : 2;
        return @out.OrderBy((c) => rank(c.tone)).ToList();
    }

    static int pct(double v, double max) => (int)Math.Max(0, Math.Min(100, Math.Round(100 * v / max, MidpointRounding.AwayFromZero)));

    /// <summary>The body's needs as bars (water only in a harsh game, where thirst counts).</summary>
    public static List<Bar> barsOf(SimState state, Survivor s)
    {
        var bars = new List<Bar>
        {
            new("Health", pct(s.health, Health.HEALTH_MAX), "health", 60, 30),
            new("Food", pct(s.satiety, Food.SATIETY_MAX), "food", pct(Food.HUNGRY, Food.SATIETY_MAX), 5),
        };
        if (state.harsh) bars.Add(new("Water", pct(s.hydration, Water.HYDRATION_MAX), "water", pct(Water.THIRSTY, Water.HYDRATION_MAX), 5));
        bars.Add(new("Rest", pct(s.rest, Rest.REST_MAX), "rest", pct(Rest.TIRED, Rest.REST_MAX), 5));
        bars.Add(new("Endurance", (int)s.endurance, "endurance", BodyMod.WINDED, 10));
        bars.Add(new("Warmth", (int)s.bodyHeat, "warmth", BodyMod.COLD, BodyMod.FREEZING));
        return bars;
    }

    /// <summary>"Mood 55: ate at a table +10, hurt -10" (a tooltip, and what the debug panel shows).</summary>
    public static string moodTitle(SimState state, Survivor s)
    {
        var factors = Mood.moodFactors(state, s).Select((f) => $"{f.Item1} {(f.Item2 > 0 ? "+" : "")}{f.Item2}").ToList();
        return $"Mood {Mood.mood(state, s)}{(Mood.isMiserable(state, s) ? " (miserable: works slower)" : "")}{(factors.Count > 0 ? $": {string.Join(", ", factors)}" : "")}";
    }

    static readonly Dictionary<string, string> BUSY = new()
    {
        ["eat"] = "eating", ["search"] = "searching", ["drink"] = "drinking", ["sleep"] = "asleep", ["treat"] = "treating", ["build"] = "building",
        ["repair"] = "repairing", ["forage"] = "foraging", ["cook"] = "cooking",
    };

    /// <summary>What they're doing, in a few words.</summary>
    public static string activity(SimState state, Survivor s)
    {
        if (state.control?.survivorId == s.id)
        {
            var busy = state.control.busy;
            if (busy != null) return $"{(BUSY.TryGetValue(busy.what, out var w) ? w : busy.what)}…";
            if (state.control.running == true && (state.control.dx != 0 || state.control.dy != 0)) return s.endurance < BodyMod.WINDED ? "too winded to run" : "running";
            return s.note ?? "";
        }
        if (s.meal != null) return s.meal.progress > 0 ? (s.meal.seat != null ? "eating at a table" : "eating") : s.carrying != null ? "taking food to a table" : "going to eat";
        if (s.treatment != null) return s.treatment.progress > 0 ? "treating themselves" : "going for a remedy";
        if (s.arming != null) return "going for a weapon";
        if (s.dressing != null) return "going for clothing";
        if (s.drink != null) return s.drink.progress > 0 ? "drinking" : "going for a drink";
        if (s.reading != null) return s.reading.progress > 0 ? "reading" : "going to read";
        if (s.childUntil != null && s.sleep == null && s.meal == null) return "playing (a child)";
        if (s.breakdown != null) return "having a breakdown";
        if (s.leisure != null) return s.leisure.progress > 0 ? "relaxing" : "going to relax";
        if (s.sleep != null && Health.isHurt(s.health)) return s.sleep.asleep ? "resting (hurt)" : "going to rest (hurt)";
        if (s.sleep != null) return s.sleep.asleep ? $"asleep ({(s.sleep.bedId != null ? "bed" : "floor")})" : "going to bed";
        var job = s.jobId != null ? Jobs.getJob(state, s.jobId) : null;
        if (job != null) return $"{job.type} ({job.x}, {job.y})";
        if (s.goal != null) return s.order?.kind == "follow" ? "following" : s.order?.kind == "loot" ? "going to loot" : s.order != null ? "going back to their post" : "walking (ordered)";
        return s.order != null ? Orders.describeOrder(s.order) : "idle";
    }
}
