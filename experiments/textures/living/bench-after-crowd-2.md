# The dead's crowd: before and after their outfits

Godot 4.6.1-stable (official), Intel(R) Core(TM) Ultra 7 265K, NVIDIA GeForce RTX 5070, 1280x720, light day, shadows on, vsync off. The look lab's field (rows 1.5 m apart from 4 m out), pz zombies walking (an AnimationPlayer each), auto LOD by height on screen; means over 240 frames after 60.

| dead | crowd | frame ms | gpu ms | render cpu ms | draws | objects | on screen tris | model files | materials | build ms |
|---|---|---|---|---|---|---|---|---|---|---|
| before | 40 | 4.18 | 0.29 | 0.26 | 420 | 420 | 362,115 | 5 | 6 | 487 |
| after | 40 | 4.88 | 0.31 | 0.42 | 585 | 585 | 388,440 | 8 | 99 | 697 |
| before | 40 | 3.92 | 0.29 | 0.28 | 420 | 420 | 362,115 | 5 | 6 | 379 |
| after | 40 | 4.10 | 0.30 | 0.37 | 585 | 585 | 388,440 | 8 | 99 | 490 |
| before | 200 | 11.15 | 0.43 | 1.18 | 1858 | 1858 | 1,128,642 | 5 | 6 | 1843 |
| after | 200 | 13.11 | 0.50 | 1.87 | 2569 | 2569 | 1,226,070 | 8 | 397 | 2288 |
| before | 200 | 11.17 | 0.45 | 1.19 | 1858 | 1858 | 1,128,648 | 5 | 6 | 1942 |
| after | 200 | 12.05 | 0.47 | 1.76 | 2569 | 2569 | 1,226,076 | 8 | 397 | 2208 |

Each crowd size runs before, after, before, after (the second pair shows the noise). Materials: distinct ones on the crowd's meshes (body, garments, hair, beard, hat); after, the dead's recolour materials are shared per model file, surface and colour pair.
