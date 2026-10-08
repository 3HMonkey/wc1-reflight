using WingCommander.Core.Rendering;

namespace WingCommander.Render.Vulkan.Tests;

/// <summary>
/// One offscreen renderer shared by the tests of the "Vulkan" collection (instance and device
/// creation is the slow part). Created lazily so machines without Vulkan skip instead of failing.
/// Validation (with synchronization validation) is on whenever the layer is installed; its
/// messages go to <see cref="ValidationLog"/>. Tests run sequentially within the collection and
/// reset the settings they change.
/// </summary>
public sealed class OffscreenRendererFixture : IDisposable
{
    private readonly Lazy<VulkanRenderer> _renderer = new(() =>
        VulkanRenderer.CreateOffscreen(640, 480, new RendererSettings(), new VulkanRendererOptions { Log = ValidationLog.Sink, UnlimitedValidationMessages = true }));

    public VulkanRenderer Renderer => _renderer.Value;

    /// <summary>Returns the shared renderer resized to <paramref name="width"/> x <paramref name="height"/> with the given settings.</summary>
    public VulkanRenderer Get(int width, int height, ScalingFilter filter = ScalingFilter.Nearest, AspectMode aspect = AspectMode.FourByThree, bool integerScaling = false)
    {
        var renderer = Renderer;
        renderer.ResizeOffscreen(width, height);
        renderer.Settings.Filter = filter;
        renderer.Settings.Aspect = aspect;
        renderer.Settings.IntegerScaling = integerScaling;
        renderer.Settings.VSync = true;
        while (renderer.TakeCapture() is not null)
        {
            // drop captures a previous test left behind
        }
        return renderer;
    }

    /// <summary>Waits for the GPU so late validation reports reach the log before a test ends.</summary>
    public void WaitIdleIfCreated()
    {
        if (_renderer.IsValueCreated)
            _renderer.Value.WaitIdle();
    }

    public void Dispose()
    {
        if (_renderer.IsValueCreated)
            _renderer.Value.Dispose();
    }
}

[CollectionDefinition("Vulkan")]
public sealed class VulkanCollection : ICollectionFixture<OffscreenRendererFixture>
{
}
