using Swarmfall.Core;

namespace Swarmfall.Tests;

public class FixedStepClockTests
{
    [Fact]
    public void One_second_of_frames_gives_exactly_one_second_of_steps()
    {
        var clock = new FixedStepClock(1.0 / 120, maxStepsPerFrame: 100);
        int steps = 0;
        // 61 uneven frames that add up to one second
        for (int i = 0; i < 60; i++) steps += clock.Advance(i % 2 == 0 ? 0.012 : 0.0213333333);
        steps += clock.Advance(1.0 - 30 * 0.012 - 30 * 0.0213333333 + 1e-9);
        Assert.Equal(120, steps);
    }

    [Fact]
    public void Alpha_is_the_leftover_fraction_of_a_step()
    {
        var clock = new FixedStepClock(0.01);
        Assert.Equal(2, clock.Advance(0.025));
        Assert.Equal(0.5f, clock.Alpha, 1e-4f);
    }

    [Fact]
    public void A_huge_frame_is_capped_instead_of_spiralling()
    {
        var clock = new FixedStepClock(0.01, maxStepsPerFrame: 5);
        Assert.Equal(5, clock.Advance(2.0)); // 200 steps owed, 5 taken
        Assert.Equal(1.95, clock.DroppedSeconds, 6);
        Assert.InRange(clock.Alpha, 0f, 1f);
        Assert.Equal(1, clock.Advance(0.01)); // and it recovers on the next frame
    }

    [Fact]
    public void Negative_frame_time_is_ignored()
    {
        var clock = new FixedStepClock(0.01);
        Assert.Equal(0, clock.Advance(-5));
        Assert.Equal(1, clock.Advance(0.01));
    }
}
