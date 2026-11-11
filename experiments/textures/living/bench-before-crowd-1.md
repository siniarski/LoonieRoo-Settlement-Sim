# The dead's crowd: before and after their outfits

Godot 4.6.1-stable (official), Intel(R) Core(TM) Ultra 7 265K, NVIDIA GeForce RTX 5070, 1280x720, light day, shadows on, vsync off. The look lab's field (rows 1.5 m apart from 4 m out), pz zombies walking (an AnimationPlayer each), auto LOD by height on screen; means over 240 frames after 60.

| dead | crowd | frame ms | gpu ms | render cpu ms | draws | objects | on screen tris | model files | materials | build ms |
|---|---|---|---|---|---|---|---|---|---|---|
| before | 40 | 4.10 | 0.28 | 0.26 | 420 | 420 | 362,115 | 5 | 6 | 414 |
| after | 40 | 4.57 | 0.30 | 0.39 | 585 | 585 | 388,440 | 8 | 99 | 674 |
| before | 40 | 4.30 | 0.29 | 0.30 | 420 | 420 | 362,115 | 5 | 6 | 461 |
| after | 40 | 4.38 | 0.30 | 0.40 | 585 | 585 | 388,440 | 8 | 99 | 487 |
| before | 200 | 11.57 | 0.44 | 1.22 | 1858 | 1858 | 1,128,663 | 5 | 6 | 1888 |
| after | 200 | 12.95 | 0.49 | 1.84 | 2569 | 2569 | 1,226,060 | 8 | 397 | 2320 |
| before | 200 | 11.02 | 0.41 | 1.22 | 1858 | 1858 | 1,128,642 | 5 | 6 | 1889 |
| after | 200 | 12.37 | 0.48 | 1.78 | 2569 | 2569 | 1,226,077 | 8 | 397 | 2282 |

Each crowd size runs before, after, before, after (the second pair shows the noise). Materials: distinct ones on the crowd's meshes (body, garments, hair, beard, hat); after, the dead's recolour materials are shared per model file, surface and colour pair.
