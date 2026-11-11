// The screen over the game when the one you played has died (state.succession): the world is held and first
// person is off. A plain panel says who died, of what and on which day, then who can carry on (name, a line
// about them, where they are: the keys 1-9 or a click), or, when nobody can, how the run ended and the way back
// to the menu. Reads state; the choice goes through the "succeed" command (SuccessionMod).

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Control = Godot.Control;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;
using Time = LoonieRoo.Sim.Time;

namespace LoonieRoo.Game.Ui;

public sealed partial class DeathScreen : Control
{
    readonly View view = null!;
    readonly Func<Command, bool> command = null!;
    readonly Action toMenu = null!;
    readonly VBoxContainer panel = new();
    string key = "\0";
    SimState? lastState;
    /// <summary>What the keys do, in order: 1-9 are the first nine; the buttons are the same list.</summary>
    readonly List<Action> options = new();

    public DeathScreen() { }

    public DeathScreen(View view, Func<Command, bool> command, Action toMenu)
    {
        this.view = view;
        this.command = command;
        this.toMenu = toMenu;
        Theme = Kit.MakeTheme();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var dim = new ColorRect { Color = Ctx.Css("rgba(6, 7, 7, 0.9)"), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centre);
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", Kit.Box("rgba(20, 23, 21, 0.98)", "#6a4a44", 8, 20));
        centre.AddChild(box);
        panel.CustomMinimumSize = new Vector2(560, 0);
        panel.AddThemeConstantOverride("separation", 8);
        box.AddChild(panel);
    }

    /// <summary>The screen is up: someone you played has died and nothing has been chosen yet (the menu goes over it).</summary>
    public static bool Wanted(SimState state, View view) => state.succession != null && view.menu == null;

    /// <summary>The run's end: nobody is left in the colony, only a fresh start far off.</summary>
    public static bool IsEnd(SimState state) => state.survivors.Count == 0;

    public void Update(SimState state)
    {
        lastState = state;
        bool shown = Wanted(state, view);
        Visible = shown;
        if (!shown) { key = "\0"; return; }
        var s = state.succession!;
        var heirs = SuccessionMod.heirs(state);
        string k = $"{s.name}|{s.cause}|{s.tick}|{string.Join(",", heirs.Select((h) => $"{h.id}:{h.resolve}:{h.pos.x},{h.pos.y}"))}|{state.survivors.Count}";
        if (k == key) return;
        key = k;
        Build(state, s, heirs);
    }

    void Build(SimState state, Succession s, List<Survivor> heirs)
    {
        foreach (var c in panel.GetChildren()) c.QueueFree();
        options.Clear();
        bool end = IsEnd(state);
        int day = Time.dayOf(s.tick);
        Line(end ? "THE END" : "YOU HAVE DIED", Ctx.Css("#d98a7a"), 12);
        Line($"{s.name} died of {s.cause}", Kit.Title, 22);
        Line($"Day {day}, {Time.clockText(s.tick)}, at ({s.x}, {s.y}).", Kit.Dim, 13);
        if (end)
            Line($"{s.name} was the last of them. The run came to {day} day{(day == 1 ? "" : "s")}" + (s.kills > 0 ? $" and {s.kills:0} of the dead put down." : "."), Kit.Text, 14, 540);
        else if (heirs.Count > 0)
        {
            Line($"{state.survivors.Count} still here. Who carries on?", Kit.Text, 14);
            foreach (var h in heirs)
                Option(h.name, $"{About(h)}  ·  resolve {h.resolve:0}  ·  {Where(h, s)}", () => Choose(h.id), $"{h.name} takes over: you play as them from here.");
        }
        else
            Line($"{state.survivors.Count} still here, but nobody has the resolve to step up ({SuccessionMod.STEP_UP}+).", Kit.Text, 14, 540);
        Option(end ? "Begin again, far from here" : "Start anew, far from here", "A new survivor, alone, somewhere else in this world, with a weapon and a bandage.", () => Choose("anew"), "");
        if (state.survivors.Count > 0) Option("Just watch the colony", "Nobody under your control; the colony runs itself.", () => Choose("none"), "");
        var menuRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        menuRow.AddChild(Kit.Button("Back to the menu (Esc)", () => BackToMenu(), tip: end ? "Leave this run: start a new game or load one." : "The game menu; the choice waits for you."));
        panel.AddChild(menuRow);
    }

    /// <summary>One thing to choose: a button for the mouse and a number key, both the same.</summary>
    void Option(string label, string line, Action act, string tip)
    {
        options.Add(act);
        int n = options.Count;
        var b = Kit.Button($"{(n <= 9 ? n.ToString() : " ")}  {label}", act, tip: tip, size: 15);
        b.Alignment = HorizontalAlignment.Left;
        panel.AddChild(b);
        var pad = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        pad.AddThemeConstantOverride("margin_left", 22);
        pad.AddChild(Kit.Label(line, Kit.Dim, 12, 520));
        panel.AddChild(pad);
    }

    void Line(string text, Color colour, int size, int wrap = 0) => panel.AddChild(Kit.Label(text, colour, size, wrap));

    static string About(Survivor h)
    {
        string job = StoryMod.STORY_JOBS.Count > h.story.job && h.story.job >= 0 ? StoryMod.STORY_JOBS[(int)h.story.job] : "someone";
        string town = StoryMod.TOWNS.Count > h.story.town && h.story.town >= 0 ? StoryMod.TOWNS[(int)h.story.town] : "somewhere";
        return $"was {job} in {town}";
    }

    static string Where(Survivor h, Succession s)
    {
        double dx = h.pos.x - s.x, dy = h.pos.y - s.y;
        return $"at ({h.pos.x}, {h.pos.y}), {Math.Sqrt(dx * dx + dy * dy):0} {(Math.Round(Math.Sqrt(dx * dx + dy * dy)) == 1 ? "tile" : "tiles")} from where {s.name} fell";
    }

    void Choose(string choice)
    {
        command(new Command { type = "succeed", choice = choice });
        key = "\0";
    }

    void BackToMenu()
    {
        // The end of a run has nothing to resume: the menu offers a new game or a load.
        if (lastState != null && IsEnd(lastState)) view.inGame = false;
        toMenu();
    }

    /// <summary>Keys: 1-9 the options in the order shown, Enter the first, Escape the menu. The world is held: no other key does anything.</summary>
    public void OnKey(Key k)
    {
        if (!Visible) return;
        if (k == Key.Escape) { BackToMenu(); return; }
        int n = k >= Key.Key1 && k <= Key.Key9 ? (int)(k - Key.Key1) : k >= Key.Kp1 && k <= Key.Kp9 ? (int)(k - Key.Kp1) : k is Key.Enter or Key.KpEnter ? 0 : -1;
        if (n >= 0 && n < options.Count) options[n]();
    }
}
