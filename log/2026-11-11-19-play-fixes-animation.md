- **2026-11-11, play fixes: animation.** The player's play by hand: "survivors have a funny third person animation
  when moving around", "survivor walking animation has the arms in the wrong place (behind back)", and "some
  animations still play when the game is paused". Both reproduced, both fixed.
  - **The arms.** Reproduced with the animation lab's strips (`Labs/AnimTest.tscn --strip=6,8 --clips=...`) and in the
    world (`--start=group --play --eye=...`, a colonist walking, from the side and behind). The library's walk and the
    derived walk were right; the derived brisk walk (what every tile-walker plays at the sim's 2.5 m/s) put the hands
    up behind the back, and the jog (the sim's 3.3 m/s) held them together at the chest. Not the hand IK (others have
    none) nor a rest pose: art/scripts/derive_clips.py's `mean_rot` averaged the arm bones' rotations into a 4D
    mathutils Vector and called `.normalized()`, which normalises the first three parts only and leaves the 4th (as a
    homogeneous w): the mean came out ~12 long (z 11.56 for the left upper arm), and `scale_swing` (any gait with
    `arm=` other than 1: walk_slow, walk_brisk, jog, run, sneak, shuffle, dead_run, and carry_brisk built on the
    brisk walk) threw the arms. Found by a Blender probe of the scaled frames (the hand 100+ m off before keying
    renormalised it). Now divided by its length; clips_more.glb baked again (47 clips, speeds and strides unchanged:
    clips_more.json identical). The walks swing the arms at the sides, a carry holds them in front, the jog and run
    have their arms; the dead's run is right; your body (the same clips) too.
  - **Held still.** Reproduced: `--ticks=22 --paused`, pictures at frame 10 and 150 differ (a colonist's stride, the
    dead's poses). The world's motion ran on frame time while the sim was held: People's and the animals'
    AnimationPlayers on Godot's own clock (the animals measured their own delta from the wall clock), the held
    item's bob, the stand-ins, the lights' flicker, the helicopter on the frame's delta, the fire's shaders on `TIME`,
    rain and snow particles. The rule (docs/characters.md "Held still"): while `view.halted` (the menu, the map,
    `--paused`), what moves gets no time; what eases to the world as it stands (the light, the weather's look, the
    dashboard's needles) keeps the frame's, so `--paused` pictures still settle their light. One place: Game.cs
    passes FirstPerson a world delta (0 while held); FirstPerson hands it to what moves; the players are on manual,
    advanced by it; Fires has a `clock` uniform in place of `TIME`; Weather sets the particles' speed to 0 when held.
    Left going: the ambient sound loops (a held game silent sounds broken), the eye's 0.28 s landing dip. No wind
    animates the trees or the grass at all (no shader there reads `TIME`), so there was nothing to decide there.
    After: the two pictures are the same.
  - **Decided.** The arm-swing fix is in the bake script, not a patch in Godot: the clips are the truth. The cockpit
    keeps the frame's delta (its needles only ease to the car's state; the car self-test pauses the world and reads
    them), and it is the cars session's file.
  - **Left.** Others hold nothing: an armed survivor walks as an unarmed one (no weapon drawn on others), and a
    carry's arms hold no visible load. The jog at 3.3 m/s is what the sim asks for (a colonist's pace): a slower
    colonist walk is the sim's call. The mid tier (no IK) still slides at stops. Pictures before and after:
    experiments/play-fixes/animation/ (README there). Self-test steps (SelfTestBodies.cs): the walks' hands by the
    hips, the carries' in front, no playhead moving while held, yours moving on after. Check: see the commit.
