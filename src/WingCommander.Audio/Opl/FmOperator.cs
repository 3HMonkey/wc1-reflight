// C# port of ymfm (https://github.com/aaronsgiles/ymfm, commit 81aec25), YM3812 subset.
// Copyright (c) 2021, Aaron Giles. All rights reserved. BSD 3-Clause License, see LICENSE-ymfm.txt.

using static WingCommander.Audio.Opl.YmfmTables;

namespace WingCommander.Audio.Opl;

/// <summary>
/// One FM operator ("slot"): phase generator, envelope generator and the log-sin/exp output
/// stage. The OPL2 has neither SSG-EG, nor a depress or reverb stage, so those branches of the
/// generic ymfm template are compiled out exactly as the C++ constants do.
/// </summary>
/// <remarks>C++: fm_operator&lt;opl2_registers&gt; (ymfm_fm.h, ymfm_fm.ipp).</remarks>
internal sealed class FmOperator
{
    /// <summary>"Quiet" attenuation used to skip work.</summary>
    private const uint EgQuiet = 0x380;

    private readonly OplRegisters _regs;
    private readonly uint _opoffs;
    private readonly OpDataCache _cache = new();
    private uint _choffs;
    private uint _phase;
    private ushort _envAttenuation = 0x3ff;
    private EnvelopeState _envState = EnvelopeState.Release;
    private byte _keyState;
    private byte _keyonLive;

    /// <remarks>C++: fm_operator::fm_operator.</remarks>
    public FmOperator(OplRegisters regs, uint opoffs)
    {
        _regs = regs;
        _opoffs = opoffs;
    }

    public uint OpOffset => _opoffs;

    public uint ChannelOffset => _choffs;

    /// <summary>Current 10-bit phase.</summary>
    /// <remarks>C++: fm_operator::phase.</remarks>
    public uint Phase => _phase >> 10;

    /// <remarks>C++: fm_operator::set_choffs.</remarks>
    public void SetChannelOffset(uint choffs) => _choffs = choffs;

    /// <remarks>C++: fm_operator::reset.</remarks>
    public void Reset()
    {
        _phase = 0;
        _envAttenuation = 0x3ff;
        _envState = EnvelopeState.Release;
        _keyState = 0;
        _keyonLive = 0;
    }

    /// <summary>Caches register data and clocks the key state; returns true while audible.</summary>
    /// <remarks>C++: fm_operator::prepare.</remarks>
    public bool Prepare()
    {
        // cache the data
        _regs.CacheOperatorData(_choffs, _opoffs, _cache);

        // clock the key state
        ClockKeystate(_keyonLive != 0 ? 1u : 0u);
        _keyonLive = (byte)(_keyonLive & ~(1 << (int)KeyonType.Csm));

        // we're active until we're quiet after the release
        return _envState != EnvelopeState.Release || _envAttenuation < EgQuiet;
    }

    /// <remarks>C++: fm_operator::clock.</remarks>
    public void Clock(uint envCounter, int lfoRawPm)
    {
        // (no SSG-EG on OPL: m_ssg_inverted is simply cleared)

        // clock the envelope if on an envelope cycle; env_counter is a x.2 value
        if (Bitfield(envCounter, 0, 2) == 0)
            ClockEnvelope(envCounter >> 2);

        // clock the phase
        ClockPhase(lfoRawPm);
    }

    /// <summary>Computes the 14-bit signed output for a phase and an AM offset.</summary>
    /// <remarks>C++: fm_operator::compute_volume.</remarks>
    public int ComputeVolume(uint phase, uint amOffset)
    {
        // early out if the envelope is effectively off
        if (_envAttenuation > EgQuiet)
            return 0;

        // get the absolute value of the sin, as attenuation, as a 4.8 fixed point value
        uint sinAttenuation = _cache.Waveform[phase & (WaveformLength - 1)];

        // get the attenuation from the envelope generator as a 4.6 value, shifted up to 4.8
        uint envAttenuation = EnvelopeAttenuation(amOffset) << 2;

        // combine into a 5.8 value, then convert from attenuation to 13-bit linear volume
        int result = (int)AttenuationToVolume((sinAttenuation & 0x7fff) + envAttenuation);

        // negate if in the negative part of the sin wave (sign bit gives 14 bits)
        return Bitfield(sinAttenuation, 15) != 0 ? -result : result;
    }

    /// <remarks>C++: fm_operator::keyonoff.</remarks>
    public void KeyOnOff(uint on, KeyonType type) =>
        _keyonLive = (byte)((_keyonLive & ~(1 << (int)type)) | (int)(Bitfield(on, 0) << (int)type));

    /// <remarks>C++: fm_operator::start_attack.</remarks>
    private void StartAttack(bool isRestart = false)
    {
        // don't change anything if already in attack state
        if (_envState == EnvelopeState.Attack)
            return;
        _envState = EnvelopeState.Attack;

        // reset the phase when we start an attack due to a key on
        if (!isRestart)
            _phase = 0;

        // if the attack rate >= 62 then immediately go to max attenuation
        if (_cache.EgRate[(int)EnvelopeState.Attack] >= 62)
            _envAttenuation = 0;
    }

    /// <remarks>C++: fm_operator::start_release.</remarks>
    private void StartRelease()
    {
        // don't change anything if already in release state
        if (_envState >= EnvelopeState.Release)
            return;
        _envState = EnvelopeState.Release;
    }

    /// <remarks>C++: fm_operator::clock_keystate.</remarks>
    private void ClockKeystate(uint keystate)
    {
        // has the key changed?
        if ((keystate ^ _keyState) != 0)
        {
            _keyState = (byte)keystate;

            // if the key has turned on, start the attack; otherwise start the release
            if (keystate != 0)
                StartAttack();
            else
                StartRelease();
        }
    }

    /// <remarks>C++: fm_operator::clock_envelope.</remarks>
    private void ClockEnvelope(uint envCounter)
    {
        // handle attack->decay transitions
        if (_envState == EnvelopeState.Attack && _envAttenuation == 0)
            _envState = EnvelopeState.Decay;

        // handle decay->sustain transitions; it is important to do this immediately
        // after the attack->decay transition above in the event that the sustain level
        // is set to 0 (in which case we will skip right to sustain without doing any decay)
        if (_envState == EnvelopeState.Decay && _envAttenuation >= _cache.EgSustain)
            _envState = EnvelopeState.Sustain;

        // fetch the appropriate 6-bit rate value from the cache
        uint rate = _cache.EgRate[(int)_envState];

        // compute the rate shift value; this is the shift needed to
        // apply to the env_counter such that it becomes a 5.11 fixed point number
        int rateShift = (int)(rate >> 2);
        envCounter <<= rateShift;

        // see if the fractional part is 0; if not, it's not time to clock
        if (Bitfield(envCounter, 0, 11) != 0)
            return;

        // determine the increment based on the non-fractional part of env_counter
        uint relevantBits = Bitfield(envCounter, rateShift <= 11 ? 11 : rateShift, 3);
        uint increment = AttenuationIncrement(rate, relevantBits);

        // attack is the only one that increases
        if (_envState == EnvelopeState.Attack)
        {
            // glitch means that attack rates of 62/63 don't increment if
            // changed after the initial key on (where they are handled specially)
            if (rate < 62)
                _envAttenuation = unchecked((ushort)(_envAttenuation + (((uint)~(int)_envAttenuation * increment) >> 4)));
        }

        // all other cases are similar
        else
        {
            // non-SSG-EG cases just apply the increment
            _envAttenuation = (ushort)(_envAttenuation + increment);

            // clamp the final attenuation
            if (_envAttenuation >= 0x400)
                _envAttenuation = 0x3ff;
        }
    }

    /// <remarks>C++: fm_operator::clock_phase.</remarks>
    private void ClockPhase(int lfoRawPm)
    {
        // read from the cache, or recalculate if PM active
        uint phaseStep = _cache.PhaseStep;
        if (phaseStep == OpDataCache.PhaseStepDynamic)
            phaseStep = _regs.ComputePhaseStep(_choffs, _opoffs, _cache, lfoRawPm);

        // finally apply the step to the current phase value
        _phase = unchecked(_phase + phaseStep);
    }

    /// <remarks>C++: fm_operator::envelope_attenuation.</remarks>
    private uint EnvelopeAttenuation(uint amOffset)
    {
        uint result = (uint)(_envAttenuation >> _cache.EgShift);

        // add in LFO AM modulation
        if (_regs.OpLfoAmEnable(_opoffs) != 0)
            result += amOffset;

        // add in total level and KSL from the cache
        result += _cache.TotalLevel;

        // clamp to max, apply shift, and return
        return Math.Min(result, 0x3ffu);
    }
}
