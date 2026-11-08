Animation bench (windowed), 120 frames per run, Godot 4.6.1-stable (official), Intel(R) Core(TM) Ultra 7 265K, NVIDIA GeForce RTX 5070

| mesh | anim | count | frame ms | p95 | render cpu | render gpu | draw calls |
|---|---|--:|--:|--:|--:|--:|--:|
| merged | tree | 500 | 10.22 | 11.34 | 0.57 | 0.17 | 500 |
| merged | tree | 1000 | 21.17 | 22.66 | 1.32 | 0.24 | 1000 |
| merged | tree | 2000 | 43.75 | 46.26 | 3.07 | 0.50 | 2000 |
| merged | player | 500 | 6.98 | 7.68 | 0.58 | 0.17 | 500 |
| merged | player | 1000 | 15.06 | 15.77 | 1.33 | 0.24 | 1000 |
| merged | player | 2000 | 31.11 | 33.40 | 3.13 | 0.47 | 2000 |
| merged | throttled | 500 | 3.02 | 3.49 | 0.42 | 0.13 | 500 |
| merged | throttled | 1000 | 6.69 | 7.50 | 1.03 | 0.15 | 1000 |
| merged | throttled | 2000 | 14.70 | 15.92 | 2.70 | 0.25 | 2000 |
| merged | none | 500 | 1.18 | 1.41 | 0.29 | 0.11 | 500 |
| merged | none | 1000 | 3.02 | 3.71 | 0.74 | 0.12 | 1000 |
| merged | none | 2000 | 7.52 | 8.73 | 1.99 | 0.19 | 2000 |
