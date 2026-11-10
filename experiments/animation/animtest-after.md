Animation test, logic `after`: a pz survivor model as People.cs plays it, moved along the ground. Slide: how far the planted foot travels along the ground while down, per step (cm). Sole: the planted sole's height over the ground (cm, mean; worst sink).

| case | clips | steps | slide mean | slide worst | sole mean | worst sink |
|---|---|--:|--:|--:|--:|--:|
| flat 0.8 m/s | walk_slow | 7 | 4.7 | 8.7 | 0.1 | -0.1 |
| flat 1.4 m/s | walk | 11 | 4.9 | 11.2 | 0.2 | -0.1 |
| flat 2.2 m/s | walk_brisk | 11 | 2.6 | 4.9 | 0.1 | -0.4 |
| flat 2.5 m/s | walk_brisk | 10 | 3.1 | 5.3 | 0.1 | -0.4 |
| flat 3.5 m/s | jog | 10 | 0.7 | 1.4 | 0.2 | -0.3 |
| flat 4.4 m/s | run | 9 | 5.7 | 8.9 | -0.1 | -0.9 |
| flat 5.0 m/s | run | 11 | 5.2 | 9.3 | -0.1 | -0.9 |
| stopping from 2.5 | idle | 2 | 16.4 | 24.1 | 0.1 | -1.2 |
| starting to 2.5 | walk_brisk, walk_slow | 4 | 13.6 | 29.5 | -0.1 | -2.2 |
| turning 90 deg on the spot | idle, shuffle | 4 | 39.7 | 57.1 | -0.5 | -4.3 |
| stopping from 2.5, foot IK | idle | 2 | 11.5 | 17.7 | 0.1 | -1.2 |
| starting to 2.5, foot IK | walk_brisk, walk_slow | 4 | 11.2 | 28.8 | -0.0 | -2.0 |
| turning 90 deg on the spot, foot IK | idle, shuffle | 6 | 12.1 | 36.5 | -0.4 | -4.3 |
| sneaking 1.25 | sneak | 5 | 6.2 | 9.8 | 0.1 | -0.1 |
| dead walker 1.68 (shamble) | dead_shamble | 21 | 4.9 | 20.3 | 0.2 | -0.2 |
| dead walker 1.68 (lurch) | dead_lurch | 11 | 9.3 | 17.3 | 0.0 | -0.7 |
| dead walker 1.68 (dragging left) | dead_drag_l | 18 | 13.4 | 35.4 | -0.2 | -2.9 |
| dead walker 1.68 (dragging right) | dead_drag_r | 17 | 18.1 | 53.6 | -0.2 | -2.7 |
| dead brute 1.25 | dead_lurch | 8 | 9.0 | 17.1 | 0.0 | -0.7 |
| dead runner 2.5 | dead_run | 6 | 0.9 | 1.3 | 0.1 | -0.4 |
| dead crawler 1.45 (hands) | dead_crawl | 11 | 4.1 | 5.7 | 0.6 | 0.0 |
| up 8 deg at 2.5 | walk_brisk | 10 | 6.8 | 10.3 | -1.5 | -4.7 |
| up 8 deg at 2.5, foot IK | walk_brisk | 10 | 3.3 | 4.3 | 0.1 | -0.3 |
| down 8 deg at 2.5 | walk_brisk | 11 | 20.8 | 30.0 | -2.4 | -7.4 |
| down 8 deg at 2.5, foot IK | walk_brisk | 10 | 4.4 | 6.7 | 0.1 | -0.4 |
