// Ported from src/sim/defences.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Defences beyond walls, and the dark.
//
// Reinforcing: a wall, door or barricade can be reinforced with scrap (a
// blueprint like any other, delivering scrap instead of wood). Reinforced
// structures take REINFORCE_FACTOR times the battering and don't burn.
//
// Turrets: a gun emplacement fed ammo by haulers (fuel jobs, jobs.ts). Every
// TURRET_EVERY ticks it shoots the nearest zombie it can see within
// TURRET_RANGE with a clear line, loudly.
//
// Lures: a noisemaker that rattles every hour at night (noise.ts), drawing
// the world's zombies and herds to it, away from where they'd otherwise go.
//
// Pits: a zombie that steps in one is stuck for PIT_TICKS (no moving, no
// hitting). Pits don't wear out, and survivors step round them.
//
// Darkness (harsh games): at night a zombie can only be fought if someone can see it: a
// survivor within NIGHT_SIGHT (more on a tower or vigilant), a lit lamp's
// pool, a fire's glow, or a dog's nose.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Defences
{

    /// <summary>What a spike trap does to the zombie that steps on it.</summary>
    public const int TRAP_DAMAGE = 100;

    public const double REINFORCE_FACTOR = 2.5;

    public const int TURRET_RANGE = 7;

    public const int TURRET_EVERY = 20;

    public const int TURRET_DAMAGE = 100;

    public const int TURRET_AMMO_MAX = 2;

    public const int PIT_TICKS = 100;

    public const int NIGHT_SIGHT = 8;

    public const int TOWER_NIGHT_SIGHT = 10;

    public static bool isReinforced(SimState state, int x, int y)
    {
        return state.grid.reinforced.includes(Grids.tileIndex(state.grid, x, y));
    }
    public static bool isReinforced(SimState state, double x, double y) => isReinforced(state, Js.I(x), Js.I(y));

    /// <summary>Can someone see this point? Always by day.</summary>
    public static bool seen(SimState state, Point p)
    {
        if (!state.harsh || !Time.isNight(state.tick))
        {
            return true;
        }
        foreach (var s in Js.Iter(state.survivors))
        {
            var sight = (Towers.onTower(state, s) ? TOWER_NIGHT_SIGHT : NIGHT_SIGHT) + (Skills.hasTrait(s, "vigilant") ? Skills.VIGILANT_SIGHT : 0);
            if (Space.manhattan(s.pos, p) <= sight)
            {
                return true;
            }
        }
        if (Lights.isLit(state, p))
        {
            return true;
        }
        if (state.fires.some((f) => Space.manhattan(f, p) <= 3))
        {
            return true;
        }
        return state.dogs.some((d) => Space.manhattan(d.pos, p) <= Dogs.DOG_SENSE);
    }

    /// <summary>Turrets shoot; lures rattle. Every tick.</summary>
    public static void updateDefences(SimState state)
    {
        if (((state.tick % Time.TICKS_PER_HOUR) == 0) && Time.isNight(state.tick))
        {
            foreach (var f in Js.Iter((FurnitureMod.ofKind(state, "lure")).ToList()))
            {
                Noise.makeNoise(state, f, "lure");
            }
        }
        if (((state.tick % TURRET_EVERY) != 0) || (state.zombies.length == 0))
        {
            return;
        }
        foreach (var t in Js.Iter((FurnitureMod.ofKind(state, "turret")).ToList()))
        {
            if ((t.loot == 0) && (t.stored == 0))
            {
                continue;
            }
            var z = Js.Get(state.zombies.filter((q) => ((Space.manhattan(q.pos, t) <= TURRET_RANGE) && seen(state, q.pos)) && Work.clearShot(state.grid, t, q.pos)).sort((a, b) => Space.manhattan(a.pos, t) - Space.manhattan(b.pos, t)), 0);
            if (!Js.T(z))
            {
                continue;
            }
            if (t.loot == 0)
            {
                t.stored -= 1;
                if (t.stored == 0)
                {
                    t.storedKind = null;
                }
                t.loot = Weapons.AMMO_ROUNDS;
            }
            t.loot -= 1;
            Noise.makeNoise(state, t, "gunfire");
            z.health = JsMath.max(0, z.health - TURRET_DAMAGE);
            if (z.health > 0)
            {
                continue;
            }
            Zombies.removeZombie(state, z);
            Corpses.leaveCorpse(state, z.pos, null);
            EndgameMod.maybeSample(state, z.id, z.pos);
            Events.logEvent(state, $"The turret at ({Js.Str(t.x)}, {Js.Str(t.y)}) brought down a zombie");
        }
    }

    /// <summary>Spikes: TRAP_DAMAGE to a zombie stepping on a trap. A spent trap goes, leaving a plan to rebuild it. Returns true if the zombie died.</summary>
    public static bool springTrap(SimState state, Zombie z)
    {
        var trap = FurnitureMod.furnitureAt(state, z.pos.x, z.pos.y);
        if ((trap?.kind != "trap") || (trap.loot <= 0))
        {
            return false;
        }
        trap.loot -= 1;
        z.health = JsMath.max(0, z.health - TRAP_DAMAGE);
        if (trap.loot == 0)
        {
            FurnitureMod.removeFurniture(state, trap);
            Designations.addBlueprint(state, trap.x, trap.y, "trap");
            Events.logEvent(state, $"A spike trap at ({Js.Str(trap.x)}, {Js.Str(trap.y)}) is spent");
        }
        if (z.health > 0)
        {
            return false;
        }
        Zombies.removeZombie(state, z);
        Corpses.leaveCorpse(state, z.pos, null);
        Events.logEvent(state, "A spike trap killed a zombie");
        return true;
    }

    /// <summary>A zombie that just stepped onto a tile: a pit holds it.</summary>
    public static void fallInPit(SimState state, Zombie z)
    {
        if (FurnitureMod.furnitureAt(state, z.pos.x, z.pos.y)?.kind != "pit")
        {
            return;
        }
        z.stuckUntil = state.tick + PIT_TICKS;
        z.path = [];
    }
}
