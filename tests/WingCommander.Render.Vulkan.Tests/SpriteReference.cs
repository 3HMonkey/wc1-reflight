using WingCommander.Core.Rendering;
using WingCommander.Core.Video;
using WingCommander.Render.Vulkan.Internal;

namespace WingCommander.Render.Vulkan.Tests;

/// <summary>A classic layer plus a space view with synthetic sprite images.</summary>
public sealed class SpriteScene
{
    public SpriteScene()
    {
        Palette = TestPatterns.DistinctPalette();
        Layer = new ClassicLayer(new Framebuffer(), Palette);
        Layer.Pixels.Clear(SpaceView.DefaultBackgroundIndex);
        Layer.MarkPixelsChanged();
        Images = new SpriteImageCache();
        Space = new SpaceView(Images);
        Frame = new RenderFrame(Layer) { Space = Space };
    }

    public Palette Palette { get; }

    public ClassicLayer Layer { get; }

    public SpriteImageCache Images { get; }

    public SpaceView Space { get; }

    public RenderFrame Frame { get; }

    /// <summary>
    /// Adds a test image: index (10 + 3i + 17j) mod 240 + 1 (never 0xBF... checked below, never
    /// 255), with transparent pixels where (i + 2j) % 5 == 4, so every pixel position is unique
    /// enough to detect a one-texel offset.
    /// </summary>
    public SpriteImageKey AddImage(int id, int width, int height, int originX, int originY, int seed = 0)
    {
        var pixels = new byte[width * height];
        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width; i++)
            {
                byte index = (byte)((10 + 3 * i + 17 * j + seed * 29) % 240 + 1);
                if (index == SpaceView.DefaultBackgroundIndex)
                    index++;
                if ((i + 2 * j + seed) % 5 == 4)
                    index = SpriteImage.TransparentIndex;
                pixels[j * width + i] = index;
            }
        }
        var key = SpriteImageKey.Create(3, id, 0);
        Images.Set(key, new SpriteImage(width, height, originX, originY, pixels));
        return key;
    }

    public ref SpriteInstance Add(SpriteImageKey image, float x, float y, float angle = 0, float scale = 1, SpriteFlip flip = SpriteFlip.None)
    {
        ref SpriteInstance sprite = ref Space.Sprites.Add();
        sprite.Image = image;
        sprite.X = x;
        sprite.Y = y;
        sprite.Angle = angle;
        sprite.Scale = scale;
        sprite.Flip = flip;
        return ref sprite;
    }
}

/// <summary>
/// CPU reference of the classic layer + nearest-filtered sprites at output resolution, using the
/// definitions of the contract (see <c>SpriteInstance</c>) in double precision. Pixels whose sample
/// point lies within <c>Epsilon</c> of a texel or quad edge are reported as ambiguous.
/// </summary>
public static class SpriteReference
{
    public const double Epsilon = 0.01;

    /// <summary>Expected 0xRRGGBB of output pixel (px, py), or null when ambiguous.</summary>
    public static int? Expected(SpriteScene scene, PresentationRect rect, int px, int py)
    {
        if (px < rect.X || py < rect.Y || px >= rect.X + rect.Width || py >= rect.Y + rect.Height)
            return 0;
        double qx = (px + 0.5 - rect.X) * Framebuffer.Width / rect.Width;
        double qy = (py + 0.5 - rect.Y) * Framebuffer.Height / rect.Height;
        int cx = (int)Math.Floor(qx), cy = (int)Math.Floor(qy);
        byte classic = scene.Layer.Pixels[cx, cy];
        int colour = TestPatterns.Rgb(scene.Palette, classic);

        SpaceView space = scene.Space;
        bool replaceable = space.Sprites.Clip.Contains(cx, cy)
            && (space.WindowMask is null || space.WindowMask[cx, cy])
            && classic == space.BackgroundIndex;
        if (!replaceable)
            return colour;

        bool ambiguous = false;
        foreach (ref readonly SpriteInstance recorded in space.Sprites.Items)
        {
            if (!space.Images.TryGet(recorded.Image, out SpriteImage? image) || image.IsEmpty)
                continue;
            SpriteInstance sprite = recorded.At(scene.Frame.Interpolation); // between the ticks (R2b)
            var (cos, sin) = SpriteTrig.Get(sprite.Angle);
            double fx = (sprite.Flip & SpriteFlip.Horizontal) != 0 ? -sprite.Scale : sprite.Scale;
            double fy = (sprite.Flip & SpriteFlip.Vertical) != 0 ? -sprite.VerticalScale : sprite.VerticalScale;
            // Columns of M: image x axis (cos fx, sin fx), image y axis (-sin fy, cos fy).
            double a = cos * fx, c = sin * fx, b = -sin * fy, d = cos * fy;
            double det = a * d - b * c;
            double dx = qx - (sprite.X + 0.5), dy = qy - (sprite.Y + 0.5);
            double lx = (d * dx - b * dy) / det;
            double ly = (-c * dx + a * dy) / det;
            double u = lx + image.OriginX + 0.5, v = ly + image.OriginY + 0.5;
            if (u < -Epsilon || v < -Epsilon || u > image.Width + Epsilon || v > image.Height + Epsilon)
                continue;
            if (Near(u) || Near(v) || u < Epsilon || v < Epsilon || u > image.Width - Epsilon || v > image.Height - Epsilon)
            {
                ambiguous = true;
                continue;
            }
            int tx = (int)Math.Floor(u), ty = (int)Math.Floor(v);
            byte index = image.Pixels.Span[ty * image.Width + tx];
            if (index != SpriteImage.TransparentIndex)
                colour = TestPatterns.Rgb(scene.Palette, index);
        }
        return ambiguous ? null : colour;
    }

    /// <summary>Compares every output pixel; returns (mismatches, ambiguous, first mismatch).</summary>
    public static (int Mismatches, int Ambiguous, string? First) Compare(SpriteScene scene, CapturedImage image, PresentationRect rect)
    {
        int mismatches = 0, ambiguous = 0;
        string? first = null;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int? expected = Expected(scene, rect, x, y);
                if (expected is null)
                {
                    ambiguous++;
                    continue;
                }
                int actual = image.GetRgb(x, y);
                if (actual != expected.Value)
                {
                    mismatches++;
                    first ??= $"output ({x},{y}): expected {expected.Value:x6}, got {actual:x6}";
                }
            }
        }
        return (mismatches, ambiguous, first);
    }

    private static bool Near(double value)
    {
        double f = value - Math.Floor(value);
        return f < Epsilon || f > 1 - Epsilon;
    }
}
