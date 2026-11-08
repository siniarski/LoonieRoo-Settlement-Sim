Animation bench (headless: CPU only), 120 frames per run, Godot 4.6.1-stable (official), Intel(R) Core(TM) Ultra 7 265K, 

| mesh | anim | count | frame ms | p95 | render cpu | render gpu | draw calls |
|---|---|--:|--:|--:|--:|--:|--:|
| regions | tree | 500 | 7.97 | 9.02 | - | - | - |
| regions | tree | 2000 | 35.05 | 37.09 | - | - | - |
| regions | player | 500 | 6.90 | 7.04 | - | - | - |
| regions | player | 2000 | 22.07 | 23.48 | - | - | - |
| regions | throttled | 500 | 6.90 | 7.13 | - | - | - |
| regions | throttled | 2000 | 9.19 | 10.18 | - | - | - |
| regions | none | 500 | 6.90 | 7.09 | - | - | - |
| regions | none | 2000 | 6.90 | 6.95 | - | - | - |
| merged | tree | 500 | 7.88 | 8.82 | - | - | - |
| merged | tree | 2000 | 34.78 | 36.80 | - | - | - |
| merged | player | 500 | 6.90 | 6.94 | - | - | - |
| merged | player | 2000 | 21.79 | 22.79 | - | - | - |
| merged | throttled | 500 | 6.90 | 7.13 | - | - | - |
| merged | throttled | 2000 | 9.06 | 9.92 | - | - | - |
| merged | none | 500 | 6.90 | 7.19 | - | - | - |
| merged | none | 2000 | 6.90 | 6.96 | - | - | - |
