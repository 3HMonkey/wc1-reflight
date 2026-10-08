namespace WingCommander.Simulation.Geometry;

/// <summary>Integer helpers of the original with their exact 16-bit semantics.</summary>
public static class ScalarMath
{
    /// <summary>Wraps degrees into (-180, 180]. The argument is a C <c>short</c>: callers must
    /// truncate wider values first (<c>unchecked((short)x)</c>), as the original call did.</summary>
    /// <remarks>C: WrapDegrees (0x418560, geom.c).</remarks>
    public static short WrapDegrees(short degrees)
    {
        int v = degrees % 360;
        if (v < -180)
            v += 360;
        if (v > 180)
            v -= 360;
        return (short)v;
    }

    /// <remarks>C: MinShort (0x41D0C0, mathutil.c).</remarks>
    public static short MinShort(short a, short b) => a < b ? a : b;

    /// <remarks>C: MaxShort (0x41D0E0, mathutil.c).</remarks>
    public static short MaxShort(short a, short b) => a > b ? a : b;

    /// <remarks>C: MinInt (0x4184E0, geom.c).</remarks>
    public static int MinInt(int a, int b) => a <= b ? a : b;

    /// <remarks>C: MaxInt (0x4184F0, geom.c).</remarks>
    public static int MaxInt(int a, int b) => b <= a ? a : b;

    /// <remarks>C: AbsInt (0x418500, geom.c); int.MinValue stays negative like the C negation.</remarks>
    public static int AbsInt(int v) => v < 0 ? unchecked(-v) : v;

    /// <summary>-1, 0 or 1.</summary>
    /// <remarks>C: SignShort (0x418520, geom.c).</remarks>
    public static short SignShort(short v) => v < 0 ? (short)-1 : v > 0 ? (short)1 : (short)0;

    /// <summary>Returns <paramref name="magnitude"/> with the sign of <paramref name="sign"/> (unreachable in the original).</summary>
    /// <remarks>C: intfract_sign (0x418510, geom.c).</remarks>
    public static int IntFractSign(int sign, int magnitude) => sign >= 0 ? magnitude : unchecked(-magnitude);

    /// <summary>Clamped linear remap of <paramref name="input"/> from [inMin, inMax] to [outMin, outMax]
    /// in int arithmetic (truncating division).</summary>
    /// <remarks>C: find_ratio (0x423BA0, logic.c).</remarks>
    public static short FindRatio(short inputMinimum, short inputMaximum, short input, short outputMinimum, short outputMaximum)
    {
        if (input < inputMinimum)
            return outputMinimum;
        if (input > inputMaximum)
            return outputMaximum;
        return unchecked((short)((short)((outputMaximum - outputMinimum) * (input - inputMinimum)
            / (inputMaximum - inputMinimum)) + outputMinimum));
    }

    /// <summary>Clamps a rotation rate to ±30 degrees/frame.</summary>
    /// <remarks>C: ClampTo30 (0x41A110, geom.c).</remarks>
    public static void ClampTo30(ref short value)
    {
        if (value > 0x1e)
            value = 0x1e;
        else if (value < -0x1e)
            value = -0x1e;
    }

    /// <summary>Moves a rotation rate one degree toward zero (misnamed in the reconstruction).</summary>
    /// <remarks>C: ClampVectorTo30 (0x41A0F0, geom.c).</remarks>
    public static void DecayTowardZero(ref short value)
    {
        if (value < 0)
            value = unchecked((short)(value + 1));
        else if (value > 0)
            value = unchecked((short)(value - 1));
    }

    /// <summary>The "percent cosine" of a 24.8 dot product: <c>(short)(((short)dot * 100) &gt;&gt; 8)</c>
    /// (arithmetic shift, rounds toward minus infinity), as stored in nFacingToTarget/nTargetFacing.</summary>
    /// <remarks>C: the inline expression of get_facing_range_from_point/object and facing_to_object (geom.c).</remarks>
    public static short FacingPercent(int dot) => unchecked((short)((short)dot * 100 >> 8));
}
