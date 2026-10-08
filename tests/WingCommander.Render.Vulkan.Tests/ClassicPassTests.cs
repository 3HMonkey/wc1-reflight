using WingCommander.Core.Rendering;
using WingCommander.Core.Video;

namespace WingCommander.Render.Vulkan.Tests;

/// <summary>Palette lookup, filters, letterbox and aspect handling of the R1 classic pass (offscreen).</summary>
[Collection("Vulkan")]
public sealed class ClassicPassTests(OffscreenRendererFixture fixture) : VulkanTestBase(fixture)
{
    private static readonly Palette Colours = TestPatterns.DistinctPalette();
    private static readonly Framebuffer Pixels = TestPatterns.Pattern();

    private static int Expected(int x, int y) => TestPatterns.Rgb(Colours, TestPatterns.Index(x, y));

    private CapturedImage RenderPattern(VulkanRenderer renderer)
    {
        renderer.Render(TestPatterns.Frame(Pixels, Colours));
        return renderer.CaptureLastFrame();
    }

    [VulkanFact]
    public void Nearest_FourByThree_ShowsEverySourcePixelExactly()
    {
        var renderer = Fixture.Get(1280, 960, ScalingFilter.Nearest, AspectMode.FourByThree);
        CapturedImage image = RenderPattern(renderer);

        Assert.Equal(1280, image.Width);
        Assert.Equal(960, image.Height);
        var rect = PresentationLayout.Compute(1280, 960, AspectMode.FourByThree, false);
        Assert.Equal(new PresentationRect(0, 0, 1280, 960), rect);
        var (mismatches, first) = TestPatterns.CompareAtPixelCentres(image, rect, Expected);
        Assert.True(mismatches == 0, $"{mismatches} mismatches; first: {first}");
        Assert.Equal(0, TestPatterns.NonPaletteColours(image, rect, Colours));
    }

    [VulkanFact]
    public void Nearest_SquarePixels_ShowsEverySourcePixelExactly()
    {
        var renderer = Fixture.Get(960, 600, ScalingFilter.Nearest, AspectMode.SquarePixels);
        CapturedImage image = RenderPattern(renderer);

        var rect = PresentationLayout.Compute(960, 600, AspectMode.SquarePixels, false);
        Assert.Equal(new PresentationRect(0, 0, 960, 600), rect);
        var (mismatches, first) = TestPatterns.CompareAtPixelCentres(image, rect, Expected);
        Assert.True(mismatches == 0, $"{mismatches} mismatches; first: {first}");
        // At 3x every output pixel belongs to exactly one source pixel.
        for (int y = 0; y < 600; y++)
        {
            for (int x = 0; x < 960; x++)
                Assert.Equal(Expected(x / 3, y / 3), image.GetRgb(x, y));
        }
    }

    [VulkanFact]
    public void Letterbox_PillarboxBarsAreOpaqueBlack()
    {
        var renderer = Fixture.Get(1000, 600, ScalingFilter.Nearest, AspectMode.FourByThree);
        CapturedImage image = RenderPattern(renderer);

        var rect = PresentationLayout.Compute(1000, 600, AspectMode.FourByThree, false);
        Assert.Equal(new PresentationRect(100, 0, 800, 600), rect);
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)255), image.GetPixel(0, 300));
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)255), image.GetPixel(99, 0));
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)255), image.GetPixel(900, 599));
        Assert.Equal(0, TestPatterns.NonBlackOutside(image, rect));
        Assert.Equal(rect, TestPatterns.NonBlackBounds(image));
        Assert.Equal(Expected(0, 0), image.GetRgb(100, 0));
        Assert.Equal(Expected(319, 199), image.GetRgb(899, 599));
        var (mismatches, first) = TestPatterns.CompareAtPixelCentres(image, rect, Expected);
        Assert.True(mismatches == 0, $"{mismatches} mismatches; first: {first}");
    }

    [VulkanTheory]
    [InlineData(1280, 800, AspectMode.FourByThree, false)]  // pillarbox 1066 wide
    [InlineData(1280, 800, AspectMode.SquarePixels, false)] // exact fit
    [InlineData(1280, 1024, AspectMode.SquarePixels, false)] // letterbox top/bottom
    [InlineData(1280, 1024, AspectMode.FourByThree, false)]
    [InlineData(1000, 700, AspectMode.FourByThree, true)]   // 2x -> 640x480
    [InlineData(1000, 700, AspectMode.SquarePixels, true)]  // 3x -> 960x600
    [InlineData(2560, 1440, AspectMode.FourByThree, true)]  // 6x -> 1920x1440
    [InlineData(733, 517, AspectMode.FourByThree, false)]   // odd sizes
    [InlineData(300, 200, AspectMode.FourByThree, true)]    // smaller than one integer step: falls back to fit
    public void Letterbox_DrawnAreaIsExactlyPresentationLayout(int width, int height, AspectMode aspect, bool integerScaling)
    {
        var renderer = Fixture.Get(width, height, ScalingFilter.Nearest, aspect, integerScaling);
        CapturedImage image = RenderPattern(renderer);

        var rect = PresentationLayout.Compute(width, height, aspect, integerScaling);
        Assert.Equal(rect, TestPatterns.NonBlackBounds(image));
        Assert.Equal(0, TestPatterns.NonBlackOutside(image, rect));
        if (rect.Width >= Framebuffer.Width && rect.Height >= Framebuffer.Height)
        {
            var (mismatches, first) = TestPatterns.CompareAtPixelCentres(image, rect, Expected);
            Assert.True(mismatches == 0, $"{mismatches} mismatches; first: {first}");
        }
    }

    [VulkanFact]
    public void AspectModes_ChangeTheRectangleOnTheSameTarget()
    {
        var renderer = Fixture.Get(1280, 800, ScalingFilter.Nearest, AspectMode.FourByThree);
        CapturedImage fourByThree = RenderPattern(renderer);
        renderer.Settings.Aspect = AspectMode.SquarePixels;
        CapturedImage square = RenderPattern(renderer);

        Assert.Equal(new PresentationRect(107, 0, 1066, 800), TestPatterns.NonBlackBounds(fourByThree));
        Assert.Equal(new PresentationRect(0, 0, 1280, 800), TestPatterns.NonBlackBounds(square));
        // 4:3 stretches 200 rows to 800 (4x), square pixels too, but horizontally 3.33x vs 4x.
        Assert.Equal(Expected(160, 100), fourByThree.GetRgb(107 + (int)(160.5 * 1066 / 320), (int)(100.5 * 4)));
        Assert.Equal(Expected(160, 100), square.GetRgb(160 * 4 + 2, 100 * 4 + 2));
    }

    [VulkanFact]
    public void IntegerScaling_UsesWholeMultiples()
    {
        var renderer = Fixture.Get(1000, 700, ScalingFilter.Nearest, AspectMode.SquarePixels, integerScaling: true);
        CapturedImage image = RenderPattern(renderer);

        var rect = new PresentationRect(20, 50, 960, 600);
        Assert.Equal(rect, PresentationLayout.Compute(1000, 700, AspectMode.SquarePixels, true));
        Assert.Equal(rect, TestPatterns.NonBlackBounds(image));
        for (int y = 0; y < 600; y++)
        {
            for (int x = 0; x < 960; x++)
                Assert.Equal(Expected(x / 3, y / 3), image.GetRgb(20 + x, 50 + y));
        }
    }

    [VulkanFact]
    public void SharpBilinear_AtIntegerScale_IsIdenticalToNearest()
    {
        var renderer = Fixture.Get(1280, 800, ScalingFilter.Nearest, AspectMode.SquarePixels);
        CapturedImage nearest = RenderPattern(renderer);
        renderer.Settings.Filter = ScalingFilter.SharpBilinear;
        CapturedImage sharp = RenderPattern(renderer);

        Assert.Equal(nearest.Rgba, sharp.Rgba);
    }

    [VulkanTheory]
    [InlineData(ScalingFilter.SharpBilinear)]
    [InlineData(ScalingFilter.Linear)]
    public void SmoothFilters_AreExactAtTexelCentres(ScalingFilter filter)
    {
        // At 3x the centre of output pixel 3x+1 is exactly the centre of source pixel x.
        var renderer = Fixture.Get(960, 600, filter, AspectMode.SquarePixels);
        CapturedImage image = RenderPattern(renderer);

        int mismatches = 0;
        for (int y = 0; y < Framebuffer.Height; y++)
        {
            for (int x = 0; x < Framebuffer.Width; x++)
            {
                if (!TestPatterns.Close(image.GetRgb(3 * x + 1, 3 * y + 1), Expected(x, y), tolerance: 1))
                    mismatches++;
            }
        }
        Assert.Equal(0, mismatches);
    }

    [VulkanFact]
    public void Filters_AtNonIntegerScale_SharpBilinearBlendsOnlyAtPixelEdges()
    {
        // 1000x625 = 3.125x. Nearest: uneven but unblended. Linear: every pixel is a blend.
        // Sharp bilinear (prescale 3): flat inside each source pixel, blends only a band of
        // 1/3 source pixel (about one output pixel) at each edge.
        var renderer = Fixture.Get(1000, 625, ScalingFilter.Nearest, AspectMode.SquarePixels);
        var rect = new PresentationRect(0, 0, 1000, 625);
        CapturedImage nearest = RenderPattern(renderer);
        renderer.Settings.Filter = ScalingFilter.SharpBilinear;
        CapturedImage sharp = RenderPattern(renderer);
        renderer.Settings.Filter = ScalingFilter.Linear;
        CapturedImage linear = RenderPattern(renderer);

        int total = rect.Width * rect.Height;
        int nearestBlended = TestPatterns.NonPaletteColours(nearest, rect, Colours);
        int sharpBlended = TestPatterns.NonPaletteColours(sharp, rect, Colours);
        int linearBlended = TestPatterns.NonPaletteColours(linear, rect, Colours);
        Assert.Equal(0, nearestBlended);
        Assert.True(linearBlended > total * 9 / 10, $"linear blended only {linearBlended} of {total} pixels");
        Assert.True(sharpBlended > 0 && sharpBlended < total * 65 / 100, $"sharp bilinear blended {sharpBlended} of {total} pixels");

        const double scale = 3.125, range = 0.5 - 0.5 / 3;
        int innerChecked = 0;
        for (int py = 0; py < rect.Height; py++)
        {
            double sy = (py + 0.5) / scale;
            double oy = sy - Math.Floor(sy) - 0.5;
            if (Math.Abs(oy) > range - 0.01)
                continue;
            for (int px = 0; px < rect.Width; px++)
            {
                double sx = (px + 0.5) / scale;
                double ox = sx - Math.Floor(sx) - 0.5;
                if (Math.Abs(ox) > range - 0.01)
                    continue;
                Assert.Equal(Expected((int)sx, (int)sy), sharp.GetRgb(px, py));
                innerChecked++;
            }
        }
        Assert.True(innerChecked > total / 3, $"only {innerChecked} inner pixels checked");
    }
}
