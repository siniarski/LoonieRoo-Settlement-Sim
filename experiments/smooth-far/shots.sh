#!/usr/bin/env bash
# The far field's pictures (experiments/smooth-far/), from the checkout it is run in:
#   experiments/smooth-far/shots.sh after [DIR]     DIR: where the pictures go (default experiments/smooth-far)
# Seed 42 at 2048 tiles, on the link road where four plan regions meet (1124, 1024: the draw bench's place): along
# the road east at eye level, and from 60 m up.
set -uo pipefail
p="${1:-after}"
d="${2:-experiments/smooth-far}"
w="--seed=42 --size=2048 --start=solo --day=1.3 --ticks=5 --view=500 --frames=200 --shot-width=1280"
SHOT_TIMEOUT=300 ./shot.sh $w --at=1124,1026 --look=10 --pitch=-3 --shot=$d/$p-ground.png
SHOT_TIMEOUT=300 ./shot.sh $w --at=1124,1026 --eye=1100,1060,60 --look=330 --pitch=-28 --shot=$d/$p-rise.png
