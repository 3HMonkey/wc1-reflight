using WingCommander.Audio.Opl;

namespace WingCommander.Audio.OriginFx;

/// <summary>
/// The DOS sound-effect engine (the reference's long-lived "sound player"): 32 effect slots
/// playing <see cref="OriginFxSoundRecords"/> on MIDI channels 1..8 of a stereo
/// <see cref="OriginFxSynth"/> (two YM3812s, panning through operator levels). Effects with
/// the same tag replace each other; when all eight channels are busy the oldest effect of
/// lower or equal priority is stolen. Records expire, glide, retrigger or chain on the 60 Hz
/// service clock. Not thread-safe: the mixer owns it on the audio thread.
/// </summary>
/// <remarks>C: SdlCreateOriginFxSoundPlayer, SdlPlayOriginFxSoundEffect, SdlStopOriginFxSoundEffects,
/// SdlMixOriginFxSoundEffects and the OriginFx*SoundEffect* helpers (src/sdl/originfx.cpp).</remarks>
public sealed class OriginFxSoundEffectEngine
{
    /// <summary>Effect slots.</summary>
    /// <remarks>C: ORIGINFX_SOUND_SLOT_COUNT.</remarks>
    public const int SlotCount = 32;

    /// <summary>MIDI channels reserved for effects.</summary>
    /// <remarks>C: ORIGINFX_SOUND_CHANNEL_COUNT.</remarks>
    public const int ChannelCount = 8;

    /// <summary>First effect channel.</summary>
    /// <remarks>C: ORIGINFX_FIRST_SOUND_CHANNEL.</remarks>
    public const int FirstChannel = 1;

    private readonly EffectSlot[] _effects = new EffectSlot[SlotCount];
    private ulong _nextSoundEffectAge = 1;
    private long _currentFrame;

    /// <summary>Creates the sound player on two fresh YM3812 chips.</summary>
    /// <remarks>C: SdlCreateOriginFxSoundPlayer.</remarks>
    public OriginFxSoundEffectEngine(OriginFxTimbreBank bank)
        : this(new OriginFxSynth(bank, new Ym3812(), new Ym3812(), true))
    {
    }

    /// <summary>Creates the sound player on an existing stereo synthesiser.</summary>
    public OriginFxSoundEffectEngine(OriginFxSynth synth)
    {
        ArgumentNullException.ThrowIfNull(synth);
        if (!synth.StereoPanningEnabled)
            throw new ArgumentException("The sound-effect player needs a stereo synthesiser.", nameof(synth));
        Synth = synth;
        for (int i = 0; i < _effects.Length; i++)
            _effects[i].Record = -1;
    }

    /// <summary>The stereo synthesiser.</summary>
    public OriginFxSynth Synth { get; }

    /// <summary>Output frames rendered so far.</summary>
    public long CurrentFrame => _currentFrame;

    /// <summary>Number of active slots.</summary>
    public int ActiveEffectCount
    {
        get
        {
            int count = 0;
            foreach (ref readonly var effect in _effects.AsSpan())
                if (effect.Active)
                    count++;
            return count;
        }
    }

    /// <summary>Returns the state of an active slot.</summary>
    public bool TryGetEffect(int slot, out OriginFxSoundEffectState state)
    {
        ref readonly var effect = ref _effects[slot];
        if (!effect.Active || effect.Record < 0)
        {
            state = default;
            return false;
        }
        state = new OriginFxSoundEffectState(effect.Record + 1, effect.Tag, effect.Priority, effect.Channel,
            effect.CurrentNote, effect.RemainingTicks, effect.Volume, effect.Pan, effect.Age);
        return true;
    }

    /// <summary>Finds the active effect with the given tag.</summary>
    public bool TryFindEffectByTag(int tag, out OriginFxSoundEffectState state)
    {
        for (int slot = 0; slot < SlotCount; slot++)
            if (_effects[slot].Active && _effects[slot].Tag == tag)
                return TryGetEffect(slot, out state);
        state = default;
        return false;
    }

    /// <summary>
    /// Starts sound <paramref name="soundNumber"/> (1..36) with volume and pan 0..127. Returns
    /// false when the number is invalid or no channel can be taken.
    /// </summary>
    /// <remarks>C: SdlPlayOriginFxSoundEffect.</remarks>
    public bool Play(int soundNumber, int volume, int pan, int tag, int priority)
    {
        if (soundNumber <= 0 || soundNumber > OriginFxSoundRecords.Count)
            return false;
        if (volume < 0)
            volume = 0;
        else if (volume > 127)
            volume = 127;
        if (pan < 0)
            pan = 0;
        else if (pan > 127)
            pan = 127;

        int channel = -1;
        ulong channelAge = 0;
        int channelPriority = priority;
        int freeEffectIndex = SlotCount;
        for (int effectIndex = 0; effectIndex < SlotCount; effectIndex++)
        {
            ref var effect = ref _effects[effectIndex];
            if (effect.Active && effect.Tag == tag)
            {
                channel = effect.Channel;
                channelAge = effect.Age;
                channelPriority = effect.Priority;
                StopSoundEffect(effectIndex, false);
                freeEffectIndex = effectIndex;
                break;
            }
            if (!effect.Active)
                freeEffectIndex = effectIndex;
        }
        if (channel < 0)
            channel = ChooseSoundEffectChannel(priority);
        if (channel < 0)
            return false;

        if (freeEffectIndex == SlotCount)
            return false;
        ref var slot = ref _effects[freeEffectIndex];
        slot = default;
        slot.Record = soundNumber - 1;
        slot.Age = channelAge != 0 ? channelAge : _nextSoundEffectAge++;
        slot.Tag = tag;
        slot.Priority = channelPriority;
        slot.Channel = (byte)channel;
        slot.Volume = (byte)volume;
        slot.Pan = (byte)pan;
        slot.Active = true;
        return StartSoundEffectRecord(ref slot);
    }

    /// <summary>Stops every effect (CC123 per channel) and releases every voice.</summary>
    /// <remarks>C: SdlStopOriginFxSoundEffects.</remarks>
    public void StopAll()
    {
        for (int effectIndex = 0; effectIndex < SlotCount; effectIndex++)
            StopSoundEffect(effectIndex, true);
        Synth.AllNotesOff(-1);
    }

    /// <summary>
    /// Mixes the next <c>interleaved.Length / 2</c> stereo frames of the effects into the
    /// buffer with saturation and <paramref name="gain"/> (0..0x7fff). Allocation-free.
    /// </summary>
    /// <remarks>C: SdlMixOriginFxSoundEffects.</remarks>
    public void Mix(Span<short> interleaved, uint gain)
    {
        int frameCount = interleaved.Length / 2;
        var synth = Synth;
        for (int frame = 0; frame < frameCount; frame++)
        {
            synth.AdvanceServiceClock();
            while (synth.TryConsumeServiceTick())
            {
                ServiceSoundEffects();
                synth.Service();
            }
            synth.GenerateOutputSample(out int left, out int right);
            short leftOutput = OriginFxSynth.ScaleOutputSample(left, gain);
            short rightOutput = OriginFxSynth.ScaleOutputSample(right, gain);
            interleaved[frame * 2] = OriginFxSynth.MixOutputSample(interleaved[frame * 2], leftOutput);
            interleaved[frame * 2 + 1] = OriginFxSynth.MixOutputSample(interleaved[frame * 2 + 1], rightOutput);
            _currentFrame++;
        }
    }

    /// <summary>One 60 Hz tick of the effect timers (normally driven by <see cref="Mix"/>).</summary>
    /// <remarks>C: OriginFxServiceSoundEffects.</remarks>
    public void ServiceSoundEffects()
    {
        for (int effectIndex = 0; effectIndex < SlotCount; effectIndex++)
        {
            ref var effect = ref _effects[effectIndex];
            int record = effect.Record;
            if (!effect.Active || record < 0)
                continue;
            byte flags = OriginFxSoundRecords.Field(record, 0);
            if ((flags & OriginFxSoundRecords.FlagSustain) != 0)
                continue;
            effect.RemainingTicks--;
            if (effect.RemainingTicks != 0)
                continue;

            Synth.NoteOff(effect.Channel, effect.CurrentNote);
            bool restart = false;
            if ((flags & OriginFxSoundRecords.FlagGlide) != 0)
            {
                byte targetNote = OriginFxSoundRecords.Field(record, 6);
                if (targetNote > effect.CurrentNote)
                {
                    effect.CurrentNote++;
                    restart = true;
                }
                else if (targetNote < effect.CurrentNote)
                {
                    effect.CurrentNote--;
                    restart = true;
                }
                else
                {
                    effect.CurrentNote = OriginFxSoundRecords.Field(record, 2);
                }
            }
            else
            {
                effect.CurrentNote = OriginFxSoundRecords.Field(record, 2);
            }
            if ((flags & OriginFxSoundRecords.FlagRetrigger) != 0)
                restart = true;
            if (restart)
            {
                Synth.NoteOn(effect.Channel, effect.CurrentNote, OriginFxSoundRecords.Field(record, 3));
                effect.RemainingTicks = OriginFxSoundRecords.Duration(record);
            }
            else if ((flags & OriginFxSoundRecords.FlagChain) != 0 && record + 1 < OriginFxSoundRecords.Count)
            {
                effect.Record = record + 1;
                StartSoundEffectRecord(ref effect);
            }
            else
            {
                effect.Active = false;
                effect.Record = -1;
                Synth.ControlChange(effect.Channel, 123, 0);
            }
        }
    }

    /// <remarks>C: OriginFxStopSoundEffect.</remarks>
    private void StopSoundEffect(int effectIndex, bool releaseChannel)
    {
        ref var effect = ref _effects[effectIndex];
        if (!effect.Active)
            return;
        if (releaseChannel)
            Synth.ControlChange(effect.Channel, 123, 0);
        else
            Synth.NoteOff(effect.Channel, effect.CurrentNote);
        effect.Active = false;
        effect.Record = -1;
    }

    /// <remarks>C: OriginFxStartSoundEffectRecord.</remarks>
    private bool StartSoundEffectRecord(ref EffectSlot effect)
    {
        int record = effect.Record;
        if (record < 0 || OriginFxSoundRecords.Field(record, 1) == 0)
        {
            effect.Active = false;
            effect.Record = -1;
            return false;
        }
        int program = OriginFxSoundRecords.Field(record, 1) - 1;
        Synth.SetProgram(effect.Channel, program);
        Synth.ControlChange(effect.Channel, 7, effect.Volume);
        Synth.ControlChange(effect.Channel, 10, effect.Pan);
        effect.CurrentNote = OriginFxSoundRecords.Field(record, 2);
        effect.RemainingTicks = OriginFxSoundRecords.Duration(record);
        Synth.NoteOn(effect.Channel, effect.CurrentNote, OriginFxSoundRecords.Field(record, 3));
        return true;
    }

    /// <summary>First free effect channel, else the channel of the oldest stealable effect, else -1.</summary>
    /// <remarks>C: OriginFxChooseSoundEffectChannel.</remarks>
    private int ChooseSoundEffectChannel(int priority)
    {
        int channelUsed = 0;
        for (int effectIndex = 0; effectIndex < SlotCount; effectIndex++)
        {
            if (!_effects[effectIndex].Active)
                continue;
            uint channel = (uint)(_effects[effectIndex].Channel - FirstChannel);
            if (channel < ChannelCount)
                channelUsed |= 1 << (int)channel;
        }
        for (int channel = 0; channel < ChannelCount; channel++)
            if ((channelUsed & (1 << channel)) == 0)
                return channel + FirstChannel;

        int victim = -1;
        for (int effectIndex = 0; effectIndex < SlotCount; effectIndex++)
        {
            ref readonly var effect = ref _effects[effectIndex];
            if (effect.Active && effect.Priority <= priority && (victim < 0 || effect.Age < _effects[victim].Age))
                victim = effectIndex;
        }
        if (victim < 0)
            return -1;
        int victimChannel = _effects[victim].Channel;
        StopSoundEffect(victim, false);
        return victimChannel;
    }

    /// <remarks>C: OriginFxSoundEffect (record pointer = 0-based row, -1 = null).</remarks>
    private struct EffectSlot
    {
        public int Record;
        public ulong Age;
        public int Tag;
        public int Priority;
        public ushort RemainingTicks;
        public byte Channel;
        public byte CurrentNote;
        public byte Volume;
        public byte Pan;
        public bool Active;
    }
}
