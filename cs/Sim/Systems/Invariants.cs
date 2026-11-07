// Ported from src/sim/invariants.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Consistency checks across survivors, jobs, items, and blueprints.
// Returns human-readable problems; empty means healthy. Used by tests after
// every tick and available to the debug panel. Read-only.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Invariants
{

    public static List<string> checkInvariants(SimState state)
    {
        List<string> problems = [];
        var __d0 = state;
        var grid = __d0.grid;
        var survivors = Js.MapOf(Expeditions.everyone(state), (s) => s.id, (s) => s);
        var jobs = Js.MapOf(state.jobs, (j) => j.id, (j) => j);
        var items = Js.MapOf(state.items, (i) => i.id, (i) => i);
        foreach (var s in Js.Iter(state.survivors))
        {
            if (!Grids.isWalkable(grid, s.pos.x, s.pos.y))
            {
                problems.push($"{Js.Str(s.name)} stands on unwalkable ({Js.Str(s.pos.x)}, {Js.Str(s.pos.y)})");
            }
            if (Js.T(s.jobId))
            {
                var job = jobs.get(s.jobId);
                if (!Js.T(job))
                {
                    problems.push($"{Js.Str(s.name)} holds missing job {Js.Str(s.jobId)}");
                }
                else if (job.reservedBy != s.id)
                {
                    problems.push($"{Js.Str(s.name)} holds {Js.Str(job.id)} reserved by {Js.Str(job.reservedBy)}");
                }
            }
            if (Js.T(s.carrying))
            {
                var item = items.get(s.carrying);
                if (!Js.T(item))
                {
                    problems.push($"{Js.Str(s.name)} carries missing item {Js.Str(s.carrying)}");
                }
                else if (item.carriedBy != s.id)
                {
                    problems.push($"{Js.Str(s.name)} carries {Js.Str(item.id)} but it says carriedBy {Js.Str(item.carriedBy)}");
                }
                if ((!Js.T(s.jobId) && (s.meal?.itemId != s.carrying)) && (state.control?.survivorId != s.id))
                {
                    problems.push($"{Js.Str(s.name)} carries {Js.Str(s.carrying)} with no job or meal");
                }
            }
        }
        var perBlueprint = new JsMap<string, double>();
        foreach (var job in Js.Iter(state.jobs))
        {
            if (Js.T(job.reservedBy))
            {
                var s = survivors.get(job.reservedBy);
                if (!Js.T(s))
                {
                    problems.push($"{Js.Str(job.id)} reserved by missing survivor {Js.Str(job.reservedBy)}");
                }
                else if (s.jobId != job.id)
                {
                    problems.push($"{Js.Str(job.id)} reserved by {Js.Str(s.name)}, who is on {Js.Str(s.jobId)}");
                }
            }
            if (Js.T(job.blueprintId))
            {
                perBlueprint.set(job.blueprintId, (Js.Q(perBlueprint.get(job.blueprintId), 0)) + 1);
            }
        }
        foreach (var (bp, n) in Js.Iter(perBlueprint))
        {
            if (n > 1)
            {
                problems.push($"blueprint {Js.Str(bp)} has {Js.Str(n)} jobs");
            }
        }
        var occupied = new JsSet<string>();
        foreach (var item in Js.Iter(state.items))
        {
            if ((((Js.T(item.pos) ? 1 : 0) + (Js.T(item.carriedBy) ? 1 : 0)) + (Js.T(item.inside) ? 1 : 0)) != 1)
            {
                problems.push($"{Js.Str(item.id)} must be either on the ground, carried, or in a container");
            }
            if (Js.T(item.pos))
            {
                var key = $"{Js.Str(item.pos.x)},{Js.Str(item.pos.y)}";
                if (occupied.has(key))
                {
                    problems.push($"two items on ({Js.Str(key)})");
                }
                occupied.add(key);
                if (!Grids.isWalkable(grid, item.pos.x, item.pos.y))
                {
                    problems.push($"{Js.Str(item.id)} inside a wall at ({Js.Str(key)})");
                }
            }
            var bearer = Js.T(item.carriedBy) ? survivors.get(item.carriedBy) : null;
            if ((((((((Js.T(item.carriedBy) && (bearer?.carrying != item.id)) && (bearer?.weapon != item.id)) && (bearer?.body != item.id)) && (bearer?.feet != item.id)) && (bearer?.cart != item.id)) && (bearer?.tool != item.id)) && !Js.T(bearer?.cartLoad.includes(item.id))) && !Js.T(bearer?.pack.includes(item.id)))
            {
                problems.push($"{Js.Str(item.id)} says carriedBy {Js.Str(item.carriedBy)}, who isn't carrying it");
            }
            if (Js.T(item.reservedBy))
            {
                var holder = survivors.get(item.reservedBy);
                var job = Js.T(Js.T(holder?.jobId)) ? jobs.get(holder.jobId) : null;
                var @using = ((((holder?.meal?.itemId == item.id) || (holder?.treatment?.itemId == item.id)) || (holder?.arming == item.id)) || (holder?.dressing == item.id)) || (holder?.reading?.itemId == item.id);
                if ((job?.itemId != item.id) && !Js.T(@using))
                {
                    problems.push($"{Js.Str(item.id)} reserved by {Js.Str(item.reservedBy)} without a matching haul job, meal, treatment, or arming");
                }
            }
        }
        var destinations = new JsSet<string>();
        foreach (var job in Js.Iter(state.jobs))
        {
            if (!Js.T(job.dest) || !Js.T(job.reservedBy))
            {
                continue;
            }
            var key = $"{Js.Str(job.dest.x)},{Js.Str(job.dest.y)}";
            if (destinations.has(key))
            {
                problems.push($"two haulers headed for storage tile ({Js.Str(key)})");
            }
            destinations.add(key);
        }
        var furnitureTiles = new JsSet<string>();
        foreach (var f in Js.Iter(state.furniture))
        {
            var key = $"{Js.Str(f.x)},{Js.Str(f.y)}";
            if (furnitureTiles.has(key))
            {
                problems.push($"two pieces of furniture on ({Js.Str(key)})");
            }
            furnitureTiles.add(key);
            if (Grids.getTile(grid, f.x, f.y) != "floor")
            {
                problems.push($"{Js.Str(f.kind)} {Js.Str(f.id)} on non-floor ({Js.Str(key)})");
            }
            if (occupied.has(key))
            {
                problems.push($"item on {Js.Str(f.kind)} at ({Js.Str(key)})");
            }
        }
        foreach (var s in Js.Iter(state.survivors))
        {
            if (((s.rest < 0) || (s.rest > Rest.REST_MAX)) || !JsNumber.isInteger(s.rest))
            {
                problems.push($"{Js.Str(s.name)} has rest {Js.Str(s.rest)}");
            }
            if (((s.satiety < 0) || (s.satiety > Food.SATIETY_MAX)) || !JsNumber.isInteger(s.satiety))
            {
                problems.push($"{Js.Str(s.name)} has satiety {Js.Str(s.satiety)}");
            }
            if (((s.health <= 0) || (s.health > Health.HEALTH_MAX)) || !JsNumber.isInteger(s.health))
            {
                problems.push($"{Js.Str(s.name)} has health {Js.Str(s.health)}");
            }
            if (Js.T(s.treatment))
            {
                var bandage = items.get(s.treatment.itemId);
                if (!Js.T(bandage) || ((bandage.kind != "bandage") && (bandage.kind != "medicine")))
                {
                    problems.push($"{Js.Str(s.name)} is treating with missing bandage {Js.Str(s.treatment.itemId)}");
                }
                else if (bandage.reservedBy != s.id)
                {
                    problems.push($"{Js.Str(s.name)} uses {Js.Str(bandage.id)}, reserved by {Js.Str(bandage.reservedBy)}");
                }
                if (Js.T(s.jobId))
                {
                    problems.push($"{Js.Str(s.name)} is treating wounds but holds job {Js.Str(s.jobId)}");
                }
                if (Js.T(Js.T(s.sleep) || Js.T(s.meal)))
                {
                    problems.push($"{Js.Str(s.name)} is treating wounds while sleeping or eating");
                }
            }
            if (Js.T(s.weapon))
            {
                var weapon = items.get(s.weapon);
                if (!Js.T(weapon) || ((weapon.kind != "weapon") && (weapon.kind != "gun")))
                {
                    problems.push($"{Js.Str(s.name)} wields missing weapon {Js.Str(s.weapon)}");
                }
                else if (weapon.carriedBy != s.id)
                {
                    problems.push($"{Js.Str(s.name)} wields {Js.Str(weapon.id)}, carried by {Js.Str(weapon.carriedBy)}");
                }
            }
            if (Js.T(s.arming))
            {
                var weapon = items.get(s.arming);
                if (!Js.T(weapon) || (((weapon.kind != "weapon") && (weapon.kind != "gun")) && (weapon.kind != "ammo")))
                {
                    problems.push($"{Js.Str(s.name)} is fetching missing weapon {Js.Str(s.arming)}");
                }
                else if ((weapon.reservedBy != s.id) || !Js.T(weapon.pos))
                {
                    problems.push($"{Js.Str(s.name)} is fetching {Js.Str(weapon.id)}, which isn't reserved on the ground");
                }
                if (Js.T((Js.T((Js.T(Js.T(s.jobId) || Js.T(s.sleep))) || Js.T(s.meal))) || Js.T(s.treatment)))
                {
                    problems.push($"{Js.Str(s.name)} is fetching a weapon while busy with something else");
                }
            }
            foreach (var slot in Js.Iter((List<string>)["body", "feet"]))
            {
                var id = Js.GetProp<string?>(s, slot);
                var worn = Js.T(id) ? items.get(id) : null;
                if (Js.T(id) && (((!Js.T(worn) || (worn.carriedBy != s.id)) || !Clothing.isGarment(worn.kind)) || (Js.Get(Clothing.SLOT_OF, worn.kind) != slot)))
                {
                    problems.push($"{Js.Str(s.name)} wears {Js.Str(id)} on {Js.Str(slot)}, which doesn't fit");
                }
            }
            if (Js.T(s.dressing))
            {
                var item = items.get(s.dressing);
                if ((!Js.T(item) || (item.reservedBy != s.id)) || !Js.T(item.pos))
                {
                    problems.push($"{Js.Str(s.name)} is fetching {Js.Str(s.dressing)}, which isn't reserved on the ground");
                }
                if (Js.T((Js.T((Js.T((Js.T(Js.T(s.jobId) || Js.T(s.sleep))) || Js.T(s.meal))) || Js.T(s.treatment))) || Js.T(s.arming)))
                {
                    problems.push($"{Js.Str(s.name)} is fetching clothing while busy with something else");
                }
            }
            if (Js.T(s.leisure))
            {
                if (Js.T((Js.T((Js.T((Js.T((Js.T(Js.T(s.jobId) || Js.T(s.sleep))) || Js.T(s.meal))) || Js.T(s.treatment))) || Js.T(s.arming))) || Js.T(s.breakdown)))
                {
                    problems.push($"{Js.Str(s.name)} is relaxing while busy with something else");
                }
                if (!Grids.isWalkable(grid, s.leisure.x, s.leisure.y))
                {
                    problems.push($"{Js.Str(s.name)} relaxes on unwalkable ({Js.Str(s.leisure.x)}, {Js.Str(s.leisure.y)})");
                }
                if (state.survivors.some((o) => ((o != s) && (o.leisure?.x == Js.NN(s.leisure).x)) && (o.leisure.y == Js.NN(s.leisure).y)))
                {
                    problems.push($"{Js.Str(s.name)} shares a rec room spot");
                }
            }
            if ((s.infection != null) && ((s.infection < 0) || !JsNumber.isInteger(s.infection)))
            {
                problems.push($"{Js.Str(s.name)} has infection {Js.Str(s.infection)}");
            }
            foreach (var (id, v) in Js.Iter(Js.ObjectEntries(s.opinions)))
            {
                if (((!survivors.has(id) || (id == s.id)) || (v < -100)) || (v > 100))
                {
                    problems.push($"{Js.Str(s.name)} has an opinion {Js.Str(v)} of {Js.Str(id)}");
                }
            }
            if (Js.T(Js.T(s.breakdown) && Js.T(s.jobId)))
            {
                problems.push($"{Js.Str(s.name)} holds job {Js.Str(s.jobId)} mid-breakdown");
            }
            if (Js.T(s.meal))
            {
                var food = items.get(s.meal.itemId);
                if (!Js.T(food) || ((food.kind != "food") && (food.kind != "meal")))
                {
                    problems.push($"{Js.Str(s.name)} is eating missing food {Js.Str(s.meal.itemId)}");
                }
                else if (food.reservedBy != s.id)
                {
                    problems.push($"{Js.Str(s.name)} eats {Js.Str(food.id)}, reserved by {Js.Str(food.reservedBy)}");
                }
                if (Js.T(s.jobId))
                {
                    problems.push($"{Js.Str(s.name)} is eating but holds job {Js.Str(s.jobId)}");
                }
                if (Js.T(s.sleep))
                {
                    problems.push($"{Js.Str(s.name)} is eating and sleeping at once");
                }
                var seat = s.meal.seat;
                if (Js.T(seat) && state.survivors.some((o) => ((Js.T((o != s) && Js.T(Js.T(o.meal?.seat)))) && (o.meal.seat.x == seat.x)) && (o.meal.seat.y == seat.y)))
                {
                    problems.push($"{Js.Str(s.name)} shares mess hall seat ({Js.Str(seat.x)}, {Js.Str(seat.y)})");
                }
            }
            if (!Js.T(s.sleep))
            {
                continue;
            }
            if (Js.T(s.jobId))
            {
                problems.push($"{Js.Str(s.name)} is sleeping but holds job {Js.Str(s.jobId)}");
            }
            if (Js.T(s.carrying))
            {
                problems.push($"{Js.Str(s.name)} is sleeping while carrying {Js.Str(s.carrying)}");
            }
            if (Js.T(s.sleep.bedId))
            {
                var bed = state.furniture.find((f) => f.id == Js.NN(s.sleep).bedId);
                if (!Js.T(bed))
                {
                    problems.push($"{Js.Str(s.name)} sleeps in missing bed {Js.Str(s.sleep.bedId)}");
                }
                else if (bed.occupant != s.id)
                {
                    problems.push($"{Js.Str(s.name)} claims {Js.Str(bed.id)}, occupied by {Js.Str(bed.occupant)}");
                }
            }
        }
        var scavengeJobs = new JsMap<string, double>();
        foreach (var j in Js.Iter(state.jobs))
        {
            if (Js.T(j.furnitureId))
            {
                scavengeJobs.set(j.furnitureId, (Js.Q(scavengeJobs.get(j.furnitureId), 0)) + 1);
            }
        }
        foreach (var f in Js.Iter(state.furniture))
        {
            if ((f.loot < 0) || !JsNumber.isInteger(f.loot))
            {
                problems.push($"{Js.Str(f.id)} has loot {Js.Str(f.loot)}");
            }
            if (f.scavenge && !((f.kind == "tree") || ((f.kind == "crate") && (f.loot > 0))))
            {
                problems.push($"{Js.Str(f.id)} marked for scavenging with nothing to find");
            }
            if ((Js.Q(scavengeJobs.get(f.id), 0)) > 1)
            {
                problems.push($"{Js.Str(f.id)} has {Js.Str(scavengeJobs.get(f.id))} scavenge jobs");
            }
            if (Js.T(f.occupant) && (f.kind != "bed"))
            {
                problems.push($"{Js.Str(f.kind)} {Js.Str(f.id)} has an occupant");
            }
            if (Js.T(f.owner) && ((f.kind != "bed") || !survivors.has(f.owner)))
            {
                problems.push($"{Js.Str(f.kind)} {Js.Str(f.id)} owned by {Js.Str(f.owner)}, who isn't here");
            }
            if ((Js.T(Js.T(f.owner) && Js.T(f.occupant))) && (f.occupant != f.owner))
            {
                problems.push($"{Js.Str(f.owner)}'s bed {Js.Str(f.id)} is used by {Js.Str(f.occupant)}");
            }
            if (Js.T(f.owner) && state.furniture.some((g) => (g != f) && (g.owner == f.owner)))
            {
                problems.push($"{Js.Str(f.owner)} owns more than one bed");
            }
        }
        foreach (var f in Js.Iter(state.furniture))
        {
            if (!Js.T(f.occupant))
            {
                continue;
            }
            var s = survivors.get(f.occupant);
            if (s?.sleep?.bedId != f.id)
            {
                problems.push($"{Js.Str(f.id)} occupied by {Js.Str(f.occupant)}, who isn't sleeping in it");
            }
        }
        var zombieIds = new JsSet<string>();
        foreach (var z in Js.Iter(state.zombies))
        {
            if (zombieIds.has(z.id))
            {
                problems.push($"duplicate zombie id {Js.Str(z.id)}");
            }
            zombieIds.add(z.id);
            bool standable(double x, double y)
            {
                return Pathfinding.isPassable(grid, x, y, Zombies.moverOf(z));
            }
            if (!standable(z.pos.x, z.pos.y))
            {
                problems.push($"zombie {Js.Str(z.id)} on non-floor ({Js.Str(z.pos.x)}, {Js.Str(z.pos.y)})");
            }
            if (Js.T(z.move) && !standable(z.move.to.x, z.move.to.y))
            {
                problems.push($"zombie {Js.Str(z.id)} stepping off floor");
            }
            if ((z.health <= 0) || (z.health > z.maxHealth))
            {
                problems.push($"zombie {Js.Str(z.id)} has health {Js.Str(z.health)}");
            }
        }
        var guarded = new JsMap<string, double>();
        foreach (var j in Js.Iter(state.jobs))
        {
            if (j.type != "guard")
            {
                continue;
            }
            if (!Js.T(j.zombieId) || !zombieIds.has(j.zombieId))
            {
                problems.push($"{Js.Str(j.id)} hunts missing zombie {Js.Str(j.zombieId)}");
            }
            else
            {
                guarded.set(j.zombieId, (Js.Q(guarded.get(j.zombieId), 0)) + 1);
            }
        }
        foreach (var (id, n) in Js.Iter(guarded))
        {
            if (n > 1)
            {
                problems.push($"zombie {Js.Str(id)} has {Js.Str(n)} guard jobs");
            }
        }
        var plotTiles = new JsSet<string>();
        var tendedPlots = new JsMap<string, double>();
        foreach (var j in Js.Iter(state.jobs))
        {
            if (Js.T(j.plotId))
            {
                tendedPlots.set(j.plotId, (Js.Q(tendedPlots.get(j.plotId), 0)) + 1);
            }
        }
        foreach (var plot in Js.Iter(state.farm))
        {
            var key = $"{Js.Str(plot.x)},{Js.Str(plot.y)}";
            if (plotTiles.has(key))
            {
                problems.push($"two farm plots on ({Js.Str(key)})");
            }
            plotTiles.add(key);
            if (Grids.getTile(grid, plot.x, plot.y) != "floor")
            {
                problems.push($"farm plot {Js.Str(plot.id)} on non-floor ({Js.Str(key)})");
            }
            if (((plot.growth < 0) || (plot.growth > Farming.growTicks(plot))) || (!plot.planted && (plot.growth != 0)))
            {
                problems.push($"farm plot {Js.Str(plot.id)} has growth {Js.Str(plot.growth)}");
            }
            if ((Js.Q(tendedPlots.get(plot.id), 0)) > 1)
            {
                problems.push($"farm plot {Js.Str(plot.id)} has {Js.Str(tendedPlots.get(plot.id))} jobs");
            }
        }
        foreach (var w in Js.Iter(state.wanderers))
        {
            if (!Grids.isWalkable(grid, w.x, w.y))
            {
                problems.push($"wanderer {Js.Str(w.name)} on unwalkable ({Js.Str(w.x)}, {Js.Str(w.y)})");
            }
        }
        void listed(List<double> arr, List<double> list, string name)
        {
            var want = arr.flatMap((v, i) => (v != 0) ? ((List<double>)[i]) : ((List<double>)[]));
            var have = Js.ListOf(list).sort((a, b) => a - b);
            if ((want.length != have.length) || want.some((v, k) => v != Js.Get(have, k)))
            {
                problems.push($"{Js.Str(name)} list out of step with the map");
            }
        }
        listed(grid.dirt.Select(v => (double)v).ToList(), grid.dirtList.Select(v => (double)v).ToList(), "dirt");
        listed(grid.damage, grid.damageList.Select(v => (double)v).ToList(), "damage");
        for (var i = 0; i < grid.damage.length; i++)
        {
            var d = Js.NN(Js.Get(grid.damage, i));
            if (d == 0)
            {
                continue;
            }
            var tile = Js.NN(Js.Get(grid.tiles, i));
            var at = $"({Js.Str(i % grid.width)}, {Js.Str(JsMath.floor(((double)(i) / (grid.width))))})";
            if (((tile != "wall") && (tile != "door")) && (tile != "barricade"))
            {
                problems.push($"damage {Js.Str(d)} on {Js.Str(tile)} {Js.Str(at)}");
            }
            else if (((d < 0) || (d >= Breach.structureHealth(grid, i))) || !JsNumber.isInteger(d))
            {
                problems.push($"{Js.Str(tile)} at {Js.Str(at)} has damage {Js.Str(d)}");
            }
        }
        var fireTiles = new JsSet<string>();
        foreach (var f in Js.Iter(state.fires))
        {
            var key = $"{Js.Str(f.x)},{Js.Str(f.y)}";
            if ((fireTiles.has(key) || !Grids.inBounds(grid, f.x, f.y)) || (f.left <= 0))
            {
                problems.push($"fire at ({Js.Str(key)}) is doubled, off the map or out");
            }
            fireTiles.add(key);
        }
        foreach (var s in Js.Iter(state.survivors))
        {
            if (((s.hydration < 0) || (s.hydration > Water.HYDRATION_MAX)) || !JsNumber.isInteger(s.hydration))
            {
                problems.push($"{Js.Str(s.name)} has hydration {Js.Str(s.hydration)}");
            }
            if (Js.T(Js.T(s.drink) && (Js.T((Js.T((Js.T((Js.T((Js.T(Js.T(s.jobId) || Js.T(s.sleep))) || Js.T(s.meal))) || Js.T(s.treatment))) || Js.T(s.arming))) || Js.T(s.dressing)))))
            {
                problems.push($"{Js.Str(s.name)} is drinking while busy with something else");
            }
        }
        foreach (var s in Js.Iter(state.survivors))
        {
            if (Js.T(s.reading))
            {
                var book = items.get(s.reading.itemId);
                if (((!Js.T(book) || (book.kind != "book")) || (book.reservedBy != s.id)) || !Js.T(book.pos))
                {
                    problems.push($"{Js.Str(s.name)} is reading {Js.Str(s.reading.itemId)}, which isn't a reserved book on the ground");
                }
                if (Js.T((Js.T((Js.T((Js.T((Js.T((Js.T((Js.T(Js.T(s.jobId) || Js.T(s.sleep))) || Js.T(s.meal))) || Js.T(s.treatment))) || Js.T(s.arming))) || Js.T(s.dressing))) || Js.T(s.drink))) || Js.T(s.leisure)))
                {
                    problems.push($"{Js.Str(s.name)} is reading while busy with something else");
                }
            }
            if ((s.bitten != null) && (s.infection == null))
            {
                problems.push($"{Js.Str(s.name)} has a bite with no infection");
            }
            if (Js.T((s.childUntil != null) && Js.T(s.jobId)))
            {
                problems.push($"{Js.Str(s.name)} is a child with job {Js.Str(s.jobId)}");
            }
            if ((s.cartLoad.length > 0) && ((!Js.T(s.jobId) || !Js.T(s.cart)) || !Js.T(s.carrying)))
            {
                problems.push($"{Js.Str(s.name)} has a loaded cart with no haul on");
            }
            if (Js.T(s.cart) && ((items.get(s.cart)?.kind != "cart") || (items.get(s.cart)?.carriedBy != s.id)))
            {
                problems.push($"{Js.Str(s.name)} pushes {Js.Str(s.cart)}, which isn't their cart");
            }
        }
        foreach (var a in Js.Iter(state.animals))
        {
            if (Grids.getTile(grid, a.pos.x, a.pos.y) != "floor")
            {
                problems.push($"animal {Js.Str(a.id)} off the floor");
            }
        }
        foreach (var p in Js.Iter(state.prisoners))
        {
            if (!Grids.isWalkable(grid, p.x, p.y))
            {
                problems.push($"prisoner {Js.Str(p.id)} in a wall");
            }
        }
        foreach (var d in Js.Iter(state.dogs))
        {
            if (!Grids.isWalkable(grid, d.pos.x, d.pos.y) || (d.health <= 0))
            {
                problems.push($"dog {Js.Str(d.id)} is in a wall or dead");
            }
        }
        foreach (var s in Js.Iter(state.survivors))
        {
            if ((s.sick != null) && ((s.sick <= 0) || !JsNumber.isInteger(s.sick)))
            {
                problems.push($"{Js.Str(s.name)} has sickness {Js.Str(s.sick)}");
            }
        }
        foreach (var i in Js.Iter(grid.reinforced))
        {
            if (((Js.Get(grid.tiles, i) != "wall") && (Js.Get(grid.tiles, i) != "door")) && (Js.Get(grid.tiles, i) != "barricade"))
            {
                problems.push($"reinforcement on a {Js.Str(Js.Get(grid.tiles, i))} at {Js.Str(i)}");
            }
        }
        foreach (var i in Js.Iter(grid.locked))
        {
            if (Js.Get(grid.tiles, i) != "door")
            {
                problems.push($"lock on a {Js.Str(Js.Get(grid.tiles, i))} at {Js.Str(i)}");
            }
        }
        foreach (var d in Js.Iter(state.dormant))
        {
            if (!Grids.inBounds(grid, d.x, d.y))
            {
                problems.push($"dormant zombie {Js.Str(d.id)} off the map");
            }
        }
        foreach (var h in Js.Iter(state.herds))
        {
            if ((!Grids.inBounds(grid, h.x, h.y) || !Grids.inBounds(grid, h.to.x, h.to.y)) || (h.size <= 0))
            {
                problems.push($"herd {Js.Str(h.id)} is off the map or empty");
            }
        }
        foreach (var a in Js.Iter(state.exploreAreas))
        {
            if ((((((a.x0 > a.x1) || (a.y0 > a.y1)) || (a.x0 < 0)) || (a.y0 < 0)) || (a.x1 >= grid.width)) || (a.y1 >= grid.height))
            {
                problems.push($"explore area {Js.Str(a.id)} is not a box on the map");
            }
        }
        var roomsClaimed = new JsSet<double>();
        foreach (var d in Js.Iter(state.roomDesignations))
        {
            if (Grids.getTile(grid, d.x, d.y) != "floor")
            {
                problems.push($"room designation {Js.Str(d.id)} anchored on non-floor");
            }
            var label = Rooms.labelAt(state, d.x, d.y);
            if ((label >= 0) && roomsClaimed.has(label))
            {
                problems.push($"room {Js.Str(label)} has two designations");
            }
            roomsClaimed.add(label);
        }
        grid.dirt.forEach((d, i) =>
        {
            if ((d == 1) && (Grids.getTile(grid, i % grid.width, JsMath.floor(((double)(i) / (grid.width)))) != "floor"))
            {
                problems.push($"dirt on non-floor tile {Js.Str(i)}");
            }
        });
        return problems;
    }
}
