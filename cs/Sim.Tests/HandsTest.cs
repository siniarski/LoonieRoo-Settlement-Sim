// Your hands in play (2026-11-11, the player's first hand-played pass): a dry gun's click is a blow (the view's
// pistol-whip reads shotTick), the quick slots as a hotbar (quick, holster), and aiming down the sights.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public class HandsTest
{
    /// <summary>An open yard, you at (1, 5) of 20 x 11, controlled, nothing else about.</summary>
    static SimState yard()
    {
        var rows = new List<string>();
        for (var y = 0; y < 11; y++) rows.Add((y == 5) ? ".s" + new string('.', 18) : new string('.', 20));
        var state = ScenarioMod.loadScenario(new Scenario { seed = 5, map = rows });
        var s = state.survivors[0];
        s.priorities = Js.RecordFrom(JobTypes.NO_JOBS, __r0 => { });
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        state.zombiesTonight = 0;
        return state;
    }

    static Item wield(SimState state, string kind, double wear = 0)
    {
        var s = state.survivors[0];
        var item = Items.spawnItem(state, kind, 1, 5);
        item.pos = null;
        item.carriedBy = s.id;
        item.wear = wear;
        s.weapon = item.id;
        return item;
    }

    static Item bagged(SimState state, string kind)
    {
        var item = Items.spawnItem(state, kind, 1, 5);
        expect(Inventory.stow(state, state.survivors[0], item)).toBe(null);
        return item;
    }

    static CommandResult quick(SimState state, int slot) => Commands.applyCommand(state, new Command { type = "quick", slot = slot });

    [Fact(DisplayName = "hands > a gun with nothing in it swings as a club: swungTick moves, shotTick does not; a shot moves both")]
    public void dryGunIsABlow()
    {
        var state = yard();
        var s = state.survivors[0];
        var gun = wield(state, "pistol", 0);
        var z = Zombies.addZombie(state, 2, 5, 1000, "static");
        z.targetId = s.id;
        var before = z.health;
        expect(Commands.applyCommand(state, new Command { type = "swing", x = 2, y = 5 }).ok).toBe(true);
        expect(state.control.swungTick).toBe(state.tick);
        expect(state.control.shotTick).toBe(null);
        expect(z.health < before).toBe(true); // the butt of it
        expect(gun.wear).toBe(0);
        gun.wear = 3;
        state.tick += 30;
        expect(Commands.applyCommand(state, new Command { type = "fire", x = 1000, y = 0 }).ok).toBe(true);
        expect(state.control.shotTick).toBe(state.tick);
        expect(state.control.swungTick).toBe(state.tick);
        expect(gun.wear).toBe(2);
    }

    [Fact(DisplayName = "hands > a quick slot takes its weapon in hand; the same key puts it away and the hands are empty")]
    public void quickTakesAndPutsAway()
    {
        var state = yard();
        var s = state.survivors[0];
        var knife = bagged(state, "knife");
        expect(Commands.applyCommand(state, new Command { type = "setQuick", slot = 2, itemId = knife.id }).ok).toBe(true);
        expect(quick(state, 2).ok).toBe(true);
        expect(s.weapon).toBe(knife.id);
        expect(s.pack.includes(knife.id)).toBe(false);
        expect(Inventory.quickInHand(state, s, 2)?.id).toBe(knife.id);
        expect(quick(state, 2).ok).toBe(true);
        expect(s.weapon).toBe(null);
        expect(s.pack.includes(knife.id)).toBe(true);
        expect(s.note).toBe("put the hunting knife away");
        expect(s.quick[2]).toBe(knife.id); // the slot still names it
        expect(quick(state, 2).ok).toBe(true);
        expect(s.weapon).toBe(knife.id);
    }

    [Fact(DisplayName = "hands > a quick slot swaps weapons: the one in hand into the bag; an empty slot says so")]
    public void quickSwaps()
    {
        var state = yard();
        var s = state.survivors[0];
        var pistol = wield(state, "pistol", 5);
        var bat = bagged(state, "bat");
        Commands.applyCommand(state, new Command { type = "setQuick", slot = 0, itemId = bat.id });
        expect(quick(state, 0).ok).toBe(true);
        expect(s.weapon).toBe(bat.id);
        expect(s.pack.includes(pistol.id)).toBe(true);
        var empty = quick(state, 2);
        expect(empty.ok).toBe(false);
        expect(empty.reason).toBe("quick slot 3 is empty");
        expect(quick(state, 9).ok).toBe(false);
    }

    [Fact(DisplayName = "hands > holster empties the hand into the bag; with the bag full it says so and keeps it in hand")]
    public void holsterFullBag()
    {
        var state = yard();
        var s = state.survivors[0];
        var bar = wield(state, "crowbar");
        expect(Commands.applyCommand(state, new Command { type = "holster" }).ok).toBe(true);
        expect(s.weapon).toBe(null);
        expect(Commands.applyCommand(state, new Command { type = "holster" }).reason).toBe("nothing in your hand");
        expect(Commands.applyCommand(state, new Command { type = "switchWeapon", itemId = bar.id }).ok).toBe(true);
        // Fill the bag with logs until nothing more goes in.
        for (var n = 0; n < 200; n++)
        {
            var wood = Items.spawnItem(state, "wood", 1, 5);
            if (Inventory.stow(state, s, wood) != null) break;
        }
        var full = Commands.applyCommand(state, new Command { type = "holster" });
        expect(full.ok).toBe(false);
        expect(full.reason).toBe("your bag is full: the crowbar stays in your hand");
        expect(s.weapon).toBe(bar.id);
    }

    [Fact(DisplayName = "hands > down the sights: half the spread, 60% of the pace, no running; only with a gun in hand")]
    public void sights()
    {
        var state = yard();
        var s = state.survivors[0];
        var c = state.control;
        var pistol = Weapons.specOfKind("pistol")!;
        expect(Shooting.spreadOf(s, pistol, false, true)).toBe(0.035 / 2);
        expect(Shooting.spreadOf(s, pistol, true, true)).toBe(0.08 / 2);
        var walk = ControlMod.stepLength(state, s, c, false);
        expect(Commands.applyCommand(state, new Command { type = "sights", on = true }).ok).toBe(true);
        expect(ControlMod.sightsUp(state, s)).toBe(false); // bare hands: nothing to aim
        wield(state, "pistol", 5);
        expect(ControlMod.sightsUp(state, s)).toBe(true);
        expect(ControlMod.stepLength(state, s, c, false)).toBe(JsMath.round(walk * 0.6));
        c.running = true;
        c.dx = 1;
        var x0 = s.at.x;
        Tick.tick(state);
        expect(s.at.x - x0 <= walk).toBe(true); // not running
        expect(Commands.applyCommand(state, new Command { type = "sights", on = false }).ok).toBe(true);
        expect(c.sights).toBe(null);
        expect(ControlMod.sightsUp(state, s)).toBe(false);
    }
}
