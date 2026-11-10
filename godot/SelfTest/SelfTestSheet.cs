// The self-test's character sheet step (SelfTest.cs calls it): Tab opens the inventory with the sheet in its left
// column, naming you, saying "fine" and leaving the bag clear; hurting you in the sim (bitten, bleeding, starving,
// freezing, a leg gone, health low) changes the sheet to "dying" with its conditions; putting that back puts it back.
// `--sheetshot=prefix` (with --selftest) also saves pictures: prefix-healthy.png and prefix-hurt.png (the left
// column) and prefix-healthy-screen.png and prefix-hurt-screen.png (the whole screen, half size).

using System;
using System.Linq;
using Task = System.Threading.Tasks.Task;
using Godot;
using LoonieRoo.Game.Ui;
using LoonieRoo.Sim;

namespace LoonieRoo.Game;

public partial class Game
{
    async Task SheetSteps()
    {
        bool wasPaused = view.paused;
        view.paused = true;
        var you = ControlMod.controlled(state)!;
        await Tap(Godot.Key.Tab);
        await Frames(4);
        var panel = inventory.GetChildren().OfType<CharacterSheetPanel>().FirstOrDefault();
        Check("Tab shows the character sheet", view.showInventory && panel is { Visible: true, Current: not null }, panel == null ? "no sheet" : $"{panel.Current?.name}");
        if (panel?.Current == null) { await Tap(Godot.Key.Tab); view.paused = wasPaused; return; }
        Check("The sheet is you, and fine", panel.Current.name == you.name && panel.Current.healthWord == CharacterSheet.build(state, you).healthWord, $"{panel.Current.name}: {panel.Current.healthWord}");
        var bag = FindLabel(inventory, "Bag ");
        var rect = panel.GetGlobalRect();
        Check("The sheet fits the window and leaves the bag clear", rect.Position.X >= 0 && rect.End.X <= GetViewport().GetVisibleRect().Size.X && (bag == null || rect.End.X <= bag.GetGlobalRect().Position.X),
            $"sheet x {rect.Position.X:0}..{rect.End.X:0}, bag label x {bag?.GetGlobalRect().Position.X:0}");
        await SheetShot("healthy");

        // Hurt, in the sim (put back below): the sheet follows.
        var (health, wound, bleeding, bleedUntil, infection, bitten, satiety, heat, lost, traits) =
            (you.health, you.wound, you.bleeding, you.bleedUntil, you.infection, you.bitten, you.satiety, you.bodyHeat, you.lostLimbs, you.traits);
        you.health = 160;
        you.wound = new Wound { limb = "leg", until = state.tick + Wounds.WOUND_TICKS };
        you.bleeding = true;
        you.bleedUntil = state.tick + Wounds.BLEED_TICKS;
        you.infection = 400;
        you.bitten = "arm";
        you.satiety = 0;
        you.bodyHeat = BodyMod.FREEZING - 2;
        you.lostLimbs = ["legLeft:knee"];
        you.traits = [.. traits, "lostLeg"];
        for (int i = 0; i < 90 && panel.Current.healthWord != "dying"; i++) await Frames(1);
        Check("Hurt, the sheet says dying and lists what is wrong, worst first", panel.Current.healthWord == "dying" && panel.Current.Conditions.Count >= 5 && panel.Current.Conditions[0].label == "Infected",
            $"{panel.Current.healthWord}: {string.Join(", ", panel.Current.Conditions.Select((c) => c.label))}");
        Check("The conditions are on screen", FindLabel(panel, "Infected") != null && FindLabel(panel, "Starving") != null && FindLabel(panel, "Bleeding") != null);
        await SheetShot("hurt");

        (you.health, you.wound, you.bleeding, you.bleedUntil, you.infection, you.bitten, you.satiety, you.bodyHeat, you.lostLimbs, you.traits) =
            (health, wound, bleeding, bleedUntil, infection, bitten, satiety, heat, lost, traits);
        for (int i = 0; i < 90 && panel.Current.Conditions.Count > 0; i++) await Frames(1);
        Check("Put right again, the sheet is back to what it was", panel.Current.healthWord == CharacterSheet.build(state, you).healthWord && FindLabel(panel, "Infected") == null, panel.Current.healthWord);
        await Tap(Godot.Key.Tab);
        Check("Tab closes the sheet with the inventory", !view.showInventory && !panel.IsVisibleInTree());
        view.paused = wasPaused;
    }

    /// <summary>`--sheetshot=prefix`: the sheet's column and the whole screen, in prefix-name.png.</summary>
    async Task SheetShot(string name)
    {
        if (!ParseArgs().TryGetValue("sheetshot", out var prefix) || prefix == "") return;
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var column = image.GetRegion(new Rect2I(0, 0, Math.Min(image.GetWidth(), CharacterSheetPanel.WIDTH + 24), image.GetHeight()));
        column.SavePng($"{prefix}-{name}.png");
        image.Resize(image.GetWidth() / 2, image.GetHeight() / 2, Image.Interpolation.Lanczos);
        image.SavePng($"{prefix}-{name}-screen.png");
        GD.Print($"shot: {prefix}-{name}.png, {prefix}-{name}-screen.png");
    }
}
