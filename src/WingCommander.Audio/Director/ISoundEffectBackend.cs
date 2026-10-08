namespace WingCommander.Audio.Director;

/// <summary>
/// The sound-effect player behind <see cref="SoundEffectManager"/>. Called on the game thread.
/// </summary>
public interface ISoundEffectBackend
{
    /// <summary>
    /// Starts sound <paramref name="soundNumber"/> (1-based) with volume and pan 0..127
    /// (64 = centre). Effects with the same <paramref name="tag"/> replace each other.
    /// Returns false when the effect was not accepted.
    /// </summary>
    /// <remarks>C: SdlPlayDosSoundEffect (src/sdl/music.c).</remarks>
    bool Play(int soundNumber, int volume, int pan, int tag, int priority);

    /// <summary>Stops every effect.</summary>
    /// <remarks>C: SdlStopDosSoundEffects (player part).</remarks>
    void StopAll();
}
