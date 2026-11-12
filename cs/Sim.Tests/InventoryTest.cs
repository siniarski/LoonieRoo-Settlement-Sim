// Ported from tests/inventory.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
using static LoonieRoo.Sim.Tests.TestKit;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class InventoryTest
{

    /// <summary>You, controlled, at (1, 1), with a kitchen counter at (2, 1) holding `loot` finds.</summary>
    public static SimState setup(double loot = 3)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [".....", ".s...", "....."] });
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = Js.NN(Js.Get(state.survivors, 0)).id });
        var counter = FurnitureMod.addFurniture(state, "crate", 2, 1);
        counter.look = "counter";
        counter.table = "kitchen";
        counter.loot = loot;
        return state;
    }

    public static void run(SimState state, double n) => TestKit.tickN(state, n);

    /// <summary>the bag</summary>
    public class D001_the_bag
    {

        [Fact(DisplayName = "the bag > searching a container turns up everything in it, and you take it into your bag")]
        public void T002_searching_a_container_turns_up_everything_in_it_and_you_take()
        {
            var state = setup(3);
            var s = Js.NN(Js.Get(state.survivors, 0));
            var counter = Js.NN(state.furniture.find((f) => f.look == "counter"));
            expect(Commands.applyCommand(state, new Command { type = "use", x = Js.I(2), y = Js.I(1) }).ok).toBe(true);
            run(state, 80);
            var inside = Inventory.contents(state, counter);
            expect(inside).toHaveLength(3);
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
            expect(Commands.applyCommand(state, new Command { type = "take", itemId = Js.NN(Js.Get(inside, 0)).id }).ok).toBe(true);
            expect(s.pack).toEqual(Js.Arr([Js.NN(Js.Get(inside, 0)).id]));
            expect(Commands.applyCommand(state, new Command { type = "takeAll", containerId = counter.id }).ok).toBe(true);
            expect(s.pack).toHaveLength(3);
            expect(Inventory.contents(state, counter)).toHaveLength(0);
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "the bag > holds only so much")]
        public void T003_holds_only_so_much()
        {
            var state = setup(0);
            var s = Js.NN(Js.Get(state.survivors, 0));
            double took = 0;
            for (var i = 0; i < 10; i++)
            {
                var wood = Items.spawnItem(state, "wood", 1, 2);
                if (Commands.applyCommand(state, new Command { type = "take", itemId = wood.id }).ok)
                {
                    took++;
                }
            }
            expect(took).toBe(JsMath.floor(((double)(Inventory.PACK_MAX) / (Inventory.ITEM_WEIGHT["wood"]))));
            expect(Inventory.packWeight(state, s)).toBeLessThanOrEqual(Inventory.PACK_MAX);
        }

        [Fact(DisplayName = "the bag > use from the bag: eat, wield (the old weapon into the bag); drop")]
        public void T004_use_from_the_bag_eat_wield_the_old_weapon_into_the_bag_drop()
        {
            var state = setup(0);
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.satiety = 100;
            var food = Items.spawnItem(state, "food", 1, 2);
            var knife = Items.spawnItem(state, "weapon", 2, 2);
            Commands.applyCommand(state, new Command { type = "take", itemId = food.id });
            Commands.applyCommand(state, new Command { type = "take", itemId = knife.id });
            expect(Commands.applyCommand(state, new Command { type = "useItem", itemId = food.id }).ok).toBe(true);
            run(state, 40);
            expect(s.satiety).toBeGreaterThan(1000);
            expect(s.pack).not.toContain(food.id);
            expect(Commands.applyCommand(state, new Command { type = "useItem", itemId = knife.id }).ok).toBe(true);
            expect(s.weapon).toBe(knife.id);
            expect(s.pack).not.toContain(knife.id);
            expect(Commands.applyCommand(state, new Command { type = "drop", itemId = Js.At(s.pack, 0, "none") }).ok).toBe(s.pack.length > 0);
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "the bag > things you pick up go in the bag; it all falls where you fall")]
        public void T005_things_you_pick_up_go_in_the_bag_it_all_falls_where_you_fall()
        {
            var state = setup(0);
            var s = Js.NN(Js.Get(state.survivors, 0));
            var book = Items.spawnItem(state, "book", 1, 2);
            expect(Commands.applyCommand(state, new Command { type = "use", x = Js.I(1), y = Js.I(2) }).ok).toBe(true);
            expect(s.pack).toContain(book.id);
            expect(s.carrying).toBeNull();
            Needs.killSurvivor(state, s, "a fall");
            expect(book.pos).not.toBeNull();
            expect(book.carriedBy).toBeNull();
        }
    }

    /// <summary>You and a searched crate beside you: `kinds` in your bag, nothing in the crate.</summary>
    public static (SimState state, Survivor s, Furniture crate) withBag(params string[] kinds)
    {
        var state = setup(0);
        var s = Js.NN(Js.Get(state.survivors, 0));
        var crate = Js.NN(state.furniture.find((f) => f.look == "counter"));
        foreach (var kind in kinds)
        {
            var item = Items.spawnItem(state, kind, 1, 2);
            expect(Commands.applyCommand(state, new Command { type = "take", itemId = item.id }).ok).toBe(true);
        }
        return (state, s, crate);
    }

    static double count(SimState state, string containerId, string kind) => Inventory.contents(state, new Furniture { id = containerId }).filter((i) => i.kind == kind).length;

    /// <summary>the inventory screen's moves: one, a stack (every one of a kind), or everything</summary>
    public class D006_moves
    {
        [Fact(DisplayName = "inventory moves > stash one, a stack, or everything; take a stack back")]
        public void T007_stash_one_a_stack_or_everything()
        {
            var (state, s, crate) = withBag("food", "food", "food", "bandage", "book");
            var food = s.pack.filter((id) => Items.getItem(state, id).kind == "food");
            expect(Commands.applyCommand(state, new Command { type = "stash", itemId = food[0], containerId = crate.id }).ok).toBe(true);
            expect(count(state, crate.id, "food")).toBe(1);
            expect(Commands.applyCommand(state, new Command { type = "stashAll", containerId = crate.id, kind = "food" }).ok).toBe(true);
            expect(count(state, crate.id, "food")).toBe(3);
            expect(s.pack).toHaveLength(2);
            expect(Commands.applyCommand(state, new Command { type = "stashAll", containerId = crate.id, kind = "food" }).reason).toBe("no food in your bag");
            expect(Commands.applyCommand(state, new Command { type = "stashAll", containerId = crate.id }).ok).toBe(true);
            expect(s.pack).toHaveLength(0);
            expect(Commands.applyCommand(state, new Command { type = "stashAll", containerId = crate.id }).reason).toBe("your bag is empty");
            expect(Commands.applyCommand(state, new Command { type = "takeAll", containerId = crate.id, kind = "food" }).ok).toBe(true);
            expect(s.pack).toHaveLength(3);
            expect(count(state, crate.id, "food")).toBe(0);
            expect(count(state, crate.id, "book")).toBe(1);
            expect(Commands.applyCommand(state, new Command { type = "takeAll", containerId = crate.id, kind = "food" }).reason).toBe("no food in it");
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "inventory moves > a stack goes in as far as it fits, and the rest stays in the bag")]
        public void T008_a_stack_goes_in_as_far_as_it_fits()
        {
            var (state, s, crate) = withBag("wood", "wood", "wood", "wood");
            // Fill the crate to within two planks of its hold (with guns: heavy for their size, so there's room left).
            for (var i = 0; i < 8; i++)
            {
                var gun = Items.spawnItem(state, "gun", 3, 2);
                gun.pos = null;
                gun.inside = crate.id;
            }
            expect(Inventory.load(state, crate.id)).toBe(240);
            expect(Commands.applyCommand(state, new Command { type = "stashAll", containerId = crate.id, kind = "wood" }).ok).toBe(true);
            expect(Inventory.load(state, crate.id)).toBe(Inventory.CRATE_HOLD);
            expect(s.pack).toHaveLength(2);
            expect(Commands.applyCommand(state, new Command { type = "stashAll", containerId = crate.id, kind = "wood" }).reason).toBe("it won't fit");
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "inventory moves > a container must be beside you, both ways")]
        public void T009_a_container_must_be_beside_you()
        {
            var (state, s, crate) = withBag("food");
            Commands.applyCommand(state, new Command { type = "stashAll", containerId = crate.id });
            s.pos = new Point { x = 4, y = 2 };
            expect(Commands.applyCommand(state, new Command { type = "takeAll", containerId = crate.id, kind = "food" }).reason).toBe("not beside you");
            s.pos = new Point { x = 1, y = 1 };
            var food = Items.spawnItem(state, "food", 1, 2);
            Commands.applyCommand(state, new Command { type = "take", itemId = food.id });
            s.pos = new Point { x = 0, y = 2 };
            expect(Commands.applyCommand(state, new Command { type = "stashAll", containerId = crate.id }).reason).toBe("not beside you");
            expect(Commands.applyCommand(state, new Command { type = "stashAll", containerId = "nothing" }).reason).toBe("not beside you");
        }

        [Fact(DisplayName = "inventory moves > wear and wield from the bag, take it off into the bag, or drop it")]
        public void T010_wear_wield_take_off_drop()
        {
            var (state, s, crate) = withBag("coat", "weapon", "axe");
            var coat = s.pack.find((id) => Items.getItem(state, id).kind == "coat");
            var knife = s.pack.find((id) => Items.getItem(state, id).kind == "weapon");
            var axe = s.pack.find((id) => Items.getItem(state, id).kind == "axe");
            expect(Commands.applyCommand(state, new Command { type = "useItem", itemId = coat }).ok).toBe(true);
            expect(Commands.applyCommand(state, new Command { type = "useItem", itemId = knife }).ok).toBe(true);
            expect(Commands.applyCommand(state, new Command { type = "useItem", itemId = axe }).ok).toBe(true);
            expect(s.body).toBe(coat);
            expect(s.weapon).toBe(knife);
            expect(s.tool).toBe(axe);
            expect(s.pack).toHaveLength(0);
            expect(Commands.applyCommand(state, new Command { type = "unequip", itemId = coat }).ok).toBe(true);
            expect(s.body).toBeNull();
            expect(s.pack).toEqual(Js.Arr([coat]));
            expect(Commands.applyCommand(state, new Command { type = "unequip", itemId = coat }).reason).toBe("you don't have it on");
            expect(Commands.applyCommand(state, new Command { type = "drop", itemId = knife }).ok).toBe(true);
            expect(s.weapon).toBeNull();
            expect(Items.getItem(state, knife).pos).not.toBeNull();
            expect(Items.getItem(state, knife).carriedBy).toBeNull();
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "inventory moves > taking something off with a full bag fails and leaves it on")]
        public void T011_taking_off_with_a_full_bag_fails()
        {
            var (state, s, crate) = withBag("armour");
            var armour = Js.At(s.pack, 0, "none");
            Commands.applyCommand(state, new Command { type = "useItem", itemId = armour });
            for (var i = 0; i < 4; i++)
            {
                var wood = Items.spawnItem(state, "wood", 1, 2);
                Commands.applyCommand(state, new Command { type = "take", itemId = wood.id });
            }
            expect(Commands.applyCommand(state, new Command { type = "unequip", itemId = armour }).reason).toBe("your bag is too full");
            expect(s.body).toBe(armour);
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "inventory moves > the moves are logged and replay to the same state")]
        public void T012_the_moves_replay()
        {
            var (state, s, crate) = withBag("food", "food", "coat");
            var coat = s.pack.find((id) => Items.getItem(state, id).kind == "coat");
            Commands.applyCommand(state, new Command { type = "stashAll", containerId = crate.id, kind = "food" });
            Commands.applyCommand(state, new Command { type = "useItem", itemId = coat });
            Commands.applyCommand(state, new Command { type = "unequip", itemId = coat });
            Commands.applyCommand(state, new Command { type = "stashAll", containerId = crate.id });
            Commands.applyCommand(state, new Command { type = "takeAll", containerId = crate.id, kind = "food" });
            var types = state.commandLog.map((e) => e.command.type);
            expect(types).toContain("stashAll");
            expect(types).toContain("unequip");
            expect(s.pack).toHaveLength(2);
            expect(count(state, crate.id, "coat")).toBe(1);
        }
    }

    /// <summary>item names</summary>
    public class D003_item_names
    {

        [Fact(DisplayName = "item names > every kind has a name, and a knife or a gun is called what it is")]
        public void T013_every_kind_has_a_name()
        {
            foreach (var kind in Js.ObjectKeys(Inventory.ITEM_WEIGHT))
            {
                expect(Items.NAMES.ContainsKey(kind)).toBe(true);
            }
            var state = setup(0);
            var knife = Items.spawnItem(state, "weapon", 1, 2);
            var gun = Items.spawnItem(state, "gun", 1, 2);
            expect(Items.NAMES["weapon"]).toContain(Items.nameOf(knife));
            expect(Items.NAMES["gun"]).toContain(Items.nameOf(gun));
            expect(Items.nameOf(knife)).not.toBe("weapon");
        }

        [Fact(DisplayName = "item names > a name comes from the item's id: the same every time, after a save, and spread over the kind's names")]
        public void T014_names_are_deterministic_and_saved()
        {
            var state = setup(0);
            var foods = new List<Item>();
            for (var i = 0; i < 40; i++)
            {
                foods.push(Items.spawnItem(state, "food", 1, 2));
            }
            var names = foods.map((f) => Items.nameOf(f));
            expect(Js.Set(names).size).toBeGreaterThan(4);
            var loaded = Js.NN(Savefile.loadFile(Savefile.toSaveFile(state)).state);
            expect(foods.map((f) => Items.nameOf(Js.NN(Items.getItem(loaded, f.id))))).toEqual(names);
            // An item from an old save has only its id and kind: that's all a name needs.
            expect(Items.nameOf(new Item { id = foods[0].id, kind = "food" })).toBe(names[0]);
        }

        [Fact(DisplayName = "item names > books by their subject, bodies by whose, stacks by a shared name or else the kind")]
        public void T015_books_bodies_stacks()
        {
            expect(Items.nameOf(new Item { id = "i1", kind = "book", tag = "farm" })).toBe("book on farming");
            expect(Items.nameOf(new Item { id = "i2", kind = "book", tag = "gunsmithing" })).toBe("book on gunsmithing");
            expect(Items.nameOf(new Item { id = "i3", kind = "corpse", tag = "Ana" })).toBe("Ana's body");
            var a = new Item { id = "i10", kind = "wood" };
            var b = new Item { id = "i11", kind = "wood" };
            expect(Items.stackName([a, b])).toBe("planks");
            var foods = new List<Item>();
            for (var i = 0; i < 20; i++) foods.push(new Item { id = $"i{100 + i}", kind = "food" });
            expect(Items.stackName(foods)).toBe("tinned food");
        }

        [Fact(DisplayName = "item names > what you're told uses the name")]
        public void T016_messages_use_the_name()
        {
            var (state, s, crate) = withBag("weapon", "coat");
            var knife = Js.NN(Items.getItem(state, Js.At(s.pack, 0, "none")));
            var coat = Js.NN(Items.getItem(state, Js.At(s.pack, 1, "none")));
            expect(Commands.applyCommand(state, new Command { type = "useItem", itemId = knife.id }).ok).toBe(true);
            expect(s.note).toBe($"took up the {Items.nameOf(knife)}");
            expect(Commands.applyCommand(state, new Command { type = "useItem", itemId = coat.id }).ok).toBe(true);
            expect(s.note).toBe($"put on the {Items.nameOf(coat)}");
        }
    }
}
