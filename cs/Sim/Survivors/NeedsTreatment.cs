// Treating yourself, part of Needs (Needs.cs): a hurt or infected survivor standing still walks to the nearest free
// remedy (medicine if infected, else a bandage), reserves it and applies it where it lies, over Health.TREAT_TICKS.
// A doctor treating someone else is a job (Work.workDoctor).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Needs
{
    /// <summary>Runs when standing still, after eating and before sleep. A hurt survivor walks to the nearest free bandage and applies it where it lies.</summary>
    public static bool updateTreatment(SimState state, Survivor s)
    {
        if (Js.T(s.sleep))
        {
            return false;
        }
        if (!Js.T(s.treatment) && onGuardDuty(state, s))
        {
            return false;
        }
        if (!Js.T(s.treatment) && !startTreatment(state, s))
        {
            return false;
        }
        var treatment = Js.NN(s.treatment);
        var item = Items.getItem(state, treatment.itemId);
        if (!Js.T(Js.T(item?.pos)) || (item.reservedBy != s.id))
        {
            cancelTreatment(state, s, "bandage gone");
            return false;
        }
        if (!Space.samePoint(s.pos, item.pos))
        {
            if (!Survivors.walkTo(state, s, item.pos))
            {
                cancelTreatment(state, s, "can't reach the bandage");
                s.nextTreatTick = state.tick + Health.TREAT_RETRY_TICKS;
                return false;
            }
            return true;
        }
        treatment.progress += 1;
        if (treatment.progress == 1)
        {
            s.note = $"bandaging wounds at ({Js.Str(item.pos.x)}, {Js.Str(item.pos.y)})";
        }
        if (treatment.progress < Time.scaled(Health.TREAT_TICKS))
        {
            return true;
        }
        Items.removeItem(state, item);
        Medicine.applyRemedy(s, item.kind);
        s.treatment = null;
        s.note = (item.kind == "medicine") ? "took medicine for the infection" : "treated wounds with a bandage";
        return true;
    }

    /// <summary>The nearest free thing that would help: medicine if infected, else a bandage if hurt.</summary>
    public static Item? nearestRemedy(SimState state, Survivor s)
    {
        foreach (var kind in Js.Iter(Medicine.remediesFor(s)))
        {
            var found = nearestFreeItem(state, s, kind);
            if (Js.T(found))
            {
                return found;
            }
        }
        return null;
    }

    public static bool startTreatment(SimState state, Survivor s)
    {
        if ((Medicine.remediesFor(s).length == 0) || (state.tick < s.nextTreatTick))
        {
            return false;
        }
        var bandage = nearestRemedy(state, s);
        if (!Js.T(Js.T(bandage?.pos)))
        {
            s.nextTreatTick = state.tick + Health.TREAT_RETRY_TICKS;
            return false;
        }
        if (Js.T(s.jobId))
        {
            Jobs.releaseSurvivor(state, s, "going to treat wounds");
        }
        Weapons.cancelArming(state, s, "going to treat wounds");
        Clothing.cancelDressing(state, s, "going to treat wounds");
        bandage.reservedBy = s.id;
        s.treatment = new SurvivorTreatment { itemId = bandage.id, progress = 0 };
        s.note = $"going for {Js.Str((bandage.kind == "medicine") ? Js.Str("medicine") : Js.Str("a bandage"))} at ({Js.Str(bandage.pos.x)}, {Js.Str(bandage.pos.y)})";
        return true;
    }

    /// <summary>Gives up the reserved bandage (if any). Safe to call when not treating.</summary>
    public static void cancelTreatment(SimState state, Survivor s, string note)
    {
        if (!Js.T(s.treatment))
        {
            return;
        }
        var item = Items.getItem(state, s.treatment.itemId);
        if (Js.T(item) && (item.reservedBy == s.id))
        {
            item.reservedBy = null;
        }
        s.treatment = null;
        s.goal = null;
        s.path = [];
        s.pathVersion = -1;
        s.note = note;
    }
}
