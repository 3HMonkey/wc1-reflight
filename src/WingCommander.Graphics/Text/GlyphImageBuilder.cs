using System.Numerics;
using WingCommander.Core.Imaging;
using WingCommander.Core.Rendering;

namespace WingCommander.Graphics.Text;

/// <summary>
/// Turns FONTS.FNT glyphs into <see cref="GlyphImage"/>s for output-resolution text (ADR-013):
/// the outline of the glyph's foreground (ink and fixed colours; background and 0xFF are empty)
/// is vectorized with <see cref="PixelOutline"/> - one-pixel staircases become diagonals, stroke
/// ends and real corners stay square - and stored as a signed distance field, so edges are crisp
/// at any magnification while the letters keep the original design.
/// </summary>
public static class GlyphImageBuilder
{
    /// <summary>Builds the image of <paramref name="character"/> in <paramref name="font"/>.</summary>
    /// <param name="smoothing">Optional Gaussian blur of the distance field in source pixels (0 = exact outline).</param>
    public static GlyphImage Build(BitmapFont font, byte character, float smoothing = 0f)
    {
        ArgumentNullException.ThrowIfNull(font);
        int width = font.GetWidth(character);
        int height = font.Height;
        ReadOnlySpan<byte> glyph = font.GetGlyph(character);
        if (glyph.Length < width * height)
            width = 0; // absent or out of range: nothing is drawn, like DrawFontGlyph
        const int scale = GlyphImage.FieldScale;
        const int padding = GlyphImage.FieldPadding;
        int fieldWidth = width * scale + 2 * padding;
        int fieldHeight = height * scale + 2 * padding;
        var texels = new byte[fieldWidth * fieldHeight * GlyphImage.BytesPerTexel];

        var mask = new bool[width * height];
        bool any = false, multicolour = false;
        for (int i = 0; i < width * height; i++)
        {
            byte value = glyph[i];
            mask[i] = value != 0xFF && value != font.BackgroundIndex;
            any |= mask[i];
            multicolour |= mask[i] && value != font.InkIndex;
        }
        if (!any)
        {
            for (int i = 0; i < fieldWidth * fieldHeight; i++)
                texels[i * 2 + 1] = font.InkIndex; // distance 0 = far outside
            return new GlyphImage(width, height, font.GetWidth(character), font.InkIndex, hasForeground: false,
                multicolour: false, texels);
        }

        var polygons = new List<IReadOnlyList<Vector2>>();
        foreach (var contour in PixelOutline.Trace(mask, width, height))
            polygons.Add(PixelOutline.Smooth(contour));
        var distance = new float[fieldWidth * fieldHeight];
        float origin = -(float)padding / scale;
        PixelOutline.SignedDistance(polygons, fieldWidth, fieldHeight, scale, origin, origin, distance,
            limit: GlyphImage.FieldSpread + 2f);
        if (smoothing > 0f)
            GaussianBlur(distance, fieldWidth, fieldHeight, smoothing * scale);

        byte[] colours = NearestForegroundColours(glyph, mask, width, height, font.InkIndex);
        int paddedWidth = width + 2;
        for (int y = 0; y < fieldHeight; y++)
        {
            int sourceY = (int)MathF.Floor(origin + (y + 0.5f) / scale) + 1;
            for (int x = 0; x < fieldWidth; x++)
            {
                int sourceX = (int)MathF.Floor(origin + (x + 0.5f) / scale) + 1;
                int t = (y * fieldWidth + x) * GlyphImage.BytesPerTexel;
                texels[t] = GlyphImage.EncodeDistance(distance[y * fieldWidth + x]);
                texels[t + 1] = colours[sourceY * paddedWidth + sourceX];
            }
        }
        return new GlyphImage(width, height, font.GetWidth(character), font.InkIndex, hasForeground: true,
            multicolour, texels);
    }

    /// <summary>Builds every glyph of <paramref name="font"/> that is not cached yet (characters 0..255).</summary>
    public static void AddFont(GlyphImageCache cache, BitmapFont font)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(font);
        if (font.Index < 0)
            throw new ArgumentException("The font has no FONTS.FNT index.", nameof(font));
        for (int c = 0; c < 256; c++)
        {
            var key = new GlyphKey((byte)font.Index, (byte)c);
            if (!cache.Contains(key))
                cache.Set(key, Build(font, (byte)c));
        }
    }

    /// <summary>
    /// Colours on the glyph grid with a one-pixel border ((width + 2) x (height + 2)): foreground
    /// pixels keep theirs, every other pixel takes the colour of a nearest foreground pixel
    /// (breadth-first, 8-neighbourhood), so the smoothed outline never reaches an undefined colour.
    /// </summary>
    private static byte[] NearestForegroundColours(ReadOnlySpan<byte> glyph, bool[] mask, int width, int height, byte ink)
    {
        int paddedWidth = width + 2, paddedHeight = height + 2;
        var colours = new byte[paddedWidth * paddedHeight];
        var known = new bool[colours.Length];
        var queue = new Queue<int>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!mask[y * width + x])
                    continue;
                int p = (y + 1) * paddedWidth + x + 1;
                colours[p] = glyph[y * width + x];
                known[p] = true;
                queue.Enqueue(p);
            }
        }
        while (queue.Count > 0)
        {
            int p = queue.Dequeue();
            int px = p % paddedWidth, py = p / paddedWidth;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = px + dx, ny = py + dy;
                    if ((uint)nx >= (uint)paddedWidth || (uint)ny >= (uint)paddedHeight)
                        continue;
                    int n = ny * paddedWidth + nx;
                    if (known[n])
                        continue;
                    known[n] = true;
                    colours[n] = colours[p];
                    queue.Enqueue(n);
                }
            }
        }
        for (int i = 0; i < colours.Length; i++)
        {
            if (!known[i])
                colours[i] = ink;
        }
        return colours;
    }

    /// <summary>Separable Gaussian blur with clamped edges (sigma in texels).</summary>
    private static void GaussianBlur(float[] values, int width, int height, float sigma)
    {
        int radius = Math.Max(1, (int)MathF.Ceiling(sigma * 3f));
        var kernel = new float[radius * 2 + 1];
        float sum = 0f;
        for (int i = -radius; i <= radius; i++)
        {
            float k = MathF.Exp(-(i * i) / (2f * sigma * sigma));
            kernel[i + radius] = k;
            sum += k;
        }
        for (int i = 0; i < kernel.Length; i++)
            kernel[i] /= sum;

        var temp = new float[values.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float acc = 0f;
                for (int k = -radius; k <= radius; k++)
                    acc += kernel[k + radius] * values[y * width + Math.Clamp(x + k, 0, width - 1)];
                temp[y * width + x] = acc;
            }
        }
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float acc = 0f;
                for (int k = -radius; k <= radius; k++)
                    acc += kernel[k + radius] * temp[Math.Clamp(y + k, 0, height - 1) * width + x];
                values[y * width + x] = acc;
            }
        }
    }
}
