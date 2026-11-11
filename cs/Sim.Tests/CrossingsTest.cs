// The dead getting over what a body can't walk through (2026-11-11, Crossings.cs): any of them over a parked car,
// slowly and without biting on the way; a crawler under a pickup; a runner over a barricade. Round when round is
// short. A crossing is state on the zombie: it saves in the middle and plays on the same.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class CrossingsTest
{
    const int SUB = Motion.SUB;

    /// <summary>A yard 16 x 12: a wall across row 6 (`wall`: that row), you under control north of it at (7, 2).</summary>
    static (SimState state, Survivor s) yard(string wall = "#######..#######")
    {
        var rows = new List<string>();
        for (int y = 0; y < 12; y++) rows.Add(y == 6 ? wall : y == 2 ? ".......s........" : "................");
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = rows });
        var s = state.survivors[0];
        foreach (var k in Js.Iter(Js.ObjectKeys(s.priorities))) s.priorities[Js.Idx(k)] = 0;
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        return (state, s);
    }

    /// <summary>A car nose to the north in the two-tile gap at x = 7, 8, its middle line on the line between them:
    /// it fits between the wall's ends, and every tile of the gap is under it.</summary>
    static Vehicle plug(SimState state, string kind = "car")
    {
        var car = Vehicles.addVehicle(state, 7, 6, 24, kind: kind);
        Vehicles.unpark(state, car);
        car.at = new Point(8 * SUB, 6 * SUB + SUB / 2);
        car.was = car.at.Copy();
        Vehicles.park(state, car);
        return car;
    }

    /// <summary>One of the dead south of the wall at (7, 10), after you.</summary>
    static Zombie dead(SimState state, Survivor s, string variant = "walker", int x = 7, int y = 10)
    {
        var z = Zombies.addZombie(state, x, y, 200, "static");
        z.variant = variant;
        z.targetId = s.id;
        return z;
    }

    sealed class Seen
    {
        public bool reached;
        public int ticks, crossingTicks, firstLeg;
        public string what = "";
        public double lift;
    }

    /// <summary>Play until the zombie is in reach of you (or `max` ticks), checking on the way that it never bites
    /// from on top of anything, that off a crossing it stands where a body fits, and that the invariants hold.</summary>
    static Seen chase(SimState state, Survivor s, Zombie z, int max = 600)
    {
        var g = state.grid;
        var seen = new Seen();
        double health = s.health;
        for (int t = 0; t < max && !seen.reached; t++)
        {
            Tick.tick(state);
            seen.ticks = t + 1;
            if (!state.zombies.includes(z)) break;
            string where = $"tick {t}: the dead at {z.at.x},{z.at.y}\n{DebugText.dumpText(state)}";
            if (z.crossing != null)
            {
                if (seen.crossingTicks == 0) { seen.what = z.crossing.what; seen.firstLeg = z.crossing.of; }
                seen.crossingTicks++;
                seen.lift = Math.Max(seen.lift, Crossings.lift(state, z, 1));
                expect(z.windup, where).toBe(0);
                expect(s.health, where).toBe(health);
            }
            else
            {
                expect(Motion.fits(g, z.at, Zombies.moverOf(z)), where).toBe(true);
                expect(Vehicles.overlapsParked(g, z.at), where).toBe(false);
            }
            expect(Invariants.checkInvariants(state), where).toEqual(Js.Arr([]));
            health = s.health;
            seen.reached = Motion.inReach(s, z, Motion.REACH, g);
        }
        return seen;
    }

    [Fact(DisplayName = "crossings > one of the dead climbs over a car that blocks the only gap in a wall, and reaches you")]
    public void Over_a_car_in_the_gap()
    {
        var (state, s) = yard();
        var car = plug(state);
        var g = state.grid;
        // The gap is shut to every body on foot...
        foreach (var x in new[] { 7, 8 })
            foreach (var mover in new[] { Pathfinding.SURVIVOR, Pathfinding.FREE, Pathfinding.ZOMBIE, Pathfinding.CRAWLER, Pathfinding.RAIDER, Pathfinding.ANIMAL })
                expect(Pathfinding.isPassable(g, x, 6, mover), $"{mover} at {x},6").toBe(false);
        var south = new Point(7, 10);
        var north = new List<Point> { new(7, 2) };
        // ...and to the living's paths, but not to the dead's: reachability and A* agree, mover by mover.
        foreach (var mover in new[] { Pathfinding.SURVIVOR, Pathfinding.FREE, Pathfinding.ANIMAL })
        {
            expect(Pathfinding.canReach(g, state.walkabilityVersion, south, north, mover), mover).toBe(false);
            expect(Pathfinding.findPathToAny(g, south, north, mover) == null, mover).toBe(true);
        }
        foreach (var mover in new[] { Pathfinding.ZOMBIE, Pathfinding.CRAWLER, Pathfinding.RAIDER, Pathfinding.RUNNER })
        {
            expect(Pathfinding.canReach(g, state.walkabilityVersion, south, north, mover), mover).toBe(true);
            var path = Pathfinding.findPathToAny(g, south, north, mover);
            expect(path != null, mover).toBe(true);
            expect(path.Count((p) => Grids.isParked(g, p.x, p.y)), mover).toBe(3); // straight over its three rows
        }
        // (A breacher batters through the wall beside it: a wall is one step to its paths, a car twelve.)
        expect(Pathfinding.findPathToAny(g, south, north, Pathfinding.BREACHER).Any((p) => Grids.isParked(g, p.x, p.y))).toBe(false);
        var z = dead(state, s);
        var seen = chase(state, s, z);
        expect(seen.reached, DebugText.dumpText(state)).toBe(true);
        expect(seen.what).toBe(Crossings.CLIMB);
        expect(seen.firstLeg).toBe(Crossings.CLIMB_TICKS);
        // Onto it, along two more of its tiles, off it: four legs, eight seconds; a walk of eight tiles is under five.
        expect(seen.crossingTicks).toBeGreaterThanOrEqual(4 * Crossings.CLIMB_TICKS - 1);
        expect(seen.lift).toBeGreaterThan(0.7); // drawn up on its bonnet and roof
        expect(z.crossing).toBeNull();
        expect(z.pos.y).toBeLessThan(6);
    }

    [Fact(DisplayName = "crossings > round a car when round is short: over it only when that is much the shorter way")]
    public void Round_when_short()
    {
        // The gap is three tiles: the car leaves one open beside it, and the dead walk through that.
        var (state, s) = yard("#######...######");
        plug(state);
        var z = dead(state, s);
        var seen = chase(state, s, z);
        expect(seen.reached, DebugText.dumpText(state)).toBe(true);
        expect(seen.crossingTicks).toBe(0);

        // In the open, a car between you: round its end is nine steps, over it three. Round.
        var f = Ported.VehiclesTest.field();
        Staging.place(f.s, 3, 1);
        var open = dead(f.state, f.s, "walker", 3, 5);
        var path = Pathfinding.findPathToAny(f.state.grid, open.pos, [new Point(3, 2)], Crossings.planner(open));
        expect(path.Count).toBe(9);
        expect(path.Any((p) => Grids.isParked(f.state.grid, p.x, p.y))).toBe(false);
        expect(chase(f.state, f.s, open).crossingTicks).toBe(0);

        // The same car with a fence either side of it for twenty tiles: now over is the way.
        var long_ = Ported.VehiclesTest.field();
        for (int x = 6; x < 30; x++) Grids.setTile(long_.state.grid, x, 3, Grids.WALL);
        Grids.setTile(long_.state.grid, 0, 3, Grids.WALL);
        long_.state.walkabilityVersion++;
        Staging.place(long_.s, 3, 1);
        var over = dead(long_.state, long_.s, "walker", 3, 5);
        var seenOver = chase(long_.state, long_.s, over);
        expect(seenOver.reached).toBe(true);
        expect(seenOver.what).toBe(Crossings.CLIMB);
        // Over its width: onto it and off it.
        expect(seenOver.crossingTicks).toBeLessThanOrEqual(2 * Crossings.CLIMB_TICKS);
    }

    [Fact(DisplayName = "crossings > a crawler goes under a pickup, and hauls itself over a sedan; the other kinds climb at their own pace")]
    public void Kinds_and_cars()
    {
        // Under the pickup: quick, on the ground, and no bite from under it.
        var (state, s) = yard();
        plug(state, "truck");
        var crawler = dead(state, s, "crawler");
        var under = chase(state, s, crawler);
        expect(under.reached, DebugText.dumpText(state)).toBe(true);
        expect(under.what).toBe(Crossings.UNDER);
        expect(under.firstLeg).toBe(Crossings.UNDER_TICKS);
        expect(under.lift).toBe(0);

        // A sedan sits too low: over it, the slowest of all.
        var (state2, s2) = yard();
        plug(state2);
        var hauled = chase(state2, s2, dead(state2, s2, "crawler"));
        expect(hauled.reached).toBe(true);
        expect(hauled.what).toBe(Crossings.CLIMB);
        expect(hauled.firstLeg).toBe(Crossings.HAUL_TICKS);

        // Everyone else climbs either: a runner quicker than a walker, a brute slower; a raider as a walker.
        foreach (var (variant, legs) in new[] { ("runner", 14), ("screamer", 20), ("brute", 27) })
        {
            var (st, sv) = yard();
            plug(st, variant == "brute" ? "truck" : "car");
            var seen = chase(st, sv, dead(st, sv, variant));
            expect(seen.reached, variant).toBe(true);
            expect(seen.what, variant).toBe(Crossings.CLIMB);
            expect(seen.firstLeg, variant).toBe(legs);
        }
        // (Raiders leave at dawn, and this is day: asked, not played.)
        var (st3, sv3) = yard();
        plug(st3);
        var raider = dead(st3, sv3);
        raider.kind = "raider";
        expect(Crossings.planner(raider)).toBe(Pathfinding.RAIDER);
        expect(Crossings.way(st3, raider, new Point(7, 8), new Point(7, 7))).toBe(Crossings.CLIMB);
        expect(Crossings.legTicks(raider, Crossings.CLIMB)).toBe(Crossings.CLIMB_TICKS);
    }

    [Fact(DisplayName = "crossings > a runner vaults a barricade (on an edge or a tile); walkers and brutes batter it, a crawler goes under as before")]
    public void Barricades()
    {
        foreach (bool edge in new[] { true, false })
        {
            SimState make(out Survivor s)
            {
                var (state, sv) = yard(edge ? "#######.########" : "#######=########");
                if (edge)
                {
                    Grids.setEdge(state.grid, 7, 6, "n", Grids.EDGE_BARRICADE);
                    state.walkabilityVersion++;
                }
                s = sv;
                return state;
            }
            double damage(SimState state) => edge ? Breach.edgeDamage(state.grid, Grids.edgeIndex(state.grid, 7, 6, "n")) : Breach.structureDamage(state, 7, 6);
            bool stands(SimState state) => edge ? Grids.edgeKindBetween(state.grid, 7, 6, 7, 5) == Grids.EDGE_BARRICADE : Grids.getTile(state.grid, 7, 6) == Grids.BARRICADE;
            string which = edge ? "edge" : "tile";

            // The runner: over it in a second or two, and the barricade is as it was.
            var state = make(out var s);
            var runner = dead(state, s, "runner");
            expect(Pathfinding.canReach(state.grid, state.walkabilityVersion, runner.pos, [s.pos], Zombies.moverOf(runner)), which).toBe(false);
            expect(Pathfinding.canReach(state.grid, state.walkabilityVersion, runner.pos, [s.pos], Crossings.planner(runner)), which).toBe(true);
            var seen = chase(state, s, runner);
            expect(seen.reached, which).toBe(true);
            expect(seen.what, which).toBe(Crossings.VAULT);
            expect(seen.crossingTicks, which).toBeLessThanOrEqual(2 * Crossings.VAULT_TICKS);
            expect(damage(state), which).toBe(0);
            expect(stands(state), which).toBe(true);
            if (edge) expect(seen.lift, which).toBeGreaterThan(0.4);

            // A walker and a brute: no way over, so they batter it down (the brute three times as fast).
            double walkerTicks = 0;
            foreach (var variant in new[] { "walker", "brute" })
            {
                state = make(out s);
                var z = dead(state, s, variant);
                seen = chase(state, s, z, 900);
                expect(seen.reached, $"{which} {variant}").toBe(true);
                expect(seen.crossingTicks, $"{which} {variant}").toBe(0);
                expect(stands(state), $"{which} {variant}").toBe(false);
                if (variant == "walker") walkerTicks = seen.ticks;
                else expect(seen.ticks).toBeLessThan(walkerTicks);
            }

            // A crawler: under it, a plain step as it always was.
            state = make(out s);
            seen = chase(state, s, dead(state, s, "crawler"));
            expect(seen.reached, which).toBe(true);
            expect(seen.crossingTicks, which).toBe(0);
            expect(stands(state), which).toBe(true);
        }
    }

    [Fact(DisplayName = "crossings > a state saved in the middle of a crossing reads back and plays on the same")]
    public void Saved_mid_crossing()
    {
        var (state, s) = yard();
        plug(state);
        var z = dead(state, s);
        dead(state, s, "runner", 8, 10);
        dead(state, s, "crawler", 6, 10);
        // To the middle of its second leg.
        for (int t = 0; t < 400 && !(z.crossing != null && Grids.isParked(state.grid, Motion.tileOf(z.crossing.from).x, Motion.tileOf(z.crossing.from).y) && z.crossing.ticks == 7); t++) Tick.tick(state);
        expect(z.crossing?.ticks).toBe(7);
        var copy = Js.DeepClone(state);
        var zc = copy.zombies.find((q) => q.id == z.id);
        expect(Js.Json_stringify(zc.crossing)).toBe(Js.Json_stringify(z.crossing));
        expect(Js.Json_stringify(copy)).toBe(Js.Json_stringify(state));
        for (int t = 0; t < 150; t++)
        {
            Tick.tick(state);
            Tick.tick(copy);
            if (t % 10 == 0) expect(Js.Json_stringify(copy.zombies), $"tick {t}").toBe(Js.Json_stringify(state.zombies));
        }
        expect(Js.Json_stringify(copy)).toBe(Js.Json_stringify(state));
        expect(z.crossing).toBeNull();
        expect(z.pos.y).toBeLessThan(6);
    }

    [Fact(DisplayName = "crossings > a horde comes over the car on its flow field, and nobody is left stuck at it or on it")]
    public void A_horde_and_nobody_stuck()
    {
        var (state, s) = yard();
        plug(state);
        var g = state.grid;
        var wave = new List<Zombie>();
        string[] kinds = ["walker", "runner", "brute", "crawler", "screamer", "walker"];
        for (int i = 0; i < kinds.Length; i++)
        {
            var z = Zombies.addZombie(state, 4 + i, 10, 2000, "static");
            z.variant = kinds[i];
            wave.Add(z);
        }
        var horde = Hordes.formHorde(state, wave, null, true);
        expect(horde != null).toBe(true);
        // You can't be killed for the length of the test: the question is who gets to you.
        var across = new HashSet<string>();
        var lastMoved = wave.ToDictionary((z) => z.id, (z) => 0);
        var lastAt = wave.ToDictionary((z) => z.id, (z) => z.at.Copy());
        for (int t = 0; t < 900 && across.Count < wave.Count; t++)
        {
            s.health = Health.HEALTH_MAX;
            Tick.tick(state);
            expect(Invariants.checkInvariants(state), $"tick {t}\n{DebugText.dumpText(state)}").toEqual(Js.Arr([]));
            foreach (var z in wave)
            {
                if (z.pos.y < 6 && z.crossing == null) across.Add(z.id);
                if (z.at.x != lastAt[z.id].x || z.at.y != lastAt[z.id].y) { lastMoved[z.id] = t; lastAt[z.id] = z.at.Copy(); }
                // South of the wall or on the car, none stands still for long (a crawler's leg over the sedan is 40 ticks).
                if (!across.Contains(z.id)) expect(t - lastMoved[z.id], $"tick {t}: {z.id} ({z.variant}) at {z.pos}\n{DebugText.dumpText(state)}").toBeLessThan(100);
                if (z.crossing == null) expect(Motion.fits(g, z.at, Zombies.moverOf(z)), $"tick {t}: {z.id}").toBe(true);
            }
        }
        expect(across.Count, DebugText.dumpText(state)).toBe(wave.Count);
    }

    [Fact(DisplayName = "crossings > a car driven off or parked under one of the dead ends its crossing on ground a body fits")]
    public void The_car_moves()
    {
        // Driven off from under it: it comes down where it was going and walks on.
        var (state, s) = yard();
        var car = plug(state);
        var z = dead(state, s);
        for (int t = 0; t < 400 && !(z.crossing != null && Grids.isParked(state.grid, z.pos.x, z.pos.y)); t++) Tick.tick(state);
        expect(z.crossing != null).toBe(true);
        Vehicles.remove(state, car);
        var seen = chase(state, s, z);
        expect(seen.reached).toBe(true);
        expect(seen.crossingTicks).toBeLessThanOrEqual(Crossings.CLIMB_TICKS);

        // Another car put down where it is climbing: moved clear (VehicleParking's evict), and the crossing is off.
        (state, s) = yard();
        plug(state);
        z = dead(state, s);
        for (int t = 0; t < 400 && !(z.crossing != null && Grids.isParked(state.grid, z.pos.x, z.pos.y)); t++) Tick.tick(state);
        var was = z.crossing;
        Vehicles.addVehicle(state, z.pos.x, z.pos.y, 0);
        expect(Vehicles.overlapsParked(state.grid, z.at)).toBe(false);
        Tick.tick(state);
        // The old leg is gone; it is on the ground (or setting off from it again, a new leg from where it was put).
        expect(z.crossing != was).toBe(true);
        expect(Motion.fits(state.grid, z.crossing?.from ?? z.at, Zombies.moverOf(z))).toBe(true);
        expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        expect(chase(state, s, z).reached).toBe(true);
    }

    [Fact(DisplayName = "crossings > in a generated town, what the dead can reach is what their paths find, mover by mover")]
    public void Reach_and_paths_agree()
    {
        var state = Worldgen.createWorld(12, 128);
        for (int y = 0; y < 128; y += Worldgen.CHUNK)
            for (int x = 0; x < 128; x += Worldgen.CHUNK) Worldgen.ensureGeneratedAround(state, new Point(x, y));
        var g = state.grid;
        var cars = Vehicles.vehiclesOf(state).Where(Vehicles.isParked).ToList();
        expect(cars.Count).toBeGreaterThan(0);
        // Barricades too, on edges and as tiles, near the cars (hashed places).
        for (int k = 0; k < 60; k++)
        {
            var c = cars[k % cars.Count];
            int x = c.pos.x + (int)Math.Floor(Hash.hashRoll(k, 1) * 15) - 7, y = c.pos.y + (int)Math.Floor(Hash.hashRoll(k, 2) * 15) - 7;
            if (!Grids.inBounds(g, x, y) || Grids.getTile(g, x, y) != Grids.FLOOR || Grids.isParked(g, x, y)) continue;
            if (k % 3 == 0) Grids.setTile(g, x, y, Grids.BARRICADE);
            else Grids.setEdge(g, x, y, k % 2 == 0 ? "n" : "w", Grids.EDGE_BARRICADE);
        }
        state.walkabilityVersion++;
        int asked = 0, reachable = 0, over = 0;
        foreach (var mover in new[] { Pathfinding.ZOMBIE, Pathfinding.CRAWLER, Pathfinding.RAIDER, Pathfinding.RUNNER, Pathfinding.SURVIVOR })
        {
            for (int k = 0; k < 80; k++)
            {
                // From beside a car to somewhere within twenty tiles of it.
                var c = cars[k % cars.Count];
                var a = new Point(c.pos.x + (int)Math.Floor(Hash.hashRoll(k, 3) * 9) - 4, c.pos.y + (int)Math.Floor(Hash.hashRoll(k, 4) * 9) - 4);
                var b = new Point(a.x + (int)Math.Floor(Hash.hashRoll(k, 5) * 41) - 20, a.y + (int)Math.Floor(Hash.hashRoll(k, 6) * 41) - 20);
                if (!Grids.inBounds(g, a.x, a.y) || !Grids.inBounds(g, b.x, b.y) || !Pathfinding.isPassable(g, a.x, a.y, mover)) continue;
                bool reach = Pathfinding.canReach(g, state.walkabilityVersion, a, [b], mover);
                var path = Pathfinding.findPathToAny(g, a, [b], mover);
                expect(path != null, $"{mover} {a} -> {b}").toBe(reach);
                asked++;
                if (reach) reachable++;
                if (path != null && path.Any((p) => !Pathfinding.isPassable(g, p.x, p.y, mover))) over++;
            }
        }
        expect(asked).toBeGreaterThan(200);
        expect(reachable).toBeGreaterThan(50);
        expect(over).toBeGreaterThan(0);
    }

    [Fact(DisplayName = "crossings > the staged scene (--stage=climb) has each of the dead part-way over, and they all get across from there")]
    public void Staged()
    {
        var state = Worldgen.createWorld(42, 256);
        var you = state.survivors[0];
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = you.id });
        Staging.place(you, 136, 89); // open ground west of the village (150,90 until its street was widened over it)
        Staging.climb(state, you);
        var mine = state.zombies.Where((z) => z.nextThinkTick > 1000).ToList();
        expect(mine.Count).toBe(7);
        string all = string.Join("; ", mine.Select((z) => $"{z.variant} row {z.pos.y - 89}: {z.crossing?.what} {z.crossing?.ticks}/{z.crossing?.of} lift {Crossings.lift(state, z, 1):0.00} stance {PoseIntents.ofZombie(state, z).stance}"));
        foreach (var z in mine) expect(z.crossing != null, all).toBe(true);
        var by = mine.ToDictionary((z) => $"{z.variant}{z.pos.y - 89}", (z) => z);
        expect(Crossings.lift(state, by["walker-2"], 1), all).toBe(0.78);   // the sedan's bonnet
        expect(Crossings.lift(state, by["screamer-4"], 1), all).toBe(1.28); // its roof
        expect(Crossings.lift(state, by["brute3"], 1), all).toBe(1.75);     // the pickup's cab
        expect(Crossings.lift(state, by["walker1"], 1), all).toBe(1.0);     // its bed
        expect(by["crawler5"].crossing.what, all).toBe(Crossings.UNDER);
        expect(Crossings.lift(state, by["crawler5"], 1), all).toBe(0);
        expect(by["runner0"].crossing.what, all).toBe(Crossings.VAULT);
        expect(Crossings.lift(state, by["runner0"], 1), all).toBeGreaterThan(0.4);
        expect(PoseIntents.ofZombie(state, by["screamer-4"]).stance).toBe("crawl");
        expect(PoseIntents.ofZombie(state, by["runner0"]).stance).toBe("stand");
        expect(PoseIntents.ofZombie(state, by["runner0"]).action).toBe(Crossings.VAULT);
        // Played on: every one of them comes down on the near side.
        for (int t = 0; t < 60; t++) Tick.tick(state);
        foreach (var z in mine.Where(state.zombies.Contains))
        {
            expect(z.crossing, z.variant).toBeNull();
            expect(z.pos.x, z.variant).toBeLessThan(154);
        }
    }

    [Fact(DisplayName = "crossings > a blow holds a climber where it is: no shove off the car, and the leg goes on after")]
    public void A_blow_on_the_car()
    {
        var (state, s) = yard();
        plug(state);
        var z = dead(state, s);
        for (int t = 0; t < 400 && !(z.crossing != null && Grids.isParked(state.grid, z.pos.x, z.pos.y)); t++) Tick.tick(state);
        var at = z.at.Copy();
        int left = z.crossing.ticks;
        Zombies.knockBack(state, z, s.at);
        expect(z.at.x).toBe(at.x);
        expect(z.at.y).toBe(at.y);
        for (int t = 0; t < Zombies.STAGGER_TICKS - 1; t++) Tick.tick(state);
        expect(z.crossing.ticks).toBe(left);
        expect(z.at.y).toBe(at.y);
        expect(chase(state, s, z).reached).toBe(true);
    }
}
