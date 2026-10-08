using WingCommander.Graphics.Raster;

namespace WingCommander.Graphics.Shapes;

/// <summary>
/// Saves and restores the pixels a sprite covers, walking the frame's raw row stream (not the
/// prepared form) at hot spot (x, y). Every span is clipped to the viewport rectangle and
/// only the clipped part is stored, sequentially in stream order. Used for the software mouse
/// cursor.
/// </summary>
public static class SpriteBackground
{
    /// <summary>
    /// Copies the viewport pixels under the frame's spans into <paramref name="background"/>.
    /// Returns the number of bytes stored (storage stops when the buffer is full).
    /// </summary>
    /// <remarks>C: CaptureSpriteBackground (0x441450, gr.c).</remarks>
    public static int CaptureSpriteBackground(Viewport viewport, Span<byte> background, int x, int y,
        ShapeTable? shape, int frame) =>
        Walk(viewport, background, ReadOnlySpan<byte>.Empty, capture: true, x, y, shape, frame);

    /// <summary>Writes the bytes saved by <see cref="CaptureSpriteBackground"/> back (same walk).</summary>
    /// <remarks>C: RestoreSpriteBackground (0x441740, gr.c); the DIB dirty flag is set by
    /// <c>GraphicsContext.RestoreSpriteBackground</c>.</remarks>
    public static int RestoreSpriteBackground(Viewport viewport, ReadOnlySpan<byte> background, int x, int y,
        ShapeTable? shape, int frame) =>
        Walk(viewport, Span<byte>.Empty, background, capture: false, x, y, shape, frame);

    /// <summary>Total pixel count of the frame's spans (the save size needed when unclipped).</summary>
    /// <remarks>C: MeasureShapeFrameStorage (0x435340, mathfp.c).</remarks>
    public static int MeasureShapeFrameStorage(ShapeTable? shape, int frame)
    {
        if (shape is null || frame < 0 || !shape.HasFrame(frame))
            return 0;
        int size = 0;
        var reader = new RowStreamReader(shape.GetFramePixels(frame).Span, shape.Name);
        ushort rowLength = reader.ReadUInt16();
        while (rowLength != 0)
        {
            reader.ReadInt16();
            reader.ReadInt16();
            if ((rowLength & 1) != 0)
            {
                int remaining = rowLength >> 1;
                while (remaining > 0)
                {
                    byte command = reader.ReadByte();
                    int run = command >> 1;
                    if ((command & 1) != 0)
                        reader.ReadByte();
                    else
                        reader.ReadBytes(run);
                    remaining -= run;
                    size += run;
                }
            }
            else
            {
                int run = rowLength >> 1;
                reader.ReadBytes(run);
                size += run;
            }
            rowLength = reader.ReadUInt16();
        }
        return size;
    }

    private static int Walk(Viewport viewport, Span<byte> captureTarget, ReadOnlySpan<byte> restoreSource,
        bool capture, int x, int y, ShapeTable? shape, int frame)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        if (shape is null || frame < 0 || !shape.HasFrame(frame) || viewport.Surface is null)
            return 0;
        IndexedSurface surface = viewport.Surface;
        var bounds = new SpanBounds(
            Math.Max(viewport.Left, surface.Left), Math.Max(viewport.Top, surface.Top),
            Math.Min(viewport.Right, surface.Right), Math.Min(viewport.Bottom, surface.Bottom));
        int capacity = capture ? captureTarget.Length : restoreSource.Length;
        int saved = 0;

        var reader = new RowStreamReader(shape.GetFramePixels(frame).Span, shape.Name);
        ushort count = reader.ReadUInt16();
        while (count != 0)
        {
            int drawX = x + reader.ReadInt16();
            int drawY = y + reader.ReadInt16();
            if ((count & 1) != 0)
            {
                int remaining = count >> 1;
                while (remaining > 0)
                {
                    byte code = reader.ReadByte();
                    int runLength = code >> 1;
                    if ((code & 1) != 0)
                        reader.ReadByte();
                    else
                        reader.ReadBytes(runLength);
                    if (bounds.Clip(drawX, drawY, runLength, out int start, out int length))
                    {
                        if (saved + length > capacity)
                            return saved;
                        Transfer(surface, start, drawY, length, captureTarget, restoreSource, saved, capture);
                        saved += length;
                    }
                    remaining -= runLength;
                    drawX += runLength;
                }
            }
            else
            {
                int runLength = count >> 1;
                reader.ReadBytes(runLength);
                if (bounds.Clip(drawX, drawY, runLength, out int start, out int length))
                {
                    if (saved + length > capacity)
                        return saved;
                    Transfer(surface, start, drawY, length, captureTarget, restoreSource, saved, capture);
                    saved += length;
                }
            }
            count = reader.ReadUInt16();
        }
        return saved;
    }

    private static void Transfer(IndexedSurface surface, int x, int y, int length, Span<byte> captureTarget,
        ReadOnlySpan<byte> restoreSource, int offset, bool capture)
    {
        Span<byte> screen = surface.Pixels.AsSpan(surface.IndexOf(x, y), length);
        if (capture)
            screen.CopyTo(captureTarget.Slice(offset, length));
        else
            restoreSource.Slice(offset, length).CopyTo(screen);
    }

    /// <summary>The viewport rectangle a run is clipped against (inclusive).</summary>
    private readonly record struct SpanBounds(int Left, int Top, int Right, int Bottom)
    {
        public bool Clip(int x, int y, int runLength, out int start, out int length)
        {
            int endX = x + runLength - 1;
            start = x;
            length = 0;
            if (runLength <= 0 || y < Top || y > Bottom || x > Right || endX < Left)
                return false;
            length = runLength;
            if (x < Left)
            {
                length -= Left - x;
                start = Left;
            }
            if (Right < endX)
                length -= endX - Right;
            return length > 0;
        }
    }
}
