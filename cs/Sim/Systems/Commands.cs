// Ported from src/sim/commands.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// The only way to change the sim from outside (architecture rule 5).
// Every accepted command that changed state is appended to state.commandLog
// for replays. No-ops (painting a wall over a wall while dragging) are not
// logged, since replaying them would change nothing.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Commands
{

    /// <summary>Plans the player can only make on ground someone has seen.</summary>
    public static readonly JsSet<string> DESIGNATIONS = new JsSet<string>(["ignite", "designateBuild", "designateRemove", "designateRoom", "designateScavenge", "designateFarm"]);

    public static CommandResult applyCommand(SimState state, Command command)
    {
        var result = execute(state, command);
        if (Js.T(result.ok && Js.T(result.changed)))
        {
            state.commandLog.push(new() { tick = state.tick, command = Js.Clone(command) });
        }
        return result;
    }

    public static CommandResult execute(SimState state, Command command)
    {
        if (command.type == "takeControl")
        {
            return takeControl(state, command.survivorId);
        }
        if (command.type == "work")
        {
            var s = ControlMod.controlled(state);
            if (!Js.T(s))
            {
                return reject("nobody is under control");
            }
            if (!JsNumber.isInteger(command.x) || !JsNumber.isInteger(command.y))
            {
                return reject("not a tile");
            }
            var done = ControlMod.work(state, s, command);
            s.note = done;
            return Js.T(Js.NN(state.control).busy) ? changed() : reject(done);
        }
        if (((((command.type == "take") || (command.type == "takeAll")) || (command.type == "drop")) || (command.type == "useItem")) || (command.type == "stash") || (command.type == "stashAll") || (command.type == "unequip"))
        {
            var s = ControlMod.controlled(state);
            if (!Js.T(s))
            {
                return reject("nobody is under control");
            }
            var why = bagCommand(state, s, command);
            return (why == null) ? changed() : reject(why);
        }
        if ((command.type == "addMapNote") || (command.type == "removeMapNote"))
        {
            var why = (command.type == "addMapNote") ? Mapnotes.addMapNote(state, Js.NN(command.x), Js.NN(command.y), Js.Str(command.text)) : Mapnotes.removeMapNote(state, command.id);
            return (why == null) ? changed() : reject(why);
        }
        if (command.type == "sneak")
        {
            if (!Js.T(ControlMod.controlled(state)))
            {
                return reject("nobody is under control");
            }
            if ((Js.NN(state.control).sneaking == true) == command.on)
            {
                return unchanged();
            }
            Js.NN(state.control).sneaking = command.on;
            return changed();
        }
        if ((((command.type == "steer") || (command.type == "swing")) || (command.type == "use")) || (command.type == "aim") || (command.type == "boot") || (command.type == "hood"))
        {
            var s = ControlMod.controlled(state);
            if (!Js.T(s))
            {
                return reject("nobody is under control");
            }
            if (command.type is "boot" or "hood")
            {
                // Open or shut the boot or the bonnet of the car beside you (vehicleActions.ts).
                var (lid, why) = Vehicles.toggleLid(state, s, command.type);
                if (Js.T(why)) return reject(why);
                s.note = lid;
                return changed();
            }
            if (command.type == "aim")
            {
                var why = ControlMod.aim(Js.NN(state.control), Js.NN(command.x), Js.NN(command.y));
                return (why == null) ? changed() : ((why == "same") ? unchanged() : reject(why));
            }
            if (command.type == "steer")
            {
                var __d0 = command;
                var dx = __d0.dx;
                var dy = __d0.dy;
                // Any direction (first person steers by the camera): whole numbers up to AIM_MAX a side; -1, 0, 1 is the eight ways.
                if (!new List<double?> { dx, dy }.every((d) => (d != null) && JsNumber.isInteger(d) && (JsMath.abs(Js.NN(d)) <= ControlMod.AIM_MAX)))
                {
                    return reject("steer takes whole numbers from -AIM_MAX to AIM_MAX");
                }
                var run = command.run == true;
                if (((Js.NN(state.control).dx == dx) && (Js.NN(state.control).dy == dy)) && ((Js.NN(state.control).running == true) == run))
                {
                    return unchanged();
                }
                Js.NN(state.control).dx = Js.NN(dx);
                Js.NN(state.control).dy = Js.NN(dy);
                Js.NN(state.control).running = run;
                return changed();
            }
            if (!JsNumber.isInteger(command.x) || !JsNumber.isInteger(command.y))
            {
                return reject("not a tile");
            }
            var done = (command.type == "swing") ? ControlMod.swing(state, s, command) : ControlMod.use(state, s, command);
            if (!Js.T(done))
            {
                return reject("nothing to do there");
            }
            s.note = done;
            return changed();
        }
        if (command.type == "escape")
        {
            if ((command.who != "others") && (command.who != "none"))
            {
                return reject("others or none");
            }
            var why = Escape.escape(state, command.who);
            return Js.T(why) ? reject(why) : changed();
        }
        if (command.type == "succeed")
        {
            var why = SuccessionMod.succeed(state, command.choice);
            return Js.T(why) ? reject(why) : changed();
        }
        if (command.type == "promiseWish")
        {
            var s = Survivors.getSurvivor(state, command.survivorId);
            if (!Js.T(s))
            {
                return reject($"no survivor {Js.Str(command.survivorId)}");
            }
            var why = Wishes.promise(state, s);
            return Js.T(why) ? reject(why) : changed();
        }
        if (command.type == "raidSettlement")
        {
            var why = Colonies.raidSettlement(state, command.settlementId);
            return Js.T(why) ? reject(why) : changed();
        }
        if (command.type == "talk")
        {
            var you = ControlMod.controlled(state);
            if (Js.T(you) && !Js.T(command.survivorId))
            {
                var r = state.residents.find((x_1) => JsMath.max(JsMath.abs(x_1.pos.x - you.pos.x), JsMath.abs(x_1.pos.y - you.pos.y)) <= Talk.TALK_RANGE);
                if (Js.T(r))
                {
                    var why_2 = Residents.meetResident(state, you, r);
                    return Js.T(why_2) ? reject(why_2) : changed();
                }
            }
            var s = Js.T(command.survivorId) ? Survivors.getSurvivor(state, command.survivorId) : (Js.T(you) ? Js.Get(state.survivors.filter((o) => o != you).sort((a, b) => (JsMath.abs(a.pos.x - you.pos.x) + JsMath.abs(a.pos.y - you.pos.y)) - (JsMath.abs(b.pos.x - you.pos.x) + JsMath.abs(b.pos.y - you.pos.y))), 0) : null);
            if (!Js.T(s))
            {
                return reject(Js.T(command.survivorId) ? $"no survivor {Js.Str(command.survivorId)}" : "nobody to talk to");
            }
            var why = Talk.talk(state, s);
            return Js.T(why) ? reject(why) : changed();
        }
        if (command.type == "setOrder")
        {
            var s = Survivors.getSurvivor(state, command.survivorId);
            if (!Js.T(s))
            {
                return reject($"no survivor {Js.Str(command.survivorId)}");
            }
            var at = ((command.x != null) && (command.y != null)) ? new Point { x = Js.I(Js.NN(command.x)), y = Js.I(Js.NN(command.y)) } : s.pos;
            if (((command.order == "hold") || (command.order == "loot")) && !Grids.isWalkable(state.grid, at.x, at.y))
            {
                return reject($"({Js.Str(at.x)}, {Js.Str(at.y)}) is not walkable");
            }
            return giveOrder(state, s, command.order, at) ? changed() : unchanged();
        }
        if (command.type == "shout")
        {
            var you = ControlMod.controlled(state);
            if (!Js.T(you))
            {
                return reject("nobody is under control");
            }
            var heard = state.survivors.filter((s) => (s != you) && ((JsMath.abs(s.pos.x - you.pos.x) + JsMath.abs(s.pos.y - you.pos.y)) <= Orders.SHOUT_RANGE));
            var any = false;
            if ((command.order == "loot") && (((command.x == null) || (command.y == null)) || !Grids.inBounds(state.grid, Js.NN(command.x), Js.NN(command.y))))
            {
                return reject("loot where?");
            }
            foreach (var s in Js.Iter(heard))
            {
                any = giveOrder(state, s, command.order, (command.order == "loot") ? new Point { x = Js.I(Js.NN(command.x)), y = Js.I(Js.NN(command.y)) } : s.pos) || any;
            }
            return any ? changed() : unchanged();
        }
        if (command.type == "setPriority")
        {
            return setPriority(state, command.survivorId, command.jobType, command.priority);
        }
        if (command.type == "designateExplore")
        {
            var x0 = JsMath.max(0, JsMath.min(command.x0, command.x1));
            var y0 = JsMath.max(0, JsMath.min(command.y0, command.y1));
            var x1 = JsMath.min(state.grid.width - 1, JsMath.max(command.x0, command.x1), (x0 + Fog.MAX_EXPLORE_SIDE) - 1);
            var y1 = JsMath.min(state.grid.height - 1, JsMath.max(command.y0, command.y1), (y0 + Fog.MAX_EXPLORE_SIDE) - 1);
            if ((!new[] { command.x0, command.y0, command.x1, command.y1 }.ToList().every((__v) => JsNumber.isInteger(__v)) || (x1 < x0)) || (y1 < y0))
            {
                return reject("not a box on the map");
            }
            var dark = false;
            for (var y_3 = y0; (y_3 <= y1) && !dark; y_3++)
            {
                for (var x_4 = x0; (x_4 <= x1) && !dark; x_4++)
                {
                    dark = !Fog.isExplored(state, x_4, y_3);
                }
            }
            if (!dark)
            {
                return reject("already explored");
            }
            state.exploreAreas.push(new() { id = $"e{Js.Str(state.nextId++)}", x0 = Js.I(x0), y0 = Js.I(y0), x1 = Js.I(x1), y1 = Js.I(y1) });
            return changed();
        }
        if (command.type == "designateHunt")
        {
            var (x0, x1) = (JsMath.min(command.x0, command.x1), JsMath.max(command.x0, command.x1));
            var (y0, y1) = (JsMath.min(command.y0, command.y1), JsMath.max(command.y0, command.y1));
            var marked = state.animals.filter((a) => ((((!a.hunted && (!Js.T(command.kinds) || command.kinds.includes(a.kind))) && (a.pos.x >= x0)) && (a.pos.x <= x1)) && (a.pos.y >= y0)) && (a.pos.y <= y1));
            foreach (var a in Js.Iter(marked))
            {
                a.hunted = true;
            }
            return (marked.length > 0) ? changed() : unchanged();
        }
        if (command.type == "assignBed")
        {
            var found = FurnitureMod.getFurniture(state, command.bedId);
            var bed = (found?.kind == "bed") ? found : null;
            if (!Js.T(bed))
            {
                return reject($"no bed {Js.Str(command.bedId)}");
            }
            var who = command.survivorId;
            if ((who != null) && !Js.T(Survivors.getSurvivor(state, who)))
            {
                return reject($"no survivor {Js.Str(who)}");
            }
            if (bed.owner == who)
            {
                return unchanged();
            }
            foreach (var f in Js.Iter(FurnitureMod.ofKind(state, "bed")))
            {
                if ((who != null) && (f.owner == who))
                {
                    f.owner = null;
                }
            }
            bed.owner = who;
            if (Js.T(bed.occupant) && (bed.occupant != who))
            {
                var sleeper = Survivors.getSurvivor(state, bed.occupant);
                if (Js.T(sleeper))
                {
                    Needs.wake(state, sleeper, "their bed was given away");
                }
            }
            return changed();
        }
        if (command.type == "damageStructure")
        {
            var __d1 = command;
            var x_5 = __d1.x;
            var y_6 = __d1.y;
            var amount = __d1.amount;
            if (!Breach.isStructure(Grids.getTile(state.grid, Js.NN(x_5), Js.NN(y_6))))
            {
                return reject($"({Js.Str(x_5)}, {Js.Str(y_6)}) is not a wall or door");
            }
            if (!JsNumber.isInteger(amount) || (amount <= 0))
            {
                return reject("damage must be a positive integer");
            }
            Breach.damageStructure(state, Js.NN(x_5), Js.NN(y_6), Js.NN(amount));
            return changed();
        }
        if (command.type == "setCraftTarget")
        {
            var __d2 = command;
            var kind = __d2.kind;
            var count = __d2.count;
            if (!Crafting.isCraftKind(kind))
            {
                return reject($"can't craft {Js.Str(Js.Str(kind))}");
            }
            if ((!JsNumber.isInteger(count) || (count < 0)) || (count > Crafting.MAX_CRAFT_TARGET))
            {
                return reject($"target must be 0-{Js.Str(Crafting.MAX_CRAFT_TARGET)}");
            }
            if (Js.Get(state.craftTargets, kind) == count)
            {
                return unchanged();
            }
            state.craftTargets[Js.Idx(kind)] = Js.NN(count);
            return changed();
        }
        if (command.type == "setStorageFilter")
        {
            var room = Rooms.roomAt(state, Js.NN(command.x), Js.NN(command.y));
            var d = Js.T(room) ? Rooms.designationOf(state, room) : null;
            if (!Js.T(d) || (d.purpose != "storage"))
            {
                return reject("not a storage room");
            }
            if (!Rooms.STORE_CATEGORIES.includes(command.category))
            {
                return reject($"unknown category {Js.Str(Js.Str(command.category))}");
            }
            var refuses = Js.Set(d.refuses ?? []);
            if (refuses.has(command.category) != command.accept)
            {
                return unchanged();
            }
            if (Js.T(command.accept))
            {
                refuses.delete(command.category);
            }
            else
            {
                refuses.add(command.category);
            }
            d.refuses = Rooms.STORE_CATEGORIES.filter((c) => refuses.has(c));
            return changed();
        }
        if ((command.type == "recruitPrisoner") || (command.type == "releasePrisoner"))
        {
            var failed = Prisoners.decidePrisoner(state, command.prisonerId, command.type == "recruitPrisoner");
            return Js.T(failed) ? reject(failed) : changed();
        }
        if (command.type == "setShift")
        {
            var s = Survivors.getSurvivor(state, command.survivorId);
            if (!Js.T(s))
            {
                return reject($"no survivor {Js.Str(command.survivorId)}");
            }
            if ((command.shift != "day") && (command.shift != "night"))
            {
                return reject("shift is day or night");
            }
            if (s.shift == command.shift)
            {
                return unchanged();
            }
            s.shift = command.shift;
            return changed();
        }
        if (command.type == "planPerimeter")
        {
            return planPerimeter(state, Js.NN(command.x0), Js.NN(command.y0), Js.NN(command.x1), Js.NN(command.y1));
        }
        if (command.type == "fulfilRequest")
        {
            var failed = Radio.fulfilRequest(state, command.requestId);
            return Js.T(failed) ? reject(failed) : changed();
        }
        if (command.type == "setPolicy")
        {
            if (!Policies.isPolicy(command.policy))
            {
                return reject($"unknown policy {Js.Str(Js.Str(command.policy))}");
            }
            var on = state.policies.includes(command.policy);
            if (on == command.on)
            {
                return unchanged();
            }
            state.policies = Js.T(command.on) ? [.. state.policies, command.policy] : state.policies.filter((p) => p != command.policy);
            return changed();
        }
        if (command.type == "amputate")
        {
            var s = Survivors.getSurvivor(state, command.survivorId);
            if (!Js.T(s))
            {
                return reject($"no survivor {Js.Str(command.survivorId)}");
            }
            var failed = Wounds.amputate(state, s);
            return Js.T(failed) ? reject(failed) : changed();
        }
        if (command.type == "recallOutpost")
        {
            var failed = Outposts.recallOutpost(state, command.expeditionId);
            return Js.T(failed) ? reject(failed) : changed();
        }
        if (command.type == "giftSettlement")
        {
            var failed = Settlements.giveGift(state, command.settlementId);
            return Js.T(failed) ? reject(failed) : changed();
        }
        if (command.type == "trade")
        {
            var failed = Visitors.trade(state, command.traderId, Js.NN(command.offer));
            return Js.T(failed) ? reject(failed) : changed();
        }
        if (command.type == "toggleDoor")
        {
            var why = toggleDoor(state, Js.NN(command.x), Js.NN(command.y));
            return Js.T(why) ? reject(why) : changed();
        }
        if (command.type == "toggleLock")
        {
            var __d3 = command;
            var x_7 = __d3.x;
            var y_8 = __d3.y;
            if (((command.side != null) && Grids.inBounds(state.grid, Js.NN(x_7), Js.NN(y_8))) && (Js.Get(state.grid.edges, Grids.edgeIndex(state.grid, Js.NN(x_7), Js.NN(y_8), command.side)) == Grids.EDGE_DOOR))
            {
                var e = Grids.edgeIndex(state.grid, Js.NN(x_7), Js.NN(y_8), command.side);
                var open_9 = state.grid.edgeOpen.indexOf(e);
                if (open_9 >= 0)
                {
                    state.grid.edgeOpen.splice(open_9, 1);
                }
                var at_10 = state.grid.edgeLocked.indexOf(e);
                if (at_10 >= 0)
                {
                    state.grid.edgeLocked.splice(at_10, 1);
                }
                else
                {
                    state.grid.edgeLocked.push(e);
                }
                Grids.noteEdgeChange(state.grid, e);
                state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
                return changed();
            }
            if (Grids.getTile(state.grid, Js.NN(x_7), Js.NN(y_8)) != "door")
            {
                return reject($"({Js.Str(x_7)}, {Js.Str(y_8)}) is not a door");
            }
            var i = ((double)(y_8) * (state.grid.width)) + x_7;
            var open = state.grid.open.indexOf(i);
            if (open >= 0)
            {
                state.grid.open.splice(open, 1);
            }
            var at = state.grid.locked.indexOf(i);
            if (at >= 0)
            {
                state.grid.locked.splice(at, 1);
            }
            else
            {
                state.grid.locked.push(i);
                Zombies.evictZombies(state, new Point { x = Js.I(Js.NN(x_7)), y = Js.I(Js.NN(y_8)) });
            }
            Grids.noteChange(state.grid, Js.NN(i));
            state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
            return changed();
        }
        if (command.type == "sendExpedition")
        {
            var members = command.survivorIds.map((id) => Survivors.getSurvivor(state, id));
            if (members.some((s) => !Js.T(s)) || (Js.Set(command.survivorIds).size != members.length))
            {
                return reject("no such survivor");
            }
            if (!state.pointsOfInterest.some((p) => p.id == command.poiId))
            {
                return reject($"no point of interest {Js.Str(command.poiId)}");
            }
            if ((members).some((s) => s.childUntil != null))
            {
                return reject("children stay home");
            }
            foreach (var s in Js.Iter(members))
            {
                if (Js.T(s.jobId))
                {
                    Jobs.releaseSurvivor(state, s, "off on an expedition");
                }
                Needs.wake(state, s, "off on an expedition");
                Needs.cancelMeal(state, s, "off on an expedition");
                Needs.cancelTreatment(state, s, "off on an expedition");
                Weapons.cancelArming(state, s, "off on an expedition");
                Clothing.cancelDressing(state, s, "off on an expedition");
                Water.cancelDrink(s, "off on an expedition");
                Books.stopReading(state, s, "off on an expedition");
                s.leisure = null;
                s.breakdown = null;
                s.goal = null;
                s.move = null;
                s.path = [];
            }
            var failed = Expeditions.sendExpedition(state, members, command.poiId, command.outpost == true);
            return Js.T(failed) ? reject(failed) : changed();
        }
        if ((command.type == "acceptWanderer") || (command.type == "dismissWanderer"))
        {
            var w = Wanderers.getWanderer(state, command.wandererId);
            if (!Js.T(w))
            {
                return reject($"no wanderer {Js.Str(command.wandererId)}");
            }
            Wanderers.removeWanderer(state, w);
            if (command.type == "acceptWanderer")
            {
                var s = Survivors.addSurvivor(state, w.x, w.y, w.name);
                s.traits = (w.traits).ToList();
                if (Js.T(state.control))
                {
                    s.order = new Order { kind = "follow" };
                }
                Events.logEvent(state, $"{Js.Str(w.name)} joined the colony");
            }
            else
            {
                Events.logEvent(state, $"{Js.Str(w.name)} was turned away");
            }
            return changed();
        }
        if (command.type == "setRest")
        {
            return setNeed(state, command.survivorId, "rest", command.rest, Rest.REST_MAX);
        }
        if (command.type == "setSatiety")
        {
            return setNeed(state, command.survivorId, "satiety", command.satiety, Food.SATIETY_MAX);
        }
        if (command.type == "setHealth")
        {
            return setNeed(state, command.survivorId, "health", command.health, Health.HEALTH_MAX);
        }
        var __d4 = command;
        var x = __d4.x;
        var y = __d4.y;
        if (!Grids.inBounds(state.grid, Js.NN(x), Js.NN(y)))
        {
            return reject($"({Js.Str(x)}, {Js.Str(y)}) is out of bounds");
        }
        if (DESIGNATIONS.has(command.type) && !Fog.isExplored(state, Js.NN(x), Js.NN(y)))
        {
            return reject($"({Js.Str(x)}, {Js.Str(y)}) is unexplored");
        }
        switch (command.type)
        {
            case "placeWall":
                {
                    if (command.side != null)
                    {
                    return placeEdge(state, Js.NN(x), Js.NN(y), command.side, Grids.EDGE_WALL);
                }
                    if (Js.T(Survivors.survivorAt(state, Js.NN(x), Js.NN(y))))
                    {
                    return reject($"({Js.Str(x)}, {Js.Str(y)}) is occupied");
                }
                    return setStructure(state, Js.NN(x), Js.NN(y), "wall");
                }
            case "placeDoor":
                {
                    if (command.side != null)
                    {
                    return placeEdge(state, Js.NN(x), Js.NN(y), command.side, Grids.EDGE_DOOR);
                }
                    return setStructure(state, Js.NN(x), Js.NN(y), "door");
                }
            case "clearTile":
                {
                    {
                    var f = FurnitureMod.furnitureAt(state, Js.NN(x), Js.NN(y));
                    if (Js.T(f))
                    {
                        FurnitureMod.removeFurniture(state, f);
                        return changed();
                    }
                    return setStructure(state, Js.NN(x), Js.NN(y), "floor");
                }
                }
            case "placeBed":
            case "placeTable":
            case "placeWorkbench":
                {
                    if (Grids.getTile(state.grid, Js.NN(x), Js.NN(y)) != "floor")
                    {
                    return reject($"({Js.Str(x)}, {Js.Str(y)}) is not floor");
                }
                    if (Js.T(FurnitureMod.furnitureAt(state, Js.NN(x), Js.NN(y))))
                    {
                    return unchanged();
                }
                    FurnitureMod.addFurniture(state, (command.type == "placeBed") ? "bed" : ((command.type == "placeTable") ? "table" : "workbench"), Js.NN(x), Js.NN(y));
                    return changed();
                }
            case "designateBuild":
                {
                    if (command.side != null)
                    {
                    return designateEdge(state, Js.NN(x), Js.NN(y), command.side, command.structure);
                }
                    return designateBuild(state, Js.NN(x), Js.NN(y), command.structure);
                }
            case "designateRemove":
                {
                    return designateRemove(state, Js.NN(x), Js.NN(y));
                }
            case "cancelDesignation":
                {
                    return cancelDesignation(state, Js.NN(x), Js.NN(y));
                }
            case "designateFarm":
                {
                    {
                    if ((command.crop != null) && !Farming.isCrop(command.crop))
                    {
                        return reject($"unknown crop {Js.Str(Js.Str(command.crop))}");
                    }
                    if (Js.T((Grids.getTile(state.grid, Js.NN(x), Js.NN(y)) != "floor") || Js.T(FurnitureMod.furnitureAt(state, Js.NN(x), Js.NN(y)))))
                    {
                        return unchanged();
                    }
                    var crop = command.crop ?? "potato";
                    var existing = Farming.plotAt(state, Js.NN(x), Js.NN(y));
                    if (existing?.crop == crop)
                    {
                        return unchanged();
                    }
                    if (Js.T(existing))
                    {
                        Farming.removePlot(state, existing);
                    }
                    Farming.addPlot(state, Js.NN(x), Js.NN(y), crop);
                    return changed();
                }
                }
            case "designateScavenge":
                {
                    {
                    var crate = FurnitureMod.furnitureAt(state, Js.NN(x), Js.NN(y));
                    if ((((crate?.kind != "crate") && (crate?.kind != "tree")) || ((crate.kind == "crate") && (crate.loot == 0))) || crate.scavenge)
                    {
                        return unchanged();
                    }
                    FurnitureMod.setScavenge(state, crate, true);
                    return changed();
                }
                }
            case "designateRoom":
                {
                    return designateRoom(state, Js.NN(x), Js.NN(y), command.purpose);
                }
            case "ignite":
                {
                    return FireMod.ignite(state, Js.NN(x), Js.NN(y)) ? changed() : unchanged();
                }
            case "spawnSurvivor":
                {
                    return spawnSurvivor(state, Js.NN(x), Js.NN(y));
                }
            case "spawnZombie":
                {
                    if (Grids.getTile(state.grid, Js.NN(x), Js.NN(y)) != "floor")
                    {
                    return reject($"({Js.Str(x)}, {Js.Str(y)}) is not floor");
                }
                    Zombies.addZombie(state, Js.NN(x), Js.NN(y));
                    return changed();
                }
            case "spawnAnimal":
                {
                    // A debug spawn (the game's --animals): deer, rabbit, wolf, bear or dog on open ground.
                    var kind = command.kind ?? "deer";
                    if ((kind != "deer") && (kind != "rabbit") && (kind != "wolf") && (kind != "bear") && (kind != "dog"))
                    {
                    return reject($"no animal called {Js.Str(kind)}");
                }
                    if (!Animals.ground(state, Js.NN(x), Js.NN(y)))
                    {
                    return reject($"({Js.Str(x)}, {Js.Str(y)}) is not open ground");
                }
                    if (kind == "dog") Dogs.addDog(state, Js.NN(x), Js.NN(y));
                    else if (kind == "deer") Animals.addDeer(state, Js.NN(x), Js.NN(y));
                    else Animals.addAnimal(state, kind, Js.NN(x), Js.NN(y));
                    return changed();
                }
            case "spawnVehicle":
                {
                    if (!Vehicles.parkable(state, new Point { x = Js.I(Js.NN(x)), y = Js.I(Js.NN(y)) }))
                    {
                    return reject($"a car won't fit at ({Js.Str(x)}, {Js.Str(y)})");
                }
                    Vehicles.addVehicle(state, Js.NN(x), Js.NN(y));
                    return changed();
                }
            case "spawnFood":
            case "spawnWeapon":
                {
                    if (!Grids.isWalkable(state.grid, Js.NN(x), Js.NN(y)))
                    {
                    return reject($"({Js.Str(x)}, {Js.Str(y)}) is not walkable");
                }
                    Items.spawnItem(state, (command.type == "spawnFood") ? "food" : "weapon", Js.NN(x), Js.NN(y));
                    return changed();
                }
            case "moveSurvivor":
                {
                    return moveSurvivor(state, command.survivorId, Js.NN(x), Js.NN(y));
                }
        }
        throw new InvalidOperationException("unreachable: every case returns");
    }

    public static CommandResult reject(string reason)
    {
        return new CommandResult { ok = false, reason = reason };
    }

    public static CommandResult changed()
    {
        return new CommandResult { ok = true, changed = true };
    }

    public static CommandResult unchanged()
    {
        return new CommandResult { ok = true, changed = false };
    }

    public static CommandResult setStructure(SimState state, int x, int y, string tile)
    {
        if (Grids.getTile(state.grid, x, y) == "water")
        {
            return reject($"({Js.Str(x)}, {Js.Str(y)}) is water");
        }
        if (!Grids.setTile(state.grid, x, y, tile))
        {
            return unchanged();
        }
        state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
        if (tile != "floor")
        {
            var f = FurnitureMod.furnitureAt(state, x, y);
            if (Js.T(f))
            {
                FurnitureMod.removeFurniture(state, f);
            }
        }
        if (tile == "wall")
        {
            Designations.evictFrom(state, new Point { x = Js.I(x), y = Js.I(y) });
        }
        if (tile != "floor")
        {
            Zombies.evictZombies(state, new Point { x = Js.I(x), y = Js.I(y) });
            Farming.clearPlot(state, new Point { x = Js.I(x), y = Js.I(y) });
        }
        return changed();
    }
    public static CommandResult setStructure(SimState state, double x, double y, string tile) => setStructure(state, Js.I(x), Js.I(y), tile);

    /// <summary>An edge on the map's own border, or not a side: nothing to build there.</summary>
    public static string? edgeProblem(SimState state, int x, int y, string side)
    {
        if ((side != "n") && (side != "w"))
        {
            return "a side is n or w";
        }
        if (((side == "n") && (y == 0)) || ((side == "w") && (x == 0)))
        {
            return "that edge is the map's border";
        }
        return null;
    }
    public static string? edgeProblem(SimState state, double x, double y, string side) => edgeProblem(state, Js.I(x), Js.I(y), side);

    /// <summary>A blueprint for an edge wall or door.</summary>
    public static CommandResult designateEdge(SimState state, int x, int y, string side, string structure)
    {
        if (((structure != "wall") && (structure != "door")) && (structure != "barricade"))
        {
            return reject("only walls, doors and barricades go on edges");
        }
        var why = edgeProblem(state, x, y, side);
        if (Js.T(why))
        {
            return reject(why);
        }
        var here = Js.Get(state.grid.edges, Grids.edgeIndex(state.grid, x, y, side));
        var boarding = ((structure == "barricade") && Grids.isWindow(here)) && !state.grid.edgeBoarded.includes(Grids.edgeIndex(state.grid, x, y, side));
        if ((here != Grids.EDGE_NONE) && !Js.T(boarding))
        {
            return unchanged();
        }
        if (state.blueprints.some((b) => ((b.x == x) && (b.y == y)) && (b.side == side)))
        {
            return unchanged();
        }
        Designations.addBlueprint(state, x, y, structure, side);
        return changed();
    }
    public static CommandResult designateEdge(SimState state, double x, double y, string side, string structure) => designateEdge(state, Js.I(x), Js.I(y), side, structure);

    /// <summary>An edge wall or door, at once (the instant tools).</summary>
    public static CommandResult placeEdge(SimState state, int x, int y, string side, double kind)
    {
        var why = edgeProblem(state, x, y, side);
        if (Js.T(why))
        {
            return reject(why);
        }
        if (!Grids.setEdge(state.grid, x, y, side, kind))
        {
            return unchanged();
        }
        state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
        return changed();
    }
    public static CommandResult placeEdge(SimState state, double x, double y, string side, double kind) => placeEdge(state, Js.I(x), Js.I(y), side, kind);

    public static CommandResult designateBuild(SimState state, int x, int y, string structure)
    {
        var existing = Designations.blueprintAt(state, x, y);
        if (existing?.target == structure)
        {
            return unchanged();
        }
        if (structure == "reinforce")
        {
            if (!Breach.isStructure(Grids.getTile(state.grid, x, y)) || state.grid.reinforced.includes(((double)(y) * (state.grid.width)) + x))
            {
                return unchanged();
            }
            if (Js.T(existing))
            {
                Designations.cancelBlueprint(state, existing);
            }
            Designations.addBlueprint(state, x, y, "reinforce");
            return changed();
        }
        if (Js.T((Grids.getTile(state.grid, x, y) != "floor") || Js.T(FurnitureMod.furnitureAt(state, x, y))))
        {
            return reject($"({Js.Str(x)}, {Js.Str(y)}) is not empty floor; remove what's there first");
        }
        if (Js.T(existing))
        {
            Designations.cancelBlueprint(state, existing);
        }
        Designations.addBlueprint(state, x, y, structure);
        return changed();
    }
    public static CommandResult designateBuild(SimState state, double x, double y, string structure) => designateBuild(state, Js.I(x), Js.I(y), structure);

    /// <summary>Walls in a box: edge-wall plans on its outer borders (grid.ts edges), a door plan mid-way along each side. Borders on the map's edge, or already built on, are skipped.</summary>
    public static CommandResult planPerimeter(SimState state, int ax, int ay, int bx, int by)
    {
        var ex0 = JsMath.max(0, JsMath.min(ax, bx));
        var ex1 = JsMath.min(state.grid.width - 1, JsMath.max(ax, bx));
        var ey0 = JsMath.max(0, JsMath.min(ay, by));
        var ey1 = JsMath.min(state.grid.height - 1, JsMath.max(ay, by));
        if (((ex1 - ex0) < 1) || ((ey1 - ey0) < 1))
        {
            return reject("too small to wall in");
        }
        var emx = JsMath.floor(((double)((ex0 + ex1)) / (2)));
        var emy = JsMath.floor(((double)((ey0 + ey1)) / (2)));
        double edgesPlanned = 0;
        void plan(double x, double y, string side, bool door)
        {
            if (!Grids.inBounds(state.grid, x, y) || !Fog.isExplored(state, x, y))
            {
                return;
            }
            var r = designateEdge(state, x, y, side, door ? "door" : "wall");
            if (Js.T(r.ok && Js.T(r.changed)))
            {
                edgesPlanned += 1;
            }
        }
        for (var x = ex0; x <= ex1; x++)
        {
            plan(x, ey0, "n", x == emx);
            plan(x, ey1 + 1, "n", x == emx);
        }
        for (var y = ey0; y <= ey1; y++)
        {
            plan(ex0, y, "w", y == emy);
            plan(ex1 + 1, y, "w", y == emy);
        }
        return (edgesPlanned > 0) ? changed() : unchanged();
    }
    public static CommandResult planPerimeter(SimState state, double ax, double ay, double bx, double by) => planPerimeter(state, Js.I(ax), Js.I(ay), Js.I(bx), Js.I(by));

    /// <summary>The old tile perimeter (kept for reference by replays of older logs: unused).</summary>
    public static CommandResult planPerimeterTiles(SimState state, int ax, int ay, int bx, int by)
    {
        var x0 = JsMath.max(0, JsMath.min(ax, bx));
        var x1 = JsMath.min(state.grid.width - 1, JsMath.max(ax, bx));
        var y0 = JsMath.max(0, JsMath.min(ay, by));
        var y1 = JsMath.min(state.grid.height - 1, JsMath.max(ay, by));
        if (((x1 - x0) < 2) || ((y1 - y0) < 2))
        {
            return reject("too small to wall in");
        }
        var mx = JsMath.floor(((double)((x0 + x1)) / (2)));
        var my = JsMath.floor(((double)((y0 + y1)) / (2)));
        double planned = 0;
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                if ((((x != x0) && (x != x1)) && (y != y0)) && (y != y1))
                {
                    continue;
                }
                if (Js.T((Js.T((!Fog.isExplored(state, x, Js.NN(y)) || (Grids.getTile(state.grid, x, Js.NN(y)) != "floor")) || Js.T(FurnitureMod.furnitureAt(state, x, Js.NN(y))))) || Js.T(Designations.blueprintAt(state, x, Js.NN(y)))))
                {
                    continue;
                }
                var door = ((x == mx) && ((y == y0) || (y == y1))) || ((y == my) && ((x == x0) || (x == x1)));
                Designations.addBlueprint(state, x, Js.NN(y), Js.T(door) ? "door" : "wall");
                planned += 1;
            }
        }
        return (planned > 0) ? changed() : unchanged();
    }
    public static CommandResult planPerimeterTiles(SimState state, double ax, double ay, double bx, double by) => planPerimeterTiles(state, Js.I(ax), Js.I(ay), Js.I(bx), Js.I(by));

    public static CommandResult designateRemove(SimState state, int x, int y)
    {
        var existing = Designations.blueprintAt(state, x, y);
        if (existing?.target == "floor")
        {
            return unchanged();
        }
        if (Js.T(existing))
        {
            Designations.cancelBlueprint(state, existing);
            return changed();
        }
        if (!Designations.hasRemovable(state, x, y))
        {
            return unchanged();
        }
        Designations.addBlueprint(state, x, y, "floor");
        return changed();
    }
    public static CommandResult designateRemove(SimState state, double x, double y) => designateRemove(state, Js.I(x), Js.I(y));

    public static CommandResult cancelDesignation(SimState state, int x, int y)
    {
        var bp = Designations.blueprintAt(state, x, y);
        if (Js.T(bp))
        {
            Designations.cancelBlueprint(state, bp);
            return changed();
        }
        var plot = Farming.plotAt(state, x, y);
        if (Js.T(plot))
        {
            Farming.removePlot(state, plot);
            return changed();
        }
        var crate = FurnitureMod.furnitureAt(state, x, y);
        if ((crate?.kind == "crate") || (crate?.kind == "tree"))
        {
            if (!crate.scavenge)
            {
                return unchanged();
            }
            FurnitureMod.setScavenge(state, crate, false);
            return changed();
        }
        var room = Rooms.roomAt(state, x, y);
        var d = Js.T(room) ? Rooms.designationOf(state, room) : state.roomDesignations.find((r) => (r.x == x) && (r.y == y));
        if (!Js.T(d))
        {
            return unchanged();
        }
        state.roomDesignations.splice(state.roomDesignations.indexOf(d), 1);
        return changed();
    }
    public static CommandResult cancelDesignation(SimState state, double x, double y) => cancelDesignation(state, Js.I(x), Js.I(y));

    public static CommandResult designateRoom(SimState state, int x, int y, object purpose)
    {
        if (!Rooms.ROOM_PURPOSES.includes((purpose as string)))
        {
            return reject($"unknown room purpose {Js.Str(Js.Str(purpose))}");
        }
        var room = Rooms.roomAt(state, x, y);
        if (!Js.T(room))
        {
            return reject($"({Js.Str(x)}, {Js.Str(y)}) is not inside an enclosed room");
        }
        var existing = Rooms.designationOf(state, room);
        if (Js.Eq(existing?.purpose, purpose))
        {
            return unchanged();
        }
        if (Js.T(existing))
        {
            existing.purpose = (purpose as string);
        }
        else
        {
            state.roomDesignations.push(new() { id = $"r{Js.Str(state.nextId++)}", x = Js.I(x), y = Js.I(y), purpose = (purpose as string) });
        }
        return changed();
    }
    public static CommandResult designateRoom(SimState state, double x, double y, object purpose) => designateRoom(state, Js.I(x), Js.I(y), purpose);

    public static CommandResult spawnSurvivor(SimState state, int x, int y)
    {
        if (!Grids.isWalkable(state.grid, x, y))
        {
            return reject($"({Js.Str(x)}, {Js.Str(y)}) is not walkable");
        }
        if (Js.T(Survivors.survivorAt(state, x, y)))
        {
            return reject($"({Js.Str(x)}, {Js.Str(y)}) is occupied");
        }
        Survivors.addSurvivor(state, x, y);
        return changed();
    }
    public static CommandResult spawnSurvivor(SimState state, double x, double y) => spawnSurvivor(state, Js.I(x), Js.I(y));

    public static CommandResult moveSurvivor(SimState state, string id, int x, int y)
    {
        var survivor = Survivors.getSurvivor(state, id);
        if (!Js.T(survivor))
        {
            return reject($"no survivor {Js.Str(id)}");
        }
        if (!Grids.isWalkable(state.grid, x, y))
        {
            return reject($"({Js.Str(x)}, {Js.Str(y)}) is not walkable");
        }
        if (((survivor.goal?.x == x) && (survivor.goal.y == y)) && !Js.T(survivor.jobId))
        {
            return unchanged();
        }
        if (ControlMod.isControlled(state, survivor))
        {
            return reject($"{Js.Str(survivor.name)} is under your control");
        }
        dropEverything(state, survivor, "an order");
        Survivors.setGoal(survivor, new Point { x = Js.I(x), y = Js.I(y) });
        return changed();
    }
    public static CommandResult moveSurvivor(SimState state, string id, double x, double y) => moveSurvivor(state, id, Js.I(x), Js.I(y));

    /// <summary>Stops whatever a survivor was doing: their job, sleep, meal, and so on.</summary>
    public static void dropEverything(SimState state, Survivor survivor, string why)
    {
        if (Js.T(survivor.jobId))
        {
            Jobs.releaseSurvivor(state, survivor, $"ordered elsewhere");
        }
        Needs.wake(state, survivor, $"woken by {Js.Str(why)}");
        Needs.cancelMeal(state, survivor, $"meal interrupted by {Js.Str(why)}");
        Needs.cancelTreatment(state, survivor, $"treatment interrupted by {Js.Str(why)}");
        Weapons.cancelArming(state, survivor, $"interrupted by {Js.Str(why)}");
        Clothing.cancelDressing(state, survivor, $"interrupted by {Js.Str(why)}");
        Water.cancelDrink(survivor, $"interrupted by {Js.Str(why)}");
        Books.stopReading(state, survivor, $"interrupted by {Js.Str(why)}");
        survivor.leisure = null;
    }

    /// <summary>Gives one survivor an order. Returns false if it changed nothing.</summary>
    public static bool giveOrder(SimState state, Survivor s, string kind, Point at)
    {
        if (ControlMod.isControlled(state, s) || (s.childUntil != null))
        {
            return false;
        }
        Order? order = (kind == "settle") ? null : ((kind == "follow") ? new() { kind = kind } : ((kind == "loot") ? new() { kind = kind, x = Js.I(at.x), y = Js.I(at.y), crateId = null, progress = 0 } : new() { kind = kind, x = Js.I(at.x), y = Js.I(at.y) }));
        if (Js.Json_stringify(order) == Js.Json_stringify(s.order))
        {
            return false;
        }
        s.order = order;
        var job = Js.T(s.jobId) ? state.jobs.find((j) => j.id == s.jobId) : null;
        if ((Js.T(Js.T(order) && Js.T(job))) && !(Orders.ORDER_JOBS.includes(job.type) && Orders.withinOrder(state, s, job)))
        {
            Jobs.releaseSurvivor(state, s, Orders.describeOrder(order));
        }
        if (Js.T(order))
        {
            Books.stopReading(state, s, Orders.describeOrder(order));
            s.leisure = null;
        }
        s.nextSeekTick = 0;
        s.note = Orders.describeOrder(order);
        return true;
    }

    /// <summary>Open or close a door. Returns why not, or null.</summary>
    public static string? toggleDoor(SimState state, int x, int y)
    {
        if (Grids.getTile(state.grid, x, y) != "door")
        {
            return $"({Js.Str(x)}, {Js.Str(y)}) is not a door";
        }
        var i = ((double)(y) * (state.grid.width)) + x;
        var at = state.grid.open.indexOf(i);
        var passing = state.grid.passing.indexOf(i);
        if (passing >= 0)
        {
            state.grid.passing.splice(passing, 1);
            return null;
        }
        if (at < 0)
        {
            if (state.grid.locked.includes(i))
            {
                return "it's locked";
            }
            state.grid.open.push(i);
        }
        else
        {
            var inTheWay = Js.ListOf<IBody>(state.survivors, state.zombies, state.residents).some((e) => (e.pos.x == x) && (e.pos.y == y));
            if (Js.T(inTheWay))
            {
                return "someone is in the doorway";
            }
            state.grid.open.splice(at, 1);
        }
        state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
        return null;
    }
    public static string? toggleDoor(SimState state, double x, double y) => toggleDoor(state, Js.I(x), Js.I(y));

    public static CommandResult takeControl(SimState state, string? id)
    {
        if (id == null)
        {
            if (!Js.T(state.control))
            {
                return unchanged();
            }
            var was = ControlMod.controlled(state);
            state.control = null;
            if (Js.T(was))
            {
                var carried = Js.T(was.carrying) ? state.items.find((i) => i.id == was.carrying) : null;
                was.carrying = null;
                if (Js.T(carried))
                {
                    Items.dropItem(state, carried, was.pos);
                }
                was.note = "back to their own devices";
            }
            return changed();
        }
        var survivor = Survivors.getSurvivor(state, id);
        if (!Js.T(survivor))
        {
            return reject($"no survivor {Js.Str(id)}");
        }
        if (state.control?.survivorId == id)
        {
            return unchanged();
        }
        if (survivor.childUntil != null)
        {
            return reject($"{Js.Str(survivor.name)} is a child");
        }
        dropEverything(state, survivor, "you");
        survivor.goal = null;
        survivor.path = [];
        survivor.pathVersion = -1;
        survivor.note = "under your control";
        state.control = new Control { survivorId = id, dx = 0, dy = 0, facing = new Point { x = Js.I(0), y = Js.I(1) }, nextSwing = 0, busy = null };
        return changed();
    }

    public static CommandResult setNeed(SimState state, string id, string need, object value, double max)
    {
        var survivor = Survivors.getSurvivor(state, id);
        if (!Js.T(survivor))
        {
            return reject($"no survivor {Js.Str(id)}");
        }
        if ((((/*TODO typeof*/Js.TypeOf(value) != "number") || !JsNumber.isInteger(value)) || (Js.Num(value) < 0)) || (Js.Num(value) > max))
        {
            return reject($"{Js.Str(need)} must be an integer 0-{Js.Str(max)}");
        }
        if (Js.Eq(Js.GetProp<double>(survivor, need), value))
        {
            return unchanged();
        }
        Js.SetProp(survivor, need, value);
        if (need == "satiety")
        {
            survivor.nextMealTick = 0;
        }
        return changed();
    }

    public static CommandResult setPriority(SimState state, string id, object jobType, object priority)
    {
        var survivor = Survivors.getSurvivor(state, id);
        if (!Js.T(survivor))
        {
            return reject($"no survivor {Js.Str(id)}");
        }
        if (!JobTypes.isJobType(jobType))
        {
            return reject($"unknown job type {Js.Str(Js.Str(jobType))}");
        }
        if (!JobTypes.isPriority(priority))
        {
            return reject($"priority must be 0-4, got {Js.Str(Js.Str(priority))}");
        }
        if (survivor.childUntil != null)
        {
            return reject($"{Js.Str(survivor.name)} is a child");
        }
        if (Js.Eq(survivor.priorities[(string)jobType], priority))
        {
            return unchanged();
        }
        survivor.priorities[(string)jobType] = Js.Num(priority);
        survivor.nextSeekTick = 0;
        return changed();
    }

    /// <summary>Within reach of your hands: under you or a tile beside you (straight or diagonal).</summary>
    public static bool besideYou(Survivor s, Point p)
    {
        return (JsMath.abs(p.x - s.pos.x) <= 1) && (JsMath.abs(p.y - s.pos.y) <= 1);
    }

    /// <summary>The bag's commands (inventory.ts): take (one, from the ground or a container beside you), takeAll (a container's
    /// contents, or every one of `kind`), drop (from the bag, or what you wear), stash (one into a container beside you),
    /// stashAll (the bag, or every one of `kind`), useItem (eat, treat, wear or wield from the bag), unequip (off into the bag).
    /// Returns why not, or null when done.</summary>
    public static string? bagCommand(SimState state, Survivor s, Command command)
    {
        if (command.type == "takeAll")
        {
            var box = Inventory.containerOf(state, command.containerId);
            if (!Js.T(box) || !besideYou(s, box.at))
            {
                return "not beside you";
            }
            var inside = Inventory.contents(state, new Furniture { id = command.containerId }).filter((i) => !Js.T(command.kind) || (i.kind == command.kind));
            if (inside.length == 0)
            {
                return Js.T(command.kind) ? $"no {Js.Str(command.kind)} in it" : "nothing in it";
            }
            double took = 0;
            foreach (var item_11 in Js.Iter(inside))
            {
                if (Inventory.stow(state, s, item_11) == null)
                {
                    took++;
                }
            }
            return (took > 0) ? null : "your bag is too full";
        }
        if (command.type == "stashAll")
        {
            var box = Inventory.containerOf(state, command.containerId);
            if (!Js.T(box) || !besideYou(s, box.at))
            {
                return "not beside you";
            }
            return Inventory.stashAll(state, s, command.containerId, command.kind);
        }
        var item = Items.getItem(state, command.itemId);
        if (!Js.T(item))
        {
            return "no such thing";
        }
        if (command.type == "unequip")
        {
            return Inventory.unequip(state, s, item);
        }
        if ((command.type == "drop") && Js.T(Inventory.slotOf(s, item.id)))
        {
            return Inventory.dropWorn(state, s, item);
        }
        if (command.type == "take")
        {
            var where = item.pos ?? (Js.T(item.inside) ? Inventory.containerOf(state, item.inside)?.at : null);
            if ((item.carriedBy != s.id) && (!Js.T(where) || !besideYou(s, where)))
            {
                return "not beside you";
            }
            if ((item.carriedBy == s.id) && (s.carrying != item.id))
            {
                return "already yours";
            }
            return Inventory.stow(state, s, item);
        }
        if (!s.pack.includes(item.id))
        {
            return "not in your bag";
        }
        if (command.type == "drop")
        {
            return Inventory.dropFromPack(state, s, item.id);
        }
        if (command.type == "stash")
        {
            var box = Inventory.containerOf(state, command.containerId);
            if (!Js.T(box) || !besideYou(s, box.at))
            {
                return "not beside you";
            }
            return Inventory.stash(state, s, item, command.containerId);
        }
        var c = Js.NN(state.control);
        if ((item.kind == "food") || (item.kind == "meal"))
        {
            c.busy = new ControlBusy { what = "eat", targetId = item.id, progress = 0, duration = Food.EAT_TICKS };
            s.note = "eating";
            return null;
        }
        if (((item.kind == "bandage") || (item.kind == "medicine")) || (item.kind == "medkit"))
        {
            if (!Medicine.remediesFor(s).includes(item.kind))
            {
                return "you don't need it";
            }
            c.busy = new ControlBusy { what = "treat", targetId = item.id, progress = 0, duration = Health.TREAT_TICKS };
            s.note = (item.kind == "medicine") ? "taking medicine" : "bandaging";
            return null;
        }
        if (Js.T(Inventory.slotFor(item.kind)))
        {
            s.note = Inventory.equipFromPack(state, s, item, s.pos);
            return null;
        }
        return "nothing to do with it here";
    }
}
