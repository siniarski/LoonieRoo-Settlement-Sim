// The graphics options (the menu's Options page): how far you see and what's drawn, kept in user://settings.cfg
// (its [graphics] section; Audio.Volume keeps [audio] in the same file). Settings of the player's machine, not
// of the game: never in the sim's state or a save (rules 1, 7).
//
// Applied live: Apply sets the renderer's knobs (FarField.Range and its trees, FirstPerson.Radius and its shadows,
// the character model budgets, the viewport's 3D scale and anti-aliasing), which are read every frame, so a
// change shows without a restart (a new detail distance or tree mode builds in over a few seconds, nearest first).
//
// The command line wins for the run without being saved: --view=M (the view distance), --detail=N (the detail
// radius in chunks). A self-test, a --shot and the benches don't read or write the player's file (Transient): they
// see the defaults and their flags, so the player's choices never change a test or a screenshot.

using System;
using Godot;
using LoonieRoo.Game.World3D;

namespace LoonieRoo.Game.Ui;

public static class Graphics
{
    const string FILE = "user://settings.cfg";
    const string SECTION = "graphics";

    public sealed class Options
    {
        /// <summary>The far field's range (m): 0 is off (the near chunks, about 56 m, and their fog).</summary>
        public int view = 500;
        /// <summary>The near chunks' radius (chunks of 16 tiles round yours): full detail, furniture, roofs, near trees.</summary>
        public int detail = 3;
        /// <summary>The far field's trees: off, simple (crowns everywhere), detailed (the far LOD to 350 m, crowns past it).</summary>
        public string farTrees = "detailed";
        /// <summary>The sun's shadows: off, low, medium, high.</summary>
        public string shadows = "medium";
        /// <summary>People (and animals) drawn as models, nearest first; the rest are simple shapes.</summary>
        public int models = 40;
        /// <summary>The 3D view's resolution (% of the window; the HUD is always sharp).</summary>
        public int scale = 100;
        /// <summary>Anti-aliasing: off, fxaa, msaa (4x).</summary>
        public string aa = "off";

        public Options Copy() => (Options)MemberwiseClone();
    }

    public static readonly int[] VIEWS = { 0, 250, 500, 750, 1000 };
    public static readonly int[] DETAILS = { 2, 3, 4, 5 };
    public static readonly string[] FAR_TREES = { "off", "simple", "detailed" };
    public static readonly string[] SHADOWS = { "off", "low", "medium", "high" };
    public static readonly int[] MODELS = { 10, 20, 40, 60 };
    public static readonly int[] SCALES = { 50, 75, 100 };
    public static readonly string[] AAS = { "off", "fxaa", "msaa" };

    /// <summary>The player's choices (the file's, or the defaults).</summary>
    public static Options Current { get; private set; } = new();
    /// <summary>The command line's, for this run only (cleared when the player picks that option in the menu).</summary>
    public static int? ViewOverride, DetailOverride;
    /// <summary>Neither read nor write the file (the self-test, a --shot).</summary>
    public static bool Transient;

    static Viewport? viewport;

    /// <summary>At start: the file's choices (unless Transient), then Apply.</summary>
    public static void Init(Viewport vp)
    {
        viewport = vp;
        if (!Transient) Load();
        Apply();
    }

    static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(FILE) != Error.Ok) return;
        var d = new Options();
        int I(string key, int def, int[] allowed) { int v = (int)cfg.GetValue(SECTION, key, def); return Array.IndexOf(allowed, v) >= 0 ? v : def; }
        string S(string key, string def, string[] allowed) { string v = (string)cfg.GetValue(SECTION, key, def); return Array.IndexOf(allowed, v) >= 0 ? v : def; }
        Current = new Options
        {
            view = I("view", d.view, VIEWS),
            detail = I("detail", d.detail, DETAILS),
            farTrees = S("far_trees", d.farTrees, FAR_TREES),
            shadows = S("shadows", d.shadows, SHADOWS),
            models = I("models", d.models, MODELS),
            scale = I("scale", d.scale, SCALES),
            aa = S("aa", d.aa, AAS),
        };
    }

    static void Save()
    {
        if (Transient) return;
        var cfg = new ConfigFile();
        cfg.Load(FILE); // keep the rest (the volume)
        var o = Current;
        cfg.SetValue(SECTION, "view", o.view);
        cfg.SetValue(SECTION, "detail", o.detail);
        cfg.SetValue(SECTION, "far_trees", o.farTrees);
        cfg.SetValue(SECTION, "shadows", o.shadows);
        cfg.SetValue(SECTION, "models", o.models);
        cfg.SetValue(SECTION, "scale", o.scale);
        cfg.SetValue(SECTION, "aa", o.aa);
        cfg.Save(FILE);
    }

    /// <summary>The menu's change: applied at once and saved.</summary>
    public static void Set(Action<Options> change)
    {
        var o = Current.Copy();
        change(o);
        // A choice that differs from what's in effect (a flag's, say) takes over from the flag.
        if (o.view != View) ViewOverride = null;
        if (o.detail != Detail) DetailOverride = null;
        Current = o;
        Apply();
        Save();
    }

    /// <summary>Back to the defaults (saved), the command line's too.</summary>
    public static void Reset()
    {
        ViewOverride = DetailOverride = null;
        Current = new Options();
        Apply();
        Save();
    }

    /// <summary>The view distance in effect (the command line's or the player's).</summary>
    public static int View => ViewOverride ?? Current.view;
    public static int Detail => DetailOverride ?? Current.detail;

    /// <summary>Sets the renderer's knobs from the options; they're read every frame.</summary>
    public static void Apply()
    {
        var o = Current;
        FarField.Range = View;
        FarField.DrawTrees = o.farTrees != "off";
        FarField.TreeTier = o.farTrees == "simple" ? 0 : FarField.TREE_TIER;
        FirstPerson.Radius = Math.Clamp(Detail, 1, 8);
        Trees.BenchRadius = FirstPerson.Radius;
        var (shadowM, atlas) = o.shadows switch { "off" => (0f, 2048), "low" => (24f, 2048), "high" => (80f, 8192), _ => (40f, 4096) };
        FirstPerson.ShadowDistance = shadowM;
        RenderingServer.DirectionalShadowAtlasSetSize(atlas, true);
        People.MAX_MODELS = o.models;
        AnimalModels.MAX_MODELS = o.models;
        if (viewport != null)
        {
            viewport.Scaling3DMode = o.scale < 100 ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
            viewport.Scaling3DScale = o.scale / 100f;
            viewport.ScreenSpaceAA = o.aa == "fxaa" ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
            viewport.Msaa3D = o.aa == "msaa" ? Viewport.Msaa.Msaa4X : Viewport.Msaa.Disabled;
        }
    }
}
