# Buildings (2026-11-10/11: built)

How generated buildings are planned and stamped: `cs/Sim/Systems/Buildings.cs` (the Draft builder), `BuildingsHouses.cs`,
`BuildingsTrade.cs`, `BuildingsCivic.cs`, `Lots.cs` (what stands on a lot, and drawing it a chunk at a time). The
world plan that cuts the lots is docs/worldplan-regions.md; the lot sizes are Worldplan's (residential 12-15 x 13-15,
commercial 12-15 x 12-14, civic 20-26 x 18-24, industrial 22-28 x 18-24, cabin 13 x 13, farms bigger).

## The plan

- A plan is drawn in its own frame, the street side at the bottom (y = h - 1), then turned to face its street and
  maybe mirrored (`Lots.transform`). It is a pure function of a roll (`Hash.hashRoll` of the seed and lot), never the
  sim RNG.
- **Rooms of floor cells, walls on edges.** A `Draft` holds a room index per cell (-1 outside); a wall stands on every
  edge between two rooms or a room and the outside, unless a door is put there; windows are edges too. Rooms are
  rectangles, joined (`extend`) into L shapes where a room needs it. The plan's rectangle is the footprint: Heights
  levels it and a one-tile apron round it; an L-shaped house leaves a corner of it outside (porches stand there).
- **Doors** (`join(a, b)`): in the middle of the wall two rooms share, away from its ends, or where the roll puts it;
  `width` for wide doors (garages, loading bays, double front doors). `frontDoor` records `plan.front`, the outer door
  on the street side.
- **Windows** by room kind (`Buildings.WINDOWS`): living rooms "many" (a pair in the middle of each outer wall, more
  along a long one), bedrooms and kitchens "some" (one a wall, every third tile on a long one), bathrooms and toilets
  "one", shop fronts and lobbies "front" (the whole street wall glazed, no side windows: the sides are for shelving),
  barns, stores, cells, corridors "none", workshops and warehouse halls "high" (sparse). Never beside a door in the
  same wall, never between two rooms. A room that should have one and got none (every spot beside a door) takes the
  first spot that will. `Buildings.shutSides`: on the lot, no window in a side wall whose outside tile is within a
  tile of the lot's edge (the neighbour's wall would be a hand's breadth off).
- **Furniture by function** (`Draft.put` and helpers): against a wall, in a corner, in the middle, `bed` (head to one
  wall, not in a corner, the foot and one side clear, a nightstand on the other side), `run` (pieces side by side
  along one wall: a kitchen's stove, sink, fridge and counters; lockers; racking), `facing` (a sofa across the room in
  line with the television). Every placement is checked: never on a tile beside a door, the room's doors still joined
  by free floor, every container in the room still beside that floor. Tall pieces (wardrobe, bookcase, shelf, fridge,
  locker, machine, pallet) never stand in front of a window.
- `Buildings.ascii(plan)` prints a plan (walls `-` `|`, doors `~` `:`, windows `o`, furniture by `GLYPHS`).

## Archetypes

Dwellings are kind "house" (other sessions key on it), the archetype in `plan.style`:

| style | size (floor) | where | layout |
|---|---|---|---|
| cottage | 8-9 x 7-8 | town, narrow or shallow lots | a hall up from the front door; kitchen at the back (back door) opening on the living room; one bedroom, the bathroom, a utility room if there's depth, off the hall |
| bungalow | 9-10 x 8-9 (+3 garage) | town | as the cottage with two bedrooms; a galley kitchen when only 8 deep; often an attached garage (3 wide, a 3-wide door, a door into the kitchen, a back door) |
| family | 12-13 x 9-10 | town lots 14+ wide | three bedrooms (two at the back, one at the front) and the bathroom off an L-shaped hall; a utility room behind the kitchen when the left column is 6 wide |
| farmhouse | as bungalow or family | farms | always a utility room (a mudroom); no garage; always a porch |
| cabin | 7-8 x 6-7 (+2 lean-to) | cabin lots | one main room (a tiled kitchen corner, stove, table, sofa) with the bedroom and bathroom off it; sometimes a lean-to woodshed with its own door |
| trailer | 10-12 x 4 | cabin lots (a third) | a single-wide: living room and kitchen (back door), a short hall to the bathroom and the bedroom |
| tiny | 6-8 x 6-7 | the fallback for a lot too small for a cottage | living-kitchen, bedroom, bathroom |

Shapes on top (by roll, as the lot's depth allows): a **wing** (the living room reaching two tiles forward: an L with
the front door in its corner), a **bay** (2-3 tiles on the living room's front, glazed), a **rear** kitchen wing (a T).
About 30% of the town's front doors open into the living room instead of the hall.

Other kinds:
- **Shops** (grocery, pharmacy, hardware; a petrol station's kiosk): a glass front with a double door, the till and
  counter by the door, shelving along the side walls and in aisles (a cross-aisle at the back), a grocery's fridges or
  a pharmacy's cabinets along the back wall; behind, a storeroom with the back door and a staff toilet.
- **Bar** (counter along a side wall, tables) and **diner** (counter across the back facing the kitchen, tables along
  the windows): a kitchen behind with the back door, a toilet off the room. Loot table "bar".
- **Workshop**: a concrete bay behind a 3-wide door, toolboxes and shelving round it, a machine, often a car in; a front
  office with its own door; a toilet. Loot table "garage".
- **Office** (commercial): reception across the front, a corridor, two offices and a toilet, a side door.
- **Police, clinic, school, office block** (civic): a lobby across the front (benches, a front desk) and a corridor
  up the middle to the back door (a school's corridor runs to its front door); rooms down each side by `ROLES`:
  offices, lockers, an armoury, police cells (a walkway beside the corridor, cells two deep with a cot and a toilet),
  wards, exam rooms (cot, cabinet, sink), stores, toilets, classrooms (pupils' tables in rows, the teacher's desk,
  shelves, windows).
- **Church**: a porch room behind the double door, the nave (pews either side of the aisle facing the altar), a vestry
  in a back corner with its own door out.
- **Warehouse / factory**: a concrete hall behind 3-wide loading doors (two at the front on wide ones, one at the
  back), racking in rows with two-tile aisles and a cross-aisle (a factory: machines and some shelving), an office
  (own door, window) and a toilet in a front corner.
- **Barn**: a 3-wide front door and a 2-wide back one, hay along the walls, a feed room (sacks, tools), sometimes a
  tractor. **Shed**: 2-3 x 2, a toolbox and a shelf.

## Lots

- Every building and its apron inside the lot (tested). Residential: set back 2-3; a third of the deepest lots keep a
  long back yard (a smaller house) with room for a shed.
- A paved **path** from the street to the front door; a **porch** (boards either side of the step, a bench) on half
  the houses, 60% of cabins, every farmhouse; a concrete **driveway** to a garage's door, where the lot's car parks
  (else in the street, as before: CAR_CHANCE unchanged).
- **Fences** on 35% of house lots (FENCE_CHANCE): plank edges (`EDGE_BARRICADE`: you climb over, the dead can't)
  round the back yard and across from the house's back corners to the lot's sides, a one-tile gap for a gate.
- **Wear** (by hash, as before): walls fall with ruin decay; 8% of doors gone, 20% open; 30% of windows broken, 8%
  boarded; and now 6% of houses beyond the start's calm boarded up whole (every window boarded, doors shut).

## Generation version

`Worldgen.GEN_VERSION` (2 since these buildings) is saved in `WorldInfo.gen` (absent: 1). Saves keep only what differs
from making a chunk again, so `Savefile.loadFile` refuses a generated world's save or bug report whose version
differs, before making any chunk, with "made with world generation version N, this is M ...". Hand-made maps load as
before. Bump it whenever what a chunk is made of changes.

## Furniture wanted (no model or kind yet)

Chairs (dining, desk, school), a kitchen stove distinct from the oven, bar stools, a reception counter, a fence and a
gate as their own edge kinds (planks stand in), hedges and garden bushes, a coat rack, a wash basin for cells, a bunk
bed, a hospital bed, a filing cabinet, a whiteboard or blackboard, a pulpit, a forklift, a workbench for workshops.
