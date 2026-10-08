using WingCommander.Core.Rendering;
using WingCommander.Core.Video;

namespace WingCommander.Render.Vulkan.Tests;

/// <summary>Known indexed pictures and palettes, and checks of captured output against them.</summary>
public static class TestPatterns
{
    /// <summary>
    /// Index at (x, y): horizontally adjacent pixels differ by 31..71, vertically by 17..58 (mod
    /// 256 never 0), so a one-pixel mapping error always changes the colour.
    /// </summary>
    public static byte Index(int x, int y) => (byte)((x * 31 + y * 17 + ((x * y) >> 3)) & 255);

    public static Framebuffer Pattern()
    {
        var frame = new Framebuffer();
        for (int y = 0; y < Framebuffer.Height; y++)
        {
            for (int x = 0; x < Framebuffer.Width; x++)
                frame[x, y] = Index(x, y);
        }
        return frame;
    }

    /// <summary>A pattern that depends on <paramref name="seed"/> (frames-in-flight tests).</summary>
    public static void FillSeeded(Framebuffer frame, int seed)
    {
        for (int y = 0; y < Framebuffer.Height; y++)
        {
            for (int x = 0; x < Framebuffer.Width; x++)
                frame[x, y] = SeededIndex(x, y, seed);
        }
    }

    public static byte SeededIndex(int x, int y, int seed) => (byte)((Index(x, y) + seed * 5) & 255);

    /// <summary>256 distinct colours, none of them black (black means "letterbox bar").</summary>
    public static Palette DistinctPalette()
    {
        var palette = new Palette();
        for (int i = 0; i < Palette.EntryCount; i++)
            palette.SetEntry(i, (byte)i, (byte)(255 - i), (byte)((i * 7 + 64) & 255));
        return palette;
    }

    public static int Rgb(Palette palette, int index)
    {
        var (r, g, b) = palette.GetEntry(index);
        return (r << 16) | (g << 8) | b;
    }

    public static RenderFrame Frame(Framebuffer pixels, Palette palette)
    {
        var layer = new ClassicLayer(new Framebuffer(), palette);
        layer.Present(pixels);
        return new RenderFrame(layer);
    }

    /// <summary>
    /// For every source pixel, the output pixel under its displayed centre must show exactly its
    /// palette colour (valid for magnification, any filter that is exact at texel centres).
    /// Returns the number of mismatches and the first one.
    /// </summary>
    public static (int Mismatches, string? First) CompareAtPixelCentres(CapturedImage image, PresentationRect rect, Func<int, int, int> expectedRgb, int tolerance = 0)
    {
        int mismatches = 0;
        string? first = null;
        for (int y = 0; y < Framebuffer.Height; y++)
        {
            for (int x = 0; x < Framebuffer.Width; x++)
            {
                var (cx, cy) = PresentationLayout.FromFrame(rect, x, y);
                int px = (int)MathF.Floor(cx), py = (int)MathF.Floor(cy);
                int actual = image.GetRgb(px, py);
                int expected = expectedRgb(x, y);
                if (!Close(actual, expected, tolerance))
                {
                    mismatches++;
                    first ??= $"source ({x},{y}) -> output ({px},{py}): expected {expected:x6}, got {actual:x6}";
                }
            }
        }
        return (mismatches, first);
    }

    public static bool Close(int a, int b, int tolerance)
    {
        for (int shift = 0; shift <= 16; shift += 8)
        {
            int ca = (a >> shift) & 255, cb = (b >> shift) & 255;
            if (Math.Abs(ca - cb) > tolerance)
                return false;
        }
        return true;
    }

    /// <summary>Bounding box of all non-black pixels: the drawn picture when the palette has no black.</summary>
    public static PresentationRect NonBlackBounds(CapturedImage image)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (image.GetRgb(x, y) == 0)
                    continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }
        return maxX < 0 ? default : new PresentationRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>Pixels outside <paramref name="rect"/> that are not opaque black.</summary>
    public static int NonBlackOutside(CapturedImage image, PresentationRect rect)
    {
        int count = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                bool inside = x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height;
                if (inside)
                    continue;
                var (r, g, b, a) = image.GetPixel(x, y);
                if (r != 0 || g != 0 || b != 0 || a != 255)
                    count++;
            }
        }
        return count;
    }

    /// <summary>Pixels inside <paramref name="rect"/> whose colour is not one of the palette's.</summary>
    public static int NonPaletteColours(CapturedImage image, PresentationRect rect, Palette palette)
    {
        var colours = new HashSet<int>();
        for (int i = 0; i < Palette.EntryCount; i++)
            colours.Add(Rgb(palette, i));
        int count = 0;
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            for (int x = rect.X; x < rect.X + rect.Width; x++)
            {
                if (!colours.Contains(image.GetRgb(x, y)))
                    count++;
            }
        }
        return count;
    }
}
