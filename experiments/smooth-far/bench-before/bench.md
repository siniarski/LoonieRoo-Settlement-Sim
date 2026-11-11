Draw bench: a 2048 world (seed 42, only the start generated (--lazy) in 5.7 s, 2,791 trees), first person at (1124, 1024), noon; 360 frames a run (a full turn), 1280x720, Godot 4.6.1-stable (official), Intel(R) Core(TM) Ultra 7 265K, NVIDIA GeForce RTX 5070

| range m | far trees | regions | far tris | far trees drawn | build ms | frame ms | p95 | render cpu | render gpu | draw calls | primitives |
|--:|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| 500 | tiered | 224 | 2,008,670 | 60,868 | 2106 | 1.34 | 1.80 | 0.33 | 0.37 | 564 | 1,469,055 |
| 1000 | tiered | 812 | 3,467,986 | 203,768 | 7578 | 1.57 | 2.41 | 0.45 | 0.54 | 872 | 2,081,491 |
