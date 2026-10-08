using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Text;
using WingCommander.Tests;

namespace WingCommander.Graphics.Tests.Text;

public class FontTests
{
    [DataFact]
    public void Fonts_fnt_has_the_four_documented_fonts()
    {
        FontCache fonts = FontCache.FromGameDirectory(GameData.Require());
        var expected = new (short Height, byte Ink, byte Background, int Size)[]
        {
            (11, 15, 0, 12949), (8, 166, 0, 6660), (6, 198, 0, 4342), (11, 15, 1, 17470),
        };
        for (int i = 0; i < 4; i++)
        {
            BitmapFont font = fonts.Get(i);
            Assert.Equal(expected[i], (font.Height, font.InkIndex, font.BackgroundIndex, font.Data.Length));
            Assert.Equal(4, font.GetWidth((byte)' '));
            Assert.Equal(0, font.GetWidth(0));
            Assert.Equal(253, Enumerable.Range(1, 255).Count(c => font.GetWidth((byte)c) != 0));
            for (int c = 0; c < 256; c++)
                Assert.Equal(font.GetWidth((byte)c) * font.Height, font.GetGlyph((byte)c).Length);
        }
        // font 0 is packed: header + sum of all glyph bitmaps
        BitmapFont font0 = fonts.Get(0);
        int total = BitmapFont.HeaderSize + Enumerable.Range(0, 256).Sum(c => font0.GetWidth((byte)c) * font0.Height);
        Assert.Equal(font0.Data.Length, total);
    }

    [DataFact]
    public void Font_cache_keeps_font_1_resident()
    {
        FontCache fonts = FontCache.FromGameDirectory(GameData.Require());
        fonts.Get(1);
        fonts.Get(2);
        fonts.ReleaseTextFont(1);
        fonts.ReleaseTextFont(2);
        Assert.True(fonts.IsLoaded(1));
        Assert.False(fonts.IsLoaded(2));
    }

    [DataFact]
    public void Real_glyph_is_blitted_with_ink_and_background_translation()
    {
        FontCache fonts = FontCache.FromGameDirectory(GameData.Require());
        BitmapFont font = fonts.Get(0);
        var gfx = new GraphicsContext { Fonts = fonts };
        var surface = new IndexedSurface(40, 20);
        surface.Clear(0x33);
        var vp = new Viewport(surface, 0, 0, 39, 19);
        var context = new TextContext { Viewport = vp };
        gfx.InitializeTextContextFromFont(context, 0, 0x60, 0x61);
        gfx.SetTextCursor(5, 3);
        gfx.DrawTextCharacter((byte)'A');
        ReadOnlySpan<byte> glyph = font.GetGlyph((byte)'A');
        int width = font.GetWidth((byte)'A');
        Assert.Equal(5 + width, context.CursorX);
        for (int r = 0; r < font.Height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                byte g = glyph[r * width + c];
                int expected = g == font.InkIndex ? 0x60 : g == font.BackgroundIndex ? 0x61 : g == 0xFF ? 0x33 : g;
                Assert.Equal(expected, surface[5 + c, 3 + r]);
            }
        }
        Assert.Contains(glyph.ToArray(), g => g == font.InkIndex);
    }
}
