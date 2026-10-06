using Swarmfall.Core;

namespace Swarmfall.Tests;

public class Vec2Tests
{
    private const float Eps = 1e-5f;

    [Fact]
    public void Normalized_has_unit_length()
    {
        var v = new Vec2(3, 4).Normalized();
        Assert.Equal(1f, v.Length, Eps);
        Assert.Equal(0.6f, v.X, Eps);
        Assert.Equal(0.8f, v.Y, Eps);
    }

    [Fact]
    public void Normalizing_zero_gives_zero_not_NaN()
    {
        var v = Vec2.Zero.Normalized();
        Assert.Equal(Vec2.Zero, v);
    }

    [Fact]
    public void Rotate_quarter_turn_maps_x_axis_to_y_axis()
    {
        var v = new Vec2(1, 0).Rotate(MathF.PI / 2);
        Assert.Equal(0f, v.X, Eps);
        Assert.Equal(1f, v.Y, Eps);
    }

    [Fact]
    public void Rotation_preserves_length()
    {
        var rng = new XorShift(7);
        for (int i = 0; i < 1000; i++)
        {
            var v = new Vec2(rng.Range(-100, 100), rng.Range(-100, 100));
            Assert.Equal(v.Length, v.Rotate(rng.Range(-10, 10)).Length, 1e-3f);
        }
    }

    [Fact]
    public void Dot_of_perpendicular_vectors_is_zero_and_cross_gives_side()
    {
        var a = new Vec2(2, 0);
        var left = new Vec2(0, 5);
        Assert.Equal(0f, Vec2.Dot(a, left));
        Assert.True(Vec2.Cross(a, left) > 0);
        Assert.True(Vec2.Cross(a, -left) < 0);
    }

    [Fact]
    public void ClampLength_caps_long_vectors_and_keeps_short_ones()
    {
        Assert.Equal(5f, new Vec2(30, 40).ClampLength(5).Length, Eps);
        Assert.Equal(new Vec2(1, 1), new Vec2(1, 1).ClampLength(5));
    }

    [Fact]
    public void Lerp_hits_endpoints_and_midpoint()
    {
        var a = new Vec2(0, 0);
        var b = new Vec2(10, -20);
        Assert.Equal(a, Vec2.Lerp(a, b, 0));
        Assert.Equal(b, Vec2.Lerp(a, b, 1));
        Assert.Equal(new Vec2(5, -10), Vec2.Lerp(a, b, 0.5f));
    }
}
