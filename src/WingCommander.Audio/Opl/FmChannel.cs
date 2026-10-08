// C# port of ymfm (https://github.com/aaronsgiles/ymfm, commit 81aec25), YM3812 subset.
// Copyright (c) 2021, Aaron Giles. All rights reserved. BSD 3-Clause License, see LICENSE-ymfm.txt.

using static WingCommander.Audio.Opl.YmfmTables;

namespace WingCommander.Audio.Opl;

/// <summary>
/// A two-operator FM channel with operator-1 self feedback, plus the three special OPL
/// rhythm-mode output paths of channels 6, 7 and 8. On the OPL2 the modulator output used by
/// the carrier is the previous sample's (MODULATOR_DELAY).
/// </summary>
/// <remarks>C++: fm_channel&lt;opl2_registers&gt; (ymfm_fm.h, ymfm_fm.ipp).</remarks>
internal sealed class FmChannel
{
    private readonly OplRegisters _regs;
    private readonly uint _choffs;
    private FmOperator _op0 = null!;
    private FmOperator _op1 = null!;
    private short _feedback0;
    private short _feedback1;
    private short _feedbackIn;

    /// <remarks>C++: fm_channel::fm_channel.</remarks>
    public FmChannel(OplRegisters regs, uint choffs)
    {
        _regs = regs;
        _choffs = choffs;
    }

    public uint ChannelOffset => _choffs;

    /// <remarks>C++: fm_channel::assign (indices 0 and 1; 2 and 3 are always null on OPL2).</remarks>
    public void Assign(FmOperator modulator, FmOperator carrier)
    {
        _op0 = modulator;
        _op1 = carrier;
        modulator.SetChannelOffset(_choffs);
        carrier.SetChannelOffset(_choffs);
    }

    /// <remarks>C++: fm_channel::reset.</remarks>
    public void Reset()
    {
        _feedback0 = _feedback1 = 0;
        _feedbackIn = 0;
    }

    /// <remarks>C++: fm_channel::keyonoff.</remarks>
    public void KeyOnOff(uint states, KeyonType type)
    {
        _op0.KeyOnOff(Bitfield(states, 0), type);
        _op1.KeyOnOff(Bitfield(states, 1), type);
    }

    /// <remarks>C++: fm_channel::prepare. Both operators are always prepared.</remarks>
    public bool Prepare()
    {
        uint activeMask = 0;
        if (_op0.Prepare())
            activeMask |= 1;
        if (_op1.Prepare())
            activeMask |= 2;
        return activeMask != 0;
    }

    /// <remarks>C++: fm_channel::clock.</remarks>
    public void Clock(uint envCounter, int lfoRawPm)
    {
        // clock the feedback through
        _feedback0 = _feedback1;
        _feedback1 = _feedbackIn;

        _op0.Clock(envCounter, lfoRawPm);
        _op1.Clock(envCounter, lfoRawPm);
    }

    /// <remarks>C++: fm_channel::output_2op.</remarks>
    public void Output2Op(ref int output, int rshift, int clipmax)
    {
        // AM amount is the same across all operators; compute it once
        uint amOffset = _regs.LfoAmOffset(_choffs);

        // operator 1 has optional self-feedback
        int opmod = 0;
        uint feedback = _regs.ChFeedback(_choffs);
        if (feedback != 0)
            opmod = (_feedback0 + _feedback1) >> (int)(10 - feedback);

        // compute the 14-bit volume/value of operator 1 and update the feedback
        _feedbackIn = (short)_op0.ComputeVolume(unchecked(_op0.Phase + (uint)opmod), amOffset);

        // (ch_output_any is always 1 on OPL2)

        // Algorithms for two-operator case:
        //    0: O1 -> O2 -> out
        //    1: (O1 + O2) -> out
        int result;
        if (Bitfield(_regs.ChAlgorithm(_choffs), 0) == 0)
        {
            // OPL2 uses the previous sample for modulation instead of the current sample
            opmod = _feedback1 >> 1;
            result = _op1.ComputeVolume(unchecked(_op1.Phase + (uint)opmod), amOffset) >> rshift;
        }
        else
        {
            result = _feedback1 >> rshift;
            result += _op1.ComputeVolume(_op1.Phase, amOffset) >> rshift;
            int clipmin = -clipmax - 1;
            result = Clamp(result, clipmin, clipmax);
        }

        // add to the output (OPL2 has a single output)
        output += result;
    }

    /// <summary>Channel 6 in rhythm mode: bass drum.</summary>
    /// <remarks>C++: fm_channel::output_rhythm_ch6.</remarks>
    public void OutputRhythmCh6(ref int output, int rshift, int clipmax)
    {
        uint amOffset = _regs.LfoAmOffset(_choffs);

        // Bass Drum: this uses operators 12 and 15 (i.e., channel 6)
        // in an almost-normal way, except that if the algorithm is 1,
        // the first operator is ignored instead of added in

        // operator 1 has optional self-feedback
        int opmod = 0;
        uint feedback = _regs.ChFeedback(_choffs);
        if (feedback != 0)
            opmod = (_feedback0 + _feedback1) >> (int)(10 - feedback);

        // compute the 14-bit volume/value of operator 1 and update the feedback
        _feedbackIn = (short)_op0.ComputeVolume(unchecked(_op0.Phase + (uint)opmod), amOffset);
        int opout1 = _feedbackIn;

        // compute the 14-bit volume/value of operator 2, which is the result
        opmod = Bitfield(_regs.ChAlgorithm(_choffs), 0) != 0 ? 0 : (opout1 >> 1);
        int result = _op1.ComputeVolume(unchecked(_op1.Phase + (uint)opmod), amOffset) >> rshift;

        // add to the output
        output += result * 2;
    }

    /// <summary>Channel 7 in rhythm mode: high hat and snare drum.</summary>
    /// <remarks>C++: fm_channel::output_rhythm_ch7.</remarks>
    public void OutputRhythmCh7(uint phaseSelect, ref int output, int rshift, int clipmax)
    {
        uint amOffset = _regs.LfoAmOffset(_choffs);
        uint noiseState = Bitfield(_regs.NoiseState, 0);

        // High Hat: this uses the envelope from operator 13 (channel 7),
        // and a combination of noise and the operator 13/17 phase select
        // to compute the phase
        uint phase = (phaseSelect << 9) | (0xd0u >> (int)(2 * (noiseState ^ phaseSelect)));
        int result = _op0.ComputeVolume(phase, amOffset) >> rshift;

        // Snare Drum: this uses the envelope from operator 16 (channel 7),
        // and a combination of noise and operator 13 phase to pick a phase
        uint op13Phase = _op0.Phase;
        phase = (0x100u << (int)Bitfield(op13Phase, 8)) ^ (noiseState << 8);
        result += _op1.ComputeVolume(phase, amOffset) >> rshift;
        result = Clamp(result, -clipmax - 1, clipmax);

        // add to the output
        output += result * 2;
    }

    /// <summary>Channel 8 in rhythm mode: tom tom and top cymbal.</summary>
    /// <remarks>C++: fm_channel::output_rhythm_ch8.</remarks>
    public void OutputRhythmCh8(uint phaseSelect, ref int output, int rshift, int clipmax)
    {
        uint amOffset = _regs.LfoAmOffset(_choffs);

        // Tom Tom: this is just a single operator processed normally
        int result = _op0.ComputeVolume(_op0.Phase, amOffset) >> rshift;

        // Top Cymbal: this uses the envelope from operator 17 (channel 8),
        // and the operator 13/17 phase select to compute the phase
        uint phase = 0x100u | (phaseSelect << 9);
        result += _op1.ComputeVolume(phase, amOffset) >> rshift;
        result = Clamp(result, -clipmax - 1, clipmax);

        // add to the output
        output += result * 2;
    }
}
