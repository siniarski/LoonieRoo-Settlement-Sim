// Weapons and firearms (2026-11-10): the table's numbers as hits and misses at set ranges, shots against walls, reloads,
// ammunition, noise, durability, old saves, replays and armed guards. The numbers themselves are in WeaponTable.cs.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class ArmsTest
{
    /// <summary>A long open yard: you at (1, 5) of 40 x 11, controlled, nothing else about.</summary>
    static SimState yard(Func<string, string>? tweak = null)
    {
        var rows = new List<string>();
        for (var y = 0; y < 11; y++)
        {
            var row = (y == 5) ? ".s" + new string('.', 38) : new string('.', 40);
            rows.Add(row);
        }
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

    /// <summary>A zombie standing still at a spot (SUB units from your centre), as aware of you as it likes.</summary>
    static Zombie dummy(SimState state, double dxSub, double dySub, double health = 1000, bool aware = true)
    {
        var s = state.survivors[0];
        var at = new Point { x = Js.I(s.at.x + dxSub), y = Js.I(s.at.y + dySub) };
        var z = Zombies.addZombie(state, Motion.tileOf(at).x, Motion.tileOf(at).y, health, "static");
        z.at = at;
        z.was = new Point { x = at.x, y = at.y };
        if (aware) z.targetId = s.id;
        return z;
    }

    static double tiles(double n) => n * Motion.SUB;

    /// <summary>One shot east, calm and still, at a dummy `d` tiles off: what it took off its health.</summary>
    static double shotAt(string kind, double d, out ShotOutcome outcome, double dySub = 0)
    {
        var state = yard();
        var s = state.survivors[0];
        var w = Weapons.specOf(new Item { kind = kind })!;
        var gun = wield(state, kind, w.magazine);
        var z = dummy(state, tiles(d), dySub);
        outcome = Shooting.fire(state, s, gun, 1000, 0, false);
        return 1000 - z.health;
    }

    static int blowsToKill(string kind, double health = 200)
    {
        var state = yard();
        var s = state.survivors[0];
        var c = state.control!;
        c.aim = new Point { x = 64, y = 0 };
        c.facing = new Point { x = 1, y = 0 };
        if (Weapons.specOf(new Item { kind = kind })!.slot == "tool")
        {
            var tool = Items.spawnItem(state, kind, 1, 5);
            tool.pos = null;
            tool.carriedBy = s.id;
            s.tool = tool.id;
        }
        else wield(state, kind);
        var z = dummy(state, 200, 0, health);
        for (var n = 1; n <= 30; n++)
        {
            state.tick += 100;
            c.nextSwing = 0;
            z.at = new Point { x = Js.I(s.at.x + 200), y = s.at.y };  // (a blow that doesn't kill knocks it back: it comes again)
            expect(ControlMod.swing(state, s, new Point { x = 2, y = 5 })).not.toBeNull();
            if (state.zombies.length == 0) return n;
        }
        return -1;
    }

    /// <summary>the weapons table</summary>
    public class D001_the_weapons_table
    {
        [Fact(DisplayName = "arms > every weapon's blows to kill a night-one zombie (200 health)")]
        public void T001_blows_to_kill()
        {
            // damage: knife 70, hatchet 100, machete 110, crowbar 100, bat 105, pipe 125, sledge 220, spear 95, plank 80, axe 150, hammer 85
            var want = new (string, int)[] { ("knife", 3), ("hatchet", 2), ("machete", 2), ("crowbar", 2), ("bat", 2), ("pipe", 2), ("sledge", 1), ("spear", 3), ("plank", 3), ("axe", 2), ("hammer", 3) };
            foreach (var (kind, blows) in want)
            {
                expect(blowsToKill(kind), kind).toBe(blows);
            }
            // The old generic weapon is the crowbar's numbers; bare hands do 50.
            expect(blowsToKill("weapon")).toBe(2);
        }

        [Fact(DisplayName = "arms > reach: a spear strikes at 1.8 tiles, a crowbar does not, a knife needs to be close")]
        public void T002_reach()
        {
            bool hitsAt(string kind, double dx, double dy = 0)
            {
                var state = yard();
                var s = state.survivors[0];
                state.control!.aim = new Point { x = 64, y = 0 };
                state.control.facing = new Point { x = 1, y = 0 };
                wield(state, kind);
                var z = dummy(state, dx, dy);
                ControlMod.swing(state, s, new Point { x = 2, y = 5 });
                return z.health < 1000;
            }
            expect(hitsAt("spear", 450)).toBe(true);
            expect(hitsAt("spear", 490)).toBe(false);
            expect(hitsAt("crowbar", 450)).toBe(false);
            expect(hitsAt("crowbar", 315)).toBe(true);
            expect(hitsAt("knife", 250)).toBe(true);
            expect(hitsAt("knife", 270)).toBe(false);
            expect(hitsAt("sledge", 350)).toBe(true);
        }

        [Fact(DisplayName = "arms > arc: a machete sweeps wide, a knife and a spear only what is ahead")]
        public void T003_arc()
        {
            bool hitsAtAngle(string kind, int degrees)
            {
                var state = yard();
                var s = state.survivors[0];
                state.control!.aim = new Point { x = 64, y = 0 };
                state.control.facing = new Point { x = 1, y = 0 };
                wield(state, kind);
                // 200 SUB off, `degrees` round from straight ahead (a table of sines and cosines, so no library decides it)
                var (dx, dy) = degrees switch { 0 => (200, 0), 35 => (164, 115), 50 => (129, 153), 70 => (68, 188), _ => (0, 200) };
                var z = dummy(state, dx, dy);
                ControlMod.swing(state, s, new Point { x = 2, y = 5 });
                return z.health < 1000;
            }
            expect(hitsAtAngle("machete", 70)).toBe(true);   // 75 degrees either way
            expect(hitsAtAngle("crowbar", 70)).toBe(false);  // 60
            expect(hitsAtAngle("crowbar", 50)).toBe(true);
            expect(hitsAtAngle("knife", 50)).toBe(false);    // 40
            expect(hitsAtAngle("knife", 35)).toBe(true);
            expect(hitsAtAngle("spear", 35)).toBe(false);    // 30
            expect(hitsAtAngle("spear", 0)).toBe(true);
        }

        [Fact(DisplayName = "arms > swing time, endurance and noise come from the weapon")]
        public void T004_swing_time_stamina_noise()
        {
            foreach (var kind in new[] { "knife", "crowbar", "sledge", "spear" })
            {
                var state = yard();
                var s = state.survivors[0];
                var c = state.control!;
                c.aim = new Point { x = 64, y = 0 };
                wield(state, kind);
                dummy(state, 200, 0);
                state.tick = 100;
                var spec = Weapons.specOf(new Item { kind = kind })!;
                var before = s.endurance;
                state.dormant = [new() { id = "d1", x = 1 + spec.noise, y = 5 }, new() { id = "d2", x = 1 + spec.noise + 1, y = 5 }];
                ControlMod.swing(state, s, new Point { x = 2, y = 5 });
                expect(c.nextSwing, kind).toBe(100 + spec.swing);
                expect(before - s.endurance, kind).toBe(spec.stamina);
                // Noise: a sleeper at the radius wakes, one a tile past does not.
                expect(state.dormant.map((d) => d.id), kind).toEqual(Js.Arr(["d2"]));
            }
        }

        [Fact(DisplayName = "arms > a two-handed weapon with the hands full is bare hands")]
        public void T005_two_handed()
        {
            var state = yard();
            var s = state.survivors[0];
            wield(state, "sledge");
            expect(Weapons.meleeSpec(state, s).kind).toBe("sledge");
            var box = Items.spawnItem(state, "wood", 2, 5);
            box.pos = null;
            box.carriedBy = s.id;
            s.carrying = box.id;
            expect(Weapons.meleeSpec(state, s).kind).toBe("unarmed");
            s.carrying = null;
            wield(state, "knife");
            s.carrying = box.id;
            expect(Weapons.meleeSpec(state, s).kind).toBe("knife");  // one hand is enough
        }

        [Fact(DisplayName = "arms > a melee weapon breaks at its own durability; an axe, a hammer and a gun do not")]
        public void T006_durability()
        {
            foreach (var (kind, kills) in new[] { ("plank", 4), ("knife", 12), ("sledge", 30) })
            {
                var state = yard();
                var s = state.survivors[0];
                var w = wield(state, kind);
                for (var n = 1; n < kills; n++) Weapons.wearWeapon(state, s);
                expect(s.weapon, kind).toBe(w.id);
                Weapons.wearWeapon(state, s);
                expect(s.weapon, kind).toBeNull();
                expect(state.items.some((i) => i.id == w.id), kind).toBe(false);
            }
            var st = yard();
            var ss = st.survivors[0];
            var axe = Items.spawnItem(st, "axe", 1, 5);
            axe.pos = null;
            axe.carriedBy = ss.id;
            ss.tool = axe.id;
            for (var n = 0; n < 100; n++) Weapons.wearWeapon(st, ss);
            expect(axe.wear).toBe(0);
            var gun = wield(st, "pistol", 5);
            for (var n = 0; n < 100; n++) Weapons.wearWeapon(st, ss);
            expect(gun.wear).toBe(5);
        }

        [Fact(DisplayName = "arms > loot tables sum to one and name only real kinds")]
        public void T007_tables()
        {
            foreach (var key in Js.ObjectKeys(Loot.LOOT_TABLES))
            {
                double sum = 0;
                foreach (var (kind, p) in Loot.LOOT_TABLES[key]) { sum += p; expect(Inventory.ITEM_WEIGHT.ContainsKey(kind), $"{key}: {kind}").toBe(true); }
                expect(JsMath.abs(sum - 1) < 0.0001, $"{key} sums to {sum}").toBe(true);
            }
            double scav = 0;
            foreach (var (kind, p) in Jobs.SCAVENGE_TABLE) scav += p;
            expect(JsMath.abs(scav - 1) < 0.0001, $"scavenge sums to {scav}").toBe(true);
            foreach (var w in Weapons.SPECS)
            {
                expect(Inventory.ITEM_WEIGHT.ContainsKey(w.kind), w.kind).toBe(true);
                expect(Items.NAMES.ContainsKey(w.kind), w.kind).toBe(true);
            }
            foreach (var a in Weapons.AMMO) expect(Items.NAMES.ContainsKey(a.kind), a.kind).toBe(true);
        }
    }

    /// <summary>shots</summary>
    public class D010_shots
    {
        [Fact(DisplayName = "arms > damage by range for each gun: full to the effective range, then falling to its fall-off, then nothing")]
        public void T010_fall_off()
        {
            // pistol 100, 5 tiles full, 14 max, 40% at max (its spread is wider than a body at 11+ tiles: the shot itself at 10, the curve for the end)
            expect(shotAt("pistol", 3, out _)).toBe(100);
            expect(shotAt("pistol", 5, out _)).toBe(100);
            expect(shotAt("pistol", 10, out _)).toBe(67);
            expect(Shooting.damagePercent(Weapons.specOfKind("pistol")!, tiles(14))).toBe(40);
            expect(shotAt("pistol", 15, out var o)).toBe(0);
            expect(o.hits).toBe(0);
            // revolver 150, 6 / 15, 50%
            expect(shotAt("revolver", 6, out _)).toBe(150);
            expect(shotAt("revolver", 12, out _)).toBe(100);
            expect(Shooting.damagePercent(Weapons.specOfKind("revolver")!, tiles(15))).toBe(50);
            // rifle 260, 14 / 30, 70%
            expect(shotAt("rifle", 14, out _)).toBe(260);
            expect(shotAt("rifle", 30, out _)).toBe(182);
            expect(shotAt("rifle", 31, out _)).toBe(0);
            // crossbow 150, 8 / 18, 60%
            expect(shotAt("crossbow", 8, out _)).toBe(150);
            expect(shotAt("crossbow", 18, out _)).toBe(90);
        }

        [Fact(DisplayName = "arms > a shotgun is eight pellets: it ruins what is close and little at range")]
        public void T011_shotgun()
        {
            expect(shotAt("shotgun", 1, out var o)).toBe(304);   // eight pellets of 38
            expect(o.hits).toBe(8);
            var near = shotAt("shotgun", 2, out _);
            expect(near).toBe(304);
            var far = shotAt("shotgun", 9, out var f);
            expect(f.hits).toBeLessThan(8);                      // a cone 0.8 of a tile either side at 9 tiles
            expect(far).toBeLessThan(60);
            expect(shotAt("shotgun", 10, out var beyond)).toBe(0);
            expect(beyond.hits).toBe(0);
        }

        [Fact(DisplayName = "arms > spread worsens moving, panicked and winded")]
        public void T012_spread()
        {
            var state = yard();
            var s = state.survivors[0];
            var pistol = Weapons.specOfKind("pistol")!;
            expect(Shooting.spreadOf(s, pistol, false)).toBe(0.035);
            expect(Shooting.spreadOf(s, pistol, true)).toBe(0.08);
            s.panic = 80;
            expect(Shooting.spreadOf(s, pistol, false)).toBe(0.09);
            s.panic = 0;
            s.endurance = 10;
            expect(Shooting.spreadOf(s, pistol, false)).toBe(0.06);
            s.panic = 80;
            expect(Shooting.spreadOf(s, pistol, true)).toBe(0.035 + 0.045 + 0.055 + 0.025);
        }

        [Fact(DisplayName = "arms > a rifle at 20 tiles hits every time still, and a small share of the time on the move or winded")]
        public void T013_hit_rate()
        {
            int hits(bool moving, double endurance)
            {
                var n = 0;
                for (var t = 1; t <= 200; t++)
                {
                    var state = yard();
                    var s = state.survivors[0];
                    s.endurance = endurance;
                    state.tick = t;
                    var gun = wield(state, "rifle", 5);
                    var z = dummy(state, tiles(20), 0);
                    if (Shooting.fire(state, s, gun, 1000, 0, moving).hits > 0) n++;
                }
                return n;
            }
            expect(hits(false, 100)).toBe(200);
            var moving = hits(true, 100);
            expect(moving).toBeGreaterThan(10);
            expect(moving).toBeLessThan(120);
            var winded = hits(false, 5);
            expect(winded).toBeGreaterThan(60);
            expect(winded).toBeLessThan(200);
        }

        [Fact(DisplayName = "arms > a miss hits what is behind")]
        public void T014_miss_hits_behind()
        {
            var state = yard();
            var s = state.survivors[0];
            var gun = wield(state, "rifle", 5);
            var near = dummy(state, tiles(5), 150);     // 150 off the line: past the body's 96
            var behind = dummy(state, tiles(12), 0);
            var o = Shooting.fire(state, s, gun, 1000, 0, false);
            expect(near.health).toBe(1000);
            expect(1000 - behind.health).toBe(260);
            expect(o.hits).toBe(1);
        }

        [Fact(DisplayName = "arms > the first body in line takes the shot; the one behind it is covered")]
        public void T015_first_in_line()
        {
            var state = yard();
            var s = state.survivors[0];
            var gun = wield(state, "rifle", 5);
            var front = dummy(state, tiles(5), 0);
            var behind = dummy(state, tiles(8), 0);
            Shooting.fire(state, s, gun, 1000, 0, false);
            expect(1000 - front.health).toBe(260);
            expect(behind.health).toBe(1000);
        }

        [Fact(DisplayName = "arms > walls, shut doors, barricades and edge walls stop a shot; open doors and windows let it through")]
        public void T016_walls()
        {
            double behindTile(Action<SimState> build)
            {
                var state = yard();
                var s = state.survivors[0];
                var gun = wield(state, "rifle", 5);
                var z = dummy(state, tiles(8), 0);
                build(state);
                Shooting.fire(state, s, gun, 1000, 0, false);
                return 1000 - z.health;
            }
            expect(behindTile((st) => { })).toBe(260);
            // a wall tile, a shut door, an open door, a barricade
            expect(behindTile((st) => Grids.setTile(st.grid, 4, 5, "wall"))).toBe(0);
            expect(behindTile((st) => Grids.setTile(st.grid, 4, 5, "door"))).toBe(0);
            expect(behindTile((st) => { Grids.setTile(st.grid, 4, 5, "door"); st.grid.open.Add(Grids.tileIndex(st.grid, 4, 5)); })).toBe(260);
            expect(behindTile((st) => Grids.setTile(st.grid, 4, 5, "barricade"))).toBe(0);
            // edge walls: a wall, a shut door, an open door, a window, a boarded one, a broken one
            expect(behindTile((st) => Grids.setEdge(st.grid, 4, 5, "w", Grids.EDGE_WALL))).toBe(0);
            expect(behindTile((st) => Grids.setEdge(st.grid, 4, 5, "w", Grids.EDGE_DOOR))).toBe(0);
            expect(behindTile((st) => { Grids.setEdge(st.grid, 4, 5, "w", Grids.EDGE_DOOR); st.grid.edgeOpen.Add(Grids.edgeIndex(st.grid, 4, 5, "w")); })).toBe(260);
            expect(behindTile((st) => Grids.setEdge(st.grid, 4, 5, "w", Grids.EDGE_WINDOW))).toBe(260);
            expect(behindTile((st) => { Grids.setEdge(st.grid, 4, 5, "w", Grids.EDGE_WINDOW); st.grid.edgeBoarded.Add(Grids.edgeIndex(st.grid, 4, 5, "w")); })).toBe(0);
            expect(behindTile((st) => Grids.setEdge(st.grid, 4, 5, "w", Grids.EDGE_WINDOW_BROKEN))).toBe(260);
            // a wall BEHIND the target does not matter
            expect(behindTile((st) => Grids.setTile(st.grid, 12, 5, "wall"))).toBe(260);
        }

        [Fact(DisplayName = "arms > a survivor of yours in the line holds your fire")]
        public void T017_friend_in_line()
        {
            var state = yard();
            var s = state.survivors[0];
            var gun = wield(state, "pistol", 5);
            var z = dummy(state, tiles(6), 0);
            var friend = Survivors.addSurvivor(state, 4, 5);
            var held = ControlMod.fire(state, s, 1000, 0);
            expect(held).toMatch(new JsRegExp("held fire", ""));
            expect(gun.wear).toBe(5);
            expect(z.health).toBe(1000);
            friend.at = new Point { x = Js.I(friend.at.x), y = Js.I(friend.at.y + 400) };
            friend.pos = Motion.tileOf(friend.at);
            expect(ControlMod.fire(state, s, 1000, 0)).not.toMatch(new JsRegExp("held fire", ""));
            expect(gun.wear).toBe(4);
        }

        [Fact(DisplayName = "arms > noise by gun: each weapon wakes the sleepers within its own radius and not a tile beyond")]
        public void T018_gun_noise()
        {
            var radius = new (string, int)[] { ("pistol", 22), ("revolver", 26), ("shotgun", 32), ("rifle", 36), ("crossbow", 4) };
            foreach (var (kind, r) in radius)
            {
                expect(Weapons.specOfKind(kind)!.noise, kind).toBe(r);
                var state = yard();
                var s = state.survivors[0];
                var gun = wield(state, kind, 3);
                state.dormant = [new() { id = "in", x = 1, y = 5 + r }, new() { id = "out", x = 1, y = 5 + r + 1 }];
                Shooting.fire(state, s, gun, 1000, 0, false);
                expect(state.dormant.map((d) => d.id), kind).toEqual(Js.Arr(["out"]));
            }
        }
    }

    /// <summary>reloading, ammunition and commands</summary>
    public class D020_reload_and_commands
    {
        static (SimState, Survivor, Item, Item) armed(string kind, double loaded, double rounds)
        {
            var state = yard();
            var s = state.survivors[0];
            var gun = wield(state, kind, loaded);
            var ammoKind = Weapons.specOfKind(kind)!.ammo!;
            var box = Items.spawnItem(state, ammoKind, 2, 5);
            box.wear = rounds;
            expect(Inventory.stow(state, s, box)).toBeNull();
            return (state, s, gun, box);
        }

        static void ticks(SimState state, int n)
        {
            for (var t = 0; t < n; t++)
            {
                Tick.tick(state);
                expect(Invariants.checkInvariants(state), DebugText.dumpText(state)).toEqual(Js.Arr([]));
            }
        }

        [Fact(DisplayName = "arms > reload time by kind: a pistol's magazine in one go, a revolver, shotgun and rifle a round at a time, a crossbow once")]
        public void T020_reload_timing()
        {
            // (kind, ticks for the first round / the lot, the lot)
            var cases = new (string, int, int, int)[] { ("pistol", 20, 20, 10), ("revolver", 8, 48, 6), ("shotgun", 10, 50, 5), ("rifle", 9, 45, 5), ("crossbow", 25, 25, 1) };
            foreach (var (kind, first, all, cap) in cases)
            {
                var (state, s, gun, box) = armed(kind, 0, 40);
                var r = Commands.applyCommand(state, new Command { type = "reload" });
                expect(r.ok, kind).toBe(true);
                ticks(state, first - 1);
                expect(gun.wear, kind + " before").toBe(0);
                ticks(state, 1);
                expect(gun.wear, kind + " first").toBe(Weapons.specOfKind(kind)!.perRound ? 1 : cap);
                ticks(state, all - first);
                expect(gun.wear, kind + " all").toBe(cap);
                expect(box.wear, kind + " box").toBe(40 - cap);
                expect(state.control!.busy, kind).toBeNull();
            }
        }

        [Fact(DisplayName = "arms > ammunition is used up, never negative; an empty gun and an empty bag say so")]
        public void T021_ammunition()
        {
            var (state, s, gun, box) = armed("revolver", 0, 2);
            Commands.applyCommand(state, new Command { type = "reload" });
            ticks(state, 8 * 3);
            expect(gun.wear).toBe(2);
            expect(state.items.some((i) => i.id == box.id)).toBe(false);   // the box is gone, with its rounds
            expect(s.pack.includes(box.id)).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "reload" }).ok).toBe(false);  // nothing in the bag
            for (var shot = 0; shot < 4; shot++)
            {
                state.tick += 20;
                var r = Commands.applyCommand(state, new Command { type = "fire", x = 1000, y = 0 });
                expect(r.ok, $"shot {shot}").toBe(shot < 2);
                expect(gun.wear).toBeGreaterThanOrEqual(0);
            }
            expect(gun.wear).toBe(0);
        }

        [Fact(DisplayName = "arms > firing sets the cooldown by weapon and stops a reload")]
        public void T022_fire_cooldown()
        {
            var (state, s, gun, box) = armed("pistol", 10, 12);
            var c = state.control!;
            state.tick = 100;
            expect(Commands.applyCommand(state, new Command { type = "fire", x = 1000, y = 0 }).ok).toBe(true);
            expect(c.nextSwing).toBe(109);
            expect(Commands.applyCommand(state, new Command { type = "fire", x = 1000, y = 0 }).ok).toBe(false);
            state.tick = 109;
            gun.wear = 5;
            Commands.applyCommand(state, new Command { type = "reload" });
            expect(c.busy?.what).toBe("reload");
            state.tick = 110;
            Commands.applyCommand(state, new Command { type = "fire", x = 1000, y = 0 });
            expect(c.busy).toBeNull();
            expect(gun.wear).toBe(4);
        }

        [Fact(DisplayName = "arms > ammunition of a kind stacks in the bag: the rounds add up in one box")]
        public void T025_stacking()
        {
            var state = yard();
            var s = state.survivors[0];
            var a = Items.spawnItem(state, "shells", 2, 5);
            var b = Items.spawnItem(state, "shells", 3, 5);
            var other = Items.spawnItem(state, "ammo", 4, 5);
            expect(Inventory.stow(state, s, a)).toBeNull();
            expect(Inventory.stow(state, s, b)).toBeNull();
            expect(Inventory.stow(state, s, other)).toBeNull();
            expect(s.pack).toEqual(Js.Arr([a.id, other.id]));
            expect(a.wear).toBe(12);
            expect(state.items.some((i) => i.id == b.id)).toBe(false);
        }

        [Fact(DisplayName = "arms > switching weapons takes the next one from the bag and puts the old one in its place")]
        public void T023_switch()
        {
            var state = yard();
            var s = state.survivors[0];
            var knife = wield(state, "knife");
            var pistol = Items.spawnItem(state, "pistol", 2, 5);
            var bat = Items.spawnItem(state, "bat", 2, 5);
            Inventory.stow(state, s, pistol);
            Inventory.stow(state, s, bat);
            expect(Commands.applyCommand(state, new Command { type = "switchWeapon" }).ok).toBe(true);
            expect(s.weapon).toBe(pistol.id);
            expect(s.pack).toEqual(Js.Arr([bat.id, knife.id]));
            expect(Commands.applyCommand(state, new Command { type = "switchWeapon" }).ok).toBe(true);
            expect(s.weapon).toBe(bat.id);
            expect(Commands.applyCommand(state, new Command { type = "switchWeapon", itemId = pistol.id }).ok).toBe(true);
            expect(s.weapon).toBe(pistol.id);
            expect(Commands.applyCommand(state, new Command { type = "switchWeapon", itemId = "nope" }).ok).toBe(false);
            s.pack.Clear();
            expect(Commands.applyCommand(state, new Command { type = "switchWeapon" }).ok).toBe(false);
        }

        [Fact(DisplayName = "arms > fire, reload and switching replay to the same state")]
        public void T024_replay()
        {
            var initial = yard();
            var s0 = initial.survivors[0];
            wield(initial, "pistol", 2);
            var box = Items.spawnItem(initial, "ammo", 2, 5);
            Inventory.stow(initial, s0, box);
            var bat = Items.spawnItem(initial, "bat", 2, 5);
            Inventory.stow(initial, s0, bat);
            Zombies.addZombie(initial, 6, 5, 400, "static");
            var live = States.cloneState(initial);
            Commands.applyCommand(live, new Command { type = "fire", x = 1000, y = 0 });
            Tick.step(live, 12);
            Commands.applyCommand(live, new Command { type = "fire", x = 1000, y = 30 });
            Tick.step(live, 10);
            Commands.applyCommand(live, new Command { type = "reload" });
            Tick.step(live, 30);
            Commands.applyCommand(live, new Command { type = "switchWeapon" });
            Tick.step(live, 5);
            Commands.applyCommand(live, new Command { type = "aim", x = 64, y = 0 });
            Commands.applyCommand(live, new Command { type = "swing", x = 2, y = 5 });
            Tick.step(live, 20);
            expect(live.commandLog.length).toBeGreaterThan(5);
            var replayed = Replay.replay(initial, live.commandLog, live.tick);
            expect(Js.Json_stringify(replayed)).toBe(Js.Json_stringify(live));
        }
    }

    /// <summary>old saves</summary>
    public class D030_old_saves
    {
        [Fact(DisplayName = "arms > a generic weapon, gun and box of rounds load as a crowbar, a pistol and 9mm; a save without readyTick loads")]
        public void T030_old_save()
        {
            var state = yard();
            var s = state.survivors[0];
            var oldWeapon = Items.spawnItem(state, "weapon", 3, 5);
            oldWeapon.wear = 3;
            var oldGun = Items.spawnItem(state, "gun", 4, 5);
            oldGun.wear = 4;
            var oldBox = Items.spawnItem(state, "ammo", 5, 5);
            oldBox.wear = 0;     // a box before boxes counted rounds
            s.weapon = oldGun.id;
            oldGun.pos = null;
            oldGun.carriedBy = s.id;
            var text = Savefile.toSaveFile(state);
            text = System.Text.RegularExpressions.Regex.Replace(text, "\"readyTick\":\\s*[^,}]+,?", "");
            var loaded = Savefile.loadFile(text);
            expect(loaded.ok).toBe(true);
            var l = loaded.state;
            var ls = l.survivors[0];
            expect(ls.readyTick).toBe(0);
            var w = l.items.find((i) => i.kind == "weapon");
            expect(Weapons.specOf(w)!.kind).toBe("crowbar");
            expect(w.wear).toBe(3);
            var g = l.items.find((i) => i.kind == "gun");
            expect(Weapons.specOf(g)!.kind).toBe("pistol");
            expect(Weapons.ammoKindOf(g)).toBe("ammo");
            expect(Items.nameOf(g)).toBe("pistol");
            var b = l.items.find((i) => i.kind == "ammo");
            expect(Weapons.rounds(b)).toBe(Weapons.AMMO_ROUNDS);
            // They work: the old gun (4 rounds) shoots, and a reload takes the old box's six.
            var z = Zombies.addZombie(l, 4, 5, 1000, "static");
            z.at = new Point { x = Js.I(ls.at.x + tiles(3)), y = Js.I(ls.at.y) };
            expect(Js.NN(ControlMod.fire(l, ls, 1000, 0))).toBe("shot it");
            expect(g.wear).toBe(3);
            expect(Weapons.loadRounds(l, g, b)).toBe(6);
            expect(g.wear).toBe(9);
            expect(l.items.some((i) => i.id == b.id)).toBe(false);
        }
    }

    /// <summary>armed guards</summary>
    public class D040_guards
    {
        [Fact(DisplayName = "arms > armed guards: a night against the dead, invariants hold, no one stays on a job over 600 ticks")]
        public void T040_armed_guards_night()
        {
            var state = ScenarioMod.loadScenario(new Scenario
            {
                seed = 11,
                map = ["..........................", ".s..s.s...................", "..........................", "..........................", "..........................", "..........................", ".........................."],
            });
            foreach (var s in state.survivors) s.priorities = Js.RecordFrom(JobTypes.NO_JOBS, __r0 => { __r0["guard"] = 3; });
            Items.spawnItem(state, "pistol", 5, 3);
            Items.spawnItem(state, "ammo", 6, 3);
            Items.spawnItem(state, "shotgun", 7, 3);
            Items.spawnItem(state, "shells", 8, 3);
            Items.spawnItem(state, "machete", 9, 3);
            state.zombiesTonight = 0;
            // By day they arm themselves; then the dead come in from the east.
            for (var t = 0; t < 300; t++) Tick.tick(state);
            expect(state.survivors.filter((s) => s.weapon != null).length).toBe(3);
            expect(state.survivors.some((s) => Weapons.heldGun(state, s) != null)).toBe(true);
            for (var k = 0; k < 3; k++) Zombies.addZombie(state, 24, 1 + k * 2, 200, "static");
            var onJob = new JsRecord<double>();
            var jobSince = new JsRecord<string>();
            for (var t = 0; t < 1500; t++)
            {
                Tick.tick(state);
                expect(Invariants.checkInvariants(state), DebugText.dumpText(state)).toEqual(Js.Arr([]));
                foreach (var s in state.survivors)
                {
                    var job = s.jobId ?? "";
                    if (jobSince[s.id] != job) { jobSince[s.id] = job; onJob[s.id] = t; }
                    expect(job == "" || (t - onJob[s.id]) <= 600, $"{s.name} on {job} since {onJob[s.id]}").toBe(true);
                }
            }
            expect(state.zombies.length, DebugText.dumpText(state) + string.Join("|", state.survivors.map((s) => s.name + ":" + s.note + ":" + s.weapon + ":" + s.health))).toBeLessThan(3);
        }
    }
}
