namespace WingCommander.Core.Numerics;

/// <summary>
/// The game's random number generator: the MSVC C runtime <c>rand()</c> LCG
/// (seed = seed * 214013 + 2531011; result = (seed &gt;&gt; 16) &amp; 0x7fff). The original
/// consumes it from simulation, rendering (dust, exhaust), cockpit and campaign code in a
/// fixed order, so the whole game shares one instance. Never replace with System.Random.
/// </summary>
/// <remarks>C: rand/srand plus the wrappers in mathfp.c (RandomBelow, RandomInRange, ...).</remarks>
public sealed class CRandom
{
    public const int RandMax = 0x7fff;

    private uint _seed;

    public CRandom(uint seed = 1)
    {
        _seed = seed;
    }

    public uint Seed
    {
        get => _seed;
        set => _seed = value;
    }

    /// <summary>C: srand.</summary>
    public void SetSeed(uint seed) => _seed = seed;

    /// <summary>C: rand(); 0..32767.</summary>
    public int Next()
    {
        _seed = unchecked(_seed * 214013u + 2531011u);
        return (int)((_seed >> 16) & 0x7fff);
    }

    /// <summary>C: RandomBelow(n) = rand() % n. <paramref name="n"/> must be positive.</summary>
    public short Below(int n) => unchecked((short)(Next() % n));

    /// <summary>
    /// C: RandomInRange(lo, hi): span = hi - lo, a zero span counts as 1, result lo + rand() % (span + 1).
    /// Note RandomInRange(0, 0) can return 1 (original quirk).
    /// </summary>
    public short InRange(int lo, int hi)
    {
        int span = unchecked((short)(hi - lo));
        if (span == 0)
            span = 1;
        return unchecked((short)(lo + Next() % (span + 1)));
    }

    /// <summary>C: RandomBelowOrEqual(n): 0 for n == 0 or -1, else rand() % (n + 1).</summary>
    public short BelowOrEqual(int n)
    {
        if (n == 0 || n == -1)
            return 0;
        return unchecked((short)(Next() % (n + 1)));
    }

    /// <summary>C: signed_random(r) = RandomBelowOrEqual(2r) - r.</summary>
    public short Signed(int r) => unchecked((short)(BelowOrEqual(unchecked((short)(2 * r))) - r));

    /// <summary>C: ChooseRandomSignedMagnitude(lo, hi, neg).</summary>
    public short SignedMagnitude(int lo, int hi, bool allowNegative)
    {
        short v = InRange(lo, hi);
        if (allowNegative && InRange(0, 1) != 0)
            v = unchecked((short)-v);
        return v;
    }
}
