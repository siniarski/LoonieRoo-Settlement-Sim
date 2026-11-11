// The self-test's steps for how bodies move (SelfTest.cs calls BodySteps after the leaps), from the player's play of
// 2026-11-11: the walks swing the arms at the sides (the brisk walk every tile-walker plays had the hands up behind
// the back: art/scripts/derive_clips.py's mean_rot), a carry holds them in front, and while the world is held (the
// menu, the map, --paused) no body's clip runs on.

using System;
using System.Linq;
using Task = System.Threading.Tasks.Task;

namespace LoonieRoo.Game;

public partial class Game
{
    async Task BodySteps()
    {
        var ppl = firstPerson.People;
        // The arms, on your body's skeleton: the walks' hands stay by the hips (not up behind the back), a carry's in front.
        var walks = new[] { "more/walk_slow", "more/walk", "more/walk_brisk", "more/shuffle" };
        var hands = walks.Select((c) => (c, h: ppl.HandsIn(c))).ToList();
        Check("The walks swing the arms at the sides: the hands by the hips, not up behind the back",
            hands.All((x) => x.h.up < 0.12f && x.h.behind < 0.4f),
            string.Join(", ", hands.Select((x) => $"{x.c[5..]} up {x.h.up:0.00} behind {x.h.behind:0.00}")));
        var carry = new[] { "more/carry_walk", "more/carry_brisk", "more/carry_idle" }.Select((c) => (c, h: ppl.HandsIn(c))).ToList();
        Check("Carrying holds the hands in front all the way through", carry.All((x) => x.h.ahead > 0.2f),
            string.Join(", ", carry.Select((x) => $"{x.c[5..]} ahead {x.h.ahead:0.00}")));

        // Held still: paused, no body's playhead moves over half a second; playing again, yours does.
        bool was = view.paused;
        view.paused = true;
        await Frames(3);
        var held = ppl.Playheads();
        await Frames(30);
        var after = ppl.Playheads();
        int moved = held.Count((h) => after.Any((a) => a.id == h.id && (a.clip != h.clip || Math.Abs(a.at - h.at) > 1e-6)));
        Check("While the world is held, no body's clip plays on", held.Count > 0 && held.Any((h) => h.id == "you") && moved == 0,
            $"{held.Count} bodies, {moved} moved: " + string.Join(", ", held.Take(4).Select((h) => $"{h.id} {h.clip} {h.at:0.000}")));
        view.paused = false;
        await Frames(30);
        var played = ppl.Playheads();
        var you0 = held.FirstOrDefault((h) => h.id == "you");
        var you1 = played.FirstOrDefault((h) => h.id == "you");
        Check("Playing again, they move on", you1.id == "you" && (you1.clip != you0.clip || Math.Abs(you1.at - you0.at) > 1e-3),
            $"you {you0.clip} {you0.at:0.000} -> {you1.clip} {you1.at:0.000}");
        view.paused = was;
    }
}
