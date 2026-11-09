// The world plan's speed-ups (docs/worldgen-speed.md) give the same worlds: each fast version against the original.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class TownsSpeedTest
{
    [Fact(DisplayName = "towns > siting by prefix sums places the same towns as the original")]
    public void same_towns()
    {
        foreach (var size in new[] { 128, 200, 256, 384, 512, 700 })
            foreach (var map in new[] { "mainland", "quarantine" })
                for (int seed = 1; seed <= 6; seed++)
                {
                    var n = NatureMod.natureFor(seed, size, map);
                    var land = Towns.landGrid(n);
                    var want = Original(n, land);
                    var got = Towns.siteTowns(n, land);
                    expect(got.Count).toBe(want.Count);
                    for (int i = 0; i < want.Count; i++)
                    {
                        expect($"{got[i].x},{got[i].y},{got[i].radius},{got[i].kind},{got[i].eastWest}").toBe($"{want[i].x},{want[i].y},{want[i].radius},{want[i].kind},{want[i].eastWest}");
                    }
                }
    }

    // Towns.siteTowns as it was before 2026-11-09 (every town x every site x a disc of the land grid).
    static List<Town> Original(Nature n, Uint8Array land)
    {
        var N = JsMath.ceil(((double)(n.size) / (4)));
        List<Town> towns = [];
        var kinds = Towns.townKinds(n.seed, n.size);
        kinds.forEach((kind, i) =>
        {
            var (lo, hi) = Js.Get(Towns.RADII, kind);
            var radius = JsMath.min(JsMath.floor(((double)(n.size) * (0.19))), lo + JsMath.floor(((double)(NatureMod.roll(n.seed, i, 2, 0x7102)) * (((hi - lo) + 1)))));
            SiteTownsBest? best = null;
            var margin = radius + 3;
            for (var y = margin; y < (n.size - margin); y += 8)
            {
                for (var x = margin; x < (n.size - margin); x += 8)
                {
                    if (JsMath.sqrt((JsMath.pow((x - n.start.x), 2)) + (JsMath.pow((y - n.start.y), 2))) < ((radius + NatureMod.START_CLEAR_RADIUS) + 6))
                    {
                        continue;
                    }
                    if (towns.some((t) => JsMath.sqrt((JsMath.pow((x - t.x), 2)) + (JsMath.pow((y - t.y), 2))) < ((radius + t.radius) + 48)))
                    {
                        continue;
                    }
                    double inside = 0;
                    double dry = 0;
                    double wooded = 0;
                    double around = 0;
                    double wet = 0;
                    var reach = radius + 14;
                    for (var j = JsMath.max(0, JsMath.floor(((double)((y - reach)) / (4)))); j <= JsMath.min(N - 1, JsMath.floor(((double)((y + reach)) / (4)))); j++)
                    {
                        for (var k = JsMath.max(0, JsMath.floor(((double)((x - reach)) / (4)))); k <= JsMath.min(N - 1, JsMath.floor(((double)((x + reach)) / (4)))); k++)
                        {
                            var d = JsMath.sqrt((JsMath.pow(((((double)(k) * (4)) + 2) - x), 2)) + (JsMath.pow(((((double)(j) * (4)) + 2) - y), 2)));
                            var c = Js.NN(Js.Get(land, ((double)(j) * (N)) + k));
                            if (d <= radius)
                            {
                                inside++;
                                if (c != 2)
                                {
                                    dry++;
                                }
                                if (c == 1)
                                {
                                    wooded++;
                                }
                            }
                            else if (d <= reach)
                            {
                                around++;
                                if (c == 2)
                                {
                                    wet++;
                                }
                            }
                        }
                    }
                    var dryShare = ((double)(dry) / (JsMath.max(1, inside)));
                    if (dryShare < 0.9)
                    {
                        continue;
                    }
                    var wetShare = ((double)(wet) / (JsMath.max(1, around)));
                    var score = ((dryShare - ((double)(((double)(wooded) / (JsMath.max(1, inside)))) * (0.6))) + (((wetShare > 0.02) && (wetShare < 0.3)) ? 0.3 : 0)) + ((double)(NatureMod.roll(n.seed, x, y, 0x7103)) * (0.15));
                    if (!Js.T(best) || (score > best.score))
                    {
                        best = new SiteTownsBest { x = Js.I(x), y = Js.I(y), score = score };
                    }
                }
            }
            if (Js.T(best))
            {
                towns.push(new() { x = Js.I(best.x), y = Js.I(best.y), radius = radius, kind = kind, eastWest = (NatureMod.hash(n.seed, best.x, best.y, 0x7104) % 2) == 0 });
            }
        });
        return towns;
    }
}
