// The self-test's hands steps (2026-11-11, the player's hand-played pass; SelfTest.cs calls them): the hotbar along
// the bottom shows the quick slots; 0 empties your hand and a click then punches (a fist drawn through the blow);
// a number key takes that slot's knife in hand, the same key puts it away; a pistol's slot takes it up, the right
// mouse held brings it to the sights (the `sights` command; the view narrows) and letting go lowers it; an empty
// pistol's click is a blow, drawn as a pistol-whip (no shot). The things are given with `take` commands; whatever
// you held is put back at the end. HANDS_SHOT=prefix saves pictures: prefix-hotbar.png, -punch.png, -sights.png.

using System;
using System.Linq;
using Task = System.Threading.Tasks.Task;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game;

public partial class Game
{
    async Task HandsSteps()
    {
        var you = ControlMod.controlled(state)!;
        var held = firstPerson.Held;
        var bar = hud.hotbar;
        var c = state.control!;
        string? had = you.weapon;
        string? shots = OS.GetEnvironment("HANDS_SHOT") is { Length: > 0 } p ? p : null;
        async Task Picture(string name)
        {
            if (shots == null) return;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng($"{shots}-{name}.png");
        }
        Item Give(string kind)
        {
            var item = Sim.Items.spawnItem(state, kind, you.pos.x, you.pos.y);
            Commands.applyCommand(state, new Command { type = "take", itemId = item.id });
            return item;
        }
        int SlotOf(Item item) => you.quick?.IndexOf(item.id) ?? -1;
        Key KeyOf(int slot) => Key.Key1 + slot;

        // 0: empty hands (what you held into the bag).
        await Tap(Key.Key0);
        await Frames(2);
        Check("0 puts the weapon in your hand away: your hands are empty", you.weapon == null, $"wielding {you.weapon ?? "nothing"}");
        // The three quick slots emptied first (the bag from the steps before fills them): the knife and the pistol take two.
        for (int q = 0; q < Inventory.QUICK_SLOTS; q++) Commands.applyCommand(state, new Command { type = "setQuick", slot = q });
        var knife = Give("knife");
        var pistol = Give("pistol");
        var box = Give("ammo");
        await Frames(3);
        int ks = SlotOf(knife), ps = SlotOf(pistol);
        Check("Picked up, the knife and the pistol take quick slots; the hotbar shows them along the bottom", ks >= 0 && ps >= 0 && bar.Visible && bar.Names.Contains("knife") && bar.Names.Contains("pistol"),
            $"slots {ks}, {ps}; shown {bar.Visible}: {bar.Names}");

        // Bare hands: a click punches, and the fist is drawn through the blow.
        double swung = c.swungTick ?? -1;
        for (int i = 0; i < 120 && state.tick < c.nextSwing; i++) await Frames(1); // past the last swing's cooldown
        await Click(GetViewport().GetVisibleRect().GetCenter());
        bool punched = false;
        var fistFrom = Vector3.Zero;
        float fistMoved = 0;
        for (int i = 0; i < 10; i++)
        {
            await Frames(1);
            if (held.Punching) { if (!punched) fistFrom = held.FistAt; punched = true; fistMoved = Math.Max(fistMoved, held.FistAt.DistanceTo(fistFrom)); }
            if (i == 3) await Picture("punch");
        }
        Check("With empty hands a click punches: the fist comes through on the swing", (c.swungTick ?? -1) > swung && punched && fistMoved > 0.1f,
            $"swungTick {swung} -> {c.swungTick}, fist drawn {punched}, moved {fistMoved:0.00} m");

        // The knife's key takes it in hand (lit on the hotbar); again puts it away.
        await Tap(KeyOf(ks));
        await Frames(2);
        Check($"{ks + 1} takes the knife in hand, lit on the hotbar", you.weapon == knife.id && bar.InHand == ks, $"wielding {you.weapon}, hotbar in hand {bar.InHand}");
        await Tap(KeyOf(ks));
        await Frames(2);
        Check($"{ks + 1} again puts it away: empty hands, the knife in the bag", you.weapon == null && you.pack.Contains(knife.id) && bar.InHand == -1, $"wielding {you.weapon ?? "nothing"}");

        // The pistol: its key, a reload, then the right mouse held for the sights.
        await Tap(KeyOf(ps));
        await Tap(KeyOf(ks)); // straight to the knife: the pistol goes back in the bag
        Check("A slot's key swaps weapons: the pistol into the bag, the knife in hand", you.weapon == knife.id && you.pack.Contains(pistol.id), $"wielding {you.weapon}");
        await Tap(KeyOf(ps));
        pistol.wear = 10;
        await Frames(20);
        await Picture("hotbar");
        var centre = GetViewport().GetVisibleRect().GetCenter();
        float fov0 = firstPerson.camera.Fov;
        Mouse(centre, MouseButton.Right, true);
        for (int i = 0; i < 20; i++) await Frames(1);
        Check("The right mouse held with a pistol aims down the sights: the gun up, the view narrowed", c.sights == true && held.Sights > 0.95 && firstPerson.camera.Fov < fov0 - 5 && !view.showInventory,
            $"sights {c.sights}, drawn {held.Sights:0.00}, fov {fov0:0} -> {firstPerson.camera.Fov:0}");
        await Picture("sights");
        Mouse(centre, MouseButton.Right, false);
        for (int i = 0; i < 20; i++) await Frames(1);
        Check("Letting go lowers it", c.sights != true && held.Sights < 0.05 && Math.Abs(firstPerson.camera.Fov - fov0) < 0.5f, $"sights {c.sights}, drawn {held.Sights:0.00}, fov {firstPerson.camera.Fov:0}");

        // Empty: a click is a blow, drawn as a pistol-whip, no shot.
        pistol.wear = 0;
        for (int i = 0; i < 120 && state.tick < c.nextSwing; i++) await Frames(1);
        swung = c.swungTick ?? -1;
        double shot = c.shotTick ?? -1;
        await Click(centre);
        bool whipped = false;
        for (int i = 0; i < 8; i++) { await Frames(1); whipped |= held.Whipping; }
        Check("An empty pistol's click is a blow, drawn as a pistol-whip (no kick, no shot)", (c.swungTick ?? -1) > swung && (c.shotTick ?? -1) == shot && whipped,
            $"swungTick {swung} -> {c.swungTick}, shotTick {shot} -> {c.shotTick}, whip drawn {whipped}");

        foreach (var it in new[] { knife, pistol, box })
        {
            Inventory.unpack(you, it.id);
            if (you.weapon == it.id) you.weapon = null;
            Sim.Items.removeItem(state, it);
        }
        if (had != null && Sim.Items.getItem(state, had) is { } old && you.pack.Contains(old.id)) Commands.applyCommand(state, new Command { type = "switchWeapon", itemId = old.id });
        await Frames(2);
    }
}
