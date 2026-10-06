namespace Swarmfall.Core;

/// <summary>
/// Turns variable frame times into a whole number of fixed simulation steps.
///
/// The simulation always advances by exactly StepSeconds, so physics behaves the
/// same at 30 FPS as at 300 FPS and the game is deterministic. Rendering then
/// draws between the last two simulation states using Alpha, so motion stays
/// smooth even when the frame rate and the step rate disagree.
///
/// MaxSteps guards against the "spiral of death": if a frame takes so long that
/// catching up would need more steps than that, the leftover time is dropped and
/// the game slows down instead of freezing.
/// </summary>
public sealed class FixedStepClock
{
    private double _accumulator;

    public FixedStepClock(double stepSeconds, int maxStepsPerFrame = 8)
    {
        if (stepSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(stepSeconds));
        if (maxStepsPerFrame <= 0) throw new ArgumentOutOfRangeException(nameof(maxStepsPerFrame));
        StepSeconds = stepSeconds;
        MaxStepsPerFrame = maxStepsPerFrame;
    }

    public double StepSeconds { get; }
    public int MaxStepsPerFrame { get; }

    /// <summary>How far between the previous and current state to draw, in [0, 1).</summary>
    public float Alpha => (float)(_accumulator / StepSeconds);

    /// <summary>Time dropped so far because frames were too slow to catch up.</summary>
    public double DroppedSeconds { get; private set; }

    /// <summary>Adds a frame's elapsed time and returns how many steps to simulate.</summary>
    public int Advance(double frameSeconds)
    {
        if (frameSeconds < 0) frameSeconds = 0;
        _accumulator += frameSeconds;
        int steps = (int)(_accumulator / StepSeconds);
        if (steps > MaxStepsPerFrame)
        {
            DroppedSeconds += (steps - MaxStepsPerFrame) * StepSeconds;
            steps = MaxStepsPerFrame;
            _accumulator -= (int)(_accumulator / StepSeconds) * StepSeconds; // keep only the fraction
        }
        else
        {
            _accumulator -= steps * StepSeconds;
        }
        return steps;
    }
}
