// The character sheet: who a survivor is and what state they are in, gathered from what the sim already keeps,
// as data (sections of rows, a severity on each, the body by part) and as plain text. Pure and read-only: no
// state of its own, nothing saved, nothing changed; the Tab screen's panel (godot/Ui/CharacterSheet.cs) and
// agents read the same Sheet.
//
// Every word and every severity comes from the sim's own thresholds (Health.HEALTH_HURT, BodyMod.COLD, Food.HUNGRY,
// Rest.TIRED, Mood.MISERABLE, Wounds.BLEED_EVERY ...): the sheet holds no numbers of its own beyond the 0..1 bars,
// so it cannot disagree with the simulation. Where the sim keeps nothing, the sheet says less rather than more:
//   - one wound at a time, on an arm or a leg, no side kept: the sheet puts it on one side by a hash of the
//     survivor and the wound's end tick (stable while it lasts; the sim's amputation chooses sides by a hash too);
//     if one arm (or leg) is already gone it is on the other. Head and torso have no wounds in the sim.
//   - no record of a bandage: a wound that is not bleeding was bandaged or stopped by itself, and the sheet says so.
//   - "dying" is critical health (Health.isCritical) with something still taking health: bleeding, starvation,
//     dehydration, freezing, sickness or an infection. The sim has no further health thresholds.
//   - pain is not modelled, so it is not listed.
#nullable disable warnings
using System.Text;
namespace LoonieRoo.Sim;

public enum Sev { Fine = 0, Note = 1, Warn = 2, Bad = 3, Critical = 4 }

public sealed class SheetRow
{
    public string label = "";
    /// <summary>What it is (for a condition: what it is doing to you).</summary>
    public string text = "";
    public Sev severity = Sev.Fine;
    /// <summary>0..1 when the row is a bar.</summary>
    public double? bar;
    /// <summary>What would help, where the sim knows (conditions).</summary>
    public string? help;
}

public sealed class SheetPart
{
    /// <summary>head, torso, armLeft, armRight, legLeft, legRight.</summary>
    public string id = "";
    public string name = "";
    public Sev severity = Sev.Fine;
    public bool missing;
    /// <summary>Where a missing limb is gone: wrist, elbow, shoulder, ankle, knee or hip; null when it is not missing.</summary>
    public string? level;
    /// <summary>Empty when intact.</summary>
    public List<string> notes = new();
}

public sealed class SheetSection
{
    public string id = "";
    public string title = "";
    public List<SheetRow> rows = new();
}

public sealed class Sheet
{
    public string name = "";
    /// <summary>fine, hurt, badly hurt or dying.</summary>
    public string healthWord = "fine";
    public Sev healthSeverity = Sev.Fine;
    /// <summary>Health as 0..1.</summary>
    public double healthBar = 1;
    public List<SheetPart> parts = new();
    /// <summary>identity, health, conditions, body, needs, mood, skills, wearing.</summary>
    public List<SheetSection> sections = new();

    public SheetSection Section(string id) => sections.First((s) => s.id == id);
    public SheetPart Part(string id) => parts.First((p) => p.id == id);
    /// <summary>Worst first; empty for a survivor with nothing wrong.</summary>
    public List<SheetRow> Conditions => Section("conditions").rows;

    public override string ToString() => CharacterSheet.text(this);
}

public static class CharacterSheet
{
    /// <summary>Everything the sheet says about this survivor, now.</summary>
    public static Sheet build(SimState state, Survivor s)
    {
        var sheet = new Sheet { name = s.name };
        var (word, sev) = healthWord(state, s);
        sheet.healthWord = word;
        sheet.healthSeverity = sev;
        sheet.healthBar = Clamp01(s.health / Health.HEALTH_MAX);
        sheet.parts = bodyParts(state, s, sev);
        sheet.sections.Add(identity(state, s));
        sheet.sections.Add(healthSection(sheet));
        sheet.sections.Add(conditions(state, s, word, sev));
        sheet.sections.Add(body(state, s));
        sheet.sections.Add(needs(state, s));
        sheet.sections.Add(moodSection(state, s));
        sheet.sections.Add(skills(s));
        sheet.sections.Add(wearing(state, s));
        return sheet;
    }

    /// <summary>The sheet as plain text, for agents and tests.</summary>
    public static string text(Sheet sheet)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CHARACTER SHEET: {sheet.name}");
        foreach (var section in sheet.sections)
        {
            sb.AppendLine();
            sb.AppendLine(section.id == "health" ? $"{section.title.ToUpperInvariant()}: {sheet.healthWord}" : section.title.ToUpperInvariant());
            if (section.rows.Count == 0) sb.AppendLine("  (nothing)");
            foreach (var r in section.rows)
            {
                var tag = r.severity == Sev.Fine ? "" : $"[{SevWord(r.severity)}] ";
                var bar = r.bar is double b ? $"{Bar(b)} " : "";
                sb.AppendLine($"  {tag}{r.label}: {bar}{r.text}".TrimEnd());
                if (r.help != null) sb.AppendLine($"      help: {r.help}");
            }
        }
        return sb.ToString().TrimEnd();
    }

    public static string SevWord(Sev s) => s switch { Sev.Critical => "critical", Sev.Bad => "bad", Sev.Warn => "warning", Sev.Note => "note", _ => "fine" };

    static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));
    static string Bar(double f) { int n = (int)Math.Round(Clamp01(f) * 10); return $"[{new string('#', n)}{new string('.', 10 - n)}] {(int)Math.Round(Clamp01(f) * 100)}%"; }

    /// <summary>A span of ticks in game time: minutes under two hours, then hours, then days.</summary>
    public static string span(double ticks)
    {
        double hours = Math.Max(0, ticks) / Time.TICKS_PER_HOUR;
        if (hours < 2) return $"{Math.Max(1, (int)Math.Round(hours * 60))} min";
        if (hours < 48) return $"{(int)Math.Round(hours)} h";
        return $"{(int)Math.Round(hours / Time.HOURS_PER_DAY)} days";
    }

    static string Signed(double v) => v > 0 ? $"+{v}" : $"{v}";
    static string Pct(double fraction) => $"{(int)Math.Round(fraction * 100)}%";

    static int InBag(SimState state, Survivor s, params string[] kinds) =>
        s.pack.Count((id) => Items.getItem(state, id) is { } i && kinds.Contains(i.kind));

    static string YouHave(int n, string thing) => n > 0 ? $"you carry {n}" : $"you have no {thing}";

    // ------------------------------------------------------------------ health

    /// <summary>Taking health away right now, so critical health is not just low but falling.</summary>
    public static bool losingHealth(SimState state, Survivor s) =>
        s.bleeding || s.satiety == 0 || (state.harsh && s.hydration == 0) || s.bodyHeat < BodyMod.FREEZING || s.sick != null || s.infection != null;

    public static (string word, Sev sev) healthWord(SimState state, Survivor s)
    {
        if (Health.isCritical(s.health)) return losingHealth(state, s) ? ("dying", Sev.Critical) : ("badly hurt", Sev.Bad);
        if (Health.isHurt(s.health)) return ("hurt", Sev.Warn);
        return ("fine", Sev.Fine);
    }

    static SheetSection healthSection(Sheet sheet)
    {
        var sec = new SheetSection { id = "health", title = "Health" };
        sec.rows.Add(new SheetRow { label = "Health", text = sheet.healthWord, severity = sheet.healthSeverity, bar = sheet.healthBar });
        foreach (var p in sheet.parts)
            sec.rows.Add(new SheetRow { label = p.name, text = p.notes.Count == 0 ? "intact" : string.Join("; ", p.notes), severity = p.severity });
        return sec;
    }

    /// <summary>Lost limbs, "armLeft:elbow" as (limb "arm", side "Left", level "elbow").</summary>
    static List<(string limb, string side, string level)> lost(Survivor s)
    {
        var o = new List<(string, string, string)>();
        foreach (var l in s.lostLimbs)
        {
            var colon = l.IndexOf(':');
            var key = colon < 0 ? l : l.Substring(0, colon);
            var level = colon < 0 ? "" : l.Substring(colon + 1);
            var limb = key.StartsWith("arm") ? "arm" : "leg";
            o.Add((limb, key.EndsWith("Right") ? "Right" : "Left", level));
        }
        return o;
    }

    /// <summary>Which side a wound or a bite is on: the one that is left if the other is gone, else a stable hash.</summary>
    static string SideOf(Survivor s, string limb, double salt)
    {
        var gone = lost(s).Where((l) => l.limb == limb).Select((l) => l.side).ToList();
        if (gone.Count == 1) return gone[0] == "Left" ? "Right" : "Left";
        return Hash.hashRoll(Hash.idNumber(s.id), salt, 0x7a1) < 0.5 ? "Left" : "Right";
    }

    static string LevelText(string limb, string level) => level switch
    {
        "wrist" or "ankle" => $"{(limb == "arm" ? "hand" : "foot")} missing at the {level}",
        "elbow" or "knee" => $"{(limb == "arm" ? "forearm" : "shin")} missing: gone below the {level}",
        "shoulder" or "hip" => $"whole {limb} missing, off at the {level}",
        _ => $"{limb} missing",
    };

    static List<SheetPart> bodyParts(SimState state, Survivor s, Sev health)
    {
        var parts = new List<SheetPart>
        {
            new() { id = "head", name = "Head" },
            new() { id = "torso", name = "Torso" },
            new() { id = "armLeft", name = "Left arm" }, new() { id = "armRight", name = "Right arm" },
            new() { id = "legLeft", name = "Left leg" }, new() { id = "legRight", name = "Right leg" },
        };
        SheetPart Of(string id) => parts.First((p) => p.id == id);
        // Health is not kept by place: the torso carries it.
        if (health != Sev.Fine) { var t = Of("torso"); t.severity = health; t.notes.Add($"{(health == Sev.Critical ? "dying" : health == Sev.Bad ? "badly hurt" : "hurt")}: health {Pct(s.health / Health.HEALTH_MAX)}"); }
        foreach (var (limb, side, level) in lost(s))
        {
            var p = Of($"{limb}{side}");
            p.missing = true;
            p.level = level;
            p.severity = Sev.Warn;
            p.notes.Add(LevelText(limb, level));
        }
        if (s.wound is { } w && (w.limb == "arm" || w.limb == "leg"))
        {
            var side = SideOf(s, w.limb, w.until);
            var p = Of($"{w.limb}{side}");
            if (!p.missing)
            {
                var bleed = s.bleeding
                    ? $"bleeding (stops by itself in {span(s.bleedUntil - state.tick)}; a bandage stops it now)"
                    : "not bleeding (bandaged, or it stopped by itself)";
                p.notes.Add($"wound from a blow: {bleed}; {(w.limb == "arm" ? "slows your work" : "slows your walking")} for {span(w.until - state.tick)} more");
                p.severity = (Sev)Math.Max((int)p.severity, (int)(s.bleeding ? Sev.Bad : Sev.Warn));
            }
        }
        if (s.infection != null && s.bitten != null && (s.bitten == "arm" || s.bitten == "leg"))
        {
            var side = SideOf(s, s.bitten, 0);
            var p = Of($"{s.bitten}{side}");
            if (!p.missing)
            {
                p.notes.Add($"bitten, infected: {Pct(s.infection.Value / Infection.INFECTION_TURN)} of the way to turning" + (Wounds.canAmputate(s) ? $"; it can still come off ({span(Wounds.amputateWindow() - s.infection.Value)} left)" : ""));
                p.severity = Sev.Critical;
            }
        }
        return parts;
    }

    // -------------------------------------------------------------- conditions

    sealed record Cond(Sev sev, int order, SheetRow row);

    static SheetSection conditions(SimState state, Survivor s, string hWord, Sev hSev)
    {
        var list = new List<Cond>();
        void Add(Sev sev, int order, string label, string text, string? help = null) =>
            list.Add(new Cond(sev, order, new SheetRow { label = label, text = text, severity = sev, help = help }));

        int bandages = InBag(state, s, "bandage"), medicine = InBag(state, s, "medicine"), food = InBag(state, s, "food", "meal");
        var coat = Clothing.wearing(state, s, "coat") != null;

        if (s.infection is double inf)
        {
            var left = Infection.INFECTION_TURN - inf;
            var help = $"medicine cures it ({YouHave(medicine, "medicine")})";
            if (Wounds.canAmputate(s)) help += $"; or the bitten {s.bitten} can still come off ({span(Wounds.amputateWindow() - inf)} left to decide)";
            else help += "; it has spread past amputating";
            Add(Sev.Critical, 0, "Infected", $"{Pct(inf / Infection.INFECTION_TURN)} of the way to turning: dead in {span(left)} unless cured. Mood {Signed(Infection.INFECTED_MOOD)}", help);
        }
        if (s.bleeding)
            Add(Sev.Bad, 1, "Bleeding", $"loses 1 health every {Wounds.BLEED_EVERY} ticks; stops by itself in {span(s.bleedUntil - state.tick)}",
                $"a bandage stops it ({YouHave(bandages, "bandage")})");
        if (hSev != Sev.Fine)
        {
            var slow = $"walks slower ({Health.HURT_STEP_TICKS} ticks a tile) and works at {Health.HURT_WORK_SKIP_EVERY - 1}/{Health.HURT_WORK_SKIP_EVERY} speed; mood {Signed(Mood.HURT_MOOD)}";
            var help = $"heals while asleep or awake and fed; a bandage restores {Pct((double)Health.BANDAGE_HEAL / Health.HEALTH_MAX)} ({YouHave(bandages, "bandage")})";
            if (hWord == "dying") Add(Sev.Critical, 2, "Dying", $"health {Pct(s.health / Health.HEALTH_MAX)} and still falling; {slow}", help);
            else if (hWord == "badly hurt") Add(Sev.Bad, 3, "Badly hurt", $"health {Pct(s.health / Health.HEALTH_MAX)}: wants a bed to recover in; {slow}", help);
            else Add(Sev.Warn, 4, "Hurt", $"health {Pct(s.health / Health.HEALTH_MAX)}; {slow}", help);
        }
        if (s.satiety == 0) Add(Sev.Critical, 5, "Starving", $"loses {Health.STARVE_DAMAGE} health every tick, and nothing heals", $"eat ({YouHave(food, "food")})");
        else if (Needs.isHungry(s)) Add(Sev.Warn, 6, "Hungry", $"stops healing while awake; mood {Signed(Mood.HUNGRY_MOOD)}", $"eat ({YouHave(food, "food")})");
        if (state.harsh)
        {
            if (s.hydration == 0) Add(Sev.Critical, 7, "Dehydrated", $"loses 1 health every {Water.DEHYDRATION_DAMAGE_EVERY} ticks", "drink at a well or a rain collector with water in it; open water is risky");
            else if (Water.isThirsty(s)) Add(Sev.Warn, 8, "Thirsty", $"mood {Signed(Water.THIRSTY_MOOD)}", "drink at a well or a rain collector with water in it; open water is risky");
        }
        if (s.bodyHeat < BodyMod.FREEZING)
            Add(Sev.Bad, 9, "Freezing", "health goes too, and every step is slower", "get indoors or beside a heater" + (coat ? "" : ", or put on a coat"));
        else if (s.bodyHeat < BodyMod.COLD)
            Add(Sev.Warn, 10, "Cold", $"walks a tick slower a tile; mood {Signed(Mood.THOUGHTS["cold"].effect)}", "get indoors or beside a heater" + (coat ? "" : ", or put on a coat"));
        if (s.sick != null)
            Add(Sev.Bad, 11, "Sick", $"slowed like the hurt, loses 1 health every {Corpses.SICK_DAMAGE_EVERY} ticks, and can spread it; mood {Signed(Corpses.SICK_MOOD)}", $"medicine cures it ({YouHave(medicine, "medicine")})");
        if (BodyMod.isPanicking(s))
            Add(Sev.Warn, 12, "Panicking", $"{Pct(BodyMod.PANIC_MISS)} of swings miss; mood {Signed(BodyMod.PANIC_MOOD)}", "it ebbs when the dead are out of range");
        if (BodyMod.isWinded(s))
            Add(Sev.Warn, 13, "Winded", $"no running; swings come {BodyMod.WINDED_WAIT} ticks later and land at {BodyMod.WINDED_DAMAGE_PCT}%", "rest: it comes back");
        if (s.rest < Rest.TIRED) Add(Sev.Warn, 14, "Exhausted", "needs sleep now; a bed is better than the floor", "sleep");
        else if (Needs.isTired(state, s)) Add(Sev.Note, 15, "Tired", "wants sleep; a bed is better than the floor", "sleep");
        if (s.wetness > BodyMod.DRENCHED) Add(Sev.Warn, 16, "Drenched", "a night outdoors will chill you even out of winter", "get indoors, or beside a heater: it dries faster");
        else if (s.wetness > BodyMod.SOAKED) Add(Sev.Note, 17, "Soaked", $"mood {Signed(Mood.THOUGHTS["soaked"].effect)}", "get indoors" + (coat ? "" : "; a coat keeps most rain off"));
        if (s.breakdown != null) Add(Sev.Bad, 18, "Breakdown", $"has dropped everything; it passes in {span(s.breakdown.until - state.tick)}");
        else if (Mood.isMiserable(state, s)) Add(Sev.Warn, 19, "Miserable", $"mood {Mood.mood(state, s)} of {Mood.MOOD_MAX}: works at {Health.HURT_WORK_SKIP_EVERY - 1}/{Health.HURT_WORK_SKIP_EVERY} speed; may break down", "see the thoughts under Mood");
        if (Resolve.isBreaking(s)) Add(Sev.Warn, 20, "Breaking", $"resolve {Math.Round(s.resolve)} of {Resolve.RESOLVE_MAX}: may freeze in a fight ({Pct(Resolve.FREEZE_CHANCE)} of swings) or walk away in the night", "a request seen through or someone found lifts it; a good day, a little");
        if (s.wound is { } w && Wounds.armSlowed(s) && w.limb == "arm") Add(Sev.Warn, 21, "Wounded arm", $"works slower for {span(w.until - state.tick)} more");
        if (s.wound is { } w2 && Wounds.legSlowed(s) && w2.limb == "leg") Add(Sev.Warn, 22, "Wounded leg", $"walks a tick slower a tile for {span(w2.until - state.tick)} more");
        var gone = lost(s);
        int arms = Math.Max(gone.Count((l) => l.limb == "arm"), Skills.hasTrait(s, "lostArm") ? 1 : 0);
        int legs = Math.Max(gone.Count((l) => l.limb == "leg"), Skills.hasTrait(s, "lostLeg") ? 1 : 0);
        if (arms > 0) Add(Sev.Warn, 23, arms > 1 ? "Missing both arms" : "Missing an arm", $"{Skills.TRAIT_INFO["lostArm"].about}; mood {Signed(Skills.TRAIT_INFO["lostArm"].mood)}");
        if (legs > 0) Add(Sev.Warn, 24, legs > 1 ? "Missing both legs" : "Missing a leg", $"{Skills.TRAIT_INFO["lostLeg"].about}; mood {Signed(Skills.TRAIT_INFO["lostLeg"].mood)}");

        var sec = new SheetSection { id = "conditions", title = "Conditions, worst first" };
        sec.rows.AddRange(list.OrderByDescending((c) => (int)c.sev).ThenBy((c) => c.order).Select((c) => c.row));
        return sec;
    }

    // ---------------------------------------------------------------- the rest

    static SheetSection identity(SimState state, Survivor s)
    {
        var sec = new SheetSection { id = "identity", title = "Identity" };
        void Row(string label, string text) => sec.rows.Add(new SheetRow { label = label, text = text });
        Row("Name", s.name);
        Row("Age", s.childUntil != null ? $"{Math.Floor(s.age)} (a child)" : s.age >= Family.ELDER ? $"{Math.Floor(s.age)} (elderly: works at 3/4 speed)" : $"{Math.Floor(s.age)}");
        var story = s.story;
        Row("Before", $"{StoryMod.STORY_JOBS[(int)story.job]} from {StoryMod.TOWNS[(int)story.town]}");
        int day = Time.dayOf(state.tick);
        Row("Days survived", $"{day - 1} (day {day} of the world)");
        if (s.kills > 0) Row("Kills", $"{s.kills}");
        if (s.traits.Count > 0)
            Row("Traits", string.Join("; ", s.traits.Select((t) => $"{Skills.TRAIT_INFO[t].label}: {Skills.TRAIT_INFO[t].about}")));
        Row("Proud of", $"{StoryMod.PRIDES[(int)story.pride]}.");
        Row("Regrets", $"{StoryMod.REGRETS[(int)story.regret]}.");
        return sec;
    }

    static SheetRow BarRow(string label, double value, double max, string word, Sev sev) =>
        new() { label = label, text = $"{Math.Round(value)} / {max}: {word}", severity = sev, bar = value / max };

    static SheetSection body(SimState state, Survivor s)
    {
        var sec = new SheetSection { id = "body", title = "Body" };
        sec.rows.Add(BarRow("Endurance", s.endurance, BodyMod.BODY_MAX, BodyMod.isWinded(s) ? "winded" : "steady", BodyMod.isWinded(s) ? Sev.Warn : Sev.Fine));
        sec.rows.Add(BarRow("Body heat", s.bodyHeat, BodyMod.BODY_MAX,
            s.bodyHeat < BodyMod.FREEZING ? "freezing" : s.bodyHeat < BodyMod.COLD ? "cold" : "warm",
            s.bodyHeat < BodyMod.FREEZING ? Sev.Bad : s.bodyHeat < BodyMod.COLD ? Sev.Warn : Sev.Fine));
        sec.rows.Add(BarRow("Wetness", s.wetness, BodyMod.BODY_MAX,
            s.wetness > BodyMod.DRENCHED ? "drenched" : s.wetness > BodyMod.SOAKED ? "soaked" : s.wetness > 0 ? "damp" : "dry",
            s.wetness > BodyMod.DRENCHED ? Sev.Warn : s.wetness > BodyMod.SOAKED ? Sev.Note : Sev.Fine));
        sec.rows.Add(BarRow("Panic", s.panic, BodyMod.BODY_MAX,
            BodyMod.isPanicking(s) ? "panicking" : s.panic < BodyMod.CALM ? "calm" : "uneasy",
            BodyMod.isPanicking(s) ? Sev.Warn : Sev.Fine));
        return sec;
    }

    static SheetSection needs(SimState state, Survivor s)
    {
        var sec = new SheetSection { id = "needs", title = "Needs" };
        sec.rows.Add(BarRow("Fed", s.satiety, Food.SATIETY_MAX, s.satiety == 0 ? "starving" : Needs.isHungry(s) ? "hungry" : "fed", s.satiety == 0 ? Sev.Critical : Needs.isHungry(s) ? Sev.Warn : Sev.Fine));
        if (state.harsh)
            sec.rows.Add(BarRow("Water", s.hydration, Water.HYDRATION_MAX, s.hydration == 0 ? "dehydrated" : Water.isThirsty(s) ? "thirsty" : "quenched", s.hydration == 0 ? Sev.Critical : Water.isThirsty(s) ? Sev.Warn : Sev.Fine));
        sec.rows.Add(BarRow("Rest", s.rest, Rest.REST_MAX, s.rest < Rest.TIRED ? "exhausted" : Needs.isTired(state, s) ? "tired" : "rested", s.rest < Rest.TIRED ? Sev.Warn : Needs.isTired(state, s) ? Sev.Note : Sev.Fine));
        sec.rows.Add(BarRow("Company", s.social, Wellbeing.NEED_MAX, s.social < Wellbeing.LOW ? "lonely" : s.social > Wellbeing.HIGH ? "good company" : "ordinary", s.social < Wellbeing.LOW ? Sev.Warn : Sev.Fine));
        sec.rows.Add(BarRow("Comfort", s.comfort, Wellbeing.NEED_MAX, s.comfort < Wellbeing.LOW ? "uncomfortable" : s.comfort > Wellbeing.HIGH ? "comfortable" : "ordinary", s.comfort < Wellbeing.LOW ? Sev.Warn : Sev.Fine));
        return sec;
    }

    public static string moodWord(double mood) =>
        mood < Mood.MISERABLE ? "miserable" : mood < Mood.MOOD_BASE ? "low" : mood == Mood.MOOD_BASE ? "steady" : "good";

    static SheetSection moodSection(SimState state, Survivor s)
    {
        var sec = new SheetSection { id = "mood", title = "Mood" };
        double mood = Mood.mood(state, s);
        sec.rows.Add(BarRow("Mood", mood, Mood.MOOD_MAX, moodWord(mood), mood < Mood.MISERABLE ? Sev.Bad : mood < Mood.MOOD_BASE ? Sev.Note : Sev.Fine));
        sec.rows.Add(BarRow("Resolve", s.resolve, Resolve.RESOLVE_MAX, Resolve.isBreaking(s) ? "breaking" : Resolve.isSteeled(s) ? "steeled" : "holding", Resolve.isBreaking(s) ? Sev.Warn : Sev.Fine));
        foreach (var (label, effect) in Mood.moodFactors(state, s).OrderByDescending((f) => Math.Abs(f.Item2)))
            sec.rows.Add(new SheetRow { label = label, text = Signed(effect), severity = effect <= -15 ? Sev.Bad : effect < 0 ? Sev.Note : Sev.Fine });
        return sec;
    }

    static SheetSection skills(Survivor s)
    {
        var sec = new SheetSection { id = "skills", title = "Skills" };
        var types = s.xp.Keys.Where((k) => Skills.skillLevel(s, k) > 0 || s.xp[k] > 0).ToList();
        if (Skills.hasTrait(s, "greenThumb") && !types.Contains("farm")) types.Add("farm");
        foreach (var k in types.OrderByDescending((k) => Skills.skillLevel(s, k)).ThenBy((k) => JobTypes.JOB_TYPES.IndexOf(k) is var i && i >= 0 ? i : 99))
        {
            double level = Skills.skillLevel(s, k);
            double xp = s.xp.ContainsKey(k) ? s.xp[k] : 0;
            double into = xp % Skills.XP_PER_LEVEL;
            bool max = level >= Skills.MAX_LEVEL;
            var text = max ? $"level {level} (the most)" : $"level {level}: {Math.Round(into)} of {Skills.XP_PER_LEVEL} to level {level + 1}";
            if (k == "farm" && Skills.hasTrait(s, "greenThumb")) text += " (green thumb counts as five levels)";
            sec.rows.Add(new SheetRow { label = k, text = text, bar = max ? 1 : Math.Floor(xp / Skills.XP_PER_LEVEL) >= Skills.MAX_LEVEL ? 1 : into / Skills.XP_PER_LEVEL });
        }
        return sec;
    }

    public static readonly Dictionary<string, string> SLOT_NAMES = new() { ["weapon"] = "In hand", ["body"] = "Body", ["feet"] = "Feet", ["tool"] = "Tool" };

    static SheetSection wearing(SimState state, Survivor s)
    {
        var sec = new SheetSection { id = "wearing", title = "Wearing and wielding" };
        double guard = Skills.skillLevel(s, "guard") * Skills.GUARD_DAMAGE_PER_LEVEL + (Skills.hasTrait(s, "hardened") ? Skills.HARDENED_DAMAGE : 0);
        foreach (var slot in Inventory.SLOTS)
        {
            var id = Js.GetProp<string?>(s, slot);
            var item = id != null ? Items.getItem(state, id) : null;
            var row = new SheetRow { label = SLOT_NAMES[slot] };
            if (item == null)
            {
                row.text = slot switch
                {
                    "weapon" => $"bare hands: {Jobs.GUARD_DAMAGE + guard} a blow",
                    "feet" => $"bare feet: a spike trap does {Clothing.TRAP_STEP_DAMAGE}",
                    _ => "nothing",
                };
                                sec.rows.Add(row);
                continue;
            }
            var name = Items.nameOf(item);
            string says;
            if (slot == "weapon")
            {
                var w = Weapons.specOf(item);
                says = w == null ? "a weapon"
                    : w.isFirearm
                        ? $"{w.damage * w.pellets + guard} a shot ({item.wear} of {w.magazine} rounds); guns don't break"
                        : $"{w.damage + guard} a blow" + (w.durability > 0 ? $"; breaks after {Math.Max(0, w.durability - item.wear)} more kills" : "; doesn't break");
            }
            else if (Clothing.isGarment(item.kind))
            {
                var life = Clothing.GARMENT_LIFE[item.kind];
                says = item.kind switch
                {
                    "coat" => "keeps the cold and the rain off",
                    "armour" => $"blows do {Clothing.ARMOUR_DAMAGE_PCT}% damage, and bites infect half as often",
                    _ => "spike traps can't hurt you",
                };
                says += $"; {Pct(Math.Max(0, 1 - item.wear / life))} left";
                if (item.wear * 2 >= life) row.severity = Sev.Warn;
            }
            else says = Skills.TOOL_FOR.ContainsKey(item.kind) ? $"helps with {Skills.TOOL_FOR[item.kind]}" : "";
            row.text = says == "" ? name : $"{name}: {says}";
            sec.rows.Add(row);
        }
        if (s.carrying != null && Items.getItem(state, s.carrying) is { } held) sec.rows.Add(new SheetRow { label = "Carrying", text = Items.nameOf(held) });
        if (s.cart != null) sec.rows.Add(new SheetRow { label = "Cart", text = "pulling one" });
        return sec;
    }
}
