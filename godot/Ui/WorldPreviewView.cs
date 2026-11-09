// The picture of a world (WorldPreview) on the New game, Load and loading screens: ask for a seed, size and map (and a
// save's preview, for what's been explored and where you are) with Want, and it fills in as a worker thread makes it
// (the land at once, then towns, then roads and lots). A newer Want stops the old picture between stages.
//
// Its own WorldRegions, not Worldplan's cache (browsing seeds would push the running game's plan out of it): Remember
// hands it to the cache when the world is about to be made or loaded, so the planning done here isn't done again.

using System;
using Godot;
using Control = Godot.Control;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Ui;

public sealed partial class WorldPreviewView : Control
{
    /// <summary>On screen, a side (the picture is at most WorldPreview.PX: small worlds are scaled up, pixel-sharp).</summary>
    public const float SIDE = 256;

    static readonly Color[] COLOURS =
    {
        C(58, 70, 54),    // land
        C(34, 68, 40),    // forest
        C(40, 78, 108),   // water
        C(84, 86, 82),    // town (sited, its streets not yet laid)
        C(150, 146, 132), // road
        C(176, 176, 172), // street
        C(150, 104, 84),  // built
        C(110, 98, 56),   // field
        C(214, 196, 120), // fence
    };
    static Color C(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);
    /// <summary>A kind's colour in the picture (the menu's legend).</summary>
    public static Color ColourOf(byte kind) => COLOURS[Math.Min(kind, COLOURS.Length - 1)];
    /// <summary>How bright ground nobody has seen is in a save's picture (it still shows the lie of the land).</summary>
    const float UNSEEN = 0.35f;

    string key = "";
    int gen;
    WorldRegions? regions;
    SavePreview? save;
    byte[]? seen;
    /// <summary>A stage of a picture, from the worker: the picture, how far it's got, which Want it's for.</summary>
    sealed record Made(byte[] img, string stage, int gen);
    // Written by the worker, taken each frame (swapped, so no stage is lost between the two).
    Made? latest;
    byte[]? shownImg;
    ImageTexture? texture;
    int px;
    double size;

    /// <summary>What the picture has got to: "the land", "towns", "roads and lots (1 of 4)", "done".</summary>
    public string Stage { get; private set; } = "";

    public WorldPreviewView()
    {
        CustomMinimumSize = new Vector2(SIDE, SIDE);
        TextureFilter = TextureFilterEnum.Nearest;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>Shows this world (a save's picture marks what's explored and you); the same as last time does nothing.</summary>
    public void Want(double seed, double size, string map, SavePreview? save = null)
    {
        string k = $"{seed}|{size}|{map}|{(save == null ? "" : string.Join(",", save.explored))}|{save?.you?.x},{save?.you?.y}";
        if (k == key) return;
        key = k;
        int mine = ++gen;
        this.size = size;
        this.save = save;
        px = WorldPreview.sideFor(size);
        seen = save != null && save.px == px ? WorldPreview.exploredOf(save) : null;
        // The same world as before (only the save's marks changed) keeps its regions and what's been planned.
        bool same = regions != null && regions.seed == seed && regions.size == (int)size && regions.map == map;
        if (!same) regions = new WorldRegions(seed, size, map);
        var r = regions!;
        Stage = "the land";
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                WorldPreview.build(r, (img, stage) => { if (mine == gen) latest = new Made(img, stage, mine); }, () => mine != gen);
            }
            catch (Exception err)
            {
                if (mine == gen) latest = new Made(new byte[WorldPreview.sideFor(size) * WorldPreview.sideFor(size)], $"couldn't: {err.Message}", mine);
            }
        });
        // The marks change at once; the land follows from the worker (or, for the same world, is already here).
        if (same && shownImg != null) Paint(shownImg);
    }

    /// <summary>Hands the regions planned here to Worldplan's cache, if they're this world's (before making or loading it).</summary>
    public void Remember(double seed, double size, string map)
    {
        if (regions != null && regions.seed == seed && regions.size == (int)size && regions.map == map) Worldplan.remember(seed, size, map, regions);
    }

    public override void _Process(double delta)
    {
        var l = System.Threading.Interlocked.Exchange(ref latest, null);
        if (l == null || l.gen != gen) return;
        Stage = l.stage;
        Paint(l.img);
    }

    void Paint(byte[] img)
    {
        if (img.Length != px * px) return;
        shownImg = img;
        var image = Image.CreateEmpty(px, px, false, Image.Format.Rgb8);
        for (int j = 0; j < px; j++)
            for (int i = 0; i < px; i++)
            {
                var c = COLOURS[Math.Min(img[j * px + i], COLOURS.Length - 1)];
                if (seen != null && seen[j * px + i] == 0) c = c.Darkened(1 - UNSEEN);
                image.SetPixel(i, j, c);
            }
        if (texture == null || texture.GetWidth() != px) texture = ImageTexture.CreateFromImage(image);
        else texture.Update(image);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var side = new Vector2(SIDE, SIDE);
        DrawRect(new Rect2(Vector2.Zero, side), C(12, 14, 13));
        if (texture != null) DrawTextureRect(texture, new Rect2(Vector2.Zero, side), false);
        DrawRect(new Rect2(Vector2.Zero, side), C(63, 74, 66), false, 1);
        if (size <= 0) return;
        float scale = SIDE / (float)size;
        // The start (every world's middle), and you in a save.
        float mid = (float)Math.Floor(size / 2) * scale;
        DrawArc(new Vector2(mid, mid), 5, 0, Mathf.Tau, 20, C(230, 80, 70), 1.5f);
        if (save?.you is { } you)
        {
            var at = new Vector2((you.x + 0.5f) * scale, (you.y + 0.5f) * scale);
            DrawCircle(at, 4, C(20, 20, 20));
            DrawCircle(at, 3, C(250, 220, 90));
        }
    }
}
