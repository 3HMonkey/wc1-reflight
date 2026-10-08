using WingCommander.Audio.Opl;

namespace WingCommander.Audio.OriginFx;

/// <summary>
/// The OriginFX AdLib driver model (STRAX.DRV behaviour as reconstructed by the SDL port):
/// 26 logical MIDI channels (9..25 are percussion pseudo-channels), 11 voice states mapped
/// onto the 9 OPL2 channels or, in rhythm mode, 6 melodic voices plus the 5 rhythm voices,
/// a 60 Hz pitch-envelope/vibrato service, and the box-filter decimation from the chip's
/// native rate to 22050 Hz. Every register write goes to a left and an optional right chip;
/// only the operator levels may differ (stereo panning for sound effects).
/// Not thread-safe: one owner thread at a time.
/// </summary>
/// <remarks>C: SdlOriginFxPlayer and the OriginFx* driver functions (src/sdl/originfx.cpp).</remarks>
public sealed class OriginFxSynth
{
    /// <summary>Output (mixing) rate.</summary>
    /// <remarks>C: ORIGINFX_OUTPUT_RATE.</remarks>
    public const int OutputRate = 22050;

    /// <summary>OPL clock (NTSC colour burst, AdLib card).</summary>
    /// <remarks>C: ORIGINFX_OPL_CLOCK.</remarks>
    public const uint OplClock = 3579545;

    /// <summary>Driver service rate (envelopes, vibrato, effect durations).</summary>
    /// <remarks>C: ORIGINFX_SERVICE_RATE.</remarks>
    public const int ServiceRate = 60;

    /// <summary>Melodic voices without rhythm mode.</summary>
    /// <remarks>C: ORIGINFX_MELODIC_VOICE_COUNT.</remarks>
    public const int MelodicVoiceCount = 9;

    /// <summary>Voice states: 0..8 melodic, 6..10 rhythm voices in rhythm mode.</summary>
    /// <remarks>C: ORIGINFX_VOICE_STATE_COUNT.</remarks>
    public const int VoiceStateCount = 11;

    /// <summary>Logical channels.</summary>
    /// <remarks>C: ORIGINFX_CHANNEL_COUNT.</remarks>
    public const int ChannelCount = 26;

    /// <summary>GM percussion input channel (0-based).</summary>
    public const int PercussionChannel = 9;

    private readonly OriginFxTimbreBank _bank;
    private readonly IOplChip _left;
    private readonly IOplChip? _right;
    private readonly ChannelState[] _channels = new ChannelState[ChannelCount];
    private readonly VoiceState[] _voices = new VoiceState[VoiceStateCount];
    private readonly uint _nativeSampleRate;
    private ulong _nextVoiceAge = 1;
    private ulong _nativeSampleAccumulator;
    private ulong _serviceAccumulator;
    private int _lastNativeSample;
    private int _lastNativeRightSample;
    private byte _melodicVoiceCount = MelodicVoiceCount;
    private byte _rhythmRegister;

    /// <summary>
    /// Creates and initialises a player: resets the chips, sets every channel to the first
    /// bank program and the percussion pseudo-channels to their drum programs.
    /// </summary>
    /// <param name="bank">Timbre bank.</param>
    /// <param name="left">Left (or mono) chip.</param>
    /// <param name="right">Right chip; required when <paramref name="stereoPanning"/> is set.
    /// The reference also writes a never-rendered right chip for music; passing null there
    /// gives identical output.</param>
    /// <param name="stereoPanning">Render both chips and honour CC10 pan (sound effects).</param>
    /// <remarks>C: SdlOriginFxPlayer constructor + OriginFxInitializePlayer.</remarks>
    public OriginFxSynth(OriginFxTimbreBank bank, IOplChip left, IOplChip? right, bool stereoPanning)
    {
        ArgumentNullException.ThrowIfNull(bank);
        ArgumentNullException.ThrowIfNull(left);
        if (stereoPanning && right is null)
            throw new ArgumentException("Stereo panning needs a right chip.", nameof(right));
        _bank = bank;
        _left = left;
        _right = right;
        StereoPanningEnabled = stereoPanning;
        _nativeSampleRate = left.SampleRate(OplClock);
        for (int i = 0; i < _voices.Length; i++)
            _voices[i].Timbre = -1;
        for (int i = 0; i < _channels.Length; i++)
            _channels[i].ProgramTimbre = bank.FindTimbre(0);
        InitializePlayer();
    }

    /// <summary>Creates a synthesiser on new <see cref="Ym3812"/> chips.</summary>
    public static OriginFxSynth Create(OriginFxTimbreBank bank, bool stereoPanning) =>
        new(bank, new Ym3812(), stereoPanning ? new Ym3812() : null, stereoPanning);

    /// <summary>True for the sound-effect player: both chips are rendered and CC10 pans.</summary>
    public bool StereoPanningEnabled { get; }

    /// <summary>Native chip sample rate (49715 Hz for the YM3812 at the AdLib clock).</summary>
    public uint NativeSampleRate => _nativeSampleRate;

    /// <summary>6 in rhythm mode, otherwise 9.</summary>
    public int CurrentMelodicVoiceCount => _melodicVoiceCount;

    /// <summary>Shadow of register 0xBD (rhythm enable and drum key bits).</summary>
    public byte RhythmRegister => _rhythmRegister;

    /// <summary>The timbre bank.</summary>
    public OriginFxTimbreBank Bank => _bank;

    /// <summary>Program currently selected on a channel.</summary>
    public int GetProgram(int channel) => _channels[channel].Program;

    /// <summary>True when voice <paramref name="voice"/> is sounding (key held).</summary>
    public bool IsVoiceActive(int voice) => _voices[voice].Active;

    /// <summary>Note of a voice (valid while it has a timbre).</summary>
    public int GetVoiceNote(int voice) => _voices[voice].Note;

    /// <summary>Channel of a voice (0xff when detached).</summary>
    public int GetVoiceChannel(int voice) => _voices[voice].Channel;

    /// <summary>Number of voices currently holding a key.</summary>
    public int ActiveVoiceCount
    {
        get
        {
            int count = 0;
            foreach (ref readonly var voice in _voices.AsSpan())
                if (voice.Active)
                    count++;
            return count;
        }
    }

    /// <summary>Scales a native sample by a 0..0x7fff gain and saturates to 16 bits.</summary>
    /// <remarks>C: OriginFxScaleOutputSample.</remarks>
    public static short ScaleOutputSample(int sample, uint gain)
    {
        if (gain > 0x7fff)
            gain = 0x7fff;
        long scaled = (long)sample * gain / 0x7fff;
        if (scaled < -32768)
            scaled = -32768;
        else if (scaled > 32767)
            scaled = 32767;
        return (short)scaled;
    }

    /// <summary>Saturating 16-bit add.</summary>
    /// <remarks>C: OriginFxMixOutputSample.</remarks>
    public static short MixOutputSample(short destination, short source)
    {
        int mixed = destination + source;
        if (mixed < -32768)
            mixed = -32768;
        else if (mixed > 32767)
            mixed = 32767;
        return (short)mixed;
    }

    /// <summary>
    /// Advances the 60 Hz service clock by one output frame; returns true when a service tick
    /// is due (the caller then services its effects and calls <see cref="Service"/>).
    /// </summary>
    /// <remarks>C: OriginFxAdvanceService (accumulator part).</remarks>
    public bool TryConsumeServiceTick()
    {
        if (_serviceAccumulator < OutputRate)
            return false;
        _serviceAccumulator -= OutputRate;
        return true;
    }

    /// <summary>Adds one output frame worth of time to the service accumulator.</summary>
    /// <remarks>C: OriginFxAdvanceService (player->serviceAccumulator += ORIGINFX_SERVICE_RATE).</remarks>
    public void AdvanceServiceClock() => _serviceAccumulator += ServiceRate;

    /// <summary>
    /// Generates one output frame: clocks the chips 2 or 3 times (native rate / output rate)
    /// and returns the mean (sample-and-hold when no native sample falls in the frame).
    /// </summary>
    /// <remarks>C: OriginFxGenerateOutputSample.</remarks>
    public void GenerateOutputSample(out int left, out int right)
    {
        long leftTotal = 0;
        long rightTotal = 0;
        int sampleCount = 0;
        _nativeSampleAccumulator += _nativeSampleRate;
        while (_nativeSampleAccumulator >= OutputRate)
        {
            _lastNativeSample = _left.Generate();
            if (StereoPanningEnabled)
                _lastNativeRightSample = _right!.Generate();
            else
                _lastNativeRightSample = _lastNativeSample;
            leftTotal += _lastNativeSample;
            rightTotal += _lastNativeRightSample;
            sampleCount++;
            _nativeSampleAccumulator -= OutputRate;
        }
        if (sampleCount == 0)
        {
            left = _lastNativeSample;
            right = _lastNativeRightSample;
        }
        else
        {
            left = (int)(leftTotal / sampleCount);
            right = (int)(rightTotal / sampleCount);
        }
    }

    /// <summary>Dispatches one MIDI channel message (GM percussion on channel 10 is translated).</summary>
    /// <remarks>C: OriginFxDispatchEvent (channel event part).</remarks>
    public void ProcessChannelMessage(byte status, byte data1, byte data2)
    {
        int channelIndex = status & 0x0f;
        int command = status & 0xf0;
        int note;
        switch (command)
        {
            case 0x80:
                note = data1;
                if (channelIndex != PercussionChannel || MapPercussionNote(note, ref channelIndex, ref note))
                    NoteOff(channelIndex, note);
                break;
            case 0x90:
                note = data1;
                if (channelIndex != PercussionChannel || MapPercussionNote(note, ref channelIndex, ref note))
                {
                    if (data2 == 0)
                        NoteOff(channelIndex, note);
                    else
                        NoteOn(channelIndex, note, data2);
                }
                break;
            case 0xb0:
                ControlChange(channelIndex, data1, data2);
                break;
            case 0xc0:
                SetProgram(channelIndex, data1);
                break;
            case 0xe0:
                _channels[channelIndex].PitchBend = (ushort)(data1 | (data2 << 7));
                UpdateChannelVoices(channelIndex, true, false);
                break;
        }
    }

    /// <summary>Starts a note: every linked timbre of the channel program gets a voice.</summary>
    /// <remarks>C: OriginFxNoteOn.</remarks>
    public void NoteOn(int channelIndex, int note, int velocity)
    {
        int timbre = _channels[channelIndex].ProgramTimbre;
        while (timbre >= 0)
        {
            StartTimbre(channelIndex, note, velocity, timbre);
            timbre = _bank.NextTimbre(timbre);
        }
    }

    /// <summary>Releases a note on every linked timbre of the channel program.</summary>
    /// <remarks>C: OriginFxNoteOff.</remarks>
    public void NoteOff(int channelIndex, int note)
    {
        int timbre = _channels[channelIndex].ProgramTimbre;
        while (timbre >= 0)
        {
            StopTimbre(channelIndex, note, timbre);
            timbre = _bank.NextTimbre(timbre);
        }
    }

    /// <summary>
    /// Releases every active voice of a channel, or of all channels for -1 (which also
    /// leaves rhythm mode).
    /// </summary>
    /// <remarks>C: OriginFxAllNotesOff.</remarks>
    public void AllNotesOff(int channelIndex)
    {
        for (int voiceIndex = 0; voiceIndex < VoiceStateCount; voiceIndex++)
        {
            ref var voice = ref _voices[voiceIndex];
            if (!voice.Active || (channelIndex >= 0 && voice.Channel != channelIndex))
                continue;
            int rhythmBit = GetRhythmBit(voiceIndex);
            if (voice.Timbre >= 0 && _bank.Byte(voice.Timbre, 11) != 0 && rhythmBit != 0)
            {
                _rhythmRegister = (byte)(_rhythmRegister & ~rhythmBit);
                WriteRegister(0xbd, _rhythmRegister);
            }
            else
            {
                WriteRegister(0xb0 + GetOplVoice(voiceIndex), voice.FrequencyHigh);
            }
            voice.Active = false;
            voice.EnvelopeState = 0;
            voice.Age = _nextVoiceAge++;
        }
        if (channelIndex < 0 && _melodicVoiceCount < MelodicVoiceCount)
        {
            _rhythmRegister = 0;
            _melodicVoiceCount = MelodicVoiceCount;
            WriteRegister(0xbd, 0);
        }
    }

    /// <summary>Selects a program; releases and detaches the channel's voices first.</summary>
    /// <remarks>C: OriginFxSetProgram.</remarks>
    public void SetProgram(int channelIndex, int program)
    {
        AllNotesOff(channelIndex);
        for (int voiceIndex = 0; voiceIndex < VoiceStateCount; voiceIndex++)
        {
            ref var voice = ref _voices[voiceIndex];
            if (voice.Timbre >= 0 && voice.Channel == channelIndex)
            {
                voice.Timbre = -1;
                voice.Channel = 0xff;
            }
        }
        ref var channel = ref _channels[channelIndex];
        channel.Program = (byte)program;
        channel.ProgramTimbre = _bank.FindTimbre(channel.Program);
        int timbre = _bank.FindTimbre(program);
        channel.ModulationRate = _bank.Byte(timbre, 16);
        channel.ModulationDepth = _bank.Byte(timbre, 17);
        if (_bank.Byte(timbre, 11) != 0)
            EnableRhythmMode();
    }

    /// <summary>
    /// Control change; on the percussion input channel it is broadcast to the pseudo-channels
    /// 10..25 first.
    /// </summary>
    /// <remarks>C: OriginFxControlChange.</remarks>
    public void ControlChange(int channelIndex, int controller, int value)
    {
        if (channelIndex == PercussionChannel)
        {
            for (int percussionChannel = 10; percussionChannel < ChannelCount; percussionChannel++)
                ApplyControlChange(percussionChannel, controller, value);
        }
        ApplyControlChange(channelIndex, controller, value);
    }

    /// <summary>Sets the 14-bit pitch bend of a channel and retunes its voices.</summary>
    public void PitchBend(int channelIndex, int value)
    {
        _channels[channelIndex].PitchBend = (ushort)value;
        UpdateChannelVoices(channelIndex, true, false);
    }

    /// <summary>The 60 Hz service: pitch envelopes and vibrato of every melodic voice.</summary>
    /// <remarks>C: OriginFxService.</remarks>
    public void Service()
    {
        ReadOnlySpan<byte> rateOffsets = OriginFxTables.EnvelopeRateOffsets;
        ReadOnlySpan<byte> targetOffsets = OriginFxTables.EnvelopeTargetOffsets;
        for (int voiceIndex = 0; voiceIndex < _melodicVoiceCount; voiceIndex++)
        {
            ref var voice = ref _voices[voiceIndex];
            int timbre = voice.Timbre;
            if (timbre < 0 || voice.Channel >= ChannelCount)
                continue;
            bool changed = false;
            int state = voice.EnvelopeState;
            if (state < targetOffsets.Length && targetOffsets[state] != 0)
            {
                int target = _bank.Int16(timbre, targetOffsets[state]);
                uint rate = (ushort)_bank.Int16(timbre, rateOffsets[state]);
                int distance = target - voice.EnvelopePitch;
                if (distance < 0)
                    distance = -distance;
                if ((uint)distance < rate)
                {
                    voice.EnvelopePitch = target;
                    voice.EnvelopeState++;
                }
                else if (voice.EnvelopePitch < target)
                {
                    voice.EnvelopePitch += (int)rate;
                }
                else
                {
                    voice.EnvelopePitch -= (int)rate;
                }
                changed = true;
            }

            ref var channel = ref _channels[voice.Channel];
            if (channel.ModulationRate != 0)
            {
                int phase = (voice.ModulationPhase + channel.ModulationRate) & 0xff;
                voice.ModulationPhase = (byte)phase;
                int triangle = phase < 0x80 ? phase : phase - 0x100;
                if (triangle > 63 || triangle < -64)
                {
                    phase = (0x80 - phase) & 0xff;
                    triangle = phase < 0x80 ? phase : phase - 0x100;
                }
                voice.ModulationPitch = ArithmeticShiftRight(channel.ModulationDepth * triangle, 4);
                changed = true;
            }
            if (changed)
                WriteVoiceFrequency(voiceIndex, voice.EnvelopeState > 1);
        }
    }

    /// <summary>Returns to 9 melodic voices when no rhythm voice is sounding.</summary>
    /// <remarks>C: OriginFxDisableRhythmModeIfIdle.</remarks>
    public void DisableRhythmModeIfIdle()
    {
        if (_melodicVoiceCount == MelodicVoiceCount)
            return;
        for (int voiceIndex = _melodicVoiceCount; voiceIndex < VoiceStateCount; voiceIndex++)
            if (_voices[voiceIndex].Active)
                return;
        _rhythmRegister = 0;
        WriteRegister(0xbd, 0);
        _melodicVoiceCount = MelodicVoiceCount;
    }

    /// <remarks>C: OriginFxInitializePlayer (after OriginFxLoadTimbres).</remarks>
    private void InitializePlayer()
    {
        ResetOpl();
        int defaultProgram = _bank.ProgramOf(0);
        for (int channelIndex = 0; channelIndex < ChannelCount; channelIndex++)
        {
            _channels[channelIndex].PitchBend = 0x2000;
            _channels[channelIndex].Volume = 0xff;
            _channels[channelIndex].Pan = 64;
            SetProgram(channelIndex, defaultProgram);
        }
        ReadOnlySpan<byte> percussionPrograms = OriginFxTables.PercussionPrograms;
        for (int percussionIndex = 0; percussionIndex < percussionPrograms.Length; percussionIndex++)
            SetProgram(9 + percussionIndex, percussionPrograms[percussionIndex]);
    }

    /// <remarks>C: OriginFxWriteRegister.</remarks>
    private void WriteRegister(int address, int value)
    {
        _left.Write(0, (byte)address);
        _left.Write(1, (byte)value);
        if (_right is not null)
        {
            _right.Write(0, (byte)address);
            _right.Write(1, (byte)value);
        }
    }

    /// <remarks>C: OriginFxWriteStereoRegister.</remarks>
    private void WriteStereoRegister(int address, int leftValue, int rightValue)
    {
        _left.Write(0, (byte)address);
        _left.Write(1, (byte)leftValue);
        if (_right is not null)
        {
            _right.Write(0, (byte)address);
            _right.Write(1, (byte)rightValue);
        }
    }

    /// <remarks>C: OriginFxResetOpl.</remarks>
    private void ResetOpl()
    {
        _left.Reset();
        _right?.Reset();
        _melodicVoiceCount = MelodicVoiceCount;
        _rhythmRegister = 0;
        WriteRegister(0x01, 0x20);
        WriteRegister(0x08, 0);
        WriteRegister(0xbd, 0);
        for (int voice = 0; voice < MelodicVoiceCount; voice++)
        {
            WriteRegister(0xa0 + voice, 0);
            WriteRegister(0xb0 + voice, 0);
        }
    }

    /// <remarks>C: OriginFxGetOperatorTableIndex.</remarks>
    private int GetOperatorTableIndex(int voiceIndex) =>
        _melodicVoiceCount < MelodicVoiceCount ? voiceIndex + MelodicVoiceCount : voiceIndex;

    /// <remarks>C: OriginFxGetCarrierOffset.</remarks>
    private int GetCarrierOffset(int voiceIndex) => OriginFxTables.CarrierOffsets[GetOperatorTableIndex(voiceIndex)];

    /// <remarks>C: OriginFxGetModulatorOffset.</remarks>
    private int GetModulatorOffset(int voiceIndex) => OriginFxTables.ModulatorOffsets[GetOperatorTableIndex(voiceIndex)];

    /// <remarks>C: OriginFxGetOplVoice.</remarks>
    private static int GetOplVoice(int voiceIndex) => voiceIndex > 8 ? 17 - voiceIndex : voiceIndex;

    /// <remarks>C: OriginFxGetRhythmBit.</remarks>
    private static int GetRhythmBit(int voiceIndex) =>
        voiceIndex < 6 || voiceIndex >= VoiceStateCount ? 0 : OriginFxTables.RhythmBits[voiceIndex - 6];

    /// <remarks>C: OriginFxEnableRhythmMode.</remarks>
    private void EnableRhythmMode()
    {
        if (_melodicVoiceCount < MelodicVoiceCount)
            return;
        WriteRegister(0xa6, 0);
        WriteRegister(0xb6, 0);
        WriteRegister(0xa7, 0);
        WriteRegister(0xb7, 0x0a);
        WriteRegister(0xa8, 0x54);
        WriteRegister(0xb8, 0x09);
        _melodicVoiceCount = 6;
        _rhythmRegister = 0x20;
        for (int voiceIndex = 6; voiceIndex < VoiceStateCount; voiceIndex++)
            _voices[voiceIndex] = new VoiceState { Timbre = -1 };
        WriteRegister(0xbd, _rhythmRegister);
    }

    /// <summary>Arithmetic (flooring) right shift with the reference's edge cases.</summary>
    /// <remarks>C: OriginFxArithmeticShiftRight.</remarks>
    private static int ArithmeticShiftRight(int value, int count)
    {
        if (count == 0)
            return value;
        if (count >= 32)
            return value < 0 ? -1 : 0;
        return value >> count;
    }

    /// <remarks>C: OriginFxClampTotalLevel.</remarks>
    private static int ClampTotalLevel(int registerValue, int totalLevel)
    {
        if (totalLevel < 0)
            totalLevel = 0;
        else if (totalLevel > 0x3f)
            totalLevel = 0x3f;
        return (registerValue & 0xc0) | totalLevel;
    }

    /// <remarks>C: OriginFxCalculateVelocityLevel.</remarks>
    private static int CalculateVelocityLevel(int registerValue, int sensitivity, int velocity)
    {
        if (sensitivity == 0)
            return registerValue;
        if (sensitivity > 7)
            sensitivity = 7;
        int attenuation = ArithmeticShiftRight(63 - velocity, 7 - sensitivity);
        int totalLevel = (registerValue & 0x3f) + attenuation;
        return ClampTotalLevel(registerValue, totalLevel);
    }

    /// <remarks>C: OriginFxCalculateCarrierLevel.</remarks>
    private static int CalculateCarrierLevel(int registerValue, int sensitivity, int velocity, int volume)
    {
        if (sensitivity == 0 && volume >= 0x100)
            return registerValue;
        if (sensitivity > 7)
            sensitivity = 7;
        int attenuation = ArithmeticShiftRight(63 - velocity, 7 - sensitivity);
        int totalLevel = (registerValue & 0x3f) + attenuation;
        totalLevel = 63 - ArithmeticShiftRight(volume * (63 - totalLevel), 8);
        return ClampTotalLevel(registerValue, totalLevel);
    }

    /// <remarks>C: OriginFxCalculatePannedLevel.</remarks>
    private static int CalculatePannedLevel(int registerValue, int pan, bool rightChannel)
    {
        uint scale;
        uint divisor;
        if (pan > 127)
            pan = 127;
        if (rightChannel)
        {
            if (pan >= 64)
                return registerValue;
            scale = (uint)pan;
            divisor = 64;
        }
        else
        {
            if (pan <= 64)
                return registerValue;
            scale = (uint)(127 - pan);
            divisor = 63;
        }
        int totalLevel = registerValue & 0x3f;
        totalLevel = 63 - (int)(scale * (uint)(63 - totalLevel) / divisor);
        return ClampTotalLevel(registerValue, totalLevel);
    }

    /// <remarks>C: OriginFxWriteVoiceLevels.</remarks>
    private void WriteVoiceLevels(int voiceIndex)
    {
        ref var voice = ref _voices[voiceIndex];
        ref var channel = ref _channels[voice.Channel];
        int timbre = voice.Timbre;
        int carrierOffset = GetCarrierOffset(voiceIndex);
        int modulatorOffset = GetModulatorOffset(voiceIndex);
        byte carrierSensitivity = _bank.Byte(timbre, 12);
        if (carrierSensitivity != 0 || channel.Volume < 0x100)
        {
            int carrierLevel = CalculateCarrierLevel(_bank.Byte(timbre, 6), carrierSensitivity, voice.Velocity, channel.Volume);
            WriteStereoRegister(0x40 + carrierOffset,
                CalculatePannedLevel(carrierLevel, channel.Pan, false),
                CalculatePannedLevel(carrierLevel, channel.Pan, true));
        }
        byte modulatorSensitivity = _bank.Byte(timbre, 13);
        byte connection = _bank.Byte(timbre, 10);
        if (modulatorSensitivity != 0 || (StereoPanningEnabled && (connection & 1) != 0))
        {
            int modulatorLevel = modulatorSensitivity != 0
                ? CalculateVelocityLevel(_bank.Byte(timbre, 1), modulatorSensitivity, voice.Velocity)
                : _bank.Byte(timbre, 1);
            if ((connection & 1) != 0)
            {
                WriteStereoRegister(0x40 + modulatorOffset,
                    CalculatePannedLevel(modulatorLevel, channel.Pan, false),
                    CalculatePannedLevel(modulatorLevel, channel.Pan, true));
            }
            else
            {
                WriteRegister(0x40 + modulatorOffset, modulatorLevel);
            }
        }
    }

    /// <remarks>C: OriginFxWriteVoiceFrequency.</remarks>
    private void WriteVoiceFrequency(int voiceIndex, bool keyOn)
    {
        ref var voice = ref _voices[voiceIndex];
        ref var channel = ref _channels[voice.Channel];
        int timbre = voice.Timbre;
        int bend = channel.PitchBend - 0x2000;
        int keyPitch = (voice.Note - 60) * 256;
        int keyTracking = (sbyte)_bank.Byte(timbre, 39);
        int trackingShift;
        if (keyTracking < 0)
        {
            keyPitch = -keyPitch;
            trackingShift = (byte)~keyTracking;
        }
        else
        {
            trackingShift = keyTracking;
        }
        keyPitch = ArithmeticShiftRight(keyPitch, trackingShift) + 60 * 256;
        int channelTimbre = channel.ProgramTimbre;
        int pitch = keyPitch
            + voice.EnvelopePitch
            + _bank.Int16(timbre, 36)
            + voice.ModulationPitch
            + ArithmeticShiftRight(bend * _bank.Byte(channelTimbre, 14), 8);
        int note = pitch / 256;
        int fraction = pitch % 256;
        if (fraction < 0)
        {
            fraction += 256;
            note--;
        }
        int tableIndex = (note + 6) % 12;
        if (tableIndex < 0)
            tableIndex += 12;
        int block = (note + 6) / 12 - 2;
        if (note + 6 < 0 && (note + 6) % 12 != 0)
            block--;
        if (block < 0)
            block = 0;
        else if (block > 7)
            block = 7;
        ReadOnlySpan<ushort> frequencyNumbers = OriginFxTables.FrequencyNumbers;
        int frequency = frequencyNumbers[tableIndex]
            + (frequencyNumbers[tableIndex + 1] - frequencyNumbers[tableIndex]) * fraction / 256;
        if (frequency > 0x3ff)
            frequency = 0x3ff;

        voice.FrequencyHigh = (byte)(((frequency >> 8) & 3) | (block << 2));
        int oplVoice = GetOplVoice(voiceIndex);
        WriteRegister(0xa0 + oplVoice, frequency & 0xff);
        WriteRegister(0xb0 + oplVoice, voice.FrequencyHigh | (keyOn ? 0x20 : 0));
    }

    /// <remarks>C: OriginFxProgramVoice.</remarks>
    private void ProgramVoice(int voiceIndex)
    {
        int timbre = _voices[voiceIndex].Timbre;
        int carrierOffset = GetCarrierOffset(voiceIndex);
        int modulatorOffset = GetModulatorOffset(voiceIndex);
        int oplVoice = GetOplVoice(voiceIndex);
        WriteRegister(0x20 + modulatorOffset, _bank.Byte(timbre, 0));
        WriteRegister(0x40 + modulatorOffset, _bank.Byte(timbre, 1));
        WriteRegister(0x60 + modulatorOffset, _bank.Byte(timbre, 2));
        WriteRegister(0x80 + modulatorOffset, _bank.Byte(timbre, 3));
        WriteRegister(0xe0 + modulatorOffset, _bank.Byte(timbre, 4));
        if (_melodicVoiceCount == MelodicVoiceCount || _bank.Byte(timbre, 11) < 7)
        {
            WriteRegister(0x20 + carrierOffset, _bank.Byte(timbre, 5));
            WriteRegister(0x40 + carrierOffset, _bank.Byte(timbre, 6));
            WriteRegister(0x60 + carrierOffset, _bank.Byte(timbre, 7));
            WriteRegister(0x80 + carrierOffset, _bank.Byte(timbre, 8));
            WriteRegister(0xe0 + carrierOffset, _bank.Byte(timbre, 9));
            WriteRegister(0xc0 + oplVoice, _bank.Byte(timbre, 10));
        }
        WriteVoiceLevels(voiceIndex);
    }

    /// <summary>Least recently used free melodic voice, else the oldest active one.</summary>
    /// <remarks>C: OriginFxChooseVoice.</remarks>
    private int ChooseVoice()
    {
        int oldestFreeVoice = _melodicVoiceCount;
        int oldestVoice = 0;
        for (int voiceIndex = 0; voiceIndex < _melodicVoiceCount; voiceIndex++)
        {
            if (!_voices[voiceIndex].Active)
            {
                if (oldestFreeVoice == _melodicVoiceCount || _voices[voiceIndex].Age < _voices[oldestFreeVoice].Age)
                    oldestFreeVoice = voiceIndex;
            }
            else if (_voices[voiceIndex].Age < _voices[oldestVoice].Age || !_voices[oldestVoice].Active)
            {
                oldestVoice = voiceIndex;
            }
        }
        return oldestFreeVoice != _melodicVoiceCount ? oldestFreeVoice : oldestVoice;
    }

    /// <remarks>C: OriginFxStartTimbre.</remarks>
    private void StartTimbre(int channelIndex, int note, int velocity, int timbre)
    {
        int voiceIndex;
        int rhythmBit;
        int rhythmVoice = _bank.Byte(timbre, 11);
        if (rhythmVoice != 0)
        {
            EnableRhythmMode();
            voiceIndex = rhythmVoice;
            if (voiceIndex < 6 || voiceIndex >= VoiceStateCount)
                return;
            rhythmBit = GetRhythmBit(voiceIndex);
            _rhythmRegister = (byte)(_rhythmRegister & ~rhythmBit);
            WriteRegister(0xbd, _rhythmRegister);
        }
        else
        {
            voiceIndex = ChooseVoice();
            rhythmBit = 0;
        }
        ref var voice = ref _voices[voiceIndex];
        if (voice.Active && rhythmBit == 0)
        {
            int oplVoice = GetOplVoice(voiceIndex);
            WriteRegister(0xa0 + oplVoice, 0);
            WriteRegister(0xb0 + oplVoice, 0);
        }
        voice.Timbre = timbre;
        voice.Age = _nextVoiceAge++;
        voice.Channel = (byte)channelIndex;
        voice.Note = (byte)note;
        voice.Velocity = (byte)velocity;
        voice.EnvelopeState = 2;
        voice.ModulationPhase = 0;
        voice.EnvelopePitch = _bank.Int16(timbre, 18);
        voice.ModulationPitch = 0;
        voice.Active = true;
        ProgramVoice(voiceIndex);
        if (rhythmBit == 0)
        {
            WriteVoiceFrequency(voiceIndex, true);
        }
        else
        {
            if (voiceIndex == 6)
                WriteVoiceFrequency(voiceIndex, false);
            _rhythmRegister = (byte)(_rhythmRegister | rhythmBit);
            WriteRegister(0xbd, _rhythmRegister);
        }
    }

    /// <remarks>C: OriginFxStopTimbre.</remarks>
    private void StopTimbre(int channelIndex, int note, int timbre)
    {
        for (int voiceIndex = 0; voiceIndex < VoiceStateCount; voiceIndex++)
        {
            ref var voice = ref _voices[voiceIndex];
            if (!voice.Active || voice.Channel != channelIndex || voice.Note != note || voice.Timbre != timbre)
                continue;
            int rhythmBit = GetRhythmBit(voiceIndex);
            if (_bank.Byte(timbre, 11) != 0 && rhythmBit != 0)
            {
                _rhythmRegister = (byte)(_rhythmRegister & ~rhythmBit);
                WriteRegister(0xbd, _rhythmRegister);
            }
            else
            {
                WriteRegister(0xb0 + GetOplVoice(voiceIndex), voice.FrequencyHigh);
            }
            voice.Active = false;
            voice.EnvelopeState = 0;
            voice.Age = _nextVoiceAge++;
            return;
        }
    }

    /// <remarks>C: OriginFxUpdateChannelVoices.</remarks>
    private void UpdateChannelVoices(int channelIndex, bool updateFrequency, bool updateLevel)
    {
        for (int voiceIndex = 0; voiceIndex < _melodicVoiceCount; voiceIndex++)
        {
            if (!_voices[voiceIndex].Active || _voices[voiceIndex].Channel != channelIndex)
                continue;
            if (updateFrequency)
                WriteVoiceFrequency(voiceIndex, true);
            if (updateLevel)
                WriteVoiceLevels(voiceIndex);
        }
    }

    /// <remarks>C: OriginFxMapPercussionNote.</remarks>
    private static bool MapPercussionNote(int note, ref int channelIndex, ref int mappedNote)
    {
        if ((uint)note >= OriginFxTables.PercussionNoteCount)
            return false;
        int channel = OriginFxTables.PercussionChannels[note];
        if (channel == 0)
            return false;
        channelIndex = channel - 1;
        mappedNote = OriginFxTables.PercussionPitches[note];
        return true;
    }

    /// <remarks>C: OriginFxApplyControlChange.</remarks>
    private void ApplyControlChange(int channelIndex, int controller, int value)
    {
        ref var channel = ref _channels[channelIndex];
        if (controller == 1)
        {
            int timbre = channel.ProgramTimbre;
            channel.ModulationDepth = (ushort)(((uint)_bank.Byte(timbre, 15) * (uint)value >> 7) + _bank.Byte(timbre, 17));
        }
        else if (controller == 7)
        {
            channel.Volume = (ushort)(value + 0x80);
        }
        else if (controller == 10 && StereoPanningEnabled)
        {
            if (value > 127)
                value = 127;
            channel.Pan = (byte)value;
            UpdateChannelVoices(channelIndex, false, true);
        }
        else if (controller == 123)
        {
            AllNotesOff(channelIndex);
            DisableRhythmModeIfIdle();
        }
        else if (controller == 121)
        {
            int timbre = channel.ProgramTimbre;
            channel.ModulationDepth = _bank.Byte(timbre, 17);
            channel.Volume = 0xff;
            channel.PitchBend = 0x2000;
            UpdateChannelVoices(channelIndex, true, false);
        }
    }

    /// <remarks>C: OriginFxChannel.</remarks>
    private struct ChannelState
    {
        public ushort PitchBend;
        public ushort Volume;
        public ushort ModulationDepth;
        public byte Program;
        public byte ModulationRate;
        public byte Pan;

        /// <summary>Cached FindTimbre(Program); the reference searches the bank on every use.</summary>
        public int ProgramTimbre;
    }

    /// <remarks>C: OriginFxVoice (timbre pointer = record index, -1 = null).</remarks>
    private struct VoiceState
    {
        public int Timbre;
        public ulong Age;
        public byte Channel;
        public byte Note;
        public byte Velocity;
        public byte FrequencyHigh;
        public byte EnvelopeState;
        public byte ModulationPhase;
        public int EnvelopePitch;
        public int ModulationPitch;
        public bool Active;
    }
}
