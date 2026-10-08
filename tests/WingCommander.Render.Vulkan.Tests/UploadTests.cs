using WingCommander.Core.Rendering;
using WingCommander.Core.Video;

namespace WingCommander.Render.Vulkan.Tests;

/// <summary>Index image and palette are uploaded only when their versions change, and changes show up.</summary>
[Collection("Vulkan")]
public sealed class UploadTests(OffscreenRendererFixture fixture) : VulkanTestBase(fixture)
{
    // 960x600 square pixels = exact 3x: source pixel (x, y) covers output (3x..3x+2, 3y..3y+2).
    private static int At(CapturedImage image, int x, int y) => image.GetRgb(3 * x + 1, 3 * y + 1);

    private VulkanRenderer Renderer() => Fixture.Get(960, 600, ScalingFilter.Nearest, AspectMode.SquarePixels);

    [VulkanFact]
    public void UnchangedFrames_UploadNothing()
    {
        var renderer = Renderer();
        var palette = TestPatterns.DistinctPalette();
        var frame = TestPatterns.Frame(TestPatterns.Pattern(), palette);

        renderer.Render(frame);
        long pixels = renderer.Statistics.PixelUploads, colours = renderer.Statistics.PaletteUploads;
        for (int i = 0; i < 10; i++)
            renderer.Render(frame);

        Assert.Equal(pixels, renderer.Statistics.PixelUploads);
        Assert.Equal(colours, renderer.Statistics.PaletteUploads);
        Assert.Equal(TestPatterns.Rgb(palette, TestPatterns.Index(5, 7)), At(renderer.CaptureLastFrame(), 5, 7));
    }

    [VulkanFact]
    public void PaletteOnlyChange_UploadsOnlyThePalette()
    {
        var renderer = Renderer();
        var palette = TestPatterns.DistinctPalette();
        var frame = TestPatterns.Frame(TestPatterns.Pattern(), palette);
        renderer.Render(frame);
        long pixels = renderer.Statistics.PixelUploads, colours = renderer.Statistics.PaletteUploads;

        byte index = TestPatterns.Index(10, 20);
        palette.SetEntry(index, 1, 2, 3); // like a damage flash: one entry, no new pixels
        renderer.Render(frame);
        CapturedImage image = renderer.CaptureLastFrame();

        Assert.Equal(pixels, renderer.Statistics.PixelUploads);
        Assert.Equal(colours + 1, renderer.Statistics.PaletteUploads);
        Assert.Equal(0x010203, At(image, 10, 20));
        // Every pixel with that index changed, the others did not.
        for (int y = 0; y < Framebuffer.Height; y += 7)
        {
            for (int x = 0; x < Framebuffer.Width; x += 3)
                Assert.Equal(TestPatterns.Rgb(palette, TestPatterns.Index(x, y)), At(image, x, y));
        }
    }

    [VulkanFact]
    public void PixelOnlyChange_UploadsOnlyThePixels()
    {
        var renderer = Renderer();
        var palette = TestPatterns.DistinctPalette();
        var layer = new ClassicLayer(new Framebuffer(), palette);
        var frame = new RenderFrame(layer);
        var work = TestPatterns.Pattern();
        layer.Present(work);
        renderer.Render(frame);
        long pixels = renderer.Statistics.PixelUploads, colours = renderer.Statistics.PaletteUploads;

        work.Clear(42);
        work[319, 199] = 7;
        layer.Present(work); // the game's present: copy + PixelsVersion++
        renderer.Render(frame);
        CapturedImage image = renderer.CaptureLastFrame();

        Assert.Equal(pixels + 1, renderer.Statistics.PixelUploads);
        Assert.Equal(colours, renderer.Statistics.PaletteUploads);
        Assert.Equal(TestPatterns.Rgb(palette, 42), At(image, 0, 0));
        Assert.Equal(TestPatterns.Rgb(palette, 42), At(image, 160, 100));
        Assert.Equal(TestPatterns.Rgb(palette, 7), At(image, 319, 199));
    }

    [VulkanFact]
    public void InPlaceChange_WithoutVersionBump_IsNotUploaded_AndMarkPixelsChangedUploadsIt()
    {
        var renderer = Renderer();
        var palette = TestPatterns.DistinctPalette();
        var layer = new ClassicLayer(new Framebuffer(), palette);
        var frame = new RenderFrame(layer);
        layer.Pixels.Clear(1);
        layer.MarkPixelsChanged();
        renderer.Render(frame);

        layer.Pixels.Clear(2); // written in place but not announced: the renderer keeps the old upload
        renderer.Render(frame);
        Assert.Equal(TestPatterns.Rgb(palette, 1), At(renderer.CaptureLastFrame(), 50, 50));

        layer.MarkPixelsChanged();
        renderer.Render(frame);
        Assert.Equal(TestPatterns.Rgb(palette, 2), At(renderer.CaptureLastFrame(), 50, 50));
    }

    [VulkanFact]
    public void NewClassicLayer_UploadsBoth()
    {
        var renderer = Renderer();
        renderer.Render(TestPatterns.Frame(TestPatterns.Pattern(), TestPatterns.DistinctPalette()));
        long pixels = renderer.Statistics.PixelUploads, colours = renderer.Statistics.PaletteUploads;

        // A different layer whose versions happen to equal the old ones must still be uploaded.
        var palette = new Palette();
        palette.Fill(10, 20, 30);
        var other = TestPatterns.Frame(new Framebuffer(), palette);
        renderer.Render(other);

        Assert.Equal(pixels + 1, renderer.Statistics.PixelUploads);
        Assert.Equal(colours + 1, renderer.Statistics.PaletteUploads);
        Assert.Equal(0x0A141E, At(renderer.CaptureLastFrame(), 100, 100));
    }
}
