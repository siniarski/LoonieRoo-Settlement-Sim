# Play fixes: animation (2026-11-11)

The player's play of 2026-11-11: survivors walking with "the arms in the wrong place (behind back)", and "some
animations still play when the game is paused". Log: log/2026-11-11-19-play-fixes-animation.md.

`before/` is the commit before the fix (0e9cf36), `after/` the fix. Same commands for both.

**Strips** (the world's animation lab, the clip played once in place, six pictures 8 frames apart, from the side):

```bash
"$GODOT" --path godot res://Labs/AnimTest.tscn -- --strip=6,8 --clips=more/walk,more/walk_brisk,more/walk_slow,more/jog,more/run,more/sneak,more/carry_brisk --out=DIR
```

- `strip-more-walk-brisk.png`: the gait every tile-walker plays at the sim's 2.5 m/s. Before, the hands go up behind the
  back; after, they swing at the sides.
- `strip-more-jog.png`: the sim's survivors moving at 3.3 m/s. Before, hands together at the chest (a boxer's
  shuffle); after, a jog's arms.
- `strip-more-walk-slow`, `-run`, `-sneak`, `-carry-brisk`: also thrown before (any gait whose arm swing is scaled).
- `strip-more-walk.png`, `strip-more-carry-walk.png`, `strip-ual-ual-walk.png` (before only): the unscaled walk and the
  library's, which were always right. `after/strip-more-dead-run.png` and `-dead-shamble.png`: the dead after.

**The world** (`--seed=42 --start=group --play`, a colonist, s5, heading west along y 65.5):

- `world-side-36.png`: `--eye=53,62.3,1.4 --look=90 --pitch=-8 --shot-at=36`, from the side.
- `world-behind-24.png`: `--eye=63,66.2,2.0 --look=185 --pitch=-12 --shot-at=24`, from behind (the
  colonist on the right has an arm out sideways before).

**Held still** (`--ticks=22 --paused`, the same view from behind, a picture after 10 frames and after 150):
`paused-frame10.png` and `paused-frame150.png`. Before, the bodies have moved on between the two (the colonist on the
right, the dead far off); after, the two are the same.
