using System.Numerics;
using WingCommander.Core.Fonts;
using WingCommander.Core.Imaging;
using WingCommander.Core.Rendering;

namespace WingCommander.Graphics.Text;

/// <summary>
/// Fits the glyphs of a TrueType font into the cells of an original FONTS.FNT font, so the game
/// keeps its layout (advances, word wrap, centring) while the output-resolution text shows the
/// replacement design (ADR-013). The replacement's cap height and baseline match the original
/// upper-case letters; horizontally every glyph is condensed by one font-wide factor (more only
/// where it would overflow the original glyph's ink) and centred on the original ink.
/// </summary>
public sealed class FontReplacement
{
    private readonly BitmapFont _original;
    private readonly TrueTypeFont _outline;

    public FontReplacement(BitmapFont original, TrueTypeFont outline, string name)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(name);
        _original = original;
        _outline = outline;
        Name = name;

        // Cap height and baseline of the original from its upper-case letters (most common extents).
        var tops = new Dictionary<int, int>();
        var bottoms = new Dictionary<int, int>();
        for (int c = 'A'; c <= 'Z'; c++)
        {
            if (InkBox((byte)c) is { } box)
            {
                tops[box.Top] = tops.GetValueOrDefault(box.Top) + 1;
                bottoms[box.Bottom] = bottoms.GetValueOrDefault(box.Bottom) + 1;
            }
        }
        int top = tops.Count > 0 ? tops.MaxBy(p => p.Value).Key : 0;
        int bottom = bottoms.Count > 0 ? bottoms.MaxBy(p => p.Value).Key : original.Height - 1;
        Baseline = bottom + 1;
        CapHeight = Math.Max(1, bottom + 1 - top);
        Scale = CapHeight / (float)Math.Max(1, outline.CapHeight);

        // One condensing factor for the whole font: the median of what each glyph would need.
        var ratios = new List<float>();
        for (int c = 0x21; c < 0x7F; c++)
        {
            if (InkBox((byte)c) is not { } box || outline.GetGlyphForCodePoint(c) is not { IsEmpty: false } glyph)
                continue;
            float width = (glyph.XMax - glyph.XMin) * Scale;
            if (width > 0.5f)
                ratios.Add(MathF.Min(1f, box.Width / width));
        }
        ratios.Sort();
        Condense = ratios.Count > 0 ? ratios[ratios.Count / 2] : 1f;
    }

    /// <summary>Name for logs and documentation ("SPACE WING LEADER").</summary>
    public string Name { get; }

    /// <summary>Baseline of the original font: cell row below the upper-case letters.</summary>
    public int Baseline { get; }

    /// <summary>Height of the original upper-case letters in pixels.</summary>
    public int CapHeight { get; }

    /// <summary>Source pixels per font unit (vertical).</summary>
    public float Scale { get; }

    /// <summary>Font-wide horizontal factor (1 = natural proportions).</summary>
    public float Condense { get; }

    /// <summary>
    /// The replacement image of <paramref name="character"/>, or null to keep the original:
    /// codes outside 0x21..0x7E, glyphs without ink in the original, or characters the
    /// replacement font does not have.
    /// </summary>
    public GlyphImage? Build(byte character)
    {
        if (character is < 0x21 or > 0x7E || InkBox(character) is not { } box)
            return null;
        if (_outline.GetGlyphForCodePoint(character) is not { IsEmpty: false } glyph)
            return null;
        int width = _original.GetWidth(character);
        int height = _original.Height;
        float glyphWidth = (glyph.XMax - glyph.XMin) * Scale;
        float horizontal = Scale * (glyphWidth > 0.5f ? MathF.Min(Condense, box.Width / glyphWidth) : Condense);
        float centre = (box.Left + box.Right + 1) / 2f;
        float glyphCentre = (glyph.XMin + glyph.XMax) / 2f;

        var polygons = new List<IReadOnlyList<Vector2>>(glyph.Contours.Count);
        foreach (Vector2[] contour in glyph.Contours)
        {
            var points = new Vector2[contour.Length];
            for (int i = 0; i < contour.Length; i++)
                points[i] = new Vector2(centre + (contour[i].X - glyphCentre) * horizontal, Baseline - contour[i].Y * Scale);
            polygons.Add(points);
        }

        const int scale = GlyphImage.FieldScale;
        const int padding = GlyphImage.FieldPadding;
        int fieldWidth = width * scale + 2 * padding;
        int fieldHeight = height * scale + 2 * padding;
        var distance = new float[fieldWidth * fieldHeight];
        float origin = -(float)padding / scale;
        PixelOutline.SignedDistance(polygons, fieldWidth, fieldHeight, scale, origin, origin, distance,
            limit: GlyphImage.FieldSpread + 2f);
        var texels = new byte[fieldWidth * fieldHeight * GlyphImage.BytesPerTexel];
        for (int i = 0; i < distance.Length; i++)
        {
            texels[i * 2] = GlyphImage.EncodeDistance(distance[i]);
            texels[i * 2 + 1] = _original.InkIndex;
        }
        return new GlyphImage(width, height, width, _original.InkIndex, hasForeground: true, multicolour: false, texels);
    }

    /// <summary>Ink extents of an original glyph (foreground = not background and not 0xFF), or null.</summary>
    private InkExtents? InkBox(byte character)
    {
        int width = _original.GetWidth(character);
        ReadOnlySpan<byte> glyph = _original.GetGlyph(character);
        if (width == 0 || glyph.Length < width * _original.Height)
            return null;
        int left = int.MaxValue, right = -1, top = int.MaxValue, bottom = -1;
        for (int y = 0; y < _original.Height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte value = glyph[y * width + x];
                if (value == 0xFF || value == _original.BackgroundIndex)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        return right < 0 ? null : new InkExtents(left, top, right, bottom);
    }

    private readonly record struct InkExtents(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right + 1 - Left;
    }
}

/// <summary>
/// Supplies the <see cref="GlyphImage"/> of every FONTS.FNT glyph the game draws, building it on
/// first use into a shared <see cref="GlyphImageCache"/>: from a configured replacement font when
/// it has the character (<see cref="FontReplacement"/>), otherwise by vectorizing the original
/// pixels (<see cref="GlyphImageBuilder"/>).
/// </summary>
public sealed class GlyphImageSource
{
    private readonly (TrueTypeFont Outline, string Name)?[] _outlines = new (TrueTypeFont, string)?[FontCache.FontCount];
    private readonly FontReplacement?[] _replacements = new FontReplacement?[FontCache.FontCount];
    private readonly BitmapFont?[] _replacementFonts = new BitmapFont?[FontCache.FontCount];

    public GlyphImageSource(GlyphImageCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        Cache = cache;
    }

    public GlyphImageCache Cache { get; }

    /// <summary>Replaces font <paramref name="fontIndex"/> by a TrueType font (null = original pixels again).</summary>
    public void SetReplacement(int fontIndex, TrueTypeFont? outline, string name = "")
    {
        if ((uint)fontIndex >= FontCache.FontCount)
            throw new ArgumentOutOfRangeException(nameof(fontIndex));
        _outlines[fontIndex] = outline is null ? null : (outline, name);
        _replacements[fontIndex] = null;
        _replacementFonts[fontIndex] = null;
        Cache.Clear();
    }

    /// <summary>The replacement fitted to <paramref name="font"/>, or null.</summary>
    public FontReplacement? GetReplacement(BitmapFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if ((uint)font.Index >= FontCache.FontCount || _outlines[font.Index] is not { } outline)
            return null;
        if (!ReferenceEquals(_replacementFonts[font.Index], font))
        {
            _replacements[font.Index] = new FontReplacement(font, outline.Outline, outline.Name);
            _replacementFonts[font.Index] = font;
        }
        return _replacements[font.Index];
    }

    /// <summary>The image of a glyph, built and cached on first use.</summary>
    public GlyphImage Get(BitmapFont font, byte character)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (font.Index < 0)
            throw new ArgumentException("The font has no FONTS.FNT index.", nameof(font));
        var key = new GlyphKey((byte)font.Index, character);
        if (!Cache.TryGet(key, out GlyphImage? image))
        {
            image = GetReplacement(font)?.Build(character) ?? GlyphImageBuilder.Build(font, character);
            Cache.Set(key, image);
        }
        return image;
    }

    /// <summary>Builds every glyph of a font (the key help overlay needs the metrics of all of them).</summary>
    public void AddFont(BitmapFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        for (int c = 0; c < 256; c++)
            Get(font, (byte)c);
    }
}
