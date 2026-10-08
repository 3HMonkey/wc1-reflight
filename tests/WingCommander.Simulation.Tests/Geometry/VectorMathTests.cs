using WingCommander.Core.Numerics;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation.Tests.Geometry;

public class VectorMathTests
{
    [Fact]
    public void Fixed_multiply_and_divide_follow_the_fpu_semantics_of_spec_1_2()
    {
        Assert.Equal(-1, FixedMath.Multiply(-3, 0x80));
        Assert.Equal(-2, (-3 * 0x80) >> 8);
        Assert.Equal(0x100, FixedMath.Multiply(0x100, 0x100));
        // Division by zero divides by 1.0.
        Assert.Equal(12345, FixedMath.Divide(12345, 0));
        // Truncation toward zero of negative quotients.
        Assert.Equal(-85, FixedMath.Divide(-0x100, 0x300));
        // float32 operands: int.MaxValue / 256 rounds up to 2^23, the result wraps like MSVC _ftol.
        Assert.Equal(int.MinValue, FixedMath.Divide(int.MaxValue, 0x100));
    }

    [Fact]
    public void Dot_and_cross_use_fixed_multiplies()
    {
        var x = new FixedVector(0x100, 0, 0);
        var y = new FixedVector(0, 0x100, 0);
        Assert.Equal(new FixedVector(0, 0, 0x100), VectorMath.Cross(x, y));
        Assert.Equal(new FixedVector(0, 0, -0x100), VectorMath.Cross(y, x));
        Assert.Equal(0, VectorMath.Dot(x, y));
        Assert.Equal(-1, VectorMath.Dot(new FixedVector(-3, 0, 0), new FixedVector(0x80, 0, 0)));
    }

    [Fact]
    public void Normalize_leaves_zero_vectors_untouched()
    {
        var zero = FixedVector.Zero;
        Assert.False(VectorMath.Normalize(ref zero));
        Assert.Equal(FixedVector.Zero, zero);

        var v = new FixedVector(221, 0, 127);
        Assert.True(VectorMath.Normalize(ref v));
        // |v| = trunc(sqrt(221^2 + 127^2)) = 254; 221/254 and 127/254 through float32.
        Assert.Equal(new FixedVector(222, 0, 128), v);
    }

    [Theory]
    [InlineData(0x100, 0, 0, 0x100, 0, 0, 100)]
    [InlineData(0x100, 0, 0, -0x100, 0, 0, -100)]
    [InlineData(0x100, 0, 0, 0, 0x100, 0, 0)]
    [InlineData(0x100, 0, 0, 0x100, 0x100, 0, 70)]
    [InlineData(-0x100, 0, 0, 0x100, 0x100, 0, -70)] // division truncates (>> 8 would give -71)
    public void VectorAngle_is_a_percent_cosine(int ax, int ay, int az, int bx, int by, int bz, short expected) =>
        Assert.Equal(expected, VectorMath.VectorAngle(new FixedVector(ax, ay, az), new FixedVector(bx, by, bz)));

    [Fact]
    public void ShrinkVector_halves_all_components_until_small()
    {
        var v = new FixedVector(0x10000, 0x100, -0x100);
        VectorMath.ShrinkVector(ref v);
        Assert.Equal(new FixedVector(0x800, 8, -8), v);

        var negative = new FixedVector(-0x10000, 0, 0);
        VectorMath.ShrinkVector(ref negative);
        Assert.Equal(new FixedVector(-0x800, 0, 0), negative);

        // Always halves at least once; C division truncates toward zero.
        var small = new FixedVector(3, -3, 0xf00);
        VectorMath.ShrinkVector(ref small);
        Assert.Equal(new FixedVector(1, -1, 0x780), small);

        // 0xf01 (just above 15.0) needs a second halving.
        var border = new FixedVector(0x1e02, 0, 0);
        VectorMath.ShrinkVector(ref border);
        Assert.Equal(new FixedVector(0x780, 0, 0), border);
    }

    [Fact]
    public void Shrink_reports_the_16_16_integer_part()
    {
        int value = 0x20000;
        Assert.True(VectorMath.Shrink(ref value));
        Assert.Equal(0x10000, value);
        value = 0x1e00;
        Assert.False(VectorMath.Shrink(ref value));
        Assert.Equal(0xf00, value);
        value = -0x1e00;
        Assert.False(VectorMath.Shrink(ref value));
        value = -0x1e04;
        Assert.True(VectorMath.Shrink(ref value));
    }

    [Fact]
    public void IsVectorWithinRange_compares_fixed_magnitudes()
    {
        Assert.True(VectorMath.IsVectorWithinRange(new FixedVector(100 << 8, 0, 0), 100));
        Assert.False(VectorMath.IsVectorWithinRange(new FixedVector((100 << 8) + 1, 0, 0), 100));
        Assert.True(VectorMath.IsVectorWithinRange(new FixedVector(30 << 8, 40 << 8, 0), -50));
    }

    [Fact]
    public void RectangularToSpherical_matches_the_original_formulas()
    {
        var s = default(SphericalVector);
        VectorMath.RectangularToSpherical(new FixedVector(0, 0, 0x100), ref s);
        Assert.Equal((0x100, (short)0, (short)0), (s.Radius, s.Yaw, s.Pitch));

        VectorMath.RectangularToSpherical(new FixedVector(0x100, 0, 0), ref s);
        Assert.Equal((short)90, s.Yaw);
        VectorMath.RectangularToSpherical(new FixedVector(-0x100, 0, 0), ref s);
        Assert.Equal((short)-90, s.Yaw);
        VectorMath.RectangularToSpherical(new FixedVector(0, 0x100, 0), ref s);
        Assert.Equal((short)-90, s.Pitch); // up is negative pitch
        VectorMath.RectangularToSpherical(new FixedVector(0, 0, -0x100), ref s);
        Assert.Equal((short)180, s.Yaw);

        // Zero vector: only the radius is written.
        s.Yaw = 77;
        VectorMath.RectangularToSpherical(FixedVector.Zero, ref s);
        Assert.Equal((0, (short)77), (s.Radius, s.Yaw));
    }

    [Fact]
    public void AlterYaw_30_from_identity_matches_hand_computation()
    {
        // c = CosFixed(30) = 221, s = SinFixed(30) = 127; i' = (221, 0, 127), k' = (-127, 0, 221);
        // fix_objects_ijk: up = (0, 253, 0) before normalising; |(221,0,127)| = 254 -> (222, 0, 128).
        var o = default(SpaceObject);
        o.InitIjk();
        o.AlterYaw(30);
        Assert.Equal(new FixedVector(222, 0, 128), o.Right);
        Assert.Equal(new FixedVector(0, 256, 0), o.Up);
        Assert.Equal(new FixedVector(-128, 0, 222), o.Forward);
    }

    [Fact]
    public void AlterYaw_minus_180_turns_around_exactly()
    {
        var o = default(SpaceObject);
        o.InitIjk();
        o.AlterYaw(-180);
        Assert.Equal(new FixedVector(-256, 0, 0), o.Right);
        Assert.Equal(new FixedVector(0, 256, 0), o.Up);
        Assert.Equal(new FixedVector(0, 0, -256), o.Forward);
    }

    [Fact]
    public void Rotations_keep_the_basis_near_orthonormal()
    {
        var o = default(SpaceObject);
        o.InitIjk();
        short[] angles = [7, -13, 29, 45, -90, 3, 1, -1, 155, -155, 61, 17];
        for (int step = 0; step < 600; step++)
        {
            short angle = angles[step % angles.Length];
            switch (step % 3)
            {
                case 0: o.AlterYaw(angle); break;
                case 1: o.AlterPitch(angle); break;
                default: o.AlterRoll(angle); break;
            }
            AssertNearUnit(o.Right);
            AssertNearUnit(o.Up);
            AssertNearUnit(o.Forward);
            Assert.InRange(VectorMath.Dot(o.Right, o.Up), -4, 4);
            Assert.InRange(VectorMath.Dot(o.Up, o.Forward), -4, 4);
            Assert.InRange(VectorMath.Dot(o.Forward, o.Right), -4, 4);
        }
    }

    [Fact]
    public void Rotation_sequences_are_deterministic()
    {
        var a = default(SpaceObject);
        var b = default(SpaceObject);
        a.InitIjk();
        b.InitIjk();
        for (int i = 0; i < 100; i++)
        {
            a.AlterRoll((short)(i * 7 - 300));
            b.AlterRoll((short)(i * 7 - 300));
            a.AlterPitch((short)(i - 50));
            b.AlterPitch((short)(i - 50));
        }
        Assert.Equal(a.Right, b.Right);
        Assert.Equal(a.Up, b.Up);
        Assert.Equal(a.Forward, b.Forward);
    }

    [Fact]
    public void PositionRelative_moves_along_a_normalised_direction()
    {
        var p = new FixedVector(0, 0, 0);
        VectorMath.PositionRelative(ref p, new FixedVector(0, 0, 0x300), 100);
        Assert.Equal(new FixedVector(0, 0, 100 << 8), p);
        VectorMath.PositionRelative(ref p, new FixedVector(0x100, 0, 0), 0);
        Assert.Equal(new FixedVector(0, 0, 100 << 8), p);
    }

    [Fact]
    public void Random_vector_helpers_consume_rand_in_component_order()
    {
        var expected = new CRandom(7);
        var random = new CRandom(7);
        var v = RandomVectors.MakeRandomVectorFixed(random, 0, 50);
        Assert.Equal(expected.SignedMagnitude(0, 50, true) * 0x100, v.X);
        Assert.Equal(expected.SignedMagnitude(0, 50, true) * 0x100, v.Y);
        Assert.Equal(expected.SignedMagnitude(0, 50, true) * 0x100, v.Z);
        Assert.Equal(expected.Seed, random.Seed);

        var n = RandomVectors.MakeRandomNormalizedVector(random);
        Assert.InRange(n.Magnitude(), 250, 257);
        Assert.True(n.X > 0 && n.Y > 0 && n.Z > 0);
    }

    private static void AssertNearUnit(FixedVector v) => Assert.InRange(v.Magnitude(), 252, 258);
}
