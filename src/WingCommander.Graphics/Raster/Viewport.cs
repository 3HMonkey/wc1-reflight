using WingCommander.Core.Video;

namespace WingCommander.Graphics.Raster;

/// <summary>
/// A drawing target: a surface plus an inclusive rectangle in absolute ("screen")
/// coordinates. The rectangle is purely a clip rectangle and coordinate frame; several
/// viewports may alias one surface with different rectangles (the original copies the
/// <c>Viewport</c> struct and shrinks it: <c>stLeftVdu = stScreen; stLeftVdu.left = ...</c>).
/// Use <see cref="Clone"/> / <see cref="CopyFrom"/> for those struct copies; the clone shares
/// the surface. Coordinates passed to drawing calls are absolute unless documented otherwise.
/// </summary>
/// <remarks>C: Viewport (include/wc1.h). The 16-bit row table, the allocation registry and
/// <c>SignExtendClipCoord</c> are replaced by <see cref="IndexedSurface"/>.</remarks>
public sealed class Viewport
{
    /// <summary>An empty viewport (no surface, zero rectangle), like a zeroed C struct.</summary>
    public Viewport()
    {
    }

    public Viewport(IndexedSurface? surface, int left, int top, int right, int bottom)
    {
        Surface = surface;
        SetViewportRect(left, top, right, bottom);
    }

    /// <summary>The pixel buffer; null when unallocated or freed (C: <c>pixels == NULL</c>).</summary>
    public IndexedSurface? Surface { get; set; }

    public short Left { get; set; }

    public short Top { get; set; }

    /// <summary>Inclusive right edge.</summary>
    public short Right { get; set; }

    /// <summary>Inclusive bottom edge.</summary>
    public short Bottom { get; set; }

    public int Width => Right - Left + 1;

    public int Height => Bottom - Top + 1;

    /// <summary>True when the viewport has a pixel buffer.</summary>
    public bool IsAllocated => Surface is not null;

    /// <summary>Creates the 320x200 screen viewport bound to the frame buffer (no copy).</summary>
    /// <remarks>C: InitializeDIBScreenViewport (0x42F740, screen.c).</remarks>
    public static Viewport InitializeDIBScreenViewport(Framebuffer framebuffer) =>
        new(IndexedSurface.FromFramebuffer(framebuffer), 0, 0, Framebuffer.Width - 1, Framebuffer.Height - 1);

    /// <summary>Allocates an off-screen 320x200 viewport, cleared unless <paramref name="clearColour"/> is -1.</summary>
    /// <remarks>C: InitFullScreenViewport (0x42F7E0, screen.c).</remarks>
    public static Viewport InitFullScreenViewport(short clearColour) => Allocate(0, 0, 319, 199, clearColour);

    /// <summary>Allocates an off-screen viewport whose buffer covers exactly the rectangle.</summary>
    /// <remarks>C: AllocateViewport (0x42E090, music.c) after setting the rectangle.</remarks>
    public static Viewport Allocate(int left, int top, int right, int bottom, short clearColour = -1)
    {
        var viewport = new Viewport(null, left, top, right, bottom);
        if (!viewport.AllocateViewport(clearColour))
            throw new ArgumentException($"Cannot allocate an empty viewport ({left},{top})-({right},{bottom}).");
        return viewport;
    }

    /// <summary>
    /// Allocates a buffer for the rectangle currently stored in the viewport (origin = the
    /// rectangle's top-left corner) and clears it to <paramref name="clearColour"/> unless that is -1.
    /// Returns false for an empty rectangle (the original's allocation failure).
    /// </summary>
    /// <remarks>C: AllocateViewport (0x42E090, music.c).</remarks>
    public bool AllocateViewport(short clearColour)
    {
        int width = Width, height = Height;
        if (width <= 0 || height <= 0)
            return false;
        Surface = new IndexedSurface(width, height, Left, Top);
        if (clearColour != -1)
            Surface.Clear(unchecked((byte)clearColour));
        return true;
    }

    /// <summary>Releases the buffer (aliases keep their own reference, like dangling C pointers would).</summary>
    /// <remarks>C: free_viewport (0x40F940, nav.c).</remarks>
    public void FreeViewport() => Surface = null;

    /// <summary>Assigns the four rectangle edges.</summary>
    /// <remarks>C: SetViewportRect (0x439400, screens.c).</remarks>
    public void SetViewportRect(int left, int top, int right, int bottom)
    {
        Left = unchecked((short)left);
        Top = unchecked((short)top);
        Right = unchecked((short)right);
        Bottom = unchecked((short)bottom);
    }

    /// <summary>Width times height, truncated to 16 bits like the original.</summary>
    /// <remarks>C: CalcRectangleArea (0x42E050, music.c).</remarks>
    public short CalcRectangleArea() =>
        unchecked((short)((short)(Bottom - Top + 1) * (short)(Right - Left + 1)));

    /// <summary>A struct-copy alias: same surface, same rectangle, independent afterwards.</summary>
    public Viewport Clone() => new() { Surface = Surface, Left = Left, Top = Top, Right = Right, Bottom = Bottom };

    /// <summary>C struct assignment <c>*this = *source</c>.</summary>
    public void CopyFrom(Viewport source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Surface = source.Surface;
        Left = source.Left;
        Top = source.Top;
        Right = source.Right;
        Bottom = source.Bottom;
    }

    /// <summary>True when the absolute point lies inside the rectangle (inclusive).</summary>
    public bool ContainsPoint(int x, int y) => Left <= x && x <= Right && Top <= y && y <= Bottom;

    /// <summary>
    /// The raster-library clip for this viewport. Throws when the viewport has no buffer, which
    /// is where the original exited with "bad viewport".
    /// </summary>
    /// <remarks>C: ValidateViewportBounds (0x440C00, gr.c), without the DIB dirty side effect
    /// (see <c>GraphicsContext.ClipViewportToScreen</c>).</remarks>
    public RasterClip GetClip() =>
        Surface is null
            ? throw new InvalidOperationException("bad viewport: the viewport has no pixel buffer.")
            : new RasterClip(Surface, Left, Top, Right, Bottom);

    public override string ToString() => $"Viewport ({Left},{Top})-({Right},{Bottom}){(Surface is null ? " unallocated" : "")}";
}
