#!/usr/bin/env bash
# Frame times among models (the light bench at the default budget: 300 frames after 90 warm, vsync off), three runs a place:
#   experiments/textures/models/bench.sh        prints a row per place and run (frame ms, GPU ms, CPU render ms)
set -uo pipefail
run() {
  echo "$1"
  shift
  for k in 1 2 3; do ./shot.sh "$@" --lightbench=24 > /dev/null 2>&1; grep '^| 24' .check/shot.log; done
}
run house     --seed=42 --size=256 --start=solo --at=130,106 --look=335 --day=1.3
run furniture --seed=42 --size=256 --start=solo --at=150,90 --lineup=furniture --look=0 --pitch=-12 --day=1.3
