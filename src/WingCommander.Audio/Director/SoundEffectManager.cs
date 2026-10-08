namespace WingCommander.Audio.Director;

/// <summary>
/// Game-side sound-effect logic for DOS data: the single entry point
/// <see cref="PlaySfx"/> (distance attenuation, stereo pan, tag = source object), "stop all",
/// the reset helpers used by scenes and flight, and the afterburner / damage-alarm
/// bookkeeping. Game thread only.
/// </summary>
/// <remarks>C: PlaySfxWaveFileByNumber + SdlPlayGameSoundEffect (music.c, sdl/music.c), stop_all_sounds
/// (sound.c), FlushSoundEffect*, ResetSoundState* (music.c), afterburner/alarm parts of logic.c,
/// spc.c and cockpt.c.</remarks>
public sealed class SoundEffectManager
{
    /// <summary>Space object slots.</summary>
    /// <remarks>C: SPACE_OBJECT_COUNT.</remarks>
    public const int SpaceObjectCount = 64;

    /// <summary>Volume of a non-positional or zero-distance sound.</summary>
    /// <remarks>C: SDL_SOUND_FULL_VOLUME.</remarks>
    public const int FullVolume = 127;

    /// <summary>Sounds quieter than this are not started.</summary>
    /// <remarks>C: SDL_SOUND_AUDIBLE_VOLUME.</remarks>
    public const int AudibleVolume = 10;

    /// <summary>Centre pan.</summary>
    /// <remarks>C: SDL_SOUND_CENTRE_PAN.</remarks>
    public const int CentrePan = 64;

    /// <summary>One volume step per 500 m.</summary>
    /// <remarks>C: SDL_SOUND_METRES_PER_VOLUME_STEP.</remarks>
    public const int MetresPerVolumeStep = 500;

    private readonly ISoundEffectBackend _backend;
    private readonly IFlightSoundWorld _world;
    private readonly bool[] _sourceActive = new bool[SpaceObjectCount + 1];

    public SoundEffectManager(ISoundEffectBackend backend, IFlightSoundWorld world)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(world);
        _backend = backend;
        _world = world;
    }

    /// <summary>Set while the player's afterburner sound (12) is the current player sound.</summary>
    /// <remarks>C: bAfterburnerSfxActive (0x5a7cec).</remarks>
    public bool AfterburnerSfxActive { get; set; }

    /// <summary>Frame until which the afterburner sound counts as running.</summary>
    /// <remarks>C: nAfterburnerSoundDeadline (0x5a7ce8).</remarks>
    public int AfterburnerSoundDeadline { get; set; }

    /// <summary>Handle of the damage alarm; never set non-zero by the reconstructed code.</summary>
    /// <remarks>C: nDamageAlarmSfxHandle (0x5a7ec0).</remarks>
    public int DamageAlarmSfxHandle { get; set; }

    /// <summary>Gate for the per-frame proximity sounds of <see cref="MusicDirector.ServiceTrack"/>.</summary>
    /// <remarks>C: nFlightSoundEffectsEnabled (0x46aa34).</remarks>
    public bool FlightSoundEffectsEnabled { get; set; } = true;

    /// <summary>True once a positional sound was started for <paramref name="sourceObject"/> (-1 allowed).</summary>
    /// <remarks>C: aiSoundEffectSourceActive[sourceObject + 1].</remarks>
    public bool IsSourceActive(int sourceObject) => _sourceActive[sourceObject + 1];

    /// <summary>Clears the "sound started" flag of an object (when the object is removed).</summary>
    /// <remarks>C: remove_hazard's aiSoundEffectSourceActive[obj + 1] = 0.</remarks>
    public void ClearSourceActive(int sourceObject) => _sourceActive[sourceObject + 1] = false;

    /// <summary>
    /// Plays game sound <paramref name="soundNumber"/> (1..36). <paramref name="sourceObject"/>
    /// is the emitting space object (-1 = player/UI sound); its distance lowers the volume by
    /// one step per 500 m (inaudible below 10) and its direction sets the pan.
    /// <paramref name="looping"/> becomes the effect priority (every call site passes 0).
    /// Returns true when the effect was started.
    /// </summary>
    /// <remarks>C: PlaySfxWaveFileByNumber (0x42EF30, music.c) -> SdlPlayGameSoundEffect (DOS branch, sdl/music.c).</remarks>
    public bool PlaySfx(int soundNumber, int sourceObject, int looping)
    {
        int magnitude = 0;
        int pan = CentrePan;
        if (sourceObject != -1)
        {
            if (sourceObject < 0 || sourceObject >= SpaceObjectCount)
                return false;
            var geometry = _world.GetSoundSourceGeometry(sourceObject);
            magnitude = geometry.Magnitude;
            int scaledPan = geometry.StereoOffset * CentrePan;
            if (scaledPan < 0)
                scaledPan = -((-scaledPan + 0xff) / 0x100);
            else
                scaledPan /= 0x100;
            pan -= scaledPan;
            if (pan < 0)
                pan = 0;
            else if (pan > 127)
                pan = 127;
        }

        int volume = FullVolume;
        if (sourceObject != -1)
            volume -= (magnitude / MetresPerVolumeStep) >> 8;
        if (volume < 0)
            volume = 0;
        if (volume < AudibleVolume)
            return false;
        if (!_backend.Play(soundNumber, volume, pan, sourceObject, looping))
            return false;
        _sourceActive[sourceObject + 1] = true;
        if (sourceObject == -1)
            AfterburnerSfxActive = soundNumber == SoundEffectNumber.Afterburner;
        return true;
    }

    /// <summary>Stops every sound effect.</summary>
    /// <remarks>C: stop_all_sounds (0x42B640, sound.c) -> SdlStopDosSoundEffects (sdl/music.c).</remarks>
    public void StopAllSounds()
    {
        AfterburnerSfxActive = false;
        _backend.StopAll();
    }

    /// <summary>Stops everything in this build.</summary>
    /// <remarks>C: FlushSoundEffect (0x42E3A0, music.c).</remarks>
    public void FlushSoundEffect() => StopAllSounds();

    /// <remarks>C: FlushSoundEffects (0x42E3C0, music.c).</remarks>
    public void FlushSoundEffects() => StopAllSounds();

    /// <summary>The handle argument of the call sites is ignored.</summary>
    /// <remarks>C: FlushSoundEffectsAndLog (0x42EF10, music.c).</remarks>
    public void FlushSoundEffectsAndLog() => FlushSoundEffects();

    /// <remarks>C: ResetSoundState (0x42EE80, music.c).</remarks>
    public void ResetSoundState()
    {
        FlushSoundEffects();
        AfterburnerSfxActive = false;
        DamageAlarmSfxHandle = 0;
    }

    /// <remarks>C: ResetSoundStateForScene (0x42EEA0, music.c).</remarks>
    public void ResetSoundStateForScene()
    {
        ResetSoundState();
        FlightSoundEffectsEnabled = false;
    }

    /// <remarks>C: ResetSoundStateForFlight (0x42EEB0, music.c).</remarks>
    public void ResetSoundStateForFlight()
    {
        ResetSoundState();
        FlightSoundEffectsEnabled = true;
    }

    /// <summary>
    /// VDU static noise. Kilrathi Saga plays sfx22.wav here; with DOS data the reference plays
    /// nothing (the ix library is disabled), so neither does the port.
    /// </summary>
    /// <remarks>C: PlaySnowStaticSound (0x42B680, sound.c).</remarks>
    public void PlaySnowStaticSound()
    {
    }

    /// <summary>Cockpit VDU selection click: always sound 25.</summary>
    /// <remarks>C: PlayCockpitSelectionSfx (0x417F00, cockpt.c).</remarks>
    public void PlayCockpitSelectionSfx(short selectionSound)
    {
        _ = selectionSound;
        PlaySfx(SoundEffectNumber.VduSelect, -1, 0);
    }

    /// <summary>
    /// Per-frame service of the Kilrathi Saga mixer. Nothing to do with DOS data: the effect
    /// timers run on the mixer's 60 Hz clock on the audio thread.
    /// </summary>
    /// <remarks>C: ServiceSoundSystem (0x42B7D0, sound.c).</remarks>
    public void ServiceSoundSystem()
    {
    }

    /// <summary>
    /// Kilrathi Saga master volume (0..64999). Ignored with DOS data: the effect gain follows
    /// <see cref="AudioVolumeSettings.SfxVolume"/> through <see cref="MusicDirector.Service"/>.
    /// </summary>
    /// <remarks>C: SetSoundEffectsVolume (0x42B7E0, sound.c).</remarks>
    public void SetSoundEffectsVolume(int level) => _ = level;

    /// <summary>Raw OriginFX descriptor player (fireworks); a stub returning 0 in this build.</summary>
    /// <remarks>C: SoundFxTick (0x42EF00, music.c).</remarks>
    public uint SoundFxTick() => 0;

    /// <summary>Re-fires the afterburner sound at most every 6 frames while it burns.</summary>
    /// <remarks>C: the sound part of your_afterburner (0x421920, logic.c).</remarks>
    public void ServiceAfterburnerSound(short spaceFrame)
    {
        int frame = spaceFrame;
        int nextSoundFrame = frame + 6;
        if (nextSoundFrame < AfterburnerSoundDeadline)
            AfterburnerSoundDeadline = 0;
        if (AfterburnerSoundDeadline < frame)
        {
            AfterburnerSoundDeadline = nextSoundFrame;
            PlaySfx(SoundEffectNumber.Afterburner, -1, 0);
        }
    }

    /// <summary>The afterburner ran out: stops the effects if the afterburner sound is current.</summary>
    /// <remarks>C: afterburner expiry in accelerate_and_move_object (0x4129A0, spc.c).</remarks>
    public void OnAfterburnerExpired()
    {
        if (AfterburnerSfxActive)
        {
            FlushSoundEffectsAndLog();
            AfterburnerSfxActive = false;
        }
    }

    /// <summary>
    /// Damage alarm while shields are collapsed: plays sound 32 when no handle is held (always,
    /// as the handle is never assigned) or every 10th frame. When the condition ends and a
    /// handle is held, the effects are flushed and true is returned (the caller then clears the
    /// cockpit warning light).
    /// </summary>
    /// <remarks>C: damage-alarm part of update_lights (0x4145B0, cockpt.c).</remarks>
    public bool ServiceDamageAlarm(bool alarmCondition, short spaceFrame)
    {
        if (alarmCondition)
        {
            if (DamageAlarmSfxHandle == 0 || spaceFrame % 10 == 0)
                PlaySfx(SoundEffectNumber.DamageAlarm, -1, 0);
            return false;
        }
        return ReleaseDamageAlarm();
    }

    /// <summary>Flushes the damage alarm if a handle is held; returns true when it did.</summary>
    /// <remarks>C: the nDamageAlarmSfxHandle release in update_lights (cockpt.c) and house_keep (0x427D40, main.c).</remarks>
    public bool ReleaseDamageAlarm()
    {
        if (DamageAlarmSfxHandle == 0)
            return false;
        FlushSoundEffectsAndLog();
        DamageAlarmSfxHandle = 0;
        return true;
    }
}
