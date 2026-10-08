namespace WingCommander.Audio.Director;

/// <summary>
/// The music player behind <see cref="MusicDirector"/> (DOS: OriginFX sequencer on the mixer;
/// later possibly the Kilrathi Saga streamer). Called on the game thread only; every member
/// is non-blocking.
/// </summary>
/// <remarks>
/// Track completion and the sequence position are reported on the game's deterministic
/// virtual clock (ADR-009): the backend derives them from the time passed to
/// <see cref="Update"/> since the track was started, not from the audio thread's progress.
/// </remarks>
public interface IMusicBackend
{
    /// <summary>Track (MUSIC.MID section) currently loaded, or -1.</summary>
    /// <remarks>C: g_nSdlActiveMusicTrack (src/sdl/music.c).</remarks>
    int ActiveTrack { get; }

    /// <summary>True when the loaded track has played to its end (as of the last <see cref="Update"/>).</summary>
    /// <remarks>C: SdlOriginFxPlayerFinished.</remarks>
    bool IsActiveTrackFinished { get; }

    /// <summary>
    /// Last sequence cue of the loaded track (0 before the first cue), or -1 when none is
    /// loaded (as of the last <see cref="Update"/>).
    /// </summary>
    /// <remarks>C: SdlGetOriginFxMusicSequencePosition.</remarks>
    int SequencePosition { get; }

    /// <summary>Advances the game-side playback clock to <paramref name="now"/> (virtual time).</summary>
    void Update(TimeSpan now);

    /// <summary>
    /// Loads <paramref name="track"/> and starts it at virtual time <paramref name="now"/>,
    /// replacing the current track. Returns false (and keeps the current track) when the
    /// section cannot be decoded or parsed.
    /// </summary>
    bool TryStartTrack(int track, TimeSpan now);

    /// <summary>Unloads the current track (silence).</summary>
    /// <remarks>C: SdlDeleteDosAdlibTrack.</remarks>
    void StopTrack();

    /// <summary>Optional warm-up: decodes and parses a track ahead of use (no playback).</summary>
    /// <remarks>C: PreloadMusicTrackHook (a stub in this build; the DOS driver loaded the section).</remarks>
    void PreloadTrack(int track);

    /// <summary>Applies the 0..20 music and sound-effect volume settings.</summary>
    /// <remarks>C: SdlUpdateDosAdlibMusicVolume.</remarks>
    void ApplyVolumeSettings(int musicVolumeSetting, int soundVolumeSetting);
}
