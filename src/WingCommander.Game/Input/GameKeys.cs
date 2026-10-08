namespace WingCommander.Game.Input;

/// <summary>
/// The keyboard keys by the PC scan code the game reads (the numeric keypad shares the codes of
/// the cursor block, as on DOS): the identifier stored in <c>config.json</c>, the short name the
/// menus and the key help show, and the Windows virtual-key code the host reports for the key on
/// a US layout (the game's "VK duplicate" key events). Key bindings (ADR-016) translate a key into
/// another key's scan code and virtual-key code, so a bound key behaves exactly like the original.
/// Small punctuation keys are named in words: their narrow glyphs are hard to read in the menus.
/// </summary>
public static class GameKeys
{
    /// <summary>Scan codes 0x00..0x7F.</summary>
    public const int Count = 0x80;

    private readonly record struct KeyInfo(string Id, string Name, ushort VirtualKey);

    private static readonly KeyInfo?[] Keys = Build();

    /// <summary>True for the scan codes of real keys.</summary>
    public static bool IsKnown(int scanCode) => (uint)scanCode < Count && Keys[scanCode] is not null;

    /// <summary>The identifier stored in config.json ("Space", "Up", "F1", "A").</summary>
    public static string Id(int scanCode) => (uint)scanCode < Count && Keys[scanCode] is { } key ? key.Id : $"0x{scanCode:x2}";

    /// <summary>The short name the menus show ("Space", "Up", "Num 5", "\").</summary>
    public static string Name(int scanCode) => (uint)scanCode < Count && Keys[scanCode] is { } key ? key.Name : $"0x{scanCode:x2}";

    /// <summary>The virtual-key code the host reports with the key (0 when it reports none).</summary>
    public static int VirtualKey(int scanCode) => (uint)scanCode < Count && Keys[scanCode] is { } key ? key.VirtualKey : 0;

    /// <summary>A key by its identifier or its name (case and spaces are ignored).</summary>
    public static bool TryParse(string? text, out int scanCode)
    {
        scanCode = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string wanted = Normalize(text);
        for (int code = 0; code < Count; code++)
        {
            if (Keys[code] is { } key && (Normalize(key.Id) == wanted || Normalize(key.Name) == wanted))
            {
                scanCode = code;
                return true;
            }
        }
        return false;
    }

    private static string Normalize(string text) => text.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

    private static KeyInfo?[] Build()
    {
        var keys = new KeyInfo?[Count];
        void Add(int scanCode, string id, string name, int virtualKey) => keys[scanCode] = new KeyInfo(id, name, (ushort)virtualKey);
        void Letters(int first, string letters)
        {
            for (int i = 0; i < letters.Length; i++)
                Add(first + i, letters[i].ToString(), letters[i].ToString(), letters[i]);
        }

        Add(0x01, "Escape", "Esc", 0x1b);
        for (int i = 0; i < 9; i++)
            Add(0x02 + i, ((char)('1' + i)).ToString(), ((char)('1' + i)).ToString(), '1' + i);
        Add(0x0b, "0", "0", '0');
        Add(0x0c, "Minus", "Minus", '-');
        Add(0x0d, "Equals", "Equals", '=');
        Add(0x0e, "Backspace", "Bksp", 0x08);
        Add(0x0f, "Tab", "Tab", 0x09);
        Letters(0x10, "QWERTYUIOP");
        Add(0x1a, "LeftBracket", "[", '[');
        Add(0x1b, "RightBracket", "]", ']');
        Add(0x1c, "Enter", "Enter", 0x0d);
        Add(0x1d, "Control", "Ctrl", 0x11);
        Letters(0x1e, "ASDFGHJKL");
        Add(0x27, "Semicolon", "Semicolon", ';');
        Add(0x28, "Apostrophe", "Quote", '\'');
        Add(0x29, "Grave", "Grave", '`');
        Add(0x2a, "Shift", "Shift", 0x10);
        Add(0x2b, "Backslash", "\\", '\\');
        Letters(0x2c, "ZXCVBNM");
        Add(0x33, "Comma", "Comma", 0xbc);
        Add(0x34, "Period", "Period", 0xbe);
        Add(0x35, "Slash", "Slash", '/');
        Add(0x36, "RightShift", "Right Shift", 0x10);
        Add(0x37, "NumMultiply", "Num *", 0);
        Add(0x38, "Alt", "Alt", 0x12);
        Add(0x39, "Space", "Space", 0x20);
        Add(0x3a, "CapsLock", "Caps Lock", 0);
        for (int i = 0; i < 10; i++)
            Add(0x3b + i, $"F{i + 1}", $"F{i + 1}", 0x70 + i);
        Add(0x45, "NumLock", "Num Lock", 0);
        Add(0x46, "ScrollLock", "Scroll Lock", 0);
        Add(0x47, "Home", "Home", 0x24);
        Add(0x48, "Up", "Up", 0x26);
        Add(0x49, "PageUp", "PgUp", 0x21);
        Add(0x4a, "NumMinus", "Num -", 0);
        Add(0x4b, "Left", "Left", 0x25);
        Add(0x4c, "Num5", "Num 5", 0);
        Add(0x4d, "Right", "Right", 0x27);
        Add(0x4e, "NumPlus", "Num +", 0);
        Add(0x4f, "End", "End", 0x23);
        Add(0x50, "Down", "Down", 0x28);
        Add(0x51, "PageDown", "PgDn", 0x22);
        Add(0x52, "Insert", "Ins", 0x2d);
        Add(0x53, "Delete", "Del", 0x2e);
        Add(0x57, "F11", "F11", 0x7a);
        Add(0x58, "F12", "F12", 0x7b);
        return keys;
    }
}
