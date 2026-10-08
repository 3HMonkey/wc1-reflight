using WingCommander.Core.Video;

namespace WingCommander.Graphics.Raster;

/// <summary>
/// An 8-bit indexed pixel buffer (one byte = one palette index, row-major, stride = width).
/// The buffer is placed in the game's absolute coordinate space: <see cref="OriginX"/> /
/// <see cref="OriginY"/> are the coordinates of <c>Pixels[0]</c>. This replaces the
/// original's <c>pixels</c> pointer plus 16-bit <c>rowOffsets</c> table
/// (<c>rowOffsets[y] = (y - OriginY) * Width - OriginX</c>): pixel (x, y) lives at
/// <c>Pixels[(y - OriginY) * Width + (x - OriginX)]</c>.
/// </summary>
/// <remarks>
/// C: the (pixels, rowOffsets) pair of <c>Viewport</c>; <c>RasterSurface</c> (include/wc1.h).
/// Every <see cref="Viewport"/> alias of the same buffer shares one surface.
/// </remarks>
public sealed class IndexedSurface
{
    /// <summary>Allocates a zero-filled surface.</summary>
    public IndexedSurface(int width, int height, int originX = 0, int originY = 0)
        : this(new byte[checked(Math.Max(width, 0) * Math.Max(height, 0))], width, height, originX, originY)
    {
    }

    /// <summary>Wraps an existing buffer without copying it (used for the 320x200 screen).</summary>
    public IndexedSurface(byte[] pixels, int width, int height, int originX = 0, int originY = 0)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Surface dimensions must be positive.");
        if (pixels.Length < width * height)
            throw new ArgumentException("Pixel buffer is smaller than width * height.", nameof(pixels));
        Pixels = pixels;
        Width = width;
        Height = height;
        OriginX = originX;
        OriginY = originY;
    }

    /// <summary>The pixel buffer (row-major, <see cref="Stride"/> bytes per row).</summary>
    public byte[] Pixels { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Bytes per row; always equal to <see cref="Width"/>.</summary>
    public int Stride => Width;

    /// <summary>Absolute x coordinate of <c>Pixels[0]</c>.</summary>
    public int OriginX { get; }

    /// <summary>Absolute y coordinate of <c>Pixels[0]</c>.</summary>
    public int OriginY { get; }

    /// <summary>Leftmost absolute column covered by the buffer.</summary>
    public int Left => OriginX;

    /// <summary>Topmost absolute row covered by the buffer.</summary>
    public int Top => OriginY;

    /// <summary>Rightmost absolute column covered by the buffer (inclusive).</summary>
    public int Right => OriginX + Width - 1;

    /// <summary>Bottom absolute row covered by the buffer (inclusive).</summary>
    public int Bottom => OriginY + Height - 1;

    /// <summary>Wraps the 320x200 frame buffer (no copy): drawing into the surface draws into the frame.</summary>
    public static IndexedSurface FromFramebuffer(Framebuffer framebuffer)
    {
        ArgumentNullException.ThrowIfNull(framebuffer);
        return new IndexedSurface(framebuffer.Pixels, Framebuffer.Width, Framebuffer.Height);
    }

    /// <summary>Buffer index of the absolute coordinate (x, y); not range checked.</summary>
    public int IndexOf(int x, int y) => (y - OriginY) * Width + (x - OriginX);

    /// <summary>True when the absolute coordinate lies inside the buffer.</summary>
    public bool Contains(int x, int y) =>
        (uint)(x - OriginX) < (uint)Width && (uint)(y - OriginY) < (uint)Height;

    /// <summary>
    /// The original 16-bit row offset of an absolute row (<c>rowOffsets[y]</c>), i.e. the byte
    /// offset of column 0 of that row relative to the buffer start, truncated to 16 bits. Only
    /// needed to reproduce the Kilrathi Saga glyph quirk (see <c>GraphicsContext.DrawFontGlyph</c>).
    /// </summary>
    public ushort GetRowOffset16(int y) => unchecked((ushort)((y - OriginY) * Width - OriginX));

    /// <summary>The pixels of one absolute row.</summary>
    public Span<byte> GetRow(int y)
    {
        if ((uint)(y - OriginY) >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(y));
        return Pixels.AsSpan((y - OriginY) * Width, Width);
    }

    /// <summary>Reads the pixel at an absolute coordinate (throws when outside the buffer).</summary>
    public byte this[int x, int y]
    {
        get
        {
            if (!Contains(x, y))
                throw new ArgumentOutOfRangeException(nameof(x), $"({x},{y}) is outside the surface.");
            return Pixels[IndexOf(x, y)];
        }
        set
        {
            if (!Contains(x, y))
                throw new ArgumentOutOfRangeException(nameof(x), $"({x},{y}) is outside the surface.");
            Pixels[IndexOf(x, y)] = value;
        }
    }

    /// <summary>Fills the whole buffer.</summary>
    public void Clear(byte colour) => Pixels.AsSpan(0, Width * Height).Fill(colour);
}
