// The character lab, part six: clothes (Wardrobe.cs). A garment per slot with a colour, the
// outfit's condition (dirt, blood, torn, wet), a random outfit; the crowd gets random outfits too,
// near as pieces, mid merged into one mesh (OutfitMerge.cs). The mid twin shows the merge beside
// the character.
//
//   --outfit=shirt,jacket,trousers,boots,gloves,cap,backpack --dirt= --blood= --torn= --wet=
//   --randomoutfit=SEED --twin

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LoonieRoo.Game.Characters;

public partial class Lab
{
    Dresser? dresser;
    Outfit outfit = new();
    readonly Dictionary<string, (OptionButton garment, OptionButton color)> slotPicks = new();
    readonly List<HSlider> conditionSliders = new();
    readonly RandomNumberGenerator outfitRng = new() { Seed = 7 };

    void SetupClothes(Dictionary<string, string> args)
    {
        dresser = new Dresser(actor, Find<Skeleton3D>(actor)!);
        if (args.TryGetValue("randomoutfit", out var rs)) { outfitRng.Seed = ulong.Parse(rs); outfit = Outfit.Random(outfitRng); }
        if (args.TryGetValue("outfit", out var list))
            foreach (var id in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (Garments.Defs.TryGetValue(id, out var g)) outfit.Worn[g.Slot] = id;
        if (args.TryGetValue("dirt", out var d)) outfit.Dirt = Num(d);
        if (args.TryGetValue("blood", out var b)) outfit.Blood = Num(b);
        if (args.TryGetValue("torn", out var t)) outfit.Torn = Num(t);
        if (args.TryGetValue("wet", out var w)) outfit.Wet = Num(w);
        if (args.ContainsKey("twin")) SetTwin(true);
        ApplyClothes();
    }

    void BuildClothesTab(VBoxContainer box)
    {
        var grid = new GridContainer { Columns = 3 };
        foreach (var slot in Garments.Slots)
        {
            grid.AddChild(new Label { Text = slot, CustomMinimumSize = new Vector2(56, 0) });
            var garment = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(110, 0) };
            garment.AddItem("none");
            foreach (var g in Garments.InSlot(slot)) garment.AddItem(g.Id);
            var color = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 0) };
            foreach (var (name, _) in Garments.Palette) color.AddItem(name);
            string s = slot;
            garment.ItemSelected += i => { if (i == 0) outfit.Worn.Remove(s); else outfit.Worn[s] = garment.GetItemText((int)i); ApplyClothes(); };
            color.ItemSelected += i =>
            {
                if (!outfit.Worn.TryGetValue(s, out var id)) return;
                if (i == 0) outfit.Dye.Remove(id); else outfit.Dye[id] = Garments.Palette[i].color;
                ApplyClothes();
            };
            grid.AddChild(garment);
            grid.AddChild(color);
            slotPicks[slot] = (garment, color);
        }
        box.AddChild(grid);
        conditionSliders.Add(Slider(box, "Dirt", 0, 1, 0.01, outfit.Dirt, v => { outfit.Dirt = v; ApplyClothes(); }));
        conditionSliders.Add(Slider(box, "Blood", 0, 1, 0.01, outfit.Blood, v => { outfit.Blood = v; ApplyClothes(); }));
        conditionSliders.Add(Slider(box, "Torn", 0, 1, 0.01, outfit.Torn, v => { outfit.Torn = v; ApplyClothes(); }));
        conditionSliders.Add(Slider(box, "Wet", 0, 1, 0.01, outfit.Wet, v => { outfit.Wet = v; ApplyClothes(); }));
        var row = new HBoxContainer();
        row.AddChild(Btn("Random outfit", () => { outfit = Outfit.Random(outfitRng); ApplyClothes(); SyncClothes(); }));
        row.AddChild(Btn("Undress", () => { outfit = new Outfit(); ApplyClothes(); SyncClothes(); }));
        var twinBox = new CheckBox { Text = "Mid twin", ButtonPressed = twin != null };
        twinBox.Toggled += SetTwin;
        row.AddChild(twinBox);
        box.AddChild(row);
        SyncClothes();
    }

    void SyncClothes()
    {
        foreach (var (slot, (garment, color)) in slotPicks)
        {
            int gi = 0, ci = 0;
            if (outfit.Worn.TryGetValue(slot, out var id))
            {
                for (int i = 0; i < garment.ItemCount; i++) if (garment.GetItemText(i) == id) gi = i;
                if (outfit.Dye.TryGetValue(id, out var c)) ci = Math.Max(0, Array.FindIndex(Garments.Palette, p => p.color == c));
            }
            garment.Select(gi);
            color.Select(ci);
        }
        double[] values = { outfit.Dirt, outfit.Blood, outfit.Torn, outfit.Wet };
        for (int i = 0; i < conditionSliders.Count; i++) conditionSliders[i].SetValueNoSignal(values[i]);
    }

    /// <summary>The body regions a missing limb takes away (LabBody's cuts).</summary>
    HashSet<string> CutRegions()
    {
        var cut = new HashSet<string>();
        for (int i = 0; i < 2; i++)
        {
            for (int r = 0; r < armCut[i]; r++) cut.Add(ArmRegions[r] + "_" + Sides[i]);
            for (int r = 0; r < legCut[i]; r++) cut.Add(LegRegions[r] + "_" + Sides[i]);
        }
        return cut;
    }

    void ApplyClothes()
    {
        dresser?.Apply(outfit, CutRegions());
        ApplyTwin();
    }

    string ClothesReadout() => (outfit.Worn.Count == 0 ? "" : $"wearing  {string.Join(", ", Garments.Slots.Where(outfit.Worn.ContainsKey).Select(s => outfit.Worn[s]))}") +
                               (twin != null ? (outfit.Worn.Count == 0 ? "" : "\n") + twinInfo : "");

    /// <summary>A crowd member's outfit: random, stable per member (the same at near and mid).</summary>
    static Outfit CrowdOutfit(int index) => Outfit.Random(new RandomNumberGenerator { Seed = (ulong)(index * 7919 + 13) });

    /// <summary>A crowd member of the near tier: the outfit's pieces on its skeleton.</summary>
    static void DressCrowd(Node3D node, int index)
    {
        var d = new Dresser(node, Find<Skeleton3D>(node)!) { Seed = index % 50 };
        var o = CrowdOutfit(index);
        d.KeepOnly(o);
        d.Apply(o, new HashSet<string>());
    }

    /// <summary>A crowd member of the mid tier: the same outfit merged into its one mesh.</summary>
    static void DressMid(Node3D node, int index) =>
        OutfitMerge.Dress(Find<MeshInstance3D>(node)!, CrowdOutfit(index), new HashSet<string>(), index % 50);

    // The mid twin (--twin, or the Clothes tab): a mid-tier copy of the lab's character, 1.2 m to its
    // left, in the same outfit merged into one mesh, standing idle: near and mid side by side.
    Node3D? twin;

    void SetTwin(bool on)
    {
        twin?.QueueFree();
        twin = null;
        if (!on) return;
        twin = GD.Load<PackedScene>(MergedMannequin).Instantiate<Node3D>();
        actor.AddChild(twin);
        twin.Position = new Vector3(1.2f, 0, 0);  // the character's left (it faces +Z): screen right from the front
        var player = Find<AnimationPlayer>(twin)!;
        player.GetAnimationLibrary("").GetAnimation("idle").LoopMode = Animation.LoopModeEnum.Linear;
        player.Play("idle");
        ApplyTwin();
    }

    void ApplyTwin()
    {
        if (twin == null) return;
        OutfitMerge.Dress(Find<MeshInstance3D>(twin)!, outfit, CutRegions(), 0);
        var m = (ArrayMesh)Find<MeshInstance3D>(twin)!.Mesh;
        twinInfo = $"mid twin {OutfitMerge.Triangles(m)} tris, 1 surface; near {dresser?.VisiblePieces ?? 0} garment pieces";
    }

    string twinInfo = "";
}
