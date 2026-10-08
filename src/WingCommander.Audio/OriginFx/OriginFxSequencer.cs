using WingCommander.Audio.Opl;

namespace WingCommander.Audio.OriginFx;

/// <summary>
/// Plays one <see cref="OriginFxSequence"/> on its own mono <see cref="OriginFxSynth"/> (the
/// reference's music player). <see cref="Mix"/> runs on the audio thread; <see cref="Finished"/>
/// and <see cref="SequencePosition"/> may be read from any thread.
/// </summary>
/// <remarks>C: SdlCreateOriginFxPlayer, SdlMixOriginFxPlayer, SdlRenderOriginFxPlayer,
/// OriginFxProcessDueEvents, OriginFxDispatchEvent (src/sdl/originfx.cpp).</remarks>
public sealed class OriginFxSequencer
{
    private readonly OriginFxEvent[] _events;
    private int _nextEvent;
    private long _currentFrame;
    private volatile bool _finished;
    private int _sequencePosition;

    /// <summary>Creates a player for <paramref name="sequence"/> on a fresh YM3812.</summary>
    /// <remarks>C: SdlCreateOriginFxPlayer.</remarks>
    public OriginFxSequencer(OriginFxSequence sequence, OriginFxTimbreBank bank)
        : this(sequence, new OriginFxSynth(bank, new Ym3812(), null, false))
    {
    }

    /// <summary>Creates a player on an existing (freshly initialised) synthesiser.</summary>
    public OriginFxSequencer(OriginFxSequence sequence, OriginFxSynth synth)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(synth);
        Sequence = sequence;
        Synth = synth;
        _events = sequence.EventArray;
    }

    /// <summary>The sequence being played.</summary>
    public OriginFxSequence Sequence { get; }

    /// <summary>The synthesiser driven by this player.</summary>
    public OriginFxSynth Synth { get; }

    /// <summary>True once every event was dispatched and the end frame reached.</summary>
    /// <remarks>C: SdlOriginFxPlayerFinished.</remarks>
    public bool Finished => _finished;

    /// <summary>Value of the last FE 03 cue (0 before the first cue).</summary>
    /// <remarks>C: SdlOriginFxPlayerSequencePosition.</remarks>
    public int SequencePosition => Volatile.Read(ref _sequencePosition);

    /// <summary>Number of output frames produced so far.</summary>
    public long CurrentFrame => Volatile.Read(ref _currentFrame);

    /// <summary>Clears <paramref name="interleaved"/> and mixes the next frames into it.</summary>
    /// <remarks>C: SdlRenderOriginFxPlayer.</remarks>
    public void Render(Span<short> interleaved, uint gain)
    {
        interleaved.Clear();
        Mix(interleaved, gain);
    }

    /// <summary>
    /// Mixes the next <c>interleaved.Length / 2</c> stereo frames into the buffer with
    /// saturation, applying <paramref name="gain"/> (0..0x7fff). Stops at the end of the
    /// sequence (the remaining frames are left untouched). Allocation-free.
    /// </summary>
    /// <remarks>C: SdlMixOriginFxPlayer.</remarks>
    public void Mix(Span<short> interleaved, uint gain)
    {
        if (_finished)
            return;
        int frameCount = interleaved.Length / 2;
        var synth = Synth;
        for (int frame = 0; frame < frameCount; frame++)
        {
            ProcessDueEvents();
            if (_nextEvent == _events.Length && _currentFrame >= Sequence.EndFrame)
            {
                synth.AllNotesOff(-1);
                _finished = true;
                return;
            }
            synth.AdvanceServiceClock();
            while (synth.TryConsumeServiceTick())
                synth.Service();
            synth.GenerateOutputSample(out int left, out int right);
            short leftOutput = OriginFxSynth.ScaleOutputSample(left, gain);
            short rightOutput = OriginFxSynth.ScaleOutputSample(right, gain);
            interleaved[frame * 2] = OriginFxSynth.MixOutputSample(interleaved[frame * 2], leftOutput);
            interleaved[frame * 2 + 1] = OriginFxSynth.MixOutputSample(interleaved[frame * 2 + 1], rightOutput);
            Volatile.Write(ref _currentFrame, _currentFrame + 1);
        }
        if (_nextEvent == _events.Length && _currentFrame >= Sequence.EndFrame)
        {
            synth.AllNotesOff(-1);
            _finished = true;
        }
    }

    /// <remarks>C: OriginFxProcessDueEvents.</remarks>
    private void ProcessDueEvents()
    {
        while (_nextEvent < _events.Length && _events[_nextEvent].Frame <= _currentFrame)
        {
            DispatchEvent(in _events[_nextEvent]);
            _nextEvent++;
        }
    }

    /// <remarks>C: OriginFxDispatchEvent.</remarks>
    private void DispatchEvent(in OriginFxEvent e)
    {
        if (e.Type == OriginFxEventType.Sequence)
        {
            Volatile.Write(ref _sequencePosition, e.Data1);
            return;
        }
        Synth.ProcessChannelMessage(e.Status, e.Data1, e.Data2);
    }
}
