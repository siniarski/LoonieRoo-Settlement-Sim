// The self-test's inventory steps (SelfTest.cs calls them): a searched shelf beside you, E on it
// opens the inventory with the shelf beside your bag; a click moves one, a shift-click the stack, a
// drag one back; "Take everything"; wear and take off a coat; Esc closes. `--invshot=x.png` also
// saves a picture of the open screen.

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
        var you = ControlMod.controlled(state)!;
        // A searched shelf on a free tile beside you (straight, so you can face it), and three tins in your bag.
        var side = new[] { (1, 0), (0, 1), (-1, 0), (0, -1) }.FirstOrDefault((d) =>
        {
            int x = you.pos.x + d.Item1, y = you.pos.y + d.Item2;
            return Items.canHoldItem(state, x, y);
        });
        if (side == default) { Check("A free tile beside you for a shelf", false); view.paused = wasPaused; return; }
        var shelf = FurnitureMod.addFurniture(state, "crate", you.pos.x + side.Item1, you.pos.y + side.Item2);
        shelf.look = "shelf";
        shelf.loot = 0;
        string coat = "";
        foreach (var kind in new[] { "food", "food", "food", "coat" })
        {
            var item = Items.spawnItem(state, kind, you.pos.x, you.pos.y);
            Commands.applyCommand(state, new Command { type = "take", itemId = item.id });
            if (kind == "coat") coat = item.id;
        }
        int Inside(string kind) => state.items.Count((i) => i.inside == shelf.id && i.kind == kind);
        int Bagged(string kind) => you.pack.Count((id) => Items.getItem(state, id)?.kind == kind);

        // E, facing the shelf: the inventory opens on it.
        view.yaw = Math.Atan2(side.Item2, side.Item1);
        view.pitch = -0.4;
        await Frames(2);
        await Tap(Godot.Key.E);
        await Frames(3);
        Check("E on a searched shelf opens the inventory beside it", view.showInventory && view.openContainer == shelf.id && inventory.Visible && Input.MouseMode == Input.MouseModeEnum.Visible, $"open {view.openContainer ?? "none"}");

        // A click on the tins in the bag: one goes in; a shift-click: the rest.
        if (inventory.RowCentre("bag", "food") is { } tins) await Click(tins);
        await Frames(2);
        Check("A click stashes one", Inside("food") == 1 && Bagged("food") == 2, $"shelf {Inside("food")}, bag {Bagged("food")}");
        if (inventory.RowCentre("bag", "food") is { } rest) await Click(rest, shift: true);
        await Frames(2);
        Check("A shift-click stashes the stack", Inside("food") == 3 && Bagged("food") == 0, $"shelf {Inside("food")}, bag {Bagged("food")}");

        if (ParseArgs().TryGetValue("invshot", out var shotAt))
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(shotAt);
            GD.Print($"shot: {shotAt}");
        }

        // A drag from the shelf to the bag brings one back.
        if (inventory.RowCentre("box", "food") is { } from)
        {
            var to = inventory.PanelCentre("bag");
            Mouse(from, MouseButton.Left, true);
            await Frames(1);
            for (int i = 1; i <= 8; i++)
            {
                var p = from.Lerp(to, i / 8f);
                Input.ParseInputEvent(new InputEventMouseMotion { Device = TEST_DEVICE, Position = p, GlobalPosition = p, Relative = (to - from) / 8f, ButtonMask = MouseButtonMask.Left });
                await Frames(1);
            }
            Mouse(to, MouseButton.Left, false);
            await Frames(2);
        }
        Check("A drag from the shelf to the bag takes one", Inside("food") == 2 && Bagged("food") == 1, $"shelf {Inside("food")}, bag {Bagged("food")}");

        var takeAll = FindButton(inventory, "← Take everything");
        if (takeAll != null) await Click(takeAll.GetGlobalRect().GetCenter());
        await Frames(2);
        Check("Take everything empties the shelf", Inside("food") == 0 && Bagged("food") == 3, $"shelf {Inside("food")}, bag {Bagged("food")}");

        // Wear the coat from the bag, then take it off again.
        var wear = FindButton(inventory, "wear");
        if (wear != null) await Click(wear.GetGlobalRect().GetCenter());
        await Frames(2);
        bool worn = you.body == coat;
        var off = inventory.SlotButton("body", "take off");
        if (off != null) await Click(off.GetGlobalRect().GetCenter());
        await Frames(2);
        Check("Wear the coat, then take it off into the bag", worn && you.body == null && you.pack.Contains(coat));

        await Tap(Godot.Key.Escape);
        await Frames(1);
        Check("Esc closes the inventory", !view.showInventory && !inventory.Visible && view.openContainer == null && view.menu == null);
        view.paused = wasPaused;
    }
}
