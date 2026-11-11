# 2026-11-12 11: look and towns (five commits)

1. **A sturdier self-test.** `ClearGroundForSound` (SelfTestSound.cs): out of any car, on open ground (`OpenGround`), the dead and solid furniture within 12-14 tiles removed, facing east, ticks running. `SoundTest` (the zombie snarl steps) and `SoundSteps` (footsteps, zombie steps) call it first, so they no longer depend on where earlier steps left you. The tick padding in `SelfTestInteract.cs` (waits to 25 and 20 ticks) is gone. New: `SELFTEST_ONLY=sound` (the sound steps alone) and `SELFTEST_SKIP=car,roof,interact,fire,light`. Proven: full run 158 checks; with `SELFTEST_SKIP=car,roof,interact,fire` 128 checks pass; `SELFTEST_ONLY=sound` 18 pass.
2. **The first minute.** FirstPerson.Light: the lighting squares the sim's twilight (`lit = dark^2`): 06:05 was 46% dark, now 21%; sun at least 16 degrees up (was 12), warmed toward the horizon in twilight; sky and ambient follow `lit`. The clock is untouched; noon and night are the same. Pictures `experiments/look-next/light-before-*` / `light-after-*` (06:05, 07, 12, 19, 21).
3. **Snow on models.** The ModelSet, Furniture and Items shaders end in `snowy()`; a cabin's inside (Cockpit) opts out (`ModelSet(..., snow: false)`). Snow cover eases in over a few seconds, so a picture wants `--frames=240`. The item in your hand whitens on its upward faces too. `snow-models-before/after.png`.
4. **Rendered walls.** The plaster layer: grain 0.045 to 0.10, a coarse stucco speckle, bolder trowel sweeps (only plaster.png changed; no layer added or moved); the render palette has six colours (sage, blue, terracotta added). A base course would have meant changing `WorldMesh.Edge`, which others touch: left. The grain only shows close up. `plaster-*.png`.
5. **Town size.** `Towns.RADII` times 1.3 (village 26-36, town 44-60, city 68-86). A town's centre must be dry (`siteTowns`: a pond at the centre of a larger town left its streets nowhere to cross; checked only for a candidate that would win, a check on every one cost 7 s at 2048). Generation changes: every town moves, plan and world hashes (the harness's `gen plan hashes`; none pinned in the sim tests) change. Test changes: the siting reference (TownsSpeedTest) gets the water check; a 256 map is not asked for cabins (the towns left them no cells).

| seeds 1-6 at 512, a world's mean | towns | streets | lots | streets/town | lots/town |
|---|---|---|---|---|---|
| before the wide roads (this log's predecessor) | n/a | 81 | 178 | | |
| now (before this change) | 9.2 | 54.0 | 138.8 | 5.9 | 15.2 |
| radii x1.2 (tried) | 8.8 | 58.3 | 171.3 | 6.6 | 19.4 |
| radii x1.3 (kept) | 8.5 | 64.5 | 182.3 | 7.6 | 21.5 |

Lots are back; streets are at about four fifths: the branches keep their 34/26 spacing and 12 clearance, so a larger town adds fewer. Towns are fewer by about 0.7 a world (more crowding). Cars parked in a street: still every one passable.

Generation speed (seed 42, `gen profile create`): 1024 makePlan 1.20 to 1.05 s, 2048 5.7 to 6.0 s (siteTowns 2.66 to 2.65 s, growStreets 0.16 to 0.24, layRoads 0.69 to 0.73): not worse beyond noise.

Left: streets at 80% of the old count (a closer spacing would restore them); a base band on rendered walls; the held item whitening in snow.
