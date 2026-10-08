namespace WingCommander.Audio.OriginFx;

/// <summary>
/// Constant tables of the OriginFX AdLib driver as reconstructed by the SDL port
/// (STRAX.DRV operator layout, WC.EXE percussion translation and drum programs).
/// </summary>
/// <remarks>C: g_abOriginFx* tables (src/sdl/originfx.cpp).</remarks>
internal static class OriginFxTables
{
    /// <summary>Number of GM percussion notes covered by the translation tables.</summary>
    public const int PercussionNoteCount = 77;

    /// <summary>OPL rhythm key-on bits for logical voices 6..10 (BD, SD, TT, CY, HH).</summary>
    /// <remarks>C: g_abOriginFxRhythmBits (STRAX.DRV 0000:01dc-020e).</remarks>
    public static ReadOnlySpan<byte> RhythmBits => [0x10, 0x08, 0x04, 0x02, 0x01];

    /// <summary>Carrier operator offsets; second half used in rhythm mode.</summary>
    /// <remarks>C: g_abOriginFxCarrierOffsets.</remarks>
    public static ReadOnlySpan<byte> CarrierOffsets =>
    [
        3, 4, 5, 11, 12, 13, 19, 20, 21,
        3, 4, 5, 11, 12, 13, 19, 20, 18, 21, 17,
    ];

    /// <summary>Modulator operator offsets; second half used in rhythm mode.</summary>
    /// <remarks>C: g_abOriginFxModulatorOffsets.</remarks>
    public static ReadOnlySpan<byte> ModulatorOffsets =>
    [
        0, 1, 2, 8, 9, 10, 16, 17, 18,
        0, 1, 2, 8, 9, 10, 16, 20, 18, 21, 17,
    ];

    /// <summary>GM percussion note -> pseudo-channel + 1 (0 = ignored).</summary>
    /// <remarks>C: g_abOriginFxPercussionChannels (WC.EXE 2231:7967).</remarks>
    public static ReadOnlySpan<byte> PercussionChannels =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 10, 10, 18, 11, 0, 12, 13, 17, 13, 16, 13, 14, 13,
        13, 15, 13, 19, 0, 0, 0, 0, 21, 0, 0, 0, 26, 26, 25, 20,
        20, 0, 0, 21, 21, 22, 23, 0, 0, 24, 0, 20, 0,
    ];

    /// <summary>GM percussion note -> fixed pitch played on the pseudo-channel.</summary>
    /// <remarks>C: g_abOriginFxPercussionPitches (WC.EXE 2231:79b4).</remarks>
    public static ReadOnlySpan<byte> PercussionPitches =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 48, 48, 48, 48, 0, 48, 42, 71, 42, 71, 47, 71, 47,
        52, 79, 52, 77, 0, 0, 0, 0, 71, 0, 0, 0, 72, 79, 79, 64,
        58, 0, 0, 89, 84, 48, 72, 0, 0, 36, 0, 96, 0,
    ];

    /// <summary>Programs of percussion pseudo-channels 9..25.</summary>
    /// <remarks>C: g_abOriginFxPercussionPrograms (WC.EXE 19c5:1682-174a).</remarks>
    public static ReadOnlySpan<byte> PercussionPrograms =>
    [
        0x80, 0x72, 0x83, 0x71, 0x86, 0x87, 0x85, 0x84, 0x81,
        0x88, 0x8d, 0x8f, 0x90, 0x91, 0x93, 0x8c, 0x8b,
    ];

    /// <summary>F-numbers for one octave starting at F#, with the next C# as interpolation end.</summary>
    /// <remarks>C: frequencyNumbers (OriginFxWriteVoiceFrequency). A static array, not a span
    /// literal: non-byte span literals may allocate in unoptimised builds and this table is used
    /// on the audio thread.</remarks>
    public static ReadOnlySpan<ushort> FrequencyNumbers => FrequencyNumberTable;

    private static readonly ushort[] FrequencyNumberTable = [485, 514, 544, 577, 611, 647, 686, 727, 770, 816, 864, 915, 970];

    /// <summary>Timbre offsets of the envelope rates per envelope state (0 = release, 2..4 = stages A..C).</summary>
    /// <remarks>C: envelopeRateOffsets (OriginFxService).</remarks>
    public static ReadOnlySpan<byte> EnvelopeRateOffsets => [32, 0, 20, 24, 28];

    /// <summary>Timbre offsets of the envelope targets per envelope state.</summary>
    /// <remarks>C: envelopeTargetOffsets (OriginFxService).</remarks>
    public static ReadOnlySpan<byte> EnvelopeTargetOffsets => [34, 0, 22, 26, 30];
}
