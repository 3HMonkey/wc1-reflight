namespace WingCommander.Core.Rendering;

/// <summary>One line of the key help: the keys and what they do.</summary>
public readonly record struct KeyHelpEntry(string Keys, string Action);

/// <summary>A titled group of key help lines.</summary>
public sealed class KeyHelpSection(string heading, IReadOnlyList<KeyHelpEntry> entries)
{
    public string Heading { get; } = heading;

    public IReadOnlyList<KeyHelpEntry> Entries { get; } = entries;
}

/// <summary>
/// The key reference a port layer shows next to (or over) the classic picture, drawn by the
/// renderer at output resolution with the game's own fonts (ADR-013). The producer (the flight
/// layer) fills the sections and toggles <see cref="Visible"/>; renderers lay it out with
/// <see cref="KeyHelpLayout"/> for their target size.
/// </summary>
public sealed class KeyHelpOverlay
{
    private IReadOnlyList<KeyHelpSection> _sections = [];
    private string _title = "";
    private bool _visible;
    private byte _font = 1;

    public KeyHelpOverlay(GlyphImageCache glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        Glyphs = glyphs;
    }

    /// <summary>Images of the panel font's glyphs (the producer adds the whole font).</summary>
    public GlyphImageCache Glyphs { get; }

    /// <summary>FONTS.FNT font of the text.</summary>
    public byte Font
    {
        get => _font;
        set
        {
            if (_font != value)
            {
                _font = value;
                Version++;
            }
        }
    }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible != value)
            {
                _visible = value;
                Version++;
            }
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _title = value;
            Version++;
        }
    }

    public IReadOnlyList<KeyHelpSection> Sections
    {
        get => _sections;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _sections = value;
            Version++;
        }
    }

    /// <summary>Changes whenever the content or the visibility changes.</summary>
    public int Version { get; private set; }
}
