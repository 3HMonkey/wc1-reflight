using System.Buffers.Binary;
using WingCommander.Graphics.Text;

namespace WingCommander.Graphics.Tests.TestSupport;

/// <summary>Builds FONTS.FNT-style sections for synthetic text tests.</summary>
internal static class TestFonts
{
    /// <summary>
    /// A font where every printable ASCII character (and space) has the given width and a solid
    /// ink glyph, except space which is all background.
    /// </summary>
    public static BitmapFont Uniform(int height, int width, byte ink = 15, byte background = 0)
    {
        var glyphs = new Dictionary<byte, byte[]>();
        for (int c = 32; c < 127; c++)
        {
            var g = new byte[width * height];
            Array.Fill(g, c == ' ' ? background : ink);
            glyphs[(byte)c] = g;
        }
        return Build(height, ink, background, glyphs, _ => width);
    }

    /// <summary>A font with explicit glyphs; <paramref name="widthOf"/> gives each glyph's width.</summary>
    public static BitmapFont Build(int height, byte ink, byte background, IReadOnlyDictionary<byte, byte[]> glyphs,
        Func<byte, int> widthOf, int index = -1)
    {
        int size = BitmapFont.HeaderSize + glyphs.Values.Sum(g => g.Length);
        var data = new byte[size];
        BinaryPrimitives.WriteInt16LittleEndian(data, (short)height);
        data[2] = ink;
        data[3] = background;
        int offset = BitmapFont.HeaderSize;
        foreach (var (code, pixels) in glyphs.OrderBy(kv => kv.Key))
        {
            data[4 + code] = (byte)widthOf(code);
            data[0x104 + code] = (byte)offset;
            data[0x204 + code] = (byte)(offset >> 8);
            pixels.CopyTo(data, offset);
            offset += pixels.Length;
        }
        return BitmapFont.Parse("test font", data, index);
    }
}
