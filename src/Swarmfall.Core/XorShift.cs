namespace Swarmfall.Core;

/// <summary>
/// A tiny deterministic random number generator (xorshift32). The same seed
/// always produces the same game, which is what makes the simulation testable
/// and the benchmark repeatable. A struct, so drawing a number allocates nothing.
/// </summary>
public struct XorShift
{
    private uint _state;

    public XorShift(uint seed) => _state = seed == 0 ? 0x9E3779B9u : seed;

    public uint NextUInt()
    {
        uint x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }

    /// <summary>Uniform float in [0, 1).</summary>
    public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

    public float Range(float min, float max) => min + (max - min) * NextFloat();
}
