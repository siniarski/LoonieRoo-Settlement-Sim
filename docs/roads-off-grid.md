# Roads off the tile grid: what it would take (2026-11-12)

The player asked to look at roads that are "not fixed to tile and instead dynamically generated". This is the
assessment; nothing here is built. Today a road is a ground kind on tiles (`Grids.GROUNDS` "road"), laid by
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

## Recommendation

Do 1. It gives what the eye wants (smooth, angled, curving roads with proper lane lines) without changing a
single rule of the sim, so saves, balance and tests are untouched. If the half-tile disagreement at the edges
proves visible in play, do 2 for roads only.

Until then: straight roads carry a dashed centre line and solid edge lines (`WorldMesh.LaneMark`, drawn only), and
a slanted road's steps are bevelled (`WorldMesh.RoadCorner`).
