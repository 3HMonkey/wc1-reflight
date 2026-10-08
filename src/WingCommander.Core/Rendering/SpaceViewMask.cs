using WingCommander.Core.Video;

namespace WingCommander.Core.Rendering;

/// <summary>
/// Which screen pixels belong to the space window (non-zero) and which to the cockpit art (zero):
/// the original's view geometry run list (PCSHIP section 6) as a 320x200 mask. Space sprites are
/// only drawn inside it. <see cref="Version"/> changes on every write so renderers re-upload.
/// </summary>
public sealed class SpaceViewMask
{
    private readonly byte[] _mask = new byte[Framebuffer.PixelCount];

    /// <summary>320x200 bytes, row-major, 1 = space window, 0 = cockpit.</summary>
    public ReadOnlySpan<byte> Mask => _mask;

    public int Version { get; private set; }

    public bool this[int x, int y] => _mask[y * Framebuffer.Width + x] != 0;

    /// <summary>Marks the whole screen as window (<paramref name="window"/> true) or as cockpit.</summary>
    public void SetAll(bool window)
    {
        Array.Fill(_mask, window ? (byte)1 : (byte)0);
        Version++;
    }

    /// <summary>Marks a rectangle (clipped to the screen).</summary>
    public void SetRect(ScreenRect rect, bool window)
    {
        ScreenRect r = rect.ClipToScreen();
        for (int y = r.Y; y < r.Bottom; y++)
            _mask.AsSpan(y * Framebuffer.Width + r.X, r.Width).Fill(window ? (byte)1 : (byte)0);
        Version++;
    }

    /// <summary>
    /// Marks one view geometry run as window: <paramref name="length"/> pixels from screen
    /// (<paramref name="x"/>, <paramref name="y"/>), continuing into the following rows like the
    /// original's linear copy runs. Clipped to the screen.
    /// </summary>
    public void SetRun(int x, int y, int length)
    {
        long start = (long)y * Framebuffer.Width + x;
        long end = Math.Min(start + Math.Max(0, length), Framebuffer.PixelCount);
        start = Math.Max(start, 0);
        if (end > start)
            _mask.AsSpan((int)start, (int)(end - start)).Fill(1);
        Version++;
    }
}
