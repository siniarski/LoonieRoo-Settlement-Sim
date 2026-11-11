// Ported from src/sim/mapnotes.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// The map (WorldMap.cs): what's on it besides the ground you've seen.
//
//   notes   your own marks: a pin and a few words on a tile (addMapNote, removeMapNote commands),
//           at most MAX_NOTES of NOTE_MAX characters. Saved with the game.
//   towns   each town in the world plan has a name (townNames: derived, never saved). The first
//           names are the towns survivors say they came from (StoryMod.cs TOWNS), so the place in
//           someone's story can turn up on your map.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Mapnotes
{

    public const int MAX_NOTES = 200;

    public const int NOTE_MAX = 40;

    /// <summary>Town names: the story's first, then more.</summary>
    public static readonly List<string> TOWN_NAMES = [.. StoryMod.TOWNS, "Ashby Fold", "Greyhithe", "Stannard", "Wexley", "Calder Bridge", "Oldmoor", "Fenwick", "Larkhill", "Redditch Vale", "Thornbury"];

    public static List<MapNote> notesOf(SimState state)
    {
        return state.mapNotes ?? [];
    }

    /// <summary>Pins a note on a tile. Returns null, or why not.</summary>
    public static string? addMapNote(SimState state, int x, int y, string text)
    {
        var clean = text.replace(new JsRegExp("[\\u0000-\\u001f]", "g"), "").trim().slice(0, NOTE_MAX);
        if ((!JsNumber.isInteger(x) || !JsNumber.isInteger(y)) || !Grids.inBounds(state.grid, x, y))
        {
            return "not on the map";
        }
        if (clean == "")
        {
            return "a note needs some words";
        }
        state.mapNotes ??= [];
        if (state.mapNotes.length >= MAX_NOTES)
        {
            return $"the map is full ({Js.Str(MAX_NOTES)} notes)";
        }
        state.mapNotes.push(new() { id = $"n{Js.Str(state.nextId++)}", x = Js.I(x), y = Js.I(y), text = clean });
        return null;
    }
    public static string? addMapNote(SimState state, double x, double y, string text) => addMapNote(state, Js.I(x), Js.I(y), text);

    public static string? removeMapNote(SimState state, string id)
    {
        var notes = notesOf(state);
        var i = notes.findIndex((n) => n.id == id);
        if (i < 0)
        {
            return "no such note";
        }
        notes.splice(i, 1);
        return null;
    }

    /// <summary>Worldgen's chunk (a literal: CLAUDE.md rule 12).</summary>
    const int CHUNK_TILES = 32;

    /// <summary>Each town's centre and name (generated worlds; none on hand-made maps).</summary>
    public static List<TownName> townNames(SimState state)
    {
        if (!Js.T(state.world))
        {
            return [];
        }
        var regions = Worldplan.regions(state.world.seed, state.world.size, state.settings.map);
        var offset = JsMath.floor(((double)(Hash.hashRoll(state.world.seed, 0x7041)) * (TOWN_NAMES.length)));
        // A world of one region: every town. A bigger one: the towns of the regions with a generated chunk in them (what
        // the world has made so far; from the state, so the same whichever regions the plan's cache holds). Names by
        // each town's index, so a town keeps its name as more regions come in.
        List<Town> towns;
        if (regions.Single) towns = regions.TownsOf(0, 0);
        else
        {
            towns = [];
            int chunks = (int)state.world.chunks, per = WorldRegions.R / CHUNK_TILES;
            for (int ry = 0; ry < regions.side; ry++)
                for (int rx = 0; rx < regions.side; rx++)
                {
                    bool any = false;
                    for (int cy = ry * per; cy < Math.Min(chunks, (ry + 1) * per) && !any; cy++)
                        for (int cx = rx * per; cx < Math.Min(chunks, (rx + 1) * per) && !any; cx++)
                            any = Js.Get(state.world.generated, (double)cy * chunks + cx) == 1;
                    if (any) towns.AddRange(regions.TownsOf(rx, ry));
                }
        }
        return towns.map((t) => new TownName { x = Js.I(t.x), y = Js.I(t.y), name = Js.NN(Js.Get(TOWN_NAMES, (offset + t.index) % TOWN_NAMES.length)) });
    }
}
