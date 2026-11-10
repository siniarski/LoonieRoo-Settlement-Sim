// The self-test's Options page steps (SelfTest.cs runs them from the main menu): the page opens with a click,
// a view distance and a detail distance picked there apply at once (the far field goes, more chunks are built),
// Defaults puts them back, Back returns to the main menu, and none of it touches the player's settings file
// (Graphics.Transient while the self-test runs).

using System.Linq;
using Task = System.Threading.Tasks.Task;
using Godot;
using LoonieRoo.Game.Ui;
using LoonieRoo.Game.World3D;

namespace LoonieRoo.Game;

public partial class Game
{
    async Task OptionsSteps()
    {
        var file = new ConfigFile();
        file.Load("user://settings.cfg");
        var before = file.HasSection("graphics") ? string.Join(",", file.GetSectionKeys("graphics").Select(k => $"{k}={file.GetValue("graphics", k)}")) : "";

        var open = FindButton(menu, "Options");
        if (open != null) await Click(open.GetGlobalRect().GetCenter());
        await Frames(2);
        Check("Options opens the options page", view.menu == "options" && FindNamed<OptionButton>(menu, "View") != null, view.menu ?? "none");

        await Frames(30); // the far field fills in behind the menu
        int regions = firstPerson.Far.Regions, chunks = firstPerson.ChunkCount;
        bool near = await Pick("View", "Near only");
        await Frames(2);
        Check("Picking Near only takes the far field away at once", near && FarField.Range == 0 && firstPerson.Far.Regions == 0 && Graphics.Current.view == 0 && firstPerson.camera.Far < 200,
            $"regions {regions} -> {firstPerson.Far.Regions}, far plane {firstPerson.camera.Far:0} m");
        bool far = await Pick("Detail", "88 m");
        await Frames(40);
        Check("Picking 88 m of detail builds more chunks round you", far && FirstPerson.Radius == 5 && firstPerson.ChunkCount > chunks, $"chunks {chunks} -> {firstPerson.ChunkCount}");

        var defaults = FindButton(menu, "Defaults");
        if (defaults != null) await Click(defaults.GetGlobalRect().GetCenter());
        await Frames(30);
        Check("Defaults puts them back (500 m, 56 m), and the far field returns", Graphics.Current.view == 500 && FarField.Range == 500 && FirstPerson.Radius == 3 && firstPerson.Far.Regions > 0,
            $"view {Graphics.Current.view}, radius {FirstPerson.Radius}, regions {firstPerson.Far.Regions}");

        file = new ConfigFile();
        file.Load("user://settings.cfg");
        var after = file.HasSection("graphics") ? string.Join(",", file.GetSectionKeys("graphics").Select(k => $"{k}={file.GetValue("graphics", k)}")) : "";
        Check("The self-test leaves the player's options file alone", Graphics.Transient && after == before, $"before [{before}], after [{after}]");

        var back = FindButton(menu, "Back");
        if (back != null) await Click(back.GetGlobalRect().GetCenter());
        await Frames(2);
        Check("Back returns to the main menu", view.menu == "main", view.menu ?? "none");
    }

    /// <summary>Picks the item starting with `text` in the options page's choice named `name` (as choosing it in its list does).</summary>
    async System.Threading.Tasks.Task<bool> Pick(string name, string text)
    {
        var choice = FindNamed<OptionButton>(menu, name);
        if (choice == null) return false;
        for (int i = 0; i < choice.ItemCount; i++)
            if (choice.GetItemText(i).StartsWith(text))
            {
                choice.Select(i);
                choice.EmitSignal(OptionButton.SignalName.ItemSelected, (long)i);
                await Frames(1); // the page is drawn again with the new choice
                return true;
            }
        return false;
    }
}
