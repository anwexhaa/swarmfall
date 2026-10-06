using Swarmfall.Core;

namespace Swarmfall.Tests;

public class WorldTests
{
    private const float Dt = 1f / 120;

    private static World Run(uint seed, int steps, Broadphase mode = Broadphase.Grid)
    {
        var world = new World(new WorldConfig(), seed) { Broadphase = mode };
        for (int i = 0; i < steps; i++)
        {
            // a scripted player that circles, so the run exercises movement too
            world.MoveInput = new Vec2(1, 0).Rotate(i * 0.01f);
            world.Step(Dt);
        }
        return world;
    }

    [Fact]
    public void Same_seed_same_game()
    {
        Assert.Equal(Run(7, 2000).StateHash(), Run(7, 2000).StateHash());
    }

    [Fact]
    public void Different_seed_different_game()
    {
        Assert.NotEqual(Run(7, 500).StateHash(), Run(8, 500).StateHash());
    }

    [Fact]
    public void Step_allocates_nothing_after_warm_up()
    {
        var world = new World(new WorldConfig { AutoWaves = false, Invulnerable = true }, 1);
        world.SpawnScattered(3000);
        for (int i = 0; i < 120; i++) world.Step(Dt); // warm up: JIT, first-use paths

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++)
        {
            world.MoveInput = new Vec2(0, 1).Rotate(i * 0.05f);
            world.Step(Dt);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void The_player_kills_enemies_and_scores()
    {
        var world = Run(3, 120 * 10);
        Assert.True(world.Kills > 0);
        Assert.True(world.Score > 0);
    }

    [Fact]
    public void A_player_who_stands_still_in_a_swarm_eventually_dies()
    {
        var world = new World(new WorldConfig { AutoWaves = false, FireInterval = 1000f }, 5);
        world.SpawnScattered(500);
        for (int i = 0; i < 120 * 60 && !world.GameOver; i++) world.Step(Dt);
        Assert.True(world.GameOver);
        Assert.Equal(0, world.PlayerHealth);
    }

    [Fact]
    public void Everything_stays_inside_the_arena()
    {
        var world = Run(11, 3000);
        var c = world.Config;
        for (int i = 0; i < world.Enemies.Count; i++)
        {
            var p = world.EnemyPosition[world.Enemies.ActiveAt(i)];
            Assert.InRange(p.X, 0, c.Width);
            Assert.InRange(p.Y, 0, c.Height);
        }
        Assert.InRange(world.PlayerPosition.X, 0, c.Width);
        Assert.InRange(world.PlayerPosition.Y, 0, c.Height);
    }

    [Fact]
    public void Brute_force_and_grid_agree_on_the_outcome_of_a_short_fight()
    {
        // Neighbour order differs between the two, so floating-point sums drift
        // apart and the runs are not bit-identical. Over a short fight they should
        // still land in the same place: similar kills, both alive.
        var grid = Run(21, 600, Broadphase.Grid);
        var brute = Run(21, 600, Broadphase.BruteForce);
        Assert.False(grid.GameOver);
        Assert.False(brute.GameOver);
        Assert.InRange(brute.Kills, grid.Kills * 0.7, grid.Kills * 1.3 + 5);
    }
}
