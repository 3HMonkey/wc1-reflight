using WingCommander.Simulation.Geometry;

namespace WingCommander.Simulation.Tests.Geometry;

public class FixedTrigTests
{
    // trunc(sin(d * DEGREES_TO_RADIANS) * 256) for d = -720, -690, ..., 720 (generated with the
    // Windows CRT; the multiples of 30 are the only arguments where the truncation is sensitive to
    // the last bit, so this locks the cross-platform behaviour).
    private static readonly int[] SinMultiplesOf30 =
    [
        0, 127, 221, 256, 221, 127, 0, -127, -221, -256, -221, -128, 0, 128, 221, 256, 221, 128, 0, -127,
        -221, -256, -221, -127, 0, 127, 221, 256, 221, 127, 0, -128, -221, -256, -221, -128, 0, 128, 221,
        256, 221, 127, 0, -127, -221, -256, -221, -127, 0,
    ];

    private static readonly int[] CosMultiplesOf30 =
    [
        256, 221, 127, 0, -127, -221, -256, -221, -127, 0, 127, 221, 256, 221, 128, 0, -128, -221, -256,
        -221, -127, 0, 128, 221, 256, 221, 128, 0, -127, -221, -256, -221, -128, 0, 128, 221, 256, 221,
        127, 0, -127, -221, -256, -221, -127, 0, 127, 221, 256,
    ];

    [Fact]
    public void Multiples_of_30_match_the_crt_golden_values()
    {
        for (int i = 0; i < SinMultiplesOf30.Length; i++)
        {
            short degrees = (short)(-720 + i * 30);
            Assert.Equal(SinMultiplesOf30[i], FixedTrig.SinFixed(degrees));
            Assert.Equal(CosMultiplesOf30[i], FixedTrig.CosFixed(degrees));
        }
    }

    [Theory]
    [InlineData(30, 127)]
    [InlineData(-30, -127)]
    [InlineData(330, -128)]
    [InlineData(390, 128)]
    [InlineData(90, 256)]
    [InlineData(-90, -256)]
    [InlineData(180, 0)]
    [InlineData(1, 4)]
    [InlineData(45, 181)]
    [InlineData(-45, -181)]
    [InlineData(179, 4)]
    [InlineData(32767, 31)]
    [InlineData(-32768, -35)]
    public void SinFixed_is_computed_from_the_signed_argument(short degrees, int expected) =>
        Assert.Equal(expected, FixedTrig.SinFixed(degrees));

    [Theory]
    [InlineData(0, 256)]
    [InlineData(60, 128)]
    [InlineData(-60, 128)]
    [InlineData(120, -127)]
    [InlineData(300, 128)]
    [InlineData(180, -256)]
    [InlineData(-180, -256)]
    [InlineData(91, -4)]
    [InlineData(-32768, 253)]
    public void CosFixed_is_computed_from_the_signed_argument(short degrees, int expected) =>
        Assert.Equal(expected, FixedTrig.CosFixed(degrees));
}
