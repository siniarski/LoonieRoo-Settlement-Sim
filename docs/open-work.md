# Open work

What is left, by subject. Rewritten on 2026-11-12 (log/2026-11-12-19-docs-for-a-reader.md): every line that only said
a thing was done is gone, and items the code shows done were dropped (they are in the log); what stays is open. Each
item names the log entry that says most about it. What the game does today: docs/state-of-the-game.md.
To add an item, put it under its subject below; when you finish one, delete the line. Say what is built in a log
entry, not here.

Sections: For the player to decide; Not played yet; Cars; The dead and animals; Water and the world; Buildings and
doors; The colony; Others; Weapons and inventory; The view; Animation and characters; Sound; Tests and tools;
Performance and big worlds.

## For the player to decide

Options the sessions found and did not take (each log entry has the numbers):
- **Thirst** (log/2026-11-10-05-balance-check.md): keep needs as they are (recommended: thirst is ~1% of colony deaths,
  all among the badly wounded); or relabel the death ("died of their wounds (dehydrated)"); or drink before eating.
  **Deer hunting never feeds anyone** (5-12 of ~480 deer seen in 30 colonies): is it meant to?
- **Light** (log/2026-11-10-05-lighting.md): should light draw the dead, and change who sees whom? Neither is built.
- **Snow** (log/2026-11-10-05-weather.md): tracks the dead or raiders could follow; cold from snow and wet.
  `Snow.slowsWalking` is on (deep snow slows walking up to 30%; colonies fell about a day earlier, within noise).
- **Fire** (log/2026-11-10-06-wildfire.md): survivors don't avoid fire or cut firebreaks; `settings.wildfire`.
- **Weapons** (log/2026-11-10-06-weapons.md): jamming and condition, attachments, recovering bolts, a bow, friendly
  fire, shots breaking glass.
- **Inventory** (log/2026-11-10-05-grid-inventory.md): timed identification of finds, a backpack item or bag sizes,
  icons rendered from item models, bodies holding their kit.
- **Cars** (logs 2026-11-11-08 and 2026-11-12-07/08): whether height should count against the dead on a roof (today a
  roof is a lookout, not a refuge); whether barricades should hold longer than 2.5 s against six walkers; whether
  cars should be faster than 22 km/h on a road (11 off it).
- **Tools**: dotnet-trace for tick profiling (ask first; only worth it before big worlds).

## Not played yet

Nothing built since 2026-11-05 has had a full playtest by a human beyond the player's two play lists (2026-11-11 and
-12: log/2026-11-11-16-play-pass.md, log/2026-11-12-02-second-play-list.md). The Godot UI is checked by the
self-test and screenshots only. To try, in this order of risk:
- Cars: getting in and out, driving, the boot, pushing (J), alarms, climbing on one, hiding under a pickup.
- Water: wading, swimming, strokes (log/2026-11-11-21-play-fixes-rivers.md).
- The Tab screen at 30 px cells, weapons' feel (each kind), lights at night, rain and snow, a grass fire, a town after
  30 and 300 days (`--day=30`), the dead in clothes and hair, the new walks and get-ups.
- Character creation, windows, line of sight and sneaking, the water and power going off, the map (M), neighbours'
  errands and feuds, colonists foraging, the colony panels.
- The far field (on at 500 m; the menu's Options set 0-1000 m) and the other graphics options.
- Questions: free movement and combat feel; is solo Apocalypse survivable (the scripted player dies by day 5); is
  the colony's winter readable.

## Cars

The sim's car is the size of the car drawn (log/2026-11-11-04-car-body.md); a parked one is in the way
(log/2026-11-11-06-parked-cars.md). Nobody has played any of it.
- **Crossings** (log/2026-11-11-10-crossings-merged.md): no queue, a crowd goes over one car at once. A path's cost
  for a car is the same whoever plans it (a crawler pays for a pickup as for a sedan); a horde's field costs a barricade
  as battering even for its runners. Tall fences are not offered to your own hands, only to colonists.
- **Climbers** (log/2026-11-12-08-climbers.md): the body slides up the flank lying down (no hauling clip); no clip for
  falling off (action "down") or a pile-up; no sound for the thump.
- **Your leaps** (log/2026-11-11-08-your-leaps.md): no hand on the car as you climb; the landing sound is the ground's;
  the truck's bed has no sides; you can't step from one roof to another; a thing dropped on a roof lands under the car.
- **The car you sit in** (log/2026-11-12-07-cars.md): the dead press against its side, their paths still go through it.
- **Pushing, alarms, hiding** (same entry): no pushing pose; slope is not felt; the alarm is a house bell pitched up,
  no flashing lights, nothing switches it off; no crawl clip under a pickup; only the dead are fooled (a wolf still
  knows); the dead and animals can stand a third of a metre into a car's flank.
- **Animals** (log/2026-11-11-09-animals-cars.md): meat from an animal killed mid-crossing drops on a car's tile, out of
  a colonist's reach; no climbing or crouching clips (they are lifted or squashed).
- **Driving** (log/2026-11-11-05-drivers-seat.md): no hands on the wheel, no body in the seat; mirrors are grey; no
  windscreen glass, rain or wipers; doors don't open; a trip is forgotten on reload; the dial goes to 60 and the car
  to 22; no passengers; the cabin doesn't pitch on a slope; a wall doesn't turn a car sliding along it; a wanderer or
  trader can still turn up on a car's tile.

## The dead and animals

- The 2D view and minimap don't show the dormant (log/2026-11-09-09-the-dead-s-outstanding-points.md); a dormant one's
  look is lost if it wakes outside the 40 m model range.
- The solo bot doesn't shut itself in and has no pack sense (it fights three at once).
- Zombies don't lose limbs in fights (only crawlers' legs).
- Trees: in a one-tile corridor tile walkers still pass through trunks; one trunk radius for every species
  (log/2026-11-10-05-trunks.md).

## Water and the world

- **Swimming** (log/2026-11-11-21-play-fixes-rivers.md): no swimming pose; the dead and animals stay out of shallows
  (reachability and hordes assume water is a wall); what you carry doesn't get wet; no current, no diving, no ripples,
  no depth fade (a flat translucent sheet); the far field's water is opaque. Rivers and lakes are the same water as
  ponds (thirst, sickness).
- **Ground relief** (log/2026-11-10-05-ground-relief.md, log/2026-11-12-05-smooth-ground.md): big lots on a slope are
  cut or filled up to half a metre at the edge; the far field's coarse ground differs from the near at the seam.
- **Roads** (log/2026-11-12-03-road-ribbons.md): towns kept their radii, so a third fewer streets and a fifth fewer lots
  (bigger radii move every town); no dropped kerb at a driveway; the quarantine gate is as wide as its road; snow lies
  over the kerb.
- **Towns:** streets are axis-aligned grids; thinner bands along region borders; still three settlements at any size.
  Small worlds (128): lakes are rare. The preview draws lots whole and, over 2 x 2 regions, towns only.
- **Growth, fire, weather** (logs 2026-11-10-05-growth, 2026-11-10-06-wildfire, 2026-11-10-05-weather): the balance
  players weren't re-run with growth; far trees on ungenerated land don't grow; burnt tiles still show ferns until
  regrown; fire has no sound; wet ground is one global parameter; roofs and trees don't take `snow_cover`; grass pokes
  green through deep snow.

## Buildings and doors

- Pieces still wanted by docs/buildings.md (chairs, the rest) need sim looks first, then models (log/2026-11-10-06-buildings.md).
- Real fence and gate edge kinds (fences are barricade edges with a gap); police cells only 1-2 when the corridor's
  last room is short; narrow lots lose side windows (long blank walls); a garage's 3-wide door is drawn as three doors.
- `FarField` calls a lot's `yard(x, y)` with map coordinates where `Lots` uses the lot's (u, v): an old bug.
- Storeys and basements: after the very large stress test.
- Worlds saved before a change to what a chunk is made of are refused: bump `Worldgen.GEN_VERSION` (now 3) when it changes.

## The colony

- Colonists haul one item at a time and never use grids; a coat a colonist wears hides its pockets' contents.
- Colonists never touch light switches (only the one you control).
- Colonies die in their first winter without coops or a greenhouse (balance pass 2026-11-06); watch whether a human finds them.
- Infected rising in bed among sleepers; guards arm only between jobs (see NOTES.md "Open questions").

## Others

- Residents are simple: they potter, trade in person, join, or fight; no caravans, scavenging runs or alliances of
  their own beyond the stand-ins (log/2026-11-06-17-neighbours-out-scavenging-the-truck-on.md).
- Stand-ins (wanderers, traders, prisoners, caravans, parties: Others.cs): no bound pose or cell for a prisoner; a
  caravan or run draws four people at most; a party walks straight at its next tile (through a wall if the step
  crosses one); no names over them, can't be looked at for a card (log/2026-11-11-03-models.md).

## Weapons and inventory

- No cover from furniture; the sim's body doesn't swing weapons in the world (Held's arms do).
- Held items: one sleeve colour; no hand IK on the grip; the bob isn't from your footfalls; things in both hands have
  no hands (log/2026-11-09-30-held-items.md).
- Ammunition boxes both stack in a cell (10) and merge rounds when stowed: pick one.
- Names are only flavour (one model per kind); a mixed stack moves its first item.
- The character sheet shows nothing about wounds by place or pain (the sim has none); not tuned under 1280x720.
- The three quick slots (2026-11-12): the comments at the top of `godot/Ui/Inventory.cs` and its hover line still say
  1-9.

## The view

- Lighting (log/2026-11-10-05-lighting.md): glows and lit windows are flat (no bloom); headlights are one spot; a lit
  room is one ceiling point; the flashlight's beam follows the camera's pitch but the sim's is yaw only; ovens don't
  glow while you cook.
- The world's textures and what line of sight and fog of war mean in first person (log/2026-11-11-12-textures-surfaces.md).
- A wall tile's blueprint is pegs only; the helicopter has no searchlight and its sound isn't placed at the model;
  ferns near trees poke through a farm plot.
- Furniture: a searched or emptied container looks the same; far places are only checked in `--lineup=furniture`.
- Item models: Godot's automatic mesh LODs never kick in on the MultiMeshes (the AABB is the 40 m round you).
- The map screen and minimap draw the whole map as one image (100 million pixels at 10,000).

## Animation and characters

Plan: docs/characters.md. Details: log/2026-11-10-06-animation.md, log/2026-11-12-10-animation.md.
- The mid tier slides at stops and on-the-spot turns; no ragdoll in the world (the dead fall by clip, then the corpse
  takes over with a pop); weapon actions are curves over keyed holds, not clips; no real vault clip; push-off and landing
  sounds are the step sounds pitched; sim durations that hold animation back (walk 2.5 m/s, lost leg 2, crawler 1.45).
- Clothing: modelled garments over the scripted shells need an artist; the pz jacket and hoodie bleed blue onto the face;
  hair's edge under a beanie is patchy; shoes aren't recoloured.
- A far LOD mesh (500-800 tris); VAT for the lab crowd's far tier; the ragdoll budget; approaches as sim data, paired
  actions, anchors for doors and beds; VRAM-compressed textures; a real sculpt over the blockout; the zombie's face
  driven by noticing; the first-person arms rig.
- **Traps:** importing a glTF through the Blender MCP closed Blender (import headless in art/scripts); Skeleton3D
  restores poses after its modifiers; Godot reserves shader-buffer slots per instance-uniform mesh (near tier tops out
  near 300; raised in project.godot); the glTF exporter keeps every bone's channels. The Blender MCP needs Blender open
  (localhost:9876).

## Sound

- Tuning by ear; sound round corners (paths, not lines); others' footsteps; doors, pickups, eating; indoor reverb;
  sound on the Colony tab and map; glass, alarm and engine kinds exist, the rest of Noise's don't; crawling is silent
  (log/2026-11-09-11-sound-two-muffling-your-steps-the.md).
- Weapons: no reload, pump, bolt or dry click; a crossbow plays the swing's whoosh; one gunshot for all four guns;
  blades and clubs on flesh. Fire has no sound (rain has, Ambience.cs).
- A CC0 pack can replace the made sounds (`res://assets/sounds/<kind>_<n>.ogg`), with the player's OK.

## Tests and tools

- The self-test's picture mode (`--invshot`) leaves a full bag, which fails the two weapon checks after it.
- Tests ported from TypeScript are grab-bags under old names (docs/modules.md lists them): regroup when touched.
- The solo harness barricade step finds no door (its player died on days 4 6 1 5 4 1: log/2026-11-11-20-play-fixes-roads.md); recheck now that R boards a door.
- The 1024 and 2048 stress scenes weren't profiled. Shadows, resolution and anti-aliasing options aren't benchmarked.
- Code structure (log/2026-11-11-02-structure.md), each long but one concern: `Commands.execute` (a long chain of
  `if (command.type == ...)`), `Items/InventoryGrid.cs` (1,180 lines), `Dead/Zombies.cs`, `Colony/Work.cs`,
  `godot/Ui/Inventory.cs`, `godot/Ui/Hud.cs`, `State/StateTypes.cs` (stays one file: the schema).

## Performance and big worlds

Numbers as measured on 2026-11-10; another session is working here, so check its log entry first.
- **Loading is generation** (log/2026-11-10-05-save-speed.md, log/2026-11-12-09-smooth-far.md): every generated chunk
  is made again on load (2048 fully explored: ~6 of 7.5 s), holding the frame; faster generation or a worker (the
  statics in Worldplan, Heights and Time must be thread-safe first). A save hashes every generated chunk (~60 ms at 2048).
- **The tick** (log/2026-11-10-05-tick-speed.md): Zombies.updateZombies 45-70%, reconcileJobs 19% on hordes,
  BodyMod.updateBody 18% and Needs.updateHealth 11.5% on small colonies; `Point` being a class is the largest allocation.
- **Unloading** (log/2026-11-10-06-grid-chunks.md; docs/worldplan-regions.md): a generated chunk is 51 KB, 48 KB of it
  furniture; needs furniture per chunk with a history-free order and an active region; trees out of `Furniture` would cut
  a chunk to ~12 KB.
- At 10,000 a region's plan (~1.5 s) is made inside a tick the first time the sim asks beyond the start's region
  (use `PlanSoon`); far-field regions take 5 ms each on the main thread; chunk generation is 1.6 ms at 2048; the 10,240
  preview takes ~20 s for its towns.
- Reachability unites every chunk's components at each walkability change; room indexes renumber when an early room is built.
- Left: zombies moving and crowding and A* for long paths in the stress harness; the sim on its own thread.
