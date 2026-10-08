namespace WingCommander.Game.Audio;

/// <summary>
/// The music hooks the DOS startup intro needs: start the intro track (OriginFX track 19),
/// read the sequence position it synchronises to, restore the previous track.
/// </summary>
/// <remarks>C: nCurrentMusicTrack = 19 + SdlServiceOriginFxMusic,
/// SdlGetOriginFxMusicSequencePosition (sdl/music.c), used by sdl/dos_intro.c.</remarks>
public interface IIntroMusic
{
    /// <summary>Starts the intro track. Returns false when music is disabled.</summary>
    bool Begin();

    /// <summary>Cue reached by the playing sequence (0..6), or -1 when the music is not sequenced.</summary>
    int SequencePosition { get; }

    /// <summary>Restores the track that was requested before <see cref="Begin"/>.</summary>
    void End();
}
