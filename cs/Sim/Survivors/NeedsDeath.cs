// A survivor's death, part of Needs (Needs.cs): everything they were doing stopped, what they held dropped, a body
// left (not for the infected: they rise), beds freed, then the event and the mourning. Walking away for good is the
// same without the body or the mourning.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Needs
{
    /// <summary>Death (or, with walkedAway, leaving for good: no body, no mourning).</summary>
    public static void killSurvivor(SimState state, Survivor s, string cause, bool walkedAway = false)
    {
        if ((state.control?.survivorId == s.id) && !Js.T(walkedAway))
        {
            SuccessionMod.beginSuccession(state, s, cause);
        }
        cancelMeal(state, s, cause);
        if (Js.T(Js.T(s.jobId) || Js.T(s.carrying)))
        {
            Jobs.releaseSurvivor(state, s, cause);
        }
        wake(state, s, cause);
        cancelTreatment(state, s, cause);
        Weapons.cancelArming(state, s, cause);
        Clothing.cancelDressing(state, s, cause);
        Books.stopReading(state, s, cause);
        Weapons.dropWeapon(state, s);
        Inventory.dropPack(state, s);
        Clothing.undress(state, s);
        if ((cause != Infection.CAUSE) && !Js.T(walkedAway))
        {
            Corpses.leaveCorpse(state, s.pos, s.name);
        }
        foreach (var f in Js.Iter(FurnitureMod.ofKind(state, "bed")))
        {
            if (f.owner == s.id)
            {
                f.owner = null;
            }
        }
        var i = state.survivors.indexOf(s);
        if (i >= 0)
        {
            state.survivors.splice(i, 1);
        }
        if (Js.T(walkedAway))
        {
            return;
        }
        Events.logEvent(state, $"{Js.Str(s.name)} died of {Js.Str(cause)}");
        Director.noteDeath(state);
        Mood.mournDeath(state, s);
    }
}
