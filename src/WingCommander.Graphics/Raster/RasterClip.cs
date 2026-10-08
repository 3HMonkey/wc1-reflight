namespace WingCommander.Graphics.Raster;

/// <summary>
/// The raster library's view of a viewport: a surface plus the inclusive clip rectangle in
/// absolute coordinates. The rectangle is already intersected with the surface bounds, which
/// reproduces the "common clip rule" of every primitive (<c>cl = max(clip.left, 0)</c>,
/// <c>cr = min(clip.right, w - 1)</c>, ...) for valid viewports and keeps invalid ones from
/// touching memory outside the buffer. <see cref="OriginX"/>/<see cref="OriginY"/> keep the
/// unclipped rectangle corner (the clip-relative origin of the C code).
/// </summary>
/// <remarks>C: RasterSurface + RasterClip built by ValidateViewportBounds (0x440C00, gr.c).</remarks>
public readonly struct RasterClip
{
    public RasterClip(IndexedSurface surface, int left, int top, int right, int bottom)
    {
        ArgumentNullException.ThrowIfNull(surface);
        Surface = surface;
        OriginX = left;
        OriginY = top;
        Left = Math.Max(left, surface.Left);
        Top = Math.Max(top, surface.Top);
        Right = Math.Min(right, surface.Right);
        Bottom = Math.Min(bottom, surface.Bottom);
    }

    public IndexedSurface Surface { get; }

    /// <summary>Clipped left edge (absolute, inclusive).</summary>
    public int Left { get; }

    /// <summary>Clipped top edge (absolute, inclusive).</summary>
    public int Top { get; }

    /// <summary>Clipped right edge (absolute, inclusive).</summary>
    public int Right { get; }

    /// <summary>Clipped bottom edge (absolute, inclusive).</summary>
    public int Bottom { get; }

    /// <summary>Unclipped left edge of the rectangle (the C clip-relative origin).</summary>
    public int OriginX { get; }

    /// <summary>Unclipped top edge of the rectangle (the C clip-relative origin).</summary>
    public int OriginY { get; }

    /// <summary>True when no pixel can be drawn (C: return code -2).</summary>
    public bool IsEmpty => Right < Left || Bottom < Top;

    /// <summary>A clip covering a whole surface.</summary>
    public static RasterClip ForSurface(IndexedSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return new RasterClip(surface, surface.Left, surface.Top, surface.Right, surface.Bottom);
    }

    /// <summary>True when the absolute coordinate is inside the clip rectangle.</summary>
    public bool Contains(int x, int y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
}
