// The character sheet on the Tab screen: a column on the left with who you are and what state you are in. The body
// outline's parts are tinted by their state, the conditions stand beside it (worst first), and under them the body,
// needs, mood and its thoughts, what you wear, skills and your story. Everything it says is the sim's own
// (cs/Sim/Survivors/CharacterSheet.cs builds it from the state with the sim's thresholds); this file only draws it.
//
// Reads state only, changes nothing. InventoryScreen creates it and calls Update while the screen is open, with the
// survivor you control (it follows you if that changes). The content is rebuilt when the sheet's text changes, at
// most five times a second, keeping the scroll.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Control = Godot.Control;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Ui;

/// <summary>The colours of the sheet: one per severity (the same in the outline, the words and the bars).</summary>
public static class SheetColours
{
    public static Color Of(Sev s) => s switch
    {
        Sev.Critical => Ctx.Css("#e8483c"),
        Sev.Bad => Ctx.Css("#f08040"),
        Sev.Warn => Ctx.Css("#ffb347"),
        Sev.Note => Ctx.Css("#c8c8a0"),
        _ => Ctx.Css("#6c9a78"),
    };

    /// <summary>The text colour for a severity: plain text when fine.</summary>
    public static Color Text(Sev s) => s == Sev.Fine ? Kit.Text : Of(s);
}

public sealed partial class CharacterSheetPanel : PanelContainer
{
    public const int WIDTH = 320;
    const int FONT = 12, SMALL = 11, INNER = WIDTH - 34;
    const ulong REFRESH_MS = 200;

    readonly ScrollContainer scroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass };
    readonly VBoxContainer content = new() { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(INNER, 0) };
    string key = "";
    ulong lastBuild, lastSeenFrame;

    /// <summary>What is shown (for the self-test and the text trace); null before the first Update.</summary>
    public Sheet? Current { get; private set; }
    public int Builds { get; private set; }

    public CharacterSheetPanel()
    {
        Name = "CharacterSheet";
        AnchorLeft = AnchorRight = 0;
        AnchorTop = 0;
        AnchorBottom = 1;
        OffsetLeft = 8;
        OffsetRight = 8 + WIDTH;
        OffsetTop = 8;
        OffsetBottom = -8;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", Kit.Box("#141715", "#c9b88a", 4, 6));
        AddChild(scroll);
        scroll.AddChild(content);
    }

    /// <summary>Called each frame while the screen is open, with the survivor you control.</summary>
    public void Update(SimState state, Survivor s)
    {
        ulong frame = (ulong)Engine.GetFramesDrawn(), now = Godot.Time.GetTicksMsec();
        bool justOpened = frame > lastSeenFrame + 1;
        lastSeenFrame = frame;
        if (!justOpened && Current != null && Current.name == s.name && now - lastBuild < REFRESH_MS) return;
        lastBuild = now;
        var sheet = CharacterSheet.build(state, s);
        var newKey = $"{s.id}|{CharacterSheet.text(sheet)}";
        if (newKey == key) return;
        bool sameSurvivor = key.StartsWith($"{s.id}|");
        key = newKey;
        Current = sheet;
        Builds++;
        int keep = sameSurvivor ? scroll.ScrollVertical : 0;
        foreach (var c in content.GetChildren()) { content.RemoveChild(c); c.QueueFree(); }
        Build(sheet);
        scroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, keep);
    }

    // --- the content ---------------------------------------------------------------------------------

    void Build(Sheet sheet)
    {
        var identity = sheet.Section("identity").rows;
        content.AddChild(Kit.Label(sheet.name, Kit.Title, 15));
        var who = identity.Where((r) => r.label is "Age" or "Before" or "Days survived").Select((r) => r.label switch
        {
            "Days survived" => DaysSurvived(r.text),
            _ => r.text,
        }).ToList();
        if (who.Count > 0) content.AddChild(Kit.Label(string.Join(" · ", who), Kit.Dim, SMALL, INNER));

        var health = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        health.AddThemeConstantOverride("separation", 8);
        health.AddChild(Kit.Label("Health", Kit.Dim, FONT));
        health.AddChild(new BarFill(sheet.healthBar, SheetColours.Of(sheet.healthSeverity)) { CustomMinimumSize = new Vector2(120, 8), SizeFlagsVertical = SizeFlags.ShrinkCenter });
        health.AddChild(Kit.Label(sheet.healthWord, SheetColours.Text(sheet.healthSeverity), 14));
        content.AddChild(health);

        // The body outline beside the conditions.
        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", 8);
        top.AddChild(new BodyOutline(sheet));
        var conditions = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(INNER - 108, 0) };
        conditions.AddThemeConstantOverride("separation", 4);
        if (sheet.Conditions.Count == 0) conditions.AddChild(Kit.Label("Nothing wrong right now.", Kit.Dim, FONT, INNER - 108));
        foreach (var c in sheet.Conditions) conditions.AddChild(Condition(c, INNER - 108));
        top.AddChild(conditions);
        content.AddChild(top);

        // The parts with something to say (the outline shows where).
        var hurtParts = sheet.parts.Where((p) => p.notes.Count > 0).ToList();
        if (hurtParts.Count > 0)
        {
            Header("The body");
            foreach (var p in hurtParts) Wrapped(p.name, string.Join("; ", p.notes), p.severity, 74);
        }

        Header("Body");
        foreach (var r in sheet.Section("body").rows) BarLine(r);
        Header("Needs");
        foreach (var r in sheet.Section("needs").rows) BarLine(r);

        Header("Mood");
        var mood = sheet.Section("mood").rows;
        foreach (var r in mood.Take(2)) BarLine(r);
        foreach (var r in mood.Skip(2))
        {
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddChild(Kit.Label(r.text, SheetColours.Text(r.severity), FONT).WithWidth(34));
            row.AddChild(Kit.Label(r.label, Kit.Dim, FONT, INNER - 40));
            content.AddChild(row);
        }

        Header("Wearing and wielding");
        foreach (var r in sheet.Section("wearing").rows) Wrapped(r.label, r.text, r.severity, 56);

        var skills = sheet.Section("skills").rows;
        Header("Skills");
        if (skills.Count == 0) content.AddChild(Kit.Label("Nothing practised yet.", Kit.Dim, FONT));
        foreach (var r in skills)
        {
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, TooltipText = r.text };
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(Kit.Label(r.label, Kit.Dim, FONT).WithWidth(74));
            row.AddChild(new BarFill(r.bar ?? 0, Ctx.Css("#c8c8a0")) { CustomMinimumSize = new Vector2(100, 5), SizeFlagsVertical = SizeFlags.ShrinkCenter });
            row.AddChild(Kit.Label(r.text.Split(':')[0], Kit.Text, FONT));
            content.AddChild(row);
        }

        Header("Who you are");
        foreach (var r in identity.Where((r) => r.label is "Traits" or "Kills" or "Proud of" or "Regrets")) Wrapped(r.label, r.text, r.severity, 74);
    }

    /// <summary>"0 (day 1 of the world)" as "0 days survived".</summary>
    static string DaysSurvived(string text)
    {
        var n = text.Split(' ')[0];
        return n == "1" ? "1 day survived" : $"{n} days survived";
    }

    void Header(string text)
    {
        content.AddChild(new Control { CustomMinimumSize = new Vector2(0, 3), MouseFilter = MouseFilterEnum.Ignore });
        content.AddChild(Kit.Label(text.ToUpperInvariant(), Kit.Title, SMALL));
        content.AddChild(new HSeparator { MouseFilter = MouseFilterEnum.Ignore });
    }

    /// <summary>A condition: its name in its colour, what it does, and what would help.</summary>
    static Control Condition(SheetRow c, int width)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 1);
        box.AddChild(Kit.Label(c.label, SheetColours.Of(c.severity), 13));
        if (c.text != "") box.AddChild(Kit.Label(c.text, Kit.Dim, SMALL, width));
        if (c.help != null) box.AddChild(Kit.Label($"→ {c.help}", Ctx.Css("#8fb8a0"), SMALL, width));
        return box;
    }

    /// <summary>A label and wrapped text beside it.</summary>
    void Wrapped(string label, string text, Sev sev, int labelWidth)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(Kit.Label(label, sev == Sev.Fine ? Kit.Dim : SheetColours.Of(sev), FONT).WithWidth(labelWidth));
        row.AddChild(Kit.Label(text, SheetColours.Text(sev) == Kit.Text ? Kit.Text : SheetColours.Text(sev), SMALL, INNER - labelWidth - 6));
        content.AddChild(row);
    }

    /// <summary>A bar row: the label, a bar tinted by severity, and the word.</summary>
    void BarLine(SheetRow r)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, TooltipText = r.text };
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(Kit.Label(r.label, Kit.Dim, FONT).WithWidth(74));
        row.AddChild(new BarFill(r.bar ?? 0, SheetColours.Of(r.severity)) { CustomMinimumSize = new Vector2(100, 5), SizeFlagsVertical = SizeFlags.ShrinkCenter });
        var word = r.text.Contains(": ") ? r.text[(r.text.LastIndexOf(": ", StringComparison.Ordinal) + 2)..] : r.text;
        row.AddChild(Kit.Label(word, SheetColours.Text(r.severity), FONT));
        content.AddChild(row);
    }
}

static class LabelWidth
{
    /// <summary>A label held to a minimum width (so rows line up).</summary>
    public static Label WithWidth(this Label l, float w) { l.CustomMinimumSize = new Vector2(w, 0); return l; }
}

/// <summary>A front view of the body, drawn in code: each part tinted by its state, a missing limb as a ghost with its stump.</summary>
public sealed partial class BodyOutline : Control
{
    static readonly Vector2 SIZE = new(100, 196);
    readonly Sheet sheet;

    public BodyOutline(Sheet sheet)
    {
        this.sheet = sheet;
        CustomMinimumSize = SIZE;
        MouseFilter = MouseFilterEnum.Stop;
        var lines = sheet.parts.Where((p) => p.notes.Count > 0).Select((p) => $"{p.name}: {string.Join("; ", p.notes)}").ToList();
        TooltipText = lines.Count == 0 ? "Nothing wrong with the body." : string.Join("\n", lines);
    }

    Color Tint(string id)
    {
        var p = sheet.Part(id);
        if (p.severity == Sev.Fine) return Ctx.Css("#4d6e57");
        return SheetColours.Of(p.severity);
    }

    static void Limb(CanvasItem g, Vector2 a, Vector2 b, float width, Color c)
    {
        g.DrawLine(a, b, c, width, true);
        g.DrawCircle(a, width / 2, c);
        g.DrawCircle(b, width / 2, c);
    }

    /// <summary>The fraction of a limb that is left for each level it can be gone at.</summary>
    static float Left(string? level) => level switch { "wrist" or "ankle" => 0.85f, "elbow" or "knee" => 0.5f, _ => 0f };

    /// <summary>One limb: its two segments (upper from the joint, lower beyond), whole, or cut short with a faint ghost of the rest.</summary>
    void DrawLimb(string id, Vector2 root, Vector2 mid, Vector2 end, float width)
    {
        var p = sheet.Part(id);
        var colour = Tint(id);
        if (!p.missing)
        {
            Limb(this, root, mid, width, colour);
            Limb(this, mid, end, width - 1, colour);
            return;
        }
        var ghost = new Color(colour, 0.3f);
        Limb(this, root, mid, width, ghost);
        Limb(this, mid, end, width - 1, ghost);
        float keep = Left(p.level);
        // Where it stops: along root-mid-end, as far as what is left of the whole.
        float whole = root.DistanceTo(mid) + mid.DistanceTo(end), reach = keep * whole;
        if (reach <= 0) return;
        float upper = root.DistanceTo(mid);
        if (reach <= upper) Limb(this, root, root.Lerp(mid, reach / upper), width, colour);
        else { Limb(this, root, mid, width, colour); Limb(this, mid, mid.Lerp(end, (reach - upper) / mid.DistanceTo(end)), width - 1, colour); }
    }

    public override void _Draw()
    {
        var o = new Vector2(Math.Max(0, (Size.X - SIZE.X) / 2), 0);
        Vector2 P(float x, float y) => o + new Vector2(x, y);
        var dim = Ctx.Css("#9aa39b");
        // Legs and arms behind the torso, then the torso and the head.
        DrawLimb("legRight", P(43, 104), P(41, 148), P(41, 186), 14);
        DrawLimb("legLeft", P(57, 104), P(59, 148), P(59, 186), 14);
        DrawLimb("armRight", P(30, 46), P(16, 84), P(12, 120), 11);
        DrawLimb("armLeft", P(70, 46), P(84, 84), P(88, 120), 11);
        DrawColoredPolygon(new[] { P(32, 38), P(68, 38), P(65, 108), P(35, 108) }, Tint("torso"));
        DrawCircle(P(50, 20), 14, Tint("head"));
        // Which side is which: you, seen from the front.
        var font = Kit.Font;
        DrawString(font, P(1, 60), "R", HorizontalAlignment.Left, -1, 10, dim);
        DrawString(font, P(90, 60), "L", HorizontalAlignment.Left, -1, 10, dim);
    }
}
