// The main menu (src/ui/menu.ts, howto.ts): what you see on starting, and (Escape, or the Menu
// button) over a paused game.
//   main       Continue (the newest save) or Resume, New game, Load, and in a game: Save, Report a bug
//   new        the presets, a Customise section (the sandbox settings, map size, a seed), Next
//   character  who you are (CharacterMod): a name, a profession, traits for points; Start
//   load       the save slots, the autosave, and a file
//   howto      How to play
//   options    Graphics: view and detail distance, far trees, shadows, character models, resolution, anti-aliasing
//              (Graphics.cs: saved to the player's settings file and applied at once)
// It only calls the actions the game hands it.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Control = Godot.Control;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Ui;

public interface IMenuActions
{
    void Resume();
    void NewGame(double size, Settings settings, double? seed, Character you);
    /// <summary>A debug arena: a small solo world with zombies near you from the start (arena.ts).</summary>
    void Arena(string kind);
    void Load(string slot);
    void ImportFile();
    void Save(string slot);
    void ExportSave();
    void ReportBug();
}

public sealed partial class Menu : Control
{
    static readonly (string heading, string[] lines)[] HOW_TO_PLAY =
    {
        ("What this is", new[]
        {
            "The dead walk. You are one survivor in a dying country: find food and water, stay warm and rested, and keep away from the dead, or fight them when you must.",
            "If you settle, the people you find can make a colony: they take jobs by priority, build, farm, cook and stand guard.",
        }),
        ("Playing one survivor", new[]
        {
            "Select someone and press F to take control (F again lets go). The screen shows only what they would see: G brings out the colony management (build, priorities, overview, neighbours) and puts it away.",
            "WASD walk, Shift run, C sneak. The mouse aims; click to swing or shoot. A swing at a window breaks it (loudly).",
            "E or right-click uses what is in front of you: search a fridge or a wardrobe, eat, drink from a tap, sleep in a bed, cook at an oven while the power lasts, get in a car.",
            "R works the tile in front: repair, barricade a door or board a window with wood, build a wall.",
            "T talks to people nearby. V asks your people to follow you, H to hold where they are, L to loot the room under the mouse.",
            "1-9 use what is in your bag. K shows every key.",
        }),
        ("Running the colony", new[]
        {
            "Left click selects, right click moves. B opens the build bar: walls, doors, furniture, rooms, and areas to scavenge, explore or farm. Shift places instantly where the tool allows.",
            "P shows your people: fold the list away, or open it out to set each job priority (1 first, 4 last, - never). O is the overview of the colony.",
            "Rooms need a purpose and what goes with it (a bedroom needs beds). An incomplete room says what it lacks.",
        }),
        ("Staying alive", new[]
        {
            "Hunger, thirst, rest and warmth all count. Wounds bleed until bandaged; bites can turn.",
            "The taps and the power go off in the first weeks. After that, water is wells, rain collectors, what is left in toilets and baths, or the river (which can make you sick). Fridges go warm and their food rots.",
            "The dead hear noise and see you in the open. Sneak, stay out of their sight, keep doors shut. They batter down what stands between you.",
            "Wolves give a warning bite if you crowd them, and come for you when starving; hurt one and the pack comes. Bears maul anyone close. Rabbits and deer are food.",
            "Neighbouring settlements trade, ask favours, send scavengers and caravans out, and remember how you treated them.",
        }),
        ("The view", new[] { "Space jumps, vaults a broken window or a barricade, or hops a ditch. The world stops while this menu or the map is open; in the colony tab, 1x, 2x and 4x speed. M is the map, N the minimap, Home recentres, the wheel zooms. Esc opens this menu." }),
    };

    readonly View view = null!;
    readonly IMenuActions actions = null!;
    readonly VBoxContainer panel = new();
    string shown = "";
    // The New game screen's choices, kept while you move between screens.
    string preset = "survivor";
    Settings settings = Copy(Difficulty.NORMAL);
    double size = Worldgen.WORLD_SIZE;
    string seed = "";
    readonly Character you = new() { name = "Ash", job = StoryMod.STORY_JOBS.IndexOf("a student"), traits = new() };

    public Menu() { }

    public Menu(View view, IMenuActions actions)
    {
        this.view = view;
        this.actions = actions;
        Theme = Kit.MakeTheme();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = Ctx.Css("rgba(8, 10, 9, 0.72)"), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centre);
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", Kit.Box("rgba(20, 23, 21, 0.97)", "#3f4a42", 8, 18));
        centre.AddChild(box);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(640, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(scroll);
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        panel.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(panel);
    }

    static Settings Copy(Settings s) => new() { start = s.start, zombies = s.zombies, weather = s.weather, progression = s.progression, winter = s.winter, dayScale = s.dayScale, map = s.map };

    void Go(string screen)
    {
        view.menu = screen;
        shown = "";
    }

    public void Refresh() => shown = "";

    public override void _Process(double delta)
    {
        Visible = view.menu != null;
        if (view.menu == null) return;
        var scroll = (ScrollContainer)panel.GetParent();
        // As tall as what it shows, up to the window (then it scrolls).
        scroll.CustomMinimumSize = new Vector2(640, Math.Min(panel.GetCombinedMinimumSize().Y, GetViewportRect().Size.Y - 80));
        string key = $"{view.menu}|{view.inGame}";
        if (key == shown) return;
        shown = key;
        foreach (var c in panel.GetChildren()) c.QueueFree();
        var r = new Rows(panel, 14);
        r.Text("LOONIEROO", Kit.Title).AddThemeFontSizeOverride("font_size", 26);
        r.Text("a settlement in the dead country", Ctx.Css("#8f998f"));
        switch (view.menu)
        {
            case "main": Main(r); break;
            case "new": NewGame(r); break;
            case "character": CharacterScreen(r); break;
            case "howto": HowTo(r); break;
            case "options": OptionsScreen(r); break;
            default: LoadScreen(r); break;
        }
    }

    static Button Primary(Button b)
    {
        b.AddThemeStyleboxOverride("normal", Kit.Box("#3d5a46", "#6c9a78", 4, 6));
        b.AddThemeStyleboxOverride("hover", Kit.Box("#47684f", "#7cab88", 4, 6));
        return b;
    }

    void Main(Rows r)
    {
        var continueFrom = Saves.newestSave();
        if (view.inGame) Primary(r.Button("Resume", actions.Resume));
        else if (continueFrom != null)
        {
            Primary(r.Button("Continue", () => actions.Load(continueFrom)));
            r.Text(Saves.slotLabel(continueFrom) ?? "", Ctx.Css("#8f998f"));
        }
        r.Button("New game", () => Go("new"));
        r.Button("Load", () => Go("load"));
        r.Button("How to play", () => Go("howto"));
        r.Button("Options", () => Go("options"));
        var sound = r.Row();
        sound.Text("Volume", Kit.Dim);
        var slider = sound.Add(new HSlider { MinValue = 0, MaxValue = 100, Step = 5, Value = Audio.Volume.Level * 100, CustomMinimumSize = new Vector2(160, 0), FocusMode = FocusModeEnum.None, Name = "Volume" });
        slider.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        slider.ValueChanged += (v) => Audio.Volume.Set((float)v / 100);
        if (view.inGame)
        {
            var saves = r.Row();
            saves.Text("Save to", Kit.Dim);
            foreach (var slot in Saves.SLOTS) saves.Button(slot, () => { actions.Save(slot); shown = ""; });
            saves.Button("File…", actions.ExportSave, tip: "Writes the save to Documents/LoonieRoo");
            r.Button("Report a bug", actions.ReportBug, tip: "Writes a bug report (the game replayed from where you started) to Documents/LoonieRoo");
        }
    }

    OptionButton Choose<T>(GridContainer grid, string label, IEnumerable<(T value, string text)> options, T value, Action<T> set, string? name = null)
    {
        grid.AddChild(Kit.Label(label, Kit.Dim, 14));
        var select = new OptionButton { FocusMode = FocusModeEnum.None };
        if (name != null) select.Name = name;
        var list = options.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            select.AddItem(list[i].text, i);
            if (EqualityComparer<T>.Default.Equals(list[i].value, value)) select.Selected = i;
        }
        select.ItemSelected += (i) => set(list[(int)i].value);
        grid.AddChild(select);
        return select;
    }

    void NewGame(Rows r)
    {
        r.Text("New game", Kit.Title).AddThemeFontSizeOverride("font_size", 18);
        var presets = r.Add(new GridContainer { Columns = 2 });
        foreach (var (id, p) in Difficulty.PRESETS)
        {
            var parts = p.label.Split(": ", 2);
            var b = Kit.Button($"{parts[0]}\n{(parts.Length > 1 ? parts[1] : "")}", () =>
            {
                preset = id;
                settings = Copy(p.settings);
                shown = "";
            }, active: preset == id, size: 13);
            b.CustomMinimumSize = new Vector2(300, 48);
            b.Alignment = HorizontalAlignment.Left;
            presets.AddChild(b);
        }
        // Customise: every setting, starting from the preset.
        r.Button(view.menuCustomOpen ? "▾ Customise" : "▸ Customise", () => { view.menuCustomOpen = !view.menuCustomOpen; shown = ""; });
        if (view.menuCustomOpen)
        {
            var grid = r.Add(new GridContainer { Columns = 2 });
            grid.AddThemeConstantOverride("h_separation", 12);
            var s = settings;
            Choose(grid, "Start", new[] { ("group", "With a group of three"), ("solo", "Alone (you control them)") }, s.start, (v) => s.start = v);
            Choose(grid, "Zombies", new double[] { 25, 50, 60, 75, 100, 125, 150, 200, 300 }.Select((n) => (n, $"{n}%")), s.zombies, (v) => s.zombies = v);
            Choose(grid, "Weather", Difficulty.WEATHER_LEVELS.Select((w, i) => ((double)i, w)), s.weather, (v) => s.weather = v);
            Choose(grid, "Since the outbreak", Difficulty.PROGRESSION_LEVELS.Select((p, i) => ((double)i, p)), s.progression, (v) => s.progression = v);
            Choose(grid, "Nuclear winter", new[] { (false, "No"), (true, "Yes") }, s.winter, (v) => s.winter = v);
            Choose(grid, "Day length", new[] { (1.0, "Normal"), (2.0, "Twice as long"), (3.0, "Three times as long") }, s.dayScale, (v) => s.dayScale = v);
            Choose(grid, "Map", Difficulty.MAP_TYPES.Select((m) => (m[0], m[1])), s.map, (v) => s.map = v);
            Choose(grid, "Map size", Worldgen.WORLD_SIZES.Select((n) => (n, $"{n} × {n}")), size, (v) => size = v);
            grid.AddChild(Kit.Label("Seed", Kit.Dim, 14));
            var input = new LineEdit { Text = seed, PlaceholderText = "random" };
            input.TextChanged += (t) => seed = t;
            grid.AddChild(input);
        }
        Primary(r.Button("Next: who you are", () => Go("character")));
        var debug = r.Row();
        debug.Text("Debug", Kit.Dim);
        debug.Button("Arena: one zombie", () => actions.Arena("one"));
        debug.Button("Arena: a crowd", () => actions.Arena("crowd"));
        r.Button("Back", () => Go("main"));
    }

    void CharacterScreen(Rows r)
    {
        r.Text("Who you are", Kit.Title).AddThemeFontSizeOverride("font_size", 18);
        var grid = r.Add(new GridContainer { Columns = 2 });
        grid.AddThemeConstantOverride("h_separation", 12);
        Button? start = null;
        grid.AddChild(Kit.Label("Name", Kit.Dim, 14));
        var name = new LineEdit { Text = you.name, MaxLength = 16 };
        name.TextChanged += (t) =>
        {
            you.name = t;
            if (start != null) start.Disabled = CharacterMod.characterProblem(you) != null;
        };
        grid.AddChild(name);
        var jobs = StoryMod.STORY_JOBS.Select((j, i) =>
        {
            var p = CharacterMod.PROFESSIONS[j];
            return ((double)i, $"{char.ToUpperInvariant(j[0])}{j[1..]} ({(p.points > 0 ? "+" : "")}{p.points})");
        });
        Choose(grid, "Before", jobs, you.job, (v) => { you.job = v; shown = ""; });
        var prof = CharacterMod.PROFESSIONS[StoryMod.STORY_JOBS[(int)you.job]];
        var skills = prof.skills.Count > 0 ? string.Join(", ", prof.skills.Select((kv) => $"{kv.Key} {kv.Value}")) : "no skills";
        var kit = prof.kit.Count > 0 ? string.Join(", ", prof.kit) : "nothing";
        grid.AddChild(new Control());
        grid.AddChild(Kit.Label($"{prof.about}: {skills}; brings {kit}", Ctx.Css("#8f998f"), 13, 420));
        double left = CharacterMod.pointsLeft(you);
        r.Text($"Traits: {left} point{(left == 1 ? "" : "s")} left{(left < 0 ? " (too many: drop one, or pick a different past)" : "")}", left < 0 ? Ctx.Css("#ff8a7a") : Ctx.Css("#8f998f"));
        var traits = r.Add(new GridContainer { Columns = 2 });
        foreach (var t in CharacterMod.CHOOSABLE)
        {
            double cost = CharacterMod.TRAIT_COST[t];
            bool on = you.traits.Contains(t);
            bool clash = CharacterMod.OPPOSED.Any((o) => (o[0] == t && you.traits.Contains(o[1])) || (o[1] == t && you.traits.Contains(o[0])));
            var info = Skills.TRAIT_INFO[t];
            var b = Kit.Button($"{info.label} ({(cost > 0 ? "-" : "+")}{Math.Abs(cost)})\n{info.about}", () =>
            {
                if (on) you.traits.Remove(t);
                else you.traits.Add(t);
                shown = "";
            }, disabled: !on && clash, active: on, size: 13);
            b.CustomMinimumSize = new Vector2(300, 44);
            b.Alignment = HorizontalAlignment.Left;
            traits.AddChild(b);
        }
        start = Primary(r.Button("Start", () =>
        {
            double? n = double.TryParse(seed.Trim(), out var v) && v > 0 && Math.Floor(v) == v ? v : null;
            actions.NewGame(size, Copy(settings), n, new Character { name = you.name, job = you.job, traits = new(you.traits) });
        }, disabled: CharacterMod.characterProblem(you) != null));
        r.Button("Back", () => Go("new"));
    }

    /// <summary>The graphics options (Graphics.cs): each choice applies at once and is saved.</summary>
    void OptionsScreen(Rows r)
    {
        r.Text("Options", Kit.Title).AddThemeFontSizeOverride("font_size", 18);
        r.Text("Graphics", Kit.Gold);
        var grid = r.Add(new GridContainer { Columns = 2 });
        grid.AddThemeConstantOverride("h_separation", 12);
        var o = Graphics.Current;
        void Set(Action<Graphics.Options> change) { Graphics.Set(change); shown = ""; }
        Choose(grid, "View distance", Graphics.VIEWS.Select((v) => (v, v == 0 ? "Near only (about 60 m)" : $"{v} m")), Graphics.View, (v) => Set((x) => x.view = v), "View");
        Choose(grid, "Detail distance", Graphics.DETAILS.Select((d) => (d, $"{d * 16 + 8} m{(d == 3 ? " (normal)" : "")}")), Graphics.Detail, (v) => Set((x) => x.detail = v), "Detail");
        Choose(grid, "Far trees", new[] { ("off", "Off"), ("simple", "Simple"), ("detailed", "Detailed") }, o.farTrees, (v) => Set((x) => x.farTrees = v), "FarTrees");
        Choose(grid, "Shadows", new[] { ("off", "Off"), ("low", "Low (24 m)"), ("medium", "Medium (40 m)"), ("high", "High (80 m)") }, o.shadows, (v) => Set((x) => x.shadows = v), "Shadows");
        Choose(grid, "Character models", Graphics.MODELS.Select((n) => (n, $"Nearest {n}")), o.models, (v) => Set((x) => x.models = v), "Models");
        Choose(grid, "3D resolution", Graphics.SCALES.Select((s) => (s, $"{s}%")), o.scale, (v) => Set((x) => x.scale = v), "Scale");
        Choose(grid, "Anti-aliasing", new[] { ("off", "Off"), ("fxaa", "FXAA"), ("msaa", "MSAA 4x") }, o.aa, (v) => Set((x) => x.aa = v), "AA");
        var notes = new List<string>();
        if (Graphics.ViewOverride != null) notes.Add($"--view={Graphics.ViewOverride}");
        if (Graphics.DetailOverride != null) notes.Add($"--detail={Graphics.DetailOverride}");
        if (notes.Count > 0) r.Text($"The command line's {string.Join(" and ", notes)} hold{(notes.Count == 1 ? "s" : "")} for this run; picking another saves yours.", Ctx.Css("#8f998f"), 600);
        r.Text("Further and more detailed costs more each frame; less helps a slow machine. Far trees past 350 m are always simple. Changes apply at once (a new distance fills in over a few seconds).", Ctx.Css("#8f998f"), 600);
        var row = r.Row();
        row.Button("Defaults", () => { Graphics.Reset(); shown = ""; });
        row.Button("Back", () => Go("main"));
    }

    void HowTo(Rows r)
    {
        r.Text("How to play", Kit.Title).AddThemeFontSizeOverride("font_size", 18);
        foreach (var (heading, lines) in HOW_TO_PLAY)
        {
            r.Text(heading, Kit.Gold);
            foreach (var line in lines) r.Text(line, Kit.Text, 600);
        }
        r.Button("Back", () => Go("main"));
    }

    void LoadScreen(Rows r)
    {
        r.Text("Load", Kit.Title).AddThemeFontSizeOverride("font_size", 18);
        foreach (var slot in Saves.SLOTS.Append(Saves.AUTO_SLOT))
        {
            var label = Saves.slotLabel(slot);
            var row = r.Row();
            row.Text(slot == Saves.AUTO_SLOT ? "Autosave" : $"Slot {slot}", Kit.Dim).CustomMinimumSize = new Vector2(90, 0);
            row.Text(label ?? "empty", Ctx.Css("#8f998f")).CustomMinimumSize = new Vector2(400, 0);
            row.Button("Load", () => actions.Load(slot), disabled: label == null);
        }
        r.Button("From a file…", actions.ImportFile);
        r.Button("Back", () => Go("main"));
    }
}
