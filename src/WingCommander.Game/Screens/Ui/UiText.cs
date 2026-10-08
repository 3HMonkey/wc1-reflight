using System.Text;

namespace WingCommander.Game.Screens.Ui;

/// <summary>Small conversions between .NET strings and the game's single-byte C strings.</summary>
public static class UiText
{
    /// <summary>The string as single bytes (chars above 0xFF become '?'), without a terminator.</summary>
    public static byte[] ToBytes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
            bytes[i] = text[i] <= 0xFF ? (byte)text[i] : (byte)'?';
        return bytes;
    }

    /// <summary>The bytes up to the first NUL (or the end) as a string.</summary>
    public static string FromBytes(ReadOnlySpan<byte> bytes)
    {
        int length = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(length < 0 ? bytes : bytes[..length]);
    }

    /// <summary>C <c>toupper</c> on a key value (only 'a'..'z' change).</summary>
    public static int ToUpper(int key) => key is >= 'a' and <= 'z' ? key - 0x20 : key;

    /// <summary>C <c>_strupr</c> (ASCII letters only).</summary>
    public static string ToUpperAscii(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= 'a' and <= 'z')
                chars[i] = (char)(chars[i] - 0x20);
        }
        return new string(chars);
    }
}
