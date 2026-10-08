namespace WingCommander.Core.Numerics;

/// <summary>
/// 24.8 fixed point (0x100 = 1.0) as used by the whole 3D engine. The Kilrathi Saga build
/// implements multiply/divide/trig through the FPU with C truncation-toward-zero casts,
/// which differs from integer shifts for negative values; these helpers reproduce that
/// exactly (see docs/analysis/simulation.md §1.2). Angles are integer degrees.
/// </summary>
public static class FixedMath
{
    public const int One = 0x100;
    public const double DegreesToRadians = 0.017453292519943295;
    private const double RadiansToDegrees = 57.295779513082323;
    private const double Inv256 = 1.0 / 256.0;

    /// <summary>MSVC (long)(double): truncate toward zero, keep the low 32 bits.</summary>
    public static int Truncate(double value) => unchecked((int)(long)value);

    /// <summary>C: MultiplyFixed.</summary>
    public static int Multiply(int l, int r) => Truncate(l * Inv256 * (r * Inv256) * 256.0);

    /// <summary>C: DivideFixed. Operands are rounded to float32; division by zero divides by 1.0.</summary>
    public static int Divide(int n, int d)
    {
        float nf = (float)(n * Inv256);
        float df = d != 0 ? (float)(d * Inv256) : 1.0f;
        return Truncate((double)nf / df * 256.0);
    }

    /// <summary>
    /// C: SinFixed(deg) = (long)(sin((double)deg * DEGREES_TO_RADIANS) * 256), computed directly
    /// from the signed 16-bit argument. Not periodic: Sin(-30) = -127 but Sin(330) = -128, so the
    /// argument must never be normalised first (verified against the reference C).
    /// </summary>
    /// <remarks>C: SinFixed (0x434E00, mathfp.c).</remarks>
    public static int Sin(short degrees) => Truncate(System.Math.Sin(degrees * DegreesToRadians) * 256.0);

    /// <summary>C: CosFixed(deg), computed directly like <see cref="Sin"/>.</summary>
    /// <remarks>C: CosFixed (0x434E30, mathfp.c).</remarks>
    public static int Cos(short degrees) => Truncate(System.Math.Cos(degrees * DegreesToRadians) * 256.0);

    /// <summary>C: ArcSin(v): integer degrees.</summary>
    public static int ArcSin(int v) => Truncate(System.Math.Asin(v * 0.00390625) * RadiansToDegrees);

    /// <summary>C: ArcCos(v): integer degrees.</summary>
    public static int ArcCos(int v) => Truncate(System.Math.Acos(v * 0.00390625) * RadiansToDegrees);

    /// <summary>C: Magnitude(v): fixed square root of a fixed value.</summary>
    public static int Sqrt(int v) => Truncate(System.Math.Sqrt(v * 0.00390625) * 256.0);

    /// <summary>C: PlanarMagnitude(x, y).</summary>
    public static int PlanarMagnitude(int x, int y)
    {
        double fx = x * Inv256, fy = y * Inv256;
        return Truncate(System.Math.Sqrt(fx * fx + fy * fy) * 256.0);
    }

    /// <summary>C: SignFixed: 0x100, 0 or -0x100.</summary>
    public static int Sign(int v) => v > 0 ? One : v < 0 ? -One : 0;

    /// <summary>C: FixedToShortSaturating: integer part clamped to ±0x7fff.</summary>
    public static short ToShortSaturating(int v)
    {
        if (v < -0x7fff00)
            return -0x7fff;
        if (v > 0x7fff00)
            return 0x7fff;
        return unchecked((short)(v >> 8));
    }

    /// <summary>C: WrapDegrees: d % 360, then below -180 add 360, above 180 subtract 360 (-180 stays).</summary>
    public static short WrapDegrees(short degrees)
    {
        int d = degrees % 360;
        if (d < -180)
            d += 360;
        if (d > 180)
            d -= 360;
        return unchecked((short)d);
    }

}

/// <summary>Three 24.8 fixed-point components. Mutable on purpose: the engine updates components in place.</summary>
/// <remarks>C: FixedVector.</remarks>
public struct FixedVector : IEquatable<FixedVector>
{
    public int X;
    public int Y;
    public int Z;

    public FixedVector(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static readonly FixedVector Zero = default;

    public readonly bool IsZero => X == 0 && Y == 0 && Z == 0;

    /// <summary>C: Vector_magnitude.</summary>
    public readonly int Magnitude()
    {
        double fx = X / 256.0, fy = Y / 256.0, fz = Z / 256.0;
        return FixedMath.Truncate(System.Math.Sqrt(fx * fx + fy * fy + fz * fz) * 256.0);
    }

    public static FixedVector operator +(FixedVector a, FixedVector b) =>
        new(unchecked(a.X + b.X), unchecked(a.Y + b.Y), unchecked(a.Z + b.Z));

    public static FixedVector operator -(FixedVector a, FixedVector b) =>
        new(unchecked(a.X - b.X), unchecked(a.Y - b.Y), unchecked(a.Z - b.Z));

    public static FixedVector operator -(FixedVector a) => new(unchecked(-a.X), unchecked(-a.Y), unchecked(-a.Z));

    public static bool operator ==(FixedVector a, FixedVector b) => a.Equals(b);

    public static bool operator !=(FixedVector a, FixedVector b) => !a.Equals(b);

    public readonly bool Equals(FixedVector other) => X == other.X && Y == other.Y && Z == other.Z;

    public override readonly bool Equals(object? obj) => obj is FixedVector v && Equals(v);

    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);

    public override readonly string ToString() => $"({X / 256.0:0.###}, {Y / 256.0:0.###}, {Z / 256.0:0.###})";
}
