Animation bench (windowed), 120 frames per run, Godot 4.6.1-stable (official), Intel(R) Core(TM) Ultra 7 265K, NVIDIA GeForce RTX 5070

| mesh | anim | count | frame ms | p95 | render cpu | render gpu | draw calls |
|---|---|--:|--:|--:|--:|--:|--:|
| merged | player | 1000 | 14.95 | 16.21 | 1.21 | 0.24 | 1000 |
| merged | player | 2000 | 30.96 | 33.16 | 2.91 | 0.49 | 2000 |
| merged | every2 | 1000 | 9.64 | 10.44 | 1.13 | 0.18 | 1000 |
| merged | every2 | 2000 | 20.88 | 22.24 | 2.93 | 0.32 | 2000 |
| merged | every3 | 1000 | 7.68 | 8.40 | 1.09 | 0.16 | 1000 |
| merged | every3 | 2000 | 16.87 | 18.06 | 2.84 | 0.28 | 2000 |
| merged | every4 | 1000 | 6.71 | 7.75 | 1.05 | 0.15 | 1000 |
| merged | every4 | 2000 | 14.83 | 16.07 | 2.81 | 0.25 | 2000 |
| merged | none | 1000 | 3.08 | 3.84 | 0.77 | 0.12 | 1000 |
| merged | none | 2000 | 7.33 | 8.35 | 1.93 | 0.20 | 2000 |
