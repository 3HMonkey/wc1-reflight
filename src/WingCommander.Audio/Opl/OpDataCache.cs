// C# port of ymfm (https://github.com/aaronsgiles/ymfm, commit 81aec25), YM3812 subset.
// Copyright (c) 2021, Aaron Giles. All rights reserved. BSD 3-Clause License, see LICENSE-ymfm.txt.

namespace WingCommander.Audio.Opl;

/// <summary>Envelope generator states (only attack..release occur on the OPL2).</summary>
/// <remarks>C++: envelope_state (ymfm.h).</remarks>
internal enum EnvelopeState
{
    Depress = 0,
    Attack = 1,
    Decay = 2,
    Sustain = 3,
    Release = 4,
    Reverb = 5,
}

/// <summary>Key-on sources; the effective key state is the OR over all of them.</summary>
/// <remarks>C++: keyon_type (ymfm_fm.h).</remarks>
internal enum KeyonType
{
    Normal = 0,
    Rhythm = 1,
    Csm = 2,
}

/// <summary>
/// Per-operator values computed once at "prepare" time from the registers and reused for every
/// sample until a register changes.
/// </summary>
/// <remarks>C++: opdata_cache (ymfm_fm.h).</remarks>
internal sealed class OpDataCache
{
    /// <summary>Marker for "recompute the phase step every sample" (PM LFO active).</summary>
    public const uint PhaseStepDynamic = 1;

    /// <summary>Base of the selected waveform table.</summary>
    public ushort[] Waveform = YmfmTables.Waveforms[0];

    /// <summary>Phase step, or <see cref="PhaseStepDynamic"/>.</summary>
    public uint PhaseStep;

    /// <summary>Total level * 8 + key scale level.</summary>
    public uint TotalLevel;

    /// <summary>Raw block/frequency value (block in bits 10..12).</summary>
    public uint BlockFreq;

    /// <summary>Detune (always 0 on OPL).</summary>
    public int Detune;

    /// <summary>Frequency multiple as an x.1 value.</summary>
    public uint Multiple;

    /// <summary>Sustain level shifted up to envelope units.</summary>
    public uint EgSustain;

    /// <summary>Envelope rate per <see cref="EnvelopeState"/>, including KSR.</summary>
    public readonly byte[] EgRate = new byte[6];

    /// <summary>Envelope shift (always 0 on OPL).</summary>
    public byte EgShift = 0;
}
