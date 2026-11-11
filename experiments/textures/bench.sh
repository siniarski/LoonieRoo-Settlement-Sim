#!/usr/bin/env bash
# Frame times in the pictures' places (the light bench at the default budget: 300 frames after 90 warm, vsync off):
#   experiments/textures/bench.sh          prints a row per place and run (frame ms, GPU ms, CPU render ms)
set -uo pipefail
run() {
  echo "$1"
  shift
  for k in 1 2; do ./shot.sh "$@" --lightbench=24 > /dev/null 2>&1; grep '^| 24' .check/shot.log; done
}
run street  --seed=42 --size=256 --start=solo --at=150,90 --look=20 --day=1.3
run house   --seed=42 --size=256 --start=solo --at=130,106 --look=335 --day=1.3
run country --seed=42 --size=256 --start=solo --at=150,60 --look=300 --pitch=-8 --day=1.3
run down    --seed=42 --size=256 --start=solo --at=150,90 --look=20 --pitch=-60 --day=1.3
