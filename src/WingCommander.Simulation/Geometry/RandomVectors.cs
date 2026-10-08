using WingCommander.Core.Numerics;

namespace WingCommander.Simulation.Geometry;

/// <summary>
/// Random vector helpers on top of the shared <see cref="CRandom"/>. Components are drawn in
/// x, y, z order, each with the exact call sequence of the original (one or two rand() calls
/// per component).
/// </summary>
public static class RandomVectors
{
    /// <summary><c>RandomBelowOrEqual(2 * range) - range</c>.</summary>
    /// <remarks>C: signed_random (0x4220F0, logic.c).</remarks>
    public static short SignedRandom(CRandom random, short range) => random.Signed(range);

    /// <summary><c>RandomInRange(lo, hi)</c>, negated when <paramref name="allowNegative"/> and a second
    /// <c>RandomInRange(0, 1)</c> is non-zero.</summary>
    /// <remarks>C: ChooseRandomSignedMagnitude (0x418750, geom.c).</remarks>
    public static short ChooseRandomSignedMagnitude(CRandom random, short minimum, short maximum, bool allowNegative) =>
        random.SignedMagnitude(minimum, maximum, allowNegative);

    /// <summary>Three signed magnitudes in [lo, hi] converted to 24.8.</summary>
    /// <remarks>C: MakeRandomVectorFixed (0x418780, geom.c).</remarks>
    public static FixedVector MakeRandomVectorFixed(CRandom random, short minimum, short maximum)
    {
        int x = random.SignedMagnitude(minimum, maximum, true) * 0x100;
        int y = random.SignedMagnitude(minimum, maximum, true) * 0x100;
        int z = random.SignedMagnitude(minimum, maximum, true) * 0x100;
        return new FixedVector(x, y, z);
    }

    /// <remarks>C: FillFixedVectorWithRandomComponents (0x4187E0, geom.c).</remarks>
    public static FixedVector FillFixedVectorWithRandomComponents(CRandom random, short limit) =>
        MakeRandomVectorFixed(random, 0, limit);

    /// <summary><paramref name="center"/> plus a random offset with components up to
    /// <c>RandomBelowOrEqual(radius)</c> units.</summary>
    /// <remarks>C: random_radial (0x418800, geom.c).</remarks>
    public static FixedVector RandomRadial(CRandom random, in FixedVector center, short radius)
    {
        var offset = FillFixedVectorWithRandomComponents(random, random.BelowOrEqual(radius));
        return VectorMath.Add(center, offset);
    }

    /// <summary>A normalised vector with components drawn from <c>RandomInRange(0x40, 0xff)</c> (all positive).</summary>
    /// <remarks>C: MakeRandomNormalizedVector (0x418840, geom.c).</remarks>
    public static FixedVector MakeRandomNormalizedVector(CRandom random)
    {
        var v = new FixedVector(
            unchecked((ushort)random.InRange(0x40, 0xff)),
            unchecked((ushort)random.InRange(0x40, 0xff)),
            unchecked((ushort)random.InRange(0x40, 0xff)));
        VectorMath.Normalize(ref v);
        return v;
    }
}
