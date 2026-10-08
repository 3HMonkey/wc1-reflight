using System.Diagnostics.CodeAnalysis;

namespace WingCommander.Core.Rendering;

/// <summary>One glyph of one FONTS.FNT font (font index 0..3, character code).</summary>
public readonly record struct GlyphKey(byte Font, byte Character)
{
    public override string ToString() => $"font {Font} '{(Character is >= 32 and < 127 ? (char)Character : '?')}' (0x{Character:X2})";
}

/// <summary>
/// A font glyph prepared for drawing at output resolution (ADR-013): the glyph's foreground (ink
/// and fixed-colour pixels; background and transparent pixels are not part of it) depixelized to
/// <see cref="FieldScale"/> texels per source pixel and stored as a signed distance field, plus
/// the palette index of the foreground at every texel. Renderers sample the distance bilinearly
/// for crisp anti-aliased edges at any magnification and replace <see cref="InkIndex"/> by the
/// text colour, so palette fades apply. Immutable once created.
/// </summary>
/// <remarks>
/// <para>Field layout: <see cref="FieldWidth"/> x <see cref="FieldHeight"/> texels, row-major,
/// two bytes per texel: [distance, colour]. The glyph cell (source pixel (0,0) to
/// (Width, Height)) starts at texel (<see cref="FieldPadding"/>, <see cref="FieldPadding"/>);
/// source pixel (x, y) covers texels [Padding + 8x, Padding + 8x + 8). Distance byte =
/// 128 + d * 127 / <see cref="FieldSpread"/> (clamped), d in texels, positive inside, the
/// edge at d = 0. The colour byte of a texel is the colour of the source pixel that contains it;
/// pixels outside the foreground repeat the nearest foreground colour, so filtered edges never
/// pick up a meaningless index. The colour of source pixel (x, y), -1 &lt;= x &lt;= Width, is at
/// texel (Padding + 8x + 4, Padding + 8y + 4), clamped to the field.</para>
/// </remarks>
public sealed class GlyphImage
{
    /// <summary>Distance-field texels per source pixel.</summary>
    public const int FieldScale = 8;

    /// <summary>Texels around the glyph cell (room for filtering and edge effects).</summary>
    public const int FieldPadding = 4;

    /// <summary>Distance in texels that maps to ±127 in the distance byte.</summary>
    public const float FieldSpread = 8f;

    /// <summary>Bytes per field texel (distance, colour).</summary>
    public const int BytesPerTexel = 2;

    public GlyphImage(int width, int height, int advance, byte inkIndex, bool hasForeground, bool multicolour,
        ReadOnlyMemory<byte> texels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Width = width;
        Height = height;
        Advance = advance;
        InkIndex = inkIndex;
        HasForeground = hasForeground && width > 0 && height > 0;
        Multicolour = multicolour;
        int expected = FieldWidth * FieldHeight * BytesPerTexel;
        if (texels.Length < expected)
            throw new ArgumentException($"A {width}x{height} glyph needs {expected} field bytes, got {texels.Length}.", nameof(texels));
        Texels = texels;
    }

    /// <summary>Cell width in source pixels (the font's bitmap width of the character).</summary>
    public int Width { get; }

    /// <summary>Cell height in source pixels (the font height).</summary>
    public int Height { get; }

    /// <summary>Cursor advance in source pixels.</summary>
    public int Advance { get; }

    /// <summary>The font's ink index: colour texels with this value take the text colour.</summary>
    public byte InkIndex { get; }

    /// <summary>False for glyphs without foreground pixels (space): nothing to draw.</summary>
    public bool HasForeground { get; }

    /// <summary>
    /// True when the foreground has fixed colours besides the ink (the chalk font's shading, the
    /// gauge symbols): renderers then blend the colours of the four nearest source pixels
    /// (bilinear at source resolution) instead of using one colour for the whole glyph.
    /// </summary>
    public bool Multicolour { get; }

    public int FieldWidth => Width * FieldScale + 2 * FieldPadding;

    public int FieldHeight => Height * FieldScale + 2 * FieldPadding;

    /// <summary>FieldWidth * FieldHeight texels of (distance, colour), row-major.</summary>
    public ReadOnlyMemory<byte> Texels { get; }

    /// <summary>Encodes a signed distance in texels as the distance byte.</summary>
    public static byte EncodeDistance(float texels) =>
        (byte)Math.Clamp((int)MathF.Round(128f + texels * 127f / FieldSpread), 0, 255);

    /// <summary>Decodes a (possibly filtered) distance byte value back to texels.</summary>
    public static float DecodeDistance(float value) => (value - 128f) * FieldSpread / 127f;
}

/// <summary>
/// Glyph images shared between the Game (producer: builds each glyph once, on first use) and the
/// renderer (consumer: uploads each glyph once into its glyph atlas). Long-lived; single-threaded
/// like the rest of the game loop.
/// </summary>
public sealed class GlyphImageCache
{
    private readonly Dictionary<GlyphKey, GlyphImage> _images = [];

    /// <summary>Changes whenever images are removed or replaced; renderers then forget what they uploaded.</summary>
    public int Generation { get; private set; }

    public int Count => _images.Count;

    public bool TryGet(GlyphKey key, [NotNullWhen(true)] out GlyphImage? image) => _images.TryGetValue(key, out image);

    public bool Contains(GlyphKey key) => _images.ContainsKey(key);

    /// <summary>Adds or replaces the image of <paramref name="key"/>.</summary>
    public void Set(GlyphKey key, GlyphImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (_images.TryGetValue(key, out var existing))
        {
            if (ReferenceEquals(existing, image))
                return;
            Generation++;
        }
        _images[key] = image;
    }

    public void Clear()
    {
        _images.Clear();
        Generation++;
    }
}
