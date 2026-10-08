using WingCommander.Core.Numerics;

namespace WingCommander.Simulation.Data;

/// <summary>
/// Compact three-axis offset with 16-bit integer components (units, not fixed point): used by
/// the formation tables and the hardpoint (child) offsets. X = right, Y = up, Z = forward.
/// </summary>
/// <remarks>C: ShortVector (include/wcdata.h).</remarks>
public readonly record struct ShortVector(short X, short Y, short Z)
{
    /// <summary>Component-wise difference with 16-bit wrap.</summary>
    /// <remarks>C: sub_int_vector (0x40C4A0, brains.c).</remarks>
    public static ShortVector Subtract(ShortVector left, ShortVector right) =>
        new(unchecked((short)(left.X - right.X)), unchecked((short)(left.Y - right.Y)), unchecked((short)(left.Z - right.Z)));

    /// <summary>Integer units to 24.8 fixed point.</summary>
    /// <remarks>C: ConvertShortVectorToFixedVector (0x418980, geom.c).</remarks>
    public FixedVector ToFixed() => new(X * 0x100, Y * 0x100, Z * 0x100);

    /// <summary>24.8 fixed point to integer units (arithmetic shift, 16-bit truncation).</summary>
    /// <remarks>C: ConvertFixedVectorToShortVector (0x4189B0, geom.c); unreachable in the original.</remarks>
    public static ShortVector FromFixed(FixedVector v) =>
        new(unchecked((short)(v.X >> 8)), unchecked((short)(v.Y >> 8)), unchecked((short)(v.Z >> 8)));
}
