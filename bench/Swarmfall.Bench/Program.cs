// Headless benchmark: runs the real simulation, no window, and times each step.
//
//   dotnet run -c Release --project bench/Swarmfall.Bench
//
// For each enemy count it runs the same seeded fight twice, once with the
// spatial grid and once with brute force, and reports step times. A step has
// to finish inside 8.33 ms for the game to hold 120 simulation steps a second,
// and inside 16.7 ms for 60 FPS.
using System.Diagnostics;
using Swarmfall.Core;

const float Dt = 1f / 120;
const int WarmupSteps = 60;
int[] counts = args.Length > 0 ? args.Select(int.Parse).ToArray() : [500, 1000, 2000, 4000, 8000];

Console.WriteLine($"Swarmfall benchmark | .NET {Environment.Version} | {Environment.ProcessorCount} logical cores | {RuntimeInfo()}");
Console.WriteLine();
Console.WriteLine("| Enemies | Broadphase | Steps | Mean ms | p50 ms | p99 ms | Max ms | Bytes allocated |");
Console.WriteLine("|--------:|------------|------:|--------:|-------:|-------:|-------:|----------------:|");

foreach (int n in counts)
{
    foreach (var mode in new[] { Broadphase.Grid, Broadphase.BruteForce })
    {
        // Brute force at high counts takes seconds per run; fewer steps keep it bearable.
        int steps = mode == Broadphase.BruteForce && n >= 4000 ? 120 : 600;
        var r = Measure(n, mode, steps);
        Console.WriteLine($"| {n,7:N0} | {mode,-10} | {steps,5} | {r.Mean,7:F3} | {r.P50,6:F3} | {r.P99,6:F3} | {r.Max,6:F3} | {r.Allocated,15:N0} |");
    }
}

static (double Mean, double P50, double P99, double Max, long Allocated) Measure(int enemies, Broadphase mode, int steps)
{
    var world = new World(new WorldConfig { AutoWaves = false, Invulnerable = true, MaxEnemies = Math.Max(enemies, 1) }, 2026)
    {
        Broadphase = mode,
    };
    world.SpawnScattered(enemies);

    for (int i = 0; i < WarmupSteps; i++) Step(world, i);

    var times = new double[steps];
    long allocBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < steps; i++)
    {
        long t0 = Stopwatch.GetTimestamp();
        Step(world, WarmupSteps + i);
        times[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;

    Array.Sort(times);
    return (times.Average(), Percentile(times, 0.50), Percentile(times, 0.99), times[^1], allocated);
}

static void Step(World world, int i)
{
    world.MoveInput = new Vec2(1, 0).Rotate(i * 0.01f); // the player circles, as in the tests
    world.Step(Dt);
}

static double Percentile(double[] sorted, double p) => sorted[Math.Min(sorted.Length - 1, (int)(p * sorted.Length))];

static string RuntimeInfo() =>
    $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim()}, {(Debugger.IsAttached ? "debugger" : "no debugger")}";
