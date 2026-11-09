# Style lineup: a first-person scene per tier

Godot 4.6.1-stable (official), Intel(R) Core(TM) Ultra 7 265K, NVIDIA GeForce RTX 5070, 1280x720, day, shadows on, vsync off. People walking round you (3-40 m; a quarter survivors), 40 animals (wolves and rabbits, walking), 900 trees (8-60 m, the game's LOD ranges capped at the tier's top), 300 items; a full turn in 5 s. Each LOD by height on screen, capped at the tier's top. Means over 300 frames after 60.

| tier | people | frame ms | fps | gpu ms | render cpu ms | primitives | draw calls | texture MB (character looks) |
|---|---|---|---|---|---|---|---|---|
| A Blocky | 40 | 1.85 | 540 | 0.27 | 0.16 | 389,375 | 208 | 0.2 |
| B Zomboid | 40 | 1.90 | 525 | 0.34 | 0.17 | 940,606 | 239 | 6.0 |
| C Mid | 40 | 2.02 | 495 | 0.42 | 0.20 | 1,284,397 | 321 | 68.7 |
| D Near | 40 | 1.98 | 504 | 0.42 | 0.19 | 1,276,487 | 294 | 68.7 |
| E Now | 40 | 1.95 | 512 | 0.40 | 0.18 | 1,266,779 | 288 | 68.7 |
| A Blocky | 200 | 6.91 | 145 | 0.29 | 0.46 | 483,066 | 477 | 0.2 |
| B Zomboid | 200 | 6.97 | 143 | 0.36 | 0.44 | 1,031,216 | 465 | 6.0 |
| C Mid | 200 | 8.36 | 120 | 0.44 | 0.58 | 1,431,036 | 575 | 68.7 |
| D Near | 200 | 8.32 | 120 | 0.44 | 0.56 | 1,424,488 | 570 | 68.7 |
| E Now | 200 | 8.15 | 123 | 0.44 | 0.51 | 1,432,454 | 541 | 68.7 |
