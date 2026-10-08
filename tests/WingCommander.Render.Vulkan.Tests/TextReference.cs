using System.Numerics;
using WingCommander.Core.Imaging;
using WingCommander.Core.Rendering;
using WingCommander.Core.Video;

namespace WingCommander.Render.Vulkan.Tests;

/// <summary>
/// Glyph images built without game data the way Graphics' <c>GlyphImageBuilder</c> builds them:
/// the foreground outline vectorized with <see cref="PixelOutline"/> (Trace + Smooth), its signed
/// distance at 8 texels per source pixel, and per texel the colour of the source pixel (outside
/// the foreground: the nearest foreground colour).
/// </summary>
public static class SyntheticGlyphs
{
    public const byte Ink = 15;

    /// <summary>
    /// A glyph from rows of characters: '.' = not foreground, '#' = ink, any other character = the
    /// fixed palette index <paramref name="colours"/> gives it (makes the glyph multicolour).
    /// </summary>
    public static GlyphImage Build(string[] rows, IReadOnlyDictionary<char, byte>? colours = null, int advance = -1)
    {
        int height = rows.Length;
        int width = height == 0 ? 0 : rows[0].Length;
        var indices = new byte[width * height];
        var mask = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            Assert.Equal(width, rows[y].Length);
            for (int x = 0; x < width; x++)
            {
                char c = rows[y][x];
                if (c == '.')
                    continue;
                mask[y * width + x] = true;
                indices[y * width + x] = c == '#' ? Ink : colours![c];
            }
        }
        return Encode(mask, indices, width, height, advance < 0 ? width + 1 : advance);
    }

    /// <summary>A filled rectangle with a rectangular hole (stress and atlas tests: few edges, quick to build).</summary>
    public static GlyphImage Ring(int width, int height, int seed)
    {
        var mask = new bool[width * height];
        var indices = new byte[width * height];
        int holeX = 2 + seed % Math.Max(1, width - 6), holeY = 2 + seed / 3 % Math.Max(1, height - 6);
        int holeWidth = 2 + seed % 3, holeHeight = 2 + seed / 2 % 3;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool hole = x >= holeX && x < holeX + holeWidth && y >= holeY && y < holeY + holeHeight;
                mask[y * width + x] = !hole;
                indices[y * width + x] = Ink;
            }
        }
        return Encode(mask, indices, width, height, width + 1);
    }

    /// <summary>A 5x8 glyph whose pixels depend on <paramref name="code"/> (an overlay test font); space has no foreground.</summary>
    public static GlyphImage FontGlyph(byte code)
    {
        const int width = 5, height = 8;
        var mask = new bool[width * height];
        var indices = new byte[width * height];
        if (code != (byte)' ')
        {
            uint bits = (uint)(code * 2654435761u) ^ 0x5A5A5A5Au;
            for (int y = 0; y < height - 1; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool frame = x == 0 || y == 0 || y == height - 2;
                    mask[y * width + x] = frame || ((bits >> ((y * width + x) % 32)) & 1) != 0;
                    indices[y * width + x] = Ink;
                }
            }
        }
        return Encode(mask, indices, width, height, code == (byte)' ' ? 4 : width + 1);
    }

    private static GlyphImage Encode(bool[] mask, byte[] indices, int width, int height, int advance)
    {
        const int scale = GlyphImage.FieldScale;
        const int padding = GlyphImage.FieldPadding;
        int fieldWidth = width * scale + 2 * padding;
        int fieldHeight = height * scale + 2 * padding;
        var texels = new byte[fieldWidth * fieldHeight * GlyphImage.BytesPerTexel];
        bool any = false, multicolour = false;
        for (int i = 0; i < mask.Length; i++)
        {
            any |= mask[i];
            multicolour |= mask[i] && indices[i] != Ink;
        }
        if (!any)
        {
            for (int i = 0; i < fieldWidth * fieldHeight; i++)
                texels[i * 2 + 1] = Ink;
            return new GlyphImage(width, height, advance, Ink, hasForeground: false, multicolour: false, texels);
        }

        var polygons = new List<IReadOnlyList<Vector2>>();
        foreach (var contour in PixelOutline.Trace(mask, width, height))
            polygons.Add(PixelOutline.Smooth(contour));
        var distance = new float[fieldWidth * fieldHeight];
        float origin = -(float)padding / scale;
        PixelOutline.SignedDistance(polygons, fieldWidth, fieldHeight, scale, origin, origin, distance, GlyphImage.FieldSpread + 2f);

        byte[] colours = NearestForegroundColours(mask, indices, width, height);
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
        return new GlyphImage(width, height, advance, Ink, hasForeground: true, multicolour, texels);
    }

    /// <summary>Colours on the grid with a one-pixel border; non-foreground pixels take a nearest foreground colour (BFS).</summary>
    private static byte[] NearestForegroundColours(bool[] mask, byte[] indices, int width, int height)
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
                colours[p] = indices[y * width + x];
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
        return colours;
    }
}

/// <summary>A classic layer plus a text layer (ADR-013) with synthetic glyphs.</summary>
public sealed class TextScene
{
    public static readonly string[] LetterA =
    [
        ".###.",
        "#...#",
        "#...#",
        "#####",
        "#...#",
        "#...#",
        "#...#",
        ".....",
    ];

    public static readonly string[] LetterG =
    [
        ".....",
        ".....",
        ".####",
        "#...#",
        "#...#",
        ".####",
        "....#",
        "####.",
    ];

    public static readonly string[] Slash =
    [
        "....#",
        "....#",
        "...#.",
        "..#..",
        "..#..",
        ".#...",
        "#....",
        "#....",
    ];

    public static readonly string[] Block =
    [
        "####",
        "####",
        "####",
        "####",
        "####",
        "####",
    ];

    /// <summary>Ink plus two fixed colours (like the chalk font's shading).</summary>
    public static readonly string[] Chalk =
    [
        "##ab#.",
        "#a..b#",
        "b....a",
        "#####b",
        "a....#",
        "#b..a#",
        ".#ab#.",
        "......",
    ];

    public static readonly Dictionary<char, byte> ChalkColours = new() { ['a'] = 100, ['b'] = 150 };

    public TextScene()
    {
        Palette = TestPatterns.DistinctPalette();
        Layer = new ClassicLayer(new Framebuffer(), Palette);
        TestPatterns.FillSeeded(Layer.Pixels, 11); // what the game drew (would include the text)
        Layer.MarkPixelsChanged();
        Glyphs = new GlyphImageCache();
        Text = new TextLayer(Glyphs);
        TestPatterns.FillSeeded(Text.Pixels, 23); // the frame without the glyphs' foreground
        Frame = new RenderFrame(Layer) { Text = Text };
    }

    public Palette Palette { get; }

    public ClassicLayer Layer { get; }

    public GlyphImageCache Glyphs { get; }

    public TextLayer Text { get; }

    public RenderFrame Frame { get; }

    public GlyphKey Set(char character, GlyphImage image, byte font = 1)
    {
        var key = new GlyphKey(font, (byte)character);
        Glyphs.Set(key, image);
        return key;
    }

    public GlyphKey Set(char character, string[] rows, IReadOnlyDictionary<char, byte>? colours = null) =>
        Set(character, SyntheticGlyphs.Build(rows, colours));

    /// <summary>Appends an instance; returns its list index.</summary>
    public int Add(int x, int y, GlyphKey glyph, byte colour) => Text.Add(new GlyphInstance((short)x, (short)y, glyph, colour));

    /// <summary>Sets the mask of a screen rectangle (clipped).</summary>
    public void SetMask(ScreenRect rect, ushort value)
    {
        ScreenRect r = rect.ClipToScreen();
        for (int y = r.Y; y < r.Bottom; y++)
            Text.Mask.AsSpan(y * Framebuffer.Width + r.X, r.Width).Fill(value);
    }

    /// <summary>Mask 1 (every instance may draw) over the cells of all instances.</summary>
    public void AllowAllCells()
    {
        foreach (GlyphInstance instance in Text.Instances)
        {
            if (Glyphs.TryGet(instance.Glyph, out GlyphImage? image))
                SetMask(new ScreenRect(instance.X, instance.Y, image.Width, image.Height), 1);
        }
    }

    public void Publish() => Text.Publish();
}

/// <summary>
/// CPU reference of the text contract (ADR-013) at output resolution: the classic pass (nearest)
/// over <see cref="TextLayer.Pixels"/>, the game text per <see cref="GlyphRasterizer"/> with the
/// mask rule and painter order, then the overlay items in target pixels, blended like the GPU
/// (premultiplied, quantized to 8 bits after every draw). Output pixels whose centre lies within
/// <see cref="Epsilon"/> output pixels of a logical pixel edge, or of the edge of a quad they would
/// visibly change, are ambiguous and not compared.
/// </summary>
public static class TextReference
{
    public const double Epsilon = 0.02;

    /// <summary>Field texels per output pixel of game text in <paramref name="rect"/>: the mean of x and y.</summary>
    public static float TexelsPerPixel(PresentationRect rect) =>
        GlyphImage.FieldScale * 0.5f * (Framebuffer.Width / (float)rect.Width + Framebuffer.Height / (float)rect.Height);

    /// <summary>The classic pass with nearest filtering: black outside the rectangle, null on a logical pixel edge.</summary>
    public static Vector3? Classic(Framebuffer pixels, Palette palette, PresentationRect rect, int px, int py)
    {
        if (!Logical(rect, px, py, out double qx, out double qy, out bool inside))
            return null;
        return inside ? PaletteColour(palette, pixels[(int)Math.Floor(qx), (int)Math.Floor(qy)]) : Vector3.Zero;
    }

    /// <summary>
    /// The game text of <paramref name="text"/> over <paramref name="below"/> (default: the classic
    /// pass over the text layer's pixels) at output pixel (px, py), or null when ambiguous.
    /// </summary>
    public static Vector3? GameText(TextLayer text, Palette palette, PresentationRect rect, int px, int py, bool linear = false,
        Func<int, int, Vector3?>? below = null)
    {
        if (!Logical(rect, px, py, out double qx, out double qy, out bool inside))
            return null;
        Vector3? start = below is null ? (inside ? PaletteColour(palette, text.Pixels[(int)Math.Floor(qx), (int)Math.Floor(qy)]) : Vector3.Zero) : below(px, py);
        if (start is not { } colour || !inside)
            return start;

        int cx = (int)Math.Floor(qx), cy = (int)Math.Floor(qy);
        ushort mask = text.Mask[cy * Framebuffer.Width + cx];
        float texelsPerPixel = TexelsPerPixel(rect);
        double ex = Epsilon * Framebuffer.Width / rect.Width, ey = Epsilon * Framebuffer.Height / rect.Height;
        ReadOnlySpan<GlyphInstance> instances = text.Instances;
        for (int i = 0; i < instances.Length; i++)
        {
            if (mask == 0 || i < mask - 1)
                continue;
            GlyphInstance instance = instances[i];
            if (!text.Glyphs.TryGet(instance.Glyph, out GlyphImage? image) || !image.HasForeground)
                continue;
            double cellX = qx - instance.X, cellY = qy - instance.Y;
            if (cellX < -ex || cellY < -ey || cellX > image.Width + ex || cellY > image.Height + ey)
                continue;
            bool nearEdge = cellX < ex || cellY < ey || cellX > image.Width - ex || cellY > image.Height - ey;
            float coverage = GlyphRasterizer.Coverage(image, (float)cellX, (float)cellY, texelsPerPixel);
            if (nearEdge)
            {
                if (coverage > 0.002f)
                    return null;
                continue;
            }
            if (coverage <= 0f)
                continue;
            Vector3 rgb = GlyphRasterizer.SampleColour(image, (float)cellX, (float)cellY, instance.Colour, palette.Rgb);
            colour = Blend(colour, rgb, coverage, linear);
        }
        return colour;
    }

    /// <summary>The overlay items of <paramref name="list"/> (target pixels) over <paramref name="below"/>, or null when ambiguous.</summary>
    public static Vector3? Overlay(OverlayDrawList list, int px, int py, Vector3 below, bool linear = false)
    {
        Vector3 colour = below;
        double x = px + 0.5, y = py + 0.5;
        foreach (ref readonly OverlayItem item in list.Items)
        {
            if (!(item.Width > 0f) || !(item.Height > 0f))
                continue;
            if (x < item.X - Epsilon || y < item.Y - Epsilon || x > item.X + item.Width + Epsilon || y > item.Y + item.Height + Epsilon)
                continue;
            bool nearEdge = x < item.X + Epsilon || y < item.Y + Epsilon || x > item.X + item.Width - Epsilon || y > item.Y + item.Height - Epsilon;
            var (r, g, b, a) = OverlayColour.Unpack(item.Colour);
            float alpha = a / 255f;
            if (item.Kind == OverlayItemKind.Glyph)
            {
                if (list.Glyphs is null || !list.Glyphs.TryGet(item.Glyph, out GlyphImage? image) || !image.HasForeground)
                    continue;
                double cellX = (x - item.X) * image.Width / item.Width, cellY = (y - item.Y) * image.Height / item.Height;
                float texelsPerPixel = GlyphImage.FieldScale * 0.5f * (image.Width / item.Width + image.Height / item.Height);
                alpha *= GlyphRasterizer.Coverage(image, (float)cellX, (float)cellY, texelsPerPixel);
            }
            if (nearEdge)
            {
                if (alpha > 0.002f)
                    return null;
                continue;
            }
            if (alpha <= 0f)
                continue;
            colour = Blend(colour, new Vector3(r, g, b), alpha, linear);
        }
        return colour;
    }

    /// <summary>
    /// Compares every output pixel with <paramref name="expected"/> (null = ambiguous, skipped).
    /// Returns the mismatches, the ambiguous pixels, the compared pixels that differ from
    /// <paramref name="background"/> (i.e. show text) and the first mismatch.
    /// </summary>
    public static (int Mismatches, int Ambiguous, int Changed, string? First) Compare(CapturedImage image, Func<int, int, Vector3?> expected,
        int tolerance, Func<int, int, Vector3?>? background = null)
    {
        int mismatches = 0, ambiguous = 0, changed = 0;
        string? first = null;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (expected(x, y) is not { } colour)
                {
                    ambiguous++;
                    continue;
                }
                int want = Rgb(colour);
                int actual = image.GetRgb(x, y);
                if (!TestPatterns.Close(actual, want, tolerance))
                {
                    mismatches++;
                    first ??= $"output ({x},{y}): expected {want:x6}, got {actual:x6}";
                }
                if (background?.Invoke(x, y) is { } plain && !TestPatterns.Close(Rgb(plain), want, 2))
                    changed++;
            }
        }
        return (mismatches, ambiguous, changed, first);
    }

    public static int Rgb(Vector3 colour) =>
        (int)MathF.Round(colour.X) << 16 | (int)MathF.Round(colour.Y) << 8 | (int)MathF.Round(colour.Z);

    public static Vector3 PaletteColour(Palette palette, int index)
    {
        var (r, g, b) = palette.GetEntry(index);
        return new Vector3(r, g, b);
    }

    /// <summary>One premultiplied draw over an 8-bit target; <paramref name="linear"/>: an sRGB target (blending in linear light).</summary>
    public static Vector3 Blend(Vector3 below, Vector3 colour, float alpha, bool linear)
    {
        if (!linear)
            return Round(below * (1f - alpha) + colour * alpha);
        Vector3 blended = ToLinear(below / 255f) * (1f - alpha) + ToLinear(colour / 255f) * alpha;
        return Round(ToSrgb(blended) * 255f);
    }

    private static bool Logical(PresentationRect rect, int px, int py, out double qx, out double qy, out bool inside)
    {
        qx = (px + 0.5 - rect.X) * Framebuffer.Width / rect.Width;
        qy = (py + 0.5 - rect.Y) * Framebuffer.Height / rect.Height;
        inside = px >= rect.X && py >= rect.Y && px < rect.X + rect.Width && py < rect.Y + rect.Height;
        if (!inside)
            return true;
        double ex = Epsilon * Framebuffer.Width / rect.Width, ey = Epsilon * Framebuffer.Height / rect.Height;
        return !NearInteger(qx, ex) && !NearInteger(qy, ey);
    }

    private static bool NearInteger(double value, double epsilon) => Math.Abs(value - Math.Round(value)) < epsilon;

    private static Vector3 Round(Vector3 v) =>
        new(MathF.Round(Math.Clamp(v.X, 0f, 255f)), MathF.Round(Math.Clamp(v.Y, 0f, 255f)), MathF.Round(Math.Clamp(v.Z, 0f, 255f)));

    private static Vector3 ToLinear(Vector3 c) => new(ToLinear(c.X), ToLinear(c.Y), ToLinear(c.Z));

    private static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static Vector3 ToSrgb(Vector3 c) => new(ToSrgb(c.X), ToSrgb(c.Y), ToSrgb(c.Z));

    private static float ToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
}
