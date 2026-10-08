using WingCommander.Core.Numerics;

namespace WingCommander.Simulation.Geometry;

/// <summary>
/// Exact Kilrathi Saga sine/cosine of integer degrees. The original computes
/// <c>(long)(sin((double)degrees * DEGREES_TO_RADIANS) * 256.0)</c> directly from the signed
/// 16-bit argument, so <c>SinFixed(-30) == -127</c> while <c>SinFixed(330) == -128</c>: the
/// result is NOT periodic in the argument. Core's <see cref="FixedMath.Sin"/>/<see cref="FixedMath.Cos"/>
/// implement exactly this (fixed after the request in docs/progress/simulation.md); these wrappers
/// keep the original names for the simulation code.
/// </summary>
/// <remarks>C: SinFixed (0x434E00), CosFixed (0x434E30) in mathfp.c.</remarks>
public static class FixedTrig
{
    /// <remarks>C: SinFixed (0x434E00, mathfp.c).</remarks>
    public static int SinFixed(short degrees) => FixedMath.Sin(degrees);

    /// <remarks>C: CosFixed (0x434E30, mathfp.c).</remarks>
    public static int CosFixed(short degrees) => FixedMath.Cos(degrees);
}
