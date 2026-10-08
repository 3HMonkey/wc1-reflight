using WingCommander.Core.Video;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Graphics.Text;

namespace WingCommander.Graphics;

/// <summary>
/// The state the original keeps in raster/text/palette globals, plus the game-facing wrappers
/// of gr.c, mathfp.c, cockpt.c and the fade functions, named like the C functions. One instance
/// per game (single-threaded). Drawing calls take absolute coordinates except where a member
/// documents otherwise. The library never presents and never waits (ADR-009): every call that
/// validates a viewport of the screen surface only sets <see cref="ScreenDirty"/> (the
/// original's <c>DIBslam</c>); presenting, frame throttling and vertical-blank waits belong to
/// the Game layer.
/// </summary>
/// <remarks>C globals: stScreen, stRasterSurface/stRasterClip, abShapeTransformScratch,
/// abRasterPaletteTranslation, abSolidColourTranslation, abPaletteTranslation,
/// pCurrentTextContext, apTextFonts, bDIBSlamPending and the palette caches.</remarks>
public sealed partial class GraphicsContext
{
    private readonly byte[] _transformScratch = new byte[RleRenderer.TransformScratchSize];
    private readonly byte[] _rasterPaletteTranslation = new byte[256];
    private readonly byte[] _solidColourTranslation = new byte[256];

    /// <summary>A context without a screen (tools and tests); drawing goes to explicit viewports.</summary>
    public GraphicsContext()
        : this(null, new Palette(), null)
    {
    }

    /// <summary>
    /// Creates the context. <paramref name="framebuffer"/> (the game's working 320x200 buffer)
    /// becomes <see cref="Screen"/> (wrapped, not copied); <paramref name="palette"/> is the live
    /// palette.
    /// </summary>
    public GraphicsContext(Framebuffer? framebuffer, Palette palette, FontCache? fonts)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (framebuffer is not null)
            Screen = Viewport.InitializeDIBScreenViewport(framebuffer);
        Palette = new GamePalette(palette);
        Fonts = fonts;
        for (int i = 0; i < 256; i++)
            _rasterPaletteTranslation[i] = (byte)i;
    }

    /// <summary>The 320x200 screen viewport (null without a frame buffer).</summary>
    /// <remarks>C: stScreen (0x005a6ba0).</remarks>
    public Viewport? Screen { get; }

    /// <summary>The screen's pixel buffer (shared by all screen aliases).</summary>
    public IndexedSurface? ScreenSurface => Screen?.Surface;

    /// <summary>
    /// Set by every call that touches a viewport of the screen surface (even reads); the Game's
    /// present consumes and clears it.
    /// </summary>
    /// <remarks>C: bDIBSlamPending, set by DIBslam (0x432960).</remarks>
    public bool ScreenDirty { get; set; }

    /// <summary>Palette state (live palette, saved mirror, fade scratch).</summary>
    public GamePalette Palette { get; }

    /// <summary>Flight palette flashes (entries 185..191).</summary>
    public FlightPaletteEffects FlightPalette { get; } = new();

    /// <summary>The four FONTS.FNT fonts (null when the context is used without text).</summary>
    public FontCache? Fonts { get; set; }

    /// <summary>
    /// Translation table used by sprites drawn with a blend mode (solid-colour silhouettes);
    /// identity initially.
    /// </summary>
    /// <remarks>C: abRasterPaletteTranslation.</remarks>
    public ReadOnlySpan<byte> RasterPaletteTranslation => _rasterPaletteTranslation;

    /// <summary>
    /// Reproduces the Kilrathi Saga glyph addressing bug: the first glyph row drawn on a
    /// viewport's top row lands in buffer row 0 when that row's 16-bit offset has bit 15 set
    /// (screen rows &gt;= 103; allocated viewports with left &gt; 0). Off by default, which gives
    /// the DOS behaviour.
    /// </summary>
    public bool EmulateSagaGlyphRowQuirk { get; set; }

    /// <summary>
    /// Builds the raster clip of a viewport and marks the screen dirty when the viewport draws
    /// into the screen surface (even for reads, like the original).
    /// </summary>
    /// <remarks>C: ClipViewportToScreen (0x440CF0) / ValidateViewportBounds (0x440C00, gr.c).</remarks>
    public RasterClip ClipViewportToScreen(Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        RasterClip clip = viewport.GetClip();
        MarkDirtyIfScreen(viewport);
        return clip;
    }

    /// <summary>True when the viewport draws into the screen buffer (C: <c>pixels == stScreen.pixels</c>).</summary>
    public bool IsScreenSurface(Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        return viewport.Surface is not null && ReferenceEquals(viewport.Surface, ScreenSurface);
    }

    private void MarkDirtyIfScreen(Viewport viewport)
    {
        if (IsScreenSurface(viewport))
            ScreenDirty = true;
    }
}
