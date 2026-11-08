# Characters and animation: the plan (Phase 1)

Written 2026-11-08, from the player's brief (a ChatGPT-drafted prompt: "AI-Assisted Character, Animation & Interaction Pipeline") and these decisions:

- **Camera.** 3D. **Solo play is first-person** (decided 2026-11-08, replacing third-person / isometric); colony management is bird's-eye.
- **Art target: "Zomboid-plus"** (2026-11-08, see "The look" below): Zomboid's grounded proportions, muted worn clothes and readable silhouettes, with the detail first person needs up close: a baked normal map, a real face, ~10-15k triangles near. (It was "stylised low-poly, ~3-8k" while the camera was isometric.) Damage and dirt as shader masks, hair as meshes.
- **Tooling.** Blender (the player installs it) driven two ways: the Blender MCP server for interactive work and inspection, and headless Python scripts (`blender --background --python`) for anything repeatable (building, validating, exporting). Assets interchange as glTF (`.glb`); the `.blend` files are kept.
- **LOD** is a first-class requirement (the scale target is Zomboid-sized: up to ~100k zombies in the world, hundreds visible at the colony zoom).

This document is the architecture. The order of work is at the end.

---

## 1. The rule everything follows: the sim owns the truth, animation is presentation

The project's architecture rules (CLAUDE.md) decide most of the hard questions in the brief:

| Brief asks for | What the rules say | So |
|---|---|---|
| Root motion vs code-driven movement | The sim owns every mover's position (`at`, SUB units); the renderer only reads | **Code-driven, always.** Clips are authored in place. Locomotion blend spaces take the sim's speed and direction as parameters, so the feet match the ground speed. Root motion is used only *inside Godot* to author/check clips, never to move anyone. |
| Ragdoll, pushes, throws, knockdowns | The sim is deterministic; Godot physics is not | **Ragdoll is cosmetic.** The sim decides "knocked down at tick T, lies at (x, y), gets up at tick T2". The renderer plays ragdoll in between and blends into a get-up clip that ends where the sim says the body is. A ragdoll never moves a sim position, never decides a hit. Co-op lockstep stays possible. |
| Interaction duration, completion, interruption | Timed actions run on `slowTick`, durations come from Time.cs | **The sim runs the action**, with a start tick, end tick and progress. The renderer plays the clip at *normalised time = sim progress*, so a slow "careful" open and a 10x day length both stay in sync. Interruptions are sim events; the renderer just cuts to the matching reaction. |
| Quiet / normal / fast / forceful approaches | Noise, stamina, success rolls are sim (Noise.cs, BodyMod) | **Approaches are sim data**; the animation variant is a name the renderer looks up. |
| Multi-character (wall boost, grabs, carrying) | Sim state per mover | **A paired action in the sim** with participants and roles. The renderer plays one clip per role, aligned on a shared anchor. |
| IK (feet, hands on objects) | Renderer-only | **Runtime IK is pure polish**: it never changes what the sim thinks happened. |

The only thing the sim needs to expose, per visible mover, is a small **pose intent** (most of it exists already):

```
stance        stand | sneak | crouch | sit | lie | down (knocked) | dead
locomotion    speed, heading (from at/was), facing (aim)
action        id, approach, role, progress 0..1 (or none), target anchor
carry         what's in each hand / on the back (item kinds -> visual ids)
condition     injury (limp left/right, arm), exhaustion, infection stage, blood, dirt
```

The renderer maps that to an AnimationTree state. Nothing about animation is saved: a loaded game rebuilds every pose from the sim state.

## 2. Units and axes

- **1 tile = 1 metre.** Fits the sim already: `Motion.BODY_RADIUS` = 77/256 tile = 0.30 m, a good human radius. Doors on tile edges are then ~0.9 m wide, which is right. Characters are ~1.75 m tall.
- Sim (x, y) maps to Godot (x, z); Godot's +Y is up. A future storey (the planned z-level) is a multiple of a fixed storey height (2.8-3 m). Camera.cs keeps owning the projection, now in 3D.
- Blender: metres, Z up, character faces -Y (Blender's front view). The glTF exporter converts to Godot's Y-up / -Z forward; validate once with a test asset and never think about it again.

## 3. The character foundation

### Skeleton: Godot's humanoid profile, unchanged

Use the bone names and hierarchy of **Godot's `SkeletonProfileHumanoid`** directly (`Root`, `Hips`, `Spine`, `Chest`, `UpperChest`, `Neck`, `Head`, `LeftShoulder`, `LeftUpperArm`, `LeftLowerArm`, `LeftHand`, finger bones, `LeftUpperLeg`, `LeftLowerLeg`, `LeftFoot`, `LeftToes`, and the right side). Why:

- Godot's import **retargeting** (BoneMap + the humanoid profile) maps any humanoid clip library onto it. Our own rig *is* the profile, so our own clips need no mapping at all, and outside libraries (Mixamo, Quaternius, mocap) retarget onto it on import.
- Survivors and zombies **share the skeleton**. Zombie difference lives in clips (shamble, lurch, crawl) and in additive layers (a twisted neck, a dragging leg), not in bones.
- Ragdoll bodies, IK chains and attachment points are all defined once, against these names.

Rules: one armature named `Armature`, `Root` at the floor between the feet, `Hips` its child; T-pose rest (the profile's reference) or A-pose with the import rest fixed in Godot; no scale on bones; no extra deforming bones. Fingers: full chains on the base rig, but most clips only key a fist/open/grip pose (stylised).

**Attachment points are not bones.** Back (backpack), belt left/right/back (holsters, pouches), hand grips right/left, head (hats), face (masks) are named transforms *relative to a bone*, stored in one data file (`sockets.json`) and realised in Godot as `BoneAttachment3D` + offset. Extra socket bones would pollute retargeting and the ragdoll.

### The base body

- One stylised base mesh, ~2.5-3.5k tris, clean quad-based topology with edge loops at shoulders, elbows, wrists, hips, knees, ankles, and around the mouth/eyes only as much as the style needs.
- **Split into regions** (separate mesh pieces or material slots under one skeleton): head, neck, torso upper, torso lower, upper arms, forearms, hands, thighs, shins, feet. A garment declares which regions it **hides**; hidden regions aren't drawn. This is how Zomboid and The Sims avoid most body-through-clothes clipping, and it saves triangles.
- Body variation: two bases (masc/fem) sharing UVs and skeleton, plus a few **shape keys** (heavy, thin). Shape keys must be transferred to every garment, so start with *one* base and add shapes only when the clothing pipeline is proven.
- Skinning: max **4 influences per vertex**, normalised, no unweighted vertex (the validator enforces it).

### The look (2026-11-08, the look lab)

The player likes Project Zomboid's models and textures, but they're made to be seen from an isometric camera ~10 m up;
ours will be seen in first person, a zombie at arm's length filling half the screen. The direction is **"Zomboid-plus"**:
keep what works (believable adult proportions, not chibi; muted, worn, dirty clothes; a silhouette that reads at a
glance) and add what first person needs (a normal map for folds, hems and skin; a modelled face with eyes and teeth;
smooth silhouettes near).

To decide budgets on something real, not the segmented mannequin, there is a **look-development body**, made by script
(art/scripts/sculpt.py, paint.py, build_lookdev.py): one continuous adult form (metaballs, voxel-remeshed), the face
pushed in by brushes, a high-poly copy with cloth folds and hems, and from it two candidates as LOD ladders on the
shared rig: **pz** (2.5k / 1.2k / 600, a 512 painted texture with strong baked shading, no normal map: Zomboid's way)
and **plus** (30k / 12k / 6k / 3k / 1.2k / 600, a 2048 colour map and a baked normal map). Two subjects, a zombie
(torn T-shirt and jeans, grey bruised skin, blood from the mouth, clouded eyes) and a survivor (jacket, trousers,
boots). It is a blockout, not final art: the face is plain and the hands are mittens; a person sculpting over it (or a
CC0 base) is the next step for the real thing.

The look lab (godot/Characters/LookLab.tscn) shows them in first person (eye height 1.65 m, 60 degrees vertical field
of view) and measures them. What it showed (screenshots: `--shots`; numbers: experiments/look-lab/look.md):

- **What a face gets on screen.** At 60 degrees vertical FOV, 720p, a 1.75 m body is ~730 px tall at 1.5 m and the
  head ~140 px at 1 m (arm's length); ~15 px at 10 m. At 1440p double that. So even up close a face is a few hundred
  pixels: a 2048 atlas gives it plenty; detail goes into the silhouette and the normal map, not more texels.
- **Up close (1-3 m) the 2.5k "pz" reads as Zomboid**: faceted head, jaw and shoulders, painted shading; the snarl
  barely works on it (too few vertices round the mouth). **12k and 30k look alike** at 2.5 m (the lineup); 30k only
  shows at arm's length (the jaw line and lips). At 10 m pz, plus 6k and plus 30k are hard to tell apart; at 30 m
  even the 600-triangle LOD holds.
- **The GPU can afford it; the CPU is the limit** (the sweep, experiments/look-lab/look.md: RTX 5070, 1280x720,
  shadows on). GPU time follows triangles drawn (with the shadow passes, ~4x the visible count): 1,000 at 30k 16 ms,
  at 12k 7.3, at 6k 3.3, at 3k 2.0, at 1.2k 1.1; 100 at 30k 2.1 ms. But frame time with a crowd is ~35 ms per 1,000
  characters whatever the LOD: one AnimationPlayer and skeleton each (as the anim bench found). The mannequin (14
  meshes each) is 3x worse again (58k draw calls for 1,000). So: near characters can be generous (30 at 30k is
  ~0.6 ms GPU here; budget for a low-end GPU at ~10x), the mid tier must be throttled players, and crowds VAT.
- **Texture memory is the next limit**: the plus character's two 2048 maps take 32 MB as imported (uncompressed RGB8
  with mipmaps), pz's 512 map 1 MB. 30 zombie outfits at that would be ~1 GB: textures must be VRAM-compressed
  (BC1/BC7, 4-8x smaller) and share atlases by garment, and the mid and far tiers use 1024 / 512 mips.
- **Budgets, revised for first person:** near (under ~5 m, up to ~30) **10-15k** dressed (the ~12k step is where the
  look stops improving at 2-5 m; 30k only for a character in your face, which can be the same 12k with the normal map
  doing the work); a 2048 atlas for the body and a 1024 for the head, normal map; mid (5-25 m) **3-6k**; far **600-1.2k**
  (VAT). LOD by height on screen: plus switches at 700 / 340 / 170 / 85 / 40 px (LookLab.cs LodAt).

**Zombie faces**: two blend shapes on the near LODs only, `jaw_open` and `angry`. Neutral is the basis with the jaw
a little open and drifting; angry eases in over 0.25 s when the zombie has seen you, with a growl (the jaw working).
The sim already knows when one has noticed you (Sight); the renderer only reads it. Faces below the near LODs are
fixed: at 10 m a head is ~15 pixels.

### Clothing, hair, equipment

- Garments are modelled on the base body in Blender and **skinned by weight transfer** from the body (Data Transfer modifier, scripted), then cleaned up. This is the step AI scripting does well and repeatably.
- **Slots and layers**: feet (socks < shoes), legs (underwear < trousers), torso (shirt < jacket < coat), hands, head (hair < hat), face, neck (scarf), back (bag), waist (belt, fanny pack). Each garment declares slot, layer, hidden body regions and *hidden lower layers* (a coat hides most of a shirt, so the shirt's hidden parts are skipped). Practical compromise: **at most two visible torso layers**, outer layers modelled to fit over the thickest inner layer they allow.
- **Hair**: low-poly skinned mesh caps (stylised), colour by shader parameter. A hat swaps the hair to a "hat-compatible" short variant via a per-hair `under_hat` mesh, rather than solving clipping.
- **Conditions are shader parameters, not new meshes**: one character shader with per-instance values (`blood`, `dirt`, `wear`, `wetness`, `infection`, `decay`, skin tint) blended through shared mask textures; torn and holed clothes are an alpha-clip mask; wounds and bruises are a small decal mask per body region. Per-instance uniforms keep many differently-damaged characters on one material. These already exist as sim numbers (Clothing condition, Wounds, Infection, BodyMod wetness) to drive them.
- **Built (2026-11-08)**, first slice: art/scripts/garments.py makes nine garments (shirt, jacket, trousers, shorts, boots, shoes, gloves, cap, backpack) as shells over the mannequin's pieces, split by body region into 28 meshes (`<garment>__<region>`), weighted like the body under them; rules and colours in garments.json, meshes in garments.glb (the rig, no clips). godot/Characters/Wardrobe.cs puts them on a character's own skeleton and applies the rules: a full piece hides the body region (and lower layers' pieces) under it, `hides_lower` pieces hide only lower layers (a jacket's body over a shirt's), missing limbs hide garments in the cut regions. Conditions are one shader (character.gdshader) with per-mesh values packed in two vec4s: colour and seed, dirt / blood / torn / wet, patterned by a rest-pose UV map so they stick to the cloth. Limit found: Godot reserves a block of global buffer slots for every mesh using instance values, so near-tier characters with ~26 meshes each top out near 300 even with the buffer raised to 131072 (fine for a near budget of 40; mid and far tiers use one merged mesh). Still to do: pinned sleeves at a stump, hair, real modelled garments.
- **The mid tier dressed (2026-11-08)**: godot/Characters/OutfitMerge.cs merges what an outfit shows (Dresser.Plan, the same rules as near: bare regions, pieces on view, cut limbs) into one skinned mesh with one surface and one material (character_merged.gdshader, the condition patterns shared with near through character.gdshaderinc). Bone indices are remapped by bind name onto the body's skin; sources are decoded once (~30 ms), a merge then takes ~0.3 ms. Colours aren't baked: each vertex carries its garment's slot and pattern seed (CUSTOM0, floats), the character's seven slot colours (8-bit sRGB packed in floats), its seed and its dirt / blood / torn / wet are three instance vec4s; so meshes are cached by the pieces shown alone (1,000 randomly dressed people share 134). Near and mid side by side (the lab's mid twin) look the same: colours, patterns, tears. Measured (Crowd tab, mid at 20 fps): 1,000 mid 9.5 ms dressed against 11.4 bare (2.8 draw calls each against 5.5: the bare mesh's face is a second surface), 2,000 19.7 against 24.0. Traps: an instance uniform's name is substituted everywhere in the shader, function parameters included (name them differently); vertex COLOR is 8-bit and gamma-converted (use a float CUSTOM); a Color given as an instance value arrives sRGB-to-linear converted.
- **Draw calls**: a dressed character is ~10 meshes. At near range that's fine. For mid and far range, the outfit is **merged once into a single mesh** whenever it changes (same skeleton, atlas textures), see LOD below.

### Missing limbs (survivors after amputation, zombies falling apart)

The sim already has them: `Wounds.amputate` gives a survivor `lostArm` or `lostLeg` for good, and `crawler` zombies exist. The rules for the visual side:

- **The skeleton never changes.** Every character keeps all 56 bones, so every clip, the retargeting, the ragdoll and the attachment points keep working. A missing limb is a mesh and animation matter only. (Shrinking the lost bones to zero scale is the common hack: it breaks attachments, IK and the ragdoll, so it's a fallback for a far LOD at most.)
- **Mesh: cut points with stump caps.** The body regions get cuts at fixed amputation points: mid-forearm, above the elbow, below the knee, above the knee (and the hand, the foot). Each cut has a small **stump cap** mesh (raw, bandaged, healed; torn for zombies), weighted to the bone above the cut. Losing a limb hides the regions below the cut and shows the cap. This is the same region-hiding as clothing, so it costs nothing new.
- **Clothing follows the cut.** A sleeve or trouser leg over a lost limb is cut at the same point: garments carry the same region split (or a per-region mask in the shader), plus a pinned-sleeve / knotted-trouser variant for survivors. Gloves and shoes on a missing hand or foot are simply not drawn (and the sim shouldn't let you wear them).
- **Animation: layers and sets, not copies of everything.**
  - One arm: an upper-body layer holds the stump (held close, slight guard) over every clip; two-handed actions (rifles, axes, carrying a body, climbing a ladder normally) are off or use one-handed variants. Left or right comes from mirroring, so one set of clips serves both sides.
  - One leg: walking needs its own small locomotion set (crutch, or a hop and limp; a prosthetic later is the normal set with a limp layer). No crutch: crawling (the army crawl above, or a sitting scoot).
  - No legs (zombies): the crawl set (zombie_crawl in the lab) is the whole locomotion; the sim's `crawler` already moves slowly and slips under barricades.
  - IK: hand IK targets that need the lost hand are dropped; weapons and items attach to the remaining hand's socket.
- **Ragdoll.** The physical bones below the cut are disabled (and the parent's mass reduced), so a one-armed body falls as one.
- **LOD.** The merged mid/far mesh is rebuilt when a limb is lost (it already is on outfit changes); zombie crowd outfits include a few legless and armless variants with their own VAT clips (crawl, one-armed shamble).
- **The sim needs more detail than it has.** `lostArm` / `lostLeg` don't say which side or where the cut is. For the visuals (and for one-handed rules) the pose intent needs side and level: e.g. `lostLimbs: ["armLeft:elbow", "legRight:knee"]`. A sim change, made when the visuals need it. Zombies could lose limbs in fights (a hash roll per heavy hit), which is where most crawlers would come from.

In the lab (2026-11-08): the mannequin is region meshes with stump caps at the joints (wrist, elbow, shoulder; ankle, knee, hip); the Missing limbs pickers hide the regions below a cut and show the cap; a lost hand or forearm turns on that arm's stump layer (a Blend2 filtered to the arm's bones, over every state); any lost leg means crawling (no crutch clips yet), both legs the zombie drag. Not yet: mid-limb cuts, clothing, ragdoll.

## 4. Animation in Godot

- **Libraries**: one `AnimationLibrary` per set on the shared skeleton: `loco` (idle, walk, run, sprint, sneak, crouch-walk, backwards, turns, limp variants), `combat`, `interact`, `traversal`, `zombie`, `paired`. Clip names `set/verb_variant`, e.g. `interact/door_open_quiet`, `paired/boost_helper`.
- **AnimationTree** per near-range character:
  - locomotion: a `BlendSpace2D` over local velocity (sim speed and direction relative to facing), so strafing and aiming-while-walking are free;
  - a state machine for stance and actions (stand, crouch, sit, lie, action, down);
  - an **upper-body layer** (Blend2 with a bone filter from `Spine` up) for carrying, aiming, holding a torch, while the legs walk;
  - **additive layers** for injury (limp, held arm), exhaustion (breathing, slump) and zombie twitches: cheap variety without new clips.
- **Variation without new clips**: mirroring (left/right limp from one clip), additive offsets, per-character playback rate ±5%, a small set of idle fidgets chosen by `hashRoll` of id (stable per character).
- **IK at runtime** (near range only, Godot's SkeletonModifier3D stack: look-at, two-bone IK for arms and legs): foot placement on stairs and slopes, hands onto door handles / steering wheel / ladder rungs / a weapon's off-hand grip, head look-at. Built for feet (2026-11-08, godot/Characters/FootIK.cs): a probe modifier (rays under the animated feet, targets that keep the clip's foot height over the real ground, the hips dropped for the lower foot), Godot's TwoBoneIK3D (thigh and shin, knee to a pole ahead), an align modifier (soles tilted to the ground, capped at 40°). The lab's IK test (`--iktest=DIR`, results in experiments/ik-test/iktest.md): standing on a 15 cm kerb and a 25° hill facing eight ways, soles within 0.6 cm (up to 15 cm off without IK); walking up, down and across, the planted foot never sinks. Note Skeleton3D restores its poses after the modifiers: read the final pose inside a modifier, not from the bone afterwards. Stairs (18 cm rise, 28 cm going): standing within 0.6 cm facing four ways, walking up, down and across without sinking; toes could still poke into the next riser (only the ankle was ray-tested; since the toe ray, below: 0-3 cm). **Toe ray, foot angle, foot locking (2026-11-08)**: (1) Found by measuring toe tips: the leg's TwoBoneIK3D carries the foot round with the shin as it bends the knee, and FootAlign only restored the clip's foot angle on slopes, so on a kerb or a step the toes pointed down into it (15 cm in standing at a kerb, 18-29 cm walking up stairs); it now always restores it. (2) A second ray under the toe tip: where the ground there stands above the plane under the ankle (a step), the toes are raised about the ankle up to 35 deg and only a lip's rest (4 cm at most) lifts the foot (lifting it whole left the heel 15-20 cm up). Toes into a step: kerb 15.6 -> 2.8 cm standing, 15.0 -> 0.1 walking onto it, stairs 18 -> 5.3 walking up. (3) Foot locking: a foot that touches down (the clip's ankle within 1 cm of its floor) stays where it landed, height and all, until the clip lifts it, then eases back over ~0.1 s; it lets go if the leg would stretch 25 cm. The rays are cast where the foot actually goes (held or easing), not under the clip's foot: on stairs those stand over different steps. Walk test (`--walktest`, experiments/ik-test/walktest.md: the root really moves): a planted foot slid 7-11 cm a step from 1.37 to 3.5 m/s, round a 3 m circle, over the kerb and up and down stairs; locked, 0.0-0.2 cm. Below ~1.2 m/s the lab blends idle into the walk and the feet travel too little for the speed: locked, it still slides ~21 cm (the lock lets go at its stretch limit); that's the clips' and the speed mapping's to fix (a slow-walk clip or stride warping), not the lock's. The ground ray now starts 2 m up (from 1.2 m, a point under the 1.26 m stair landing started inside it, and the walk test began under the stairs). **Hands** (godot/Characters/HandIK.cs): TwoBoneIK3D per arm to where a grip puts the wrist, elbows to poles down and out, then each hand turned to the grip (a grip is a node: origin at the palm's centre, X the palm's normal, Y the fingers); weights blend the clip's arm and the grip (a reach is a weight 0 -> 1). Test props in the lab: pistol (both hands), rifle (butt in the shoulder, both hands), knife (one hand), bat (both hands, over the right shoulder), aimed by a pitch; guns also **aimed at eye level** (2026-11-08): the rear sight on the line from the eye along the aim (the right eye for the rifle, with a cheek weld: the head tipped onto the stock; between the eyes for the pistol held out in both hands), the pitch shared by the spine (60%), neck and head; raising blends the ready hold into the aimed one over the raise time. This runs inside the skeleton update before the arm IK (a modifier), so the gun sits on this frame's body, slump and breathing included; ready holds ride the upper chest the same way. A door whose lever the character reaches, presses and pushes, quietly (slow, follows the door in, lets go at 30 deg) or fast (a shove, lets go at 14 deg). Hand test (`--handtest=DIR`, experiments/ik-test/handtest.md): every hand on its grip to under 1 mm, standing and walking, aim -30/0/+30, and through both door openings. Found by it: the rifle's handguard was out of reach of the mannequin's arms (5-8 cm short): the arms are now an adult's (0.30 + 0.28 m) and the handguard 48 cm from the butt.
- **Where the clips come from** (be realistic):
  1. **CC0 / free libraries for the bulk**, retargeted on import: Quaternius's animation libraries (CC0), Mixamo (free with an Adobe account; usable in games, raw files not redistributable). Locomotion and generic combat should come from here or from mocap, not hand-keyframing.
  - **Weapon actions, keyed over IK (2026-11-08, godot/Characters/LabActions.cs)**: a bat swing (wind-up, contact in front at chest height, follow-through, the spine twisting with it), a knife stab, recoil (kick back, muzzle up, settle) and a reload (the gun tipped in; the left hand to the well, the magazine out, a fresh one from the left hip, seated, back to the grip; a magazine prop in the gun or the hand). Each is an action with progress 0..1 (as the sim would run it): the weapon's hold is keyed and the hands follow by IK, so they stay on the grips (hand test: 0.0 cm, the bat's left hand 1.3 at worst). Blocking quality: timing and weight still want a real clip or the player's eye.
  2. **Blender-authored with AI help** for what libraries don't have (door variants, barricading, boosts, zombie grabs): Claude scripts poses and keys through MCP or Python, and the player judges and corrects. Expect blocking-quality first passes; keyframing good motion through scripts is the weakest link of the whole pipeline.
  3. Optional later: video-to-mocap tools for specific actions (a new dependency: ask first).
- **Retargeting, built (2026-11-08)**: baked in Blender (art/scripts/retarget.py, a bone map per library in
  art/scripts/bonemaps/), not Godot's import-time BoneMap, so the clips land on our exact rig, pass the validator, and
  need no import setup. Each mapped bone takes the source bone's world rotation relative to its rest, applied to ours,
  then swung (least rotation) to point where the source bone points: rest differences would otherwise carry into the
  pose (the library's shoulders rest 31° back; without the swing our hands sat 10 cm forward in a pistol aim). Hips
  are placed so the point between the hip joints follows the source's, scaled by leg length (the hips bone sits at
  different heights relative to the hip joints on the two rigs, so following it moved the feet 1-3.5 cm with every
  pelvis tilt). First library: Quaternius's Universal Animation Library, Standard (CC0, 43 clips: locomotion, crouch,
  pistol, punches, sword, sitting, kneeling repair, push, pick-up, death, hits, swim, drive...) in
  godot/assets/characters/clips_ual.glb, our ankles within ~1 cm of the source's in every clip. The lab's Library
  tab plays them. Not done yet: putting library locomotion into the blend space (the cycles start in their own phase,
  not ours), and the library's 10 cm shorter toe bone means feet roll off the toe a little differently.

### First person

First person is the solo view (2026-11-08). The player's own body: a separate **arms rig** (the same arm bone names, so arm clips can be shared) with its own FOV-friendly clips, timed from the same sim progress as the third-person clips so intent and timing stay identical. The full body stays for shadows and for others. Nothing in the plan above blocks this.

### States on the body and face (2026-11-08, in the lab)

Survivors show how they are; zombies have two faces only, **neutral** (slack, the jaw a little open) and **angry** (it has seen you: brows down, the upper lip pulled off the teeth, the jaw open in a growl), as two blend shapes (jaw_open, angry) on the near LODs; no face below them (2026-11-08, the look lab). The sim already has the numbers: BodyMod's panic and endurance (0..100, PANICKING 60, WINDED 25) and body heat, Health and Wounds for pain; a startle would be a sim event (a zombie suddenly noticed, a hit taken). The pose intent carries them as 0..1 weights, and the renderer turns them into layers over whatever clip plays:

| state | body | face | movement (the sim's call; the lab shows it) |
|---|---|---|---|
| panic (stiff) | shoulders up, arms held close and bent (less swing), quick shallow breaths, the head glancing round | brows worried, eyes wide, mouth tight | quicker to speed up |
| fatigue (sluggish) | slumped, head down, arms hanging slack, slow heavy heaves | heavy lids, slack mouth, slow blinks | slower to speed up, lower top speed, slower to crouch |
| cold | hunched, arms in, shivering | tight mouth | |
| pain | (a limp is a clip's job) | brows down, eyes narrowed, grimace | |
| startle | a ~1 s flinch: shoulders and head jerk back, arms thrown up | brows up, eyes wide, mouth open | |

How: postures and breathing are **additive clips** (Godot's Add2 adds their difference from the rest pose, so they sit on any clip); arm holds **replace** the clip's arms through a bone filter (Blend2); breathing, shivering and the panicked glance are additive loops with their own rate. The face is **blend shapes** (shape keys from Blender: brows up / frown / worry, eyes wide / heavy, mouth open / frown / tight), mixed per frame from the states, plus blinking (often when panicked, slow when tired). All of it is near-tier only: faces aren't visible further out, and mid-tier characters keep the merged mesh's single clip.

## 5. Interactions, anchors and approaches

- **Anchors are sim data**, per thing kind: a door has `front`/`back` stand points and a handle height; a bed has `lie` and `sit_left/right`; a car has `driver_door`, `driver_seat`, `passenger_*`, `boot`; a tree has chop points around it; a window has `climb_in`/`climb_out`. Each is an offset from the thing in SUB units plus a facing. The sim walks the mover to the anchor and starts the action; the renderer turns the same offset into a transform for alignment and IK targets.
- **Approaches** per interaction, as a sim table:

```
door.open:  quiet  { time 2.0x, noise 0.2x, stamina 0.5x, anim interact/door_open_quiet }
            normal { time 1.0x, noise 1.0x,               anim interact/door_open }
            fast   { time 0.5x, noise 2.0x,               anim interact/door_open_fast }
            force  { time 0.7x, noise 5.0x, stamina 2.0x, success roll, anim interact/door_shoulder }
```

  Character state (stamina, injuries, encumbrance, stance) scales these and can remove options (no "force" with a broken arm). Distinct variants are distinct clips (a quiet open is a different body pose, not the normal clip slowed down); when a variant clip doesn't exist yet the renderer falls back to the normal clip time-scaled, so sim features never wait for animation.
- **Paired actions** (boost, pull up, carry, drag, grab and struggle): one sim action with roles and an anchor shared by both participants; each role has its clip; the clips are authored together in one Blender file (two rigs, one scene) so they line up, and exported as two clips. Interruption ends the action for both and each gets its own exit.
- This needs sim work (approach on commands, anchors per kind, a paired-action record). It's a sim change with tests like any other, done when the matching animation phase needs it, not before.
- **Built in the sim (2026-11-08)**: cs/Sim/Systems/PoseIntents.cs, the pose intent of section 1 as a pure read (not saved): stance (stand, sneak, sit in a car, lie asleep, crawl for crawlers, dead), speed in m/s and heading, facing, the controlled survivor's action with progress 0..1, its target and anchor, carry (weapon, tool, both hands, pack, cart), condition weights (limp and arm per side, exhaustion, panic, cold, pain, infection, blood, wet, startle) and lost limbs. Survivors keep `lostLimbs` ("legLeft:knee": an amputation's side by hash, arms at the elbow, legs at the knee) and `startledAt` (a blow from a zombie or an animal, or the dead turning up while calm). Car actions (VehicleActions.cs) are timed, in ticks whatever the day length: getting in 2.5 s and out 2 s (E; walking off cancels; the car rolls to a stop while you get out), the boot (U) and the bonnet (I) open and shut in 0.8 and 1 s; the car keeps bootOpen and hoodOpen. Anchors (driverDoor on the car's left, boot behind, hood in front, each with the way to face) are where a body stands for each; the renderer maps the names onto its model. Schema 72. The sim's car is still a 0.43 m circle where the 3D one is 4.2 m: the anchors sit round the circle for now.
- **The boot and the bonnet, in the lab (2026-11-08, LabCarLids.cs)**: lids on hinges (the bonnet's at the windscreen, the boot's at the rear window) over dark bays; opening, the character bends to the edge, lifts it palms up and lets go near the top of its reach (the lid swings on to 40 / 45 deg); shutting, it reaches up, palms on top, and pushes it down. Car test: hands on the edge 0.0 cm opening; shutting 0.2 (bonnet) and 6.5 cm at worst (the boot, at first touch). At 45 / 50 deg the raised edges were out of reach to shut (16-26 cm): opened less, stood nearer.
- **A car, in the lab (2026-11-08, godot/Characters/LabCar.cs)**: a mock-up compact (4.2 x 1.76 m, left-hand drive) with a driver's door hinged at its front, a seat (H-point 55 cm up, seatback 18 deg), a wheel tilted 25 deg, accelerator, brake and footrest. Getting in is one action with progress 0..1 (5 s, as the sim would time it): left hand on the outer handle, pull the door to 25 deg stepping back along the handle's arc (it swings on to 42), round the door's edge into the opening, back to the seat, hips down onto its edge (the body blends into the retargeted ual_driving clip, feet planted outside), swivel in (legs over the sill to the pedals, right hand to the wheel), lean out, pull the door shut by its inner pull, left hand to the wheel. Getting out plays it backwards with its own walk. Seated: the point between the hip joints on the H-point, pelvis reclined to the seatback (-0.3 cm: touching), neck and head levelling the gaze, hands on the wheel at 9 and 3 (push-pull: the rim turns by the steering, the hands by half), the right foot on the accelerator or the brake (moving between them with a lift), pitched by how far it's pressed, the left on the footrest. How: a SeatPose modifier first in the skeleton's stack, the legs' own TwoBoneIK3D and a PedalAlign, then the hand IK; the ground foot IK is off while the seat has the legs. The hand IK gained ShoulderReach: a grip beyond the straight arm turns the clavicle towards it (up to 25 deg), which took the out-of-reach cases (wheel at +-60, the door handle as it swings) from 3-10 cm to 0-1.5. Car test (`--cartest=DIR`, experiments/ik-test/cartest.md): seated, every hand and foot on its target, steering +-60 within 1.5 cm; getting in and out, hands within 0-5 cm except the moment the hand takes the inner pull at full lean (13 cm, for a frame or two) and the outer handle just before letting go (7 cm). The sim's car is a 0.43 m collision circle: anchors (driver door, seat) and an enter/exit action with progress are the sim side, when the game's 3D view needs them.

## 6. Ragdoll

- Godot: `PhysicalBoneSimulator3D` with `PhysicalBone3D`s generated for the humanoid profile (~11-15 bodies: hips, spine, chest, head, upper/lower arms, upper/lower legs; hands and feet folded into their parents), capsule shapes, cone/hinge joint limits, masses roughly proportional. Defined **once** for the shared skeleton and checked in a test scene.
- Full ragdoll for knockdowns, deaths and throws; **partial** ragdoll (upper body only, blended with the clip) for hit reactions and being shoved.
- Recovery: when the sim's get-up time comes, read the ragdoll's hip orientation (face up or face down), pick the matching get-up clip, blend from the ragdoll pose over ~0.2 s. The sim position the body ends at is the sim's; if the ragdoll drifted, the blend slides it there.
- **Budget**: at most ~8 simulated ragdolls at once, near range only; past that (or after a few seconds of rest) a body freezes into a static pose. Corpses that must persist become a static mesh in their last pose.
- **Built in the lab (2026-11-08, godot/Characters/LabRagdoll.cs, the Ragdoll tab)**: 12 bodies on the profile's names (pelvis and chest boxes, a head sphere, capsules for the spine and the limbs; hands and feet folded into the forearms and shins), cone joints with swing and twist limits, hinges at elbows (about the vertical in the T-pose: palms down, the forearm folds forward) and knees (about left-right), ~75 kg. Knock down (backwards or forwards, an impulse at the chest), a partial hit (spine up and arms at influence 0.6 for 0.6 s, eased out), get up: once at rest, which way up is read from the pelvis, the pose as drawn is captured in world space and the physics stopped, the root moves under the hips turned so the lying or prone clip's body lies along the ragdoll's, and the bones blend from the captured pose into the clip over 0.4 s, then get_up or prone_up. Test (`--ragtest`, experiments/ik-test/ragtest.md): at rest after 1.6 s, read the right way up both ways, the head moves at most 0.1-1.2 cm a frame through the blend. **Decided: Jolt** (Godot 4.6's built-in, project.godot physics/3d/physics_engine): Godot Physics never let the ragdoll rest (bodies jittering at 1-3 m/s after it lay down); Jolt settles in under 2 s. Only the lab uses 3D physics. Found on the way: (1) the simulator's pose is relative to the skeleton, so moving the root while it still has a say (blending by its influence) drags the body a metre and more: blend from a captured world pose instead; (2) its joints stretch under an impulse (the chest came 20 cm off the hips in a hit): every bone but the hips gets its rest offset back after it (no clip translates another bone); (3) the bodies exist, following the clip, even when not simulating: they're on collision layer 2 and the foot IK's rays look at layer 1 only; (4) a hinge whose axis lies along its bone gives a NaN joint frame (the elbows, at first).

## 7. LOD (characters)

LOD has four layers, and the sim already has the outermost one (dormant zombies, herds, abstract settlements):

| Tier | When | Mesh | Animation | Extras |
|---|---|---|---|---|
| **Near** | close to the camera, small count (budget ~30-40) | full outfit, separate pieces | AnimationTree, every frame, layers | IK, ragdoll, attachments, full shader |
| **Mid** | on screen, medium distance or over the near budget (~200) | outfit **merged into one mesh**, Godot's auto mesh LOD | AnimationPlayer only (base state clip), updated every 2-4 frames, staggered | no IK, no ragdoll, simplified shader |
| **Far / crowd** | colony bird's-eye, hordes, past the mid budget | merged mesh, lowest LOD | **no skeleton**: vertex animation textures (VAT) on a `MultiMeshInstance3D`, per-instance clip and phase in custom data | one draw call per outfit variant |
| **Gone** | off screen or very far | billboard/dot or nothing | none | (sim: dormant) |

- Tiers are picked by **screen size and a count budget**, not distance alone, and use hysteresis so characters don't flicker between tiers. The iso/bird's-eye camera puts most characters at mid or far most of the time, so mid and far are not an afterthought: they're the common case.
- **VAT**: a Blender (or Godot tool) script bakes the few crowd clips (zombie idle, shamble, run, attack, fall) per merged zombie outfit into textures. Zombies use a limited set of **pre-built outfit combinations** (say 20-40) so a horde is a handful of MultiMeshes.
- Mesh LOD: Godot generates LODs for skinned meshes on import, but the ladders are made in Blender (build_lookdev.py) so the counts are ours; the art budgets (first person, 2026-11-08, "The look") are near ~10-15k tris dressed, mid ~3-6k, far ~600-1.2k.
- **Measured** (2026-11-08, `godot/Characters/Bench.tscn`, results in experiments/anim-bench/README.md, fast desktop): per character per frame an AnimationTree costs ~18 µs, an AnimationPlayer ~12 µs, a player updated every 4th frame ~3.6 µs; the cost is per animated skeleton, not per track (168 -> 15 channels changed little). Separate meshes cost more than animation: 2,000 characters of 14 meshes took 77 ms unanimated, merged 8 ms. So: near ~30-40 characters with trees and separate meshes, mid ~200-300 merged and throttled, far VAT on MultiMesh. **VAT measured** (2026-11-08, godot/Characters/Vat.cs and vat.gdshader, the scene HordeBench.tscn, experiments/anim-bench/horde.md): an outfit's merged mesh baked with four clips at 30 fps (positions only, RGBA float; flat normals from derivatives in the shader; colours and conditions as at mid range, per instance), four outfits 159 ms and 10.9 MB; drawn by one MultiMesh per outfit with clip, phase, speed and seed in the instance's custom data and dirt/blood/torn/wet in its colour: 5,000 in 2.9 ms, 10,000 in 5.5, 20,000 in 10.8 (shadows on: 7.8, 13.4, 26.1), five draw calls, the CPU ~0.1 ms whatever the count: it's all GPU, ~0.5 µs a character at ~2,800 triangles. A far LOD mesh (500-800 triangles, as budgeted) would take most of that; half-float textures would halve the memory.

## 8. Godot structure

```
godot/
  Characters/
    CharacterView.cs      one near/mid character: skeleton, outfit pieces, AnimationTree, reads a pose intent
    PoseIntent.cs         maps sim state -> stance/locomotion/action/carry/condition (pure read)
    Outfit.cs             slots, layers, hidden regions, merge-to-one-mesh
    Sockets.cs            loads sockets.json, places BoneAttachment3Ds
    Ragdoll.cs            activate (full/partial), recover, budget
    CrowdView.cs          far tier: MultiMesh + VAT per outfit variant
    CharacterLod.cs       picks tiers each frame by screen size and budgets
  assets/characters/      exported .glb (base, garments, hair, gear, clip libraries), sockets.json, outfits.json
art/                      Blender sources and scripts (not in the Godot project)
  blend/                  .blend files (Git LFS)
  scripts/                build_base.py, transfer_weights.py, validate.py, export.py, bake_vat.py
  README.md               how to run each script headless
```

The 3D world renderer (terrain, buildings, the projection in Camera.cs) is its own project ("the look", NOTES.md "Next" item 4). Until it exists, characters are developed in a **character lab** scene (`godot/Characters/Lab.tscn`; since 2026-11-08 a floor with a 1 m grid, the iso camera and the test mannequin walking, running and idling). To come: a door, a bed, a car mock-up, and debug UI to set a pose intent by hand. It can also replay a sim run's movers from a save to check that animation follows sim movement.

## 9. AI-assisted production: what Claude can and can't do well

Good, and worth automating:
- Writing and running Blender Python: building the base rig to the profile's names, generating garment blanks around the body, weight transfer, shape-key transfer, naming, UVs on simple pieces, material setup, export settings, VAT baking.
- **Validation**, the most valuable script: bone names and hierarchy match the profile; ≤4 weights per vertex and normalised; no unweighted vertices; triangle budgets per tier; scale 1.0 and applied transforms; clip names follow the convention; every garment declares slot, layer and hidden regions. Run it on every export (later in `./check.sh --all` style, if Blender is on the machine).
- Inspection through MCP: viewport screenshots of a pose, a deformation test at extreme angles, a garment against the body.
- Retargeting setup and batch import settings in Godot.

Weak, needs the player's eye:
- Artistic modelling of faces, hair and silhouettes from scratch (expect blockouts; using CC0 base meshes as starting points is a reasonable shortcut).
- Weight painting quality at shoulders, hips and clothing folds.
- Animation feel (timing, weight, anticipation). Libraries and mocap first.

Claude Code skills to make later, once each workflow has been done by hand once: `blender-export` (validate + export + reimport check), `new-garment` (blank from body, weights, slot data), `new-clip` (rig scene, keys, export into the right library).

## 10. Order of work

**Phase 0: tools (now).** Install Blender; install and connect the Blender MCP server; prove the round trip: a CC0 test character (e.g. a Quaternius character) → Blender → validator → `.glb` → Godot, plays a clip, retargets a second library's clip onto it. Decide Git LFS for `.blend` files.

**Phase 1: technical spikes in the character lab** (each a small, throwaway check; results in the session log):
1. Retargeting onto the humanoid profile from two sources.
2. AnimationTree: locomotion blend space driven by a fake sim speed/heading, an upper-body layer, an additive limp.
3. IK: foot on a step, hand on a door handle (which 4.6 modifiers work).
4. Ragdoll on the profile: activate, partial, get-up blend.
5. **Crowd benchmark**: VAT + MultiMesh vs skeletons, counts and ms.
6. Outfit merge to one mesh at runtime: cost and correctness (done 2026-11-08: OutfitMerge.cs).

**Phase 2: base character.** Our own base body and rig to the standards above, skinned; idle/walk/run (library clips retargeted); export, validate, look at it in the lab at the iso angle.

**Phase 3: modular outfit.** Shoes, trousers, shirt, jacket, one hair, one hat, a backpack; hidden regions; the condition shader (blood, dirt, torn).

**Phase 4: representative clips.** Crouch, melee swing, reload, sit and sleep, chop, door quiet vs fast, knockdown and get-up; a zombie shamble and grab.

**Phase 5: the sim side and the paired prototype.** Approaches and anchors in the sim (doors first, tested); pose intent wired from real sim state; a wall boost as the first paired action; grabs and knockdown with ragdoll recovery.

**Phase 6: expand**, driven by the 3D renderer's progress and playtests: more garments and zombie outfits, the LOD tiers in the real game, first-person when it's wanted.

## Decided (2026-11-08)

- **No Git LFS, ever** (asked again 2026-11-08: still no; sizes below). The scripts in art/scripts are the source for generated assets; their .blend files are build outputs and stay out of git. A hand-made .blend that must be kept goes in git only if it is small (saved with Blender's compression, a few MB at most); anything bigger lives outside the repo, with the script or export that matters committed. Exported .glb files (small, low-poly) are committed so the game builds without Blender.
  - Sizes: the mannequin's .blend is 0.3 MB as Blender saves it (compressed, the default) and 2.3 MB uncompressed. A finished stylised character with its clips: ~1-5 MB; textures packed into a .blend add ~1-10 MB each (keep them as separate PNGs instead). GitHub refuses files over 100 MB and warns over 50 MB; a repository is comfortable under ~1-2 GB. Git stores every committed version of a binary whole (compressed .blend files hardly delta), so 50 assets x 3 MB x 10 commits each is ~1.5 GB. Without LFS that's fine if .blend files are committed at milestones, not every save, and textures stay out of them. If it ever outgrows that, `git lfs migrate` can move the history over later (it rewrites history), so nothing is closed off.
- **Base meshes are our own** (body, garments, hair, gear: built by script and refined in Blender), so they fit the rig, the regions and the stump cuts exactly.
- **Animation clips may come from free third-party libraries** (CC0 first, e.g. Quaternius) retargeted onto our rig, with our own scripted clips for what they lack.
- **Animation update rate by LOD tier:** full rate near, 20 or 15 fps (staggered) mid, baked far (the bench numbers in section 7).
