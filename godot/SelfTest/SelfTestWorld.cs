// The self-test's steps for what first person draws since 2026-11-11 (WorldSteps is called from SelfTest.cs after the
// fire): farm plots and crops (World3D/Fields.cs), building sites (Sites.cs), a car as a model (Cars.cs), the people
// the sim keeps as a tile (Others.cs: a wanderer, a trader, a prisoner, a caravan, a scavenging party) and the
// helicopter (Aircraft.cs). The scenes are the sim's (Staging); each is taken away again.

using System.Linq;
using Task = System.Threading.Tasks.Task;
using LoonieRoo.Sim;

namespace LoonieRoo.Game;

public partial class Game
{
    async Task WorldSteps()
    {
        var you = ControlMod.controlled(state)!;
        int plots = state.farm.Count, sites = state.blueprints.Count;
        int plotsDrawn = firstPerson.PlotCount, carsDrawn = firstPerson.CarCount;
        var heli = state.timeline?.heli;

        Staging.farm(state, you);
        Staging.sites(state, you);
        var car = Staging.car(state, you.pos.x + 6, you.pos.y + 4, 0);
        await Frames(30); // the plots are gathered every couple of seconds
        Check("Farm plots are drawn in first person: bare beds and crops by stage", firstPerson.PlotCount == plotsDrawn + 24 && state.farm.Count - plots == 24
            && World3D.Fields.ModelOf(state.farm[^1]) == "herb_3" && World3D.Fields.ModelOf(state.farm[plots]) == "soil",
            $"{firstPerson.PlotCount} drawn of {state.farm.Count} plots; the last is {World3D.Fields.ModelOf(state.farm[^1])}");
        Check("Building sites are drawn: pegs, frames going up, material delivered", firstPerson.SiteCount >= state.blueprints.Count - sites && state.blueprints.Count > sites,
            $"{firstPerson.SiteCount} pieces for {state.blueprints.Count - sites} blueprints");
        Check("A car is drawn as a model, not a box", firstPerson.CarCount == carsDrawn + 1 && World3D.Cars.ModelOf(car).StartsWith("car"), $"{firstPerson.CarCount} cars, the new one a {World3D.Cars.ModelOf(car)}");

        int people = firstPerson.OtherPeople, props = firstPerson.OtherProps, models = firstPerson.Modelled;
        int wanderers = state.wanderers.Count, traders = state.traders.Count, prisoners = state.prisoners.Count;
        int caravans = Caravans.caravansOf(state).Count, runs = Runs.runsOf(state).Count;
        Staging.visitors(state, you);
        Staging.helicopter(state, you.pos.x + 10, you.pos.y);
        await Frames(20); // a few models are made a frame
        Check("A wanderer, a trader, a prisoner, a caravan and a scavenging party are people in first person", firstPerson.OtherPeople == people + 9 && firstPerson.OtherProps == props + 2 && firstPerson.Modelled >= models + 9,
            $"{firstPerson.OtherPeople - people} people (9: one each, three with the cart, three scavenging), {firstPerson.OtherProps - props} props (the stall and the handcart), {firstPerson.Modelled - models} more models");
        Check("The helicopter is overhead", firstPerson.HelicopterShown || state.timeline == null, state.timeline == null ? "no timeline in this world" : "");

        // Put the world back as it was.
        state.farm.RemoveRange(plots, state.farm.Count - plots);
        state.blueprints.RemoveRange(sites, state.blueprints.Count - sites);
        state.vehicles?.Remove(car);
        state.wanderers.RemoveRange(wanderers, state.wanderers.Count - wanderers);
        state.traders.RemoveRange(traders, state.traders.Count - traders);
        state.prisoners.RemoveRange(prisoners, state.prisoners.Count - prisoners);
        state.caravans?.RemoveRange(caravans, state.caravans.Count - caravans);
        state.runs?.RemoveRange(runs, state.runs.Count - runs);
        if (state.timeline != null) state.timeline.heli = heli;
        await Frames(30);
        Check("With them gone nothing of theirs is drawn", firstPerson.PlotCount == plotsDrawn && firstPerson.OtherPeople == people && firstPerson.OtherProps == props && firstPerson.CarCount == carsDrawn,
            $"{firstPerson.PlotCount} plots, {firstPerson.OtherPeople} others, {firstPerson.OtherProps} props, {firstPerson.CarCount} cars");
    }
}
