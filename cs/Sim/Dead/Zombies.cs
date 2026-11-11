// Ported from src/sim/zombies.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Zombies: crude night pressure (milestone 7).
//
// Each night brings one more zombie than the last (2 on the first night, plus
// one per SURVIVORS_PER_EXTRA_ZOMBIE survivors), each a little tougher,
// spread evenly through the night (in groups from night 4), each in the dark past explored ground a
// little way from someone, or at the map's edge once all is explored (spawnPoint; sim RNG). Each
// shambles toward the nearest
// survivor it can reach, and hits an adjacent survivor every ATTACK_EVERY
// ticks. Zombies only walk on floor: walls and doors stop them. With nobody
// reachable, a zombie heads for the nearest survivor anyway, through doors
// if it can and through walls if it must, battering whatever is in the way
// (Breach.cs). With no survivors at all they wander. At dawn they all
// shamble off. Guards (Jobs.cs) hunt them down.
//
// Moving (2026-11-08): zombies are free movers. They own `at` and `pos` is the tile under it.
// With a target within CHASE_RANGE and a straight walk to them (Motion.walkLine: no wall, shut door,
// window or water on the line), a zombie comes straight at where they are (chase), sliding along
// walls. Otherwise it follows its tile path smoothed: straight at the furthest of the next
// LOOKAHEAD waypoints it can walk to (aimAhead), and tile by tile only where the next step
// needs it (stepTile: battering, a raider opening a door, climbing through a broken window).
// Parked cars and barricades can be crossed, slowly (Crossings.cs: begun from stepBlocked, ticked first thing).
//
// This file: the dead's numbers, the tick (updateZombies, updateAll, updateZombie), how one moves, plans, notices,
// hits and is hurt. Beside it, parts of the same class: ZombieSpawn.cs (how many arrive each night, when, where)
// and ZombieCrowd.cs (who is near whom: zombiesWithin, survivorsNear, pushApart).
#nullable disable warnings
using System.Runtime.CompilerServices;

namespace LoonieRoo.Sim;

public static partial class Zombies
{

    /// <summary>A night-1 zombie's health; later nights add ZOMBIE_HEALTH_PER_NIGHT each (balance pass 2; 20 -&gt; 10 in pass 3).</summary>
    public const int ZOMBIE_HEALTH = 200;

    public const int ZOMBIE_HEALTH_PER_NIGHT = 10;

    /// <summary>One more zombie each night per this many survivors, counted at nightfall (balance pass 2).</summary>
    public const int SURVIVORS_PER_EXTRA_ZOMBIE = 4;

    /// <summary>Slower than survivors (4), so guards can catch them and runners can escape.</summary>
    public const int ZOMBIE_TICKS_PER_TILE = 6;

    public const int ZOMBIE_DAMAGE = 40;

    public const int ZOMBIE_ATTACK_EVERY = 10;

    /// <summary>A bite is wound up this many ticks first (shown), and lands only if they're still in reach. Part of ZOMBIE_ATTACK_EVERY, so biting keeps its rhythm; only the first bite comes later.</summary>
    public const int WINDUP_TICKS = 4;

    /// <summary>A blow from the survivor you control staggers a zombie this long (no biting, no moving) and shoves it back SHOVE (brutes half as far).</summary>
    public const int STAGGER_TICKS = 6;

    public const int SHOVE = 96;

    /// <summary>Zombies on night 1; every night after brings one more. (Was 4: too much for night one.)</summary>
    public const int FIRST_NIGHT_ZOMBIES = 2;

    /// <summary>Safety cap on zombies alive at once (not a balance lever).</summary>
    public const int MAX_ZOMBIES = 60;

    /// <summary>Static and herd zombies only go for survivors this close (Manhattan), and only ones they see or hear (Sight.cs); night zombies go for anyone.</summary>
    public const int AGGRO_RADIUS = 20;

    /// <summary>Night zombies arrive in the dark this far (Manhattan, roughly) from a survivor, past explored ground.</summary>
    public const int FRONTIER_MIN = 20;

    public const int FRONTIER_MAX = 32;

    /// <summary>Zombies never spawn this close (Manhattan) to a survivor.</summary>
    public const int SPAWN_MIN_DISTANCE = 8;

    /// <summary>Zombies re-pick their target and path this often.</summary>
    public const int REPATH_EVERY = 15;

    /// <summary>At most this many zombies plan a path in one tick; the rest keep to their old path (or wait a tick) and plan when there's room. Profiling: 144 zombies replanning together cost 14-20 ms in one tick; spread out, the same work never stalls a frame. Deterministic: first come in state order.</summary>
    public const int PLANS_PER_TICK = 20;

    /// <summary>Survivors won't go to bed with a zombie this close (Manhattan); see Needs.cs.</summary>
    public const int DANGER_RADIUS = 10;

    public static Zombie? getZombie(SimState state, string id)
    {
        return Lookup.findById(state.zombies, id);
    }

    public static Zombie addZombie(SimState state, int x, int y, double health = ZOMBIE_HEALTH, string kind = "night")
    {
        Zombie z = new Zombie { id = $"z{Js.Str(state.nextId++)}", pos = new Point { x = Js.I(x), y = Js.I(y) }, move = null, path = [], targetId = null, health = health, maxHealth = health, cooldown = 0, nextThinkTick = 0, kind = kind, heading = null, variant = "walker", screamed = false, stuckUntil = 0, windup = 0, staggerUntil = 0, hurtTick = -1, at = Motion.placedAt(new Point { x = Js.I(x), y = Js.I(y) }).at, was = Motion.placedAt(new Point { x = Js.I(x), y = Js.I(y) }).was };
        state.zombies.push(z);
        edited(state.zombies);
        return z;
    }
    public static Zombie addZombie(SimState state, double x, double y, double health = ZOMBIE_HEALTH, string kind = "night") => addZombie(state, Js.I(x), Js.I(y), health, kind);

    public static void removeZombie(SimState state, Zombie z)
    {
        var i = state.zombies.indexOf(z);
        if (i >= 0)
        {
            state.zombies.splice(i, 1);
            edited(state.zombies);
        }
    }

    /// <summary>Any zombie within `radius` (Manhattan) of a point.</summary>
    public static bool zombieNear(SimState state, Point p, double radius = DANGER_RADIUS)
    {
        return state.zombies.some((z) => Space.manhattan(z.pos, p) <= radius);
    }

    /// <summary>Raiders open unlocked doors; zombies don't open any; crawlers get under barricades.</summary>
    public static string moverOf(Zombie z)
    {
        return (z.kind == "raider") ? "raider" : ((z.variant == "crawler") ? "crawler" : "zombie");
    }

    /// <summary>Spawning, the dawn retreat, and every zombie's move or attack. Runs first each tick.</summary>
    public static void updateZombies(SimState state)
    {
        if (!Time.isNight(state.tick))
        {
            state.zombiesTonight = null;
            bool leaves(Zombie z)
            {
                return (z.kind == "night") || (z.kind == "raider");
            }
            var n = state.zombies.filter((__f0) => leaves(__f0)).length;
            if (n > 0)
            {
                state.zombies = state.zombies.filter((z) => !leaves(z));
                Events.logEvent(state, $"{Js.Str(n)} zombie{Js.Str((n == 1) ? Js.Str("") : Js.Str("s"))} shambled off at dawn");
            }
            updateAll(state);
            return;
        }
        var night = Js.NN(Time.nightNumber(state.tick));
        if (Time.ticksIntoNight(state.tick) == 0)
        {
            state.zombiesTonight = JsMath.max(1, Difficulty.zombieScale(state, JsMath.round(((double)(((double)(((double)(zombiesForNight(night, state.survivors.length)) * (Director.threatScale(state)))) * (EndgameMod.siegeFactor(state, night)))) * ((state.harsh ? groupShare(state.survivors.length) : 1))))));
            var siege = EndgameMod.siegeFactor(state, night) > 1;
            Events.logEvent(state, $"Night {Js.Str(night)} falls: {Js.Str(state.zombiesTonight)} zombies expected{Js.Str(siege ? Js.Str(" (a SIEGE night)") : Js.Str(""))}");
        }
        var arriving = JsMath.min(spawnsAt(state.tick, tonight(state, night)), MAX_ZOMBIES - state.zombies.length);
        if (arriving > 0)
        {
            spawnGroup(state, arriving, zombieHealthForNight(night));
        }
        updateAll(state);
    }

    public static void updateAll(SimState state)
    {
        Hordes.updateHordes(state);
        Budget budget = new Budget { plans = PLANS_PER_TICK, near = survivorsNear(state) };
        foreach (var z in Js.Iter(state.zombies))
        {
            updateZombie(state, z, budget);
        }
        pushApart(state);
        foreach (var z in Js.Iter(state.zombies))
        {
            if ((Js.T((z.kind == "static") && Js.T(z.heading))) && (Space.manhattan(z.pos, z.heading) <= 1))
            {
                z.heading = null;
            }
        }
        bool gone(Zombie z)
        {
            return ((Js.T((z.kind == "herd") && Js.T(z.heading))) && (Space.manhattan(z.pos, z.heading) <= 1)) && !Js.T(z.move);
        }
        if (state.zombies.some((__f0) => gone(__f0)))
        {
            state.zombies = state.zombies.filter((z) => !gone(z));
        }
    }

    public static void updateZombie(SimState state, Zombie z, Budget budget)
    {
        z.cooldown = JsMath.max(0, z.cooldown - 1);
        z.move = null;
        // On its way over a car or a barricade (Crossings.cs): no biting, no steering; a blow holds it where it is.
        if ((z.crossing != null) && ((state.tick < z.staggerUntil) || Crossings.tick(state, z)))
        {
            return;
        }
        Motion.followTile(z, 1);
        if (state.tick < z.stuckUntil)
        {
            return;
        }
        if (state.tick < z.staggerUntil)
        {
            return;
        }
        {
            // The target if in reach, else the first in reach (no list made: most zombies have nobody in reach).
            Survivor? victim = null, first = null;
            foreach (var s in Js.Iter(budget.near(z.at)))
            {
                if ((JsMath.abs(s.at.x - z.at.x) > Motion.REACH) || (JsMath.abs(s.at.y - z.at.y) > Motion.REACH))
                {
                    continue;
                }
                if (Motion.inReach(s, z, Motion.REACH, state.grid))
                {
                    first ??= s;
                    if (victim == null && s.id == z.targetId) victim = s;
                }
            }
            victim ??= first;
            if (Js.T(victim))
            {
                if (z.windup > 0)
                {
                    z.windup -= 1;
                    if ((z.windup == 0) && !Vehicles.batterVehicle(state, victim))
                    {
                        hit(state, z, victim);
                    }
                }
                else if (z.cooldown == 0)
                {
                    z.windup = WINDUP_TICKS;
                }
                return;
            }
            if (z.windup > 0)
            {
                z.windup = 0;
                z.cooldown = WINDUP_TICKS;
            }
            Dog? dog = null;
            foreach (var d in state.dogs)
            {
                if (Motion.inReach(d, z, Motion.REACH, state.grid))
                {
                    dog = d;
                    break;
                }
            }
            if (Js.T(dog))
            {
                if (z.cooldown == 0)
                {
                    Dogs.hitDog(state, dog, (z.kind == "raider") ? Visitors.RAIDER_DAMAGE : ZOMBIE_DAMAGE);
                    z.cooldown = ZOMBIE_ATTACK_EVERY;
                }
                return;
            }
        }
        var speed = JsMath.ceil(((double)(Motion.SUB) / (Js.Get(ZombieKinds.VARIANTS, z.variant).ticksPerTile)));
        if (enter(state, z))
        {
            return;
        }
        var target = chaseTarget(state, z);
        var horde = Hordes.getHorde(state, z.hordeId);
        var planned = false;
        if (Js.T(horde) && (z.path.length == 0))
        {
            if (!Js.T(target))
            {
                var step = Hordes.hordeStep(state, z, horde);
                z.path = Js.T(step) ? ((List<Point>)[step]) : ((List<Point>)[]);
            }
        }
        else if (((state.tick >= z.nextThinkTick) || ((z.path.length == 0) && !Js.T(target))) && (budget.plans > 0))
        {
            budget.plans -= 1;
            plan(state, z);
            z.nextThinkTick = state.tick + REPATH_EVERY;
            planned = true;
            target = chaseTarget(state, z);
        }
        if (Js.T(target))
        {
            chase(state, z, target, speed);
            return;
        }
        if (planned)
        {
            aimAhead(state, z);
        }
        followPath(state, z, speed);
    }

    /// <summary>Chasing: a zombie with a target this near (SUB units, centre to centre: as far as static zombies notice anyone) and a straight walk to them comes straight at where they are, not along its tile path.</summary>
    public const int CHASE_RANGE = AGGRO_RADIUS * Motion.SUB;

    /// <summary>Following a path, a zombie heads for the furthest of this many waypoints ahead that it can walk straight to, rather than visiting every tile centre.</summary>
    public const int LOOKAHEAD = 12;

    /// <summary>A waypoint this near (SUB units) counts as reached.</summary>
    public const int ARRIVED = 64;

    /// <summary>Zombies push apart to this far, centre to centre.</summary>
    public static readonly double CROWD = ((double)(2) * (Motion.BODY_RADIUS));

    /// <summary>Move a zombie's centre towards a point; true once it's there.</summary>
    public static bool glide(Zombie z, Point to, double speed)
    {
        var dx = to.x - z.at.x;
        var dy = to.y - z.at.y;
        if ((((double)(dx) * (dx)) + ((double)(dy) * (dy))) <= ((double)(speed) * (speed)))
        {
            z.at = new Point { x = Js.I(to.x), y = Js.I(to.y) };
            return true;
        }
        var v = Motion.toward(dx, dy, speed);
        z.at = new Point { x = Js.I(z.at.x + v.x), y = Js.I(z.at.y + v.y) };
        return false;
    }

    /// <summary>The pos follows the tile under `at` (free movers own `at`); stepping onto a new tile can spring a trap or drop it in a pit. True if a trap took it.</summary>
    public static bool enter(SimState state, Zombie z)
    {
        // Motion.tileOf, made only when it differs.
        var tx = Js.I(JsMath.floor(((double)(z.at.x) / (Motion.SUB))));
        var ty = Js.I(JsMath.floor(((double)(z.at.y) / (Motion.SUB))));
        if ((tx == z.pos.x) && (ty == z.pos.y))
        {
            return false;
        }
        z.pos = new Point { x = tx, y = ty };
        if (Defences.springTrap(state, z))
        {
            return true;
        }
        Defences.fallInPit(state, z);
        return false;
    }

    /// <summary>The mover a zombie's straight lines are checked for: raiders open doors as they reach them, so their straight lines stop at shut ones (and go by the path, which opens them).</summary>
    public static string lineMover(Zombie z)
    {
        return (z.kind == "raider") ? Pathfinding.ZOMBIE : moverOf(z);
    }

    /// <summary>The survivor this zombie is after, if near enough (CHASE_RANGE) with a straight walk to them.</summary>
    public static Survivor? chaseTarget(SimState state, Zombie z)
    {
        var target = Js.T(z.targetId) ? Lookup.findById(state.survivors, z.targetId) : null;
        if (!Js.T(target) || (Motion.distance2(target.at, z.at) > ((double)(CHASE_RANGE) * (CHASE_RANGE))))
        {
            return null;
        }
        return Motion.walkLine(state.grid, z.at, target.at, lineMover(z), true) ? target : null;
    }

    /// <summary>Come straight at where the target is (not their tile), sliding along walls, stopping just inside reach. The tile path is dropped: once the line is lost, it plans afresh.</summary>
    public static void chase(SimState state, Zombie z, Survivor target, double speed)
    {
        var d = JsMath.sqrt(Motion.distance2(target.at, z.at));
        var step = JsMath.min(speed, JsMath.max(0, JsMath.floor(d - (Motion.REACH - 16))));
        if (step == 0)
        {
            return;
        }
        var v = Motion.toward(target.at.x - z.at.x, target.at.y - z.at.y, step);
        z.at = Motion.slide(state, z.at, v.x, v.y, moverOf(z));
        z.path = [];
        enter(state, z);
    }

    /// <summary>Skip waypoints: head for the furthest of the next LOOKAHEAD it can walk straight to (dropping those before it).</summary>
    public static void aimAhead(SimState state, Zombie z)
    {
        var last = JsMath.min(LOOKAHEAD, z.path.length) - 1;
        for (var j = last; j >= 1; j--)
        {
            if (Motion.walkLine(state.grid, z.at, Motion.centre(Js.NN(Js.Get(z.path, j))), lineMover(z), false))
            {
                z.path = z.path.slice(j);
                return;
            }
        }
    }

    /// <summary>Follow the tile path: straight at the next waypoint while it can walk there (sliding along walls), and tile by tile where it can't (a door or a wall to batter, a broken window to climb).</summary>
    public static void followPath(SimState state, Zombie z, double speed)
    {
        // A waypoint it's already standing on is behind it (a crowd keeps it off the centre).
        while ((z.path.length > 1) && (Js.NN(Js.Get(z.path, 0)).x == z.pos.x) && (Js.NN(Js.Get(z.path, 0)).y == z.pos.y))
        {
            z.path.shift();
        }
        var next = Js.Get(z.path, 0);
        if (!Js.T(next))
        {
            return;
        }
        var aim = Motion.centre(next);
        if (Motion.walkLine(state.grid, z.at, aim, lineMover(z), false))
        {
            var dx = aim.x - z.at.x;
            var dy = aim.y - z.at.y;
            var v = ((((double)(dx) * (dx)) + ((double)(dy) * (dy))) <= ((double)(speed) * (speed))) ? new Point { x = Js.I(dx), y = Js.I(dy) } : Motion.toward(dx, dy, speed);
            z.at = Motion.slide(state, z.at, v.x, v.y, moverOf(z));
            var was = z.pos;
            var reached = (Motion.distance2(z.at, aim) <= ((double)(ARRIVED) * (ARRIVED))) || Trunks.against(state, z.at, next, Motion.BODY_RADIUS, 16);  // a tree's tile: its trunk is as near as it gets
            if (reached)
            {
                z.path.shift();
            }
            if (enter(state, z))
            {
                return;
            }
            if (reached || (was.x != z.pos.x) || (was.y != z.pos.y))
            {
                aimAhead(state, z);
            }
            return;
        }
        stepTile(state, z, next, speed);
    }

    /// <summary>A step to the next tile the old way, from the centre of its own: battering what's shut, opening doors (raiders), climbing.</summary>
    public static void stepTile(SimState state, Zombie z, Point next, double speed)
    {
        var home = Motion.centre(z.pos);
        var adjacent = Space.manhattan(z.pos, next) == 1;
        var open = adjacent && Pathfinding.crossable(state.grid, z.pos.x, z.pos.y, next.x, next.y, moverOf(z)) && Pathfinding.isPassable(state.grid, next.x, next.y, moverOf(z));
        // Battering needs only to be at the thing (a crowd at a door jostles), so near the tile's centre will do.
        if (adjacent && !open && (Motion.distance2(z.at, home) <= ((double)(ARRIVED) * (ARRIVED))))
        {
            stepBlocked(state, z, next);
            return;
        }
        if ((z.at.x != home.x) || (z.at.y != home.y))
        {
            // Part way across already (climbing in through a window, say): carry on, if the way is still open.
            var onTheWay = open && ((next.x != z.pos.x) ? ((z.at.y == home.y) && (JsMath.sign(z.at.x - home.x) == (next.x - z.pos.x))) : ((z.at.x == home.x) && (JsMath.sign(z.at.y - home.y) == (next.y - z.pos.y))));
            if (!onTheWay)
            {
                var v = Motion.toward(home.x - z.at.x, home.y - z.at.y, JsMath.min(speed, JsMath.sqrt(Motion.distance2(home, z.at))));
                z.at = Motion.slide(state, z.at, v.x, v.y, moverOf(z));
                if (!adjacent)
                {
                    z.path = [];
                }
                enter(state, z);
                return;
            }
        }
        if (!adjacent)
        {
            z.path = [];
            return;
        }
        if (stepBlocked(state, z, next))
        {
            return;
        }
        var from = z.pos;
        if (glide(z, Motion.centre(next), climbing(state, from, next) ? JsMath.ceil(((double)(speed) / (2))) : speed))
        {
            z.path.shift();
        }
        enter(state, z);
    }

    /// <summary>The next tile can't be walked into from here: batter what's in the way (or give up the path). Raiders open doors. True if it took the tick.</summary>
    public static bool stepBlocked(SimState state, Zombie z, Point next)
    {
        if (!Pathfinding.crossable(state.grid, z.pos.x, z.pos.y, next.x, next.y, moverOf(z)))
        {
            if (Crossings.begin(state, z, next))
            {
                return true; // a runner vaults a barricade
            }
            var e = Grids.edgeBetween(state.grid, z.pos.x, z.pos.y, next.x, next.y);
            if ((e >= 0) && (z.cooldown == 0))
            {
                Breach.damageEdge(state, e, ((double)(ZOMBIE_DAMAGE) * (Js.Get(ZombieKinds.VARIANTS, z.variant).battering)));
                z.cooldown = ZOMBIE_ATTACK_EVERY;
            }
            return true;
        }
        if (!Pathfinding.isPassable(state.grid, next.x, next.y, moverOf(z)))
        {
            if (Crossings.begin(state, z, next))
            {
                return true; // over a parked car (a crawler under a pickup), a runner over a barricade
            }
            if (Breach.isStructure(Grids.getTile(state.grid, next.x, next.y)))
            {
                if (z.cooldown == 0)
                {
                    if (((z.kind == "raider") && (Hash.hashRoll(Hash.idNumber(z.id), state.tick) < FireMod.FIREBOMB_CHANCE)) && FireMod.ignite(state, next.x, next.y))
                    {
                        Events.logEvent(state, $"Raiders firebombed the {Js.Str(Grids.getTile(state.grid, next.x, next.y))} at ({Js.Str(next.x)}, {Js.Str(next.y)})");
                    }
                    Breach.damageStructure(state, next.x, next.y, ((double)(ZOMBIE_DAMAGE) * (Js.Get(ZombieKinds.VARIANTS, z.variant).battering)));
                    z.cooldown = ZOMBIE_ATTACK_EVERY;
                }
                return true;
            }
            z.path = [];
            return true;
        }
        if (z.kind == "raider")
        {
            Doors.passThrough(state, next.x, next.y, false, z.pos);
        }
        return false;
    }

    /// <summary>Chase the nearest reachable survivor; failing that, break in to the nearest one; failing that, a herd zombie walks on towards where the herd was going, and others wander. Static and herd zombies only notice survivors within AGGRO_RADIUS that they see or hear (Sight.cs), or the one they were already after; raiders anyone within AGGRO_RADIUS.</summary>
    public static void plan(SimState state, Zombie z)
    {
        var __d6 = state;
        var grid = __d6.grid;
        var v = __d6.walkabilityVersion;
        List<Point> goalsFor(Survivor s)
        {
            return [s.pos, .. Grids.neighbours4(grid, s.pos.x, s.pos.y)];
        }
        var noticed = (z.kind == "night") ? state.survivors : state.survivors.filter((s) => notices(state, z, s));
        // Its paths go over what its body can't walk through (a parked car; a runner, barricades): Crossings.cs.
        var mover = Crossings.planner(z);
        var target = noticed.slice().sort((a, b) => Space.manhattan(z.pos, a.pos) - Space.manhattan(z.pos, b.pos)).find((s) => Pathfinding.canReach(grid, v, z.pos, goalsFor(s), mover));
        z.targetId = target?.id ?? null;
        if ((Js.T(target) && (z.variant == "screamer")) && !z.screamed)
        {
            z.screamed = true;
            Events.logEvent(state, $"A screamer shrieked at ({Js.Str(z.pos.x)}, {Js.Str(z.pos.y)})!");
            Noise.makeNoise(state, z.pos, "scream");
        }
        if (Js.T(target))
        {
            z.path = Pathfinding.findPathToAny(grid, z.pos, goalsFor(target), mover) ?? [];
            return;
        }
        var victim = Js.Get(noticed.slice().sort((a, b) => Space.manhattan(z.pos, a.pos) - Space.manhattan(z.pos, b.pos)), 0);
        if (Js.T(victim))
        {
            var path = Pathfinding.findPathToAny(grid, z.pos, goalsFor(victim), "survivor") ?? Pathfinding.findPathToAny(grid, z.pos, goalsFor(victim), "breacher");
            if (Js.T(path) && (path.length > 0))
            {
                z.targetId = victim.id;
                z.path = path;
                return;
            }
        }
        z.targetId = null;
        var options = Grids.neighbours4(grid, z.pos.x, z.pos.y).filter((p) => Pathfinding.isPassable(grid, p.x, p.y, moverOf(z)));
        if (Js.T(z.heading))
        {
            var path = Pathfinding.canReach(grid, v, z.pos, [z.heading], "zombie") ? Pathfinding.findPathToAny(grid, z.pos, [z.heading], "zombie") : null;
            if (Js.T(path) && (path.length > 0))
            {
                z.path = path.slice(0, REPATH_EVERY);
                return;
            }
            var best = Js.Get(options.sort((a, b) => Space.manhattan(a, Js.NN(z.heading)) - Space.manhattan(b, Js.NN(z.heading))), 0);
            z.path = (Js.T(best) && (Space.manhattan(best, z.heading) < Space.manhattan(z.pos, z.heading))) ? ((List<Point>)[best]) : ((List<Point>)[]);
            return;
        }
        z.path = (options.length > 0) ? [Rng.pick(state, options)] : [];
    }

    /// <summary>Does a (static, herd or raider) zombie notice this survivor?</summary>
    public static bool noticesSurvivor(SimState state, Zombie z, Survivor s)
    {
        return notices(state, z, s);
    }

    public static bool notices(SimState state, Zombie z, Survivor s)
    {
        var d = Space.manhattan(z.pos, s.pos);
        if (d > AGGRO_RADIUS)
        {
            return false;
        }
        if ((z.kind == "raider") || (z.targetId == s.id))
        {
            return true;
        }
        return (d <= Sight.heardFrom(state, s)) || Sight.zombieSees(state, z.pos, s.pos, Sight.isSneaking(state, s));
    }

    public static void hit(SimState state, Zombie z, Survivor s)
    {
        var damage = (z.kind == "raider") ? Visitors.RAIDER_DAMAGE : Js.Get(ZombieKinds.VARIANTS, z.variant).damage;
        var armoured = Clothing.wearing(state, s, "armour") != null;
        var limb = Wounds.maybeWound(state, s, z.id);
        s.health = JsMath.max(0, s.health - Clothing.armourDamage(state, s, Skills.hasTrait(s, "tough") ? JsMath.round(((double)(((double)(damage) * (3))) / (4))) : damage));
        s.lastHurtBy = (z.kind == "raider") ? "a raider" : "a zombie attack";
        s.hurtTick = state.tick;
        s.note = $"attacked by a zombie at ({Js.Str(z.pos.x)}, {Js.Str(z.pos.y)})";
        Mood.addThought(state, s, "attacked");
        Mood.addThought(state, s, "shaken");
        BodyMod.frighten(s, BodyMod.HIT_PANIC);
        BodyMod.startle(state, s);
        if (z.kind != "raider")
        {
            var was = s.infection;
            Infection.maybeInfect(state, s, z.id, armoured ? ((double)(Infection.INFECTION_CHANCE) / (2)) : Infection.INFECTION_CHANCE);
            if ((was == null) && (s.infection != null))
            {
                s.bitten = limb;
            }
        }
        z.cooldown = ZOMBIE_ATTACK_EVERY - WINDUP_TICKS;
    }

    /// <summary>A blow from you: the zombie reels (no biting or moving for STAGGER_TICKS, any windup lost) and is shoved away from `from`, sliding along walls.</summary>
    public static void knockBack(SimState state, Zombie z, Point from)
    {
        z.staggerUntil = state.tick + STAGGER_TICKS;
        z.windup = 0;
        if (z.crossing != null)
        {
            return; // on a car or over a barricade: it reels where it is (Crossings.cs)
        }
        var v = Motion.toward(z.at.x - from.x, z.at.y - from.y, (z.variant == "brute") ? ((double)(SHOVE) / (2)) : SHOVE);
        z.at = Motion.slide(state, z.at, v.x, v.y, moverOf(z));
        var tile = Motion.tileOf(z.at);
        if ((tile.x != z.pos.x) || (tile.y != z.pos.y))
        {
            z.pos = tile;
            z.path = [];
            z.nextThinkTick = state.tick;
        }
    }

    /// <summary>A guard's blow. Returns true if the zombie died (and was removed).</summary>
    public static bool damageZombie(SimState state, Zombie z, double amount, Survivor by)
    {
        z.health = JsMath.max(0, z.health - amount);
        z.hurtTick = state.tick;
        if (z.health > 0)
        {
            return false;
        }
        removeZombie(state, z);
        by.kills += 1;
        Mood.addThought(state, by, "bloodUp");
        EndgameMod.maybeSample(state, z.id, z.pos);
        if (by.kills == Skills.HARDENED_KILLS)
        {
            Skills.earnTrait(state, by, "hardened", $"{Js.Str(Skills.HARDENED_KILLS)} kills");
        }
        if (Prisoners.maybeCapture(state, z, by.name))
        {
            return true;
        }
        Corpses.leaveCorpse(state, z.pos, null);
        Events.logEvent(state, $"{Js.Str(by.name)} killed a {Js.Str((z.kind == "raider") ? Js.Str("raider") : Js.Str("zombie"))}");
        return true;
    }

    /// <summary>Moves zombies off a tile that stopped being floor (wall or door built on it).</summary>
    public static void evictZombies(SimState state, Point tile)
    {
        foreach (var z in Js.Iter(state.zombies))
        {
            var onIt = Space.samePoint(z.pos, tile) || ((z.move != null) && Space.samePoint(z.move.to, tile));
            if (!Js.T(onIt))
            {
                continue;
            }
            var to = Space.nearestTile(state.grid, tile, (x, y) => Pathfinding.isPassable(state.grid, x, y, "zombie"));
            if (!Js.T(to))
            {
                continue;
            }
            z.pos = to;
            z.move = null;
            z.path = [];
        }
    }

    /// <summary>Through a broken window between two tiles: a climb, at half speed.</summary>
    public static bool climbing(SimState state, Point a, Point b)
    {
        return Grids.edgeKindBetween(state.grid, a.x, a.y, b.x, b.y) == Grids.EDGE_WINDOW_BROKEN;
    }
}
