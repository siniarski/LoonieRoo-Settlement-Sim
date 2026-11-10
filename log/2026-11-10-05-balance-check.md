- **2026-11-10, balance check: thirst since organic worlds, and animals off the grid.** Two worries were open in
  docs/open-work.md: 3 thirst deaths in 29 runs after organic generation, where the report had said none; and, since
  animals moved off the grid, a colony falling on day 12 (seed 11), no colony alive at day 25, and solo deaths by
  zombies 3 -> 7 of 16. Both were measured on more seeds against the commits before each change. Verdict: **neither is
  real.** Two bugs in the scripted players were fixed. No sim change.
  - **How.** Each old commit was built in its own temporary worktree: before organic f4c78b5, before animals 08086dd,
    animals 4d95ce7. Each got the same read-only `Probe` (cs/Harness/Probe.cs, `PROBE=path`). It records every event
    (the state keeps only the last 50), each death with the dying one's last 40 notes, drinks by source, prey caught
    (an animal gone in a tick with new food beside it), meat it gave, and thirst against the other needs. 25 days, 8
    runs at a time, about 50 s a colony. Both old commits reproduce the earlier logs seed for seed: colony wipe days
    22,17,23,23,21,21,25,alive,21,24,21,22,25 before animals and 22,18,23,21,21,22,21,22,24,22,12,19,25 after. The
    organic log's death counts come out the same too (main: zombie 248, bleeding 42, cold 34, thirst 2).
  - **Thirst: the harness under-counted, and the world had nothing to do with it.** The colony harness read
    `state.events` once a day, and the state keeps only the last 50, so a bad day lost its oldest events: about a tenth
    of all deaths (main seeds 1-13: zombies 275, not 248). Counted in full, **the commit before organic generation has
    4 colony thirst deaths in 13 runs and main has 3.** Over 30 seeds: 10 before animals, 9 after, 8 on main, out of
    about 900 deaths each. No thirst death involved a river or lake. Across 30 colonies on main, colonists drank 11,947
    times at the well, 6,646 at the rain collectors and 36 times at open water (2 got sick). Of 12 colony thirst deaths
    (main 8, pre-organic 4), 10 were 3-16 tiles from the colony's own well or collectors. Every one was already badly
    hurt: they died 56-495 ticks after running dry (a healthy person lasts 2,000). The cause is whatever hurt them last
    (Water.cs sets `lastHurtBy = "thirst"` every 2 ticks while dry). What kept them from water:
    - 9 of the 12: a meal first, then a walk to water. In Tick.behave, `updateMeal` comes before `updateDrink`, and
      `Needs.startMeal` cancels a drink under way. In the 30 colonies, thirsty colonists started a meal 4,432 times
      and dropped a drink already under way for food 2,020 times.
    - Also seen: a night in bed (colonists ran dry asleep 390 times), being woken by the dead, breakdowns, and one
      scavenger 52 tiles out who couldn't reach any water.
  - **Thirst: experiment, not kept** (Needs.cs in a throwaway worktree, 30 seeds against main):
    - A, drink before eating when thirsty: colony thirst deaths 8 -> 5, starvation 31 -> 42, mean fall day +0.3
      (sign-flip permutation p 0.69). The wounded die of something else.
    - A plus B (wake a sleeper who has run dry): thirst 7, starvation 47, fall day +0.1. Nothing gained.
    So the colony's need order is not worth changing for balance.
  - **Thirst, solo: the scripted player.** The bot drank only at wells, rain collectors and taps within 40 tiles,
    never at open water or a tank. Thirst deaths in 40 seeds: 5 before animals, 7 after, 7 on main (1 of 16 on the
    original seeds). One died 1 tile from a pond. **Fixed** (SoloHarness): with none of those, it makes for the sim's
    own nearest water (`Water.findWater`: a tank, a pond, a river), looked up every 40 ticks. Main, 40 seeds: thirst
    7 -> 2 (those 7 now die of starvation 3 more, zombies 2 more), mean death day +0.2. The 2 left were fighting the
    dead (fights come before drinking for the bot); one was making for a clean tank 28 tiles off rather than a pond 7
    off (findWater takes clean water first). Pre-organic with the fix: 0 thirst of 40. Day of death, pre-organic vs
    main with the fix: +0.55 days on main (p 0.21).
  - **Animals: noise for the colony.** Colony, seeds 1-30, before (08086dd) vs after (4d95ce7):
    - fell 28 vs 30 of 30; alive at day 25: 2 vs 0 (Fisher p 0.49).
    - Mean wipe day 21.8 vs 21.3; paired by seed -0.5 days, later on 8, earlier on 14, same on 6 (p 0.48).
    - Deaths: zombie 692 vs 663, bleeding 116 vs 109, cold 59 vs 79, starvation 27 vs 29, wolves 6 vs 7.
    - Seed 11's day 12 has a twin before the change: seed 26 fell on day 8.
    - Main, 30 seeds: 27 of 30 fell, 3 alive at day 25, mean 21.6.

    Hunting is the same before and after, and tiny. Deer seen 480 / 473 / 486 (before / after / main), deer caught
    8 / 12 / 5, rabbits 5 / 0 / 0. Meat, mostly from bears and wolves the guards kill: 259 / 260 / 242 over 30
    colonies, about 8 food a colony in 25 days. The harness's old "hunted" column counted deer sightings, not kills (a
    colonist's kill logs no event); it is now "deer seen".
  - **Animals: solo, the bot's own change.** Seeds 1-40, before vs after:
    - Deaths by zombies 9 vs 15 (Fisher p 0.22), starvation 23 vs 16; mean death day 4.08 vs 4.42 (+0.35, p 0.06).
    - Prey caught: none in all 80 runs, nor in 80 more on main and pre-organic.
    - The shift is the same commit's change to the bot's swing (by reach, not by tile). Before it, the bot stood
      swinging at animals one tile off but out of reach (21-116 swings on seeds 8, 10, 12, 14, 22, 29, 33, never a
      blow landed) until it starved. After it, those seeds walk after the prey or forage, live a little longer and
      mostly die to the dead.
    - On main, 40 seeds: zombies 5, starvation 27, thirst 7.
  - **Decided.** No retuning. Water is not too far: colonies drink at their own well from day one, and nobody died
    for want of a river. River water is not the problem: 36 drinks in 30 colonies, 2 sick. Animals left balance where
    it was. Fixed, in the harness only:
    - the colony harness drains events every tick (`Probe.newEvents`), so its deaths and counts are complete;
    - the solo bot drinks from any water when it knows no well, collector or tap.
  - **Options for the player (none taken).**
    1. Keep needs as they are (recommended). Colony thirst is about 1% of deaths, all among the badly wounded, and
       moving it costs as many starvation deaths.
    2. Name the death better: "died of their wounds (dehydrated)" when health ran out while dry, so thirst doesn't
       look like a water problem. Water.cs: a label, no balance change.
    3. Drink before eating when both are due (experiment A): thirst 8 -> 5 and starvation 31 -> 42 per 30 colonies;
       only if it reads better to a player who watches colonists walk away from the well to eat.
  - **Left.** Two thirst deaths in 40 solo seeds remain, both mid-fight. Neither the colony's hunting nor the solo
    bot's hunting ever brings in deer: 5-12 of ~480 seen in 30 colonies, none in 160 solo runs. The deer bolt after a
    missed roll, and the hunt order marks only deer. That's a balance question of its own: is deer hunting meant to
    feed anyone?
    - Tests: cs/Harness/HarnessTest.cs, 2 tests: newEvents across the 50-event cap; the solo bot drinks from a pond
      with no well. The pond test fails without the fix. The harness isn't in the check.
    - The probe changes no state: with it, the 30 colony runs on main are identical to without.

### Thirst: colony, seeds 1-13 (thirst deaths: as the old harness reported them / all)

| seed | pre-organic f4c78b5: fell | thirst | main: fell | thirst |
|---|---|---|---|---|
| 1 | 22 | 0 / 0 | 19 | 0 / 0 |
| 2 | 21 | 0 / 0 | 22 | 0 / 0 |
| 3 | 23 | 0 / 1 | 22 | 1 / 1 |
| 4 | 18 | 0 / 0 | 21 | 0 / 1 |
| 5 | 20 | 0 / 0 | 23 | 0 / 0 |
| 6 | 24 | 0 / 1 | 21 | 0 / 0 |
| 7 | 24 | 0 / 1 | 21 | 0 / 0 |
| 8 | alive | 0 / 0 | 24 | 0 / 0 |
| 9 | 22 | 0 / 0 | 21 | 1 / 1 |
| 10 | 22 | 0 / 0 | 23 | 0 / 0 |
| 11 | 17 | 0 / 0 | 24 | 0 / 0 |
| 12 | 22 | 0 / 0 | 21 | 0 / 0 |
| 13 | 24 | 0 / 1 | 22 | 0 / 0 |

### Solo, seeds 1-40: day and cause of death (THIRST in capitals)

| seed | pre-organic | main | pre-organic, bot fixed | main, bot fixed |
|---|---|---|---|---|
| 1 | 3 starv | 5 starv | 3 starv | 5 starv |
| 2 | 3 starv | 4 THIRST | 3 starv | 5 THIRST |
| 3 | 4 starv | 5 starv | 4 starv | 5 starv |
| 4 | 3 zombie | 1 starv | 3 zombie | 1 starv |
| 5 | 1 starv | 3 bleed | 1 starv | 3 bleed |
| 6 | 3 zombie | 1 zombie | 3 zombie | 1 zombie |
| 7 | 3 starv | 3 starv | 3 starv | 3 starv |
| 8 | 3 zombie | 4 starv | 3 zombie | 4 starv |
| 9 | 1 starv | 1 starv | 1 starv | 1 starv |
| 10 | 1 zombie | 1 starv | 1 zombie | 1 starv |
| 11 | 3 starv | 1 starv | 3 starv | 1 starv |
| 12 | 1 starv | 4 starv | 1 starv | 4 starv |
| 13 | 1 starv | 3 zombie | 1 starv | 3 zombie |
| 14 | 4 starv | 1 starv | 4 starv | 1 starv |
| 15 | 1 starv | 4 starv | 1 starv | 4 starv |
| 16 | 4 starv | 3 zombie | 4 starv | 3 zombie |
| 17 | - | 3 starv | 3 starv | 3 starv |
| 18 | - | 4 starv | 4 starv | 4 starv |
| 19 | - | 3 starv | 1 starv | 3 starv |
| 20 | - | 4 THIRST | 1 starv | 7 zombie |
| 21 | - | 3 starv | 6 starv | 3 starv |
| 22 | - | 5 starv | 1 starv | 5 starv |
| 23 | - | 4 starv | 3 starv | 4 starv |
| 24 | - | 6 starv | 3 starv | 6 starv |
| 25 | - | 3 starv | 3 zombie | 3 starv |
| 26 | - | 1 zombie | 7 starv | 1 zombie |
| 27 | - | 1 starv | 3 starv | 1 starv |
| 28 | - | 6 THIRST | 3 starv | 6 starv |
| 29 | - | 1 starv | 5 zombie | 1 starv |
| 30 | - | 4 starv | 1 starv | 4 starv |
| 31 | - | 4 zombie | 1 starv | 4 zombie |
| 32 | - | 3 starv | 1 starv | 3 starv |
| 33 | - | 1 starv | 3 starv | 1 starv |
| 34 | - | 4 THIRST | 4 zombie | 6 starv |
| 35 | - | 3 starv | 6 starv | 3 starv |
| 36 | - | 5 THIRST | 3 starv | 6 starv |
| 37 | - | 4 THIRST | 5 zombie | 5 zombie |
| 38 | - | 6 THIRST | 1 starv | 6 THIRST |
| 39 | - | 4 starv | 3 starv | 4 starv |
| 40 | - | 1 starv | 3 starv | 1 starv |

### Animals: colony, seeds 1-30, day wiped out (harness's day)

| seed | before 08086dd | after 4d95ce7 | main |
|---|---|---|---|
| 1 | 22 | 22 | 19 |
| 2 | 17 | 18 | 22 |
| 3 | 23 | 23 | 22 |
| 4 | 23 | 21 | 21 |
| 5 | 21 | 21 | 23 |
| 6 | 21 | 22 | 21 |
| 7 | 25 | 21 | 21 |
| 8 | alive | 22 | 24 |
| 9 | 21 | 24 | 21 |
| 10 | 24 | 22 | 23 |
| 11 | 21 | 12 | 24 |
| 12 | 22 | 19 | 21 |
| 13 | 25 | 25 | 22 |
| 14 | 21 | 20 | 22 |
| 15 | 20 | 19 | 23 |
| 16 | 21 | 21 | alive |
| 17 | 21 | 21 | 22 |
| 18 | alive | 24 | 21 |
| 19 | 24 | 21 | 22 |
| 20 | 19 | 24 | 22 |
| 21 | 25 | 22 | alive |
| 22 | 23 | 21 | 21 |
| 23 | 21 | 21 | 11 |
| 24 | 25 | 22 | 24 |
| 25 | 25 | 21 | 24 |
| 26 | 8 | 19 | 20 |
| 27 | 21 | 21 | alive |
| 28 | 23 | 24 | 24 |
| 29 | 19 | 22 | 23 |
| 30 | 21 | 24 | 19 |

### Animals: solo, seeds 1-40, day and cause

| seed | before 08086dd (swings at animals) | after 4d95ce7 (swings) |
|---|---|---|
| 1 | 5 bleed (0) | 3 zombie (0) |
| 2 | 4 zombie (0) | 5 starv (1) |
| 3 | 3 starv (1) | 4 starv (1) |
| 4 | 5 THIRST (0) | 5 starv (0) |
| 5 | 4 THIRST (0) | 4 THIRST (0) |
| 6 | 5 starv (1) | 5 starv (0) |
| 7 | 5 THIRST (3) | 5 starv (0) |
| 8 | 4 starv (72) | 4 zombie (0) |
| 9 | 6 starv (0) | 7 bleed (4) |
| 10 | 6 starv (21) | 6 zombie (0) |
| 11 | 1 zombie (0) | 3 zombie (0) |
| 12 | 5 starv (35) | 3 zombie (0) |
| 13 | 5 starv (121) | 5 starv (0) |
| 14 | 5 starv (80) | 6 zombie (1) |
| 15 | 2 THIRST (0) | 2 THIRST (0) |
| 16 | 3 zombie (0) | 4 zombie (0) |
| 17 | 4 starv (0) | 5 zombie (2) |
| 18 | 1 zombie (0) | 4 zombie (0) |
| 19 | 5 zombie (0) | 4 THIRST (0) |
| 20 | 5 zombie (0) | 5 starv (0) |
| 21 | 6 starv (0) | 6 raider (1) |
| 22 | 4 starv (30) | 3 zombie (0) |
| 23 | 1 THIRST (0) | 1 THIRST (0) |
| 24 | 4 starv (10) | 6 starv (1) |
| 25 | 5 starv (98) | 6 starv (3) |
| 26 | 5 starv (0) | 5 starv (0) |
| 27 | 1 zombie (0) | 4 THIRST (0) |
| 28 | 1 zombie (0) | 1 zombie (0) |
| 29 | 5 starv (116) | 5 zombie (0) |
| 30 | 3 starv (3) | 4 THIRST (2) |
| 31 | 5 starv (5) | 6 starv (1) |
| 32 | 5 cold (0) | 6 starv (1) |
| 33 | 5 starv (6) | 4 zombie (0) |
| 34 | 3 starv (0) | 3 starv (0) |
| 35 | 5 cold (0) | 5 zombie (0) |
| 36 | 6 starv (0) | 6 starv (1) |
| 37 | 5 starv (2) | 5 starv (2) |
| 38 | 5 starv (0) | 5 starv (0) |
| 39 | 5 starv (3) | 6 THIRST (4) |
| 40 | 1 zombie (0) | 1 zombie (0) |
