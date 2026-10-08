using System.Buffers.Binary;

namespace WingCommander.Audio.OriginFx;

/// <summary>
/// Read-only view of one 48-byte AdLib timbre record of WINGLDR.TIM (layout in
/// docs/analysis/audio.md §3.3). Used by tools and tests; the synthesiser reads the bank
/// bytes directly.
/// </summary>
public readonly struct OriginFxTimbre
{
    private readonly byte[] _record;

    internal OriginFxTimbre(int index, byte[] record)
    {
        Index = index;
        _record = record;
    }

    /// <summary>Record index in the bank.</summary>
    public int Index { get; }

    /// <summary>The raw 48 bytes.</summary>
    public ReadOnlySpan<byte> Raw => _record;

    /// <summary>Modulator registers 0x20, 0x40, 0x60, 0x80, 0xE0 (bytes 0..4).</summary>
    public ReadOnlySpan<byte> ModulatorRegisters => _record.AsSpan(0, 5);

    /// <summary>Carrier registers 0x20, 0x40, 0x60, 0x80, 0xE0 (bytes 5..9).</summary>
    public ReadOnlySpan<byte> CarrierRegisters => _record.AsSpan(5, 5);

    /// <summary>Channel register 0xC0: feedback (bits 1..3) and connection (bit 0).</summary>
    public byte FeedbackConnection => _record[10];

    /// <summary>0 = melodic; 6..10 = OPL rhythm voice (bass drum, snare, tom, cymbal, hi-hat).</summary>
    public byte RhythmVoice => _record[11];

    /// <summary>Carrier velocity sensitivity 0..7.</summary>
    public byte CarrierVelocitySensitivity => _record[12];

    /// <summary>Modulator velocity sensitivity 0..7.</summary>
    public byte ModulatorVelocitySensitivity => _record[13];

    /// <summary>Pitch-bend range factor (96 = +/-12 semitones).</summary>
    public byte PitchBendRange => _record[14];

    /// <summary>Mod-wheel (CC1) depth scale.</summary>
    public byte ModWheelScale => _record[15];

    /// <summary>Vibrato phase increment per 60 Hz tick.</summary>
    public byte VibratoRate => _record[16];

    /// <summary>Vibrato base depth.</summary>
    public byte VibratoDepth => _record[17];

    /// <summary>Initial pitch-envelope offset at note on (1/256 semitone units).</summary>
    public short InitialPitch => Int16(18);

    /// <summary>Pitch-envelope stage <paramref name="stage"/> (0 = A, 1 = B, 2 = C, 3 = release): rate per tick and target.</summary>
    public (ushort Rate, short Target) EnvelopeStage(int stage)
    {
        if ((uint)stage > 3)
            throw new ArgumentOutOfRangeException(nameof(stage));
        return ((ushort)Int16(20 + stage * 4), Int16(22 + stage * 4));
    }

    /// <summary>Fixed detune added to the pitch (1/256 semitone units).</summary>
    public short Detune => Int16(36);

    /// <summary>Non-zero: the next record is layered on the same note.</summary>
    public byte Link => _record[38];

    /// <summary>Key-tracking shift (negative = inverted tracking).</summary>
    public sbyte KeyTracking => (sbyte)_record[39];

    /// <summary>Program number this record answers to (0..127 melodic, 128.. percussion).</summary>
    public byte Program => _record[47];

    private short Int16(int offset) => BinaryPrimitives.ReadInt16LittleEndian(_record.AsSpan(offset));
}
