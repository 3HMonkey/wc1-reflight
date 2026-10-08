namespace WingCommander.Core.Video;

/// <summary>
/// The 320x200 8-bit indexed frame the game renders into (VGA mode 13h). Pixels are
/// stored row-major, one byte (palette index) per pixel. This is the only image the
/// host ever sees; everything else (viewports, off-screen buffers) lives in the game.
/// </summary>
public sealed class Framebuffer
{
    public const int Width = 320;
    public const int Height = 200;
    public const int PixelCount = Width * Height;

    public Framebuffer()
    {
        Pixels = new byte[PixelCount];
    }

    public byte[] Pixels { get; }

    public Span<byte> Row(int y) => Pixels.AsSpan(y * Width, Width);

    public byte this[int x, int y]
    {
        get => Pixels[y * Width + x];
        set => Pixels[y * Width + x] = value;
    }

    public void Clear(byte colour) => Pixels.AsSpan().Fill(colour);

    public void CopyTo(Framebuffer destination) => Pixels.AsSpan().CopyTo(destination.Pixels);

    /// <summary>
    /// Expands the indexed frame into 32-bit 0xAARRGGBB pixels using the palette's packed
    /// table. Used by hosts to upload a texture.
    /// </summary>
    public void ToArgb(ReadOnlySpan<uint> paletteArgb, Span<uint> destination)
    {
        if (paletteArgb.Length < 256)
            throw new ArgumentException("Palette needs 256 entries.", nameof(paletteArgb));
        if (destination.Length < PixelCount)
            throw new ArgumentException("Destination too small.", nameof(destination));
        ReadOnlySpan<byte> src = Pixels;
        for (int i = 0; i < src.Length; i++)
            destination[i] = paletteArgb[src[i]];
    }
}
