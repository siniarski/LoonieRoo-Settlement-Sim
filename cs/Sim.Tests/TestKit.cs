#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

/// <summary>The helpers many test files share: tick the sim with the invariants checked, run until a condition holds,
/// and give everyone the same job priorities. A file keeps its own version only where it really differs (its
/// default tick budget, say, as a one-line wrapper over these).</summary>
public static class TestKit
{
    /// <summary>Ticks the sim n times, checking the invariants after each tick.</summary>
    public static void run(SimState state, double n)
    {
        for (var t = 0; t < n; t++)
        {
            Tick.tick(state);
            expect(Invariants.checkInvariants(state), DebugText.dumpText(state)).toEqual(Js.Arr([]));
        }
    }

    /// <summary>Ticks the sim n times with no checks (for tests whose scenes the invariants do not hold in).</summary>
    public static void tickN(SimState state, double n)
    {
        for (var t = 0; t < n; t++) Tick.tick(state);
    }

    /// <summary>Ticks until done(state) holds and returns the ticks it took; fails, with the state dumped, after max ticks.
    /// The invariants are checked after each tick.</summary>
    public static double runUntil(SimState state, Func<SimState, bool> done, double max = 2000)
    {
        for (var t = 0; t < max; t++)
        {
            if (Js.T(done(state)))
            {
                return t;
            }
            Tick.tick(state);
            expect(Invariants.checkInvariants(state), DebugText.dumpText(state)).toEqual(Js.Arr([]));
        }
        throw new Exception($"condition not met after {Js.Str(max)} ticks\n{Js.Str(DebugText.dumpText(state))}\n{Js.Str(state.survivors.map((s) => s.note).join("\n"))}");
    }

    /// <summary>Gives every survivor the job priorities in jobs and no others (the rest at JobTypes.NO_JOBS).</summary>
    public static void setJobs(SimState state, JsRecord<double> jobs)
    {
        foreach (var s in Js.Iter(state.survivors))
        {
            s.priorities = Js.RecordFrom(JobTypes.NO_JOBS, __r0 => { Js.MergeInto(__r0, jobs); });
        }
    }
}
