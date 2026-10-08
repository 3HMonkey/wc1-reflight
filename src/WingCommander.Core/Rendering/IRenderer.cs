namespace WingCommander.Core.Rendering;

/// <summary>How the 320x200 image is magnified.</summary>
public enum ScalingFilter
{
    /// <summary>Nearest neighbour: crisp pixels, uneven at non-integer scales.</summary>
    Nearest,

    /// <summary>Integer nearest prescale followed by bilinear: crisp and even (default).</summary>
    SharpBilinear,

    /// <summary>Plain bilinear: soft.</summary>
    Linear,
}

/// <summary>Display aspect of the 320x200 image.</summary>
public enum AspectMode
{
    /// <summary>4:3 like the original monitors (pixels 1.2 times taller than wide). Default.</summary>
    FourByThree,

    /// <summary>Square pixels (16:10).</summary>
    SquarePixels,
}

/// <summary>User-adjustable presentation settings shared by all renderers.</summary>
public sealed class RendererSettings
{
    public ScalingFilter Filter { get; set; } = ScalingFilter.SharpBilinear;

    public AspectMode Aspect { get; set; } = AspectMode.FourByThree;

    /// <summary>Only scale by whole multiples (smaller picture, perfectly even pixels).</summary>
    public bool IntegerScaling { get; set; }

    public bool VSync { get; set; } = true;
}

/// <summary>
/// Draws and presents frames. Implementations: the Vulkan renderer (default) and the SDL_Renderer
/// fallback. Called only from the host's main loop thread.
/// </summary>
public interface IRenderer : IDisposable
{
    /// <summary>Short name for logs ("Vulkan (NVIDIA GeForce RTX 2060 SUPER)", "SDL software").</summary>
    string Name { get; }

    RendererSettings Settings { get; }

    /// <summary>Draws <paramref name="frame"/> and presents it (blocking on vsync when enabled).</summary>
    void Render(RenderFrame frame);

    /// <summary>The window's drawable size changed (resize, fullscreen toggle, DPI change).</summary>
    void SurfaceResized();
}
