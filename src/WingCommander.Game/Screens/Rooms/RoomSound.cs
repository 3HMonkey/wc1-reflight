using WingCommander.Audio.Director;

namespace WingCommander.Game.Screens.Rooms;

/// <summary>Music and interface sounds of the rooms (all no-ops when the game runs without audio).</summary>
internal static class RoomSound
{
    /// <summary>Requests a room's music track with a switch.</summary>
    /// <remarks>C: <c>PreloadMusicTrackHook(track); spacetrack(track, 2, 1)</c>.</remarks>
    public static void StartTrack(Wc1Game game, int track)
    {
        if (game.Audio?.Music is not { } music)
            return;
        music.PreloadMusicTrackHook(track);
        music.SpaceTrack(track, 2, 1);
    }

    /// <summary>Requests a music track (spacetrack) without preloading.</summary>
    public static void SpaceTrack(Wc1Game game, int track, int mode, short enabled) =>
        game.Audio?.Music.SpaceTrack(track, mode, enabled);

    /// <remarks>C: PreloadMusicTrackHook.</remarks>
    public static void Preload(Wc1Game game, int track) => game.Audio?.Music.PreloadMusicTrackHook(track);

    /// <remarks>C: ReleaseMusicTrackHook.</remarks>
    public static void Release(Wc1Game game, int track) => game.Audio?.Music.ReleaseMusicTrackHook(track);

    /// <summary>Stops the room music unless music commands are suppressed, and releases the track.</summary>
    /// <remarks>C: <c>StopMusicUnlessSuppressed(); ReleaseMusicTrackHook(track)</c>.</remarks>
    public static void StopTrack(Wc1Game game, int track)
    {
        if (game.Audio?.Music is not { } music)
            return;
        music.StopMusicUnlessSuppressed();
        music.ReleaseMusicTrackHook(track);
    }

    /// <remarks>C: StopMusicUnlessSuppressed (0x42E8B0, music.c).</remarks>
    public static void StopMusicUnlessSuppressed(Wc1Game game) => game.Audio?.Music.StopMusicUnlessSuppressed();

    /// <summary>Plays an interface sound at full volume, centred (no source object).</summary>
    /// <remarks>C: PlaySfxWaveFileByNumber(number, -1, 0) (0x42EF30, music.c); with source -1 the
    /// sound manager plays at volume 127 and pan 64 with tag -1.</remarks>
    public static void PlayInterfaceSound(Wc1Game game, int soundNumber) => game.Audio?.Sfx.PlaySfx(soundNumber, -1, 0);

    /// <summary>Stops every sound effect.</summary>
    /// <remarks>C: FlushSoundEffects / ResetSoundState (music.c) -> SdlStopDosSoundEffects.</remarks>
    public static void StopAllSounds(Wc1Game game) => game.Audio?.Sfx.StopAllSounds();
}
