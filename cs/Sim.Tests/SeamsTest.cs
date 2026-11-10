// Where the features of 2026-11-10 meet (the merge): fire and growth, fire and snow, the weapons and the flashlight in
// the grid inventory.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public class SeamsTest
{
    static CommandResult cmd(SimState state, Command c) => Commands.applyCommand(state, c);

    [Fact(DisplayName = "seams > grass that burns out starts its growth again (Growth.clear), under its scar")]
    public void T001_fire_clears_growth()
    {
        var state = WildfireTest.setup(WildfireTest.open(30, 7));
        expect(state.cleared.Count).toBe(0);
        FireMod.ignite(state, 15, 3);
        WildfireTest.run(state, 600);
        expect(state.burnt.Count).toBeGreaterThan(3);
        foreach (var k in state.burnt.Keys)
        {
            var p = k.Split(',');
            int x = int.Parse(p[0]), y = int.Parse(p[1]);
            var at = Js.Str(Grids.tileIndex(state.grid, x, y));
            expect(state.cleared.ContainsKey(at), $"burnt {k} isn't cleared").toBe(true);
            expect(Growth.epochOf(state, Grids.tileIndex(state.grid, x, y))).toBeGreaterThan(WildfireTest.SUMMER);
        }
    }

    [Fact(DisplayName = "seams > grass under lying snow doesn't catch; the same fire without the snow spreads")]
    public void T002_snow_stops_grass_fire()
    {
        var bare = WildfireTest.setup(WildfireTest.open(30, 7));
        FireMod.ignite(bare, 15, 3);
        WildfireTest.run(bare, 200);
        expect(bare.burnt.Count + bare.fires.Count).toBeGreaterThan(5);

        var snowy = WildfireTest.setup(WildfireTest.open(30, 7));
        snowy.snow = new SnowState { depth = [500, 500, 500], fallen = [0, 0, 0], melted = [0, 0, 0] };
        FireMod.ignite(snowy, 15, 3);
        WildfireTest.run(snowy, 200);
        // The premise: the snow outlasted the test (a heatwave melts it, but not half a metre in 20 seconds).
        expect(Snow.depthAt(snowy, 16, 3)).toBeGreaterThan(0);
        expect(snowy.burnt.Count + snowy.fires.Count).toBeLessThanOrEqual(1);
    }

    [Fact(DisplayName = "seams > the new weapons, their ammunition and the flashlight have footprints, take quick slots and are used from the bag")]
    public void T003_weapons_in_the_grid()
    {
        var state = InventoryTest.setup(0);
        var s = Js.NN(Js.Get(state.survivors, 0));
        Item take(string kind)
        {
            var item = Items.spawnItem(state, kind, 1, 2);
            expect(cmd(state, new Command { type = "take", itemId = item.id }).reason).toBeNull();
            return item;
        }
        var pistol = take("pistol");
        var rifle = take("rifle");
        var torch = take("flashlight");
        expect(Inventory.sizeOf(pistol)).toEqual((2, 2, 1));
        expect(Inventory.sizeOf(rifle)).toEqual((2, 5, 1));
        expect(Inventory.sizeOf(torch)).toEqual((1, 1, 1));
        // Each is a kind of its own, so each took a quick slot when picked up.
        expect(s.quick.includes(pistol.id)).toBe(true);
        expect(s.quick.includes(rifle.id)).toBe(true);
        expect(s.quick.includes(torch.id)).toBe(true);
        // Two boxes of a kind are one box in the bag (Inventory.stow), holding both boxes' rounds.
        var box = take("ammo308");
        var rounds = Weapons.rounds(box);
        var second = Items.spawnItem(state, "ammo308", 1, 2);
        var more = Weapons.rounds(second);
        expect(cmd(state, new Command { type = "take", itemId = second.id }).reason).toBeNull();
        expect(s.pack.filter((id) => Items.getItem(state, id)?.kind == "ammo308")).toHaveLength(1);
        expect(Weapons.rounds(box)).toBe(rounds + more);
        // Using the flashlight from the bag switches it; using the pistol wields it.
        expect(cmd(state, new Command { type = "useItem", itemId = torch.id }).reason).toBeNull();
        expect(torch.lit).toBe(true);
        expect(cmd(state, new Command { type = "useItem", itemId = pistol.id }).reason).toBeNull();
        InventoryTest.run(state, 5);
        expect(s.weapon).toBe(pistol.id);
        expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
    }
}
