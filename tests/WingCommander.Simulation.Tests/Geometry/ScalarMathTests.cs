using WingCommander.Simulation.Geometry;

namespace WingCommander.Simulation.Tests.Geometry;

public class ScalarMathTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(180, 180)]
    [InlineData(-180, -180)] // C remainder keeps the sign: -180 is not < -180, so it stays
    [InlineData(181, -179)]
    [InlineData(-181, 179)]
    [InlineData(359, -1)]
    [InlineData(360, 0)]
    [InlineData(540, 180)]
    [InlineData(-540, -180)]
    [InlineData(725, 5)]
    [InlineData(-32768, -8)]
    [InlineData(32767, 7)]
    public void WrapDegrees_matches_the_c_remainder_semantics(short degrees, short expected) =>
        Assert.Equal(expected, ScalarMath.WrapDegrees(degrees));

    [Theory]
    [InlineData(0, 500, 250, 8, 25, 16)]
    [InlineData(0, 500, -1, 8, 25, 8)]
    [InlineData(0, 500, 501, 8, 25, 25)]
    [InlineData(30, 74, 50, 29, 15, 23)] // (15-29)*(50-30)/44 = -6.36 -> -6, + 29
    [InlineData(-15, 15, 5, -150, 150, 50)]
    [InlineData(0, 20, 10, 4300, 3100, 3700)]
    public void FindRatio_is_a_clamped_truncating_remap(short inMin, short inMax, short input, short outMin, short outMax, short expected) =>
        Assert.Equal(expected, ScalarMath.FindRatio(inMin, inMax, input, outMin, outMax));

    [Fact]
    public void Facing_percent_uses_an_arithmetic_shift()
    {
        Assert.Equal(100, ScalarMath.FacingPercent(0x100));
        Assert.Equal(-71, ScalarMath.FacingPercent(-181));
        Assert.Equal(70, ScalarMath.FacingPercent(181));
    }

    [Fact]
    public void Rotation_rate_clamps_and_decays()
    {
        short v = 40;
        ScalarMath.ClampTo30(ref v);
        Assert.Equal(30, v);
        v = -45;
        ScalarMath.ClampTo30(ref v);
        Assert.Equal(-30, v);
        ScalarMath.DecayTowardZero(ref v);
        Assert.Equal(-29, v);
        v = 1;
        ScalarMath.DecayTowardZero(ref v);
        Assert.Equal(0, v);
        ScalarMath.DecayTowardZero(ref v);
        Assert.Equal(0, v);
    }
}
