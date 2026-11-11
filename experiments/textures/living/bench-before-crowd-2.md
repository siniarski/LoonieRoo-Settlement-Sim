# The dead's crowd: before and after their outfits

Godot 4.6.1-stable (official), Intel(R) Core(TM) Ultra 7 265K, NVIDIA GeForce RTX 5070, 1280x720, light day, shadows on, vsync off. The look lab's field (rows 1.5 m apart from 4 m out), pz zombies walking (an AnimationPlayer each), auto LOD by height on screen; means over 240 frames after 60.

| dead | crowd | frame ms | gpu ms | render cpu ms | draws | objects | on screen tris | model files | materials | build ms |
|---|---|---|---|---|---|---|---|---|---|---|
| before | 40 | 4.37 | 0.28 | 0.29 | 420 | 420 | 362,115 | 5 | 6 | 506 |
| after | 40 | 4.48 | 0.30 | 0.39 | 585 | 585 | 388,440 | 8 | 99 | 674 |
| before | 40 | 3.95 | 0.28 | 0.27 | 420 | 420 | 362,115 | 5 | 6 | 390 |
| after | 40 | 4.84 | 0.31 | 0.43 | 585 | 585 | 388,440 | 8 | 99 | 478 |
| before | 200 | 10.75 | 0.41 | 1.17 | 1858 | 1858 | 1,128,637 | 5 | 6 | 1930 |
| after | 200 | 12.36 | 0.46 | 1.80 | 2569 | 2569 | 1,226,064 | 8 | 397 | 2586 |
| before | 200 | 10.94 | 0.41 | 1.22 | 1858 | 1858 | 1,128,642 | 5 | 6 | 1992 |
| after | 200 | 12.64 | 0.47 | 1.84 | 2569 | 2569 | 1,226,053 | 8 | 397 | 2284 |

Each crowd size runs before, after, before, after (the second pair shows the noise). Materials: distinct ones on the crowd's meshes (body, garments, hair, beard, hat); after, the dead's recolour materials are shared per model file, surface and colour pair.
