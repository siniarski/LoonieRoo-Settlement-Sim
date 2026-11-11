#!/usr/bin/env bash
# The models' pictures, the same before and after:  experiments/textures/models/shots.sh before|after [view...]
# (run from the repository's root; pictures land in experiments/textures/models/<name>-<view>.png).
set -uo pipefail
n="${1:-after}"
shift || true
d=experiments/textures/models
W="--seed=42 --size=256 --start=solo --day=1.3 --frames=80"
views="${*:-furniture furniture-near sofa bed kitchen items items-near street truck car-near drive farm sites}"
for v in $views; do
  case "$v" in
    furniture)      a="--at=150,90 --lineup=furniture --look=0 --pitch=-12";;
    furniture-near) a="--at=150,90 --lineup=furniture --eye=152.2,89.2,1.5 --look=20 --pitch=-30";;
    sofa)           a="--at=150,90 --lineup=furniture --eye=158,94.4,1.5 --look=90 --pitch=-28";;
    bed)            a="--at=150,90 --lineup=furniture --eye=155,82.2,1.6 --look=90 --pitch=-32";;
    kitchen)        a="--at=150,90 --lineup=furniture --eye=163,88.2,1.6 --look=90 --pitch=-22";;
    items)          a="--at=150,90 --lineup --look=0 --pitch=-35";;
    items-near)     a="--at=150,90 --lineup --eye=151.2,89.6,0.9 --look=10 --pitch=-50";;
    street)         a="--at=150,90 --car=152,92,0 --look=20 --pitch=-8";;
    truck)          a="--at=150,90 --car=152,92,0,truck --look=20 --pitch=-8";;
    car-near)       a="--at=150,90 --car=152,92,0 --eye=150.6,91.2,1.6 --look=30 --pitch=-25";;
    drive)          a="--at=150,90 --car=152,92,0 --drive --pitch=-12";;
    farm)           a="--at=150,90 --stage=farm --look=0 --pitch=-25";;
    sites)          a="--at=150,90 --stage=sites --look=0 --pitch=-18";;
    *) echo "no view $v"; continue;;
  esac
  ./shot.sh $W $a --shot=$d/$n-$v.png | tail -2
done
