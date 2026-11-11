// The self-test's inventory steps (SelfTest.cs calls them), on the grid screen: a searched shelf beside you,
// E on it opens the inventory with the shelf beside your bag; shift-click moves a stack across, ctrl-click one;
// a drag from the shelf's grid to a cell of the bag puts it on that cell; R while dragging turns the hammer;
// "Take everything"; drag the coat onto the Body slot and from the slot to a bag cell; drag a tin out onto the
// world; a drag onto a quick slot sets it; F on the cursor's thing moves it across; Esc closes; Tab in the Colony tab opens it there.
// `--invshot=x.png` saves a picture of the open screen with a full bag and the shelf stocked (and x-colony.png,
// the screen in the Colony tab).

using System;
using System.Linq;
using Task = System.Threading.Tasks.Task;
using Godot;
using LoonieRoo.Game.Ui;
using LoonieRoo.Sim;

namespace LoonieRoo.Game;

public partial class Game
{
    async Task InventorySteps()
    {
        bool wasPaused = view.paused;
        view.paused = true;
        inventory.testDevice = TEST_DEVICE;
        var you = ControlMod.controlled(state)!;
        // A searched shelf on a free tile beside you (straight, so you can face it); three tins, a coat and a hammer in your bag.
        var side = new[] { (1, 0), (0, 1), (-1, 0), (0, -1) }.FirstOrDefault((d) =>
        {
            int x = you.pos.x + d.Item1, y = you.pos.y + d.Item2;
            return Items.canHoldItem(state, x, y);
        });
        if (side == default) { Check("A free tile beside you for a shelf", false); view.paused = wasPaused; inventory.testDevice = 0; return; }
        var shelf = FurnitureMod.addFurniture(state, "crate", you.pos.x + side.Item1, you.pos.y + side.Item2);
        shelf.look = "shelf";
        shelf.loot = 0;
        // Whatever you came with goes on the floor first, so the bag starts empty and the cells below are known.
        foreach (var id in you.pack.ToList()) Commands.applyCommand(state, new Command { type = "drop", itemId = id });
        string coat = "", hammer = "";
        foreach (var kind in new[] { "food", "food", "food", "coat", "hammer" })
        {
            var item = Items.spawnItem(state, kind, you.pos.x, you.pos.y);
            Commands.applyCommand(state, new Command { type = "take", itemId = item.id });
            if (kind == "coat") coat = item.id;
            if (kind == "hammer") hammer = item.id;
        }
        int Inside(string kind) => state.items.Count((i) => i.inside == shelf.id && i.kind == kind);
        int Bagged(string kind) => you.pack.Count((id) => Items.getItem(state, id)?.kind == kind);
        string Tin(bool bagged) => (bagged ? you.pack : state.items.Where((i) => i.inside == shelf.id).Select((i) => i.id)).First((id) => Items.getItem(state, id)?.kind == "food");
        Item It(string id) => Items.getItem(state, id)!;

        // E, facing the shelf: the inventory opens on it.
        view.yaw = Math.Atan2(side.Item2, side.Item1);
        view.pitch = -0.4;
        await Frames(2);
        await Tap(Godot.Key.E);
        await Frames(3);
        Check("E on a searched shelf opens the inventory beside it", view.showInventory && view.openContainer == shelf.id && inventory.Visible && Input.MouseMode == Input.MouseModeEnum.Visible, $"open {view.openContainer ?? "none"}");

        // Shift-click a tin in the bag: across to the shelf; ctrl-click another: one across.
        if (inventory.ItemCentre(Tin(true)) is { } tin1) await Click(tin1, shift: true);
        await Frames(2);
        Check("Shift-click moves it across to the shelf", Inside("food") == 1 && Bagged("food") == 2, $"shelf {Inside("food")}, bag {Bagged("food")}");
        if (inventory.ItemCentre(Tin(true)) is { } tin2) await CtrlClick(tin2);
        Check("Ctrl-click moves one across", Inside("food") == 2 && Bagged("food") == 1, $"shelf {Inside("food")}, bag {Bagged("food")}");

        // A drag from the shelf's grid to the bag's bottom-right cell puts the tin on that cell.
        var moving = Tin(false);
        if (inventory.ItemCentre(moving) is { } from && inventory.CellCentre(Inventory.BAG, 7, 5) is { } to) await Drag(from, to);
        Check("A drag from the shelf to a bag cell puts it on that cell", It(moving).carriedBy == you.id && It(moving).gx == 7 && It(moving).gy == 5, $"at {It(moving).gx},{It(moving).gy} inside {It(moving).inside ?? "-"}");

        // R while dragging: the hammer (1x3, held by its middle) turns to lie across, and lands with its middle on the pointer.
        if (inventory.ItemCentre(hammer) is { } h0 && inventory.CellCentre(Inventory.BAG, 4, 5) is { } h1) await Drag(h0, h1, turnHalfway: true);
        Check("R while dragging turns the hammer, and it lands across", It(hammer).turned == true && It(hammer).gx == 3 && It(hammer).gy == 5, $"at {It(hammer).gx},{It(hammer).gy} turned {It(hammer).turned}");

        // A drag onto a quick slot sets it (the hotbar's key 3 then takes the hammer up).
        if (inventory.ItemCentre(hammer) is { } h2) await Drag(h2, inventory.QuickCentre(2));
        Check("A drag onto quick slot 3 puts the hammer there", you.quick != null && you.quick[2] == hammer && you.pack.Contains(hammer), $"slot 3 {(you.quick != null ? you.quick[2] : "none")}");

        var takeAll = FindButton(inventory, "← Take everything");
        if (takeAll != null) await Click(takeAll.GetGlobalRect().GetCenter());
        await Frames(2);
        Check("Take everything empties the shelf", Inside("food") == 0 && Bagged("food") == 3, $"shelf {Inside("food")}, bag {Bagged("food")}");

        // Drag the coat onto the Body slot to wear it, from the slot to a cell of the bag to take it off, and a tin out onto the world.
        if (inventory.ItemCentre(coat) is { } coatAt) await Drag(coatAt, inventory.SlotCentre("body"));
        Check("A drag onto the Body slot wears the coat", you.body == coat, $"body {you.body ?? "none"}");
        if (inventory.CellCentre(Inventory.BAG, 0, 3) is { } bagCell) await Drag(inventory.SlotCentre("body"), bagCell);
        Check("A drag from the Body slot to a bag cell takes it off there", you.body == null && you.pack.Contains(coat) && It(coat).gx == 0 && It(coat).gy == 3, $"body {you.body ?? "none"}, at {It(coat).gx},{It(coat).gy}");
        int onGround = state.items.Count((i) => i.kind == "food" && i.pos != null);
        if (inventory.ItemCentre(Tin(true)) is { } tinOut) await Drag(tinOut, inventory.GroundPoint());
        Check("A drag out onto the world drops it at your feet", Bagged("food") == 2 && state.items.Count((i) => i.kind == "food" && i.pos != null) == onGround + 1, $"bag {Bagged("food")}");
        Check("Things show by name (a coat as a parka, a jacket...)", Items.NAMES["coat"].Any((n) => inventory.ShownNames().Contains(n)), string.Join(",", inventory.ShownNames()));

        // The keyboard: a plain click puts the cursor on the hammer; F moves it across to the shelf.
        if (inventory.ItemCentre(hammer) is { } hammerAt) await Click(hammerAt);
        await Tap(Godot.Key.F);
        await Frames(2);
        Check("F moves the thing under the cursor across", It(hammer).inside == shelf.id, $"cursor {inventory.Cursor}, inside {It(hammer).inside ?? "-"}");

        if (ParseArgs().TryGetValue("invshot", out var shotAt))
        {
            // A full bag (and a bandage in the coat's pockets) beside a stocked shelf. What the picture adds is
            // taken away again after it (below): the steps that follow count on the bag as it was.
            var added = new System.Collections.Generic.List<string>();
            foreach (var kind in new[] { "wood", "gun", "weapon", "bandage", "bandage", "bandage", "book", "medkit", "ammo", "ammo" })
            {
                var taken = Items.spawnItem(state, kind, you.pos.x, you.pos.y);
                added.Add(taken.id);
                Commands.applyCommand(state, new Command { type = "take", itemId = taken.id });
            }
            var bandage = you.pack.First((id) => It(id).kind == "bandage");
            Commands.applyCommand(state, new Command { type = "moveItem", itemId = bandage, containerId = coat, x = 0, y = 0 });
            foreach (var kind in new[] { "food", "food", "boots", "cloth", "cloth", "scrap", "fuel", "book", "axe", "medicine" })
            {
                var item = Items.spawnItem(state, kind, shelf.x, shelf.y);
                item.pos = null;
                item.inside = shelf.id;
                added.Add(item.id);
            }
            await Frames(3);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            WriteShot(ProjectSettings.GlobalizePath(shotAt));
            // And holding the shelf's hatchet over the bag (x-drag.png): the ghost says whether it fits.
            var axe = state.items.First((i) => i.inside == shelf.id && i.kind == "axe").id;
            if (inventory.ItemCentre(axe) is { } a0 && inventory.CellCentre(Inventory.BAG, 4, 4) is { } a1)
            {
                Mouse(a0, MouseButton.Left, true);
                await Frames(1);
                for (int i = 1; i <= 6; i++)
                {
                    var p = a0.Lerp(a1, i / 6f);
                    Input.ParseInputEvent(new InputEventMouseMotion { Device = TEST_DEVICE, Position = p, GlobalPosition = p, Relative = (a1 - a0) / 6f, ButtonMask = MouseButtonMask.Left });
                    await Frames(1);
                }
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                WriteShot(ProjectSettings.GlobalizePath(shotAt.EndsWith(".png") ? shotAt[..^4] + "-drag.png" : shotAt + "-drag.png"));
                await Tap(Godot.Key.Escape); // lets go of it: nothing moves
                Mouse(a1, MouseButton.Left, false);
                await Frames(2);
                Check("Esc while dragging lets go, and nothing moves", state.items.First((i) => i.id == axe).inside == shelf.id);
            }
            foreach (var id in added)
            {
                var item = state.items.FirstOrDefault((i) => i.id == id);
                if (item == null) continue;
                Inventory.unpack(you, id);
                Items.removeItem(state, item);
            }
            await Frames(2);
        }

        await Tap(Godot.Key.Escape);
        await Frames(1);
        Check("Esc closes the inventory", !view.showInventory && !inventory.Visible && view.openContainer == null && view.menu == null);

        // The Colony tab: Tab opens your bag over the map, and a drag onto the world drops there too; Esc shuts it.
        view.openTab("colony");
        await Frames(2);
        await Tap(Godot.Key.Tab);
        await Frames(3);
        bool colonyOpen = view.showInventory && inventory.Visible && view.colonyMode && !view.firstPerson;
        if (ParseArgs().TryGetValue("invshot", out var colonyShot))
        {
            var at = colonyShot.EndsWith(".png") ? colonyShot[..^4] + "-colony.png" : colonyShot + "-colony.png";
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            WriteShot(ProjectSettings.GlobalizePath(at));
        }
        int tinsBefore = Bagged("food");
        if (inventory.ItemCentre(Tin(true)) is { } tin3) await Drag(tin3, inventory.GroundPoint());
        Check("Tab in the Colony tab opens your bag, and a drag out drops", colonyOpen && Bagged("food") == tinsBefore - 1, $"open {colonyOpen}, bag {Bagged("food")}");
        await Tap(Godot.Key.Escape);
        await Frames(1);
        bool shut = !view.showInventory && !inventory.Visible && view.colonyMode;
        view.closeMScreen();
        await Frames(2);
        Check("Esc there shuts the bag and stays in the Colony tab", shut && view.firstPerson);
        inventory.testDevice = 0;
        view.paused = wasPaused;
    }

    /// <summary>A real mouse drag, pressed at `from`, moved in steps, released at `to` (R tapped halfway if `turnHalfway`).</summary>
    async Task Drag(Vector2 from, Vector2 to, bool turnHalfway = false)
    {
        Mouse(from, MouseButton.Left, true);
        await Frames(1);
        for (int i = 1; i <= 8; i++)
        {
            var p = from.Lerp(to, i / 8f);
            Input.ParseInputEvent(new InputEventMouseMotion { Device = TEST_DEVICE, Position = p, GlobalPosition = p, Relative = (to - from) / 8f, ButtonMask = MouseButtonMask.Left });
            await Frames(1);
            if (turnHalfway && i == 4) await Tap(Godot.Key.R);
        }
        Mouse(to, MouseButton.Left, false);
        await Frames(3);
    }

    /// <summary>A click with ctrl held.</summary>
    async Task CtrlClick(Vector2 at)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Device = TEST_DEVICE, Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { Device = TEST_DEVICE, Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = true, CtrlPressed = true, ButtonMask = MouseButtonMask.Left });
        await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { Device = TEST_DEVICE, Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = false, CtrlPressed = true });
        await Frames(2);
    }
}
