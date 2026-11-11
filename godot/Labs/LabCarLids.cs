// The character lab, part seven (b): the car's boot and bonnet (LabCar.cs), opened and shut by the
// character, as the sim's openBoot / closeBoot / openHood / closeHood actions (VehicleActions.cs):
// one action with progress 0..1, the character at the anchor (behind the car for the boot, in front
// for the bonnet), facing it.
//   open   bend to the lid's edge, fingers under it (palms up), lift it until it's near the top of a
//          standing reach, let go: it swings on to its stop (the bonnet 40 deg, the boot 45)
//   shut   reach up to the raised edge, palms on top, push it down, straighten up
// Both hands on the edge by hand IK (the shoulder reaches in, ShoulderReach); the spine bends
// forward where the edge is low.
//
//   --lid=boot|hood [--lidopen] (start with it open, to shut it) --lidat=P (hold at progress P)

using System;
using System.Collections.Generic;
using Godot;

namespace LoonieRoo.Game.Characters;

public partial class Lab
{
    Node3D? carHood, carBoot;
    readonly Dictionary<string, Node3D[]> lidGrips = new();  // "hoodUnder", "hoodTop", "bootUnder", "bootTop" -> left, right
    string? lidAction;        // openBoot, closeBoot, openHood, closeHood
    double lidP, lidHoldAt = -1, lidDuration = 1.2;
    bool bootOpen, hoodOpen;
    // Open no further than a standing reach can take back: at 50 deg the boot's edge was 26 cm out of reach to shut.
    const float HoodOpen = 40, BootOpen = 45;
    // Hinges (car frame) and lid lengths: the bonnet from the windscreen forward, the boot lid from the rear window back.
    static readonly Vector3 HoodHinge = new(0, 0.955f, 0.80f), BootHinge = new(0, 0.995f, -0.95f);
    const float HoodLength = 1.26f, BootLength = 1.12f;
    static readonly Vector3 HoodStand = new(0, 0, 2.26f), BootStand = new(0, 0, -2.30f);  // just clear of the bumpers

    /// <summary>The lids and their grips, onto the car (called from BuildCar).</summary>
    void BuildLids(Node3D carNode, Color paint, Color trim)
    {
        // Dark bays under the lids (seen when they're up).
        carNode.AddChild(Part(new BoxMesh { Size = new Vector3(1.5f, 0.02f, 1.2f) }, new Vector3(0, 0.935f, 1.42f), trim));
        carNode.AddChild(Part(new BoxMesh { Size = new Vector3(1.5f, 0.02f, 1.05f) }, new Vector3(0, 0.975f, -1.50f), trim));
        carHood = new Node3D { Name = "Hood", Position = HoodHinge };
        carNode.AddChild(carHood);
        carHood.AddChild(Part(new BoxMesh { Size = new Vector3(1.68f, 0.03f, HoodLength) }, new Vector3(0, 0, HoodLength / 2), paint));
        carBoot = new Node3D { Name = "Boot", Position = BootHinge };
        carNode.AddChild(carBoot);
        carBoot.AddChild(Part(new BoxMesh { Size = new Vector3(1.68f, 0.03f, BootLength) }, new Vector3(0, 0, -BootLength / 2), paint));
        // Grips at the free edge, a shoulder's width apart: under it (palm up, fingers in under the lid)
        // and on top (palm down, fingers towards the hinge).
        foreach (var (lid, node, edge, inward) in new[] { ("hood", carHood, HoodLength, -1f), ("boot", carBoot, -BootLength, 1f) })
        {
            var under = new Node3D[2];
            var top = new Node3D[2];
            for (int i = 0; i < 2; i++)
            {
                // The character faces the lid's edge: its left is the car's -x at the bonnet (it faces -z), +x at the boot.
                float x = (i == 0 ? 0.22f : -0.22f) * (lid == "hood" ? -1 : 1);
                var at = new Vector3(x, 0, edge - inward * 0.04f);
                under[i] = GripAt($"{lid}_under_{i}", at + new Vector3(0, -0.035f, 0), Vector3.Up, new Vector3(0, 0, inward));
                top[i] = GripAt($"{lid}_top_{i}", at + new Vector3(0, 0.035f, 0), Vector3.Down, new Vector3(0, 0, inward));
                node.AddChild(under[i]);
                node.AddChild(top[i]);
            }
            lidGrips[lid + "Under"] = under;
            lidGrips[lid + "Top"] = top;
        }
    }

    void BuildLidRow(VBoxContainer box)
    {
        var row = new HBoxContainer();
        row.AddChild(Btn("Boot", () => StartLid("boot")));
        row.AddChild(Btn("Bonnet", () => StartLid("hood")));
        box.AddChild(row);
    }

    void SetupLids(Dictionary<string, string> args)
    {
        if (args.TryGetValue("lidat", out var at)) lidHoldAt = Num(at);
        if (args.TryGetValue("lid", out var lid))
        {
            if (args.ContainsKey("lidopen")) { bootOpen = lid == "boot"; hoodOpen = lid == "hood"; }
            CallDeferred(nameof(StartLid), lid);
        }
    }

    /// <summary>Open the lid if it's shut, shut it if it's open: the character goes to its anchor.</summary>
    void StartLid(string lid)
    {
        if (car == null) BuildCar();
        if (seat == null) return;
        carActive = true;
        carP = 0;
        carDir = 0;
        carHoldAt = 0;  // standing outside, the door shut
        bool open = lid == "boot" ? bootOpen : hoodOpen;
        lidAction = (open ? "close" : "open") + (lid == "boot" ? "Boot" : "Hood");
        lidDuration = lid == "boot" ? 1.0 : 1.2;
        lidP = lidHoldAt >= 0 ? lidHoldAt : 0;
    }

    bool LidOn => lidAction != null;

    /// <summary>Each frame (from UpdateCar): the lid's angle, where the character stands, its bend.</summary>
    void UpdateLid(double delta)
    {
        if (carHood == null || carBoot == null) return;
        double hood = hoodOpen ? HoodOpen : 0, boot = bootOpen ? BootOpen : 0;
        if (lidAction != null)
        {
            if (lidHoldAt >= 0) lidP = lidHoldAt;
            else lidP = Math.Min(1, lidP + delta / lidDuration);
            bool isBoot = lidAction.EndsWith("Boot"), opening = lidAction.StartsWith("open");
            double max = isBoot ? BootOpen : HoodOpen;
            double angle = opening ? Keys(lidP, (0.25, 0), (0.70, max * 0.75), (0.90, max)) : Keys(lidP, (0.25, max), (0.80, 0));
            if (isBoot) boot = angle; else hood = angle;
            // The character at the anchor, facing the car; bent to the edge where it's low.
            testPosition = isBoot ? BootStand : HoodStand;
            heading = Math.PI / 2 - (isBoot ? 0 : Math.PI);  // facing +z at the boot, -z at the bonnet
            speed = targetSpeed = 0;
            seat!.Bend = (float)(opening ? Keys(lidP, (0, 0), (0.22, 1), (0.65, 0)) : Keys(lidP, (0.30, 0), (0.80, 1), (1.0, 0)));
            seat.Yaw = isBoot ? 0 : 180;
            if (lidP >= 1 && lidHoldAt < 0)
            {
                if (isBoot) bootOpen = opening; else hoodOpen = opening;
                lidAction = null;
                seat.Bend = 0;
            }
        }
        // Lids turn about their hinges, the free edge rising: the bonnet's edge (+z) up is a turn about -x.
        carHood.Rotation = new Vector3(-Mathf.DegToRad((float)hood), 0, 0);
        carBoot.Rotation = new Vector3(Mathf.DegToRad((float)boot), 0, 0);
    }

    /// <summary>The hands on the lid's edge while opening or shutting it; false when no lid action.</summary>
    bool LidHands()
    {
        if (lidAction == null || handIk == null) return false;
        bool isBoot = lidAction.EndsWith("Boot"), opening = lidAction.StartsWith("open");
        var grips = lidGrips[(isBoot ? "boot" : "hood") + (opening ? "Under" : "Top")];
        float w = (float)(opening ? Keys(lidP, (0, 0), (0.22, 1), (0.70, 1), (0.80, 0)) : Keys(lidP, (0, 0), (0.25, 1), (0.78, 1), (0.88, 0)));
        for (int i = 0; i < 2; i++) { handIk.Grip[i] = grips[i]; handIk.Weight[i] = w; }
        return true;
    }

    string LidReadout() => lidAction == null ? "" : $"lid      {lidAction} {lidP:0.00}";
}
