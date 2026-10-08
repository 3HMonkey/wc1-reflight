namespace WingCommander.Graphics.Shapes;

/// <summary>
/// Decodes the raw on-disk row stream of a shape frame into a bitmap. Each span starts at
/// hot spot + (dx, dy); spans are opaque and transparency is expressed by the gaps between
/// them, so callers pre-fill the bitmap with the transparent index 0xFF.
/// </summary>
public static class ShapeFrameDecoder
{
    /// <summary>
    /// Writes the spans of <paramref name="frame"/> into a <paramref name="width"/>-stride bitmap
    /// with the hot spot at (<paramref name="leftExtent"/>, <paramref name="topExtent"/>). Rows
    /// outside <c>[0, height-1]</c> are skipped (the stream is still consumed) and spans are
    /// clipped to <c>[0, width-1]</c>. Pixels not covered by a span are left untouched.
    /// </summary>
    /// <remarks>C: DecodeShapeFrame (0x440960, killbrd.c).</remarks>
    public static void DecodeShapeFrame(ShapeTable shape, int frame, Span<byte> bitmap, int width, int height,
        int leftExtent, int topExtent)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (frame < 0 || !shape.HasFrame(frame))
            return;
        DecodeRowStream(shape.GetFramePixels(frame).Span, shape.Name, bitmap, width, height, leftExtent, topExtent);
    }

    /// <summary>Decodes a raw row stream (the bytes after the 8-byte extents header).</summary>
    /// <remarks>C: DecodeShapeFrame (0x440960, killbrd.c).</remarks>
    public static void DecodeRowStream(ReadOnlySpan<byte> rowStream, string name, Span<byte> bitmap, int width,
        int height, int leftExtent, int topExtent)
    {
        int maximumX = width - 1;
        int maximumY = height - 1;
        var reader = new RowStreamReader(rowStream, name);
        ushort rowCode = reader.ReadUInt16();
        while (rowCode != 0)
        {
            int x = leftExtent + reader.ReadInt16();
            int y = topExtent + reader.ReadInt16();
            bool rowVisible = y >= 0 && y <= maximumY;
            int rowBase = y * width;
            if ((rowCode & 1) != 0)
            {
                int remaining = rowCode >> 1;
                while (remaining > 0)
                {
                    byte code = reader.ReadByte();
                    int runLength = code >> 1;
                    if ((code & 1) != 0)
                    {
                        byte colour = reader.ReadByte();
                        if (rowVisible && ClipRun(x, runLength, maximumX, out int skip, out int length))
                            bitmap.Slice(rowBase + x + skip, length).Fill(colour);
                    }
                    else
                    {
                        ReadOnlySpan<byte> run = reader.ReadBytes(runLength);
                        if (rowVisible && ClipRun(x, runLength, maximumX, out int skip, out int length))
                            run.Slice(skip, length).CopyTo(bitmap.Slice(rowBase + x + skip, length));
                    }
                    remaining -= runLength;
                    x += runLength;
                }
            }
            else
            {
                int runLength = rowCode >> 1;
                ReadOnlySpan<byte> run = reader.ReadBytes(runLength);
                if (rowVisible && ClipRun(x, runLength, maximumX, out int skip, out int length))
                    run.Slice(skip, length).CopyTo(bitmap.Slice(rowBase + x + skip, length));
            }
            rowCode = reader.ReadUInt16();
        }
    }

    /// <summary>Clips the run [x, x+runLength-1] to [0, maximumX].</summary>
    private static bool ClipRun(int x, int runLength, int maximumX, out int skip, out int length)
    {
        int runRight = x + runLength - 1;
        skip = 0;
        length = 0;
        if (runLength <= 0 || x > maximumX || runRight < 0)
            return false;
        length = runLength;
        if (x < 0)
        {
            skip = -x;
            length += x;
        }
        if (maximumX < runRight)
            length -= runRight - maximumX;
        return length > 0;
    }
}
