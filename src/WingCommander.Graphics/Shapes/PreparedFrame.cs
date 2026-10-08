using System.Buffers;

namespace WingCommander.Graphics.Shapes;

/// <summary>
/// The prepared ("1.00") draw form of one shape frame: exactly <see cref="Height"/> rows of
/// byte ops, each row starting at bitmap column 0 (x = <see cref="Left"/> relative to the hot
/// spot). Ops: <c>0x00</c> end of row, <c>0x01 n</c> skip n transparent pixels, odd
/// <c>c &gt;= 3</c> copy <c>c &gt;&gt; 1</c> literal bytes, even <c>c &gt;= 2</c> fill
/// <c>c &gt;&gt; 1</c> pixels with the next byte (supported by the renderer, never emitted by the
/// encoder). <see cref="RowStarts"/> indexes the first op of each row so clipped rows can be
/// skipped without parsing.
/// </summary>
/// <remarks>C: RLEFrameHeader + the per-frame block built by PrepareShapeRLEData (0x440D50, gr.c).</remarks>
public sealed class PreparedFrame
{
    private readonly byte[] _ops;
    private readonly int[] _rowStarts;

    private PreparedFrame(int width, int height, int leftExtent, int topExtent, byte[] ops, int[] rowStarts)
    {
        Width = width;
        Height = height;
        LeftExtent = leftExtent;
        TopExtent = topExtent;
        _ops = ops;
        _rowStarts = rowStarts;
    }

    /// <summary>Bitmap width (<c>leftExtent + rightExtent + 1</c>).</summary>
    public int Width { get; }

    /// <summary>Bitmap height (<c>topExtent + bottomExtent + 1</c>).</summary>
    public int Height { get; }

    public int LeftExtent { get; }

    public int TopExtent { get; }

    /// <summary>Leftmost pixel relative to the hot spot (<c>-LeftExtent</c>).</summary>
    public int Left => -LeftExtent;

    /// <summary>Topmost pixel relative to the hot spot (<c>-TopExtent</c>).</summary>
    public int Top => -TopExtent;

    /// <summary>Rightmost pixel relative to the hot spot, inclusive.</summary>
    public int Right => Width - LeftExtent - 1;

    /// <summary>Bottom pixel relative to the hot spot, inclusive.</summary>
    public int Bottom => Height - TopExtent - 1;

    /// <summary>The op stream of all rows.</summary>
    public ReadOnlySpan<byte> Ops => _ops;

    /// <summary>Index into <see cref="Ops"/> of the first op of each row.</summary>
    public ReadOnlySpan<int> RowStarts => _rowStarts;

    internal byte[] OpsArray => _ops;

    internal int[] RowStartsArray => _rowStarts;

    /// <summary>Decodes a raw frame and encodes it in prepared form.</summary>
    /// <remarks>C: the per-frame body of PrepareShapeRLEData (0x440D50, gr.c).</remarks>
    public static PreparedFrame Prepare(ShapeTable shape, int frame)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ShapeExtents e = shape.GetExtents(frame);
        int width = unchecked((short)(e.Left + e.Right + 1));
        int height = unchecked((short)(e.Top + e.Bottom + 1));
        int area = Math.Max(width, 0) * Math.Max(height, 0);
        byte[] bitmap = ArrayPool<byte>.Shared.Rent(Math.Max(area, 1));
        try
        {
            Span<byte> pixels = bitmap.AsSpan(0, area);
            pixels.Fill(0xFF);
            if (area > 0)
                ShapeFrameDecoder.DecodeRowStream(shape.GetFramePixels(frame).Span, $"{shape.Name} frame {frame}",
                    pixels, width, height, e.Left, e.Top);
            return Encode(pixels, width, height, e.Left, e.Top);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bitmap);
        }
    }

    /// <summary>
    /// Encodes a <paramref name="width"/> x <paramref name="height"/> bitmap (0xFF = transparent):
    /// literal runs of up to 127 opaque pixels, skip runs of up to 255 transparent pixels, every
    /// row terminated by 0x00 (also trailing transparent pixels are encoded as a skip).
    /// </summary>
    /// <remarks>C: the encoder loop of PrepareShapeRLEData (0x440D50, gr.c).</remarks>
    public static PreparedFrame Encode(ReadOnlySpan<byte> bitmap, int width, int height, int leftExtent, int topExtent)
    {
        int rows = Math.Max(height, 0);
        int columns = Math.Max(width, 0);
        var rowStarts = new int[rows];
        byte[] buffer = ArrayPool<byte>.Shared.Rent(2 * columns * rows + rows + 1);
        try
        {
            int output = 0;
            for (int row = 0; row < rows; row++)
            {
                rowStarts[row] = output;
                ReadOnlySpan<byte> line = bitmap.Slice(row * columns, columns);
                int pixel = 0;
                int remaining = columns;
                while (remaining > 0)
                {
                    int runLength = 0;
                    if (line[pixel] != 0xFF)
                    {
                        int code = output++;
                        while (remaining > 0 && runLength < 0x7F && line[pixel] != 0xFF)
                        {
                            buffer[output++] = line[pixel++];
                            runLength++;
                            remaining--;
                        }
                        buffer[code] = (byte)(runLength * 2 + 1);
                    }
                    else
                    {
                        while (remaining > 0 && runLength < 0xFF && line[pixel] == 0xFF)
                        {
                            pixel++;
                            runLength++;
                            remaining--;
                        }
                        buffer[output++] = 1;
                        buffer[output++] = (byte)runLength;
                    }
                }
                buffer[output++] = 0;
            }
            return new PreparedFrame(width, height, leftExtent, topExtent, buffer.AsSpan(0, output).ToArray(), rowStarts);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Writes the opaque pixels into a <see cref="Width"/> x <see cref="Height"/> bitmap (bitmap
    /// (c, r) = frame pixel (Left + c, Top + r)), translating them through
    /// <paramref name="translation"/> when it is not empty. Transparent pixels are left untouched,
    /// so callers pre-fill with 0xFF.
    /// </summary>
    /// <remarks>C: DrawRLEImage / DrawRLEImageColor into the scratch clip of RotateRLEImage.</remarks>
    public void DecodeTo(Span<byte> bitmap, ReadOnlySpan<byte> translation)
    {
        if (Width <= 0 || Height <= 0)
            return;
        bool translate = !translation.IsEmpty;
        byte[] ops = _ops;
        for (int row = 0; row < Height; row++)
        {
            int p = _rowStarts[row];
            int column = 0;
            int rowBase = row * Width;
            while (true)
            {
                byte op = ops[p++];
                if (op == 0)
                    break;
                if (op == 1)
                {
                    column += ops[p++];
                    continue;
                }
                int count = op >> 1;
                int start = Math.Max(column, 0);
                int end = Math.Min(column + count, Width);
                if ((op & 1) != 0)
                {
                    for (int c = start; c < end; c++)
                    {
                        byte v = ops[p + c - column];
                        bitmap[rowBase + c] = translate ? translation[v] : v;
                    }
                    p += count;
                }
                else
                {
                    byte v = ops[p++];
                    if (translate)
                        v = translation[v];
                    for (int c = start; c < end; c++)
                        bitmap[rowBase + c] = v;
                }
                column += count;
            }
        }
    }
}
