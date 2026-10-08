using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Graphics.Tests.TestSupport;
using WingCommander.Tests;

namespace WingCommander.Graphics.Tests.Shapes;

public class ShapeRenderTests
{
    // ARROW.VGA frame 0 (10x14, hot spot (1,2)), decoded by hand from the raw bytes; -1 = transparent.
    private static readonly int[,] Arrow =
    {
        { 0x00, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        { 0x00, 0x00, -1, -1, -1, -1, -1, -1, -1, -1 },
        { 0x00, 0x98, 0x00, -1, -1, -1, -1, -1, -1, -1 },
        { 0x00, 0x98, 0x93, 0x00, -1, -1, -1, -1, -1, -1 },
        { 0x00, 0x97, 0x95, 0x92, 0x00, -1, -1, -1, -1, -1 },
        { 0x00, 0x98, 0x98, 0x95, 0x96, 0x00, -1, -1, -1, -1 },
        { 0x00, 0x98, 0x95, 0x93, 0x93, 0x94, 0x00, -1, -1, -1 },
        { 0x00, 0x98, 0x98, 0x96, 0x96, 0x95, 0x92, 0x00, -1, -1 },
        { 0x00, 0x97, 0x98, 0x97, 0x97, 0x96, 0x95, 0x92, 0x00, -1 },
        { 0x00, 0x98, 0x95, 0x94, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
        { 0x00, 0x98, 0x93, 0x00, -1, -1, -1, -1, -1, -1 },
        { 0x00, 0x97, 0x00, -1, -1, -1, -1, -1, -1, -1 },
        { 0x00, 0x00, -1, -1, -1, -1, -1, -1, -1, -1 },
        { 0x00, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
    };

    private static ShapeTable ArrowShape() => ShapeTable.FromSection(GameData.Require().OpenPacket("ARROW.VGA"), 0);

    [DataFact]
    public void Arrow_frame_0_draws_pixel_exact_at_its_hot_spot()
    {
        var gfx = new GraphicsContext();
        var surface = new IndexedSurface(200, 100);
        surface.Clear(0xEE);
        var vp = new Viewport(surface, 0, 0, 199, 99);
        gfx.DrawSpriteDefault(vp, 100, 50, ArrowShape(), 0);
        for (int r = 0; r < 14; r++)
        {
            for (int c = 0; c < 10; c++)
            {
                int expected = Arrow[r, c] < 0 ? 0xEE : Arrow[r, c];
                Assert.Equal(expected, surface[100 - 1 + c, 50 - 2 + r]);
            }
        }
        Assert.Equal(10 * 14 - Arrow.Cast<int>().Count(v => v < 0), surface.Pixels.Count(p => p != 0xEE));
    }

    [DataFact]
    public void Arrow_frame_0_prepares_into_the_original_op_stream()
    {
        PreparedFrame prepared = ArrowShape().GetPreparedFrame(0);
        Assert.Equal((10, 14, -1, -2, 8, 11), (prepared.Width, prepared.Height, prepared.Left, prepared.Top, prepared.Right, prepared.Bottom));
        // row 0: one literal pixel 0x00, skip 9, end; row 9: literal of all 10 pixels, end
        Assert.Equal(new byte[] { 0x03, 0x00, 0x01, 0x09, 0x00 }, prepared.Ops.Slice(prepared.RowStarts[0], 5).ToArray());
        Assert.Equal(new byte[] { 0x15, 0x00, 0x98, 0x95, 0x94, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            prepared.Ops.Slice(prepared.RowStarts[9], 12).ToArray());
    }

    [DataFact]
    public void Arrow_storage_size_counts_every_span_pixel()
    {
        int opaque = Arrow.Cast<int>().Count(v => v >= 0);
        Assert.Equal(opaque, SpriteBackground.MeasureShapeFrameStorage(ArrowShape(), 0));
    }

    [Fact]
    public void Encoder_splits_long_runs_like_the_original()
    {
        // 300 opaque pixels -> literal runs of 127, 127, 46; 300 transparent -> skips of 255, 45
        var opaque = Enumerable.Repeat((byte)7, 300).ToArray();
        PreparedFrame a = PreparedFrame.Encode(opaque, 300, 1, 0, 0);
        Assert.Equal(new byte[] { 0xFF, 0x7F, 0x5D }, new[] { a.Ops[0], (byte)(a.Ops[0] >> 1), a.Ops[1 + 127 + 1 + 127] });
        Assert.Equal(0, a.Ops[^1]);
        var clear = Enumerable.Repeat((byte)0xFF, 300).ToArray();
        PreparedFrame b = PreparedFrame.Encode(clear, 300, 1, 0, 0);
        Assert.Equal(new byte[] { 0x01, 0xFF, 0x01, 45, 0x00 }, b.Ops.ToArray());
    }

    [Fact]
    public void Raw_decoder_supports_fill_and_literal_sub_runs()
    {
        byte[] pixels = [5, 5, 5, 6, 0xFF, 7, 8, 8, 8, 8, 9, 0xFF];
        ShapeTable plain = TestShapes.Table(TestShapes.Frame(6, 2, 2, 1, pixels));
        ShapeTable subRuns = TestShapes.Table(TestShapes.Frame(6, 2, 2, 1, pixels, useSubRuns: true));
        foreach (ShapeTable shape in new[] { plain, subRuns })
        {
            var bitmap = new byte[12];
            Array.Fill(bitmap, (byte)0xFF);
            ShapeFrameDecoder.DecodeShapeFrame(shape, 0, bitmap, 6, 2, 2, 1);
            Assert.Equal(pixels, bitmap);
        }
    }

    [Fact]
    public void Clipped_draw_writes_only_inside_the_viewport()
    {
        byte[] pixels = Enumerable.Range(1, 25).Select(i => (byte)i).ToArray();
        ShapeTable shape = TestShapes.Table(TestShapes.Frame(5, 5, 2, 2, pixels));
        var gfx = new GraphicsContext();
        var surface = new IndexedSurface(20, 20);
        var vp = new Viewport(surface, 5, 5, 14, 14);
        gfx.DrawSpriteDefault(vp, 5, 6, shape, 0); // covers x 3..7, y 4..8
        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                bool inside = x >= 5 && x <= 7 && y >= 5 && y <= 8;
                int expected = inside ? pixels[(y - 4) * 5 + (x - 3)] : 0;
                Assert.Equal(expected, surface[x, y]);
            }
        }
        var clip = vp.GetClip();
        Assert.Equal(-3, RleRenderer.DrawRLEImage(clip, shape.GetPreparedFrame(0), 30, 30));
        Assert.Equal(0, RleRenderer.DrawRLEImage(clip, shape.GetPreparedFrame(0), 10, 10));
    }

    [Fact]
    public void Pixel_value_0xFF_inside_a_span_becomes_transparent_when_prepared()
    {
        byte[] pixels = [1, 0xFF, 2];
        // encode the 0xFF as part of one literal span (the raw format allows it)
        byte[] frame = TestShapes.Frame(3, 1, 0, 0, [1, 3, 2]);
        frame[8 + 6 + 1] = 0xFF; // patch the middle literal byte
        ShapeTable shape = TestShapes.Table(frame);
        var surface = new IndexedSurface(3, 1);
        surface.Clear(9);
        new GraphicsContext().DrawSpriteDefault(new Viewport(surface, 0, 0, 2, 0), 0, 0, shape, 0);
        Assert.Equal(new byte[] { 1, 9, 2 }, surface.Pixels);
        _ = pixels;
    }

    [Fact]
    public void Solid_colour_sprite_recolours_every_opaque_pixel()
    {
        byte[] pixels = [1, 2, 0xFF, 3];
        ShapeTable shape = TestShapes.Table(TestShapes.Frame(2, 2, 0, 0, pixels));
        var gfx = new GraphicsContext();
        var surface = new IndexedSurface(2, 2);
        gfx.DrawSolidColourSprite(new Viewport(surface, 0, 0, 1, 1), 0, 0, shape, 0, 0x55);
        Assert.Equal(new byte[] { 0x55, 0x55, 0, 0x55 }, surface.Pixels);
        Assert.Equal(0x55, gfx.RasterPaletteTranslation[254]);
        Assert.Equal(0xFF, gfx.RasterPaletteTranslation[255]);
    }

    [Fact]
    public void Invalid_flip_is_fatal_and_invalid_frames_draw_nothing()
    {
        ShapeTable shape = TestShapes.Table(TestShapes.Frame(1, 1, 0, 0, [4]));
        var gfx = new GraphicsContext();
        var surface = new IndexedSurface(4, 4);
        var vp = new Viewport(surface, 0, 0, 3, 3);
        Assert.Throws<InvalidOperationException>(() => gfx.DrawSpriteTransformed(vp, 1, 1, shape, 0, 0, 256, 256, 0x40, 0));
        gfx.DrawSpriteDefault(vp, 1, 1, shape, 1);
        gfx.DrawSpriteDefault(vp, 1, 1, shape, -1);
        gfx.DrawSpriteDefault(vp, 1, 1, null, 0);
        Assert.All(surface.Pixels, p => Assert.Equal(0, p));
    }

    [DataFact]
    public void Capture_draw_restore_leaves_the_screen_unchanged()
    {
        var gfx = new GraphicsContext();
        var surface = new IndexedSurface(40, 40);
        new Random(5).NextBytes(surface.Pixels);
        byte[] before = surface.Pixels.ToArray();
        var vp = new Viewport(surface, 0, 0, 39, 39);
        var save = new byte[0x1000];
        ShapeTable arrow = ArrowShape();
        int stored = gfx.CaptureSpriteBackground(vp, save, 20, 20, arrow, 0);
        Assert.Equal(SpriteBackground.MeasureShapeFrameStorage(arrow, 0), stored);
        gfx.DrawSpriteDefault(vp, 20, 20, arrow, 0);
        Assert.NotEqual(before, surface.Pixels);
        gfx.RestoreSpriteBackground(vp, save, 20, 20, arrow, 0);
        Assert.Equal(before, surface.Pixels);

        // partially outside the viewport: only the clipped part is saved
        int clipped = gfx.CaptureSpriteBackground(vp, save, 36, 34, arrow, 0);
        Assert.True(clipped < stored && clipped > 0);
        gfx.DrawSpriteDefault(vp, 36, 34, arrow, 0);
        gfx.RestoreSpriteBackground(vp, save, 36, 34, arrow, 0);
        Assert.Equal(before, surface.Pixels);
    }

    [DataFact]
    public void Frame_bounds_and_transformed_bounds_follow_the_original_formulas()
    {
        ShapeTable arrow = ArrowShape();
        Span<short> bounds = stackalloc short[4];
        Assert.Equal(-1, ShapeBounds.GetShapeFrameBounds(bounds, 100, 50, arrow, 0));
        Assert.Equal(new short[] { 99, 48, 108, 61 }, bounds.ToArray());
        Assert.Equal(0, ShapeBounds.GetShapeFrameBounds(bounds, 0, 0, arrow, 3));
        Assert.Equal(108, ShapeBounds.GetShapeFrameExtent(100, 50, arrow, 0, 2));

        // angle 0, scale 1.0: |cos| = 255/256, |sin| = 0 -> 1 (hand computed from 0x442050)
        var vp = new Viewport(null, 0, 0, 319, 199);
        Assert.Equal(1, ShapeBounds.GetTransformedShapeBounds(vp, 100, 50, arrow, 0, 0, 256, 0, bounds));
        Assert.Equal(new short[] { 99, 49, 109, 62 }, bounds.ToArray());
        Assert.Equal(0, ShapeBounds.GetTransformedShapeBounds(vp, 400, 50, arrow, 0, 0, 256, 0, bounds));
        Assert.Equal(1, ShapeBounds.GetTransformedShapeBounds(vp, 5, 5, null, 0, 0, 256, 0, bounds));
        Assert.Equal(0, ShapeBounds.GetTransformedShapeBounds(vp, -5, 5, null, 0, 0, 256, 0, bounds));
    }

    [DataFact]
    public void Shape_font_captions_are_centred()
    {
        var title = GameData.Require().OpenPacket("TITLE.VGA");
        ShapeTable font = ShapeTable.FromSection(title, 1);
        Assert.Equal(60, font.FrameCount);
        for (int f = 26; f < 32; f++)
            Assert.Equal(new ShapeExtents(0, 0, 0, 0), font.GetExtents(f));
        short width = GraphicsContext.GetLineLength(font, "AB C"u8);
        int expected = font.GetExtents(0).Right + 2 + font.GetExtents(1).Right + 2 + 6 + font.GetExtents(2).Right + 2;
        Assert.Equal(expected, width);
        var gfx = new GraphicsContext();
        var surface = new IndexedSurface(320, 128);
        surface.Clear(0xFF);
        var vp = new Viewport(surface, 0, 0, 319, 127);
        gfx.PrintSubtitle(vp, font, "AB C"u8);
        int left = Enumerable.Range(0, 320).First(x => Enumerable.Range(0, 128).Any(y => surface[x, y] != 0xFF));
        Assert.Equal((320 - width) >> 1, left + font.GetExtents(0).Left);
    }
}
