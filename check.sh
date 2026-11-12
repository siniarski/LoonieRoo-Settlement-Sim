#!/usr/bin/env bash
# The check: run it before every commit (a red check means stop and fix).
#   ./check.sh         build everything (the sim, its tests, the harness, the Godot game) and run the sim's tests
#   ./check.sh --all   and the Godot self-test: real input through the running game (a window opens for a few seconds),
#                      run while the sim's tests run
# Godot runs the game's DEBUG build (godot/.godot/mono/temp/bin/Debug), so every check builds that too (build-debug.log,
# started once the Release build is done, beside the tests: ~1.5 s when little changed; with --all the self-test's
# own Debug build does it). A --shot after a green check therefore always sees the sim as it is now. See also shot.sh
# (build Debug and take a picture) and finish.sh (check, then commit if green).
# It prints a short summary; everything else is in .check/ (build.log, build-debug.log, tests-N.log, selftest.log): read
# those rather than running the check again to see more.
# The sim's tests run as SHARDS processes at once (default 10: 10 s here on 20 cores, 13-14 s with 6), each a set of whole test classes, balanced by how long
# each class took last time (.check/test-times.txt, else the committed cs/Sim.Tests/test-times.txt). Processes,
# not threads: the sim's statics (the clock scale, caches) are per process, and inside one the tests still run one
# at a time in file order. One more process runs whatever test class the list missed.
# Godot is $GODOT, else the usual Windows install.
set -uo pipefail
cd "$(dirname "$0")"
OUT=.check
mkdir -p "$OUT"
rm -f "$OUT"/tests-*.log "$OUT"/tests-*.trx "$OUT"/selftest.log "$OUT"/build.log "$OUT"/build-debug.log
SHARDS="${SHARDS:-10}"
ALL=0; [ "${1:-}" = "--all" ] && ALL=1
start=$(date +%s)
secs() { echo "$(( $(date +%s) - start )) s"; }

# --- Build (the sim, its tests, the game in Release; the game in Debug follows, beside the tests: Godot runs Debug) ---
if ! dotnet build cs/LoonieRoo.slnx -c Release -v q -nologo > "$OUT/build.log" 2>&1; then
  grep -E ' error ' "$OUT/build.log" | sed 's/^ *//' | sort -u | head -30
  echo "check: RED, the build failed ($OUT/build.log)"; exit 1
fi

# --- The sim's tests, sharded ---
classes=$(grep -lE '\[(Fact|Theory)' cs/Sim.Tests/*.cs | xargs grep -hoE '^(public |internal |sealed |static |partial )*class \w+' | awk '{print $NF}' | sort -u)
times="$OUT/test-times.txt"; [ -s "$times" ] || times=cs/Sim.Tests/test-times.txt
# Longest first, each onto the lightest shard so far (unknown classes count 0.2 s). Prints one filter per shard.
filters=$(echo "$classes" | awk -v n="$SHARDS" -v tf="$times" '
  BEGIN { while ((getline l < tf) > 0) { split(l, f, " "); t[f[2]] = f[1] } }
  { c[NR] = $1; w[NR] = ($1 in t) ? t[$1] : 0.2 }
  END {
    for (i = 1; i <= NR; i++) for (j = i + 1; j <= NR; j++) if (w[j] > w[i]) { x = w[i]; w[i] = w[j]; w[j] = x; x = c[i]; c[i] = c[j]; c[j] = x }
    for (i = 1; i <= NR; i++) {
      b = 0; for (k = 1; k < n; k++) if (load[k] < load[b]) b = k
      load[b] += w[i]; s[b] = s[b] (s[b] == "" ? "" : "|") "FullyQualifiedName~." c[i] ".|FullyQualifiedName~." c[i] "+"
    }
    for (k = 0; k < n; k++) if (s[k] != "") print s[k]
  }')
# The rest: any test in a class the list above didn't see (declared some other way). Usually none.
rest=$(echo "$classes" | awk '{ printf "%sFullyQualifiedName!~.%s.&FullyQualifiedName!~.%s+", (NR > 1 ? "&" : ""), $1, $1 }')
pids=(); i=0
while IFS= read -r f; do
  dotnet test cs/Sim.Tests -c Release --no-build -v q -nologo --filter "$f" --results-directory "$OUT" --logger "trx;LogFileName=tests-$i.trx" --logger "console;verbosity=normal" > "$OUT/tests-$i.log" 2>&1 &
  pids+=($!); i=$((i + 1))
done <<< "$filters"
dotnet test cs/Sim.Tests -c Release --no-build -v q -nologo --filter "$rest" --results-directory "$OUT" --logger "trx;LogFileName=tests-rest.trx" --logger "console;verbosity=normal" > "$OUT/tests-rest.log" 2>&1 &
pids+=($!)

# --- The game's Debug build, meanwhile (after the Release build: two builds at once fight over the sim's obj/). With
# --all the self-test below builds it, so not twice. ---
debug_ok=1; dbpid=""
if [ $ALL = 0 ]; then
  dotnet build godot/LoonieRoo.csproj -v q -nologo > "$OUT/build-debug.log" 2>&1 &
  dbpid=$!
fi

# --- The self-test, meanwhile ---
selftest_ok=1
if [ $ALL = 1 ]; then
  GODOT="${GODOT:-/c/Program Files/Godot_v4.6.1-stable_mono_win64/Godot_v4.6.1-stable_mono_win64_console.exe}"
  {
    if [ ! -x "$GODOT" ]; then echo "no Godot at $GODOT (set GODOT)"; exit 1; fi
    dotnet build godot/LoonieRoo.csproj -v q -nologo > "$OUT/build-debug.log" 2>&1 || { grep -E ' error ' "$OUT/build-debug.log" | sed 's/^ *//' | sort -u | head -30; echo "the game's Debug build failed"; exit 1; }
    # Import the assets first: a fresh checkout or worktree has no godot/.godot/imported, and new models
    # from a merge need importing too. ~9 s the first time, ~3 s when there's nothing new.
    timeout 600 "$GODOT" --headless --path godot --import >/dev/null 2>&1 || { echo "importing the assets failed: run $GODOT --headless --path godot --import to see why"; exit 1; }
    code=0
    timeout 180 "$GODOT" --path godot -- --selftest || code=$?
    [ "$code" = 124 ] && echo "selftest: timed out after 180 s (the last PASS/FAIL above is where it stopped)"
    exit 0
  } > "$OUT/selftest.log" 2>&1 &
  stpid=$!
fi

# --- Wait, then sum up ---
tests_ok=1
for p in "${pids[@]}"; do wait "$p" || tests_ok=0; done
passed=0; failed=0
for n in $(seq 0 $((i - 1))) rest; do
  t="$OUT/tests-$n.trx"
  # The counts from the trx. The catch-all writes none when it has nothing to run (its exit code was checked above).
  [ -s "$t" ] || { [ $n = rest ] || { tests_ok=0; echo "no result from shard $n (crashed or failed to start): $OUT/tests-$n.log"; }; continue; }
  read -r np nf < <(grep -o '<Counters [^>]*>' "$t" | awk '{ p = 0; f = 0; if (match($0, / passed="[0-9]+"/)) p = substr($0, RSTART + 9, RLENGTH - 10); if (match($0, / failed="[0-9]+"/)) f = substr($0, RSTART + 9, RLENGTH - 10); print p, f }')
  passed=$((passed + ${np:-0})); failed=$((failed + ${nf:-0}))
done
[ "$failed" -gt 0 ] && tests_ok=0
# Each class's time this run, for the next run's balance (only from a full, green run: a red one stops early).
if [ $tests_ok = 1 ]; then
  cat "$OUT"/tests-*.trx 2>/dev/null | awk '
    /<UnitTestResult / { match($0, /testId="[^"]*"/); id = substr($0, RSTART + 8, RLENGTH - 9); match($0, /duration="[^"]*"/); d = substr($0, RSTART + 10, RLENGTH - 11); split(d, h, ":"); dur[id] = h[1] * 3600 + h[2] * 60 + h[3] }
    /<UnitTest / { match($0, / id="[^"]*"/); cur = substr($0, RSTART + 5, RLENGTH - 6) }
    /<TestMethod / { match($0, /className="[^"]*"/); c = substr($0, RSTART + 11, RLENGTH - 12); sub(/\+.*/, "", c); sub(/.*\./, "", c); cls[cur] = c }
    END { for (id in cls) sum[cls[id]] += dur[id]; for (c in sum) printf "%.2f %s\n", sum[c], c }' | sort -rn > "$OUT/test-times.new"
  [ -s "$OUT/test-times.new" ] && mv "$OUT/test-times.new" "$OUT/test-times.txt" || rm -f "$OUT/test-times.new"
fi
if [ $tests_ok = 1 ]; then
  echo "tests: $passed passed ($(secs), $i shards)"
else
  echo "tests: $failed FAILED, $passed passed"
  # The failures with their messages: "  Failed <name> [time]", the error, the stack (the framework's frames left out).
  awk '/^  Failed / { on = 1 } /^  (Passed|Skipped) |^$|^Test Run|^Total tests/ { on = 0 } on && !/^ +at (Xunit|System)\./' "$OUT"/tests-*.log | head -80
fi

if [ -n "$dbpid" ] && ! wait "$dbpid"; then
  debug_ok=0
  grep -E ' error ' "$OUT/build-debug.log" | sed 's/^ *//' | sort -u | head -30
  echo "build: the game's Debug build FAILED ($OUT/build-debug.log)"
fi

if [ $ALL = 1 ]; then
  wait "$stpid"
  grep -E '^(FAIL|selftest)|^no Godot|failed' "$OUT/selftest.log" | grep -vE '^PASS|^selftest: all passed'
  grep -q '^selftest: all passed' "$OUT/selftest.log" || { selftest_ok=0; grep -q '^selftest' "$OUT/selftest.log" || echo "selftest: no result: see $OUT/selftest.log and the Godot log"; }
  [ $selftest_ok = 1 ] && echo "selftest: $(grep -c '^PASS' "$OUT/selftest.log") checks passed ($(secs))"
fi

if [ $tests_ok = 1 ] && [ $selftest_ok = 1 ] && [ $debug_ok = 1 ]; then echo "check: green ($(secs))"; else echo "check: RED (logs in $OUT/)"; exit 1; fi
