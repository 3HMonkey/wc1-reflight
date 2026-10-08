using WingCommander.Core.Numerics;

namespace WingCommander.Core.Tests.Numerics;

public class FixedMathTests
{
    [Theory]
    [InlineData(-30, -127)]   // computed from the signed argument ...
    [InlineData(330, -128)]   // ... so the result is not periodic (reference C behaviour)
    [InlineData(30, 127)]
    [InlineData(90, 256)]
    [InlineData(0, 0)]
    public void Sin_is_computed_directly_from_the_signed_degrees(short degrees, int expected) =>
        Assert.Equal(expected, FixedMath.Sin(degrees));

    [Fact]
    public void Multiply_truncates_toward_zero_like_the_fpu_path()
    {
        Assert.Equal(-1, FixedMath.Multiply(-3, 0x80));
        Assert.Equal(-2, (-3 * 0x80) >> 8);
    }

    [Fact]
    public void Divide_by_zero_divides_by_one()
    {
        Assert.Equal(0x1234, FixedMath.Divide(0x1234, 0));
    }

    [Theory]
    [InlineData(-180, -180)]
    [InlineData(181, -179)]
    [InlineData(-181, 179)]
    [InlineData(540, 180)]
    [InlineData(720, 0)]
    public void WrapDegrees_keeps_minus_180(short input, short expected) =>
        Assert.Equal(expected, FixedMath.WrapDegrees(input));

    [Fact]
    public void Random_matches_the_msvc_lcg()
    {
        var r = new CRandom(1);
        Assert.Equal([41, 18467, 6334, 26500, 19169], Enumerable.Range(0, 5).Select(_ => r.Next()));
    }
}
