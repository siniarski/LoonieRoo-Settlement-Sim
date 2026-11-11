// The grid inventory (2026-11-10): footprints, rotation, stacks, nesting, refusals, first-fit, old saves, replays.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public class InventoryGridTest
{
    static CommandResult cmd(SimState state, Command c) => Commands.applyCommand(state, c);

    /// <summary>You at (1, 1) beside an empty searched counter at (2, 1); `kinds` spawned beside you and taken into your bag.</summary>
    static (SimState state, Survivor s, Furniture crate, List<Item> items) start(params string[] kinds)
    {
        var state = InventoryTest.setup(0);
        var s = Js.NN(Js.Get(state.survivors, 0));
        var crate = Js.NN(state.furniture.find((f) => f.look == "counter"));
        List<Item> items = [];
        foreach (var kind in kinds)
        {
            var item = Items.spawnItem(state, kind, 1, 2);
            expect(cmd(state, new Command { type = "take", itemId = item.id }).reason).toBeNull();
            items.Add(item);
        }
        return (state, s, crate, items);
    }

    /// <summary>A thing straight into a container, as a search leaves it (no cell yet).</summary>
    static Item into(SimState state, string kind, string containerId)
    {
        var item = Items.spawnItem(state, kind, 0, 0);
        item.pos = null;
        item.inside = containerId;
        return item;
    }

    static Command move(Item item, string grid, int x, int y, bool turned = false, double? count = null) =>
        new() { type = "moveItem", itemId = item.id, containerId = grid, x = x, y = y, turned = turned, count = count };

    static string where(Item i) => $"{i.gx},{i.gy}{(i.turned == true ? "t" : "")}";

    /// <summary>A grid drawn as text: each cell the first letter of what covers it, "." if empty.</summary>
    static string picture(SimState state, Survivor s, string grid)
    {
        var stacks = Inventory.layout(state, s, grid);
        var (w, _) = Inventory.gridSize(state, grid)!.Value;
        var rows = Inventory.rowsOf(state, stacks, grid);
        var lines = new List<string>();
        for (var y = 0; y < rows; y++)
        {
            var line = "";
            for (var x = 0; x < w; x++)
            {
                var p = stacks.FirstOrDefault((q) => x >= q.x && x < q.x + q.w && y >= q.y && y < q.y + q.h);
                line += p == null ? "." : p.first.kind[0].ToString();
            }
            lines.Add(line);
        }
        return string.Join("\n", lines);
    }

    public class D001_placement
    {
        [Fact(DisplayName = "grid inventory > every kind has a footprint; 12 kg of one kind (but medkits) fits the bag, as it did by weight")]
        public void T001_sizes()
        {
            foreach (var kind in Inventory.ITEM_WEIGHT.Keys)
            {
                var (w, h, n) = Inventory.ITEM_SIZE[kind];
                expect(w * h * n).toBeGreaterThan(0);
                var weight = Inventory.ITEM_WEIGHT[kind];
                if (weight == double.PositiveInfinity || kind == "medkit")
                {
                    continue;
                }
                var (state, s, _, _) = start();
                var many = (int)Math.Floor(Inventory.PACK_MAX / weight);
                var took = 0;
                for (var i = 0; i < many; i++)
                {
                    var item = Items.spawnItem(state, kind, 1, 2);
                    if (Inventory.stow(state, s, item) == null) took++;
                }
                expect($"{kind} {took}").toBe($"{kind} {many}");
            }
        }

        [Fact(DisplayName = "grid inventory > a thing covers its footprint, turned a quarter turn too; overlaps and the edge refuse")]
        public void T002_footprints()
        {
            var (state, s, _, items) = start("axe", "hammer");
            var (axe, hammer) = (items[0], items[1]);
            // First-fit: the axe (2x4) at the corner, the hammer (1x3) beside it.
            expect(where(axe)).toBe("0,0");
            expect(where(hammer)).toBe("2,0");
            expect(cmd(state, move(hammer, "bag", 1, 1)).reason).toBe("something's in the way");
            expect(cmd(state, move(hammer, "bag", 7, 4)).reason).toBe("it doesn't fit there");
            // Turned, it lies 3 across.
            expect(cmd(state, move(hammer, "bag", 6, 5, true)).reason).toBe("it doesn't fit there");
            expect(cmd(state, move(hammer, "bag", 5, 5, false)).reason).toBe("it doesn't fit there");
            expect(cmd(state, move(hammer, "bag", 2, 5, true)).reason).toBeNull();
            expect(where(hammer)).toBe("2,5t");
            expect(picture(state, s, "bag")).toBe("aa......\naa......\naa......\naa......\n........\n..hhh...");
            expect(cmd(state, move(axe, "bag", 3, 4, true)).reason).toBe("something's in the way");
            expect(cmd(state, move(axe, "bag", 4, 3, true)).reason).toBeNull();
            expect(picture(state, s, "bag")).toBe("........\n........\n........\n....aaaa\n....aaaa\n..hhh...");
        }

        [Fact(DisplayName = "grid inventory > first-fit turns a thing when only that fits, and is the same every time")]
        public void T003_first_fit()
        {
            string run()
            {
                var (state, s, _, items) = start("wood", "wood", "wood", "medkit", "medkit");
                // Three plank bundles and two medkits fill the top four rows; a hammer (1x3) only fits turned, below.
                var last = Items.spawnItem(state, "hammer", 1, 2);
                expect(cmd(state, new Command { type = "take", itemId = last.id }).ok).toBe(true);
                expect(last.turned).toBe(true);
                return picture(state, s, "bag");
            }
            var once = run();
            expect(run()).toBe(once);
            expect(once).toBe("wwwwwwmm\nwwwwwwmm\nwwwwwwmm\nwwwwwwmm\nhhh.....\n........");
        }

        [Fact(DisplayName = "grid inventory > refusals say why: too heavy, no room, out of reach, a cart")]
        public void T004_refusals()
        {
            var (state, s, crate, _) = start("wood", "wood", "wood", "wood");
            var more = Items.spawnItem(state, "wood", 1, 2);
            expect(cmd(state, new Command { type = "take", itemId = more.id }).reason).toBe("your bag is too full");
            var cart = Items.spawnItem(state, "cart", 1, 2);
            expect(cmd(state, new Command { type = "take", itemId = cart.id }).reason).toBe("a cart won't go in a bag");
            // Space: twelve medkits (2x2) fill the bag at 3.6 kg; a book is light, but there's no room.
            var (state2, s2, _, _) = start(Enumerable.Repeat("medkit", 12).ToArray());
            var ninth = Items.spawnItem(state2, "book", 1, 2);
            expect(cmd(state2, new Command { type = "take", itemId = ninth.id }).reason).toBe("no room in your bag");
            expect(Inventory.packWeight(state2, s2)).toBeLessThan(Inventory.PACK_MAX);
            // Out of reach: a crate across the room.
            var far = FurnitureMod.addFurniture(state, "crate", 4, 2);
            far.loot = 0;
            expect(cmd(state, move(Items.getItem(state, s.pack[0]), far.id, 0, 0)).reason).toBe("out of reach");
            expect(cmd(state, new Command { type = "quickMove", itemId = s.pack[0], containerId = far.id }).reason).toBe("out of reach");
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }
    }

    public class D002_stacks
    {
        [Fact(DisplayName = "grid inventory > small things of a kind stack to their limit; split and merge")]
        public void T005_stacks()
        {
            var (state, s, _, items) = start("bandage", "bandage", "bandage", "bandage", "bandage", "bandage", "bandage");
            // Each stack as "x,y:count", in cell order; the first thing of the stack at a cell.
            List<string> counts() => Inventory.layout(state, s, "bag").OrderBy((p) => p.y).ThenBy((p) => p.x).Select((p) => $"{p.x},{p.y}:{p.items.Count}").ToList();
            Item at(int x, int y) => Inventory.layout(state, s, "bag").First((p) => p.x == x && p.y == y).first;
            expect(counts()).toEqual(new List<string> { "0,0:5", "1,0:2" });
            // Split two off the five: onto the first free cell.
            expect(cmd(state, new Command { type = "splitStack", itemId = items[0].id, count = 2 }).reason).toBeNull();
            expect(counts()).toEqual(new List<string> { "0,0:3", "1,0:2", "2,0:2" });
            // Merge: two onto three makes five; onto a full five, refused; a stack of two onto four: one goes.
            expect(cmd(state, move(at(2, 0), "bag", 0, 0)).reason).toBeNull();
            expect(counts()).toEqual(new List<string> { "0,0:5", "1,0:2" });
            expect(cmd(state, move(at(1, 0), "bag", 0, 0)).reason).toBe("that stack is full");
            expect(cmd(state, new Command { type = "splitStack", itemId = at(0, 0).id }).reason).toBeNull(); // half of five: two
            expect(counts()).toEqual(new List<string> { "0,0:3", "1,0:2", "2,0:2" });
            var more = Items.spawnItem(state, "bandage", 1, 2);
            expect(cmd(state, new Command { type = "take", itemId = more.id }).ok).toBe(true); // onto the first with room
            expect(counts()).toEqual(new List<string> { "0,0:4", "1,0:2", "2,0:2" });
            expect(cmd(state, move(at(1, 0), "bag", 0, 0)).reason).toBeNull();
            expect(counts()).toEqual(new List<string> { "0,0:5", "1,0:1", "2,0:2" });
            expect(cmd(state, new Command { type = "splitStack", itemId = at(1, 0).id }).reason).toBe("nothing to split");
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "grid inventory > swap, quick-move to the crate and back, arrange")]
        public void T006_swap_quick_arrange()
        {
            var (state, s, crate, items) = start("hammer", "gun", "food", "food", "bandage");
            var (hammer, gun) = (items[0], items[1]);
            expect(where(hammer)).toBe("0,0");
            expect(where(gun)).toBe("1,0");
            expect(cmd(state, new Command { type = "swapItems", itemId = hammer.id, id = gun.id }).reason).toBe("they don't fit each other's places");
            expect(cmd(state, move(gun, "bag", 4, 0)).reason).toBe("something's in the way");
            expect(cmd(state, move(gun, "bag", 6, 0)).reason).toBeNull();
            expect(cmd(state, new Command { type = "swapItems", itemId = hammer.id, id = gun.id }).reason).toBeNull();
            expect(where(hammer)).toBe("6,0");
            expect(where(gun)).toBe("0,0");
            // Quick-move: the tin into the counter, by first-fit; and back (no container: your grids).
            expect(cmd(state, new Command { type = "quickMove", itemId = items[2].id, containerId = crate.id }).reason).toBeNull();
            expect(items[2].inside).toBe(crate.id);
            expect(where(items[2])).toBe("0,0");
            expect(cmd(state, new Command { type = "quickMove", itemId = items[2].id }).reason).toBeNull();
            expect(s.pack).toContain(items[2].id);
            expect(cmd(state, new Command { type = "arrange" }).reason).toBeNull();
            expect(picture(state, s, "bag")).toBe("gghbff..\nggh.....\n..h.....\n........\n........\n........");
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }
    }

    public class D003_nesting
    {
        [Fact(DisplayName = "grid inventory > a coat keeps what's in its pockets: worn, in the bag, in a crate, on the ground")]
        public void T007_nesting()
        {
            var (state, s, crate, items) = start("coat", "food", "bandage");
            var (coat, tin, bandage) = (items[0], items[1], items[2]);
            expect(cmd(state, move(tin, coat.id, 0, 0)).reason).toBeNull();
            expect(tin.grid).toBe(coat.id);
            expect(s.pack).toContain(tin.id); // still carried, and weighed
            expect(cmd(state, move(coat, coat.id, 0, 0)).reason).toBe("it can't go inside itself");
            // Wear it: the pockets are one of your grids; take it off: still in them.
            expect(cmd(state, new Command { type = "useItem", itemId = coat.id }).ok).toBe(true);
            expect(s.body).toBe(coat.id);
            expect(Inventory.yourGrids(state, s)).toEqual(new List<string> { "bag", coat.id });
            expect(Inventory.gridOf(state, s, tin)).toBe(coat.id);
            expect(cmd(state, move(bandage, coat.id, 1, 0)).reason).toBeNull();
            expect(cmd(state, new Command { type = "unequip", itemId = coat.id }).reason).toBeNull();
            expect(Inventory.gridOf(state, s, tin)).toBe(coat.id);
            // Into the counter: the tin goes with it, and counts towards the counter's load.
            expect(cmd(state, new Command { type = "quickMove", itemId = coat.id, containerId = crate.id }).reason).toBeNull();
            expect(coat.inside).toBe(crate.id);
            expect(tin.inside).toBe(coat.id);
            expect(s.pack).not.toContain(tin.id);
            expect(Inventory.load(state, crate.id)).toBe(Inventory.ITEM_WEIGHT["coat"] + Inventory.ITEM_WEIGHT["food"] + Inventory.ITEM_WEIGHT["bandage"]);
            expect(Inventory.openGrids(state, crate.id)).toEqual(new List<string> { crate.id, coat.id });
            // Reach into its pockets there; a coat won't go into another coat.
            expect(cmd(state, move(tin, "bag", 7, 5)).reason).toBeNull();
            var coat2 = Items.spawnItem(state, "coat", 1, 2);
            expect(cmd(state, move(coat2, coat.id, 0, 0)).reason).toBe($"a {Items.nameOf(coat2)} won't go inside another");
            expect(cmd(state, move(tin, coat.id, 0, 0)).reason).toBeNull();
            // Take the coat back: the tin's carried again.
            expect(cmd(state, new Command { type = "take", itemId = coat.id }).reason).toBeNull();
            expect(tin.carriedBy).toBe(s.id);
            expect(tin.inside).toBeNull();
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "grid inventory > dropping a coat drops its pockets with it; picking it up (E) wears it with them")]
        public void T008_drop_a_bag()
        {
            var (state, s, _, items) = start("coat", "food");
            var (coat, tin) = (items[0], items[1]);
            expect(cmd(state, move(tin, coat.id, 1, 1)).ok).toBe(true);
            expect(cmd(state, new Command { type = "drop", itemId = coat.id }).ok).toBe(true);
            expect(coat.pos).not.toBeNull();
            expect(tin.pos).toBeNull();
            expect(tin.inside).toBe(coat.id);
            expect(s.pack).toHaveLength(0);
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
            expect(cmd(state, new Command { type = "use", x = coat.pos.x, y = coat.pos.y }).ok).toBe(true);
            expect(s.body).toBe(coat.id);
            expect(tin.carriedBy).toBe(s.id);
            expect(Inventory.gridOf(state, s, tin)).toBe(coat.id);
            expect(where(tin)).toBe("1,1");
            // Worn through, the coat leaves the tin in your bag.
            Items.removeItem(state, coat);
            s.body = null;
            expect(Inventory.gridOf(state, s, tin)).toBe("bag");
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "grid inventory > death drops everything as before, pockets too")]
        public void T009_death()
        {
            var (state, s, _, items) = start("coat", "food", "book", "bandage");
            expect(cmd(state, move(items[1], items[0].id, 0, 0)).ok).toBe(true);
            expect(cmd(state, new Command { type = "useItem", itemId = items[0].id }).ok).toBe(true);
            Needs.killSurvivor(state, s, "a fall");
            foreach (var item in items)
            {
                expect(item.pos).not.toBeNull();
                expect(item.carriedBy).toBeNull();
                expect(item.inside).toBeNull();
            }
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }
    }

    public class D004_saves_and_replays
    {
        [Fact(DisplayName = "grid inventory > an old save without places lays out the same on two loads, and a search's finds get places")]
        public void T010_old_save()
        {
            var (state, s, crate, _) = start("wood", "food", "bandage", "bandage", "hammer", "coat", "book");
            foreach (var kind in new[] { "wood", "wood", "gun", "food", "cloth" }) into(state, kind, crate.id);
            foreach (var item in state.items)
            {
                item.gx = null;
                item.gy = null;
                item.turned = null;
                item.grid = null;
            }
            s.quick = null;
            var file = Savefile.toSaveFile(state);
            expect(file).not.toContain("\"gx\"");
            string looks(SimState st)
            {
                var you = st.survivors[0];
                return picture(st, you, "bag") + "\n--\n" + picture(st, you, crate.id);
            }
            var a = Savefile.loadFile(file).state;
            var b = Savefile.loadFile(file).state;
            expect(looks(a)).toBe(looks(b));
            expect(looks(a)).toBe(looks(state));
            // The first move settles both grids where they were shown.
            var shown = looks(a);
            var tin = a.items.First((i) => i.kind == "food" && i.carriedBy != null);
            expect(cmd(a, new Command { type = "quickMove", itemId = tin.id, containerId = crate.id }).ok).toBe(true);
            expect(cmd(a, new Command { type = "quickMove", itemId = tin.id }).ok).toBe(true);
            expect(a.items.Where((i) => i.carriedBy != null && i.kind != "food").All((i) => i.gx != null)).toBe(true);
            expect(looks(a).Replace("f", "?")).toBe(shown.Replace("f", "?"));
            // A search lays its finds out at once.
            var state2 = InventoryTest.setup(3);
            expect(cmd(state2, new Command { type = "use", x = 2, y = 1 }).ok).toBe(true);
            InventoryTest.run(state2, 80);
            var finds = state2.items.filter((i) => i.inside != null);
            expect(finds).toHaveLength(3);
            expect(finds.All((i) => i.gx != null && i.gy != null)).toBe(true);
        }

        [Fact(DisplayName = "grid inventory > every move replays from the command log to the same state")]
        public void T011_replay()
        {
            var (state0, s0, crate0, _) = start("food", "food", "bandage", "bandage", "bandage", "coat", "hammer");
            var initial = States.cloneState(state0);
            initial.commandLog = [];
            var state = state0;
            var s = s0;
            var crate = crate0;
            var startTick = state.tick;
            state.commandLog = [];
            var bandage = state.items.First((i) => i.kind == "bandage");
            var coat = state.items.First((i) => i.kind == "coat");
            var tin = state.items.First((i) => i.kind == "food");
            var hammer = state.items.First((i) => i.kind == "hammer");
            expect(cmd(state, new Command { type = "splitStack", itemId = bandage.id, count = 1 }).ok).toBe(true);
            expect(cmd(state, move(tin, coat.id, 0, 0)).ok).toBe(true);
            expect(cmd(state, move(hammer, "bag", 5, 2, true)).ok).toBe(true);
            expect(cmd(state, new Command { type = "quickMove", itemId = coat.id, containerId = crate.id }).ok).toBe(true);
            expect(cmd(state, new Command { type = "useItem", itemId = state.items.First((i) => i.kind == "food" && i.carriedBy != null).id }).ok).toBe(true);
            InventoryTest.run(state, 40);
            expect(cmd(state, new Command { type = "arrange", containerId = crate.id }).ok).toBe(true);
            expect(cmd(state, new Command { type = "setQuick", slot = 2, itemId = hammer.id }).ok).toBe(true);
            expect(cmd(state, new Command { type = "moveItem", itemId = bandage.id, containerId = "ground" }).ok).toBe(true);
            cmd(state, new Command { type = "swapItems", itemId = hammer.id, id = state.items.First((i) => i.kind == "bandage" && i.carriedBy != null).id });
            InventoryTest.run(state, 5);
            expect(Replay.replay(initial, state.commandLog, state.tick)).toEqual(state);
        }
    }

    public class D005_quick_slots
    {
        [Fact(DisplayName = "grid inventory > quick slots: filled on pick-up, assignable, and passed on when used up")]
        public void T012_quick()
        {
            var (state, s, crate, items) = start("food", "food", "wood", "hammer");
            expect(s.quick).toEqual(new List<string> { items[0].id, "", "" }.Select((x, i) => i == 1 ? items[3].id : x).ToList());
            expect(Inventory.quickItem(state, s, 0)).toBe(items[0]);
            expect(cmd(state, new Command { type = "setQuick", slot = 2, itemId = items[2].id }).ok).toBe(true);
            expect(Inventory.quickItem(state, s, 2)).toBe(items[2]);
            expect(cmd(state, new Command { type = "setQuick", slot = 3, itemId = items[2].id }).reason).toBe("no such quick slot");
            // Eat the first tin: the slot passes to the other.
            s.satiety = 100;
            expect(cmd(state, new Command { type = "useItem", itemId = Inventory.quickItem(state, s, 0).id }).ok).toBe(true);
            InventoryTest.run(state, 40);
            expect(Inventory.quickItem(state, s, 0)).toBe(items[1]);
            // An old save's survivor (no slots) keeps the bag's first three.
            s.quick = null;
            expect(Inventory.quickItem(state, s, 1)).toBe(Items.getItem(state, s.pack[1]));
            expect(cmd(state, new Command { type = "setQuick", slot = 2 }).ok).toBe(true);
            expect(s.quick).toHaveLength(3);
        }
    }
}
