// C# port of ymfm (https://github.com/aaronsgiles/ymfm, commit 81aec25), YM3812 subset.
// Copyright (c) 2021, Aaron Giles. All rights reserved. BSD 3-Clause License, see LICENSE-ymfm.txt.

using static WingCommander.Audio.Opl.YmfmTables;

namespace WingCommander.Audio.Opl;

/// <summary>
/// The OPL2 register file plus the free-running noise generator and the two fixed LFOs.
/// Only the YM3812 revision (<c>opl_registers_base&lt;2&gt;</c>) is ported: 9 two-operator
/// channels, 4 waveforms, rhythm mode, no OPL3 extensions.
/// </summary>
/// <remarks>C++: opl_registers_base&lt;2&gt; = opl2_registers (ymfm_opl.h, ymfm_opl.cpp).</remarks>
internal sealed class OplRegisters
{
    public const int Channels = 9;
    public const uint AllChannels = (1u << Channels) - 1;
    public const int Operators = Channels * 2;
    public const int Waveforms = 4;
    public const int Registers = 0x100;
    public const int RegMode = 0x04;
    public const int DefaultPrescale = 4;
    public const byte StatusTimerA = 0x40;
    public const byte StatusTimerB = 0x20;
    public const byte StatusBusy = 0;
    public const byte StatusIrq = 0x80;

    /// <summary>Channel number reported by <see cref="Write"/> for writes to the rhythm key-on register.</summary>
    /// <remarks>C++: fm_registers_base::RHYTHM_CHANNEL.</remarks>
    public const uint RhythmChannel = 0xff;

    private readonly byte[] _regData = new byte[Registers];
    private ushort _lfoAmCounter;
    private ushort _lfoPmCounter;
    private uint _noiseLfsr = 1;
    private byte _lfoAm;

    /// <remarks>C++: opl_registers_base::channel_offset.</remarks>
    public static uint ChannelOffset(uint chnum) => chnum;

    /// <remarks>C++: opl_registers_base::operator_offset.</remarks>
    public static uint OperatorOffset(uint opnum) => opnum + 2 * (opnum / 6);

    /// <summary>Fixed OPL2 operator pairs per channel (modulator, carrier).</summary>
    /// <remarks>C++: opl_registers_base::operator_map (Revision &lt;= 2 fixed map).</remarks>
    public static (int Modulator, int Carrier) OperatorPair(int chnum) => chnum switch
    {
        0 => (0, 3),
        1 => (1, 4),
        2 => (2, 5),
        3 => (6, 9),
        4 => (7, 10),
        5 => (8, 11),
        6 => (12, 15),
        7 => (13, 16),
        _ => (14, 17),
    };

    /// <remarks>C++: opl_registers_base::reset. The LFO and noise state deliberately survive.</remarks>
    public void Reset() => Array.Clear(_regData);

    /// <remarks>C++: opl_registers_base::read.</remarks>
    public byte Read(int index) => _regData[index];

    /// <summary>
    /// Stores a register value; returns true when the write changes key-on state, with the
    /// affected channel (or <see cref="RhythmChannel"/>) and operator mask.
    /// </summary>
    /// <remarks>C++: opl_registers_base::write.</remarks>
    public bool Write(int index, byte data, out uint channel, out uint opmask)
    {
        // writes to the mode register with high bit set ignore the low bits
        if (index == RegMode && Bitfield(data, 7) != 0)
            _regData[index] |= 0x80;
        else
            _regData[index] = data;

        // handle writes to the rhythm keyons
        if (index == 0xbd)
        {
            channel = RhythmChannel;
            opmask = Bitfield(data, 5) != 0 ? Bitfield(data, 0, 5) : 0;
            return true;
        }

        // handle writes to the channel keyons
        if ((index & 0xf0) == 0xb0)
        {
            channel = (uint)(index & 0x0f);
            if (channel < Channels)
            {
                opmask = Bitfield(data, 5) != 0 ? 15u : 0u;
                return true;
            }
        }
        channel = 0;
        opmask = 0;
        return false;
    }

    /// <summary>Clocks the noise LFSR and both LFOs; returns the raw PM LFO value.</summary>
    /// <remarks>C++: opl_registers_base::clock_noise_and_lfo / opl_clock_noise_and_lfo.</remarks>
    public int ClockNoiseAndLfo()
    {
        // OPL has a 23-bit noise generator for the rhythm section, running at
        // a constant rate, used only for percussion input
        _noiseLfsr <<= 1;
        _noiseLfsr |= Bitfield(_noiseLfsr, 23) ^ Bitfield(_noiseLfsr, 9) ^ Bitfield(_noiseLfsr, 8) ^ Bitfield(_noiseLfsr, 1);

        // the AM LFO has 210*64 steps
        uint amCounter = _lfoAmCounter++;
        if (amCounter >= 210 * 64 - 1)
            _lfoAmCounter = 0;

        // low 8 bits are fractional; depth 0 is divided by 2, while depth 1 is times 2
        int shift = 9 - 2 * (int)LfoAmDepth;

        // AM value is the upper bits of the value, inverted across the midpoint
        // to produce a triangle
        _lfoAm = (byte)(((amCounter < 105 * 64) ? amCounter : (210u * 64 + 63 - amCounter)) >> shift);

        // the PM LFO has 8192 steps, or a nominal period of 6.1Hz
        uint pmCounter = _lfoPmCounter++;
        return PmScaleAt(Bitfield(pmCounter, 10, 3)) >> (int)(LfoPmDepth ^ 1);
    }

    /// <summary>AM offset of the LFO; on OPL this is the same for every channel.</summary>
    /// <remarks>C++: opl_registers_base::lfo_am_offset.</remarks>
    public uint LfoAmOffset(uint choffs) => _lfoAm;

    /// <remarks>C++: opl_registers_base::noise_state.</remarks>
    public uint NoiseState => _noiseLfsr >> 23;

    /// <summary>Fills the operator cache from the current registers.</summary>
    /// <remarks>C++: opl_registers_base::cache_operator_data.</remarks>
    public void CacheOperatorData(uint choffs, uint opoffs, OpDataCache cache)
    {
        // set up the easy stuff
        cache.Waveform = YmfmTables.Waveforms[OpWaveform(opoffs) % Waveforms];

        // get frequency from the channel
        uint blockFreq = cache.BlockFreq = ChBlockFreq(choffs);

        // compute the keycode: the 4-bit keycode uses the top 3 bits plus one of the next two bits
        uint keycode = Bitfield(blockFreq, 10, 3) << 1;

        // lowest bit is determined by note_select(); note that it is
        // actually reversed from what the manual says, however
        keycode |= Bitfield(blockFreq, 9 - (int)NoteSelect, 1);

        // no detune adjustment on OPL
        cache.Detune = 0;

        // multiple value, as an x.1 value (0 means 0.5)
        // replace the low bit with a table lookup to give 0,1,2,3,4,5,6,7,8,9,10,10,12,12,15,15
        uint multiple = OpMultiple(opoffs);
        cache.Multiple = ((multiple & 0xe) | Bitfield(0xc2aa, (int)multiple)) * 2;
        if (cache.Multiple == 0)
            cache.Multiple = 1;

        // phase step, or PHASE_STEP_DYNAMIC if PM is active; this depends on block_freq, detune,
        // and multiple, so compute it after we've done those
        if (OpLfoPmEnable(opoffs) == 0)
            cache.PhaseStep = ComputePhaseStep(choffs, opoffs, cache, 0);
        else
            cache.PhaseStep = OpDataCache.PhaseStepDynamic;

        // total level, scaled by 8
        cache.TotalLevel = OpTotalLevel(opoffs) << 3;

        // pre-add key scale level
        uint ksl = OpKsl(opoffs);
        if (ksl != 0)
            cache.TotalLevel += OplKeyScaleAtten(Bitfield(blockFreq, 10, 3), Bitfield(blockFreq, 6, 4)) << (int)ksl;

        // 4-bit sustain level, but 15 means 31 so effectively 5 bits
        cache.EgSustain = OpSustainLevel(opoffs);
        cache.EgSustain |= (cache.EgSustain + 1) & 0x10;
        cache.EgSustain <<= 5;

        // determine KSR adjustment for envelope rates
        uint ksrVal = keycode >> (int)(2 * (OpKsr(opoffs) ^ 1));
        cache.EgRate[(int)EnvelopeState.Attack] = (byte)EffectiveRate(OpAttackRate(opoffs) * 4, ksrVal);
        cache.EgRate[(int)EnvelopeState.Decay] = (byte)EffectiveRate(OpDecayRate(opoffs) * 4, ksrVal);
        cache.EgRate[(int)EnvelopeState.Sustain] = OpEgSustain(opoffs) != 0 ? (byte)0 : (byte)EffectiveRate(OpReleaseRate(opoffs) * 4, ksrVal);
        cache.EgRate[(int)EnvelopeState.Release] = (byte)EffectiveRate(OpReleaseRate(opoffs) * 4, ksrVal);
        cache.EgRate[(int)EnvelopeState.Depress] = 0x3f;
    }

    /// <summary>Computes the phase step for the cached frequency, given a raw PM LFO value.</summary>
    /// <remarks>C++: opl_registers_base::compute_phase_step / opl_compute_phase_step.</remarks>
    public uint ComputePhaseStep(uint choffs, uint opoffs, OpDataCache cache, int lfoRawPm)
    {
        int pm = OpLfoPmEnable(opoffs) != 0 ? lfoRawPm : 0;
        uint blockFreq = cache.BlockFreq;

        // extract frequency number as a 12-bit fraction
        uint fnum = Bitfield(blockFreq, 0, 10) << 2;

        // apply the phase adjustment based on the upper 3 bits
        // of FNUM and the PM depth parameters (unsigned arithmetic as in C++)
        fnum += unchecked((uint)(pm * (int)Bitfield(blockFreq, 7, 3))) >> 1;

        // keep fnum to 12 bits
        fnum &= 0xfff;

        // apply block shift to compute phase step
        uint block = Bitfield(blockFreq, 10, 3);
        uint phaseStep = (fnum << (int)block) >> 2;

        // apply frequency multiplier (which is cached as an x.1 value)
        return unchecked(phaseStep * cache.Multiple) >> 1;
    }

    // system-wide registers
    public uint Test => RegBits(0x01, 0, 8);
    public uint WaveformEnable => RegBits(0x01, 5, 1);
    public uint TimerAValue => RegBits(0x02, 0, 8) * 4;
    public uint TimerBValue => RegBits(0x03, 0, 8);
    public uint StatusMask => RegBits(0x04, 0, 8) & 0x78;
    public uint IrqReset => RegBits(0x04, 7, 1);
    public uint ResetTimerB => RegBits(0x04, 7, 1) | RegBits(0x04, 5, 1);
    public uint ResetTimerA => RegBits(0x04, 7, 1) | RegBits(0x04, 6, 1);
    public uint LoadTimerB => RegBits(0x04, 1, 1);
    public uint LoadTimerA => RegBits(0x04, 0, 1);
    public uint Csm => RegBits(0x08, 7, 1);
    public uint NoteSelect => RegBits(0x08, 6, 1);
    public uint LfoAmDepth => RegBits(0xbd, 7, 1);
    public uint LfoPmDepth => RegBits(0xbd, 6, 1);
    public uint RhythmEnable => RegBits(0xbd, 5, 1);

    // per-channel registers
    public uint ChBlockFreq(uint choffs) => (RegBits(0xb0, 0, 5, choffs) << 8) | RegBits(0xa0, 0, 8, choffs);
    public uint ChFeedback(uint choffs) => RegBits(0xc0, 1, 3, choffs);
    public uint ChAlgorithm(uint choffs) => RegBits(0xc0, 0, 1, choffs);

    // per-operator registers
    public uint OpLfoAmEnable(uint opoffs) => RegBits(0x20, 7, 1, opoffs);
    public uint OpLfoPmEnable(uint opoffs) => RegBits(0x20, 6, 1, opoffs);
    public uint OpEgSustain(uint opoffs) => RegBits(0x20, 5, 1, opoffs);
    public uint OpKsr(uint opoffs) => RegBits(0x20, 4, 1, opoffs);
    public uint OpMultiple(uint opoffs) => RegBits(0x20, 0, 4, opoffs);

    public uint OpKsl(uint opoffs)
    {
        uint temp = RegBits(0x40, 6, 2, opoffs);
        return Bitfield(temp, 1) | (Bitfield(temp, 0) << 1);
    }

    public uint OpTotalLevel(uint opoffs) => RegBits(0x40, 0, 6, opoffs);
    public uint OpAttackRate(uint opoffs) => RegBits(0x60, 4, 4, opoffs);
    public uint OpDecayRate(uint opoffs) => RegBits(0x60, 0, 4, opoffs);
    public uint OpSustainLevel(uint opoffs) => RegBits(0x80, 4, 4, opoffs);
    public uint OpReleaseRate(uint opoffs) => RegBits(0x80, 0, 4, opoffs);
    public uint OpWaveform(uint opoffs) => WaveformEnable != 0 ? RegBits(0xe0, 0, 2, opoffs) : 0;

    /// <remarks>C++: opl_registers_base::byte.</remarks>
    private uint RegBits(uint offset, int start, int count, uint extraOffset = 0) =>
        Bitfield(_regData[offset + extraOffset], start, count);
}
