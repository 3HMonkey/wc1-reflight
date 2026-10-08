using WingCommander.Audio.Director;
using WingCommander.Audio.OriginFx;
using WingCommander.Core.Resources;

namespace WingCommander.Audio.Dos;

/// <summary>
/// Game-thread glue between the directors and the <see cref="DosAudioMixer"/>: loads
/// MUSIC.MID / WINGLDR.TIM, parses requested sections (cached, immutable) into new
/// sequencers handed to the audio thread, converts the volume settings to gains and applies
/// the laser rapid-fire tag rule. Hand <see cref="Mixer"/> to <c>IGameHost.StartAudio</c>.
/// Track completion and cue positions follow the game's virtual clock (ADR-009): played
/// frames = (now - start) x 22050, evaluated against the same sequence the audio thread
/// renders, so game logic is deterministic whether or not audio is running.
/// </summary>
/// <remarks>C: SdlInitializeOriginFxAudio, SdlServiceOriginFxMusic (player part), SdlPlayDosSoundEffect,
/// SdlStopDosSoundEffects, SdlCalculateDosAudioGain, SdlUpdateDosAdlibMusicVolume (src/sdl/music.c).</remarks>
public sealed class DosAudioBackend : IMusicBackend, ISoundEffectBackend
{
    /// <summary>Tag pair used for the laser sound so paired guns overlap.</summary>
    public const int RapidFireTagBase = 64;

    private readonly PacketFile _music;
    private readonly OriginFxSequence?[] _sequences;
    private OriginFxSequencer? _activeSequencer;
    private TimeSpan _trackStart;
    private long _playedFrames;
    private uint _rapidFireTag;
    private int _musicVolumeSetting = -1;
    private int _soundVolumeSetting = -1;

    /// <param name="music">The MUSIC.MID container.</param>
    /// <param name="bank">Timbres of WINGLDR.TIM section 1.</param>
    public DosAudioBackend(PacketFile music, OriginFxTimbreBank bank)
        : this(music, bank, new DosAudioMixer(bank))
    {
    }

    /// <summary>Creates the backend around an existing mixer (tests, custom effect players).</summary>
    public DosAudioBackend(PacketFile music, OriginFxTimbreBank bank, DosAudioMixer mixer)
    {
        ArgumentNullException.ThrowIfNull(music);
        ArgumentNullException.ThrowIfNull(bank);
        ArgumentNullException.ThrowIfNull(mixer);
        _music = music;
        Bank = bank;
        _sequences = new OriginFxSequence?[music.SectionCount];
        Mixer = mixer;
    }

    /// <summary>The audio source to start on the host.</summary>
    public DosAudioMixer Mixer { get; }

    /// <summary>The timbre bank.</summary>
    public OriginFxTimbreBank Bank { get; }

    /// <summary>Number of MUSIC.MID sections.</summary>
    public int TrackCount => _music.SectionCount;

    /// <inheritdoc />
    public int ActiveTrack { get; private set; } = -1;

    /// <inheritdoc />
    public bool IsActiveTrackFinished => _activeSequencer is null || _activeSequencer.Sequence.IsFinishedAfter(_playedFrames);

    /// <inheritdoc />
    public int SequencePosition => _activeSequencer?.Sequence.GetSequencePositionAfter(_playedFrames) ?? -1;

    /// <summary>Output frames the active track has played on the game clock (as of the last <see cref="Update"/>).</summary>
    public long PlayedFrames => _playedFrames;

    /// <summary>The sequencer handed to the mixer for the active track.</summary>
    public OriginFxSequencer? ActiveSequencer => _activeSequencer;

    /// <summary>Loads MUSIC.MID and the AdLib timbres from the game directory.</summary>
    /// <remarks>C: SdlInitializeOriginFxAudio(1) without opening the device.</remarks>
    public static DosAudioBackend Create(GameDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        return new DosAudioBackend(directory.OpenPacket(OriginFxSequence.FileName), OriginFxTimbreBank.Load(directory));
    }

    /// <summary>The 0..0x7fff gain of a 0..20 volume setting.</summary>
    /// <remarks>C: SdlCalculateDosAudioGain.</remarks>
    public static uint CalculateGain(int volumeSetting)
    {
        int tableIndex = volumeSetting / 2;
        if (tableIndex < 0)
            tableIndex = 0;
        else if (tableIndex > 10)
            tableIndex = 10;
        int level = AudioVolumeSettings.VolumeLevels[tableIndex];
        if (level < 0)
            level = 0;
        else if (level > 64000)
            level = 64000;
        return (uint)((long)level * 0x7fff / 64000);
    }

    /// <summary>Returns the parsed sequence of a section (parsed once, then cached).</summary>
    public OriginFxSequence GetSequence(int track)
    {
        if ((uint)track >= (uint)_sequences.Length)
            throw new GameDataException($"MUSIC.MID has no section {track}.");
        return _sequences[track] ??= OriginFxSequence.Parse(_music.GetSection(track).Span);
    }

    /// <summary>Parses every section now so later track changes never parse on a game tick.</summary>
    public void PreloadAllTracks()
    {
        for (int track = 0; track < _sequences.Length; track++)
            PreloadTrack(track);
    }

    /// <inheritdoc />
    public void PreloadTrack(int track)
    {
        try
        {
            GetSequence(track);
        }
        catch (GameDataException)
        {
            // reported when the track is actually requested
        }
    }

    /// <inheritdoc />
    public void Update(TimeSpan now)
    {
        if (_activeSequencer is null)
            return;
        long elapsedTicks = Math.Max(0, (now - _trackStart).Ticks);
        _playedFrames = elapsedTicks / TimeSpan.TicksPerSecond * OriginFxSynth.OutputRate
            + elapsedTicks % TimeSpan.TicksPerSecond * OriginFxSynth.OutputRate / TimeSpan.TicksPerSecond;
    }

    /// <inheritdoc />
    /// <remarks>C: SdlServiceOriginFxMusic (extract, SdlCreateOriginFxPlayer, swap).</remarks>
    public bool TryStartTrack(int track, TimeSpan now)
    {
        OriginFxSequence sequence;
        try
        {
            sequence = GetSequence(track);
        }
        catch (GameDataException)
        {
            return false;
        }
        var sequencer = new OriginFxSequencer(sequence, Bank);
        _activeSequencer = sequencer;
        _trackStart = now;
        _playedFrames = 0;
        ActiveTrack = track;
        Mixer.PostMusic(sequencer);
        return true;
    }

    /// <inheritdoc />
    /// <remarks>C: SdlDeleteDosAdlibTrack.</remarks>
    public void StopTrack()
    {
        if (_activeSequencer is not null)
            Mixer.PostMusic(null);
        _activeSequencer = null;
        _playedFrames = 0;
        ActiveTrack = -1;
    }

    /// <inheritdoc />
    /// <remarks>C: SdlUpdateDosAdlibMusicVolume.</remarks>
    public void ApplyVolumeSettings(int musicVolumeSetting, int soundVolumeSetting)
    {
        if (_musicVolumeSetting != musicVolumeSetting && Mixer.PostMusicGain(CalculateGain(musicVolumeSetting)))
            _musicVolumeSetting = musicVolumeSetting;
        if (_soundVolumeSetting != soundVolumeSetting && Mixer.PostSoundGain(CalculateGain(soundVolumeSetting)))
            _soundVolumeSetting = soundVolumeSetting;
    }

    /// <inheritdoc />
    /// <remarks>
    /// C: SdlPlayDosSoundEffect. The effect is queued for the audio thread, so the engine's
    /// own accept/reject result is not known here; true means "queued with a valid number"
    /// (the engine can only reject a valid effect when every channel holds a higher-priority
    /// effect, which cannot happen with the priority 0 that every call site passes).
    /// </remarks>
    public bool Play(int soundNumber, int volume, int pan, int tag, int priority)
    {
        if (soundNumber <= 0 || soundNumber > OriginFxSoundRecords.Count)
            return false;
        if (soundNumber == SoundEffectNumber.Laser)
        {
            tag = RapidFireTagBase + (int)(_rapidFireTag & 1);
            _rapidFireTag++;
        }
        return Mixer.PostPlaySoundEffect(soundNumber, volume, pan, tag, priority);
    }

    /// <inheritdoc />
    /// <remarks>C: SdlStopDosSoundEffects (the bAfterburnerSfxActive reset lives in SoundEffectManager).</remarks>
    public void StopAll() => Mixer.PostStopSoundEffects();
}
