namespace Swarmfall.Core;

/// <summary>
/// A 2D vector. A readonly struct, so it lives on the stack or inline in an
/// array and never costs the garbage collector anything.
/// </summary>
public readonly record struct Vec2(float X, float Y)
{
    public static readonly Vec2 Zero = new(0f, 0f);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, float s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(float s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, float s) => new(a.X / s, a.Y / s);

    public float LengthSquared => X * X + Y * Y;
    public float Length => MathF.Sqrt(LengthSquared);

    public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    /// <summary>The z component of the 3D cross product; its sign says which side b is on.</summary>
    public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

    public static float DistanceSquared(Vec2 a, Vec2 b) => (a - b).LengthSquared;

    /// <summary>Unit vector in the same direction, or zero for a zero vector instead of NaN.</summary>
    public Vec2 Normalized()
    {
        float len = Length;
        return len > 1e-6f ? this / len : Zero;
    }

    /// <summary>Caps the length at max, keeping the direction.</summary>
    public Vec2 ClampLength(float max)
    {
        float lenSq = LengthSquared;
        return lenSq > max * max ? this * (max / MathF.Sqrt(lenSq)) : this;
    }

    /// <summary>Rotates by an angle in radians: the 2D rotation matrix applied to this vector.</summary>
    public Vec2 Rotate(float radians)
    {
        float c = MathF.Cos(radians), s = MathF.Sin(radians);
        return new(X * c - Y * s, X * s + Y * c);
    }

    /// <summary>Angle of the vector in radians, measured from the +X axis.</summary>
    public float Angle => MathF.Atan2(Y, X);

    public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => a + (b - a) * t;
}
