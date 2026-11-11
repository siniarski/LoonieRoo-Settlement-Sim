#!/usr/bin/env bash
# The joins from above (places from cs/Harness "road edges", seed 42 size 256): experiments/smooth-far/joins.sh before|after
set -uo pipefail
p="${1:-after}"
d=experiments/smooth-far
w="--seed=42 --size=256 --start=solo --day=1.3 --frames=60 --shot-width=1100"
./shot.sh $w --at=128,140 --eye=128,142,22 --look=270 --pitch=-75 --shot=$d/$p-junction.png
./shot.sh $w --at=194,100 --eye=194,108,20 --look=270 --pitch=-60 --shot=$d/$p-bridge.png
./shot.sh $w --at=147,100 --eye=149,104,16 --look=270 --pitch=-62 --shot=$d/$p-street.png
