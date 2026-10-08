using WingCommander.Core.Video;

namespace WingCommander.Core.Rendering;

/// <summary>Destination rectangle of the 320x200 image inside the drawable, in pixels.</summary>
public readonly record struct PresentationRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// Letterbox math shared by every renderer and by the host's mouse mapping, so the pointer
/// always lands on the pixel that is displayed under it.
/// </summary>
public static class PresentationLayout
{
    /// <summary>Computes where the frame goes in a drawable of the given pixel size.</summary>
    public static PresentationRect Compute(int drawableWidth, int drawableHeight, AspectMode aspect, bool integerScaling)
    {
        if (drawableWidth <= 0 || drawableHeight <= 0)
            return default;

        // Target aspect: 4:3 (320x240 logical) or square pixels (320x200).
        int logicalWidth = Framebuffer.Width;
        int logicalHeight = aspect == AspectMode.FourByThree ? 240 : Framebuffer.Height;

        int width, height;
        if (integerScaling)
        {
            int scale = Math.Max(1, Math.Min(drawableWidth / logicalWidth, drawableHeight / logicalHeight));
            width = logicalWidth * scale;
            height = logicalHeight * scale;
            if (width > drawableWidth || height > drawableHeight)
                (width, height) = Fit(drawableWidth, drawableHeight, logicalWidth, logicalHeight);
        }
        else
        {
            (width, height) = Fit(drawableWidth, drawableHeight, logicalWidth, logicalHeight);
        }
        return new PresentationRect((drawableWidth - width) / 2, (drawableHeight - height) / 2, width, height);
    }

    /// <summary>Maps a drawable pixel position to frame coordinates (clamped to 0..319 / 0..199).</summary>
    public static (int X, int Y) ToFrame(PresentationRect rect, float pixelX, float pixelY)
    {
        if (rect.IsEmpty)
            return (0, 0);
        int x = (int)MathF.Floor((pixelX - rect.X) * Framebuffer.Width / rect.Width);
        int y = (int)MathF.Floor((pixelY - rect.Y) * Framebuffer.Height / rect.Height);
        return (Math.Clamp(x, 0, Framebuffer.Width - 1), Math.Clamp(y, 0, Framebuffer.Height - 1));
    }

    /// <summary>Maps a frame coordinate to the centre of its displayed pixel in drawable pixels.</summary>
    public static (float X, float Y) FromFrame(PresentationRect rect, int frameX, int frameY) =>
        (rect.X + (frameX + 0.5f) * rect.Width / Framebuffer.Width,
         rect.Y + (frameY + 0.5f) * rect.Height / Framebuffer.Height);

    private static (int Width, int Height) Fit(int availableWidth, int availableHeight, int logicalWidth, int logicalHeight)
    {
        // Largest rectangle with the logical aspect that fits (the reference's SdlCalculateOutputViewport).
        if ((long)availableWidth * logicalHeight > (long)availableHeight * logicalWidth)
            return ((int)((long)availableHeight * logicalWidth / logicalHeight), availableHeight);
        return (availableWidth, (int)((long)availableWidth * logicalHeight / logicalWidth));
    }
}
