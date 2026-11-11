Draw bench: a 2048 world (seed 42, only the start generated (--lazy) in 5.9 s, 2,791 trees), first person at (1124, 1024), noon; 360 frames a run (a full turn), 1280x720, Godot 4.6.1-stable (official), Intel(R) Core(TM) Ultra 7 265K, NVIDIA GeForce RTX 5070

| range m | far trees | regions | far tris | far trees drawn | build ms | frame ms | p95 | render cpu | render gpu | draw calls | primitives |
|--:|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| 500 | tiered | 224 | 2,050,678 | 60,868 | 2590 | 1.08 | 2.25 | 0.29 | 0.40 | 564 | 1,484,943 |
| 1000 | tiered | 812 | 3,703,906 | 203,768 | 10727 | 1.55 | 2.85 | 0.44 | 0.57 | 872 | 2,144,237 |
