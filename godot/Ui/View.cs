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
    /// <summary>Edge tools (Wall, Door): the tile border nearest the cursor, previewed (Grid.cs edges).</summary>
    public (int x, int y, string side)? hoverEdge;
    public int speed = 1;
    /// <summary>Held still by the game itself: --paused (screenshots, debugging), the self-test. There's no pause key:
    /// the game stops on its own while the menu or the map is up (`halted`).</summary>
    public bool paused;
    /// <summary>No ticks run: paused, or the menu or the map is open.</summary>
    public bool halted => paused || menu != null || showMap;
    public bool showDebug;
    public bool showPriorities = true;
    /// <summary>The menu screen over the game, or null when playing (Menu.cs): main, new, character, load, howto.</summary>
    public string? menu;
    /// <summary>A game is going on under the menu (so it offers Resume and Save, not Continue).</summary>
    public bool inGame;
    public bool menuCustomOpen;
    public bool showMinimap = true;
    public bool showMap;
    public bool showOverview;
    public bool showKeys;
    /// <summary>Under direct control, the Colony tab of the M screen: the colony's management in the 2D top-down view. Off, you're in first person.</summary>
    public bool colonyMode;
    /// <summary>Set by the game each frame: you're seeing through your survivor's eyes (no menu, map or colony tab).</summary>
    public bool firstPerson;
    /// <summary>Tab in first person (or in the Colony tab): the bag, what's beside you, and your card, with the mouse let go.</summary>
    public bool showInventory;

    /// <summary>The inventory can be open: in first person, or in the Colony tab while you play someone (not under the menu or the map).</summary>
    public bool inventoryAllowed(bool controlling) => menu == null && (firstPerson || (controlling && colonyMode && !showMap));
    /// <summary>The container the inventory is open on (one you used), or null: then whatever is beside you.</summary>
    public string? openContainer;
    /// <summary>First person: where you're looking (radians; yaw 0 is east, pi/2 south, as the grid; pitch up is positive).</summary>
    public double yaw, pitch;
    /// <summary>The survivor list: folded, short, or full.</summary>
    public string roster = "short";

    public void Toast(string text, ulong ms = 6000)
    {
        toast = text;
        toastUntil = Godot.Time.GetTicksMsec() + ms;
    }

    /// <summary>Playing one survivor with the colony's management put away (its tab under M brings it out).</summary>
    public bool survivorMode(bool controlling) => controlling && !colonyMode;

    /// <summary>The M screen is up: the map, or (playing someone) the colony tab.</summary>
    public bool mScreen(bool controlling) => showMap || (controlling && colonyMode);

    /// <summary>The M screen's tabs: "map" or "colony".</summary>
    public void openTab(string tab)
    {
        showMap = tab == "map";
        colonyMode = tab == "colony";
        showInventory = false;
    }

    /// <summary>Back to first person: the M screen shut, the colony's management put away (closing build and the overview).</summary>
    public void closeMScreen()
    {
        showMap = false;
        colonyMode = false;
        buildOpen = false;
        tool = null;
        showOverview = false;
    }
}
