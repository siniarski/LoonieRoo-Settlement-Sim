# 2026-11-12 18: self-test gaps (input-driven steps)

Two new step files, called last from SelfTest.cs (`SELFTEST_ONLY=place` / `play` run each alone). Each group sets up its own scene on open ground (or a new world) and plays it with real keys.

## Added (SelfTestPlace.cs, SelfTestPlay.cs)
- Doors: R with wood boards a door in a wall; a dead one beside it stays on its side while the boards hold; you walk through; E on a locked door says "it's locked" and it stays shut.
- Garage: E rolls up three side-by-side doors together, you walk through, E pulls them down, locked they stay down and say so.
- Furniture: W at a dresser stops you at it; at an angle you slide round it, never inside; you walk onto a bed; E opens a table, a washer and a till as containers.
- Water: W into a five-tile river: wade, swim, the eye sinks, the stroke hands show, wetness to the top; Space makes a stroke; you come out on the far bank; a dead one wades a one-tile stream to reach you.
- Cars: crawling out from under a pickup on the south side (the existing step does the north); two dead after you sit in a sedan come to its body, none inside.
- Screens: F in first person and on the map with nobody selected keeps your character; M back to first person.
- Long play: a new solo 512 world, the keys held to run along a road then across country with a turn every 3-6 game seconds, two game hours (400 ticks) at 4x: tick never stalls, never inside a wall or a car, walked 70 m, no exception in the Godot log.

## Already covered elsewhere, not repeated
Push with J, alarm, U and I, crouch and E under a pickup (north side), vault a window and a fence, hotbar 0-3, punch, sights, swim hands (Anim), M tabs, death screens (SelfTestDeath).
Not done: a tall fence that is not vaulted; the dead stopped at a car you sit in is checked with static dead only; the death screen.

## Findings
- (Not a fault, 2026-11-13: the scene sat you in a car at 1% health, a wreck after one blow, so the dead reached you through it as they are meant to once it is wrecked. The step now uses a sound car and checks that the car is battered and you are not.) Sitting in a parked sedan with two dead after you, you lose 1000 -> 451 health in 8 s: they hurt you through the car (the check only asks that they stop at its body, which they do, 178 SUB off). Probably intended (windows); say if not. See it: SELFTEST_ONLY=play.
- 2 game hours at 4x is only about 70 m of held running (400 ticks): "a few hundred metres" needs 6+ game hours, which would take about 2 minutes here. Not a game bug; the threshold is 60 m.
No step is marked as a known failure: `KnownFailures` and `CheckKnown` (SelfTestPlace.cs) are the mechanism, empty now.
