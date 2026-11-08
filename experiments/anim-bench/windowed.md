Animation bench (windowed), 120 frames per run, Godot 4.6.1-stable (official), Intel(R) Core(TM) Ultra 7 265K, NVIDIA GeForce RTX 5070

| mesh | anim | count | frame ms | p95 | render cpu | render gpu | draw calls |
|---|---|--:|--:|--:|--:|--:|--:|
| regions | tree | 100 | 5.43 | 6.17 | 1.55 | 0.27 | 1400 |
| regions | tree | 500 | 33.61 | 36.24 | 11.81 | 1.17 | 7000 |
| regions | tree | 1000 | 74.93 | 82.87 | 30.48 | 2.32 | 14000 |
| regions | tree | 2000 | 159.45 | 170.47 | 65.82 | 4.59 | 28000 |
| regions | player | 100 | 5.41 | 6.12 | 1.78 | 0.28 | 1400 |
| regions | player | 500 | 32.46 | 35.11 | 13.31 | 1.17 | 7000 |
| regions | player | 1000 | 71.15 | 75.97 | 32.54 | 2.31 | 14000 |
| regions | player | 2000 | 150.93 | 162.99 | 69.16 | 4.63 | 28000 |
| regions | throttled | 100 | 3.28 | 3.72 | 1.25 | 0.16 | 1400 |
| regions | throttled | 500 | 20.50 | 22.27 | 10.98 | 0.55 | 7000 |
| regions | throttled | 1000 | 46.48 | 49.03 | 27.28 | 1.16 | 14000 |
| regions | throttled | 2000 | 98.42 | 105.23 | 59.12 | 2.30 | 28000 |
| regions | none | 100 | 2.39 | 3.46 | 1.10 | 0.14 | 1399 |
| regions | none | 500 | 14.90 | 17.05 | 9.93 | 0.36 | 7000 |
| regions | none | 1000 | 35.92 | 39.45 | 24.86 | 0.75 | 14000 |
| regions | none | 2000 | 77.42 | 83.88 | 54.69 | 1.42 | 28000 |
| merged | tree | 100 | 2.71 | 2.96 | 0.13 | 0.10 | 100 |
| merged | tree | 500 | 10.97 | 12.17 | 0.81 | 0.17 | 500 |
| merged | tree | 1000 | 22.25 | 23.24 | 1.70 | 0.25 | 1000 |
| merged | tree | 2000 | 45.55 | 47.42 | 3.82 | 0.47 | 2000 |
| merged | player | 100 | 1.27 | 1.37 | 0.12 | 0.09 | 100 |
| merged | player | 500 | 7.48 | 8.17 | 0.74 | 0.18 | 500 |
| merged | player | 1000 | 15.80 | 17.07 | 1.67 | 0.24 | 1000 |
| merged | player | 2000 | 32.75 | 35.27 | 3.76 | 0.48 | 2000 |
| merged | throttled | 100 | 0.78 | 0.95 | 0.11 | 0.08 | 100 |
| merged | throttled | 500 | 3.34 | 3.73 | 0.53 | 0.14 | 500 |
| merged | throttled | 1000 | 7.17 | 8.10 | 1.24 | 0.15 | 1000 |
| merged | throttled | 2000 | 15.67 | 16.57 | 3.17 | 0.25 | 2000 |
| merged | none | 100 | 0.49 | 0.75 | 0.14 | 0.08 | 100 |
| merged | none | 500 | 1.33 | 1.56 | 0.35 | 0.11 | 500 |
| merged | none | 1000 | 3.30 | 3.90 | 0.87 | 0.12 | 1000 |
| merged | none | 2000 | 8.05 | 9.09 | 2.36 | 0.20 | 2000 |
