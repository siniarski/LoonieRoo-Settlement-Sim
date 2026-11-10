Animation test, logic `before`: a pz survivor model as People.cs plays it, moved along the ground. Slide: how far the planted foot travels along the ground while down, per step (cm). Sole: the planted sole's height over the ground (cm, mean; worst sink).

| case | clips | steps | slide mean | slide worst | sole mean | worst sink |
|---|---|--:|--:|--:|--:|--:|
| flat 0.8 m/s | ual_walk | 4 | 37.7 | 54.2 | -2.3 | -5.5 |
| flat 1.4 m/s | ual_walk | 7 | 36.7 | 53.8 | -2.4 | -5.5 |
| flat 2.2 m/s | ual_walk | 9 | 41.6 | 51.1 | -2.4 | -5.5 |
| flat 2.5 m/s | ual_jog_fwd | 7 | 45.9 | 52.9 | -1.9 | -4.1 |
| flat 3.5 m/s | ual_jog_fwd | 9 | 41.8 | 45.2 | -2.3 | -3.9 |
| flat 4.4 m/s | ual_sprint | 10 | 43.6 | 56.2 | -1.8 | -5.5 |
| flat 5.0 m/s | ual_sprint | 11 | 46.0 | 53.2 | -1.8 | -5.1 |
| stopping from 2.5 | ual_idle, ual_walk | 3 | 39.7 | 58.8 | -5.1 | -6.4 |
| starting to 2.5 | ual_jog_fwd, ual_walk | 3 | 53.5 | 79.4 | -4.8 | -8.2 |
| turning 90 deg on the spot | ual_idle | 2 | 47.6 | 50.5 | -5.5 | -5.7 |
| sneaking 1.25 | ual_crouch_fwd | 6 | 102.3 | 440.0 | -1.8 | -5.2 |
| dead walker 1.68 (shamble) | ual_walk | 9 | 31.1 | 41.2 | -2.4 | -5.4 |
| dead walker 1.68 (lurch) | ual_walk | 9 | 31.1 | 41.2 | -2.4 | -5.4 |
| dead walker 1.68 (dragging left) | ual_walk | 9 | 31.1 | 41.2 | -2.4 | -5.4 |
| dead walker 1.68 (dragging right) | ual_walk | 9 | 31.1 | 41.2 | -2.4 | -5.4 |
| dead brute 1.25 | ual_walk | 8 | 32.9 | 46.2 | -2.2 | -5.4 |
| dead runner 2.5 | ual_walk | 10 | 49.7 | 60.8 | -2.3 | -5.5 |
| left leg lost, 2.0 | ual_walk | 4 | 48.7 | 54.1 | -2.3 | -5.5 |
| left leg lost, standing | ual_idle | 1 | 2.9 | 2.9 | -5.5 | -5.6 |
| left arm lost, walking 1.4 (the stump strays up to 18 deg from held) | ual_walk | 7 | 36.7 | 53.8 | -2.4 | -5.5 |
| dead idle, its target to its left (its face 93 deg off it) | ual_idle | 2 | 1.0 | 1.0 | -5.5 | -5.7 |
| dead crawler 1.45 (hands) | zombie_crawl | 9 | 102.5 | 133.8 | -21.6 | -39.9 |
| up 8 deg at 2.5 | ual_jog_fwd | 7 | 39.5 | 47.5 | -3.6 | -9.0 |
| down 8 deg at 2.5 | ual_jog_fwd | 7 | 41.2 | 48.5 | -2.8 | -6.7 |
