- **2026-11-11, into the rivers** (the player's playtest: "rivers cannot be entered (perhaps the heightmap should
  continue underwater and your player gets wet as a result of entering and swimming (spacebar should be one way to
  swim up)"). You walk into water now: wade the shallows, swim the deep, soaked and chilled; Space is a stroke out of
  your depth. New: `cs/Sim/You/Swim.cs`, `cs/Sim.Tests/SwimTest.cs` (10 tests), pictures in
  `experiments/play-fixes/rivers/`.
  - **How it works.** Depth by the distance from the bank (tiles only): a water tile with land (not a bridge's deck)
    among its eight neighbours is shallow, any other deep; the map's edge is not a bank. The Heights' bed already
    sloped down from the bank (shore corners 0.05 m under, then 0.7 m, 1.0 m, 1.3 m at most) and agrees with it, but
    the tick reads no heights (hand-made maps have none). Your body moves as a new mover, `Pathfinding.SWIMMER` (the
    free mover's rules, and water: two lines in Pathfinding.cs), in your walking (ControlMod), jumps and strokes
    (Leaps.tick); nobody plans a path for it. Wading: 60% of your pace (1.5 tiles/s against 2.5 on the bank, measured
    in the game's trace), running allowed, wetness to 55 (over "soaked"), body heat down a point every 4 slow ticks.
    Swimming: 40% (1.0 tiles/s), no running, 1 endurance a tick paddling (a run's cost), a point every 4 ticks
    treading water, no endurance back; wetness to 100 at once, body heat down every 2 slow ticks (the cold season's
    chill comes on top, and soaked through at night outdoors chills you after, as rain does). Winded in deep water:
    20%, and 30 health (of 1000) a second ("drowning" is the cause of death) until you reach the shallows. Space in
    deep water is a stroke: 10 ticks, a tile and a quarter, 2 endurance (paddling that far costs about 12); refused
    winded. Swinging, shooting, reloading, using and working are refused while swimming (Commands.cs: "swimming");
    wading allows them. Letting go of control in the water puts you on the nearest land (`Swim.ashore`).
  - **Only you.** Colonists, the dead, animals and dogs keep water as a wall (tested for every mover, and the dead
    walking to the bank for you never step in over 300 ticks). Letting the dead wade the shallows would be a line in
    isPassable, but their reachability labels, hordes and herds assume water stops them: not done.
  - **The hop stays** (a tile of water: dry, and quicker than wading); a jump into wider water now lands you in it
    (LeapsTest updated).
  - **The view.** The water's surface is its own surface on the ground's mesh with a translucent material
    (`WorldLook.Water`: dark, a little glossy, clearer looking down than across), and the bed under it darkens with
    depth; your eye drops onto the bed's slope as you wade (it already stood on the bed's heights) and, swimming,
    eases down to 0.14 m over the surface (`FirstPerson` sink, 3/s), your body let down with it (head and shoulders
    out, the rest seen through the water); the weapon is put away while swimming; a stroke and going out of your depth
    splash (the shallow-water step sound, louder and lower: Audio/Yours.cs). No swimming pose: the body treads water
    upright in the walk clip.
  - **Numbers.** Solo balance, seeds 1-6, before and after: seeds 3, 4 and 6 identical line for line; seed 1 dies of
    starvation on day 7 at tick 58013 (was 58085), seed 2 of starvation on day 4 at 35901 (was 31924), seed 5 of
    starvation on day 3 at 22998 (was day 4 at 34811). The scripted player never enters water (logged tile by tile);
    the change is that your body may now overlap the water's edge, so a bank no longer slides you along it as a wall
    (confirmed: with SWIMMER treating water as the free mover does, seed 5 is identical). Bench: within its noise (128
    with 3 colonists 0.097 -> 0.090 ms, 99 colonists 0.720 -> 0.808, 173 zombies 0.652 -> 0.900, 10 days on 128 1.7 s
    -> 1.4 s; nobody swims in it), save sizes unchanged.
  - **Pictures** (`experiments/play-fixes/rivers/`, seed 3 at 256, the river at x 116-126 by y 97): `bed-from-bank.png`
    (the shelf by the bank and the bed darkening away), `wading.png` and `wading-outside.png`, `swimming-eyes.png`
    (the far bank at eye level), `swimming-outside.png` (head over the surface, the body under it).
  - **Left.** A swimming pose and stroke animation; the dead and animals in the shallows; carried things getting wet;
    currents; ripples, depth fade and the far field's water (still opaque); playing it by hand.
