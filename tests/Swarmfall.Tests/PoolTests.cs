using Swarmfall.Core;

namespace Swarmfall.Tests;

public class PoolTests
{
    [Fact]
    public void Spawn_fills_to_capacity_then_reports_full()
    {
        var pool = new Pool<int>(3);
        Assert.Equal(0, pool.Spawn(10));
        Assert.Equal(1, pool.Spawn(11));
        Assert.Equal(2, pool.Spawn(12));
        Assert.Equal(-1, pool.Spawn(13));
        Assert.Equal(3, pool.Count);
    }

    [Fact]
    public void Despawned_slots_are_reused()
    {
        var pool = new Pool<int>(2);
        int a = pool.Spawn(1);
        pool.Spawn(2);
        pool.Despawn(a);
        Assert.False(pool.IsActive(a));
        Assert.Equal(a, pool.Spawn(3));
        Assert.Equal(3, pool[a]);
    }

    [Fact]
    public void Live_list_stays_dense_and_exact_through_random_churn()
    {
        var pool = new Pool<int>(100);
        var live = new HashSet<int>();
        var rng = new XorShift(42);
        for (int step = 0; step < 10_000; step++)
        {
            if (live.Count > 0 && (rng.NextUInt() & 1) == 0)
            {
                int victim = pool.ActiveAt((int)(rng.NextUInt() % (uint)pool.Count));
                pool.Despawn(victim);
                live.Remove(victim);
            }
            else
            {
                int slot = pool.Spawn(step);
                if (slot >= 0) live.Add(slot);
            }

            Assert.Equal(live.Count, pool.Count);
            var listed = new HashSet<int>();
            for (int i = 0; i < pool.Count; i++) listed.Add(pool.ActiveAt(i));
            Assert.Equal(live, listed);
        }
    }

    [Fact]
    public void Despawning_twice_is_harmless()
    {
        var pool = new Pool<int>(2);
        int a = pool.Spawn(1);
        pool.Despawn(a);
        pool.Despawn(a);
        Assert.Equal(0, pool.Count);
        Assert.NotEqual(-1, pool.Spawn(2));
        Assert.NotEqual(-1, pool.Spawn(3));
        Assert.Equal(-1, pool.Spawn(4));
    }
}
