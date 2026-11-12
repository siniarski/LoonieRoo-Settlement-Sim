Animation test, logic `after`: a pz survivor model as People.cs plays it, moved along the ground. Slide: how far the planted foot travels along the ground while down (within 2.5 cm), per step (cm); planted: the same within 1 cm (FootIK.PlantHeight). Sole: the planted sole's height over the ground (cm, mean; worst sink).

| case | clips | steps | slide mean | slide worst | planted mean | planted worst | sole mean | worst sink |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| flat 0.8 m/s | walk_slow | 7 | 4.7 | 8.7 | 3.2 | 7.2 | 0.1 | -0.1 |
| flat 1.4 m/s | walk | 11 | 4.9 | 11.2 | 0.5 | 1.1 | 0.2 | -0.1 |
| flat 2.2 m/s | walk_brisk | 11 | 2.6 | 4.9 | 0.7 | 2.1 | 0.1 | -0.4 |
| flat 2.5 m/s | walk_brisk | 10 | 3.1 | 5.3 | 1.1 | 2.0 | 0.1 | -0.4 |
| flat 3.5 m/s | jog | 10 | 0.7 | 1.4 | 0.4 | 0.6 | 0.2 | -0.3 |
| flat 4.4 m/s | run | 9 | 5.7 | 8.9 | 1.6 | 2.9 | -0.1 | -0.9 |
| flat 5.0 m/s | run | 11 | 5.2 | 9.3 | 1.5 | 3.6 | -0.1 | -0.9 |
| flat 0.8 m/s, foot IK | walk_slow | 7 | 5.5 | 9.8 | 3.9 | 8.3 | 0.1 | -0.1 |
| flat 1.4 m/s, foot IK | walk | 11 | 5.4 | 12.3 | 0.9 | 2.0 | 0.2 | -0.1 |
| flat 2.5 m/s, foot IK | walk_brisk | 10 | 3.3 | 5.8 | 1.9 | 2.5 | 0.1 | -0.4 |
| flat 4.4 m/s, foot IK | run | 9 | 7.2 | 10.4 | 3.1 | 4.3 | -0.1 | -0.9 |
| stopping from 2.5 | idle | 2 | 16.4 | 24.1 | 15.1 | 24.1 | 0.1 | -1.2 |
| starting to 2.5 | walk_brisk, walk_slow | 4 | 13.6 | 29.5 | 12.3 | 28.3 | -0.1 | -2.2 |
| turning 90 deg on the spot | idle, shuffle | 4 | 39.7 | 57.1 | 34.5 | 44.7 | -0.5 | -4.3 |
| stopping from 2.5, foot IK | idle | 2 | 11.5 | 17.7 | 10.2 | 17.7 | 0.1 | -1.2 |
| starting to 2.5, foot IK | walk_brisk, walk_slow | 4 | 8.9 | 19.6 | 4.8 | 10.4 | 0.1 | -2.0 |
| turning 90 deg on the spot, foot IK | idle, shuffle | 6 | 12.1 | 36.5 | 7.5 | 26.7 | -0.4 | -4.3 |
| sneaking 1.25 | sneak | 5 | 6.2 | 9.8 | 6.0 | 9.7 | 0.1 | -0.1 |
| dead walker 1.68 (shamble) | dead_shamble | 21 | 4.9 | 20.3 | 0.8 | 10.2 | 0.2 | -0.2 |
| dead walker 1.68 (lurch) | dead_lurch | 11 | 9.3 | 17.3 | 6.8 | 15.1 | 0.0 | -0.7 |
| dead walker 1.68 (dragging left) | dead_drag_l | 18 | 13.4 | 35.4 | 5.3 | 22.4 | -0.2 | -2.9 |
| dead walker 1.68 (dragging right) | dead_drag_r | 17 | 18.1 | 53.6 | 8.9 | 39.5 | -0.2 | -2.7 |
| dead walker 1.68 (shamble), foot IK | dead_shamble | 21 | 5.0 | 20.3 | 1.1 | 10.2 | 0.2 | -0.2 |
| dead walker 1.68 (lurch), foot IK | dead_lurch | 11 | 10.2 | 17.5 | 7.7 | 15.7 | 0.0 | -0.7 |
| dead walker 1.68 (dragging right), foot IK | dead_drag_r | 17 | 17.5 | 49.5 | 8.1 | 35.0 | -0.2 | -2.7 |
| dead brute 1.25 | dead_lurch | 8 | 9.0 | 17.1 | 7.3 | 15.4 | 0.0 | -0.7 |
| dead runner 2.5 | dead_run | 6 | 0.9 | 1.3 | 0.7 | 0.9 | 0.1 | -0.4 |
| left leg lost, 2.0 | hop_l | 14 | 1.0 | 1.6 | 0.2 | 0.8 | 0.1 | -0.1 |
| left leg lost, standing | stand_l | 1 | 1.1 | 1.1 | 1.1 | 1.1 | -0.0 | -0.0 |
| left arm lost, walking 1.4 (the stump strays up to 1 deg from held) | walk | 11 | 4.9 | 11.2 | 0.5 | 1.1 | 0.2 | -0.1 |
| dead idle, its target to its left (its face 27 deg off it) | dead_idle | 2 | 1.1 | 1.2 | 1.1 | 1.2 | 0.1 | -0.0 |
| dead crawler 1.45 (hands) | dead_crawl | 11 | 4.1 | 5.7 | 1.8 | 2.6 | 0.6 | 0.0 |
| up 8 deg at 2.5 | walk_brisk | 10 | 6.8 | 10.3 | 6.8 | 10.3 | -1.5 | -4.7 |
| up 8 deg at 2.5, foot IK | walk_brisk | 10 | 3.3 | 4.3 | 1.8 | 2.2 | 0.1 | -0.3 |
| down 8 deg at 2.5 | walk_brisk | 11 | 20.8 | 30.0 | 17.7 | 21.8 | -2.4 | -7.4 |
| down 8 deg at 2.5, foot IK | walk_brisk | 10 | 4.4 | 6.7 | 2.2 | 3.0 | 0.1 | -0.4 |
