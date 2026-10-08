using WingCommander.Core.Video;

namespace WingCommander.Core.Rendering;

/// <summary>A rectangle in logical 320x200 screen pixels (the classic frame's coordinates).</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    /// <summary>The whole 320x200 screen.</summary>
    public static ScreenRect Full => new(0, 0, Framebuffer.Width, Framebuffer.Height);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;

    /// <summary>The part of this rectangle that lies on the 320x200 screen.</summary>
    public ScreenRect ClipToScreen()
    {
        int x0 = Math.Clamp(X, 0, Framebuffer.Width), y0 = Math.Clamp(Y, 0, Framebuffer.Height);
        int x1 = Math.Clamp(X + Width, 0, Framebuffer.Width), y1 = Math.Clamp(Y + Height, 0, Framebuffer.Height);
        return new ScreenRect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }
}
