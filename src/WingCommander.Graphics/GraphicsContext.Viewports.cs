using WingCommander.Graphics.Cockpit;
using WingCommander.Graphics.Raster;

namespace WingCommander.Graphics;

public sealed partial class GraphicsContext
{
    /// <summary>
    /// Fills the viewport rectangle (skipped for an unallocated viewport). Returns true when the
    /// viewport is the <see cref="Screen"/> object itself (identity, not an alias): the original
    /// then presented immediately (<c>DIBslam(); DIBslamReal()</c>), which the Game layer must do
    /// (this library never presents).
    /// </summary>
    /// <remarks>C: ClearViewport (0x441AE0, gr.c).</remarks>
    public bool ClearViewport(Viewport viewport, byte colour)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        if (viewport.Surface is not null)
            RasterPrimitives.FillRasterClip(ClipViewportToScreen(viewport), colour);
        if (!ReferenceEquals(viewport, Screen))
            return false;
        ScreenDirty = true;
        return true;
    }

    /// <summary>
    /// Copies the top-left aligned overlap of the two rectangles: source pixel
    /// (src.Left + i, src.Top + j) goes to (dst.Left + i, dst.Top + j). The rectangles need not
    /// share coordinates or surfaces.
    /// </summary>
    /// <remarks>C: CopyViewportContents (0x441A90, gr.c) -> BlitRasterClip.</remarks>
    public void CopyViewportContents(Viewport source, Viewport destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        RasterClip sourceClip = ClipViewportToScreen(source);
        RasterClip destinationClip = ClipViewportToScreen(destination);
        RasterPrimitives.BlitRasterClip(sourceClip, source.Left, source.Top, destinationClip,
            destination.Left, destination.Top, TextTracker);
    }

    /// <summary>Sets one clipped pixel (absolute coordinates).</summary>
    /// <remarks>C: DrawViewportPixel (0x441B20, gr.c).</remarks>
    public void DrawViewportPixel(Viewport viewport, int x, int y, byte colour) =>
        RasterPrimitives.SetRasterClipPixel(ClipViewportToScreen(viewport), x, y, colour);

    /// <summary>Reads one pixel; -2 for an empty viewport, -3 outside the rectangle.</summary>
    /// <remarks>C: GetViewportPixel (0x441B60, gr.c).</remarks>
    public int GetViewportPixel(Viewport viewport, int x, int y) =>
        RasterPrimitives.ReadRasterClipPixel(ClipViewportToScreen(viewport), x, y);

    /// <summary>Clipped line between two absolute points (both inclusive).</summary>
    /// <remarks>C: DrawViewportLine (0x441BA0, gr.c).</remarks>
    public void DrawViewportLine(Viewport viewport, int x1, int y1, int x2, int y2, byte colour) =>
        RasterPrimitives.DrawClippedLine(ClipViewportToScreen(viewport), x1, y1, x2, y2, colour);

    /// <summary>Filled rectangle drawn as one horizontal line per row <c>top..bottom</c>.</summary>
    /// <remarks>C: DrawFilledViewportRect (0x441C70, gr.c).</remarks>
    public void DrawFilledViewportRect(Viewport viewport, int left, int top, int right, int bottom, byte colour)
    {
        RasterClip clip = ClipViewportToScreen(viewport);
        int height = bottom - top;
        for (int row = 0; row <= height; row++)
            RasterPrimitives.DrawClippedLine(clip, left, row + top, right, row + top, colour);
    }

    /// <summary>Rectangle outline: top, bottom, left and right edges (corners drawn twice).</summary>
    /// <remarks>C: DrawViewportBorder (0x441CF0, gr.c).</remarks>
    public void DrawViewportBorder(Viewport viewport, int left, int top, int right, int bottom, byte colour)
    {
        RasterClip clip = ClipViewportToScreen(viewport);
        RasterPrimitives.DrawClippedLine(clip, left, top, right, top, colour);
        RasterPrimitives.DrawClippedLine(clip, left, bottom, right, bottom, colour);
        RasterPrimitives.DrawClippedLine(clip, left, top, left, bottom, colour);
        RasterPrimitives.DrawClippedLine(clip, right, top, right, bottom, colour);
    }

    /// <summary>
    /// Ellipse outline. Note the argument order (vertical radius first) and that the centre is
    /// relative to the viewport's top-left corner: unlike every other wrapper the original does
    /// not subtract the viewport origin here.
    /// </summary>
    /// <remarks>C: DrawViewportEllipse (0x441DD0, gr.c).</remarks>
    public void DrawViewportEllipse(Viewport viewport, int x, int y, int verticalRadius, int horizontalRadius,
        byte colour)
    {
        RasterClip clip = ClipViewportToScreen(viewport);
        RasterPrimitives.DrawRasterEllipse(clip, x + viewport.Left, y + viewport.Top, horizontalRadius,
            verticalRadius, colour);
    }

    /// <summary>Identical to <see cref="DrawViewportEllipse"/> (the "shadow" variant has the same body).</summary>
    /// <remarks>C: DrawViewportEllipseShadow (0x441E70, gr.c).</remarks>
    public void DrawViewportEllipseShadow(Viewport viewport, int x, int y, int verticalRadius,
        int horizontalRadius, byte colour) =>
        DrawViewportEllipse(viewport, x, y, verticalRadius, horizontalRadius, colour);

    /// <summary>Filled ellipse; same argument conventions as <see cref="DrawViewportEllipse"/>.</summary>
    /// <remarks>C: FillViewportEllipse (0x441E20, gr.c). No caller in the shipped game.</remarks>
    public void FillViewportEllipse(Viewport viewport, int x, int y, int verticalRadius, int horizontalRadius,
        byte colour)
    {
        RasterClip clip = ClipViewportToScreen(viewport);
        RasterPrimitives.FillRasterEllipse(clip, x + viewport.Left, y + viewport.Top, horizontalRadius,
            verticalRadius, colour);
    }

    /// <summary>
    /// Copies the space view buffer to the screen through a cockpit view mask: every run copies
    /// <c>length</c> bytes linearly from buffer position (destX - originX, screenY - originY) to
    /// screen position (destX, screenY), so runs may continue into following rows of both
    /// buffers. The name is historical; there is no fizzle.
    /// </summary>
    /// <remarks>C: fizzle_fade (0x442200, gr.c). Runs are clamped to both buffers.</remarks>
    public void FizzleFade(Viewport source, Viewport destination, ViewGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(geometry);
        if (source.Surface is null || destination.Surface is null)
            return;
        IndexedSurface src = source.Surface, dst = destination.Surface;
        int sourceLeft = geometry.OriginX, sourceTop = geometry.OriginY;
        foreach (ViewRun run in geometry.Runs)
        {
            int from = src.IndexOf(run.DestinationX - sourceLeft, run.ScreenY - sourceTop);
            int to = dst.IndexOf(run.DestinationX, run.ScreenY);
            int length = run.Length;
            int skip = Math.Max(0, Math.Max(-from, -to));
            from += skip;
            to += skip;
            length -= skip;
            length = Math.Min(length, Math.Min(src.Pixels.Length - from, dst.Pixels.Length - to));
            if (length > 0)
            {
                TextTracker?.OnCopy(src, from, dst, to, length);
                src.Pixels.AsSpan(from, length).CopyTo(dst.Pixels.AsSpan(to, length));
            }
        }
        MarkDirtyIfScreen(destination);
    }

    /// <summary>
    /// Static noise on a knocked-out display. The retail Kilrathi Saga build draws nothing (only
    /// the dirty flag is set); the SDL port's xorshift static is not reproduced.
    /// </summary>
    /// <remarks>C: snow_viewport (0x442300, gr.c).</remarks>
    public void SnowViewport(Viewport viewport, int effect, int colour)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        _ = effect;
        _ = colour;
        MarkDirtyIfScreen(viewport);
    }
}
