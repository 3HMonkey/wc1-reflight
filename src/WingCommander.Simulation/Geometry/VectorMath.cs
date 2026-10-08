using WingCommander.Core.Numerics;

namespace WingCommander.Simulation.Geometry;

/// <summary>
/// Fixed-point vector algebra of the 3D engine, bit-exact with the Kilrathi Saga build: every
/// product goes through <see cref="FixedMath.Multiply"/>, every quotient through
/// <see cref="FixedMath.Divide"/> (float32 operands), magnitudes through doubles with C
/// truncation. Integer adds/subtracts wrap like 32-bit C.
/// </summary>
public static class VectorMath
{
    /// <remarks>C: AddFixedVectors (0x418620, geom.c).</remarks>
    public static FixedVector Add(in FixedVector left, in FixedVector right) =>
        new(unchecked(right.X + left.X), unchecked(right.Y + left.Y), unchecked(right.Z + left.Z));

    /// <summary><c>left - right</c>.</summary>
    /// <remarks>C: SubtractFixedVectors (0x418650, geom.c).</remarks>
    public static FixedVector Subtract(in FixedVector left, in FixedVector right) =>
        new(unchecked(left.X - right.X), unchecked(left.Y - right.Y), unchecked(left.Z - right.Z));

    /// <summary><c>to - from</c>.</summary>
    /// <remarks>C: ComputeVectorDelta (0x418680, geom.c).</remarks>
    public static FixedVector Delta(in FixedVector from, in FixedVector to) =>
        new(unchecked(to.X - from.X), unchecked(to.Y - from.Y), unchecked(to.Z - from.Z));

    /// <remarks>C: negate_vector (0x418600, geom.c).</remarks>
    public static FixedVector Negate(in FixedVector v) => new(unchecked(-v.X), unchecked(-v.Y), unchecked(-v.Z));

    /// <remarks>C: equ_vector (0x418590, geom.c).</remarks>
    public static bool AreEqual(in FixedVector left, in FixedVector right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z;

    /// <summary>Component-wise <see cref="FixedMath.Multiply"/> by a 24.8 scale.</summary>
    /// <remarks>C: ScaleFixedVector (0x4186B0, geom.c).</remarks>
    public static FixedVector Scale(in FixedVector v, int scale) =>
        new(FixedMath.Multiply(v.X, scale), FixedMath.Multiply(v.Y, scale), FixedMath.Multiply(v.Z, scale));

    /// <summary>Component-wise <see cref="FixedMath.Divide"/> by a 24.8 divisor.</summary>
    /// <remarks>C: divide_vector (0x418700, geom.c).</remarks>
    public static FixedVector Divide(in FixedVector v, int divisor) =>
        new(FixedMath.Divide(v.X, divisor), FixedMath.Divide(v.Y, divisor), FixedMath.Divide(v.Z, divisor));

    /// <remarks>C: dot_product (0x4189E0, geom.c).</remarks>
    public static int Dot(in FixedVector left, in FixedVector right) =>
        unchecked(FixedMath.Multiply(left.X, right.X) + FixedMath.Multiply(left.Y, right.Y) + FixedMath.Multiply(left.Z, right.Z));

    /// <remarks>C: vector_cross_product (0x418A80, geom.c).</remarks>
    public static FixedVector Cross(in FixedVector left, in FixedVector right) => new(
        unchecked(FixedMath.Multiply(left.Y, right.Z) - FixedMath.Multiply(left.Z, right.Y)),
        unchecked(FixedMath.Multiply(left.Z, right.X) - FixedMath.Multiply(left.X, right.Z)),
        unchecked(FixedMath.Multiply(left.X, right.Y) - FixedMath.Multiply(left.Y, right.X)));

    /// <summary>Length as 24.8 (<c>Vector_magnitude</c>).</summary>
    /// <remarks>C: Vector_magnitude (0x434F20, mathfp.c).</remarks>
    public static int Magnitude(in FixedVector v) => v.Magnitude();

    /// <summary>Divides each component by the magnitude; leaves the vector untouched and returns
    /// false when the magnitude is 0.</summary>
    /// <remarks>C: NormalizeFixedVector (0x418B10, geom.c).</remarks>
    public static bool Normalize(ref FixedVector v)
    {
        int magnitude = v.Magnitude();
        if (magnitude == 0)
            return false;
        v.X = FixedMath.Divide(v.X, magnitude);
        v.Y = FixedMath.Divide(v.Y, magnitude);
        v.Z = FixedMath.Divide(v.Z, magnitude);
        return true;
    }

    /// <summary>Copy of <paramref name="v"/>, normalised.</summary>
    public static FixedVector Normalized(FixedVector v)
    {
        Normalize(ref v);
        return v;
    }

    /// <summary>Cosine between two directions in percent (-100..100), truncating division.</summary>
    /// <remarks>C: vector_angle (0x418A30, geom.c).</remarks>
    public static short VectorAngle(FixedVector left, FixedVector right)
    {
        Normalize(ref left);
        Normalize(ref right);
        return unchecked((short)((short)Dot(left, right) * 100 / 0x100));
    }

    /// <summary>Signed length of <paramref name="v"/> along a unit <paramref name="direction"/>.</summary>
    /// <remarks>C: vector_length_in_dir (0x418B60, geom.c).</remarks>
    public static int LengthInDirection(in FixedVector v, in FixedVector direction)
    {
        var normalized = v;
        Normalize(ref normalized);
        return FixedMath.Multiply(v.Magnitude(), Dot(direction, normalized));
    }

    /// <summary>Projection of <paramref name="v"/> onto a unit <paramref name="direction"/>.</summary>
    /// <remarks>C: vector_component_in_dir (0x418BB0, geom.c).</remarks>
    public static FixedVector ComponentInDirection(in FixedVector v, in FixedVector direction) =>
        Scale(direction, LengthInDirection(v, direction));

    /// <summary>Rotates <paramref name="j"/> and <paramref name="k"/> about i:
    /// <c>j' = j*c - k*s; k' = j*s + k*c</c> (component-wise, in x, y, z order).</summary>
    /// <remarks>C: rotate_about_i (0x418BE0, geom.c).</remarks>
    public static void RotateAboutI(short angle, ref FixedVector j, ref FixedVector k)
    {
        int cosine = FixedTrig.CosFixed(angle);
        int sine = FixedTrig.SinFixed(angle);
        int old = j.X;
        j.X = unchecked(FixedMath.Multiply(old, cosine) - FixedMath.Multiply(k.X, sine));
        k.X = unchecked(FixedMath.Multiply(old, sine) + FixedMath.Multiply(k.X, cosine));
        old = j.Y;
        j.Y = unchecked(FixedMath.Multiply(old, cosine) - FixedMath.Multiply(k.Y, sine));
        k.Y = unchecked(FixedMath.Multiply(old, sine) + FixedMath.Multiply(k.Y, cosine));
        old = j.Z;
        j.Z = unchecked(FixedMath.Multiply(old, cosine) - FixedMath.Multiply(k.Z, sine));
        k.Z = unchecked(FixedMath.Multiply(old, sine) + FixedMath.Multiply(k.Z, cosine));
    }

    /// <summary>Rotates <paramref name="i"/> and <paramref name="k"/> about j:
    /// <c>i' = k*s + i*c; k' = k*c - i*s</c>.</summary>
    /// <remarks>C: rotate_about_j (0x418D00, geom.c).</remarks>
    public static void RotateAboutJ(short angle, ref FixedVector i, ref FixedVector k)
    {
        int cosine = FixedTrig.CosFixed(angle);
        int sine = FixedTrig.SinFixed(angle);
        int old = i.X;
        i.X = unchecked(FixedMath.Multiply(k.X, sine) + FixedMath.Multiply(old, cosine));
        k.X = unchecked(FixedMath.Multiply(k.X, cosine) - FixedMath.Multiply(old, sine));
        old = i.Y;
        i.Y = unchecked(FixedMath.Multiply(k.Y, sine) + FixedMath.Multiply(old, cosine));
        k.Y = unchecked(FixedMath.Multiply(k.Y, cosine) - FixedMath.Multiply(old, sine));
        old = i.Z;
        i.Z = unchecked(FixedMath.Multiply(k.Z, sine) + FixedMath.Multiply(old, cosine));
        k.Z = unchecked(FixedMath.Multiply(k.Z, cosine) - FixedMath.Multiply(old, sine));
    }

    /// <summary>Rotates <paramref name="i"/> and <paramref name="j"/> about k:
    /// <c>i' = i*c - j*s; j' = i*s + j*c</c>.</summary>
    /// <remarks>C: rotate_about_k (0x418E40, geom.c).</remarks>
    public static void RotateAboutK(short angle, ref FixedVector i, ref FixedVector j)
    {
        int cosine = FixedTrig.CosFixed(angle);
        int sine = FixedTrig.SinFixed(angle);
        int old = i.X;
        i.X = unchecked(FixedMath.Multiply(old, cosine) - FixedMath.Multiply(j.X, sine));
        j.X = unchecked(FixedMath.Multiply(old, sine) + FixedMath.Multiply(j.X, cosine));
        old = i.Y;
        i.Y = unchecked(FixedMath.Multiply(old, cosine) - FixedMath.Multiply(j.Y, sine));
        j.Y = unchecked(FixedMath.Multiply(old, sine) + FixedMath.Multiply(j.Y, cosine));
        old = i.Z;
        i.Z = unchecked(FixedMath.Multiply(old, cosine) - FixedMath.Multiply(j.Z, sine));
        j.Z = unchecked(FixedMath.Multiply(old, sine) + FixedMath.Multiply(j.Z, cosine));
    }

    /// <summary>Halves every component (C <c>/ 2</c>, truncating) until all three are roughly within
    /// ±15.0 (16.16 integer part 0 with fraction ≤ 0x0f00, or -1 with fraction ≥ 0xf100). A crude
    /// normalisation without division; always halves at least once.</summary>
    /// <remarks>C: shrink_vector (0x436A30, eventmgr.c).</remarks>
    public static void ShrinkVector(ref FixedVector v)
    {
        bool shrinking;
        do
        {
            shrinking = Shrink(ref v.X);
            shrinking |= Shrink(ref v.Y);
            shrinking |= Shrink(ref v.Z);
        }
        while (shrinking);
    }

    /// <summary>One halving step of <see cref="ShrinkVector"/>; returns true while the component is still large.</summary>
    /// <remarks>C: shrink (0x436A70, eventmgr.c).</remarks>
    public static bool Shrink(ref int component)
    {
        int value = component / 2;
        component = value;
        ushort fraction = unchecked((ushort)value);
        short integerPart = unchecked((short)((uint)value >> 16));
        if (integerPart == 0)
            return fraction > 0x0f00;
        if (integerPart == -1)
            return fraction < 0xf100;
        return true;
    }

    /// <summary>True when <c>|range &lt;&lt; 8| &gt;= |v|</c>.</summary>
    /// <remarks>C: IsVectorWithinRange (0x436A00, eventmgr.c).</remarks>
    public static bool IsVectorWithinRange(in FixedVector v, short range)
    {
        int magnitude = v.Magnitude();
        int fixedRange = System.Math.Abs(range << 8);
        return fixedRange >= magnitude;
    }

    /// <remarks>C: IsPointWithinRange (0x419990, geom.c).</remarks>
    public static bool IsPointWithinRange(in FixedVector from, in FixedVector to, short range) =>
        IsVectorWithinRange(Delta(from, to), range);

    /// <summary>Distance in whole units, saturated to ±0x7fff.</summary>
    /// <remarks>C: distance_between_points (0x4191D0, geom.c).</remarks>
    public static short DistanceBetweenPoints(in FixedVector from, in FixedVector to) =>
        FixedMath.ToShortSaturating(Delta(from, to).Magnitude());

    /// <summary>
    /// Converts to radius / yaw / pitch: yaw = <c>ArcCos(z / planar(x, z))</c> negated for
    /// x &lt; 0, pitch = <c>ArcCos(y / r) - 90</c>. When the radius is 0 only
    /// <see cref="SphericalVector.Radius"/> is written (the angles keep their previous values,
    /// uninitialised stack data in the original).
    /// </summary>
    /// <remarks>C: rectangular_to_spherical (0x418890, geom.c).</remarks>
    public static void RectangularToSpherical(in FixedVector rectangular, ref SphericalVector spherical)
    {
        spherical.Radius = rectangular.Magnitude();
        if (spherical.Radius == 0)
            return;
        int z = rectangular.Z;
        int horizontalLength = FixedMath.PlanarMagnitude(rectangular.X, z);
        spherical.Yaw = unchecked((short)FixedMath.ArcCos(FixedMath.Divide(z, horizontalLength)));
        if (rectangular.X < 0)
            spherical.Yaw = unchecked((short)-spherical.Yaw);
        spherical.Pitch = unchecked((short)(FixedMath.ArcCos(FixedMath.Divide(rectangular.Y, spherical.Radius)) - 90));
    }

    /// <summary>Moves <paramref name="position"/> by <paramref name="distance"/> units along
    /// <paramref name="direction"/> (normalised first); no-op for distance 0.</summary>
    /// <remarks>C: position_relative (0x4183D0, geom.c).</remarks>
    public static void PositionRelative(ref FixedVector position, FixedVector direction, short distance)
    {
        if (distance == 0)
            return;
        Normalize(ref direction);
        direction = Scale(direction, distance * 0x100);
        position = Add(position, direction);
    }

    /// <remarks>C: NormalizeAndScaleVector (0x419950, geom.c).</remarks>
    public static void NormalizeAndScale(ref FixedVector v, int scale)
    {
        Normalize(ref v);
        v = Scale(v, scale);
    }

    /// <summary>Normalises <paramref name="v"/> and scales it to <paramref name="length"/> whole units.</summary>
    /// <remarks>C: SetVectorFixedPoint (0x419970, geom.c).</remarks>
    public static void SetLength(ref FixedVector v, short length) => NormalizeAndScale(ref v, length * 0x100);
}
