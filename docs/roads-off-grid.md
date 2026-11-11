# Roads off the tile grid: what it would take (2026-11-12)

The player asked to look at roads that are "not fixed to tile and instead dynamically generated". This is the
assessment. **Step 1 is built** (2026-11-12, `godot/World3D/RoadRibbons.cs`): country roads are drawn as
ribbons along the plan's lines, with lane lines, near and far, joined at junctions, streets and bridges; the rest
is not, and the measured disagreement says it need not be (below). Today a road is a ground kind on tiles (`Grids.GROUNDS` "road"), laid by
`cs/Sim/Worldgen/Roads.cs` from the world plan's road lines (which are already lines between points: `PointD`),
six tiles wide in towns since 2026-11-11, and everything else reads tiles: lots front onto road tiles, cars
drive faster on road tiles, kerbs sink road tiles, paths cost by tile.

## What "off the grid" can mean, cheapest first

1. **Draw the plan's lines, keep the tiles (recommended first step).** The sim keeps road tiles exactly as now.
   The renderer stops drawing a road as its tiles and draws a ribbon along the plan's centre line instead: a
   strip of constant width with smooth edges, kerbs and lane lines following the curve, and the tiles under it
   only decide what the sim does. A slanted or curving road then looks like a road, not stairs. Cost: the plan's
   road lines must be kept in state (or regenerated from the seed per chunk, as lots are), a ribbon mesher per
   chunk, and the ground mesh must not draw asphalt where the ribbon is. Risk: where the ribbon and the tiles
   disagree by up to half a tile, you can stand "on grass" that drives like road, or the reverse. Days, not weeks.
2. **Roads as shapes in the sim.** A road is a polyline with a width in state; "is this point on a road" is a
   distance to a line, not a tile lookup. Cars get their road speed from the shape; lots are placed against the
   shape. Tiles stay for walls, floors and paths. This removes the disagreement in 1 but touches worldgen (lots,
   parking, driveways), vehicles, the scripted players and every test that paints road tiles. Two to three weeks,
   and every generated world changes.
3. **Everything off the grid** (buildings at any angle along curving streets). That is a different sim: rooms,
   edge walls, pathfinding and the colony's building all assume the grid. Not recommended.

## How far the ribbon and the tiles disagree (measured 2026-11-12)

The harness's `road edges` (cs/Harness/RoadEdges.cs) samples every country road tile and its neighbours 4x4 a tile
against the ribbon (ROAD_REACH, 2.9 m, each side of the line). Seeds 1, 2, 3 and 42 at 512 tiles (16-19 roads,
1800-2300 m of line each):

| | per metre of road | deepest |
|--|--|--|
| looks road, drives as grass | 0.03-0.05 m2 | 0.52 m |
| looks grass, drives as road | 0.14-0.23 m2 | 0.52 m |

11-14% of road tiles have some wrong point; a wrong point is a median 0.13 m and 90% of them under 0.46 m from
the drawn edge. Against a road 5.8 m wide that is 3-4% of its area, all in a band along the edges. A car's wheel
can be half a metre into the grass and still on road (or the reverse): never the car's body, never a lane.
**Not worth step 2 for this.** Step 2 would change every generated world, worldgen's lots and parking, vehicles and
the scripted players for a disagreement no one standing on the road can see. Revisit only if driving at the very
edge (a road speed while the wheels look on grass) shows up in play; then a cheaper fix is a car's road speed by
distance to the plan's line, not the whole of step 2.

## Joins and the far view (2026-11-12, log 09)

The roads of every planned region in reach are drawn (`RoadNet`: a road crossing into the next region is in both
plans, drawn once). Where roads meet a disc of asphalt fills the corner and no lane line runs through the other
road; a road is cut along a town street's kerb line; over a bridge the ribbon is a deck of planks with its sides
down to the water, the bridge's tiles drawn as water under it. The far field draws the same lines, coarser.

## Recommendation

Do 1. It gives what the eye wants (smooth, angled, curving roads with proper lane lines) without changing a
single rule of the sim, so saves, balance and tests are untouched. If the half-tile disagreement at the edges
proves visible in play, do 2 for roads only.

Until then: straight roads carry a dashed centre line and solid edge lines (`WorldMesh.LaneMark`, drawn only), and
a slanted road's steps are bevelled (`WorldMesh.RoadCorner`).
