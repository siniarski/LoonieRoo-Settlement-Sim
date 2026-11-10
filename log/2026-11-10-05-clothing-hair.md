- **2026-11-10, clothing and hair: stumps, hair in the world, the dead dressed** (docs/characters.md "Clothing,
  hair, equipment" and "Missing limbs"). Asked for, in three commits: garments that end at a stump, hair, and clothes
  and hair on the dead (who all wore the same painted tee and jeans with grey matted hair).
  - **Stumps (StumpCut.cs), cut at load in Godot, not variants from Blender.** The game draws the look models: a whole
    skinned mesh per LOD, clothes painted into the body's map or as garment shells. A Blender variant per side and
    level would multiply every subject, face, outfit and LOD file by the 15 combinations of four lost limbs; a cut by
    plane and bone weights works on any skinned mesh (body, garment, a future modelled garment) with no re-export,
    once per mesh and cut set (cached). The plane is 7 cm below the elbow or 10 cm below the knee (Wounds.lostLimb's
    levels, either side); triangles weighted to the limb are clipped (every attribute and blend shape interpolated),
    the stump is reweighted onto the upper arm or thigh, and each cut loop (found by position, so UV seams don't break
    it) is capped from its own vertices and UVs: so the cap is the colour of what was cut. Living arm: a rounded end,
    the empty sleeve folded up the back of the arm as a thin shell, a safety pin through it (a metal surface). Living
    leg: a rounded end, a knot, a short tail. The dead: ragged tatters and raw flesh inside (a dark red surface). The
    winding is read off each mesh (not assumed). People.cs: survivors' lostLimbs and crawlers' legs are part of the
    look. Look lab: `--lose=`, a Missing picker, the "stumps" lineup, `--aimat=H`.
  - **Hair was already built** (2026-11-08: seven styles, under-hat versions, a beanie, a beard) but only the lab
    used hats and beards. Looks.cs now holds who wears what, by a stateless hash of the id: style and colour (the
    lab's five-colour palette), a beard for a quarter of the living (not with long hair or a ponytail) and some of
    the dead, a beanie for 22% of the living and 10% of the dead (the hair swapped to its under-hat file), the dead's
    hair dulled with grime. Colour is a material per source and tint, shared (no instance uniforms). People.cs pools
    models by body and lost limbs and re-dresses a pooled one handed to someone who wears something else (8 a frame),
    so more variety doesn't grow the pools. The pz candidate in the look lab now wears the hair ladder's 3rd-5th
    levels, as the game does. Cost: 250-850 triangles near (long hair under a beanie the most), 100-340 mid, 60-200
    far, one to three more meshes a character. Lineups "hats" (every style under the beanie: no poke-through front or
    back) and "people".
  - **The dead dressed.** Three outfits built (build_lookdev.py --only pz, 18 s each): `--outfit longsleeve` (a
    painted work shirt and trousers, paint.OUTFIT_VARIANTS), and the zombie in the jacket and in the hoodie (the
    garment meshes, clothes.albedo's `dead`: grime, blood down the front and on the cuffs, tears). Every zombie pz
    build also writes a recolour mask (`<file>_mask.png`: red the top's clean cloth, green the trousers'; blood,
    dirt and tears left out) and `<file>_mask.json` (the painted colours and the cloth's mean luminance). The game
    recolours (recolour.gdshader): under each mask, the palette colour times the texel's brightness over the cloth's
    mean, so the painted shading and wear stay. A first try scaled by colour ratios: far too bright and saturated
    (the painted cloth is lighter than its nominal colour, and daylight plus the filmic tonemap push it further);
    the palette was muted too. Looks.DeadBody/DeadOutfit: the tee half the time (with any of the five faces), else
    the long sleeves, the jacket or the hoodie; ten tops and seven trousers; hair from matted, long, receding, crop,
    buzz or the painted scalp. All by hash: no RNG draw, nothing saved, no schema change.
  - **Measured** (look lab `--crowdbench`, experiments/look-lab/dead-crowd.md; RTX 5070, 1280x720, shadows on;
    "before" is the old look, one tee file per face with grey matted hair): 40 walking (the world's model budget)
    4.10 / 3.82 ms a frame before, 4.25 / 4.10 after (render CPU 0.26 -> 0.36 ms, GPU 0.28 -> 0.30, draw calls 420 ->
    585); 200 walking 11.0 -> 12.8 ms (render CPU 1.2 -> 1.86, draw calls 1,858 -> 2,569, materials 6 -> 397). The
    extra cost is the beards', hats' and garments' meshes (draw calls), not the shader. A stump cut costs 6-13 ms
    the first time a model file meets a cut set (41 ms the very first: JIT), nothing after (cached): a one-off
    hitch when the first one-armed survivor or crawler of a look comes near.
  - **Decided.** Stumps are cut at load (above). The dead's colours are plain materials per model file, surface and
    colour pair, never instance uniforms. Pools are by body, the dress put on after.
  - **Left.** No sim side: zombies don't lose limbs in fights yet (only crawlers' legs), and nothing one-armed
    animates (People.cs plays clips as they are; the lab's stump hold isn't in the world). The mannequin lab
    (Lab.tscn, Wardrobe.cs) still hides garment regions at a cut with no pinned sleeve. The dead's skin tatters are
    small; the raw end shows mostly from below. The pz survivor jacket and hoodie files have blue texels bleeding onto
    the face and jacket (it was there before; a bake or UV padding fault in those builds). Under a beanie the decimated
    hair's edge at the nape is patchy. The long-sleeved outfit is painted, so it reads as tight. Shoes are not
    recoloured. Real modelled garments over the scripted shells need an artist: sculpted folds and hems per garment,
    retopology to the budgets, UVs that share an atlas, weight painting at shoulders and hips, and a recolour mask
    painted with them (the pipeline takes them as any other garment mesh). Check green with the self-test after each
    commit (727 tests, 78 self-test checks).
