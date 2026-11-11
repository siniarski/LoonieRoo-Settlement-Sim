// The death screen (Ui/DeathScreen.cs), driven with real input: the one you play dies (the fixture sets their health
// to 0), and the screen holds the world and takes first person's input; with two who could carry on, a number key
// and a click each choose; with nobody left it is the end of the run, with its way back to the menu and a way to
// begin again. `--deathshot=x.png` also saves the screen in both forms (x.png, x-end.png).

using System;
using System.Linq;
using Task = System.Threading.Tasks.Task;
using Godot;
using LoonieRoo.Game.Ui;
using LoonieRoo.Sim;

namespace LoonieRoo.Game;

public sealed partial class Game
{
    async Task DeathSteps()
    {
        var you = ControlMod.controlled(state)!;
        string dead = you.name;
        // Two who could carry on: added to the colony beside you (a fixture, not play).
        var marta = Sim.Survivors.addSurvivor(state, you.pos.x + 1, you.pos.y, "Marta");
        var joe = Sim.Survivors.addSurvivor(state, you.pos.x - 1, you.pos.y, "Joe");
        marta.resolve = 80;
        joe.resolve = 60;
        view.paused = false;
        you.health = 0;
        await Frames(4);
        Check("When you die, the death screen is up and first person is off", state.succession != null && deathScreen.Visible && !view.firstPerson && !firstPerson.Visible, $"succession {state.succession?.name}, screen {deathScreen.Visible}, first person {view.firstPerson}");
        Check("The death screen says who died and who can carry on", FindButton(deathScreen, $"1  Marta") != null && FindButton(deathScreen, "2  Joe") != null && FindButton(deathScreen, "3  Start anew, far from here") != null && FindButton(deathScreen, "Back to the menu (Esc)") != null);
        var said = AllText(deathScreen);
        Check("It names them, of what, and the day", said.Contains(dead) && said.Contains("died of") && said.Contains($"Day {Sim.Time.dayOf(state.succession!.tick)}"), said.Replace("\n", " | "));
        if (ParseArgs().TryGetValue("deathshot", out var shot))
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            WriteShot(ProjectSettings.GlobalizePath(shot));
        }
        // The world is held, and the movement keys, the mouse and F do nothing.
        int tick = state.tick;
        var at = marta.at;
        SendKey(Godot.Key.W, true);
        await Click(new Vector2(40, 40));
        await Tap(Godot.Key.F);
        await Frames(10);
        SendKey(Godot.Key.W, false);
        Check("The world is held while the death screen is up", state.tick == tick && marta.at.x == at.x && marta.at.y == at.y && state.succession != null && state.control == null, $"tick {tick} -> {state.tick}, control {state.control?.survivorId}");

        // 1 chooses the first of them.
        await Tap(Godot.Key.Key1);
        await Frames(3);
        Check("A number key chooses who carries on", state.succession == null && state.control?.survivorId == marta.id && !deathScreen.Visible && view.firstPerson, $"control {state.control?.survivorId}");
        await PlaysOn("The world goes on after the choice");

        // Marta dies in turn: Joe by a click.
        marta.health = 0;
        await Frames(4);
        Check("The next death shows it again, with the one left", deathScreen.Visible && FindButton(deathScreen, "1  Joe") != null && FindButton(deathScreen, "2  Joe") == null);
        var joeButton = FindButton(deathScreen, "1  Joe");
        if (joeButton != null) await Click(joeButton.GetGlobalRect().GetCenter());
        await Frames(3);
        Check("A click chooses who carries on", state.succession == null && state.control?.survivorId == joe.id && view.firstPerson, $"control {state.control?.survivorId}");

        // Nobody left: the end of the run.
        state.survivors.RemoveAll((s) => s.id != joe.id);
        joe.kills = 4;
        joe.health = 0;
        await Frames(4);
        var end = AllText(deathScreen);
        Check("With nobody left it is the end, with what the run came to", deathScreen.Visible && end.Contains("THE END") && end.Contains("The run came to") && end.Contains("4 of the dead"), end.Replace("\n", " | "));
        Check("...and offers beginning again and the way back to the menu", FindButton(deathScreen, "1  Begin again, far from here") != null && FindButton(deathScreen, "Back to the menu (Esc)") != null && FindButton(deathScreen, "Just watch the colony", true) == null);
        if (ParseArgs().TryGetValue("deathshot", out var shot2))
        {
            var path = shot2.EndsWith(".png") ? shot2[..^4] + "-end.png" : shot2 + "-end.png";
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            WriteShot(ProjectSettings.GlobalizePath(path));
        }
        var back = FindButton(deathScreen, "Back to the menu (Esc)");
        if (back != null) await Click(back.GetGlobalRect().GetCenter());
        await Frames(3);
        Check("The way back to the menu opens it, with nothing to resume", view.menu == "main" && menu.Visible && !view.inGame, $"menu {view.menu}, in game {view.inGame}");
        // Back to the screen (a fixture: the menu's own routes into a game are tested elsewhere), then begin again by key.
        view.menu = null;
        view.inGame = true;
        menu.Refresh();
        await Frames(3);
        Check("The end screen is still there to choose from", deathScreen.Visible && state.succession != null);
        await Tap(Godot.Key.Key1);
        await Frames(3);
        Check("Beginning again puts you in first person on a new survivor", state.succession == null && state.control != null && view.firstPerson && state.survivors.Count == 1, $"survivors {state.survivors.Count}, control {state.control?.survivorId}");
    }

    /// <summary>Every label and button text under a node, a line each.</summary>
    static string AllText(Node root)
    {
        var lines = new System.Collections.Generic.List<string>();
        void walk(Node n)
        {
            if (n is Label l && l.IsVisibleInTree()) lines.Add(l.Text);
            else if (n is Button b && b.IsVisibleInTree()) lines.Add(b.Text);
            foreach (var c in n.GetChildren()) walk(c);
        }
        walk(root);
        return string.Join("\n", lines);
    }
}
