using WingCommander.Audio.Director;
using WingCommander.Audio.Dos;
using WingCommander.Core.Numerics;
using WingCommander.Core.Resources;
using WingCommander.Core.Runtime;

namespace WingCommander.Game.Audio;

/// <summary>
/// The game's DOS audio: the OPL2/OriginFX backend whose mixer the host plays, the music
/// director and the volume settings. All game-side calls run on the virtual clock, so music
/// cues (intro sync, end of track) are deterministic and work without an audio device.
/// </summary>
/// <remarks>C: the Origin FX driver set-up of LoadOriginFxDrivers (logic.c) and the SDL port's
/// SdlServiceOriginFxMusic at the top of every event pump (sdl/music.c).</remarks>
public sealed class GameAudio : IIntroMusic
{
    private readonly GameScheduler _scheduler;
    private int _introPreviousTrack = MusicDirector.IntroMusicDisabled;

    public GameAudio(DosAudioBackend backend, AudioVolumeSettings volumes, CRandom random, GameScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(scheduler);
        Backend = backend;
        Volumes = volumes;
        Music = new MusicDirector(backend, volumes, random);
        Sfx = new SoundEffectManager(backend, SoundWorld);
        _scheduler = scheduler;
    }

    /// <summary>Loads MUSIC.MID and WINGLDR.TIM.</summary>
    public static GameAudio Create(GameDirectory directory, AudioVolumeSettings volumes, CRandom random, GameScheduler scheduler) =>
        new(DosAudioBackend.Create(directory), volumes, random, scheduler);

    public DosAudioBackend Backend { get; }

    public MusicDirector Music { get; }

    /// <summary>Game-wide sound effects (C: PlaySfxWaveFileByNumber and friends).</summary>
    public SoundEffectManager Sfx { get; }

    /// <summary>The flight world positional sounds are computed in (set by the flight layer).</summary>
    public SoundWorldSlot SoundWorld { get; } = new();

    public AudioVolumeSettings Volumes { get; }

    private TimeSpan Now => TimeSpan.FromMilliseconds(_scheduler.Now);

    /// <summary>Per-pump music servicing (track requests, volume, end of track).</summary>
    /// <remarks>C: SdlServiceOriginFxMusic.</remarks>
    public void Service() => Music.Service(Now);

    bool IIntroMusic.Begin()
    {
        _introPreviousTrack = Music.BeginStartupIntroMusic(Now);
        return _introPreviousTrack != MusicDirector.IntroMusicDisabled;
    }

    int IIntroMusic.SequencePosition => Music.SequencePosition;

    void IIntroMusic.End()
    {
        Music.EndStartupIntroMusic(_introPreviousTrack, Now);
        _introPreviousTrack = MusicDirector.IntroMusicDisabled;
    }
}
