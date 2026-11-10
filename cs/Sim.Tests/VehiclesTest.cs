// Ported from tests/vehicles.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class VehiclesTest
{

    public static readonly string ROW = ".".repeat(40);

    /// <summary>You at (2, 3) in an open field, under control, with a car facing east at (3, 3).</summary>
    public static FieldResultShape37 field(List<string>? map = null)
    {
        map ??= Js.ArrayFrom(7, (int _i) => ROW);
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = map.map((r, y) => (y == 3) ? ((r.slice(0, 2) + "s") + r.slice(3)) : r) });
        var s = Js.NN(Js.Get(state.survivors, 0));
        foreach (var k in Js.Iter(Js.ObjectKeys(s.priorities)))
        {
            s.priorities[Js.Idx(k)] = 0;
        }
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        var car = Vehicles.addVehicle(state, 3, 3);
        return new FieldResultShape37 { state = state, s = s, car = car };
    }

    public static void run(SimState state, double n)
    {
        for (var i = 0; i < n; i++)
        {
            Tick.tick(state);
        }
    }

    /// <summary>Use the car at (3, 3) and wait out getting in (vehicleActions.ts). Returns whether "use" was taken.</summary>
    public static bool getIn(SimState state)
    {
        var ok = Commands.applyCommand(state, new Command { type = "use", x = Js.I(3), y = Js.I(3) }).ok;
        run(state, Vehicles.GET_IN_TICKS);
        return ok;
    }

    /// <summary>vehicles</summary>
    public class D001_vehicles
    {

        [Fact(DisplayName = "vehicles > you get in, put your foot down, and go; it burns fuel and it is loud")]
        public void T002_you_get_in_put_your_foot_down_and_go_it_burns_fuel_and_it_is()
        {
            var __d0 = field();
            var state = __d0.state;
            var s = __d0.s;
            var car = __d0.car;
            state.dormant = [new() { id = "d1", x = Js.I(20), y = Js.I(6) }];
            expect(getIn(state)).toBe(true);
            expect(Vehicles.vehicleOf(state, s)).toBe(car);
            Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = -1 });
            run(state, 30);
            expect(car.pos.x).toBeGreaterThan(10);
            expect(s.pos).toEqual(car.pos);
            expect(car.fuel).toBeLessThan(Vehicles.FUEL_MAX);
            expect(state.dormant).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "vehicles > turns only while moving, and gets you out beside it")]
        public void T003_turns_only_while_moving_and_gets_you_out_beside_it()
        {
            var __d1 = field();
            var state = __d1.state;
            var s = __d1.s;
            var car = __d1.car;
            getIn(state);
            Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
            run(state, 10);
            expect(car.heading).toBe(0);
            Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = -1 });
            run(state, 10);
            expect(car.heading).toBeGreaterThan(0);
            Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 1 });
            run(state, 40);
            expect(car.speed).toBeLessThanOrEqual(0);
            expect(Commands.applyCommand(state, new Command { type = "use", x = Js.I(car.pos.x), y = Js.I(car.pos.y) }).ok).toBe(true);
            expect(Vehicles.vehicleOf(state, s)).toBe(car);  // getting out takes a moment (vehicleActions.ts)
            run(state, Vehicles.GET_OUT_TICKS);
            expect(Vehicles.vehicleOf(state, s)).toBeUndefined();
            expect(JsMath.max(JsMath.abs(s.pos.x - car.pos.x), JsMath.abs(s.pos.y - car.pos.y))).toBe(1);
        }

        [Fact(DisplayName = "vehicles > a wall at speed is a crash; an empty tank goes nowhere")]
        public void T004_a_wall_at_speed_is_a_crash_an_empty_tank_goes_nowhere()
        {
            var map = Js.ArrayFrom(7, (int _i) => (ROW.slice(0, 25) + "#") + ROW.slice(26));
            var __d2 = field(map);
            var state = __d2.state;
            var car = __d2.car;
            getIn(state);
            Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = -1 });
            double fastest = 0;
            for (var i = 0; i < 120; i++)
            {
                Tick.tick(state);
                fastest = JsMath.max(fastest, car.speed);
            }
            expect(fastest).toBeGreaterThan(Vehicles.CRASH_SPEED);
            expect(car.pos.x).toBeLessThan(25);
            expect(car.health).toBeLessThan(Vehicles.VEHICLE_HEALTH);
            expect(state.events.some((e) => e.message.includes("crashed"))).toBe(true);
            var dry = field();
            dry.car.fuel = 0;
            getIn(dry.state);
            Commands.applyCommand(dry.state, new Command { type = "steer", dx = 0, dy = -1 });
            run(dry.state, 20);
            expect(dry.car.pos).toEqual(Js.Obj(("x", (object?)(3)), ("y", (object?)(3))));
        }

        [Fact(DisplayName = "vehicles > runs the dead down, and they can only batter the car to get at you")]
        public void T005_runs_the_dead_down_and_they_can_only_batter_the_car_to_get_a()
        {
            var __d3 = field();
            var state = __d3.state;
            var s = __d3.s;
            var car = __d3.car;
            var z = Zombies.addZombie(state, 16, 3, 200, "static");
            // Already after you, so it comes straight down the lane at the car (left to wander, it can stand a tile to the side as the car goes by).
            z.targetId = s.id;
            getIn(state);
            Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = -1 });
            run(state, 40);
            expect(state.zombies.includes(z)).toBe(false);
            Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 1 });
            run(state, 40);
            Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 0 });
            run(state, 10);
            var biter = Zombies.addZombie(state, car.pos.x, car.pos.y + 1, 200, "static");
            biter.at = Motion.centre(biter.pos);
            var dent = car.health;
            run(state, 60);
            expect(s.health).toBe(Health.HEALTH_MAX);
            expect(car.health).toBeLessThan(dent);
        }

        [Fact(DisplayName = "vehicles > a fuel can from your bag fills the tank")]
        public void T006_a_fuel_can_from_your_bag_fills_the_tank()
        {
            var __d4 = field();
            var state = __d4.state;
            var s = __d4.s;
            var car = __d4.car;
            car.fuel = 0;
            Inventory.stow(state, s, Items.spawnItem(state, "fuel", 2, 3));
            expect(Commands.applyCommand(state, new Command { type = "use", x = Js.I(3), y = Js.I(3) }).ok).toBe(true);
            expect(car.fuel).toBe(Vehicles.FUEL_PER_CAN);
            expect(s.pack).toEqual(Js.Arr([]));
            expect(Vehicles.vehicleOf(state, s)).toBeUndefined();
        }

        [Fact(DisplayName = "vehicles > letting go of control puts you out of the car")]
        public void T007_letting_go_of_control_puts_you_out_of_the_car()
        {
            var __d5 = field();
            var state = __d5.state;
            var s = __d5.s;
            getIn(state);
            Commands.applyCommand(state, new Command { type = "takeControl", survivorId = null });
            Tick.tick(state);
            expect(Vehicles.vehicleOf(state, s)).toBeUndefined();
        }

        [Fact(DisplayName = "vehicles > no door, window or barricade lets a car through")]
        public void T008_no_door_window_or_barricade_lets_a_car_through()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["..."], edges = [new() { x = Js.I(1), y = Js.I(0), side = "w", kind = "door" }] });
            state.grid.edgeOpen.push(((double)(1) * (2)) + 1);
            expect(Pathfinding.crossable(state.grid, 0, 0, 1, 0, "vehicle")).toBe(false);
            expect(Pathfinding.crossable(state.grid, 1, 0, 2, 0, "vehicle")).toBe(true);
        }

        [Fact(DisplayName = "vehicles > towns have cars parked in the street")]
        public void T009_towns_have_cars_parked_in_the_street()
        {
            var state = Worldgen.createWorld(5, 256);
            for (var y = 0; y < 256; y += Worldgen.CHUNK)
            {
                for (var x = 0; x < 256; x += Worldgen.CHUNK)
                {
                    Worldgen.ensureGeneratedAround(state, new Point { x = Js.I(x), y = Js.I(y) });
                }
            }
            var cars = state.vehicles ?? [];
            expect(cars.length).toBeGreaterThan(4);
            var onRoad = cars.filter((c) => Js.Get(Grids.GROUNDS, Js.NN(Grids.groundAt(state.grid, ((double)(c.pos.y) * (256)) + c.pos.x))) == "road").length;
            expect(onRoad).toBeGreaterThan(((double)(cars.length) / (2)));
            foreach (var c in Js.Iter(cars))
            {
                expect(c.fuel).toBeLessThan(Vehicles.FUEL_MAX);
            }
        }
    }

    /// <summary>vehicles: roads, trees and the boot</summary>
    public class D010_vehicles_roads_trees_and_the_boot
    {

        [Fact(DisplayName = "vehicles: roads, trees and the boot > goes slower off the road, and a tree stops it like a wall")]
        public void T011_goes_slower_off_the_road_and_a_tree_stops_it_like_a_wall()
        {
            double top(bool road)
            {
                var __d6 = field();
                var state_1 = __d6.state;
                var car_2 = __d6.car;
                if (road)
                {
                    Grids.fillGround(state_1.grid, Grids.GROUNDS.indexOf("road"));
                }
                getIn(state_1);
                Commands.applyCommand(state_1, new Command { type = "steer", dx = 0, dy = -1 });
                double fastest = 0;
                for (var i = 0; i < 25; i++)
                {
                    Tick.tick(state_1);
                    fastest = JsMath.max(fastest, car_2.speed);
                }
                return fastest;
            }
            expect(top(false)).toBe(Vehicles.OFFROAD_MAX);
            expect(top(true)).toBe(Vehicles.MAX_SPEED);
            var __d7 = field();
            var state = __d7.state;
            var car = __d7.car;
            FurnitureMod.addFurniture(state, "tree", 12, 3);
            getIn(state);
            Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = -1 });
            run(state, 80);
            expect(car.pos.x).toBeLessThan(12);
        }

        [Fact(DisplayName = "vehicles: roads, trees and the boot > things go in the boot from your bag, and come out again")]
        public void T012_things_go_in_the_boot_from_your_bag_and_come_out_again()
        {
            var __d8 = field();
            var state = __d8.state;
            var s = __d8.s;
            var car = __d8.car;
            var can = Items.spawnItem(state, "wood", 2, 3);
            Inventory.stow(state, s, can);
            expect(Commands.applyCommand(state, new Command { type = "stash", itemId = can.id, containerId = car.id }).ok).toBe(true);
            expect(s.pack).toEqual(Js.Arr([]));
            expect(Inventory.contents(state, new Furniture { id = car.id }).map((i) => i.id)).toEqual(Js.Arr([can.id]));
            expect(Commands.applyCommand(state, new Command { type = "takeAll", containerId = car.id }).ok).toBe(true);
            expect(s.pack).toEqual(Js.Arr([can.id]));
            for (var i = 0; ((double)(i) * (Inventory.ITEM_WEIGHT["wood"])) <= Vehicles.BOOT_MAX; i++)
            {
                var w = Items.spawnItem(state, "wood", 2, 3);
                w.pos = null;
                w.inside = car.id;
            }
            expect(Commands.applyCommand(state, new Command { type = "stash", itemId = can.id, containerId = car.id }).ok).toBe(false);
        }

        [Fact(DisplayName = "vehicles: roads, trees and the boot > the expedition truck is a vehicle, and a trip takes fuel from its tank")]
        public void T013_the_expedition_truck_is_a_vehicle_and_a_trip_takes_fuel_from()
        {
            var state = Worldgen.createWorld(2, 128);
            var t = Js.NN((state.vehicles ?? []).find((v) => v.kind == "truck"));
            expect(t).toBeDefined();
            Grids.fillExplored(state.grid, 1);
            expect(Outposts.truck(state)).toEqual(t.pos);
            var fuel = t.fuel;
            expect(Outposts.takeTruck(state)).toBe(true);
            expect(t.fuel).toBe(fuel - Outposts.TRIP_FUEL);
        }
    }
}
