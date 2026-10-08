using WingCommander.Core.Rendering;
using WingCommander.Core.Video;

namespace WingCommander.Render.Vulkan.Tests;

/// <summary>Two frames in flight: per-slot staging and captures never mix frames; no allocations per frame.</summary>
[Collection("Vulkan")]
public sealed class FramesInFlightTests(OffscreenRendererFixture fixture) : VulkanTestBase(fixture)
{
    [VulkanFact]
    public void ManyFrames_EveryCaptureShowsItsOwnFrame()
    {
        // 640x400 square pixels = 2x. Every frame has new pixels and a new palette entry 0..255
        // rotation, the CPU runs up to two frames ahead and captures are collected late, so a
        // shared staging region or capture buffer would show the wrong frame's content.
        var renderer = Fixture.Get(640, 400, ScalingFilter.Nearest, AspectMode.SquarePixels);
        var palette = TestPatterns.DistinctPalette();
        var layer = new ClassicLayer(new Framebuffer(), palette);
        var frame = new RenderFrame(layer);
        var work = new Framebuffer();
        const int frames = 120;

        long firstFrame = renderer.Statistics.FramesRendered;
        var captures = new List<CapturedImage>();
        for (int f = 0; f < frames; f++)
        {
            TestPatterns.FillSeeded(work, f);
            layer.Present(work);
            palette.SetEntry(f & 255, (byte)f, 0, 0); // palette changes every frame too
            if (f % 3 != 1)
                renderer.RequestCapture();
            renderer.Render(frame);
            if (renderer.TakeCapture(waitForGpu: false) is { } early)
                captures.Add(early);
        }
        while (renderer.TakeCapture() is { } late)
            captures.Add(late);

        Assert.Equal(frames - frames / 3, captures.Count);
        var reference = TestPatterns.DistinctPalette();
        long previous = -1;
        foreach (var image in captures)
        {
            int f = (int)(image.FrameNumber - firstFrame);
            Assert.True(image.FrameNumber > previous, "captures come back in frame order");
            previous = image.FrameNumber;
            Assert.NotEqual(1, f % 3);
            for (int y = 0; y < Framebuffer.Height; y += 9)
            {
                for (int x = 0; x < Framebuffer.Width; x += 5)
                {
                    byte index = TestPatterns.SeededIndex(x, y, f);
                    // Entries 0..f were overwritten with (n, 0, 0) by frame f (n = entry index).
                    int expected = index <= f ? index << 16 : TestPatterns.Rgb(reference, index);
                    int actual = image.GetRgb(2 * x, 2 * y);
                    Assert.True(expected == actual, $"frame {f}, pixel ({x},{y}): expected {expected:x6}, got {actual:x6}");
                }
            }
        }
        Assert.Equal(frames, renderer.Statistics.FramesRendered - firstFrame);
    }

    [VulkanFact]
    public void SteadyStateFrames_DoNotAllocate()
    {
        var renderer = Fixture.Get(800, 600, ScalingFilter.SharpBilinear, AspectMode.FourByThree);
        var palette = TestPatterns.DistinctPalette();
        var layer = new ClassicLayer(new Framebuffer(), palette);
        var frame = new RenderFrame(layer);
        var work = TestPatterns.Pattern();
        for (int i = 0; i < 10; i++)
        {
            layer.Present(work);
            renderer.Render(frame);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            work[i, 0] = (byte)i;
            layer.Present(work);                       // pixel upload every frame
            palette.SetEntry(i & 255, (byte)i, 0, 0);  // palette upload every frame
            renderer.Render(frame);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        renderer.WaitIdle();

        Assert.Equal(0, allocated);
    }
}
