// Ported from tests/colonies.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class ColoniesTest
{

    public static SimState world()
    {
        return States.createState(new NewStateOptions { seed = 1, width = Js.I(120), height = Js.I(120) });
    }

    public static void dawn(SimState state, double day)
    {
        state.tick = Js.I(((double)(day) * (Time.DAY_LENGTH)));
        Colonies.updateColonies(state);
    }

    /// <summary>settlements on their own</summary>
    public class D001_settlements_on_their_own
    {

        [Fact(DisplayName = "settlements on their own > a settlement with no food starves, and one with none left falls")]
        public void T002_a_settlement_with_no_food_starves_and_one_with_none_left_fal()
        {
            var state = world();
            var n = Settlements.newSettlement(state, "Dry Hollow", 90, 90, 6, 0, 0);
            n.unfolded = true;
            double day = 1;
            while ((n.people > 0) && (day < 60))
            {
                dawn(state, day++);
            }
            expect(n.people).toBe(0);
            expect(state.events.some((e) => Js.T(new JsRegExp("starved at Dry Hollow", "").test(e.message)))).toBe(true);
            expect(state.events.some((e) => Js.T(new JsRegExp("Dry Hollow has fallen", "").test(e.message)))).toBe(true);
        }

        [Fact(DisplayName = "settlements on their own > a well-fed one sends food to a starving neighbour, or the starving raid")]
        public void T003_a_well_fed_one_sends_food_to_a_starving_neighbour_or_the_sta()
        {
            var state = world();
            var rich = Settlements.newSettlement(state, "Plenty", 20, 20, 6, 200, 2);
            var poor = Settlements.newSettlement(state, "Want", 40, 20, 6, 0, 0);
            rich.unfolded = poor.unfolded = true;
            state.settings.winter = true;
            bool helped()
            {
                return state.events.some((e) => Js.T(new JsRegExp("sent food|raided", "").test(e.message)));
            }
            for (var day = 1; (day <= 15) && !helped(); day++)
            {
                dawn(state, day);
            }
            expect(helped()).toBe(true);
            expect(Js.Missing(rich.ties, poor.id) ? null : (object?)(Js.Get(rich.ties, poor.id))).not.toBe(0);
        }

        [Fact(DisplayName = "settlements on their own > a crowded one splits, founding another")]
        public void T004_a_crowded_one_splits_founding_another()
        {
            var state = world();
            var n = Settlements.newSettlement(state, "Crowded", 60, 60, Colonies.SPLIT_AT, 500, 3);
            n.unfolded = true;
            dawn(state, 1);
            expect(state.settlements).toHaveLength(2);
            expect(Js.NN(Js.Get(state.settlements, 1)).people).toBe(Colonies.SPLIT_PEOPLE);
        }

        [Fact(DisplayName = "settlements on their own > unfolds into a walled compound with beds and food when someone comes near")]
        public void T005_unfolds_into_a_walled_compound_with_beds_and_food_when_someo()
        {
            var state = world();
            var n = Settlements.newSettlement(state, "Near", 60, 60, 6, 20, 1);
            Survivors.addSurvivor(state, 60, 80);
            state.tick = Js.I(50);
            Colonies.updateColonies(state);
            expect(n.unfolded).toBe(true);
            var box = Colonies.compound(n);
            expect(Grids.getTile(state.grid, box.x0, box.y0)).toBe("wall");
            expect(Grids.getTile(state.grid, n.x, box.y1)).toBe("door");
            expect(state.furniture.filter((f) => f.kind == "bed")).toHaveLength(6);
            expect(state.furniture.some((f) => f.kind == "table")).toBe(true);
            expect(Residents.residentsOf(state, n)).toHaveLength(6);
            for (var t = 0; t < 30; t++)
            {
                Tick.tick(state);
                expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
            }
        }

        [Fact(DisplayName = "settlements on their own > a fallen one unfolds as a ruin, with the dead asleep inside")]
        public void T006_a_fallen_one_unfolds_as_a_ruin_with_the_dead_asleep_inside()
        {
            var state = world();
            var n = Settlements.newSettlement(state, "Gone", 60, 60, 0, 0, 0);
            Colonies.unfold(state, n);
            expect(state.dormant.length).toBeGreaterThan(0);
            expect(Residents.residentsOf(state, n)).toEqual(Js.Arr([]));
        }
    }

    /// <summary>you and them</summary>
    public class D007_you_and_them
    {

        [Fact(DisplayName = "you and them > food to a starving place counts double, and feeds them")]
        public void T008_food_to_a_starving_place_counts_double_and_feeds_them()
        {
            var state = world();
            var n = Settlements.newSettlement(state, "Want", 100, 100, 6, 0, 0);
            var s = Survivors.addSurvivor(state, 5, 5);
            for (var i = 0; i < 3; i++)
            {
                Items.spawnItem(state, "food", (s.pos.x + 1) + i, s.pos.y);
            }
            expect(Colonies.hungerOf(n)).toBe("starving");
            expect(Commands.applyCommand(state, new Command { type = "giftSettlement", settlementId = n.id })).toMatchObject(Js.Obj(("ok", (object?)(true))));
            expect(n.reputation).toBe(20);
            expect(n.food).toBe(3);
        }

        [Fact(DisplayName = "you and them > a raid needs someone armed, and makes an enemy either way")]
        public void T009_a_raid_needs_someone_armed_and_makes_an_enemy_either_way()
        {
            var state = world();
            var n = Settlements.newSettlement(state, "Mark", 100, 100, 2, 20, 0);
            var s = Survivors.addSurvivor(state, 5, 5);
            expect(Colonies.raidSettlement(state, n.id)).toMatch(new JsRegExp("armed", ""));
            Commands.applyCommand(state, new Command { type = "spawnWeapon", x = Js.I(5), y = Js.I(5) });
            s.weapon = Js.NN(state.items.find((i) => i.kind == "weapon")).id;
            Js.NN(state.items.find((i) => i.kind == "weapon")).pos = null;
            Js.NN(state.items.find((i) => i.kind == "weapon")).carriedBy = s.id;
            expect(Colonies.raidSettlement(state, n.id)).toBeNull();
            expect(n.reputation).toBeLessThan(-30);
            expect(Colonies.raidSettlement(state, n.id)).toMatch(new JsRegExp("already", ""));
        }
    }
}
