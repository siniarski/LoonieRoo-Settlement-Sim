// The character card's plain functions of state: a survivor's conditions, need bars, mood tooltip and activity, all
// read from the sim's character sheet (CharacterSheet.build), so the card and the Tab sheet cannot differ. Panels: Hud.cs.

using System;
using System.Collections.Generic;
using System.Linq;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Ui;

public sealed record Condition(string id, string label, string glyph, string tone, string about);

public sealed record Bar(string label, int pct, string kind, Sev severity);

public static class Card
{
    static readonly Dictionary<string, string> GLYPHS = new()
    {
        ["Infected"] = "☣", ["Bleeding"] = "✚", ["Dying"] = "♥", ["Badly hurt"] = "♥", ["Hurt"] = "♥", ["Starving"] = "◒", ["Hungry"] = "◒",
        ["Dehydrated"] = "◊", ["Thirsty"] = "◊", ["Freezing"] = "❄", ["Cold"] = "❄", ["Sick"] = "☁", ["Panicking"] = "!", ["Winded"] = "~",
        ["Exhausted"] = "z", ["Tired"] = "z", ["Drenched"] = "≈", ["Soaked"] = "≈", ["Breakdown"] = "▼", ["Miserable"] = "▽", ["Breaking"] = "▼",
        ["Wounded arm"] = "✚", ["Wounded leg"] = "✚", ["Irradiated"] = "☢", ["Lonely"] = "…", ["Uncomfortable"] = "□", ["Steeled"] = "▲",
    };

    static string tone(Sev s) => s >= Sev.Bad ? "bad" : s >= Sev.Note ? "mild" : "good";

    /// <summary>Everything notable about a survivor right now, worst first: the character sheet's conditions
    /// (cs/Sim/Survivors/CharacterSheet.cs), which this card and the Tab sheet both show.</summary>
    public static List<Condition> conditionsOf(SimState state, Survivor s) => conditionsOf(CharacterSheet.build(state, s));

    public static List<Condition> conditionsOf(Sheet sheet) => sheet.Conditions.Select((r) =>
    {
        string glyph = GLYPHS.TryGetValue(r.label, out var g) ? g
            : r.label.StartsWith("Grabbed") ? "✋" : r.label.StartsWith("Missing") ? "✚" : r.severity == Sev.Fine ? "▲" : "▽";
        string about = char.ToUpperInvariant(r.text[0]) + r.text[1..] + (r.help != null ? $". Help: {r.help}." : ".");
        return new Condition(r.label, r.label, glyph, tone(r.severity), about);
    }).ToList();

    /// <summary>The body's needs as bars, from the sheet's bar rows (water only in a harsh game, where thirst counts).</summary>
    public static List<Bar> barsOf(SimState state, Survivor s) => barsOf(CharacterSheet.build(state, s));

    public static List<Bar> barsOf(Sheet sheet)
    {
        var bars = new List<Bar>();
        void add(string section, string label, string kind)
        {
            var r = sheet.Section(section).rows.FirstOrDefault((x) => x.label == label);
            if (r?.bar is double b) bars.Add(new Bar(label, (int)Math.Max(0, Math.Min(100, Math.Round(100 * b, MidpointRounding.AwayFromZero))), kind, r.severity));
        }
        add("health", "Health", "health");
        add("needs", "Fed", "food");
        add("needs", "Water", "water");
        add("needs", "Rest", "rest");
        add("body", "Endurance", "endurance");
        add("body", "Body heat", "warmth");
        return bars;
    }

    /// <summary>"Mood 55: ate at a table +10, hurt -10" (a tooltip, and what the debug panel shows).</summary>
    public static string moodTitle(SimState state, Survivor s)
    {
        var factors = Mood.moodFactors(state, s).Select((f) => $"{f.Item1} {(f.Item2 > 0 ? "+" : "")}{f.Item2}").ToList();
        return $"Mood {Mood.mood(state, s)}{(Mood.isMiserable(state, s) ? " (miserable: works slower)" : "")}{(factors.Count > 0 ? $": {string.Join(", ", factors)}" : "")}";
    }

    /// <summary>What they're doing, in a few words (CharacterSheet.activity).</summary>
    public static string activity(SimState state, Survivor s) => CharacterSheet.activity(state, s);
}
