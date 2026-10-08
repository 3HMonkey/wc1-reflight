using System.Buffers.Binary;
using WingCommander.Core.Resources;

namespace WingCommander.Graphics.Text;

/// <summary>
/// One FONTS.FNT section (a proportional bitmap font). Layout: <c>i16 height</c>,
/// <c>u8 inkIndex</c>, <c>u8 backgroundIndex</c>, <c>u8 width[256]</c> at 0x004,
/// <c>u8 offsetLow[256]</c> at 0x104, <c>u8 offsetHigh[256]</c> at 0x204, then the glyph
/// bitmaps (<c>width * height</c> bytes each, row-major, one palette index per pixel).
/// Glyph pixels equal to the ink index are drawn in the text colour, background pixels in the
/// background colour (0xFF = not drawn), 0xFF is never drawn, anything else as-is.
/// </summary>
/// <remarks>C: the font block addressed through TextContext.font (apTextFonts[4]).</remarks>
public sealed class BitmapFont
{
    /// <summary>Size of the fixed header (height, ink, background, three 256-byte tables).</summary>
    public const int HeaderSize = 0x304;

    private readonly ReadOnlyMemory<byte> _data;

    private BitmapFont(string name, ReadOnlyMemory<byte> data, int index)
    {
        Name = name;
        Index = index;
        _data = data;
        ReadOnlySpan<byte> span = data.Span;
        Height = BinaryPrimitives.ReadInt16LittleEndian(span);
        InkIndex = span[2];
        BackgroundIndex = span[3];
    }

    public string Name { get; }

    /// <summary>Index of the font in FONTS.FNT (0..3), or -1 when parsed from elsewhere.</summary>
    public int Index { get; }

    /// <summary>Glyph height in pixels (all glyphs share it).</summary>
    public short Height { get; }

    /// <summary>Palette index of "ink" pixels in the glyph bitmaps (replaced by the text colour).</summary>
    public byte InkIndex { get; }

    /// <summary>Palette index of "background" pixels (replaced by the background colour).</summary>
    public byte BackgroundIndex { get; }

    /// <summary>The whole section.</summary>
    public ReadOnlySpan<byte> Data => _data.Span;

    /// <summary>The 256-entry advance/bitmap width table.</summary>
    public ReadOnlySpan<byte> Widths => _data.Span.Slice(4, 256);

    /// <summary>
    /// Parses a decoded FONTS.FNT section. Every glyph with a non-zero width must lie inside the
    /// section; glyphs that would not are treated as width 0 by <see cref="GetGlyph"/>.
    /// </summary>
    public static BitmapFont Parse(string name, ReadOnlyMemory<byte> section, int index = -1)
    {
        if (section.Length < HeaderSize)
            throw new GameDataException($"{name}: font section is shorter than its {HeaderSize}-byte header.");
        var font = new BitmapFont(name, section, index);
        if (font.Height <= 0)
            throw new GameDataException($"{name}: invalid glyph height {font.Height}.");
        return font;
    }

    /// <summary>Advance (and bitmap width) of a character code.</summary>
    /// <remarks>C: GetFontCharWidth (0x434FF0, mathfp.c) reads <c>font[4 + c]</c> (unsigned, as the SDL port).</remarks>
    public byte GetWidth(byte character) => _data.Span[4 + character];

    /// <summary>Offset of the glyph bitmap from the section start.</summary>
    public int GetGlyphOffset(byte character)
    {
        ReadOnlySpan<byte> span = _data.Span;
        return span[0x104 + character] | (span[0x204 + character] << 8);
    }

    /// <summary>The glyph bitmap (<c>width * height</c> bytes), empty when absent or out of range.</summary>
    public ReadOnlySpan<byte> GetGlyph(byte character)
    {
        int length = GetWidth(character) * Height;
        int offset = GetGlyphOffset(character);
        if (length == 0 || offset < 0 || offset + length > _data.Length)
            return ReadOnlySpan<byte>.Empty;
        return _data.Span.Slice(offset, length);
    }
}
