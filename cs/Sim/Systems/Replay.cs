// Ported from src/sim/replay.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// seed + initial state + command log = the full game.
// Commands logged at tick T are applied before tick T advances, matching how
// live input is applied between frames.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Replay
{

    public static SimState replay(SimState initial, List<LoggedCommand> log, double untilTick) => replayFrom(States.cloneState(initial), log, untilTick);

    /// <summary>replay, but played on `state` itself (a state nothing else holds: a bug report's, just loaded).</summary>
    public static SimState replayFrom(SimState state, List<LoggedCommand> log, double untilTick)
    {
        double next = 0;
        for (; ; )
        {
            for (var entry = Js.Get(log, next); (entry != null) && (entry.tick <= state.tick); entry = Js.Get(log, ++next))
            {
                if (entry.tick < state.tick)
                {
                    throw new Exception($"replay: command at tick {Js.Str(entry.tick)} is earlier than tick {Js.Str(state.tick)} (log unsorted?)");
                }
                var result = Commands.applyCommand(state, entry.command);
                if (!result.ok)
                {
                    throw new Exception($"replay: command at tick {Js.Str(entry.tick)} was rejected: {Js.Str(result.reason)}");
                }
            }
            if (state.tick >= untilTick)
            {
                return state;
            }
            Tick.tick(state);
        }
    }
}
