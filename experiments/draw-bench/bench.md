Draw bench: a 2048 world (seed 42, generated in 1152.6 s, 332,843 trees), first person at (1024, 1024), noon; 360 frames a run (a full turn), 1280x720, Godot 4.6.1-stable (official), Intel(R) Core(TM) Ultra 7 265K, NVIDIA GeForce RTX 5070

| range m | far trees | regions | far tris | far trees drawn | build ms | frame ms | p95 | render cpu | render gpu | draw calls | primitives |
|--:|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| 0 | yes | 0 | 0 | 0 | 0 | 10.13 | 10.41 | 0.29 | 0.31 | 365 | 536,164 |
| 250 | yes | 60 | 1,105,512 | 17,785 | 528 | 10.30 | 10.69 | 0.43 | 0.39 | 583 | 1,014,000 |
| 500 | yes | 222 | 4,600,044 | 73,715 | 2161 | 11.05 | 11.56 | 0.83 | 0.62 | 1055 | 1,997,417 |
| 1000 | yes | 830 | 16,737,220 | 267,945 | 7374 | 12.79 | 13.42 | 1.94 | 1.41 | 2708 | 5,439,233 |
| 1000 | no | 830 | 452,890 | 0 | 5425 | 10.84 | 11.19 | 0.59 | 0.35 | 601 | 664,384 |
