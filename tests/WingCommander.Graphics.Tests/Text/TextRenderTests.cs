using WingCommander.Core.Video;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Tests.TestSupport;
using WingCommander.Graphics.Text;

namespace WingCommander.Graphics.Tests.Text;

public class TextRenderTests
{
    /// <summary>Uniform 2-pixel-high font, every glyph 10 wide (space all background).</summary>
    private static (GraphicsContext Gfx, TextContext Context, IndexedSurface Surface) Setup(int left, int right,
        int glyphWidth = 10)
    {
        var surface = new IndexedSurface(320, 200);
        var vp = new Viewport(surface, left, 0, right, 199);
        var context = new TextContext
        {
            Viewport = vp,
            Font = TestFonts.Uniform(2, glyphWidth, ink: 15, background: 0),
            Colour = 15,
            BackgroundColour = 0xFF,
        };
        var gfx = new GraphicsContext();
        gfx.SetTextContext(context);
        return (gfx, context, surface);
    }

    /// <summary>First x of every run of ink pixels on a row (one entry per drawn glyph of width 10).</summary>
    private static List<int> GlyphStarts(IndexedSurface s, int row, int glyphWidth = 10)
    {
        var starts = new List<int>();
        for (int x = 0; x < s.Width; x++)
        {
            if (s[x, row] == 15 && (x == 0 || s[x - 1, row] != 15 || (x - starts.LastOrDefault(-100)) >= glyphWidth))
            {
                if (starts.Count == 0 || x - starts[^1] >= glyphWidth)
                    starts.Add(x);
            }
        }
        return starts;
    }

    [Fact]
    public void Wraps_at_the_last_space_before_the_right_edge()
    {
        var (gfx, context, surface) = Setup(0, 100);
        gfx.SetTextCursor(0, 0);
        gfx.DrawTextString("AAAA BBBB CCCC"u8);
        // "AAAA BBBB" would end at 90 >= 100? no: 4*10 + 10 + 40 = 90 < 100 -> fits; "CCCC" wraps
        Assert.Equal([0, 10, 20, 30, 50, 60, 70, 80], GlyphStarts(surface, 0));
        Assert.Equal([0, 10, 20, 30], GlyphStarts(surface, 2));
        Assert.Equal((40, 2), (context.CursorX, context.CursorY));
    }

    [Fact]
    public void Right_edge_is_exclusive()
    {
        var (gfx, _, surface) = Setup(0, 100);
        gfx.SetTextCursor(0, 0);
        gfx.DrawTextString("AAAAAAAAA BB"u8); // nine glyphs end exactly at 90; the space makes 100 -> wrap
        Assert.Equal(9, GlyphStarts(surface, 0).Count);
        Assert.Equal([0, 10], GlyphStarts(surface, 2));
    }

    [Fact]
    public void Leading_spaces_of_every_line_are_skipped()
    {
        var (gfx, context, surface) = Setup(0, 100);
        gfx.SetTextCursor(0, 0);
        gfx.DrawTextString("   AA\n   BB"u8);
        Assert.Equal([0, 10], GlyphStarts(surface, 0));
        Assert.Equal([0, 10], GlyphStarts(surface, 2));
        Assert.Equal(2, context.CursorY);
    }

    [Fact]
    public void Centring_uses_the_doubly_subtracted_width_of_a_wrapped_line()
    {
        // viewport 0..100. "AAAA BBBBBBB": "AAAA " = 50, B overflows at the 6th B (50+60 >= 100):
        // lineWidth = 50 + 50 (five Bs) -> minus the B again (double subtraction) and the four
        // remaining Bs back to the space -> 100 - 10 - 40 = 50, minus nothing for the space -> 40?
        var (gfx, _, surface) = Setup(0, 100);
        gfx.CurrentTextContext!.Alignment = TextContext.AlignCentre;
        gfx.SetTextCursor(0, 0);
        gfx.DrawTextString("AAAA BBBBBBB"u8);
        // first line "AAAA" (40 px real width); centring sees lineWidth = 100-10-50 = 40 here:
        // cursorX = 0 + (100 - 0 - lineWidth + 0 + 1) / 2
        List<int> first = GlyphStarts(surface, 0);
        Assert.Equal(4, first.Count);
        int lineWidth = 50 + 50 - 10 - 50; // width up to overflow, minus overflowing B, minus BBBBB backtrack
        Assert.Equal((100 - lineWidth + 1) / 2, first[0]);
        List<int> second = GlyphStarts(surface, 2);
        Assert.Equal(7, second.Count);
        Assert.Equal((100 - 70 + 1) / 2, second[0]);
    }

    [Fact]
    public void Newline_in_centred_text_restores_the_saved_cursor_x()
    {
        var (gfx, context, surface) = Setup(0, 200);
        context.Alignment = TextContext.AlignCentre;
        gfx.SetTextCursor(60, 0);
        gfx.DrawTextString("AA\nBB"u8);
        // line 1: lineWidth = 60 + 20 = 80 -> x = (200 - 80 + 60 + 1) / 2 = 90
        Assert.Equal([90, 100], GlyphStarts(surface, 0));
        // '\n' moved the cursor to the left edge, but centring then restored x = 60, so line 2
        // is centred from 60 again
        Assert.Equal([90, 100], GlyphStarts(surface, 2));
        Assert.Equal((60, 2), (context.CursorX, context.CursorY));
    }

    [Fact]
    public void Cursor_at_the_right_edge_draws_nothing_instead_of_hanging()
    {
        var (gfx, _, surface) = Setup(0, 100);
        gfx.SetTextCursor(100, 0);
        gfx.DrawTextString("AA"u8);
        Assert.All(surface.Pixels, p => Assert.Equal(0, p));
    }

    [Fact]
    public void A_word_wider_than_the_viewport_is_broken_instead_of_hanging()
    {
        var (gfx, _, surface) = Setup(0, 50);
        gfx.SetTextCursor(0, 0);
        gfx.DrawTextString("X AAAAAAAAAA"u8);
        Assert.Equal([0], GlyphStarts(surface, 0));
        Assert.Equal([0, 10, 20, 30], GlyphStarts(surface, 2));
        Assert.Equal(4, GlyphStarts(surface, 4).Count);
    }

    [Fact]
    public void Overflow_of_the_first_character_is_the_original_fatal_error()
    {
        var (gfx, _, _) = Setup(0, 50);
        gfx.SetTextCursor(45, 0);
        Assert.Throws<InvalidOperationException>(() => gfx.DrawTextString("AAAAAAAAAA"u8));
    }

    [Fact]
    public void Glyphs_are_not_clipped_to_the_viewport_and_wrap_linearly_in_the_buffer()
    {
        var surface = new IndexedSurface(20, 10);
        var vp = new Viewport(surface, 0, 0, 9, 9);
        var context = new TextContext { Viewport = vp, Font = TestFonts.Uniform(2, 4), Colour = 15, BackgroundColour = 0xFF };
        var gfx = new GraphicsContext();
        gfx.SetTextContext(context);
        gfx.SetTextCursor(18, 3);
        gfx.DrawTextCharacter((byte)'A');
        Assert.Equal(15, surface[18, 3]);
        Assert.Equal(15, surface[19, 3]);
        Assert.Equal(15, surface[0, 4]); // continues on the next buffer row like the original pointer walk
        Assert.Equal(15, surface[1, 4]);
        Assert.Equal(22, context.CursorX);
    }

    [Fact]
    public void Saga_glyph_quirk_moves_the_first_row_to_buffer_row_0_when_enabled()
    {
        var framebuffer = new Framebuffer();
        var gfx = new GraphicsContext(framebuffer, new Palette(), null);
        var vdu = gfx.Screen!.Clone();
        vdu.SetViewportRect(10, 133, 82, 198);
        var context = new TextContext { Viewport = vdu, Font = TestFonts.Uniform(2, 3), Colour = 15, BackgroundColour = 0xFF };
        gfx.SetTextContext(context);
        gfx.SetTextCursor(20, 133);
        gfx.DrawTextCharacter((byte)'A');
        Assert.Equal(15, framebuffer[20, 133]);
        Assert.Equal(0, framebuffer[20, 0]);

        framebuffer.Clear(0);
        gfx.EmulateSagaGlyphRowQuirk = true;
        gfx.SetTextCursor(20, 133);
        gfx.DrawTextCharacter((byte)'A');
        Assert.Equal(15, framebuffer[20, 0]);   // 133*320 has bit 15 set: row 0 instead of row 133
        Assert.Equal(0, framebuffer[20, 133]);
        Assert.Equal(15, framebuffer[20, 134]); // the second row is unaffected

        framebuffer.Clear(0);
        vdu.SetViewportRect(10, 100, 82, 198);   // 100*320 < 0x8000: no displacement
        gfx.SetTextCursor(20, 100);
        gfx.DrawTextCharacter((byte)'A');
        Assert.Equal(15, framebuffer[20, 100]);
    }

    [Fact]
    public void Ink_background_and_transparent_pixels_translate_like_the_palette_table()
    {
        var glyph = new byte[] { 15, 0, 0xFF, 7 };
        var font = TestFonts.Build(1, 15, 0, new Dictionary<byte, byte[]> { [(byte)'Q'] = glyph }, _ => 4);
        var surface = new IndexedSurface(4, 1);
        surface.Clear(9);
        var context = new TextContext { Viewport = new Viewport(surface, 0, 0, 3, 0), Font = font, Colour = 0x20, BackgroundColour = 0x21 };
        var gfx = new GraphicsContext();
        gfx.SetTextContext(context);
        gfx.DrawTextCharacter((byte)'Q');
        Assert.Equal(new byte[] { 0x20, 0x21, 9, 7 }, surface.Pixels);
        surface.Clear(9);
        context.CursorX = 0;
        context.BackgroundColour = 0xFF;
        gfx.DrawTextCharacter((byte)'Q');
        Assert.Equal(new byte[] { 0x20, 9, 9, 7 }, surface.Pixels);
    }

    [Fact]
    public void Measure_clamped_width_stops_at_320_and_drops_the_last_character()
    {
        var (gfx, _, _) = Setup(0, 319, glyphWidth: 100);
        Assert.Equal(400, gfx.MeasureTextPixelWidthClamped("AAAA"u8));  // reached 320 at the last char: kept
        Assert.Equal(300, gfx.MeasureTextPixelWidthClamped("AAAAA"u8)); // stopped early: last char removed
        Assert.Equal(200, gfx.MeasureTextPixelWidthClamped("AA"u8));
        Assert.Equal(0, gfx.MeasureTextPixelWidthClamped(""u8));
    }

    [Fact]
    public void Draw_text_at_restores_the_alignment()
    {
        var (gfx, context, surface) = Setup(0, 200);
        context.Alignment = 0;
        gfx.DrawTextAt(context, 0, 10, "AA", TextContext.AlignCentre);
        Assert.Equal(0, context.Alignment);
        Assert.Equal([(200 - 20 + 1) / 2, (200 - 20 + 1) / 2 + 10], GlyphStarts(surface, 10));
    }
}
