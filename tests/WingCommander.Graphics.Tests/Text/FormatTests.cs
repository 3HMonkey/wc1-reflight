using System.Text;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Tests.TestSupport;
using WingCommander.Graphics.Text;

namespace WingCommander.Graphics.Tests.Text;

public class FormatTests
{
    private static (GraphicsContext Gfx, TextContext Context) Setup()
    {
        var context = new TextContext
        {
            Viewport = new Viewport(new IndexedSurface(320, 200), 0, 0, 319, 199),
            Font = TestFonts.Uniform(2, 4),
            Colour = 15,
            BackgroundColour = 0xFF,
            TextBuffer = new byte[128],
        };
        var gfx = new GraphicsContext();
        gfx.SetTextContext(context);
        return (gfx, context);
    }

    private static string Format(string format, params TextArg[] args)
    {
        var (gfx, context) = Setup();
        gfx.FormatTextBufferFromStart(Encoding.Latin1.GetBytes(format), args);
        return Encoding.Latin1.GetString(context.GetBufferText());
    }

    [Fact]
    public void Numeric_tokens_follow_the_original_conversions()
    {
        Assert.Equal("-5", Format("%d", -5));
        Assert.Equal("-1", Format("%d", 0xFFFF));          // (short)arg
        Assert.Equal("65535", Format("%u", -1));           // (u16)arg
        Assert.Equal("BEEF", Format("%x", 0xBEEF));        // upper case, no leading zeros
        Assert.Equal("0", Format("%x", 0));
        Assert.Equal("-100000", Format("%D", -100000));
        Assert.Equal("4294967295", Format("%U", -1));
        Assert.Equal("A1B", Format("%c%d%c", 'A', 1, 'B'));
    }

    [Fact]
    public void String_and_literal_tokens()
    {
        Assert.Equal("Wait for Kurasawa", Format("Wait for %s", "Kurasawa"));
        Assert.Equal("100%", Format("100%%"));
        Assert.Equal("aqb", Format("a%qb"));
        Assert.Equal("x", Format("x%s", (string?)null));
        Assert.Equal("1 km", Format("%d%s", 1, Encoding.ASCII.GetBytes(" km\0junk")));
    }

    [Fact]
    public void Context_tokens_change_colour_alignment_and_cursor()
    {
        var (gfx, context) = Setup();
        gfx.DrawFormattedText("%F%B%J%X%Y"u8, 0x47, 0x10, 2, 33, 44);
        Assert.Equal((0x47, 0x10, 2, 33, 44), (context.Colour, context.BackgroundColour, context.Alignment, context.CursorX, context.CursorY));
    }

    [Fact]
    public void Append_keeps_the_buffer_nul_terminated_and_from_start_rewinds()
    {
        var (gfx, context) = Setup();
        gfx.FormatTextBufferFromStart("AB"u8);
        gfx.AppendFormattedText("%d"u8, 7);
        Assert.Equal("AB7", Encoding.ASCII.GetString(context.GetBufferText()));
        gfx.FormatTextBufferFromStart("X"u8);
        Assert.Equal("X", Encoding.ASCII.GetString(context.GetBufferText()));
        Assert.Equal(1, context.TextCursor);
    }

    [Fact]
    public void P_token_draws_the_string_builder_buffer()
    {
        var (gfx, context) = Setup();
        gfx.SetTextCursor(10, 20);
        // DrawModalTextPanel pattern: format into the buffer, then %P draws it
        gfx.FormatTextBufferFromStart("HI%P"u8);
        Assert.Equal(18, context.CursorX); // two 4-pixel glyphs drawn
        Assert.Equal(15, context.Viewport!.Surface![10, 20]);
    }

    [Fact]
    public void Draw_formatted_text_draws_at_the_cursor_and_handles_newlines()
    {
        var (gfx, context) = Setup();
        gfx.SetTextCursor(5, 5);
        gfx.DrawFormattedText("%d\n%s", 12, "AB");
        Assert.Equal((8, 7), (context.CursorX, context.CursorY));
        Assert.Equal(15, context.Viewport!.Surface![5, 5]);
        Assert.Equal(15, context.Viewport.Surface[0, 7]);
    }
}
