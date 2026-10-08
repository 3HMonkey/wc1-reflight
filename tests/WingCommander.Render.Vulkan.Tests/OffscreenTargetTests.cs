using Vortice.Vulkan;
using WingCommander.Core.Rendering;
using WingCommander.Core.Video;

namespace WingCommander.Render.Vulkan.Tests;

/// <summary>Offscreen target recreation, zero-sized targets, other target formats and the Vulkan 1.2 path.</summary>
[Collection("Vulkan")]
public sealed class OffscreenTargetTests(OffscreenRendererFixture fixture) : VulkanTestBase(fixture)
{
    private static readonly Palette Colours = TestPatterns.DistinctPalette();
    private static readonly Framebuffer Pixels = TestPatterns.Pattern();

    private static int Expected(int x, int y) => TestPatterns.Rgb(Colours, TestPatterns.Index(x, y));

    private static void AssertPattern(CapturedImage image, AspectMode aspect, bool integerScaling = false, int tolerance = 0)
    {
        var rect = PresentationLayout.Compute(image.Width, image.Height, aspect, integerScaling);
        Assert.Equal(rect, TestPatterns.NonBlackBounds(image));
        Assert.Equal(0, TestPatterns.NonBlackOutside(image, rect));
        var (mismatches, first) = TestPatterns.CompareAtPixelCentres(image, rect, Expected, tolerance);
        Assert.True(mismatches == 0, $"{mismatches} mismatches; first: {first}");
    }

    [VulkanFact]
    public void Resize_RecreatesTheTarget_AndZeroSizeSkipsFrames()
    {
        var renderer = Fixture.Get(640, 480, ScalingFilter.Nearest, AspectMode.FourByThree);
        var frame = TestPatterns.Frame(Pixels, Colours);
        long recreations = renderer.Statistics.TargetRecreations;

        renderer.Render(frame);
        AssertPattern(renderer.CaptureLastFrame(), AspectMode.FourByThree);

        renderer.ResizeOffscreen(1600, 900);
        Assert.Throws<InvalidOperationException>(() => renderer.CaptureLastFrame()); // nothing rendered into the new target yet
        renderer.Render(frame);
        CapturedImage wide = renderer.CaptureLastFrame();
        Assert.Equal((1600, 900), (wide.Width, wide.Height));
        Assert.Equal(new PresentationRect(200, 0, 1200, 900), TestPatterns.NonBlackBounds(wide));
        AssertPattern(wide, AspectMode.FourByThree);

        long skipped = renderer.Statistics.FramesSkipped;
        long rendered = renderer.Statistics.FramesRendered;
        renderer.ResizeOffscreen(0, 0); // like a minimised window
        renderer.Render(frame);
        renderer.Render(frame);
        Assert.Equal(skipped + 2, renderer.Statistics.FramesSkipped);
        Assert.Equal(rendered, renderer.Statistics.FramesRendered);
        Assert.Equal((0, 0), renderer.TargetSize);

        renderer.ResizeOffscreen(320, 200);
        renderer.Render(frame);
        CapturedImage small = renderer.CaptureLastFrame();
        Assert.Equal(new PresentationRect(27, 0, 266, 200), TestPatterns.NonBlackBounds(small));
        Assert.Equal(recreations + 3, renderer.Statistics.TargetRecreations);
    }

    [VulkanFact]
    public void Resize_BetweenFramesInFlight_KeepsContentCorrect()
    {
        var renderer = Fixture.Get(640, 400, ScalingFilter.Nearest, AspectMode.SquarePixels);
        var frame = TestPatterns.Frame(Pixels, Colours);
        for (int i = 0; i < 20; i++)
        {
            renderer.ResizeOffscreen(640 + i * 16, 400 + i * 10);
            renderer.Render(frame);
            renderer.Render(frame);
        }
        CapturedImage image = renderer.CaptureLastFrame();
        Assert.Equal((944, 590), (image.Width, image.Height));
        AssertPattern(image, AspectMode.SquarePixels);
    }

    [VulkanTheory]
    [InlineData(VkFormat.B8G8R8A8Unorm, 0)]          // the usual Windows/Linux swapchain format (BGRA readback swizzle)
    [InlineData(VkFormat.A2B10G10R10UnormPack32, 0)] // 10-bit: 8-bit palette values survive the round trip
    [InlineData(VkFormat.R8G8B8A8Srgb, 1)]           // sRGB-only surfaces: shader decodes, hardware re-encodes
    [InlineData(VkFormat.B8G8R8A8Srgb, 1)]
    public void OtherTargetFormats_ReproduceThePalette(VkFormat format, int tolerance)
    {
        using var renderer = VulkanRenderer.CreateOffscreen(960, 720, new RendererSettings { Filter = ScalingFilter.Nearest },
            new VulkanRendererOptions { OffscreenFormat = format, Log = ValidationLog.Sink, UnlimitedValidationMessages = true });
        renderer.Render(TestPatterns.Frame(Pixels, Colours));
        CapturedImage image = renderer.CaptureLastFrame();

        var rect = PresentationLayout.Compute(960, 720, AspectMode.FourByThree, false);
        var (mismatches, first) = TestPatterns.CompareAtPixelCentres(image, rect, Expected, tolerance);
        Assert.True(mismatches == 0, $"{format}: {mismatches} mismatches; first: {first}");
    }

    [VulkanFact]
    public void Vulkan12Path_WithKhrExtensions_RendersTheSame()
    {
        var logged = new List<string>();
        using var renderer = VulkanRenderer.CreateOffscreen(960, 600, new RendererSettings { Filter = ScalingFilter.Nearest, Aspect = AspectMode.SquarePixels },
            new VulkanRendererOptions { MaxApiVersion = new Version(1, 2), Log = ValidationLog.Combine((_, message) => logged.Add(message)), UnlimitedValidationMessages = true });

        Assert.Equal(new Version(1, 2, 0), new Version(renderer.Capabilities.ApiVersion.Major, renderer.Capabilities.ApiVersion.Minor, 0));
        Assert.False(renderer.Capabilities.UsesVulkan13Core);
        Assert.Contains(logged, m => m.Contains("from KHR extensions", StringComparison.Ordinal));

        var frame = TestPatterns.Frame(Pixels, Colours);
        for (int i = 0; i < 5; i++)
            renderer.Render(frame);
        CapturedImage image = renderer.CaptureLastFrame();
        AssertPattern(image, AspectMode.SquarePixels);
    }
}
