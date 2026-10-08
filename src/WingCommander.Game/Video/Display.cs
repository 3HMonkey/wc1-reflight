using WingCommander.Core.Rendering;
using WingCommander.Core.Video;
using WingCommander.Game.Timing;
using WingCommander.Graphics;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Text;

namespace WingCommander.Game.Video;

/// <summary>Draws and erases the software mouse cursor in the working frame around a present.</summary>
public interface ISoftwareCursor
{
    /// <summary>True when the cursor is visible and its viewport is the screen.</summary>
    bool IsOnScreen { get; }

    void CaptureAndDraw();

    void Restore();

    /// <summary>Draws the cursor as presented into <paramref name="surface"/> (a 320x200 scratch copy of the screen).</summary>
    void DrawInto(IndexedSurface surface);
}

/// <summary>
/// The game's view of the DIB layer: the working 320x200 frame every screen draws into (wrapped
/// by <see cref="Graphics"/> as the screen viewport), the live palette and the front buffer the
/// renderer shows. Presenting copies the working frame (with the software cursor) into the
/// front buffer and then throttles to the frame rate.
/// </summary>
/// <remarks>
/// <para>The slam flag is <see cref="GraphicsContext.ScreenDirty"/>: every raster call on the
/// screen sets it (the original's DIBslam inside ValidateViewportBounds) and a present clears it,
/// exactly like bDIBSlamPending.</para>
/// <para>The front buffer shares the live palette, so palette writes (fades) affect the pixels
/// on display immediately, as on VGA and DirectDraw. <see cref="PaletteChanged"/> additionally
/// shows the current working pixels, which is what the SDL port's DIBramPalette does.</para>
/// C: dib.c DIBslam (0x432960), DIBslamReal (0x432970), DIBupdate, DIBramPalette,
/// DIBwaitForVerticalBlank; pDIBPixelBuffer, abDIBPaletteCache, bDIBSlamPending, nDIBSlamCount.
/// </remarks>
public sealed class Display
{
    private readonly FrameTiming _timing;
    private int _owedThrottles;

    public Display(FrameTiming timing)
    {
        _timing = timing;
        Palette = new Palette();
        Front = new ClassicLayer(new Framebuffer(), Palette);
        Graphics = new GraphicsContext(Working, Palette, fonts: null);
    }

    /// <summary>The frame the game draws into (pDIBPixelBuffer; the screen viewport aliases it).</summary>
    public Framebuffer Working { get; } = new();

    /// <summary>The live palette (abDIBPaletteCache), shared with the front buffer.</summary>
    public Palette Palette { get; }

    /// <summary>What the renderer shows.</summary>
    public ClassicLayer Front { get; }

    /// <summary>The raster/text/palette state; its <see cref="GraphicsContext.Screen"/> wraps <see cref="Working"/>.</summary>
    public GraphicsContext Graphics { get; }

    /// <remarks>C: bDIBSlamPending.</remarks>
    public bool SlamPending => Graphics.ScreenDirty;

    /// <remarks>C: nDIBSlamCount.</remarks>
    public int SlamCount { get; private set; }

    /// <summary>Software cursor composited at present time.</summary>
    public ISoftwareCursor? Cursor { get; set; }

    /// <summary>Per-present sound servicing (ServiceSoundSystem).</summary>
    public Action? ServiceSound { get; set; }

    /// <summary>
    /// Called right after the working frame became the front buffer (also by <see cref="Update"/>
    /// and <see cref="PaletteChanged"/>), so layers that are drawn by the renderer next to the
    /// classic frame (the R2 space view) can publish their state for exactly that frame.
    /// </summary>
    public Action? Presented { get; set; }

    /// <summary>
    /// The game's text for output-resolution drawing (ADR-013), republished with every present;
    /// null until <see cref="EnableHighResolutionText"/>.
    /// </summary>
    public TextLayer? Text { get; private set; }

    /// <summary>Follows the glyphs drawn into the screen; null while output-resolution text is off.</summary>
    public TextLayerTracker? TextTracker { get; private set; }

    /// <summary>
    /// Starts following the font glyphs drawn into the screen and publishing them in
    /// <see cref="Text"/> with every present (for renderers that draw text at output resolution).
    /// The classic frame is not affected.
    /// </summary>
    public void EnableHighResolutionText(GlyphImageSource glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        TextTracker = new TextLayerTracker(Graphics.ScreenSurface!, glyphs);
        Graphics.TextTracker = TextTracker;
        Text = new TextLayer(glyphs.Cache);
        TextTracker.Publish(Working.Pixels, Text);
    }

    /// <summary>Marks the whole frame dirty.</summary>
    /// <remarks>C: DIBslam (0x432960).</remarks>
    public void Slam() => Graphics.ScreenDirty = true;

    /// <summary>
    /// Presents the working frame if it is pending (with the cursor), services sound, then waits
    /// for the frame deadline (16 fps cinematic, 20 fps in flight).
    /// </summary>
    /// <remarks>C: DIBslamReal (0x432970) + ThrottleFrameAndDrawFps.</remarks>
    public async Task SlamRealAsync()
    {
        if (_owedThrottles > 0)
            await SettleDeferredPresentsAsync();
        if (Graphics.ScreenDirty)
        {
            PresentWorking();
            Graphics.ScreenDirty = false;
        }
        SlamCount++;
        ServiceSound?.Invoke();
        await _timing.ThrottleFrameAsync();
    }

    /// <summary>
    /// The synchronous half of <see cref="SlamRealAsync"/> for presents that the original made in
    /// the middle of synchronous code (the simulation's new_view calling initialize_cockpit(4),
    /// ClearViewport of the screen): presents the working frame if it is pending, counts and
    /// services sound now, and owes the frame throttle, which <see cref="SettleDeferredPresentsAsync"/>
    /// (or the next <see cref="SlamRealAsync"/>) performs at the next await point. The original
    /// copied first and waited afterwards, and no virtual time passes in between, so pixels,
    /// deadlines and the random sequence are the same.
    /// </summary>
    /// <remarks>C: DIBslamReal (0x432970) called from synchronous code; see flight-ui.md §7.2.</remarks>
    public void SlamRealNow()
    {
        if (Graphics.ScreenDirty)
        {
            PresentWorking();
            Graphics.ScreenDirty = false;
        }
        SlamCount++;
        ServiceSound?.Invoke();
        _owedThrottles++;
    }

    /// <summary>True while presents made with <see cref="SlamRealNow"/> still owe their throttle wait.</summary>
    public bool HasDeferredPresents => _owedThrottles > 0;

    /// <summary>Performs the throttle waits owed by <see cref="SlamRealNow"/>, in order.</summary>
    public async Task SettleDeferredPresentsAsync()
    {
        while (_owedThrottles > 0)
        {
            _owedThrottles--;
            await _timing.ThrottleFrameAsync();
        }
    }

    /// <summary>DIBslam followed by DIBslamReal, the pattern most loops use.</summary>
    public Task PresentAsync()
    {
        Slam();
        return SlamRealAsync();
    }

    /// <summary>Clears a viewport; clearing the screen viewport object itself also presents, like the original.</summary>
    /// <remarks>C: ClearViewport (0x441AE0, gr.c) calls DIBslam + DIBslamReal for stScreen.</remarks>
    public Task ClearViewportAsync(Viewport viewport, byte colour) =>
        Graphics.ClearViewport(viewport, colour) ? SlamRealAsync() : Task.CompletedTask;

    /// <summary>Shows the working frame immediately, without the throttle; the slam flag is left alone.</summary>
    /// <remarks>C: DIBupdate and RefreshMouseCursorDisplay (partial updates present the whole
    /// frame, as in the SDL port).</remarks>
    public void Update() => PresentWorking();

    /// <summary>
    /// Shows the current working pixels with the current palette right away (the SDL port's
    /// DIBramPalette). Plain palette writes do not need it to become visible.
    /// </summary>
    /// <remarks>C: DIBramPalette (0x432EA0).</remarks>
    public void PaletteChanged()
    {
        if (TextTracker is not null)
            TextTracker.Publish(Working.Pixels, Text!);
        Front.Present(Working);
        Presented?.Invoke();
    }

    /// <summary>Waits for the next virtual 70 Hz retrace (palette fades step on it).</summary>
    /// <remarks>C: DIBwaitForVerticalBlank (0x4331E0).</remarks>
    public Task WaitForVerticalBlankAsync() => _timing.WaitForVerticalBlankAsync();

    private void PresentWorking()
    {
        var cursor = Cursor;
        bool drawCursor = cursor is { IsOnScreen: true };
        if (TextTracker is not null)
            TextTracker.Publish(Working.Pixels, Text!);
        if (drawCursor)
            cursor!.CaptureAndDraw();
        Front.Present(Working);
        if (drawCursor)
        {
            cursor!.Restore();
            if (Text is not null)
                KeepCursorOnTop(cursor);
        }
        Presented?.Invoke();
    }

    private IndexedSurface? _cursorScratch;

    /// <summary>
    /// The cursor is composited after the text layer was built: its pixels go back into the
    /// layer's frame and no glyph may draw over them. The cursor is drawn twice into a scratch
    /// screen (cleared to 0x00 and to 0xFF) so every pixel it sets is found, whatever its colour.
    /// </summary>
    private void KeepCursorOnTop(ISoftwareCursor cursor)
    {
        _cursorScratch ??= new IndexedSurface(Framebuffer.Width, Framebuffer.Height);
        byte[] scratch = _cursorScratch.Pixels;
        byte[] front = Front.Pixels.Pixels;
        byte[] pixels = Text!.Pixels.Pixels;
        ushort[] mask = Text.Mask;
        for (int pass = 0; pass < 2; pass++)
        {
            byte fill = pass == 0 ? (byte)0x00 : (byte)0xFF;
            Array.Fill(scratch, fill);
            cursor.DrawInto(_cursorScratch);
            for (int p = 0; p < scratch.Length; p++)
            {
                if (scratch[p] == fill)
                    continue;
                pixels[p] = front[p];
                mask[p] = 0;
            }
        }
    }
}
