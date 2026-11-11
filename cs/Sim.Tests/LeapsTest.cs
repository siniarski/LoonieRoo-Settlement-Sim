// Leaps (Leaps.cs): Space in first person jumps, vaults a broken window or a barricade, or hops a tile of water.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class LeapsTest
{
    static readonly List<string> MAP = [".......", ".......", ".......", ".......", "......."];

    /// <summary>You at the centre of (3, 2), under control, the map and edges given.</summary>
    static (SimState state, Survivor s) start(List<string> map, List<ScenarioEdgesItem> edges = null)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = map, edges = edges ?? new() });
        var s = state.survivors[0];
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        s.at = Motion.centre(new Point { x = 3, y = 2 });
        s.pos = new Point { x = 3, y = 2 };
        s.endurance = BodyMod.BODY_MAX;
        return (state, s);
    }

    static List<string> you(List<string> map) => map.map((r, y) => (y == 2) ? r.Substring(0, 3) + "s" + r.Substring(4) : r);

    /// <summary>One edge of `kind` on the east side of (3, 2) (the west side of (4, 2)).</summary>
    static List<ScenarioEdgesItem> eastEdge(string kind) => [new() { x = 4, y = 2, side = "w", kind = kind }];

    /// <summary>Ticks until you're through: on the far tile and clear of the frame.</summary>
    static int ticksToCross(SimState state, Survivor s)
    {
        int t = 0;
        for (; t < 100 && (s.pos.x < 4 || Motion.inWindowFrame(state.grid, s.at)); t++) Tick.tick(state);
        return t;
    }

    [Fact(DisplayName = "leaps > on open ground, a jump lifts you and lands you where you were going, a little tired")]
    public void Jump()
    {
        var (state, s) = start(you(MAP));
        Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
        expect(Commands.applyCommand(state, new Command { type = "jump" }).ok).toBe(true);
        expect(state.control.busy.what).toBe("jump");
        expect(s.endurance).toBe(BodyMod.BODY_MAX - Leaps.JUMP_COST);
        expect(Commands.applyCommand(state, new Command { type = "jump" }).ok).toBe(false); // already up
        double x0 = s.at.x;
        Tick.tick(state);
        expect(PoseIntents.ofSurvivor(state, s).lift).toBeGreaterThan(0);
        for (int i = 1; i < Leaps.JUMP_TICKS; i++) Tick.tick(state);
        expect(state.control.busy).toBe(null);
        expect(s.at.x).toBeGreaterThan(x0);    // carried on the way you were going
        expect(s.at.y).toBe(Motion.centre(new Point { x = 3, y = 2 }).y);
    }

    [Fact(DisplayName = "leaps > standing still, a jump goes straight up and down")]
    public void JumpInPlace()
    {
        var (state, s) = start(you(MAP));
        var at = s.at;
        expect(Commands.applyCommand(state, new Command { type = "jump" }).ok).toBe(true);
        for (int i = 0; i < Leaps.JUMP_TICKS; i++) Tick.tick(state);
        expect(s.at.x).toBe(at.x);
        expect(s.at.y).toBe(at.y);
        expect(state.control.busy).toBe(null);
    }

    [Fact(DisplayName = "leaps > winded, you can't jump; in the air, you can't swing or use")]
    public void Winded()
    {
        var (state, s) = start(you(MAP));
        s.endurance = BodyMod.WINDED - 1;
        expect(Commands.applyCommand(state, new Command { type = "jump" }).ok).toBe(false);
        s.endurance = BodyMod.BODY_MAX;
        expect(Commands.applyCommand(state, new Command { type = "jump" }).ok).toBe(true);
        expect(Commands.applyCommand(state, new Command { type = "swing", x = 4, y = 2 }).ok).toBe(false);
        expect(Commands.applyCommand(state, new Command { type = "use", x = 4, y = 2 }).ok).toBe(false);
    }

    [Fact(DisplayName = "leaps > a vault takes you through a broken window much faster than climbing through")]
    public void VaultWindow()
    {
        int climb()
        {
            var (state, s) = start(you(MAP), eastEdge("brokenWindow"));
            s.at = new Point { x = 3 * Motion.SUB + 200, y = s.at.y };
            Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
            return ticksToCross(state, s);
        }
        var (st, sv) = start(you(MAP), eastEdge("brokenWindow"));
        sv.at = new Point { x = 3 * Motion.SUB + 200, y = sv.at.y };   // 56 SUB from the sill
        Commands.applyCommand(st, new Command { type = "steer", dx = 1, dy = 0 });
        expect(Commands.applyCommand(st, new Command { type = "jump" }).ok).toBe(true);
        expect(st.control.busy.what).toBe("vault");
        expect(sv.note).toBe("climbed through the window");
        Commands.applyCommand(st, new Command { type = "steer", dx = 0, dy = 0 });  // let go of W in the air: you land and stand
        int vault = ticksToCross(st, sv);
        for (int i = 0; i < Leaps.VAULT_TICKS; i++) Tick.tick(st);
        expect(sv.pos.x).toBe(4);
        expect(Motion.fits(st.grid, sv.at, "free")).toBe(true);
        expect(vault).toBeLessThan(climb());
    }

    [Fact(DisplayName = "leaps > you vault a barricade but not glass, boards or a wall: there you just jump")]
    public void VaultWhat()
    {
        foreach (var (kind, vaults) in new[] { ("barricade", true), ("window", false), ("wall", false) })
        {
            var (state, s) = start(you(MAP), eastEdge(kind));
            s.at = new Point { x = 3 * Motion.SUB + 200, y = s.at.y };
            Commands.applyCommand(state, new Command { type = "aim", x = 1, y = 0 });
            expect(Commands.applyCommand(state, new Command { type = "jump" }).ok, kind).toBe(true);
            expect(state.control.busy.what, kind).toBe(vaults ? "vault" : "jump");
            for (int i = 0; i < 10; i++) Tick.tick(state);
            expect(s.pos.x, kind).toBe(vaults ? 4 : 3);
        }
        var (st, sv) = start(you(MAP), eastEdge("brokenWindow"));
        st.grid.edgeBoarded.push(Grids.edgeBetween(st.grid, 3, 2, 4, 2));
        sv.at = new Point { x = 3 * Motion.SUB + 200, y = sv.at.y };
        Commands.applyCommand(st, new Command { type = "aim", x = 1, y = 0 });
        Commands.applyCommand(st, new Command { type = "jump" });
        expect(st.control.busy.what).toBe("jump");
    }

    [Fact(DisplayName = "leaps > too far from the window, it's a jump, not a vault")]
    public void VaultReach()
    {
        var (state, s) = start(you(MAP), eastEdge("barricade"));
        s.at = new Point { x = 3 * Motion.SUB + 40, y = s.at.y };  // 216 SUB away
        Commands.applyCommand(state, new Command { type = "aim", x = 1, y = 0 });
        Commands.applyCommand(state, new Command { type = "jump" });
        expect(state.control.busy.what).toBe("jump");
    }

    [Fact(DisplayName = "leaps > a hop takes you over a tile of water, not over two")]
    public void Hop()
    {
        var one = you(MAP.map((r, y) => r.Substring(0, 4) + "%" + r.Substring(5)));
        var (state, s) = start(one);
        s.at = new Point { x = 3 * Motion.SUB + 200, y = s.at.y };
        Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
        expect(Commands.applyCommand(state, new Command { type = "jump" }).ok).toBe(true);
        expect(state.control.busy.what).toBe("hop");
        Tick.tick(state);
        Tick.tick(state);
        Tick.tick(state);
        Tick.tick(state);
        expect(s.pos.x).toBe(3);   // over the water, your tile stays on the ground
        for (int i = 0; i < Leaps.HOP_TICKS; i++) Tick.tick(state);
        expect(s.pos.x).toBeGreaterThanOrEqual(5);

        var two = you(MAP.map((r, y) => r.Substring(0, 4) + "%%" + r.Substring(6)));
        var (st2, s2) = start(two);
        s2.at = new Point { x = 3 * Motion.SUB + 200, y = s2.at.y };
        Commands.applyCommand(st2, new Command { type = "steer", dx = 1, dy = 0 });
        Commands.applyCommand(st2, new Command { type = "jump" });
        expect(st2.control.busy.what).toBe("jump");
        for (int i = 0; i < 20; i++) Tick.tick(st2);
        expect(s2.pos.x).toBe(3);   // the water's edge stops you
    }
}
