- **2026-11-12, a quality pass on the animations.** The player asked for every animation to be looked at as it moves, graded, and the wrong ones fixed, the most-seen first. The gaits, the one-shot clips, first-person swings and shots, the dead over cars and animals over cars were pictured in strips; the feet were measured in AnimTest. One real fault was found in code and fixed: a planted foot jumped ~23 cm in one frame when a body near you set off. Everything else seen was right or is in the clips themselves. Strips: experiments/anim-quality/. Docs: docs/characters.md "The quality pass".
  - **Why / what was tried.**
    - The lab's slide (a foot within 2.5 cm of the ground) said the walk slid 5 cm a step, the run 6 and the lurch 9. But the jog and the dead's run slid under 1, and those are the two whose `--calibrate` speed matched Gaits.Measure's. So the first guess was the measured speeds. Measure's median was swapped for the mean: every slide got worse (the walk 4.9 -> 7.9, the shamble 4.9 -> 10.3). Reverted.
    - Then a sweep: `--speedof=` scaled the lurch, slow walk and sneak from 0.8 to 1.08. 1.0 (Measure's median) was the least slide for each, so the speeds are right.
    - ANIMTRACE showed every stance at 0.00 m/s. The "slide" was the swinging foot skimming the ground at touch-down and lift-off (1-2 frames at 1-3 m/s). So the lab now also reports a "planted" slide, within 1 cm (FootIK.PlantHeight). On that measure the walk is 0.5 cm, the brisk walk 0.7-1.1, the run 1.5, the shamble 0.8 and the dead's run 0.7.
    - Foot IK made no difference on flat ground. Setting off with it slid 9.1 cm a planted step, worst 27.5. The trace showed a held foot let go at the 25 cm stretch and locked again the next frame at the clip's place: one 14 m/s frame. That is fixed (below).
  - **Decided.**
    - A foot let go for the stretch is not locked again until the clip lifts it (FootIK Foot.Stretched). Starting with foot IK: 9.1 -> 4.8 cm, worst 27.5 -> 10.4. Nothing else in the table moved.
    - Gaits.Measure's median stays. Don't set speeds from `--calibrate`'s mean: it counts the skims.
    - The lab gains the planted columns, foot-IK twins of the flat gaits and the dead's walks, and `--speedof=CLIP:SCALE` (Gaits.ScaleSpeed, internal, the lab only).
    - Paths: AnimTest's `--out=` is relative to `godot/` (use `--out=../experiments/...`).
  - **Left.**
    - From the front, the walk sets its feet in one line and the run throws its arms out to the sides at shoulder height. The sneak is a deep squat with the knees wide. The lurch, slow walk and sneak flick a toe back as it lifts (3-7 cm, 1-2 frames). All four are in the clips: a Blender pass on art/scripts/derive_clips.py.
    - Stopping (10 cm with IK) and turning on the spot (7.5) mix FootIK's deliberate settling steps, which are lifted on an arc, with real slide. The lab can't tell them apart: it would need a column that leaves out frames where Foot.Step > 0.
    - Not pictured, for want of time or a flag: timed actions on others (eat, cook, dig, hammer: only the hammer was stripped), reloads and the pistol-whip in first person (`--swingat` with an empty gun gives the whip), the sights (`--hold=aim`), the raise (`--raiseat=`), swimming hands, sleeping, the car seat, deaths and ragdolls, animals' walk, run and bite, your hand on a car (`LEAP_SHOT` with `--eye`), and the dog under a car (needs a low eye beside the car).
    - Check green with the self-test (`./check.sh --all`) for commit 4a6c81e.

| motion | driven by | before | after | strip |
|---|---|---|---|---|
| stand (living) | more/idle, People.LivingClip | right | right | gaits-side.png (start rows) |
| walk slow 0.8 | more/walk_slow, Gaits WALKING | toe flick at lift-off (3.2 cm planted) | same (clip) | gaits-side.png |
| walk 1.4 | more/walk | right from the side; feet in one line from the front | same (clip) | gaits-side.png, gaits-front.png |
| brisk walk 2.2-2.5 | more/walk_brisk | right (0.7-1.1 cm) | right | gaits-side.png row 2 |
| jog 3.5 | more/jog | right (0.4 cm) | right | animtest-after.md |
| run/sprint 4.4-5 | more/run | right from the side; arms out sideways from the front | same (clip) | gaits-side.png row 3, gaits-front.png row 2 |
| sneak 1.25 | more/sneak | deep squat, toe flick (6 cm) | same (clip) | gaits-side.png row 7, gaits-front.png row 3 |
| setting off | idle -> walk, FootIK lock | wrong: foot pops ~23 cm in a frame (with IK) | fixed (4.8 cm, smooth) | animtest-after.md |
| stopping | walk -> idle, FootIK steps | can't tell: steps and slide mixed | same | animtest-after.md |
| turning on the spot | more/shuffle, FootIK steps | can't tell (7.5 cm with IK) | same | animtest-after.md |
| hop (a leg lost) | more/hop_l, hop_r | right (0.2 cm) | right | clips-side.png row 11 |
| vault, jump start, in air, land | more/vault, jump_* (People.Leap.cs) | right | right | clips-side.png rows 8-10 |
| flinch (a hit taken) | more/flinch | right | right | clips-side.png row 7 |
| work: hammer | more/work_hammer | right | right | clips-side.png row 12 |
| eat, drink, cook, forage, treat, search, dig | more/* (People.Clips.cs) | not pictured | - | - |
| swing by class (others) | People.Hold.cs grips + walk arms | right (2026-11-12 pass) | not re-pictured | experiments/anim-next/ |
| carry | more/carry_* | not pictured | - | - |
| swim (seen from outside) | ual_swim_fwd, ual_swim_idle | not pictured | - | experiments/anim-next/ |
| sleep, sit in a car, in and out | People.cs, Cockpit | not pictured | - | - |
| dead: idle | more/dead_idle | right | right | animtest-after.md |
| dead: shamble | more/dead_shamble | right (0.8 cm) | right | gaits-side.png row 8 |
| dead: lurch (and brutes) | more/dead_lurch | toe flick at lift-off (6.8 cm) | same (clip) | gaits-side.png rows 9, 13 |
| dead: drag a leg | more/dead_drag_l, _r | right (the dragged foot slides, as meant) | right | gaits-side.png row 10 |
| dead: run | more/dead_run | right (0.7 cm) | right | gaits-side.png row 14 |
| dead: crawl | more/dead_crawl | right (hands 1.8 cm) | right | gaits-side.png row 15 |
| dead: lunge | more/dead_lunge | right | right | clips-side.png row 1 |
| dead: hit front, back | more/dead_hit_* | right | right | clips-side.png rows 2-3 |
| dead: fall forward | more/dead_fall_front | right | right | clips-side.png row 4 |
| dead: rise from back, from front | more/dead_rise_* | right | right | clips-side.png rows 5-6 |
| dead: haul onto a car, across, drop off, pile over a barricade | People.Climb.cs | right (one still, all phases) | right | climb-staged.png |
| dead: bite, batter, each death, ragdoll | People.cs, ragdoll | not pictured | - | - |
| animals over a car (deer bound, bear, wolf) | Animals.cs | right (one still) | right | crossings-staged.png |
| animals: walk, run, bite; dog under a car | Animals.cs | not pictured | - | - |
| first person: punch | Held.cs | right (a straight jab) | right | fp-punch.png, fp-swings.png row 1 |
| first person: knife, machete, axe | Held.cs | right (in hand through the swing) | right | fp-swings.png rows 2-4 |
| first person: pistol, shotgun shot | Held.cs | right (flash at the muzzle, kick) | right | fp-swings.png rows 5-6 |
| first person: reload, whip, sights, raise, put away, swim hands | Held.cs, SwimHands.cs | not pictured | - | - |
