#!/usr/bin/env bash
# The four views of the textures work, the same before and after:  experiments/textures/shots.sh before|after
# (run from the repository's root; pictures land in experiments/textures/<name>-<view>.png).
set -uo pipefail
n="${1:-after}"
d=experiments/textures
./shot.sh --seed=42 --size=256 --start=solo --at=150,90 --look=20 --day=1.3 --frames=70 --shot=$d/$n-street.png
./shot.sh --seed=42 --size=256 --start=solo --at=130,106 --look=335 --day=1.3 --frames=70 --shot=$d/$n-house.png
./shot.sh --seed=42 --size=256 --start=solo --at=150,60 --look=300 --pitch=-8 --day=1.3 --frames=70 --shot=$d/$n-country.png
./shot.sh --seed=3 --day=17.33 --weather=snow --snow=22 --at=27,102 --ticks=3 --frames=70 --shot=$d/$n-snow.png
./shot.sh --seed=42 --size=256 --start=solo --at=150,90 --look=20 --hour=22 --give=flashlight:on --frames=70 --shot=$d/$n-night.png
