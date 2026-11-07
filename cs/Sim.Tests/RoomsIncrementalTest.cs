// Ported from tests/rooms-incremental.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class RoomsIncrementalTest
{

    /// <summary>rooms worked out round a change</summary>
    public class D001_rooms_worked_out_round_a_change
    {

        [Fact(DisplayName = "rooms worked out round a change > match rooms worked out from scratch, change after change")]
        public void T002_match_rooms_worked_out_from_scratch_change_after_change()
        {
            var state = Worldgen.createWorld(5, 128);
            for (var y = 0; y < 128; y += Worldgen.CHUNK)
            {
                for (var x = 0; x < 128; x += Worldgen.CHUNK)
                {
                    Worldgen.ensureGeneratedAround(state, new Point { x = Js.I(x), y = Js.I(y) });
                }
            }
            Rooms.getRoomMap(state);
            for (var n = 0; n < 120; n++)
            {
                double r(double k)
                {
                    return Hash.hashRoll(n, k, 0x900d);
                }
                var rooms = Rooms.getRoomMap(state).rooms;
                var room = ((rooms.length > 0) && (r(0) < 0.7)) ? Js.Get(rooms, JsMath.floor(((double)(r(1)) * (rooms.length)))) : null;
                var x = Js.T(room) ? (room.minX + JsMath.floor(((double)(r(2)) * (((room.maxX - room.minX) + 2))))) : (1 + JsMath.floor(((double)(r(2)) * (126))));
                var y = Js.T(room) ? (room.minY + JsMath.floor(((double)(r(3)) * (((room.maxY - room.minY) + 2))))) : (1 + JsMath.floor(((double)(r(3)) * (126))));
                var what = r(4);
                if (what < 0.4)
                {
                    Grids.setEdge(state.grid, x, y, (r(5) < 0.5) ? "n" : "w", Grids.EDGE_NONE);
                }
                else if (what < 0.65)
                {
                    Grids.setEdge(state.grid, x, y, (r(5) < 0.5) ? "n" : "w", Grids.EDGE_WALL);
                }
                else if (what < 0.8)
                {
                    Grids.setEdge(state.grid, x, y, (r(5) < 0.5) ? "n" : "w", Grids.EDGE_DOOR);
                }
                else if (what < 0.9)
                {
                    Grids.setTile(state.grid, x, y, "wall");
                }
                else
                {
                    Grids.setTile(state.grid, x, y, "floor");
                }
                state.walkabilityVersion++;
                var fast = Rooms.getRoomMap(state);
                var full = Rooms.computeRooms(state.grid);
                expect((fast.labels).ToList(), $"change {Js.Str(n)} at {Js.Str(x)},{Js.Str(y)}").toEqual((full.labels).ToList());
                expect(fast.rooms.map((q) => Js.Arr([q.index, q.tiles, q.doors])), $"change {Js.Str(n)}").toEqual(full.rooms.map((q) => Js.Arr([q.index, q.tiles, q.doors])));
            }
        }
    }
}
