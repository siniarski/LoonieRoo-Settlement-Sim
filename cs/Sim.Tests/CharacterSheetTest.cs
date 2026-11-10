// The character sheet (CharacterSheet.cs): a healthy survivor, wounds, missing limbs, conditions worst first, and every
// word on the sheet agreeing with the sim's own constants. Read-only: the sheet changes nothing.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class CharacterSheetTest
{
    static (SimState state, Survivor s) start()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["......", "..s...", "......"] });
        var s = state.survivors[0];
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        return (state, s);
    }

    static List<string> labels(Sheet sheet) => sheet.Conditions.map((r) => r.label);
    static bool has(Sheet sheet, string label) => labels(sheet).Contains(label);
    static SheetRow row(Sheet sheet, string section, string label) => sheet.Section(section).rows.First((r) => r.label == label);

    [Fact(DisplayName = "character sheet > a healthy survivor has no conditions, an intact body and a fine word")]
    public void Healthy()
    {
        var (state, s) = start();
        var sheet = CharacterSheet.build(state, s);
        expect(sheet.healthWord).toBe("fine");
        expect(sheet.healthSeverity).toBe(Sev.Fine);
        expect(sheet.healthBar).toBe(1);
        expect(sheet.Conditions.Count).toBe(0);
        expect(sheet.parts.Count).toBe(6);
        foreach (var p in sheet.parts) expect(p.notes.Count, p.id).toBe(0);
        expect(sheet.sections.map((x) => x.id)).toEqual(Js.Arr(["identity", "health", "conditions", "body", "needs", "mood", "skills", "wearing"]));
        expect(row(sheet, "identity", "Name").text).toBe(s.name);
        expect(row(sheet, "identity", "Days survived").text.StartsWith("0 ")).toBe(true);
        var text = CharacterSheet.text(sheet);
        expect(text.Contains($"HEALTH: fine")).toBe(true);
        expect(text.Contains("CONDITIONS, WORST FIRST\n  (nothing)") || text.Contains("CONDITIONS, WORST FIRST\r\n  (nothing)")).toBe(true);
    }

    [Fact(DisplayName = "character sheet > reading the sheet changes nothing")]
    public void ReadOnly()
    {
        var (state, s) = start();
        s.wound = new Wound { limb = "arm", until = state.tick + Wounds.WOUND_TICKS };
        s.bleeding = true;
        s.bleedUntil = state.tick + 100;
        s.infection = 5;
        s.bitten = "leg";
        var before = StateJson.Write(state);
        var a = CharacterSheet.text(CharacterSheet.build(state, s));
        var b = CharacterSheet.text(CharacterSheet.build(state, s));
        expect(StateJson.Write(state) == before).toBe(true);
        expect(a == b).toBe(true);
    }

    [Fact(DisplayName = "character sheet > a bandaged wound and an infected bite")]
    public void WoundAndInfection()
    {
        var (state, s) = start();
        s.wound = new Wound { limb = "arm", until = state.tick + Wounds.WOUND_TICKS };
        s.bleeding = true;
        s.bleedUntil = state.tick + Wounds.BLEED_TICKS;
        var bleeding = CharacterSheet.build(state, s);
        expect(has(bleeding, "Bleeding")).toBe(true);
        expect(has(bleeding, "Wounded arm")).toBe(true);
        var arm = bleeding.parts.First((p) => p.id.StartsWith("arm") && p.notes.Count > 0);
        expect(arm.notes[0].Contains("bleeding (stops by itself")).toBe(true);
        expect(arm.severity).toBe(Sev.Bad);
        expect(bleeding.parts.Count((p) => p.notes.Count > 0)).toBe(1);

        // A bandage stops it (the sim keeps no more than that): the wound stays, not bleeding.
        Medicine.applyRemedy(s, "bandage");
        var bandaged = CharacterSheet.build(state, s);
        expect(has(bandaged, "Bleeding")).toBe(false);
        expect(has(bandaged, "Wounded arm")).toBe(true);
        expect(bandaged.Part(arm.id).notes[0].Contains("not bleeding (bandaged")).toBe(true);
        expect(bandaged.Part(arm.id).severity).toBe(Sev.Warn);

        // An infected bite on a leg: critical, early enough to amputate, on the body and at the top of the list.
        s.infection = 10;
        s.bitten = "leg";
        var sheet = CharacterSheet.build(state, s);
        expect(sheet.Conditions[0].label).toBe("Infected");
        expect(sheet.Conditions[0].severity).toBe(Sev.Critical);
        expect(sheet.Conditions[0].help.Contains("can still come off")).toBe(true);
        var leg = sheet.parts.First((p) => p.id.StartsWith("leg") && p.notes.Count > 0);
        expect(leg.severity).toBe(Sev.Critical);
        expect(leg.notes[0].Contains("bitten, infected")).toBe(true);
        // Past the amputation window the sheet says it has spread.
        s.infection = Wounds.amputateWindow() + 1;
        expect(CharacterSheet.build(state, s).Conditions[0].help.Contains("spread past amputating")).toBe(true);
    }

    [Fact(DisplayName = "character sheet > a missing arm or leg at each level, with its side")]
    public void MissingLimbs()
    {
        var (state, s) = start();
        var cases = new (string lost, string part, string says)[]
        {
            ("armLeft:wrist", "armLeft", "hand missing at the wrist"), ("armRight:elbow", "armRight", "below the elbow"), ("armLeft:shoulder", "armLeft", "whole arm missing, off at the shoulder"),
            ("legRight:ankle", "legRight", "foot missing at the ankle"), ("legLeft:knee", "legLeft", "below the knee"), ("legRight:hip", "legRight", "whole leg missing, off at the hip"),
        };
        foreach (var (lost, part, says) in cases)
        {
            s.lostLimbs = [lost];
            var sheet = CharacterSheet.build(state, s);
            var p = sheet.Part(part);
            expect(p.missing, lost).toBe(true);
            expect(p.notes[0].Contains(says), $"{lost}: {p.notes[0]}").toBe(true);
            expect(sheet.parts.Count((q) => q.missing), lost).toBe(1);
            expect(has(sheet, lost.StartsWith("arm") ? "Missing an arm" : "Missing a leg"), lost).toBe(true);
        }
        s.lostLimbs = ["armLeft:elbow", "armRight:elbow"];
        expect(has(CharacterSheet.build(state, s), "Missing both arms")).toBe(true);
        // A wound goes on the limb that is left.
        s.lostLimbs = ["armLeft:elbow"];
        s.wound = new Wound { limb = "arm", until = state.tick + 100 };
        var wounded = CharacterSheet.build(state, s);
        expect(wounded.Part("armRight").notes.Count).toBe(1);
        expect(wounded.Part("armLeft").notes[0].Contains("wound")).toBe(false);
    }

    [Fact(DisplayName = "character sheet > starving and freezing at once, worst first")]
    public void Ordering()
    {
        var (state, s) = start();
        s.satiety = 0;
        s.bodyHeat = BodyMod.FREEZING - 1;
        s.endurance = BodyMod.WINDED - 1;
        s.wetness = BodyMod.SOAKED + 1;
        var sheet = CharacterSheet.build(state, s);
        var order = sheet.Conditions.map((r) => (int)r.severity);
        expect(order.SequenceEqual(order.OrderByDescending((x) => x))).toBe(true);
        expect(sheet.Conditions[0].label).toBe("Starving");
        expect(sheet.Conditions[0].severity).toBe(Sev.Critical);
        expect(labels(sheet).IndexOf("Freezing") < labels(sheet).IndexOf("Winded")).toBe(true);
        expect(labels(sheet).IndexOf("Winded") < labels(sheet).IndexOf("Soaked")).toBe(true);
        expect(has(sheet, "Hungry")).toBe(false);
        expect(has(sheet, "Cold")).toBe(false);
        // Critical health with something taking it: dying, first of all.
        s.health = Health.HEALTH_CRITICAL - 1;
        var dying = CharacterSheet.build(state, s);
        expect(dying.healthWord).toBe("dying");
        expect(dying.Conditions[0].label).toBe("Dying");
        // Critical but nothing taking it: badly hurt.
        s.satiety = Food.SATIETY_MAX;
        s.bodyHeat = BodyMod.BODY_MAX;
        expect(CharacterSheet.build(state, s).healthWord).toBe("badly hurt");
    }

    [Fact(DisplayName = "character sheet > every word is the sim's own threshold")]
    public void Thresholds()
    {
        var (state, s) = start();
        string word(double health) { s.health = health; return CharacterSheet.build(state, s).healthWord; }
        expect(word(Health.HEALTH_HURT)).toBe("fine");
        expect(word(Health.HEALTH_HURT - 1)).toBe("hurt");
        expect(word(Health.HEALTH_CRITICAL)).toBe("hurt");
        expect(word(Health.HEALTH_CRITICAL - 1)).toBe("badly hurt");
        s.health = Health.HEALTH_MAX;
        void at(Action set, string label, bool on)
        {
            var (st, sv) = start();
            s = sv; state = st;
            set();
            expect(has(CharacterSheet.build(state, s), label), $"{label} {(on ? "on" : "off")}").toBe(on);
        }
        at(() => s.satiety = Food.HUNGRY, "Hungry", false);
        at(() => s.satiety = Food.HUNGRY - 1, "Hungry", true);
        at(() => s.satiety = 0, "Starving", true);
        at(() => s.satiety = 1, "Starving", false);
        at(() => s.bodyHeat = BodyMod.COLD, "Cold", false);
        at(() => s.bodyHeat = BodyMod.COLD - 1, "Cold", true);
        at(() => s.bodyHeat = BodyMod.FREEZING, "Freezing", false);
        at(() => s.bodyHeat = BodyMod.FREEZING - 1, "Freezing", true);
        at(() => s.wetness = BodyMod.SOAKED, "Soaked", false);
        at(() => s.wetness = BodyMod.SOAKED + 1, "Soaked", true);
        at(() => s.wetness = BodyMod.DRENCHED + 1, "Drenched", true);
        at(() => s.endurance = BodyMod.WINDED, "Winded", false);
        at(() => s.endurance = BodyMod.WINDED - 1, "Winded", true);
        at(() => s.panic = BodyMod.PANICKING, "Panicking", false);
        at(() => s.panic = BodyMod.PANICKING + 1, "Panicking", true);
        at(() => s.rest = Rest.TIRED, "Exhausted", false);
        at(() => s.rest = Rest.TIRED - 1, "Exhausted", true);
        at(() => s.resolve = Resolve.BREAKING, "Breaking", false);
        at(() => s.resolve = Resolve.BREAKING - 1, "Breaking", true);
        at(() => s.sick = Corpses.SICK_TICKS, "Sick", true);
        // Thirst only matters in a harsh game, as in the sim.
        at(() => s.hydration = Water.THIRSTY - 1, "Thirsty", false);
        at(() => { state.harsh = true; s.hydration = Water.THIRSTY - 1; }, "Thirsty", true);
        at(() => { state.harsh = true; s.hydration = 0; }, "Dehydrated", true);
        // The words on the bars use the same constants.
        (state, s) = start();
        s.endurance = BodyMod.WINDED - 1; s.bodyHeat = BodyMod.COLD - 1; s.wetness = BodyMod.SOAKED + 1; s.panic = BodyMod.PANICKING + 1;
        var sheet = CharacterSheet.build(state, s);
        expect(row(sheet, "body", "Endurance").text.EndsWith("winded")).toBe(true);
        expect(row(sheet, "body", "Body heat").text.EndsWith("cold")).toBe(true);
        expect(row(sheet, "body", "Wetness").text.EndsWith("soaked")).toBe(true);
        expect(row(sheet, "body", "Panic").text.EndsWith("panicking")).toBe(true);
        expect(row(sheet, "body", "Endurance").bar).toBe(s.endurance / BodyMod.BODY_MAX);
        // Mood's words follow Mood's constants.
        expect(CharacterSheet.moodWord(Mood.MISERABLE - 1)).toBe("miserable");
        expect(CharacterSheet.moodWord(Mood.MISERABLE)).toBe("low");
        expect(CharacterSheet.moodWord(Mood.MOOD_BASE)).toBe("steady");
        expect(CharacterSheet.moodWord(Mood.MOOD_BASE + 1)).toBe("good");
    }

    [Fact(DisplayName = "character sheet > mood with the thoughts behind it, skills, and what is worn")]
    public void MoodSkillsWearing()
    {
        var (state, s) = start();
        Mood.addThought(state, s, "ateAtTable");
        Mood.addThought(state, s, "sawDeath");
        s.xp["build"] = Skills.XP_PER_LEVEL * 2 + 100;
        s.xp["guard"] = 10;
        var sheet = CharacterSheet.build(state, s);
        var mood = sheet.Section("mood").rows;
        expect(mood[0].label).toBe("Mood");
        expect(mood[0].text.StartsWith($"{Mood.mood(state, s)} / {Mood.MOOD_MAX}")).toBe(true);
        var death = mood.First((r) => r.label == Mood.THOUGHTS["sawDeath"].label);
        expect(death.text).toBe(Mood.THOUGHTS["sawDeath"].effect.ToString());
        expect(death.severity).toBe(Sev.Bad);
        // Strongest first.
        var weights = mood.Skip(2).Select((r) => Math.Abs(double.Parse(r.text))).ToList();
        expect(weights.SequenceEqual(weights.OrderByDescending((w) => w))).toBe(true);

        var skills = sheet.Section("skills").rows;
        expect(skills[0].label).toBe("build");
        expect(skills[0].text).toBe($"level 2: 100 of {Skills.XP_PER_LEVEL} to level 3");
        expect(skills[0].bar).toBe(100.0 / Skills.XP_PER_LEVEL);
        expect(skills[1].label).toBe("guard");

        var coat = Items.spawnItem(state, "coat", s.pos.x, s.pos.y);
        var knife = Items.spawnItem(state, "weapon", s.pos.x, s.pos.y);
        foreach (var it in new[] { coat, knife })
        {
            Commands.applyCommand(state, new Command { type = "take", itemId = it.id });
            Commands.applyCommand(state, new Command { type = "useItem", itemId = it.id });
        }
        coat.wear = Clothing.GARMENT_LIFE["coat"] / 2;
        var wearing = CharacterSheet.build(state, s).Section("wearing").rows;
        expect(wearing.map((r) => r.label)).toEqual(Js.Arr(["In hand", "Body", "Feet", "Tool"]));
        expect(wearing[0].text.Contains($"{Weapons.ARMED_DAMAGE} a blow")).toBe(true);
        expect(wearing[1].text.Contains("50% left")).toBe(true);
        expect(wearing[1].severity).toBe(Sev.Warn);
        expect(wearing[2].text.Contains($"{Clothing.TRAP_STEP_DAMAGE}")).toBe(true);
    }

    [Fact(DisplayName = "character sheet > the text of a healthy and of a badly hurt survivor")]
    public void Text()
    {
        var (state, s) = start();
        s.age = 34;
        var healthy = CharacterSheet.text(CharacterSheet.build(state, s));
        Console.WriteLine(healthy);
        s.health = 180;
        s.satiety = 0;
        s.bodyHeat = 8;
        s.wetness = 70;
        s.panic = 75;
        s.endurance = 12;
        s.infection = 90;
        s.bitten = "arm";
        s.wound = new Wound { limb = "leg", until = state.tick + 500 };
        s.bleeding = true;
        s.bleedUntil = state.tick + 200;
        s.lostLimbs = ["legLeft:knee"];
        s.traits = ["lostLeg"];
        s.xp["scavenge"] = 530;
        var hurt = CharacterSheet.text(CharacterSheet.build(state, s));
        Console.WriteLine(hurt);
        expect(hurt.Contains("HEALTH: dying")).toBe(true);
        expect(hurt.Contains("[critical] Infected")).toBe(true);
    }
}
