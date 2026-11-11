// Ported from src/sim/jobTypes.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Job types and priorities. Kept separate from Jobs.cs so Survivors.cs can
// use them without an import cycle.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class JobTypes
{

    /// <summary>Also the tie-break order when two job types share a priority: threats first.</summary>
    public static readonly List<string> JOB_TYPES = ["guard", "firefight", "doctor", "build", "farm", "haul", "craft", "research", "cook", "scout", "scavenge", "hunt", "clean"];

    public static readonly JsRecord<double> DEFAULT_PRIORITIES = new() { ["guard"] = 3, ["firefight"] = 3, ["doctor"] = 3, ["build"] = 3, ["farm"] = 3, ["haul"] = 3, ["craft"] = 3, ["research"] = 3, ["cook"] = 3, ["scout"] = 3, ["scavenge"] = 3, ["hunt"] = 3, ["clean"] = 3 };

    /// <summary>All job types off: handy for tests that want a survivor to do nothing but move.</summary>
    public static readonly JsRecord<double> NO_JOBS = new() { ["guard"] = 0, ["firefight"] = 0, ["doctor"] = 0, ["build"] = 0, ["farm"] = 0, ["haul"] = 0, ["craft"] = 0, ["research"] = 0, ["cook"] = 0, ["scout"] = 0, ["scavenge"] = 0, ["hunt"] = 0, ["clean"] = 0 };

    public static bool isPriority(object n)
    {
        return ((((Js.Eq(n, 0)) || (Js.Eq(n, 1))) || (Js.Eq(n, 2))) || (Js.Eq(n, 3))) || (Js.Eq(n, 4));
    }

    public static bool isJobType(object t)
    {
        return (Js.TypeOf(t) == "string") && (JOB_TYPES).includes(t);
    }
}
