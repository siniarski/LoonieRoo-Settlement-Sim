Animation bench (windowed), 180 frames per run, Godot 4.6.1-stable (official), Intel(R) Core(TM) Ultra 7 265K, NVIDIA GeForce RTX 5070

| mesh | anim | count | frame ms | p95 | render cpu | render gpu | draw calls |
|---|---|--:|--:|--:|--:|--:|--:|
| regions | tree | 100 | 5.66 | 6.42 | 1.60 | 0.28 | 1500 |
| regions | tree | 500 | 35.78 | 39.18 | 13.03 | 1.25 | 7500 |
| regions | player | 100 | 5.47 | 6.65 | 1.91 | 0.28 | 1500 |
| regions | player | 500 | 33.51 | 35.91 | 13.95 | 1.26 | 7500 |
| regions | throttled | 100 | 2.74 | 3.36 | 1.22 | 0.17 | 1500 |
| regions | throttled | 500 | 20.90 | 22.99 | 11.25 | 0.59 | 7500 |
| merged | tree | 100 | 2.00 | 2.24 | 0.19 | 0.11 | 200 |
| merged | tree | 500 | 12.84 | 13.59 | 1.54 | 0.25 | 1000 |
| merged | player | 100 | 1.54 | 1.86 | 0.19 | 0.11 | 200 |
| merged | player | 500 | 9.58 | 10.46 | 1.49 | 0.25 | 1000 |
| merged | throttled | 100 | 0.73 | 1.02 | 0.15 | 0.09 | 200 |
| merged | throttled | 500 | 4.02 | 4.75 | 0.91 | 0.17 | 1000 |
