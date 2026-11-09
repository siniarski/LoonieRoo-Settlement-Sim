Draw bench: a 2048 world (seed 42, generated in 8.7 s, 333,787 trees), first person at (1060, 1024), noon; 360 frames a run (a full turn), 1280x720, Godot 4.6.1-stable (official), Intel(R) Core(TM) Ultra 7 265K, NVIDIA GeForce RTX 5070

| range m | far trees | regions | far tris | far trees drawn | build ms | frame ms | p95 | render cpu | render gpu | draw calls | primitives |
|--:|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| 0 | yes | 0 | 0 | 0 | 0 | 0.80 | 0.96 | 0.17 | 0.25 | 345 | 286,330 |
| 250 | yes | 62 | 1,104,330 | 17,670 | 99 | 1.02 | 1.27 | 0.33 | 0.32 | 557 | 706,096 |
| 500 | yes | 224 | 4,648,560 | 74,585 | 339 | 1.34 | 1.60 | 0.55 | 0.57 | 1021 | 1,747,466 |
| 1000 | yes | 825 | 16,682,518 | 267,145 | 1283 | 2.81 | 3.48 | 1.39 | 1.35 | 2652 | 5,067,702 |
| 1000 | no | 825 | 449,750 | 0 | 789 | 1.26 | 1.41 | 0.34 | 0.27 | 582 | 415,489 |
