using WingCommander.Graphics.Raster;

namespace WingCommander.Graphics.Text;

/// <summary>
/// Where and how text is drawn: target viewport (also the wrap/centre rectangle), absolute
/// cursor, font, colours, alignment and the string-builder buffer used by the <c>%P</c> token
/// and <c>FormatTextBufferFromStart</c>/<c>AppendFormattedText</c>. Copy with
/// <see cref="CopyFrom"/> where the C code assigns the struct.
/// </summary>
/// <remarks>C: TextContext (include/wc1.h, 27 bytes); the unused fontWorkspace is dropped.</remarks>
public sealed class TextContext
{
    /// <summary>Alignment value that centres every line in the viewport.</summary>
    public const byte AlignCentre = 2;

    /// <summary>Target viewport; its rectangle is the wrap and centring frame.</summary>
    public Viewport? Viewport { get; set; }

    /// <summary>Absolute x of the next glyph.</summary>
    public short CursorX { get; set; }

    /// <summary>Absolute y of the top row of the next glyph.</summary>
    public short CursorY { get; set; }

    public BitmapFont? Font { get; set; }

    /// <summary>Replaces the font's ink index.</summary>
    public byte Colour { get; set; }

    /// <summary>Replaces the font's background index; 0xFF = transparent.</summary>
    public byte BackgroundColour { get; set; }

    /// <summary>2 centres each line in the viewport; anything else is left aligned from the cursor.</summary>
    public byte Alignment { get; set; }

    /// <summary>String-builder buffer (NUL-terminated CP437 bytes); null when the context has none.</summary>
    public byte[]? TextBuffer { get; set; }

    /// <summary>Write position in <see cref="TextBuffer"/>.</summary>
    public int TextCursor { get; set; }

    /// <summary>C struct assignment <c>*this = *source</c> (the buffer reference is shared).</summary>
    public void CopyFrom(TextContext source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Viewport = source.Viewport;
        CursorX = source.CursorX;
        CursorY = source.CursorY;
        Font = source.Font;
        Colour = source.Colour;
        BackgroundColour = source.BackgroundColour;
        Alignment = source.Alignment;
        TextBuffer = source.TextBuffer;
        TextCursor = source.TextCursor;
    }

    /// <summary>A copy of the context (shares viewport, font and buffer references).</summary>
    public TextContext Clone()
    {
        var copy = new TextContext();
        copy.CopyFrom(this);
        return copy;
    }

    /// <summary>The string-builder contents up to the terminating NUL.</summary>
    public ReadOnlySpan<byte> GetBufferText()
    {
        if (TextBuffer is null)
            return ReadOnlySpan<byte>.Empty;
        ReadOnlySpan<byte> all = TextBuffer;
        int end = all.IndexOf((byte)0);
        return end < 0 ? all : all[..end];
    }
}
