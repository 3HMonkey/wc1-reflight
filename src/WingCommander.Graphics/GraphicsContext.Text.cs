using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Text;

namespace WingCommander.Graphics;

public sealed partial class GraphicsContext
{
    private const int StackTextLimit = 512;

    /// <summary>The implicit target of the text functions.</summary>
    /// <remarks>C: pCurrentTextContext.</remarks>
    public TextContext? CurrentTextContext { get; set; }

    /// <summary>
    /// Follows the glyphs drawn into the screen for output-resolution text (ADR-013); null = off.
    /// Port addition, no effect on the pixels.
    /// </summary>
    public TextLayerTracker? TextTracker { get; set; }

    /// <remarks>C: SetTextContext (0x434FA0, mathfp.c).</remarks>
    public void SetTextContext(TextContext? context) => CurrentTextContext = context;

    /// <summary>Sets the absolute text cursor of the current context.</summary>
    /// <remarks>C: SetTextCursor (0x434F70, mathfp.c).</remarks>
    public void SetTextCursor(int x, int y)
    {
        TextContext context = RequireTextContext();
        context.CursorX = unchecked((short)x);
        context.CursorY = unchecked((short)y);
    }

    /// <remarks>C: ResetTextCursor (0x4353F0, mathfp.c).</remarks>
    public void ResetTextCursor()
    {
        TextContext context = RequireTextContext();
        context.CursorX = 0;
        context.CursorY = 0;
    }

    /// <summary>Loads the font if needed, sets font and colours and makes the context current.</summary>
    /// <remarks>C: InitializeTextContextFromFont (0x41D510, disk.c).</remarks>
    public void InitializeTextContextFromFont(TextContext context, int fontIndex, byte colour, byte background)
    {
        ArgumentNullException.ThrowIfNull(context);
        FontCache fonts = Fonts ?? throw new InvalidOperationException("No font cache is configured.");
        context.Font = fonts.Get(fontIndex);
        context.Colour = colour;
        context.BackgroundColour = background;
        SetTextContext(context);
    }

    /// <remarks>C: ReleaseTextFont (0x41D590, disk.c).</remarks>
    public void ReleaseTextFont(int fontIndex) => Fonts?.ReleaseTextFont(fontIndex);

    /// <summary>Advance of a character in the current context's font.</summary>
    /// <remarks>C: GetFontCharWidth (0x434FF0, mathfp.c), unsigned indexing as in the SDL port.</remarks>
    public ushort GetFontCharWidth(byte character) => RequireFont(RequireTextContext()).GetWidth(character);

    /// <summary>
    /// Draws a NUL-terminated (or span-terminated) CP437 string at the current cursor with
    /// word wrapping at the viewport's right edge (exclusive) and optional centring
    /// (<see cref="TextContext.Alignment"/> 2), reproducing the original's quirks: leading
    /// spaces of a line are skipped, a wrapped line is broken at the last space before the
    /// overflowing word, the overflowing character's width is subtracted twice before
    /// centring, '\n' and '\r' are executed through <see cref="DrawTextCharacter"/> (with
    /// centring the cursor x is restored afterwards), and nothing clips vertically.
    /// </summary>
    /// <remarks>C: DrawTextString (0x4350F0, mathfp.c). Deviations: the original loops forever
    /// when the cursor starts at or right of the viewport's right edge (the port returns) or when
    /// a word is wider than the whole viewport (the port breaks the word); the "INVALID STRING"
    /// fatal error becomes an <see cref="InvalidOperationException"/>.</remarks>
    public void DrawTextString(ReadOnlySpan<byte> text)
    {
        TextContext context = RequireTextContext();
        Viewport viewport = RequireViewport(context);
        BitmapFont font = RequireFont(context);
        int cursor = 0;
        bool wrapped = false;
        bool finished = false;
        while (true)
        {
            int lineWidth = context.CursorX;
            int lineStartX = context.CursorX;
            while (At(text, cursor) == ' ')
                cursor++;
            int lineStart = cursor;
            int right = viewport.Right;
            if (lineWidth >= right)
                return; // the original never consumes input here and hangs
            while (true)
            {
                byte value = At(text, cursor);
                cursor++;
                if (value == '\n' || value == '\r')
                    break;
                if (value == 0)
                {
                    finished = true;
                    break;
                }
                lineWidth += font.GetWidth(value);
                if (lineWidth >= right)
                {
                    cursor--;
                    wrapped = true;
                    lineWidth -= font.GetWidth(value);
                    if (At(text, cursor) != ' ')
                    {
                        int overflow = cursor;
                        int widthAtOverflow = lineWidth;
                        if (cursor <= 0)
                            throw InvalidString(text);
                        do
                        {
                            value = At(text, cursor);
                            cursor--;
                            lineWidth -= font.GetWidth(value);
                        }
                        while (cursor >= 0 && At(text, cursor) != ' ');
                        if (cursor <= 0)
                            throw InvalidString(text);
                        if (cursor < lineStart && lineStartX <= viewport.Left)
                        {
                            // The word is wider than the viewport: the original would wrap forever.
                            if (overflow == lineStart)
                                return;
                            cursor = overflow;
                            lineWidth = widthAtOverflow;
                        }
                    }
                    break;
                }
            }

            int savedX = 0;
            bool centre = context.Alignment == TextContext.AlignCentre;
            if (centre)
            {
                savedX = context.CursorX;
                context.CursorX = unchecked((short)(viewport.Left
                    + ((viewport.Right - viewport.Left) - lineWidth + savedX + 1) / 2));
            }
            for (int i = lineStart; i < cursor; i++)
                DrawTextCharacter(At(text, i));
            if (centre)
                context.CursorX = unchecked((short)savedX);
            if (wrapped)
            {
                context.CursorX = viewport.Left;
                wrapped = false;
                context.CursorY = unchecked((short)(context.CursorY + font.Height));
            }
            if (finished)
                return;
        }
    }

    /// <summary>String overload of <see cref="DrawTextString(ReadOnlySpan{byte})"/> (chars map to their low byte).</summary>
    public void DrawTextString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Span<byte> bytes = text.Length <= StackTextLimit ? stackalloc byte[text.Length] : new byte[text.Length];
        EncodeText(text, bytes);
        DrawTextString(bytes);
    }

    /// <summary>'\n' = new line at the viewport's left edge, '\r' = back to the left edge, 0 = nothing, else a glyph.</summary>
    /// <remarks>C: DrawTextCharacter (0x435290, mathfp.c).</remarks>
    public void DrawTextCharacter(byte character)
    {
        TextContext context = RequireTextContext();
        if (character == '\n')
        {
            context.CursorX = RequireViewport(context).Left;
            context.CursorY = unchecked((short)(context.CursorY + RequireFont(context).Height));
        }
        else if (character == '\r')
        {
            context.CursorX = RequireViewport(context).Left;
        }
        else if (character != 0)
        {
            BitmapFont font = RequireFont(context);
            DrawFontGlyph(character, context, font.Height, font.GetWidth(character), context.CursorY);
        }
    }

    /// <summary>
    /// Blits one glyph at (cursorX, <paramref name="y"/>) and advances the cursor by the glyph
    /// width. Ink pixels take the context colour, background pixels the background colour
    /// (0xFF = not drawn), 0xFF is transparent, other values are drawn as-is. There is no
    /// clipping: rows are addressed linearly in the viewport's buffer (a glyph crossing the
    /// buffer's right edge continues on the next row, as in the original); only writes outside
    /// the buffer are dropped.
    /// </summary>
    /// <remarks>C: DrawFontGlyph (0x441150, gr.c). With <see cref="EmulateSagaGlyphRowQuirk"/> the
    /// Kilrathi Saga top-row displacement is reproduced.</remarks>
    public void DrawFontGlyph(byte character, TextContext context, int height, int width, int y)
    {
        ArgumentNullException.ThrowIfNull(context);
        BitmapFont font = RequireFont(context);
        Viewport viewport = RequireViewport(context);
        IndexedSurface? surface = viewport.Surface;
        byte ink = font.InkIndex, background = font.BackgroundIndex;
        byte colour = context.Colour, backgroundColour = context.BackgroundColour;
        bool translate = ink != colour || background != backgroundColour;
        ReadOnlySpan<byte> glyph = font.GetGlyph(character);
        if (surface is not null && glyph.Length >= width * height && width > 0)
        {
            if (TextTracker is { Enabled: true } tracker
                && !(EmulateSagaGlyphRowQuirk && viewport.Top >= y && viewport.Top < y + height
                    && (surface.GetRowOffset16(viewport.Top) & 0x8000) != 0))
                tracker.RecordGlyph(surface, font, character, context.CursorX - surface.OriginX, y - surface.OriginY,
                    width, height, colour, backgroundColour);
            byte[] pixels = surface.Pixels;
            int source = 0;
            for (int row = y; row < y + height; row++)
            {
                int destination = EmulateSagaGlyphRowQuirk && row == viewport.Top
                    && (surface.GetRowOffset16(row) & 0x8000) != 0
                    ? context.CursorX
                    : surface.IndexOf(context.CursorX, row);
                for (int column = 0; column < width; column++)
                {
                    byte value = glyph[source++];
                    if (translate)
                    {
                        if (value == background)
                            value = backgroundColour;
                        else if (value == ink)
                            value = colour;
                    }
                    int index = destination + column;
                    if (value != 0xFF && (uint)index < (uint)pixels.Length)
                        pixels[index] = value;
                }
            }
        }
        context.CursorX = unchecked((short)(context.CursorX + font.GetWidth(character)));
    }

    /// <summary>Appends one character to the string-builder buffer and keeps it NUL-terminated.</summary>
    /// <remarks>C: AppendTextCharacter (0x435310, mathfp.c). Characters that would overflow the
    /// buffer are dropped.</remarks>
    public void AppendTextCharacter(byte character)
    {
        TextContext context = RequireTextContext();
        byte[]? buffer = context.TextBuffer;
        if (buffer is null || context.TextCursor < 0 || context.TextCursor + 1 >= buffer.Length)
            return;
        buffer[context.TextCursor] = character;
        context.TextCursor++;
        buffer[context.TextCursor] = 0;
    }

    /// <summary>
    /// The game's printf: every character except <c>%</c> tokens goes to the sink. Tokens:
    /// <c>%B</c>/<c>%F</c>/<c>%J</c> set background colour / colour / alignment, <c>%X</c>/<c>%Y</c>
    /// the cursor, <c>%P</c> draws the string-builder buffer now, <c>%c</c> one character,
    /// <c>%d</c> short decimal, <c>%u</c> unsigned short decimal, <c>%x</c> unsigned short upper-case
    /// hex, <c>%D</c>/<c>%U</c> 32-bit signed/unsigned decimal, <c>%s</c> a string; any other
    /// character after <c>%</c> is emitted literally. Missing arguments read as 0 / empty.
    /// </summary>
    /// <remarks>C: FormatTextTokens (0x413A40, cockpt.c) and EmitTextString (0x413A10).</remarks>
    public void FormatTextTokens(TextSink sink, ReadOnlySpan<byte> format, ReadOnlySpan<TextArg> args)
    {
        TextContext context = RequireTextContext();
        int argument = 0;
        for (int i = 0; i < format.Length; i++)
        {
            byte c = format[i];
            if (c == 0)
                return;
            if (c != '%')
            {
                Emit(sink, c);
                continue;
            }
            i++;
            byte token = i < format.Length ? format[i] : (byte)0;
            switch ((char)token)
            {
                case 'B':
                    context.BackgroundColour = unchecked((byte)NextValue(args, ref argument));
                    break;
                case 'D':
                    EmitSigned(sink, NextValue(args, ref argument));
                    break;
                case 'F':
                    context.Colour = unchecked((byte)NextValue(args, ref argument));
                    break;
                case 'J':
                    context.Alignment = unchecked((byte)NextValue(args, ref argument));
                    break;
                case 'P':
                    DrawTextString(context.GetBufferText());
                    break;
                case 'U':
                    EmitUnsigned(sink, unchecked((uint)NextValue(args, ref argument)), 10);
                    break;
                case 'X':
                    context.CursorX = unchecked((short)NextValue(args, ref argument));
                    break;
                case 'Y':
                    context.CursorY = unchecked((short)NextValue(args, ref argument));
                    break;
                case 'c':
                    Emit(sink, unchecked((byte)NextValue(args, ref argument)));
                    break;
                case 'd':
                    EmitSigned(sink, unchecked((short)NextValue(args, ref argument)));
                    break;
                case 's':
                    {
                        TextArg text = argument < args.Length ? args[argument] : default;
                        argument++;
                        int length = text.StringLength;
                        for (int k = 0; k < length; k++)
                            Emit(sink, text.CharAt(k));
                        break;
                    }
                case 'u':
                    EmitUnsigned(sink, unchecked((ushort)NextValue(args, ref argument)), 10);
                    break;
                case 'x':
                    EmitUnsigned(sink, unchecked((ushort)NextValue(args, ref argument)), 16);
                    break;
                case '\0':
                    Emit(sink, 0);
                    return;
                default:
                    Emit(sink, token);
                    break;
            }
        }
    }

    /// <summary>Formats and draws at the current cursor; marks the screen dirty.</summary>
    /// <remarks>C: DrawFormattedText (0x413C40, cockpt.c).</remarks>
    public void DrawFormattedText(ReadOnlySpan<byte> format, params ReadOnlySpan<TextArg> args)
    {
        FormatTextTokens(TextSink.Draw, format, args);
        MarkTextDirty();
    }

    /// <summary>String-format overload of <see cref="DrawFormattedText(ReadOnlySpan{byte}, ReadOnlySpan{TextArg})"/>.</summary>
    public void DrawFormattedText(string format, params ReadOnlySpan<TextArg> args)
    {
        ArgumentNullException.ThrowIfNull(format);
        Span<byte> bytes = format.Length <= StackTextLimit ? stackalloc byte[format.Length] : new byte[format.Length];
        EncodeText(format, bytes);
        DrawFormattedText(bytes, args);
    }

    /// <summary>Resets the string builder to its start and formats into it (no NUL is written first).</summary>
    /// <remarks>C: FormatTextBufferFromStart (0x413C70, cockpt.c).</remarks>
    public void FormatTextBufferFromStart(ReadOnlySpan<byte> format, params ReadOnlySpan<TextArg> args)
    {
        RequireTextContext().TextCursor = 0;
        FormatTextTokens(TextSink.Append, format, args);
        MarkTextDirty();
    }

    /// <summary>Formats and appends to the string builder.</summary>
    /// <remarks>C: AppendFormattedText (0x413CB0, cockpt.c).</remarks>
    public void AppendFormattedText(ReadOnlySpan<byte> format, params ReadOnlySpan<TextArg> args)
    {
        FormatTextTokens(TextSink.Append, format, args);
        MarkTextDirty();
    }

    /// <summary>Makes <paramref name="context"/> current, sets the cursor and draws with a temporary alignment.</summary>
    /// <remarks>C: DrawTextAt (0x41D5F0, disk.c). The temporary swap of the context's text pointer
    /// has no observable effect and is omitted.</remarks>
    public void DrawTextAt(TextContext context, int x, int y, ReadOnlySpan<byte> text, byte alignment)
    {
        ArgumentNullException.ThrowIfNull(context);
        SetTextContext(context);
        SetTextCursor(x, y);
        byte savedAlignment = context.Alignment;
        context.Alignment = alignment;
        try
        {
            DrawTextString(text);
        }
        finally
        {
            context.Alignment = savedAlignment;
        }
        MarkTextDirty();
    }

    /// <summary>String overload of <see cref="DrawTextAt(TextContext, int, int, ReadOnlySpan{byte}, byte)"/>.</summary>
    public void DrawTextAt(TextContext context, int x, int y, string text, byte alignment)
    {
        ArgumentNullException.ThrowIfNull(text);
        Span<byte> bytes = text.Length <= StackTextLimit ? stackalloc byte[text.Length] : new byte[text.Length];
        EncodeText(text, bytes);
        DrawTextAt(context, x, y, bytes, alignment);
    }

    /// <summary>
    /// Width of the text in the current font, stopping once it reaches 320; when it stops early
    /// the last character's width is removed again.
    /// </summary>
    /// <remarks>C: MeasureTextPixelWidthClamped (0x418080, geom.c).</remarks>
    public short MeasureTextPixelWidthClamped(ReadOnlySpan<byte> text)
    {
        BitmapFont font = RequireFont(RequireTextContext());
        short width = 0;
        int scan = 0;
        while (At(text, scan) != 0)
        {
            width = unchecked((short)(width + font.GetWidth(At(text, scan++))));
            if (width >= 320)
                break;
        }
        if (At(text, scan) != 0)
        {
            scan--;
            width = unchecked((short)(width - font.GetWidth(At(text, scan))));
        }
        return width;
    }

    /// <summary>Plain sum of the character widths in the current font (up to NUL).</summary>
    public int MeasureTextWidth(ReadOnlySpan<byte> text)
    {
        BitmapFont font = RequireFont(RequireTextContext());
        int width = 0;
        for (int i = 0; i < text.Length && text[i] != 0; i++)
            width += font.GetWidth(text[i]);
        return width;
    }

    /// <summary>Converts .NET text to the byte string the renderer expects (chars above 0xFF become '?').</summary>
    public static void EncodeText(string text, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (int i = 0; i < text.Length && i < destination.Length; i++)
        {
            char c = text[i];
            destination[i] = c <= 0xFF ? (byte)c : (byte)'?';
        }
    }

    private static byte At(ReadOnlySpan<byte> text, int index) =>
        (uint)index < (uint)text.Length ? text[index] : (byte)0;

    private static int NextValue(ReadOnlySpan<TextArg> args, ref int argument)
    {
        int value = argument < args.Length ? args[argument].Value : 0;
        argument++;
        return value;
    }

    private static InvalidOperationException InvalidString(ReadOnlySpan<byte> text)
    {
        int end = text.IndexOf((byte)0);
        var chars = new char[end < 0 ? text.Length : end];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = (char)text[i];
        return new InvalidOperationException($"FATAL : INVALID STRING '{new string(chars)}'");
    }

    private void Emit(TextSink sink, byte character)
    {
        if (sink == TextSink.Draw)
            DrawTextCharacter(character);
        else
            AppendTextCharacter(character);
    }

    private void EmitSigned(TextSink sink, int value)
    {
        if (value < 0)
        {
            Emit(sink, (byte)'-');
            EmitUnsigned(sink, unchecked((uint)-(long)value), 10);
        }
        else
        {
            EmitUnsigned(sink, (uint)value, 10);
        }
    }

    private void EmitUnsigned(TextSink sink, uint value, uint radix)
    {
        Span<byte> digits = stackalloc byte[12];
        int count = 0;
        do
        {
            uint digit = value % radix;
            digits[count++] = (byte)(digit < 10 ? '0' + digit : 'A' + digit - 10);
            value /= radix;
        }
        while (value != 0);
        while (count > 0)
            Emit(sink, digits[--count]);
    }

    private void MarkTextDirty()
    {
        if (CurrentTextContext?.Viewport is { } viewport)
            MarkDirtyIfScreen(viewport);
    }

    private TextContext RequireTextContext() =>
        CurrentTextContext ?? throw new InvalidOperationException("No current text context.");

    private static Viewport RequireViewport(TextContext context) =>
        context.Viewport ?? throw new InvalidOperationException("The text context has no viewport.");

    private static BitmapFont RequireFont(TextContext context) =>
        context.Font ?? throw new InvalidOperationException("The text context has no font.");
}
