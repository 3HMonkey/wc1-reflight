namespace WingCommander.Core.Rendering;

/// <summary>What an <see cref="OverlayItem"/> draws.</summary>
public enum OverlayItemKind : byte
{
    /// <summary>A filled rectangle in <see cref="OverlayItem.Colour"/>.</summary>
    Rectangle,

    /// <summary>A glyph: its cell (0..Width, 0..Height source pixels) mapped onto the rectangle, foreground in the colour.</summary>
    Glyph,
}

/// <summary>
/// One element of a port overlay (the key help) in render-target pixels, drawn on top of
/// everything with straight (non-premultiplied) alpha. Glyphs use the shape of their
/// <see cref="GlyphImage"/> (distance field, anti-aliased) in a single colour.
/// </summary>
public struct OverlayItem
{
    public float X;
    public float Y;
    public float Width;
    public float Height;

    /// <summary>RGBA8 with red in the lowest byte (see <see cref="OverlayColour"/>).</summary>
    public uint Colour;

    public GlyphKey Glyph;

    public OverlayItemKind Kind;
}

/// <summary>Packs overlay colours.</summary>
public static class OverlayColour
{
    public static uint Rgba(byte r, byte g, byte b, byte a = 255) => (uint)(r | g << 8 | b << 16 | a << 24);

    public static (byte R, byte G, byte B, byte A) Unpack(uint colour) =>
        ((byte)colour, (byte)(colour >> 8), (byte)(colour >> 16), (byte)(colour >> 24));
}

/// <summary>
/// Overlay elements in painter order, laid out for one render-target size. Reusable without
/// allocations once the capacity is reached.
/// </summary>
public sealed class OverlayDrawList
{
    private OverlayItem[] _items = new OverlayItem[256];

    public int Count { get; private set; }

    public ReadOnlySpan<OverlayItem> Items => _items.AsSpan(0, Count);

    /// <summary>Images of the glyphs the items refer to (null while the list is empty).</summary>
    public GlyphImageCache? Glyphs { get; set; }

    public void Clear() => Count = 0;

    public void AddRectangle(float x, float y, float width, float height, uint colour) =>
        Add(new OverlayItem { X = x, Y = y, Width = width, Height = height, Colour = colour, Kind = OverlayItemKind.Rectangle });

    public void AddGlyph(float x, float y, float width, float height, GlyphKey glyph, uint colour) =>
        Add(new OverlayItem { X = x, Y = y, Width = width, Height = height, Colour = colour, Glyph = glyph, Kind = OverlayItemKind.Glyph });

    private void Add(in OverlayItem item)
    {
        if (Count == _items.Length)
            Array.Resize(ref _items, _items.Length * 2);
        _items[Count++] = item;
    }
}
