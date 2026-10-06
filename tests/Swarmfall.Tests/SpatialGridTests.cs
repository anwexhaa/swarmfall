using Swarmfall.Core;

namespace Swarmfall.Tests;

/// <summary>
/// The grid is only worth having if it finds exactly what checking every point
/// would find. These tests compare it against brute force on random layouts.
/// </summary>
public class SpatialGridTests
{
    private const float W = 800, H = 600, Cell = 20;

    private static (Vec2[] pos, int[] slots) RandomPoints(ref XorShift rng, int n)
    {
        var pos = new Vec2[n];
        var slots = new int[n];
        for (int i = 0; i < n; i++)
        {
            pos[i] = new Vec2(rng.Range(0, W), rng.Range(0, H));
            slots[i] = i;
        }
        return (pos, slots);
    }

    [Fact]
    public void Query_matches_brute_force_on_random_layouts()
    {
        var rng = new XorShift(12345);
        for (int trial = 0; trial < 200; trial++)
        {
            int n = 1 + (int)(rng.NextUInt() % 2000);
            var (pos, slots) = RandomPoints(ref rng, n);
            var grid = new SpatialGrid(W, H, Cell, n);
            grid.Build(slots, pos);

            var center = new Vec2(rng.Range(0, W), rng.Range(0, H));
            float radius = rng.Range(0, Cell);
            var results = new int[n];
            int found = grid.Query(center, radius, pos, results);

            var expected = Enumerable.Range(0, n)
                .Where(i => Vec2.DistanceSquared(pos[i], center) <= radius * radius)
                .OrderBy(i => i);
            Assert.Equal(expected, results.Take(found).OrderBy(i => i));
        }
    }

    [Fact]
    public void Nearest_matches_brute_force_on_random_layouts()
    {
        var rng = new XorShift(999);
        for (int trial = 0; trial < 300; trial++)
        {
            int n = 1 + (int)(rng.NextUInt() % 500);
            var (pos, slots) = RandomPoints(ref rng, n);
            var grid = new SpatialGrid(W, H, Cell, n);
            grid.Build(slots, pos);

            var center = new Vec2(rng.Range(0, W), rng.Range(0, H));
            float maxR = rng.Range(10, 400);
            int got = grid.Nearest(center, maxR, pos);

            int want = -1;
            float best = maxR * maxR;
            for (int i = 0; i < n; i++)
            {
                float d2 = Vec2.DistanceSquared(pos[i], center);
                if (d2 < best) { best = d2; want = i; }
            }

            if (want < 0) Assert.Equal(-1, got);
            else Assert.Equal(Vec2.DistanceSquared(pos[want], center), Vec2.DistanceSquared(pos[got], center));
        }
    }

    [Fact]
    public void Points_on_the_world_edge_are_still_found()
    {
        var pos = new[] { new Vec2(0, 0), new Vec2(W, H), new Vec2(W, 0) };
        var grid = new SpatialGrid(W, H, Cell, pos.Length);
        grid.Build(new[] { 0, 1, 2 }, pos);
        var results = new int[3];
        Assert.Equal(1, grid.Query(new Vec2(W - 1, H - 1), 5, pos, results));
        Assert.Equal(1, results[0]);
    }

    [Fact]
    public void Query_stops_when_the_result_buffer_is_full()
    {
        var pos = Enumerable.Repeat(new Vec2(100, 100), 50).ToArray();
        var grid = new SpatialGrid(W, H, Cell, pos.Length);
        grid.Build(Enumerable.Range(0, 50).ToArray(), pos);
        Assert.Equal(8, grid.Query(new Vec2(100, 100), 5, pos, new int[8]));
    }
}
