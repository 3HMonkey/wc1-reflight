namespace WingCommander.Audio.OriginFx;

/// <summary>
/// The 36 DOS sound-effect records, indexed by the 1-based game sound number. Each row is
/// <c>{flags, program + 1, note, velocity, duration (u16 LE, 60 Hz ticks), glide target, unused}</c>.
/// Flags: 1 = chain to the next row on expiry, 2 = glide one semitone per expiry towards the
/// target, 4 = sustain (never expires), 8 = retrigger on expiry.
/// </summary>
/// <remarks>C: g_aabOriginFxSoundRecords (src/sdl/originfx.cpp; WC.EXE 2231:051e-063d).</remarks>
public static class OriginFxSoundRecords
{
    /// <summary>Number of records (highest valid sound number).</summary>
    public const int Count = 36;

    /// <summary>Bytes per record.</summary>
    public const int RecordSize = 8;

    /// <summary>Flag: continue with the next row when the record expires.</summary>
    public const byte FlagChain = 1;

    /// <summary>Flag: step the note towards the glide target on every expiry.</summary>
    public const byte FlagGlide = 2;

    /// <summary>Flag: the effect never expires.</summary>
    public const byte FlagSustain = 4;

    /// <summary>Flag: restart the note on every expiry.</summary>
    public const byte FlagRetrigger = 8;

    private static ReadOnlySpan<byte> Table =>
    [
        0, 1, 64, 64, 60, 0, 0, 0,
        2, 2, 64, 64, 1, 0, 0, 0,
        0, 39, 64, 64, 60, 0, 0, 0,
        0, 4, 64, 64, 60, 0, 0, 0,
        0, 7, 64, 64, 6, 0, 0, 0,
        0, 8, 64, 64, 30, 0, 0, 0,
        0, 9, 64, 64, 10, 0, 0, 0,
        0, 10, 64, 64, 60, 0, 0, 0,
        0, 11, 64, 64, 6, 0, 0, 0,
        0, 12, 64, 64, 10, 0, 0, 0,
        4, 13, 64, 64, 60, 0, 0, 0,
        4, 14, 64, 64, 60, 0, 0, 0,
        0, 15, 64, 64, 6, 0, 0, 0,
        8, 19, 64, 64, 60, 0, 0, 0,
        4, 20, 64, 64, 60, 0, 0, 0,
        0, 21, 64, 64, 60, 0, 0, 0,
        4, 22, 64, 64, 60, 0, 0, 0,
        2, 23, 84, 64, 6, 0, 57, 0,
        0, 24, 64, 64, 60, 0, 0, 0,
        2, 41, 24, 64, 2, 0, 127, 0,
        0, 42, 64, 64, 40, 0, 0, 0,
        0, 43, 64, 64, 40, 0, 0, 0,
        0, 45, 64, 64, 5, 0, 0, 0,
        0, 46, 64, 64, 5, 0, 0, 0,
        0, 47, 64, 64, 5, 0, 0, 0,
        0, 48, 64, 64, 40, 0, 0, 0,
        0, 64, 64, 64, 40, 0, 0, 0,
        0, 125, 64, 64, 40, 0, 0, 0,
        0, 62, 64, 64, 40, 0, 0, 0,
        0, 63, 64, 64, 40, 0, 0, 0,
        0, 106, 64, 64, 60, 0, 0, 0,
        8, 107, 64, 64, 40, 0, 0, 0,
        0, 109, 64, 64, 60, 0, 0, 0,
        0, 110, 64, 64, 80, 0, 0, 0,
        0, 111, 64, 64, 40, 0, 0, 0,
        0, 112, 64, 64, 60, 0, 0, 0,
    ];

    /// <summary>The record of a 1-based sound number (1..36).</summary>
    public static ReadOnlySpan<byte> Get(int soundNumber)
    {
        if (soundNumber < 1 || soundNumber > Count)
            throw new ArgumentOutOfRangeException(nameof(soundNumber));
        return Table.Slice((soundNumber - 1) * RecordSize, RecordSize);
    }

    /// <summary>Byte <paramref name="field"/> of the 0-based row <paramref name="row"/> (hot path).</summary>
    internal static byte Field(int row, int field) => Table[row * RecordSize + field];

    /// <summary>Duration field (60 Hz ticks) of a 0-based row.</summary>
    internal static ushort Duration(int row) => (ushort)(Field(row, 4) | (Field(row, 5) << 8));
}
