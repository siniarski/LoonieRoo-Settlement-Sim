// The hotbar (2026-11-11; three slots since 2026-11-12, faint until something is in one): the quick slots along the bottom of the screen in first person, what each holds
// (InventoryQuick.cs: survivor.quick, set on the inventory screen by a drag or 1-9 over a thing), the one in your
// hand lit gold, and 0 for empty hands. Keys 1-9 send the `quick` command (that slot's thing in hand, or used; again,
// put away), 0 `holster` (App/GameInput.cs). Reads state only.

using System;
using Godot;
using Control = Godot.Control;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Ui;

public sealed partial class Hotbar : Control
{
    const float SLOT_W = 80, SLOT_H = 40, GAP = 3;
    /// <summary>What each slot shows now (name, kind; in hand), and the hands' state: redrawn only when it changes.</summary>
    readonly (string name, string kind, bool inHand)[] slots = new (string, string, bool)[Inventory.QUICK_SLOTS];
    bool empty;

    /// <summary>Which slot is in your hand (0-8), or -1 (the self-test).</summary>
    public int InHand { get; private set; } = -1;
    /// <summary>The names shown, slot by slot (the self-test).</summary>
    public string Names => string.Join(",", Array.ConvertAll(slots, (s) => s.name));

    public Hotbar()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Array.Fill(slots, ("", "", false));
        CustomMinimumSize = new Vector2(Inventory.QUICK_SLOTS * (SLOT_W + GAP) - GAP, SLOT_H);
    }

    public void Update(SimState state, Survivor? you, bool show)
    {
        Visible = show && you != null;
        if (!Visible || you == null) return;
        bool changed = false;
        int inHand = -1;
        for (int n = 0; n < Inventory.QUICK_SLOTS; n++)
        {
            var held = Inventory.quickInHand(state, you, n);
            var item = held ?? Inventory.quickItem(state, you, n);
            var now = (item != null ? Items.nameOf(item) : "", item?.kind ?? "", held != null);
            if (held != null && held.id == you.weapon) inHand = n;
            if (slots[n] != now) { slots[n] = now; changed = true; }
        }
        bool bare = !Js.T(you.weapon);
        if (changed || bare != empty || inHand != InHand) QueueRedraw();
        empty = bare;
        InHand = inHand;
    }

    public override void _Draw()
    {
        // Three slots and no more: the slot in your hand pressed again puts it away, and your hands are empty (0 too).
        for (int n = 0; n < Inventory.QUICK_SLOTS; n++)
        {
            var r = new Rect2(n * (SLOT_W + GAP), 0, SLOT_W, SLOT_H);
            bool hands = n == Inventory.QUICK_SLOTS;
            var (name, kind, inHand) = hands ? ("hands", "", empty) : slots[n];
            // An empty slot is barely there; one with something in it (or the hands' slot when your hands are empty) shows.
            bool filled = hands ? inHand : name != "";
            float a = filled ? 0.7f : 0.12f;
            DrawRect(r, filled && !hands ? InventoryScreen.KindColour(kind) with { A = a } : new Color(0.08f, 0.09f, 0.085f, a));
            DrawRect(r, inHand ? Kit.Gold : Ctx.Css("#3a423c") with { A = filled ? 0.9f : 0.25f }, false, inHand ? 2 : 1);
            DrawString(Kit.Font, r.Position + new Vector2(4, 12), hands ? "0" : $"{n + 1}", HorizontalAlignment.Left, -1, 11, inHand ? Kit.Gold : Kit.Dim with { A = filled ? 1f : 0.35f });
            if (name != "" && filled) DrawString(Kit.Font, r.Position + new Vector2(4, 31), name, HorizontalAlignment.Left, SLOT_W - 6, 10, Kit.Text);
        }
    }
}
