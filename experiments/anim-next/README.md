# In hand, in water, over cars (2026-11-12)

Log: log/2026-11-12-10-animation.md. Docs: docs/characters.md "In hand, in water, over cars". `before/` is the code
before each change, `after/` the code after it.

- `grip-<kind>.png` (bat, knife, axe, pistol, rifle, wood): `grips.sh before|after`. These are AnimTest sheets with
  one row per clip and view: idle, walk and run, from the side and the front. `grip-wood.png` in after/ uses the carry
  clips (`KINDS=wood CLIPS=more/carry_idle,more/carry_walk`).
- `swim-clips.png` (before): the library's swim clips in the lab.
- `swim-outside.png`: before is the rivers session's picture (standing on the bed). After:
  `./shot.sh --seed=3 --size=256 --at=123,96 --look=180 --hold=w --ticks=25 --day=1.5 --eye=124.8,96.9,2.4 --pitch=-28`.
- `after/swim-look-down.png`: first person looking down while swimming
  (`--at=121,97 --look=0 --ticks=25 --pitch=-70`): the stroke's hands, no body.
- `after/wading-outside.png`: `--at=116,95 --look=90 --hold=w --ticks=12 --eye=117.6,93.2,2.6 --pitch=-25`.
- `climb-paused.png`: the README's `--stage=climb --paused` view. It is the same before and after, because every
  staged body is mid-leg on the top. `after/climb-4.png` and `-14.png` come from `--shot-at`, but the staged scene
  doesn't move in them. The haul and the drop are checked by the self-test's text.
- `animals.png`: `--seed=42 --size=256 --at=140,96 --stage=crossings --paused --day=1.3 --eye=141.5,92.6,2.0
  --look=0 --pitch=-10`. The deer is mid-bound over the sedan, the bear climbing behind it.
