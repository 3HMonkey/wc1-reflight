using WingCommander.Audio.OriginFx;
using WingCommander.Core.Platform;

namespace WingCommander.Audio.Dos;

/// <summary>
/// The DOS audio output: 22050 Hz interleaved stereo s16, music player plus the long-lived
/// sound-effect player, each scaled by its own gain and mixed with saturation. The host pulls
/// <see cref="Render"/> on its audio thread; the game thread talks to the mixer only through
/// a lock-free command queue (play effect, stop effects, change music, gains), which replaces
/// the reference's mutex. Commands are applied at the start of the next <see cref="Render"/>
/// call, i.e. at buffer granularity exactly like the reference's lock around the callback.
/// </summary>
/// <remarks>C: SdlMixDosAdlibMusic (src/sdl/music.c) with SdlRenderOriginFxPlayer and
/// SdlMixOriginFxSoundEffects; SdlStartAudio's 22050 Hz stereo S16 format (src/sdl/audio.c).</remarks>
public sealed class DosAudioMixer : IAudioSource
{
    /// <summary>Output rate.</summary>
    public const int OutputRate = OriginFxSynth.OutputRate;

    /// <summary>Frames per buffer of the reference's SDL device (1/15 s).</summary>
    public const int ReferenceBufferFrames = 1470;

    /// <summary>Full-scale gain.</summary>
    public const uint UnityGain = 0x7fff;

    /// <summary>Command queue capacity.</summary>
    public const int CommandCapacity = 1024;

    private readonly AudioCommandQueue _commands = new(CommandCapacity);
    private readonly OriginFxSoundEffectEngine _soundEffects;
    private OriginFxSequencer? _music;
    private uint _musicGain = UnityGain;
    private uint _soundGain = UnityGain;
    private long _droppedCommands;
    private long _renderedFrames;
    private Exception? _fault;

    /// <summary>Creates the mixer with a fresh sound-effect player on <paramref name="bank"/>.</summary>
    public DosAudioMixer(OriginFxTimbreBank bank)
        : this(new OriginFxSoundEffectEngine(bank))
    {
    }

    /// <summary>
    /// Creates the mixer around an existing sound-effect player; from now on the player must
    /// only be touched by the audio thread (through this mixer).
    /// </summary>
    public DosAudioMixer(OriginFxSoundEffectEngine soundEffects)
    {
        ArgumentNullException.ThrowIfNull(soundEffects);
        _soundEffects = soundEffects;
    }

    /// <inheritdoc />
    public int SampleRate => OutputRate;

    /// <summary>Commands rejected because the queue was full (audio thread not running).</summary>
    public long DroppedCommandCount => Interlocked.Read(ref _droppedCommands);

    /// <summary>Total frames rendered (audio thread clock).</summary>
    public long RenderedFrames => Interlocked.Read(ref _renderedFrames);

    /// <summary>The exception that disabled rendering, if any (the mixer then outputs silence).</summary>
    public Exception? Fault => Volatile.Read(ref _fault);

    // ---------------------------------------------------------------- game thread

    /// <summary>Queues a sound effect (see <see cref="OriginFxSoundEffectEngine.Play"/>).</summary>
    /// <returns>False when the queue is full.</returns>
    public bool PostPlaySoundEffect(int soundNumber, int volume, int pan, int tag, int priority) =>
        Post(new AudioCommand
        {
            Kind = AudioCommandKind.PlaySoundEffect,
            Arg0 = soundNumber,
            Arg1 = volume,
            Arg2 = pan,
            Arg3 = tag,
            Arg4 = priority,
        });

    /// <summary>Queues "stop every sound effect".</summary>
    public bool PostStopSoundEffects() => Post(new AudioCommand { Kind = AudioCommandKind.StopSoundEffects });

    /// <summary>
    /// Queues a music change: <paramref name="sequencer"/> (freshly created, not yet rendered)
    /// replaces the current music; null stops the music.
    /// </summary>
    public bool PostMusic(OriginFxSequencer? sequencer) =>
        Post(new AudioCommand { Kind = AudioCommandKind.SetMusic, Music = sequencer });

    /// <summary>Queues a new music gain (0..0x7fff).</summary>
    public bool PostMusicGain(uint gain) =>
        Post(new AudioCommand { Kind = AudioCommandKind.SetMusicGain, Arg0 = (int)Math.Min(gain, UnityGain) });

    /// <summary>Queues a new sound-effect gain (0..0x7fff).</summary>
    public bool PostSoundGain(uint gain) =>
        Post(new AudioCommand { Kind = AudioCommandKind.SetSoundGain, Arg0 = (int)Math.Min(gain, UnityGain) });

    // ---------------------------------------------------------------- audio thread

    /// <summary>
    /// Renders the next <c>interleavedStereo.Length / 2</c> frames. Never blocks, allocates or
    /// throws: an unexpected exception is stored in <see cref="Fault"/> and silence follows.
    /// </summary>
    /// <remarks>C: SdlMixDosAdlibMusic.</remarks>
    public void Render(Span<short> interleavedStereo)
    {
        interleavedStereo.Clear();
        if (_fault is not null)
            return;
        try
        {
            ApplyCommands();
            _music?.Mix(interleavedStereo, _musicGain);
            _soundEffects.Mix(interleavedStereo, _soundGain);
            Interlocked.Add(ref _renderedFrames, interleavedStereo.Length / 2);
        }
        catch (Exception e)
        {
            interleavedStereo.Clear();
            Volatile.Write(ref _fault, e);
        }
    }

    private bool Post(in AudioCommand command)
    {
        if (_commands.TryEnqueue(command))
            return true;
        Interlocked.Increment(ref _droppedCommands);
        return false;
    }

    private void ApplyCommands()
    {
        while (_commands.TryDequeue(out var command))
        {
            switch (command.Kind)
            {
                case AudioCommandKind.PlaySoundEffect:
                    _soundEffects.Play(command.Arg0, command.Arg1, command.Arg2, command.Arg3, command.Arg4);
                    break;
                case AudioCommandKind.StopSoundEffects:
                    _soundEffects.StopAll();
                    break;
                case AudioCommandKind.SetMusic:
                    _music = command.Music;
                    break;
                case AudioCommandKind.SetMusicGain:
                    _musicGain = (uint)command.Arg0;
                    break;
                case AudioCommandKind.SetSoundGain:
                    _soundGain = (uint)command.Arg0;
                    break;
            }
        }
    }
}
