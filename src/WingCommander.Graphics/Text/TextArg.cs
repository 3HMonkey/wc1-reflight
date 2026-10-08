namespace WingCommander.Graphics.Text;

/// <summary>
/// One argument of the game's printf-like token formatter: an integer (for <c>%B %F %J %X %Y
/// %c %d %u %x %D %U</c>) or a string (for <c>%s</c>, CP437 bytes or a .NET string whose chars
/// are emitted as their low byte). Implicit conversions keep call sites short and allocation-free:
/// <c>gfx.DrawFormattedText("%d km"u8, distance)</c>.
/// </summary>
public readonly struct TextArg
{
    private readonly int _value;
    private readonly byte[]? _bytes;
    private readonly string? _string;

    private TextArg(int value, byte[]? bytes, string? text)
    {
        _value = value;
        _bytes = bytes;
        _string = text;
    }

    /// <summary>The integer value (0 for string arguments).</summary>
    public int Value => _value;

    /// <summary>True when the argument carries a string.</summary>
    public bool IsString => _bytes is not null || _string is not null;

    public static implicit operator TextArg(int value) => new(value, null, null);

    public static implicit operator TextArg(short value) => new(value, null, null);

    public static implicit operator TextArg(byte value) => new(value, null, null);

    public static implicit operator TextArg(string? text) => new(0, null, text ?? string.Empty);

    public static implicit operator TextArg(byte[]? text) => new(0, text ?? [], null);

    public static TextArg FromInt32(int value) => value;

    public static TextArg FromString(string? text) => text;

    public static TextArg FromBytes(byte[]? text) => text;

    /// <summary>Length of the string argument up to its first NUL.</summary>
    internal int StringLength
    {
        get
        {
            if (_bytes is not null)
            {
                int end = Array.IndexOf(_bytes, (byte)0);
                return end < 0 ? _bytes.Length : end;
            }
            if (_string is not null)
            {
                int end = _string.IndexOf('\0', StringComparison.Ordinal);
                return end < 0 ? _string.Length : end;
            }
            return 0;
        }
    }

    /// <summary>Character <paramref name="index"/> of the string argument as a byte.</summary>
    internal byte CharAt(int index)
    {
        if (_bytes is not null)
            return _bytes[index];
        char c = _string![index];
        return c <= 0xFF ? (byte)c : (byte)'?';
    }
}
