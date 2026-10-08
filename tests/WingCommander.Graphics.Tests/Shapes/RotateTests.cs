using WingCommander.Core.Resources;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Graphics.Tests.TestSupport;
using WingCommander.Tests;

namespace WingCommander.Graphics.Tests.Shapes;

public class RotateTests
{
    // 3x2 frame with the hot spot at its top-left pixel:  1 2 3 / 4 5 6
    private static readonly ShapeTable Small = TestShapes.Table(TestShapes.Frame(3, 2, 0, 0, [1, 2, 3, 4, 5, 6]));

    private static IndexedSurface Render(ShapeTable shape, int frame, int x, int y, int angleTenths, int scaleX, int scaleY,
        int width = 40, int height = 40, int originX = -20, int originY = -20)
    {
        var surface = new IndexedSurface(width, height, originX, originY);
        var scratch = new byte[RleRenderer.TransformScratchSize];
        RleRenderer.RotateRLEImage(RasterClip.ForSurface(surface), shape.GetPreparedFrame(frame), x, y, scratch,
            angleTenths, scaleX, scaleY);
        return surface;
    }

    private static int[,] Window(IndexedSurface s, int left, int top, int width, int height)
    {
        var w = new int[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                w[y, x] = s[left + x, top + y];
        return w;
    }

    [Fact]
    public void Quarter_cosine_table_and_trig_quadrants()
    {
        Assert.Equal(65536, TrigTables.QuarterCosine[0]);
        Assert.Equal(0, TrigTables.QuarterCosine[900]);
        Assert.Equal(901, TrigTables.QuarterCosine.Length);
        Assert.Equal((360, 360), (TrigTables.AbsoluteCosine.Length, TrigTables.AbsoluteSine.Length));
        (int, int) Trig(int a)
        {
            RleRenderer.GetRLETransformTrig(a, out int c, out int s);
            return (c, s);
        }
        Assert.Equal((65536, 0), Trig(0));
        Assert.Equal((0, 65536), Trig(900));
        Assert.Equal((-65536, 0), Trig(1800));
        Assert.Equal((0, -65536), Trig(2700));
        Assert.Equal((65536, 0), Trig(3600));
        Assert.Equal(Trig(2700), Trig(-900));
        Assert.Equal(Trig(450), Trig(450 + 7200));
        Assert.Equal((TrigTables.QuarterCosine[300], TrigTables.QuarterCosine[600]), Trig(300));
    }

    [Fact]
    public void Transform_point_floors_the_scale_and_rounds_each_rotation_product()
    {
        RleRenderer.TransformRLEPoint(10, 0, 0, 0, 900, 0x10000, 0x10000, out int x, out int y);
        Assert.Equal((0, 10), (x, y)); // +x rotates to +y: clockwise on screen
        RleRenderer.TransformRLEPoint(1, 0, 0, 0, 0, 0x8000, 0x8000, out x, out y);
        Assert.Equal((0, 0), (x, y)); // 0.5 floors to 0
        RleRenderer.TransformRLEPoint(-1, 0, 0, 0, 0, 0x8000, 0x8000, out x, out y);
        Assert.Equal((-1, 0), (x, y)); // -0.5 floors to -1 (not rounded)
        RleRenderer.TransformRLEPoint(7, 3, 5, 1, 0, 0x10000, 0x10000, out x, out y);
        Assert.Equal((7, 3), (x, y));
    }

    [Fact]
    public void Scale_two_at_zero_degrees_samples_centre_based()
    {
        // corner to corner spans 2*(W-1)+1 pixels; sx = floor(0.5 + i/2) -> column 0 once, others twice
        IndexedSurface s = Render(Small, 0, 0, 0, 0, 0x20000, 0x20000);
        Assert.Equal(new[,] { { 1, 2, 2, 3, 3 }, { 4, 5, 5, 6, 6 }, { 4, 5, 5, 6, 6 } }, Window(s, 0, 0, 5, 3));
        Assert.Equal(15, s.Pixels.Count(p => p != 0));
    }

    [Fact]
    public void Ninety_degrees_rotates_clockwise()
    {
        IndexedSurface s = Render(Small, 0, 0, 0, 900, 0x10000, 0x10000);
        Assert.Equal(new[,] { { 4, 1 }, { 5, 2 }, { 6, 3 } }, Window(s, -1, 0, 2, 3));
        Assert.Equal(6, s.Pixels.Count(p => p != 0));
    }

    [Fact]
    public void Negative_scale_mirrors_around_the_hot_spot()
    {
        IndexedSurface s = Render(Small, 0, 0, 0, 0, -0x10000, 0x10000);
        Assert.Equal(new[,] { { 3, 2, 1 }, { 6, 5, 4 } }, Window(s, -2, 0, 3, 2));
        Assert.Equal(6, s.Pixels.Count(p => p != 0));
    }

    [Fact]
    public void Frames_larger_than_64000_pixels_are_not_drawn()
    {
        var big = new byte[321 * 200];
        Array.Fill(big, (byte)1);
        ShapeTable shape = TestShapes.Table(TestShapes.Frame(321, 200, 0, 0, big));
        var surface = new IndexedSurface(10, 10);
        int result = RleRenderer.RotateRLEImage(RasterClip.ForSurface(surface), shape.GetPreparedFrame(0), 0, 0,
            new byte[RleRenderer.TransformScratchSize], 10, 0x10000, 0x10000);
        Assert.Equal(-4, result);
        Assert.All(surface.Pixels, p => Assert.Equal(0, p));
    }

    [DataFact]
    public void Mapper_at_zero_degrees_and_unit_scale_equals_the_unscaled_draw()
    {
        // angle 3600 has identity trig but bypasses the fast path, so the scan converter runs.
        GameDirectory data = GameData.Require();
        foreach (string file in new[] { "ARROW.VGA", "OBJECTS.VGA", "SHIPTYPE.V00", "COCKPIT.VGA" })
        {
            PacketFile packet = data.OpenPacket(file);
            for (int section = 0; section < packet.SectionCount; section++)
            {
                if (!ShapeTable.TryParse($"{file}[{section}]", packet.GetSection(section), out ShapeTable? shape))
                    continue;
                for (int f = 0; f < shape.FrameCount; f++)
                {
                    PreparedFrame prepared = shape.GetPreparedFrame(f);
                    if (prepared.Height < 2)
                        continue; // a one-row quad is degenerate for the mapper (draws nothing)
                    var fast = new IndexedSurface(320, 200);
                    var mapped = new IndexedSurface(320, 200);
                    RleRenderer.DrawRLEImage(RasterClip.ForSurface(fast), prepared, 160, 100);
                    RleRenderer.RotateRLEImage(RasterClip.ForSurface(mapped), prepared, 160, 100,
                        new byte[RleRenderer.TransformScratchSize], 3600, 0x10000, 0x10000);
                    Assert.True(fast.Pixels.AsSpan().SequenceEqual(mapped.Pixels), $"{file}[{section}] frame {f}");
                }
            }
        }
    }

    [DataFact]
    public void Left_top_and_right_clipping_never_changes_the_drawn_pixels()
    {
        GameDirectory data = GameData.Require();
        ShapeTable ship = ShapeTable.FromSection(data.OpenPacket("SHIPTYPE.V00"), 0);
        var random = new Random(77);
        var scratch = new byte[RleRenderer.TransformScratchSize];
        for (int i = 0; i < 300; i++)
        {
            int frame = random.Next(ship.FrameCount);
            int angle = random.Next(-3600, 7200);
            int scale = random.Next(0x4000, 0x30000);
            int x = random.Next(100, 220), y = random.Next(60, 140);
            PreparedFrame prepared = ship.GetPreparedFrame(frame);
            var full = new IndexedSurface(320, 200);
            var part = new IndexedSurface(320, 200);
            RleRenderer.RotateRLEImage(RasterClip.ForSurface(full), prepared, x, y, scratch, angle, scale, scale);
            int cl = x - random.Next(0, 40), ct = y - random.Next(0, 40), cr = x + random.Next(0, 40);
            RleRenderer.RotateRLEImage(new RasterClip(part, cl, ct, cr, 199), prepared, x, y, scratch, angle, scale, scale);
            for (int py = 0; py < 200; py++)
            {
                for (int px = 0; px < 320; px++)
                {
                    int expected = px >= cl && px <= cr && py >= ct ? full[px, py] : 0;
                    if (expected != part[px, py])
                        Assert.Fail($"frame {frame} angle {angle} scale {scale:x} at ({x},{y}) clip ({cl},{ct},{cr}): pixel ({px},{py})");
                }
            }
        }
    }

    [DataFact]
    public void Rotated_draw_stays_inside_the_transformed_bounds_estimate_in_the_first_quadrant()
    {
        // The original estimate combines |cos|, |sin| with the unrotated extents, so it is only a
        // true bound for 0..90 degrees; beyond that it assumes extents symmetric about the hot spot.
        GameDirectory data = GameData.Require();
        ShapeTable ship = ShapeTable.FromSection(data.OpenPacket("SHIPTYPE.V00"), 0);
        var gfx = new GraphicsContext();
        Span<short> bounds = stackalloc short[4];
        foreach (int angle in new[] { 0, 30, 60, 90 })
        {
            var surface = new IndexedSurface(320, 200);
            var vp = new Viewport(surface, 0, 0, 319, 199);
            gfx.DrawSpriteScaled(vp, 160, 100, ship, 0, angle, 0x100, 0);
            Assert.Equal(1, ShapeBounds.GetTransformedShapeBounds(vp, 160, 100, ship, 0, angle, 0x100, 0, bounds));
            for (int y = 0; y < 200; y++)
                for (int x = 0; x < 320; x++)
                    if (surface[x, y] != 0)
                        Assert.True(x >= bounds[0] - 1 && x <= bounds[2] + 1 && y >= bounds[1] - 1 && y <= bounds[3] + 1,
                            $"angle {angle}: ({x},{y}) outside {string.Join(",", bounds.ToArray())}");
        }
    }
}
