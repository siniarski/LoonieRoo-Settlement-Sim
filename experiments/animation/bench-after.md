Animation bench (windowed), 180 frames per run, Godot 4.6.1-stable (official), Intel(R) Core(TM) Ultra 7 265K, NVIDIA GeForce RTX 5070

| mesh | anim | count | frame ms | p95 | render cpu | render gpu | draw calls |
|---|---|--:|--:|--:|--:|--:|--:|
| regions | tree | 100 | 8.05 | 8.73 | 2.60 | 0.28 | 1500 |
| regions | tree | 500 | 47.67 | 51.07 | 19.54 | 2.67 | 7500 |
| regions | player | 100 | 7.37 | 8.04 | 2.75 | 0.28 | 1500 |
| regions | player | 500 | 40.96 | 43.12 | 18.27 | 2.98 | 7500 |
| regions | throttled | 100 | 3.21 | 3.93 | 1.46 | 0.16 | 1500 |
| regions | throttled | 500 | 22.91 | 25.87 | 12.67 | 0.72 | 7500 |
| merged | tree | 100 | 1.99 | 2.16 | 0.19 | 0.14 | 200 |
| merged | tree | 500 | 12.73 | 13.28 | 1.51 | 0.25 | 1000 |
| merged | player | 100 | 1.40 | 1.53 | 0.18 | 0.11 | 200 |
| merged | player | 500 | 9.44 | 10.35 | 1.47 | 0.25 | 1000 |
| merged | throttled | 100 | 0.68 | 0.79 | 0.14 | 0.09 | 200 |
| merged | throttled | 500 | 3.90 | 4.39 | 0.89 | 0.16 | 1000 |
