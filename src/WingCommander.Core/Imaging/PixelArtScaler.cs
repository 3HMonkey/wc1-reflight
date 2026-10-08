namespace WingCommander.Core.Imaging;

/// <summary>
/// Magnification of indexed pixel art that keeps edges hard and turns one-pixel staircases into
/// diagonals: EPX / Scale2x (each pixel becomes 2x2; a sub-pixel takes the colour of its two
/// outer neighbours when they agree and the opposite neighbours differ). Colours are compared by
/// value, so any byte can mark "empty". Used to depixelize font glyphs (ADR-013).
/// </summary>
public static class PixelArtScaler
{
    /// <summary>
    /// Scales <paramref name="source"/> (<paramref name="width"/> x <paramref name="height"/>) by 2
    /// into <paramref name="destination"/> (2w x 2h). Pixels outside the image read as
    /// <paramref name="outside"/>.
    /// </summary>
    public static void Scale2x(ReadOnlySpan<byte> source, int width, int height, Span<byte> destination, byte outside)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (source.Length < width * height)
            throw new ArgumentException("The source is smaller than width * height.", nameof(source));
        if (destination.Length < width * height * 4)
            throw new ArgumentException("The destination needs (2 * width) * (2 * height) bytes.", nameof(destination));
        int stride = width * 2;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte p = source[y * width + x];
                byte a = y > 0 ? source[(y - 1) * width + x] : outside;          // up
                byte b = x + 1 < width ? source[y * width + x + 1] : outside;   // right
                byte c = x > 0 ? source[y * width + x - 1] : outside;           // left
                byte d = y + 1 < height ? source[(y + 1) * width + x] : outside; // down
                int top = y * 2 * stride + x * 2;
                destination[top] = c == a && c != d && a != b ? a : p;
                destination[top + 1] = a == b && a != c && b != d ? b : p;
                destination[top + stride] = d == c && d != b && c != a ? c : p;
                destination[top + stride + 1] = b == d && b != a && d != c ? d : p;
            }
        }
    }

    /// <summary>
    /// Applies <see cref="Scale2x"/> <paramref name="passes"/> times (factor 2^passes) and returns
    /// the result; <paramref name="scaledWidth"/>/<paramref name="scaledHeight"/> give its size.
    /// </summary>
    public static byte[] Scale2xRepeated(ReadOnlySpan<byte> source, int width, int height, int passes, byte outside,
        out int scaledWidth, out int scaledHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(passes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(passes, 6);
        byte[] current = source[..(width * height)].ToArray();
        int w = width, h = height;
        for (int pass = 0; pass < passes; pass++)
        {
            var next = new byte[w * h * 4];
            Scale2x(current, w, h, next, outside);
            current = next;
            w *= 2;
            h *= 2;
        }
        scaledWidth = w;
        scaledHeight = h;
        return current;
    }
}
