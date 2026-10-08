// C# port of ymfm (https://github.com/aaronsgiles/ymfm, commit 81aec25), YM3812 subset.
// Copyright (c) 2021, Aaron Giles. All rights reserved. BSD 3-Clause License, see LICENSE-ymfm.txt.

using static WingCommander.Audio.Opl.YmfmTables;

namespace WingCommander.Audio.Opl;

/// <summary>
/// The FM core of the YM3812: 18 operators in 9 channels, the envelope counter, status/IRQ
/// bookkeeping and the timer registers. The C++ engine talks to a <c>ymfm_interface</c>; the
/// reference player uses the default interface (no timers, no busy state, no IRQ consumer),
/// so its callbacks are inlined here: mode writes go straight to
/// <see cref="EngineModeWrite"/> and timers never fire.
/// </summary>
/// <remarks>C++: fm_engine_base&lt;opl2_registers&gt; (ymfm_fm.h, ymfm_fm.ipp).</remarks>
internal sealed class OplFmEngine
{
    private readonly OplRegisters _regs = new();
    private readonly FmChannel[] _channels = new FmChannel[OplRegisters.Channels];
    private readonly FmOperator[] _operators = new FmOperator[OplRegisters.Operators];
    private readonly byte[] _timerRunning = new byte[2];
    private uint _envCounter;
    private byte _status;
    private byte _clockPrescale = OplRegisters.DefaultPrescale;
    private byte _irqMask = OplRegisters.StatusTimerA | OplRegisters.StatusTimerB;
    private byte _irqState;
    private byte _totalClocks;
    private uint _activeChannels = OplRegisters.AllChannels;
    private uint _modifiedChannels = OplRegisters.AllChannels;
    private uint _prepareCount;

    /// <remarks>C++: fm_engine_base::fm_engine_base.</remarks>
    public OplFmEngine()
    {
        for (uint chnum = 0; chnum < OplRegisters.Channels; chnum++)
            _channels[chnum] = new FmChannel(_regs, OplRegisters.ChannelOffset(chnum));
        for (uint opnum = 0; opnum < OplRegisters.Operators; opnum++)
            _operators[opnum] = new FmOperator(_regs, OplRegisters.OperatorOffset(opnum));
        AssignOperators();
    }

    public uint ClockPrescale => _clockPrescale;

    /// <remarks>C++: fm_engine_base::sample_rate.</remarks>
    public uint SampleRate(uint baseClock) => baseClock / (_clockPrescale * (uint)OplRegisters.Operators);

    /// <remarks>C++: fm_engine_base::reset.</remarks>
    public void Reset()
    {
        // reset all status bits
        SetResetStatus(0, 0xff);

        // register type-specific initialization
        _regs.Reset();

        // explicitly write to the mode register since it has side-effects
        Write(OplRegisters.RegMode, 0);

        foreach (var channel in _channels)
            channel.Reset();
        foreach (var op in _operators)
            op.Reset();
    }

    /// <summary>Clocks every selected channel one sample forward.</summary>
    /// <remarks>C++: fm_engine_base::clock.</remarks>
    public uint Clock(uint chanmask)
    {
        // update the clock counter
        _totalClocks++;

        // if something was modified, prepare
        // also prepare every 4k samples to catch ending notes
        if (_modifiedChannels != 0 || _prepareCount++ >= 4096)
        {
            // call each channel to prepare
            _activeChannels = 0;
            for (int chnum = 0; chnum < OplRegisters.Channels; chnum++)
                if (Bitfield(chanmask, chnum) != 0)
                    if (_channels[chnum].Prepare())
                        _activeChannels |= 1u << chnum;

            // reset the modified channels and prepare count
            _modifiedChannels = _prepareCount = 0;
        }

        // the envelope clock divider is 1 on OPL, so just increment by 4
        _envCounter += 4;

        // clock the noise generator
        int lfoRawPm = _regs.ClockNoiseAndLfo();

        // now update the state of all the channels and operators
        for (int chnum = 0; chnum < OplRegisters.Channels; chnum++)
            if (Bitfield(chanmask, chnum) != 0)
                _channels[chnum].Clock(_envCounter, lfoRawPm);

        return _envCounter;
    }

    /// <summary>Adds the outputs of the selected, active channels to <paramref name="output"/>.</summary>
    /// <remarks>C++: fm_engine_base::output.</remarks>
    public void Output(ref int output, int rshift, int clipmax, uint chanmask)
    {
        // mask out inactive channels
        chanmask &= _activeChannels;

        // handle the rhythm case, where some of the operators are dedicated to percussion
        if (_regs.RhythmEnable != 0)
        {
            // precompute the operator 13+17 phase selection value
            uint op13Phase = _operators[13].Phase;
            uint op17Phase = _operators[17].Phase;
            uint phaseSelect = (Bitfield(op13Phase, 2) ^ Bitfield(op13Phase, 7)) | Bitfield(op13Phase, 3) |
                (Bitfield(op17Phase, 5) ^ Bitfield(op17Phase, 3));

            // sum over all the desired channels
            for (int chnum = 0; chnum < OplRegisters.Channels; chnum++)
            {
                if (Bitfield(chanmask, chnum) == 0)
                    continue;
                if (chnum == 6)
                    _channels[chnum].OutputRhythmCh6(ref output, rshift, clipmax);
                else if (chnum == 7)
                    _channels[chnum].OutputRhythmCh7(phaseSelect, ref output, rshift, clipmax);
                else if (chnum == 8)
                    _channels[chnum].OutputRhythmCh8(phaseSelect, ref output, rshift, clipmax);
                else
                    _channels[chnum].Output2Op(ref output, rshift, clipmax);
            }
        }
        else
        {
            // sum over all the desired channels
            for (int chnum = 0; chnum < OplRegisters.Channels; chnum++)
                if (Bitfield(chanmask, chnum) != 0)
                    _channels[chnum].Output2Op(ref output, rshift, clipmax);
        }
    }

    /// <summary>Writes one register.</summary>
    /// <remarks>C++: fm_engine_base::write.</remarks>
    public void Write(int regnum, byte data)
    {
        // special case: writes to the mode register can impact IRQs
        // (ymfm_interface::ymfm_sync_mode_write -> engine_mode_write)
        if (regnum == OplRegisters.RegMode)
        {
            EngineModeWrite(data);
            return;
        }

        // for now just mark all channels as modified
        _modifiedChannels = OplRegisters.AllChannels;

        // most writes are passive, consumed only when needed
        if (_regs.Write(regnum, data, out uint keyonChannel, out uint keyonOpmask))
        {
            // handle writes to the keyon register(s)
            if (keyonChannel < OplRegisters.Channels)
            {
                // normal channel on/off
                _channels[keyonChannel].KeyOnOff(keyonOpmask, KeyonType.Normal);
            }
            else if (keyonChannel == OplRegisters.RhythmChannel)
            {
                // special case for the OPL rhythm channels
                _channels[6].KeyOnOff(Bitfield(keyonOpmask, 4) != 0 ? 3u : 0u, KeyonType.Rhythm);
                _channels[7].KeyOnOff(Bitfield(keyonOpmask, 0) | (Bitfield(keyonOpmask, 3) << 1), KeyonType.Rhythm);
                _channels[8].KeyOnOff(Bitfield(keyonOpmask, 2) | (Bitfield(keyonOpmask, 1) << 1), KeyonType.Rhythm);
            }
        }
    }

    /// <remarks>C++: fm_engine_base::status.</remarks>
    public byte Status() => (byte)(_status & ~OplRegisters.StatusBusy & ~_regs.StatusMask);

    /// <remarks>C++: fm_engine_base::set_reset_status.</remarks>
    private byte SetResetStatus(byte set, byte reset)
    {
        _status = (byte)((_status | set) & ~(reset | OplRegisters.StatusBusy));
        EngineCheckInterrupts();
        return (byte)(_status & ~_regs.StatusMask);
    }

    /// <remarks>C++: fm_engine_base::assign_operators (fixed OPL2 map).</remarks>
    private void AssignOperators()
    {
        for (int chnum = 0; chnum < OplRegisters.Channels; chnum++)
        {
            var (modulator, carrier) = OplRegisters.OperatorPair(chnum);
            _channels[chnum].Assign(_operators[modulator], _operators[carrier]);
        }
    }

    /// <summary>
    /// Starts or stops a timer. The reference interface ignores timer requests, so a running
    /// timer never expires; only the running flag is tracked.
    /// </summary>
    /// <remarks>C++: fm_engine_base::update_timer.</remarks>
    private void UpdateTimer(int tnum, uint enable, int deltaClocks)
    {
        _ = deltaClocks; // the period (register value + delta) would only matter to a timer callback
        if (enable != 0 && _timerRunning[tnum] == 0)
            _timerRunning[tnum] = 1;
        else if (enable == 0)
            _timerRunning[tnum] = 0;
    }

    /// <remarks>C++: fm_engine_base::engine_check_interrupts.</remarks>
    private void EngineCheckInterrupts()
    {
        _irqState = (byte)((_status & _irqMask & ~_regs.StatusMask) != 0 ? 1 : 0);
        if (_irqState != 0)
            _status |= OplRegisters.StatusIrq;
        else
            _status = (byte)(_status & ~OplRegisters.StatusIrq);
    }

    /// <remarks>C++: fm_engine_base::engine_mode_write.</remarks>
    private void EngineModeWrite(byte data)
    {
        // mark all channels as modified
        _modifiedChannels = OplRegisters.AllChannels;

        // actually write the mode register now
        _regs.Write(OplRegisters.RegMode, data, out _, out _);

        // reset IRQ status -- when written, all other bits are ignored
        if (_regs.IrqReset != 0)
        {
            SetResetStatus(0, 0x78);
        }
        else
        {
            // reset timer status
            byte resetMask = 0;
            if (_regs.ResetTimerB != 0)
                resetMask |= OplRegisters.StatusTimerB;
            if (_regs.ResetTimerA != 0)
                resetMask |= OplRegisters.StatusTimerA;
            SetResetStatus(0, resetMask);

            // load timers
            UpdateTimer(1, _regs.LoadTimerB, -(_totalClocks & 15));
            UpdateTimer(0, _regs.LoadTimerA, 0);
        }
    }
}
