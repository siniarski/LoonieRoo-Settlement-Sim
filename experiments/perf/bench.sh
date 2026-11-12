#!/usr/bin/env bash
# The whole game's frame and chunk build by place (log/2026-11-12-17-performance.md): the light bench at the default
# budget (300 frames after 90 warm, vsync off), the chunk timer over the run (cold builds, JIT included) and the chunk
# bench after it (every chunk in range built three times over, warm, by part), near chunks only and the far view at its default.
#   experiments/perf/bench.sh [runs] [places]   a row per place, view and run (places: street,house,country,river,crowd)
#   OPT=1 experiments/perf/bench.sh             the same on an optimized build (the Debug config with Optimize=true,
#                                               as a player's export is optimized); the Debug build is put back after
set -uo pipefail
RUNS=${1:-2}
PLACES=${2:-street,house,country,river,crowd}
GODOT=${GODOT:-"/c/Program Files/Godot_v4.6.1-stable_mono_win64/Godot_v4.6.1-stable_mono_win64.exe"}
LOG=.check/shot.log
mkdir -p .check
if [ "${OPT:-0}" = 1 ]; then
  dotnet build godot/LoonieRoo.csproj -v q -nologo --no-incremental -p:Optimize=true > .check/perf-build.log 2>&1 || { echo "build failed"; exit 1; }
  game() { timeout 300 "$GODOT" --path godot -- "$@" > "$LOG" 2>&1; }
else
  game() { ./shot.sh "$@" > /dev/null 2>&1; }
fi
run() {
  name=$1; shift
  case ",$PLACES," in *",$name,"*) ;; *) return ;; esac
  for view in 0 500; do
    for k in $(seq 1 "$RUNS"); do
      game "$@" --view=$view --lightbench=24
      echo "$name view=$view: $(grep '^| 24' $LOG | cut -d'|' -f5-7) $(grep '^chunks:' $LOG | sed 's/ signatures.*//') $(grep '^chunk bench' $LOG | sed 's/chunk bench: [0-9]* chunks x 3, //')"
    done
  done
}
run street  --seed=42 --size=256 --start=solo --at=150,90 --look=20 --day=1.3
run house   --seed=42 --size=256 --start=solo --at=130,106 --look=335 --day=1.3
run country --seed=42 --size=256 --start=solo --at=150,60 --look=300 --pitch=-8 --day=1.3
run river   --seed=42 --size=256 --start=solo --at=166,212 --look=60 --day=1.3
run crowd   --arena=crowd --look=0 --day=1.3
if [ "${OPT:-0}" = 1 ]; then dotnet build godot/LoonieRoo.csproj -v q -nologo --no-incremental > .check/perf-build.log 2>&1; fi
