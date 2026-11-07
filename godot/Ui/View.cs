// View state (src/ui/view.ts): everything the UI remembers that is NOT sim state (camera, build
// mode, selection, hover, speed, which panels are open). Kept apart so it never leaks into saves.

using LoonieRoo.Game.Render;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Ui;

public sealed class View
{
    public Cam camera = new(0, 0, 1);
    /// <summary>Build mode (B): the bottom build bar is open.</summary>
    public bool buildOpen;
    public string buildCategory = "structure";
    /// <summary>The armed build tool, or null: clicks select and right-clicks move. Only set while buildOpen.</summary>
    public string? tool;
    public string? selectedId;
    /// <summary>While dragging an area tool: the box being dragged out (tile corners).</summary>
    public (Point from, Point to)? areaDrag;
    /// <summary>A short message for the player, shown until `toastUntil` (ms since start).</summary>
    public string? toast;
    public ulong toastUntil;
    public Point? hover;
    /// <summary>Edge tools (Wall, Door): the tile border nearest the cursor, previewed (grid.ts edges).</summary>
    public (int x, int y, string side)? hoverEdge;
    public int speed = 1;
    public bool paused;
    public bool showDebug;
    public bool showPriorities = true;
    /// <summary>The menu screen over the game, or null when playing (menu.ts): main, new, character, load, howto.</summary>
    public string? menu;
    /// <summary>A game is going on under the menu (so it offers Resume and Save, not Continue).</summary>
    public bool inGame;
    public bool menuCustomOpen;
    public bool showMinimap = true;
    public bool showMap;
    public bool showOverview;
    public bool showKeys;
    /// <summary>Under direct control, the colony's management (G). Off, you see only what your survivor would.</summary>
    public bool colonyMode;
    /// <summary>The survivor list: folded, short, or full.</summary>
    public string roster = "short";

    public void Toast(string text, ulong ms = 6000)
    {
        toast = text;
        toastUntil = Godot.Time.GetTicksMsec() + ms;
    }

    /// <summary>Playing one survivor with the colony's management put away (G brings it back).</summary>
    public bool survivorMode(bool controlling) => controlling && !colonyMode;

    /// <summary>G under direct control: the colony's management out, or put away (closing build and the overview).</summary>
    public void toggleColonyMode()
    {
        colonyMode = !colonyMode;
        if (!colonyMode)
        {
            buildOpen = false;
            tool = null;
            showOverview = false;
        }
    }
}
