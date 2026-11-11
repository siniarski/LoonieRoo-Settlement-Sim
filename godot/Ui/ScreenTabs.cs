// The M screen's tabs, top right: Map (the world, top-down) and Colony (the colony's management in
// the 2D top-down view), and, while you're playing someone, the way back to first person.
// With nobody under your control the colony view is the game itself, and the tabs stay up.

using Godot;
using Control = Godot.Control;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Ui;

public sealed partial class ScreenTabs : PanelContainer
{
    readonly View view = null!;
    readonly Button map = null!, colony = null!, world = null!, back = null!;

    public ScreenTabs() { }

    public ScreenTabs(View view)
    {
        this.view = view;
        Theme = Kit.MakeTheme();
        MouseFilter = MouseFilterEnum.Stop;
        var row = new HBoxContainer();
        AddChild(row);
        map = Kit.Button("Map", () => view.openTab("map"), tip: "The ground round you from above, to look at");
        colony = Kit.Button("Manage", () => view.openTab("colony"), tip: "The same view with the colony's controls: build, priorities, the overview, neighbours and visitors");
        world = Kit.Button("World", () => view.openTab("world"), tip: "The whole world as far as you've seen it");
        back = Kit.Button("Back (M)", () => view.closeMScreen(), tip: "Back to first person (M or Esc)");
        row.AddChild(map);
        row.AddChild(colony);
        row.AddChild(world);
        row.AddChild(back);
        AnchorLeft = AnchorRight = 1;
        OffsetLeft = OffsetRight = -12;
        OffsetTop = OffsetBottom = 8;
        GrowHorizontal = GrowDirection.Begin;
    }

    public void Update(SimState state)
    {
        bool controlling = ControlMod.controlled(state) != null;
        Visible = view.inGame && view.menu == null && !view.firstPerson;
        if (!Visible) return;
        back.Visible = controlling;
        Kit.SetActive(map, !view.showMap && !view.manage);
        Kit.SetActive(colony, !view.showMap && view.manage);
        Kit.SetActive(world, view.showMap);
    }
}
