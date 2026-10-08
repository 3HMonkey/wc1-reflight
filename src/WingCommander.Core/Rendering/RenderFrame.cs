using WingCommander.Core.Video;

namespace WingCommander.Core.Rendering;

/// <summary>
/// The classic 320x200 indexed picture as last presented by the game (the front buffer), with
/// the live palette. The game writes pixels into its own working buffer and copies them here
/// when it presents; palette writes are visible immediately, like on the VGA.
/// </summary>
public sealed class ClassicLayer
{
    public ClassicLayer(Framebuffer pixels, Palette palette)
    {
        Pixels = pixels;
        Palette = palette;
    }

    /// <summary>The presented pixels. Renderers read only; the game replaces them on present.</summary>
    public Framebuffer Pixels { get; }

    /// <summary>The live palette (<see cref="Palette.Version"/> tells renderers when to re-upload).</summary>
    public Palette Palette { get; }

    /// <summary>Incremented on every present; renderers re-upload the pixels when it changes.</summary>
    public int PixelsVersion { get; private set; }

    /// <summary>Copies a finished frame into the front buffer.</summary>
    public void Present(Framebuffer source)
    {
        source.CopyTo(Pixels);
        PixelsVersion++;
    }

    /// <summary>Signals that <see cref="Pixels"/> was modified in place.</summary>
    public void MarkPixelsChanged() => PixelsVersion++;
}

/// <summary>
/// Everything a renderer needs to draw one displayed frame: the classic layer, plus optional
/// layers a modern renderer draws at output resolution - the space view underneath the
/// cockpit/HUD pixels (ADR-010 R2+), the game's text on top of it and the key help overlay
/// (ADR-013).
/// </summary>
public sealed class RenderFrame
{
    public RenderFrame(ClassicLayer classic)
    {
        Classic = classic;
    }

    public ClassicLayer Classic { get; }

    /// <summary>Position between the previous and the latest simulation tick, 0..1 (for interpolation).</summary>
    public float Interpolation { get; set; }

    /// <summary>
    /// The space window drawn at output resolution underneath the classic layer's cockpit and HUD
    /// (ADR-010 R2+), or null: then the frame is the classic layer only.
    /// </summary>
    public SpaceView? Space { get; set; }

    /// <summary>
    /// The game's font text drawn at output resolution (ADR-013), or null. While set, renderers
    /// that support it show <see cref="TextLayer.Pixels"/> instead of the classic pixels and draw
    /// the glyphs on top (after the space view); others ignore it.
    /// </summary>
    public TextLayer? Text { get; set; }

    /// <summary>The key reference shown next to or over the picture (ADR-013), or null.</summary>
    public KeyHelpOverlay? KeyHelp { get; set; }
}
