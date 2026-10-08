using WingCommander.Core.Rendering;
using WingCommander.Core.Video;

namespace WingCommander.Render.Vulkan.Tests;

/// <summary>Creation, capabilities, disposal and misuse.</summary>
[Collection("Vulkan")]
public sealed class LifecycleTests(OffscreenRendererFixture fixture) : VulkanTestBase(fixture)
{
    [VulkanFact]
    public void Capabilities_AreDetectedAndLogged()
    {
        var log = new List<(VulkanLogLevel Level, string Message)>();
        using var renderer = VulkanRenderer.CreateOffscreen(64, 64, null, new VulkanRendererOptions { Log = ValidationLog.Combine((level, message) => log.Add((level, message))), UnlimitedValidationMessages = true });
        VulkanCapabilities caps = renderer.Capabilities;

        Assert.False(string.IsNullOrWhiteSpace(caps.DeviceName));
        Assert.Equal($"Vulkan ({caps.DeviceName})", renderer.Name);
        Assert.True(caps.ApiVersion >= new Version(1, 2), $"API {caps.ApiVersion}");
        Assert.True(caps.DeviceApiVersion >= caps.ApiVersion);
        Assert.NotEmpty(caps.AvailableDevices);
        Assert.True(caps.MaxColorSamples >= 1);
        Assert.True(caps.MaxImageDimension2D >= 4096);
        Assert.Contains(log, entry => entry.Level == VulkanLogLevel.Info && entry.Message.Contains("roadmap:", StringComparison.Ordinal));
        // Roadmap features are only reported, never required: the renderer runs either way.
        Assert.Equal(caps.AccelerationStructure && caps.RayQuery && caps.BufferDeviceAddress && caps.DescriptorIndexing, caps.SupportsRayTracedEffects);
    }

    [VulkanFact]
    public void Dispose_IsIdempotent_AndLaterCallsThrow()
    {
        var renderer = VulkanRenderer.CreateOffscreen(64, 64, null, new VulkanRendererOptions { Log = ValidationLog.Sink, UnlimitedValidationMessages = true });
        var frame = TestPatterns.Frame(TestPatterns.Pattern(), TestPatterns.DistinctPalette());
        renderer.Render(frame);
        renderer.RequestCapture();
        renderer.Render(frame); // dispose with a pending capture and frames in flight

        renderer.Dispose();
        renderer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => renderer.Render(frame));
        Assert.Throws<ObjectDisposedException>(() => renderer.CaptureLastFrame());
        Assert.Throws<ObjectDisposedException>(() => renderer.RequestCapture());
        renderer.SurfaceResized(); // harmless after dispose
    }

    [VulkanFact]
    public void ManyRenderers_CanBeCreatedAndDestroyed()
    {
        for (int i = 0; i < 4; i++)
        {
            using var renderer = VulkanRenderer.CreateOffscreen(32 + i, 32, null, new VulkanRendererOptions { Log = ValidationLog.Sink, UnlimitedValidationMessages = true });
            renderer.Render(TestPatterns.Frame(TestPatterns.Pattern(), TestPatterns.DistinctPalette()));
            Assert.Equal(32 + i, renderer.CaptureLastFrame().Width);
        }
    }

    [VulkanFact]
    public void Misuse_IsReportedClearly()
    {
        var renderer = Fixture.Get(64, 64);
        Assert.Throws<ArgumentNullException>(() => renderer.Render(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.ResizeOffscreen(-1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.ResizeOffscreen(1 << 20, 10));
        Assert.Throws<ArgumentNullException>(() => VulkanRenderer.Create(null!));
        Assert.Throws<ArgumentException>(() => VulkanRenderer.CreateOffscreen(8, 8, null, new VulkanRendererOptions { MaxApiVersion = new Version(1, 1) }));
    }

    [VulkanFact]
    public void RequestedCapture_WithoutFrame_StaysPendingUntilTheNextRender()
    {
        var renderer = Fixture.Get(320, 200, ScalingFilter.Nearest, AspectMode.SquarePixels);
        renderer.RequestCapture();
        Assert.Null(renderer.TakeCapture());

        var palette = new Palette();
        palette.Fill(200, 100, 50);
        renderer.Render(TestPatterns.Frame(new Framebuffer(), palette));
        CapturedImage? image = renderer.TakeCapture();
        Assert.NotNull(image);
        Assert.Equal(0xC86432, image.GetRgb(160, 100));
        Assert.Null(renderer.TakeCapture());
    }

    [ValidationFact]
    public void SharedRenderer_RunsWithSynchronizationValidation_WhenTheLayerIsInstalled()
    {
        VulkanCapabilities caps = Fixture.Renderer.Capabilities;
        Assert.True(caps.ValidationEnabled);
        Assert.True(caps.DebugUtils);
        Assert.True(caps.SynchronizationValidation);
    }

    [ValidationFact]
    public void ValidationMessages_ReachTheTestLogAndTheStatistics()
    {
        using var renderer = VulkanRenderer.CreateOffscreen(16, 16, null,
            new VulkanRendererOptions { Validation = ValidationMode.Required, Log = ValidationLog.Sink, UnlimitedValidationMessages = true });
        Assert.True(renderer.SubmitDebugMessage(error: true, "injected error"));
        Assert.True(renderer.SubmitDebugMessage(error: false, "injected warning"));

        Assert.Equal(1, renderer.Statistics.ValidationErrors);
        Assert.Equal(1, renderer.Statistics.ValidationWarnings);
        string[] messages = ValidationLog.Drain(); // drained here, so the base class check passes
        Assert.Equal(2, messages.Length);
        Assert.Contains("injected error", messages[0], StringComparison.Ordinal);
        Assert.StartsWith("Error: validation:", messages[0], StringComparison.Ordinal);
        Assert.StartsWith("Warning: validation:", messages[1], StringComparison.Ordinal);
    }

    [VulkanFact]
    public void SynchronizationValidation_CanBeTurnedOff()
    {
        using var renderer = VulkanRenderer.CreateOffscreen(16, 16, null,
            new VulkanRendererOptions { SynchronizationValidation = false, Log = ValidationLog.Sink });
        Assert.False(renderer.Capabilities.SynchronizationValidation);
    }

    [VulkanFact]
    public void IsAvailable_ReportsTheDevice()
    {
        Assert.True(VulkanRenderer.IsAvailable(out string description));
        Assert.False(string.IsNullOrEmpty(description));
    }
}
