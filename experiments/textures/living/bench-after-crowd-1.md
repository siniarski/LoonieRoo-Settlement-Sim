# The dead's crowd: before and after their outfits

Godot 4.6.1-stable (official), Intel(R) Core(TM) Ultra 7 265K, NVIDIA GeForce RTX 5070, 1280x720, light day, shadows on, vsync off. The look lab's field (rows 1.5 m apart from 4 m out), pz zombies walking (an AnimationPlayer each), auto LOD by height on screen; means over 240 frames after 60.

| dead | crowd | frame ms | gpu ms | render cpu ms | draws | objects | on screen tris | model files | materials | build ms |
|---|---|---|---|---|---|---|---|---|---|---|
| before | 40 | 4.16 | 0.28 | 0.27 | 420 | 420 | 362,115 | 5 | 6 | 482 |
| after | 40 | 4.45 | 0.30 | 0.40 | 585 | 585 | 388,440 | 8 | 99 | 650 |
| before | 40 | 3.97 | 0.29 | 0.28 | 420 | 420 | 362,115 | 5 | 6 | 364 |
| after | 40 | 4.32 | 0.30 | 0.40 | 585 | 585 | 388,440 | 8 | 99 | 488 |
| before | 200 | 10.73 | 0.42 | 1.14 | 1858 | 1858 | 1,128,642 | 5 | 6 | 1915 |
| after | 200 | 12.03 | 0.47 | 1.72 | 2569 | 2569 | 1,226,078 | 8 | 397 | 2315 |
| before | 200 | 10.82 | 0.41 | 1.19 | 1858 | 1858 | 1,128,645 | 5 | 6 | 1864 |
| after | 200 | 12.49 | 0.47 | 1.85 | 2569 | 2569 | 1,226,063 | 8 | 397 | 2520 |

Each crowd size runs before, after, before, after (the second pair shows the noise). Materials: distinct ones on the crowd's meshes (body, garments, hair, beard, hat); after, the dead's recolour materials are shared per model file, surface and colour pair.
