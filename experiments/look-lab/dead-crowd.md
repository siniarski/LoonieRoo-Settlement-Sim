# The dead's crowd: before and after their outfits

Godot 4.6.1-stable (official), Intel(R) Core(TM) Ultra 7 265K, NVIDIA GeForce RTX 5070, 1280x720, light day, shadows on, vsync off. The look lab's field (rows 1.5 m apart from 4 m out), pz zombies walking (an AnimationPlayer each), auto LOD by height on screen; means over 240 frames after 60.

| dead | crowd | frame ms | gpu ms | render cpu ms | draws | objects | on screen tris | model files | materials | build ms |
|---|---|---|---|---|---|---|---|---|---|---|
| before | 40 | 4.10 | 0.28 | 0.26 | 420 | 420 | 362,115 | 5 | 6 | 404 |
| after | 40 | 4.25 | 0.30 | 0.36 | 585 | 585 | 388,440 | 8 | 99 | 652 |
| before | 40 | 3.82 | 0.28 | 0.25 | 420 | 420 | 362,115 | 5 | 6 | 353 |
| after | 40 | 4.10 | 0.29 | 0.37 | 585 | 585 | 388,440 | 8 | 99 | 457 |
| before | 200 | 11.05 | 0.41 | 1.17 | 1858 | 1858 | 1,128,645 | 5 | 6 | 1756 |
| after | 200 | 12.88 | 0.47 | 1.86 | 2569 | 2569 | 1,226,058 | 8 | 397 | 2524 |
| before | 200 | 10.93 | 0.41 | 1.23 | 1858 | 1858 | 1,128,650 | 5 | 6 | 1920 |
| after | 200 | 12.69 | 0.47 | 1.86 | 2569 | 2569 | 1,226,062 | 8 | 397 | 2310 |

Each crowd size runs before, after, before, after (the second pair shows the noise). Materials: distinct ones on the crowd's meshes (body, garments, hair, beard, hat); after, the dead's recolour materials are shared per model file, surface and colour pair.
