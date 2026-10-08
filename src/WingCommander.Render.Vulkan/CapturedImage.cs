namespace WingCommander.Render.Vulkan;

/// <summary>A rendered frame read back from the GPU: RGBA, 8 bits per channel, top row first.</summary>
public sealed class CapturedImage
{
    internal CapturedImage(int width, int height, byte[] rgba, long frameNumber)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
        FrameNumber = frameNumber;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Width * Height * 4 bytes: R, G, B, A per pixel (alpha is always 255).</summary>
    public byte[] Rgba { get; }

    /// <summary>
    /// Zero-based number of the captured frame: the value of
    /// <see cref="VulkanRendererStatistics.FramesRendered"/> before that frame was submitted.
    /// </summary>
    public long FrameNumber { get; }

    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is outside {Width}x{Height}.");
        int o = (y * Width + x) * 4;
        return (Rgba[o], Rgba[o + 1], Rgba[o + 2], Rgba[o + 3]);
    }

    /// <summary>The pixel packed as 0xRRGGBB (alpha dropped), convenient for comparisons.</summary>
    public int GetRgb(int x, int y)
    {
        var (r, g, b, _) = GetPixel(x, y);
        return (r << 16) | (g << 8) | b;
    }
}
