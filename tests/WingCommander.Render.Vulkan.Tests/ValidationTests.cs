using WingCommander.Core.Rendering;
using WingCommander.Core.Video;

namespace WingCommander.Render.Vulkan.Tests;

/// <summary>A fact that only runs when the Khronos validation layer is installed (Vulkan SDK).</summary>
public sealed class ValidationFactAttribute : FactAttribute
{
    private static readonly Lazy<(bool Available, string Description)> Probe = new(() =>
    {
        bool available = VulkanRenderer.IsAvailable(out string description,
            new VulkanRendererOptions { Validation = ValidationMode.Required, Log = (_, _) => { } });
        return (available, description);
    });

    public ValidationFactAttribute()
    {
        if (!Probe.Value.Available)
            Skip = $"Vulkan validation layer not usable: {Probe.Value.Description}";
    }
}

/// <summary>
/// Runs the renderer's offscreen paths with VK_LAYER_KHRONOS_validation required and fails on
/// any validation error or warning. Set VK_KHRONOS_VALIDATION_VALIDATE_SYNC=true in the
/// environment to add synchronization validation.
/// </summary>
[Collection("Vulkan")]
public sealed class ValidationTests
{
    [ValidationFact]
    public void Offscreen_paths_produce_no_validation_messages_on_the_1_3_path() => Exercise(maxApiVersion: null);

    [ValidationFact]
    public void Offscreen_paths_produce_no_validation_messages_on_the_1_2_path() => Exercise(new Version(1, 2));

    private static void Exercise(Version? maxApiVersion)
    {
        var messages = new List<string>();
        void Log(VulkanLogLevel level, string message)
        {
            if (level >= VulkanLogLevel.Warning && message.StartsWith("validation:", StringComparison.Ordinal))
            {
                lock (messages)
                    messages.Add($"{level}: {message}");
            }
        }

        using (var renderer = VulkanRenderer.CreateOffscreen(640, 480, new RendererSettings(),
                   new VulkanRendererOptions { Validation = ValidationMode.Required, MaxApiVersion = maxApiVersion, Log = Log }))
        {
            Assert.True(renderer.Capabilities.ValidationEnabled);
            var pixels = TestPatterns.Pattern();
            var palette = TestPatterns.DistinctPalette();
            var layer = new ClassicLayer(new Framebuffer(), palette);
            var frame = new RenderFrame(layer);
            int seed = 0;
            foreach (var filter in Enum.GetValues<ScalingFilter>())
            {
                foreach (var aspect in Enum.GetValues<AspectMode>())
                {
                    renderer.Settings.Filter = filter;
                    renderer.Settings.Aspect = aspect;
                    renderer.Settings.IntegerScaling = seed % 2 == 1;
                    for (int i = 0; i < 6; i++, seed++)
                    {
                        if (i % 2 == 0)
                        {
                            TestPatterns.FillSeeded(pixels, seed);
                            layer.Present(pixels);
                        }
                        else
                        {
                            palette.SetEntry(seed & 255, (byte)seed, 40, 200);
                        }
                        renderer.Render(frame);
                    }
                }
            }

            renderer.RequestCapture();
            renderer.Render(frame);
            Assert.NotNull(renderer.TakeCapture());

            renderer.ResizeOffscreen(1600, 900);
            renderer.Render(frame);
            renderer.ResizeOffscreen(0, 0);
            renderer.Render(frame);
            renderer.ResizeOffscreen(320, 200);
            renderer.Render(frame);
            renderer.Render(new RenderFrame(new ClassicLayer(new Framebuffer(), new Palette())));
            Assert.Equal(320, renderer.CaptureLastFrame().Width);
            renderer.WaitIdle();

            Assert.True(renderer.Statistics.ValidationErrors == 0 && renderer.Statistics.ValidationWarnings == 0,
                string.Join(Environment.NewLine, messages));
        }
        Assert.True(messages.Count == 0, string.Join(Environment.NewLine, messages));
    }
}
