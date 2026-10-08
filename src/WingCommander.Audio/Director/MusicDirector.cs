using WingCommander.Core.Numerics;

namespace WingCommander.Audio.Director;

/// <summary>
/// The game-side music state machine: which track is requested (<see cref="CurrentMusicTrack"/>),
/// whether the current one finished (<see cref="MusicTrackComplete"/>), the music mode from
/// the command line, in-flight track selection (<c>gametrack</c>/<c>changetrack</c>/kill cues)
/// and the per-frame proximity sounds of <c>servicetrack</c>. <see cref="Service"/> is the DOS
/// player glue that turns the requested track into playback (call it from every event pump
/// with the virtual time). A literal transcription of the C state machine; game thread only;
/// every member is non-blocking (ADR-009): the original busy wait for the music end is
/// split into <see cref="BeginWaitForEndOfMusic"/> and <see cref="ContinueWaitForEndOfMusic"/>.
/// </summary>
/// <remarks>C: music.c (spacetrack, ProcessMusicScriptCommand, gametrack, servicetrack, ...),
/// logic.c (init_inflight_music, free_inflight_music) and sdl/music.c (SdlServiceOriginFxMusic).</remarks>
public sealed class MusicDirector
{
    /// <summary>Music mode set at startup when no option is given.</summary>
    public const short DefaultPlaybackMode = 4;

    /// <summary>C enum ObjectClass value of asteroids.</summary>
    public const int ObjectClassAsteroid = 9;

    /// <summary>C enum ObjectClass value of fighters.</summary>
    public const int ObjectClassShip = 12;

    /// <summary>C enum ObjectClass value of capital ships.</summary>
    public const int ObjectClassCapitalShip = 13;

    /// <summary>C enum MissionType values used by the music.</summary>
    public const int MissionTypePatrol = 0;
    public const int MissionTypeEscort = 1;
    public const int MissionTypeStrike = 2;
    public const int MissionTypeDefend = 3;
    public const int MissionTypeRendezvous = 9;

    /// <summary>C enum Side values.</summary>
    public const int SideImperial = 0;
    public const int SideKilrathi = 1;

    /// <summary>Returned by <see cref="BeginStartupIntroMusic"/> when music is disabled.</summary>
    public const int IntroMusicDisabled = int.MinValue;

    /// <summary>Screen X of an object that is not on screen.</summary>
    public const short OffScreen = unchecked((short)0x8001);

    private readonly IMusicBackend _backend;
    private readonly AudioVolumeSettings _volumes;
    private readonly CRandom _random;

    /// <param name="backend">Player (DOS: <c>Dos.DosAudioBackend</c>).</param>
    /// <param name="volumes">Shared volume settings (read by <see cref="Service"/>).</param>
    /// <param name="random">The game's shared C rand() (consumed by kill cues).</param>
    public MusicDirector(IMusicBackend backend, AudioVolumeSettings volumes, CRandom random)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(volumes);
        ArgumentNullException.ThrowIfNull(random);
        _backend = backend;
        _volumes = volumes;
        _random = random;
    }

    /// <summary>Music mode: R = 1, A&lt;n&gt; = 2, P = 3 (no music), otherwise 4. Music plays iff not 0 or 3.</summary>
    /// <remarks>C: nMusicPlaybackMode (0x46a9f8).</remarks>
    public short MusicPlaybackMode { get; set; } = DefaultPlaybackMode;

    /// <summary>When set, every music request is ignored (never set in the reconstruction).</summary>
    /// <remarks>C: bMusicCommandSuppressed (0x46a9fc).</remarks>
    public bool MusicCommandSuppressed { get; set; }

    /// <summary>1 when no track plays or the current one ended.</summary>
    /// <remarks>C: nMusicTrackComplete (0x46aa04).</remarks>
    public short MusicTrackComplete { get; set; } = 1;

    /// <summary>The requested track, -1 = none.</summary>
    /// <remarks>C: nCurrentMusicTrack (0x46aa14).</remarks>
    public int CurrentMusicTrack { get; set; } = -1;

    /// <summary>Kilrathi Saga stream set of the last request (0 preflite, 1 posflite, 2 mission, -1 none).</summary>
    /// <remarks>C: nMusicStreamSet (0x46aa18); also consulted by gametrack in the simulator.</remarks>
    public int MusicStreamSet { get; private set; } = -1;

    /// <summary>Scenes wait for the music end only when set.</summary>
    /// <remarks>C: nWaitForMusicEnabled (0x46aa30).</remarks>
    public bool WaitForMusicEnabled { get; set; } = true;

    /// <summary>Set while in space flight (gate of <see cref="GameTrack"/>).</summary>
    /// <remarks>C: nInFlightMusicActive (0x46aa40).</remarks>
    public bool InFlightMusicActive { get; set; }

    /// <summary>In flight: combat music family selected.</summary>
    /// <remarks>C: nCombatMusicActive (0x46aa3c).</remarks>
    public bool CombatMusicActive { get; set; }

    /// <summary>Set by <see cref="InitInflightMusic"/>, cleared at the first combat selection.</summary>
    /// <remarks>C: nInitialFlightMusicPending (0x46aa38).</remarks>
    public bool InitialFlightMusicPending { get; set; } = true;

    /// <summary>Object whose "passing" sound was played last (-1 = none).</summary>
    /// <remarks>C: nPassingShipSoundObject (0x46aa48).</remarks>
    public short PassingShipSoundObject { get; set; } = -1;

    /// <remarks>C: nPassingShipSoundCountdown (0x46aa4c).</remarks>
    public short PassingShipSoundCountdown { get; set; }

    /// <summary>Frame before which no further passing sound is played.</summary>
    /// <remarks>C: nPassingShipSoundCooldown (0x5a68e8).</remarks>
    public int PassingShipSoundCooldown { get; set; }

    /// <summary>True when the music mode allows music.</summary>
    public bool IsMusicEnabled => MusicPlaybackMode != 0 && MusicPlaybackMode != 3;

    /// <summary>
    /// Sequence cue of the playing track on the game clock as of the last <see cref="Service"/>
    /// (the startup intro synchronises to it), -1 when no track is loaded.
    /// </summary>
    /// <remarks>C: SdlGetOriginFxMusicSequencePosition (sdl/music.c).</remarks>
    public int SequencePosition => _backend.SequencePosition;

    /// <summary>Requests a track (mode: 0 queue_start, 1 queue_break, 2 queue_switch, 3 queue_interrupt, 4 queue_stop).</summary>
    /// <remarks>C: spacetrack (0x42E880, music.c).</remarks>
    public uint SpaceTrack(int track, int mode, short enabled)
    {
        if (IsMusicEnabled)
            ProcessMusicScriptCommand(track, mode, enabled);
        return 1;
    }

    /// <summary>
    /// Handles a music request. With DOS data only <see cref="CurrentMusicTrack"/> matters; the
    /// Saga streamer routing (intensity/trigger) is not ported.
    /// </summary>
    /// <remarks>C: ProcessMusicScriptCommand (0x42E6F0, music.c).</remarks>
    public void ProcessMusicScriptCommand(int track, int command, short enabled)
    {
        if (track == -1 || MusicCommandSuppressed)
            return;
        if (command == 4)
        {
            StopMusic(enabled);
            CurrentMusicTrack = -1;
            return;
        }

        // "skipping for QA": these tracks are not re-requested while current
        if ((CurrentMusicTrack == 25 && track == 25) ||
            (CurrentMusicTrack == 38 && track == 38) ||
            (CurrentMusicTrack == 39 && track == 39) ||
            (CurrentMusicTrack == 40 && track == 40))
            return;

        CurrentMusicTrack = track;
        SelectFlightMusicTrack(track);
    }

    /// <remarks>C: StopMusic (0x42E350, music.c); the streamer stop is Saga only.</remarks>
    public void StopMusic(short unused = 0)
    {
        _ = unused;
        CurrentMusicTrack = -1;
    }

    /// <remarks>C: StopMusicUnlessSuppressed (0x42E8B0, music.c).</remarks>
    public void StopMusicUnlessSuppressed()
    {
        if (IsMusicEnabled)
            StopMusic(0);
    }

    /// <summary>1 when music is enabled and the current track has finished.</summary>
    /// <remarks>C: GetMusicMode (0x42E8D0, music.c).</remarks>
    public ushort GetMusicMode() => (ushort)(IsMusicEnabled && MusicTrackComplete != 0 ? 1 : 0);

    /// <summary>
    /// First half of <c>wait_for_end_of_music</c>: returns true when the caller has to keep
    /// calling <see cref="ContinueWaitForEndOfMusic"/> once per tick (after pumping events),
    /// false when there is nothing to wait for (music disabled, waiting disabled, or the track
    /// already ended). Unused by the reconstructed game.
    /// </summary>
    /// <remarks>C: wait_for_end_of_music (0x42E900, music.c), loop entry.</remarks>
    public bool BeginWaitForEndOfMusic()
    {
        if (!IsMusicEnabled)
            return false;
        if (!WaitForMusicEnabled)
        {
            StopMusic(0);
            return false;
        }
        SetMusBreakpt(0, 0);
        return MusicTrackComplete == 0;
    }

    /// <summary>
    /// One iteration of the wait: true when finished, i.e. the track completed or
    /// <paramref name="escapeRequested"/> (bEscapePressed or CheckEscaped) stopped the music.
    /// </summary>
    /// <remarks>C: wait_for_end_of_music (0x42E900, music.c), loop body.</remarks>
    public bool ContinueWaitForEndOfMusic(bool escapeRequested)
    {
        if (MusicTrackComplete != 0)
            return true;
        if (!escapeRequested)
            return false;
        StopMusic(0);
        return true;
    }

    /// <summary>Optional cache warm-up before a scene requests a track.</summary>
    /// <remarks>C: PreloadMusicTrackHook (0x424CE0, logic.c; a stub, the DOS driver preloaded the section).</remarks>
    public void PreloadMusicTrackHook(int track) => _backend.PreloadTrack(track);

    /// <summary>Counterpart of <see cref="PreloadMusicTrackHook"/>; parsed sections stay cached.</summary>
    /// <remarks>C: ReleaseMusicTrackHook (0x424CF0, logic.c; a stub).</remarks>
    public void ReleaseMusicTrackHook(int track) => _ = track;

    /// <remarks>C: EnableMusicForScene (0x42EEE0, music.c).</remarks>
    public void EnableMusicForScene()
    {
        WaitForMusicEnabled = true;
        SetMusicOn(1);
    }

    /// <summary>Driver stub in this build (debug print only).</summary>
    /// <remarks>C: SetMusicOn (0x42E330, music.c).</remarks>
    public void SetMusicOn(short enabled) => _ = enabled;

    /// <summary>Driver stub in this build ("music breakpoint", see open questions).</summary>
    /// <remarks>C: SetMusBreakpt (0x42E380, music.c).</remarks>
    public void SetMusBreakpt(int first, int second)
    {
        _ = first;
        _ = second;
    }

    /// <summary>Driver stub in this build.</summary>
    /// <remarks>C: FadeMusic (0x42E320, music.c).</remarks>
    public void FadeMusic()
    {
    }

    /// <summary>
    /// Kilrathi Saga streamer volume. Ignored with DOS data: the music gain follows
    /// <see cref="AudioVolumeSettings.MusicVolume"/> in <see cref="Service"/>.
    /// </summary>
    /// <remarks>C: SetMusicStreamVolume (0x442590, gr.c).</remarks>
    public void SetMusicStreamVolume(ushort level) => _ = level;

    /// <summary>Entering space flight.</summary>
    /// <remarks>C: init_inflight_music (0x424C60, logic.c).</remarks>
    public void InitInflightMusic()
    {
        CombatMusicActive = false;
        InFlightMusicActive = true;
        InitialFlightMusicPending = true;
    }

    /// <summary>Leaving space flight. (The legacy sound-slot bookkeeping is never armed and is not ported.)</summary>
    /// <remarks>C: free_inflight_music (0x424C80, logic.c).</remarks>
    public void FreeInflightMusic()
    {
        StopMusicUnlessSuppressed();
        InFlightMusicActive = false;
    }

    /// <summary>Music cue when something is destroyed in flight.</summary>
    /// <remarks>C: new_space_music_changes (0x42E9E0, music.c).</remarks>
    public void NewSpaceMusicChanges(IFlightMusicState state, short attacker, short victim)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!InFlightMusicActive || state.TrainingSimulatorActive)
            return;
        int side = state.GetShipSide(victim);
        if (side == SideKilrathi)
        {
            if (!state.ReportKilrathiRout(1))
            {
                SpaceTrack(MusicTrack.OverallVictory, 1, 0);
                return;
            }
            if (attacker == 0)
            {
                if (state.GetShipRating(victim) == -1 && _random.InRange(0, 3) != 0)
                {
                    SpaceTrack(MusicTrack.TargetHit, 3, 0);
                    return;
                }
                SpaceTrack(MusicTrack.EnemyAceKilled, 3, 0);
            }
            return;
        }
        if (state.YourWingman == victim)
        {
            SpaceTrack(MusicTrack.WingmanHit, 3, 0);
            return;
        }
        if (side == SideImperial)
        {
            int missionType = state.PlayerMissionType;
            if ((missionType == MissionTypeDefend || missionType == MissionTypeEscort) &&
                state.GetShipMissionIndex(victim) == state.PlayerMissionShip)
            {
                SpaceTrack(MusicTrack.OverallDefeat, 3, 0);
                return;
            }
            SpaceTrack(MusicTrack.AllyKilled, 3, 0);
        }
    }

    /// <summary>Cruise track for the current mission and objective.</summary>
    /// <remarks>C: changetrack (0x42EAD0, music.c).</remarks>
    public int ChangeTrack(IFlightMusicState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        int track = state.PlayerMissionType switch
        {
            MissionTypeEscort => MusicTrack.EscortMission,
            MissionTypeStrike => MusicTrack.StrikeMission,
            MissionTypeDefend or MissionTypeRendezvous => MusicTrack.DefendingTheClaw,
            _ => MusicTrack.FlyingToDogfight,
        };
        if (state.CurrentObjectiveIsHomeBase)
        {
            if (state.Triumph())
                return state.PlayerMissionType == MissionTypePatrol ? MusicTrack.ReturningNormal : MusicTrack.ReturningTriumphant;
            track = MusicTrack.ReturningDefeated;
        }
        return track;
    }

    /// <summary>
    /// In-flight track selection, every 16th frame or when the track ended (which makes
    /// flight music loop by restarting).
    /// </summary>
    /// <remarks>C: gametrack (0x42EB60, music.c).</remarks>
    public void GameTrack(IFlightMusicState state, short spaceFrame)
    {
        ArgumentNullException.ThrowIfNull(state);
        int track = -1;
        if (!InFlightMusicActive)
            return;
        if (state.TrainingSimulatorActive)
        {
            if (MusicStreamSet != 0 || CurrentMusicTrack != MusicTrack.ArcadeTheme)
                SpaceTrack(MusicTrack.ArcadeTheme, 1, 0);
            return;
        }
        if (CombatMusicActive)
        {
            if ((spaceFrame & 0xf) == 0 || MusicTrackComplete != 0)
            {
                if (InitialFlightMusicPending)
                    InitialFlightMusicPending = false;
                if (state.MissileOnTail())
                {
                    track = MusicTrack.MissileTrackingYou;
                }
                else if (state.AnyEnemyOnTail())
                {
                    track = MusicTrack.BeingTailed;
                }
                else if (state.IsShipTailingPlayerTarget())
                {
                    track = MusicTrack.TailingAnEnemy;
                }
                else
                {
                    short damage = (short)state.CalculateDamageLevel();
                    if (damage < 2)
                        track = damage == 1 ? MusicTrack.IntenseCombat : MusicTrack.RegularCombat;
                    else
                        track = MusicTrack.SeverelyDamaged;
                }
                if (!state.ReportKilrathiRout(1))
                    CombatMusicActive = false;
            }
        }
        else if ((spaceFrame & 0xf) == 0 || MusicTrackComplete != 0)
        {
            track = ChangeTrack(state);
            if (state.ReportKilrathiRout(2))
                CombatMusicActive = true;
        }
        SpaceTrack(track, 1, 0);
    }

    /// <summary>
    /// Once per simulation frame: <see cref="GameTrack"/>, then the asteroid (6) and passing
    /// ship (2) proximity sounds.
    /// </summary>
    /// <remarks>C: servicetrack (0x42ECB0, music.c).</remarks>
    public void ServiceTrack(IFlightMusicState state, IFlightSoundWorld world, SoundEffectManager soundEffects, short spaceFrame)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(soundEffects);
        GameTrack(state, spaceFrame);
        if (!soundEffects.FlightSoundEffectsEnabled)
            return;
        for (short obj = 0; obj < SoundEffectManager.SpaceObjectCount; obj++)
        {
            int objectClass = world.GetObjectClass(obj);
            if (obj == PassingShipSoundObject)
            {
                // original bug: "class != ship || class != capital ship" is always true, so the
                // tracked object is released on every pass and only the cooldown limits the sound
                if (objectClass != ObjectClassShip || objectClass != ObjectClassCapitalShip)
                    PassingShipSoundObject = -1;
            }
            if (objectClass == ObjectClassAsteroid)
            {
                if (world.GetObjectDistance(obj) == 0 &&
                    (ushort)world.GetPreviousObjectDistance(obj) < 50 &&
                    !soundEffects.IsSourceActive(obj))
                    soundEffects.PlaySfx(SoundEffectNumber.AsteroidPassing, obj, 0);
            }
            else if (objectClass >= ObjectClassShip && objectClass <= ObjectClassCapitalShip &&
                     world.GetObjectScreenX(obj) != OffScreen &&
                     (ushort)world.GetObjectDistance(obj) < 0x55a)
            {
                if (PassingShipSoundObject == -1)
                {
                    if (world.ComputePassingShipDot(obj) < 0xdd)
                    {
                        PassingShipSoundCountdown = 10;
                        PassingShipSoundObject = obj;
                        if (PassingShipSoundCooldown < spaceFrame)
                        {
                            PassingShipSoundCooldown = spaceFrame + 6;
                            soundEffects.PlaySfx(SoundEffectNumber.ShipPassing, obj, 0);
                        }
                    }
                }
                else if (obj == PassingShipSoundObject)
                {
                    PassingShipSoundCountdown--;
                    if (PassingShipSoundCountdown == 0)
                        PassingShipSoundObject = -1;
                }
            }
        }
    }

    /// <summary>
    /// DOS music service, called from every event pump with the current virtual time:
    /// refreshes the gains, notices the end of the current track (sets
    /// <see cref="MusicTrackComplete"/> and clears <see cref="CurrentMusicTrack"/> so in-flight
    /// music restarts), and starts a newly requested track at <paramref name="now"/>.
    /// </summary>
    /// <remarks>C: SdlServiceOriginFxMusic (sdl/music.c).</remarks>
    public void Service(TimeSpan now)
    {
        _backend.ApplyVolumeSettings(_volumes.MusicVolume, _volumes.SfxVolume);
        _backend.Update(now);
        if (_backend.ActiveTrack >= 0 && _backend.IsActiveTrackFinished)
        {
            int finishedTrack = _backend.ActiveTrack;
            _backend.StopTrack();
            MusicTrackComplete = 1;
            if (CurrentMusicTrack == finishedTrack)
                CurrentMusicTrack = -1;
        }
        int desiredTrack = CurrentMusicTrack;
        if (desiredTrack == _backend.ActiveTrack)
            return;
        if (desiredTrack < 0)
        {
            _backend.StopTrack();
            MusicTrackComplete = 1;
            return;
        }
        if (!_backend.TryStartTrack(desiredTrack, now))
        {
            CurrentMusicTrack = -1;
            MusicTrackComplete = 1;
            return;
        }
        MusicTrackComplete = 0;
    }

    /// <summary>
    /// Starts the startup-intro music (track 19) at <paramref name="now"/> and returns the track
    /// to restore afterwards, or <see cref="IntroMusicDisabled"/> when music is disabled. Then
    /// poll <see cref="SequencePosition"/> each tick (after <see cref="Service"/>; cues 0..6,
    /// -1 = no music) to synchronise the animation.
    /// </summary>
    /// <remarks>C: music part of SdlPlayDosStartupIntro (sdl/dos_intro.c).</remarks>
    public int BeginStartupIntroMusic(TimeSpan now)
    {
        if (!IsMusicEnabled)
            return IntroMusicDisabled;
        int previousTrack = CurrentMusicTrack;
        CurrentMusicTrack = MusicTrack.StartupIntro;
        Service(now);
        return previousTrack;
    }

    /// <summary>Restores the music that was requested before <see cref="BeginStartupIntroMusic"/>.</summary>
    /// <remarks>C: end of SdlPlayDosStartupIntro (sdl/dos_intro.c).</remarks>
    public void EndStartupIntroMusic(int previousTrack, TimeSpan now)
    {
        if (previousTrack == IntroMusicDisabled)
            return;
        CurrentMusicTrack = previousTrack;
        Service(now);
    }

    /// <summary>Saga stream set of a track (-1: none or OriginFX intro).</summary>
    /// <remarks>C: the stream mapping of SelectFlightMusicTrack (0x42E3F0, music.c). Track 19 leaves the
    /// local uninitialised in the original; the port treats it as "no stream".</remarks>
    public static int GetStreamSet(int track) => track switch
    {
        (>= 0 and <= 18) or 27 or 31 or 32 => 2,
        (>= 20 and <= 26) or 30 or 35 => 0,
        28 or 29 or (>= 33 and <= 40 and not 35) => 1,
        _ => -1,
    };

    /// <summary>Saga streamer trigger tag of a track for the non-mission streams.</summary>
    /// <remarks>C: MapMusicTrackToStreamerCommand (0x42E520, music.c).</remarks>
    public static int MapMusicTrackToStreamerCommand(int track) => track switch
    {
        0 => 5,
        1 => 7,
        2 => 7,
        3 => 8,
        4 => 9,
        5 => 6,
        6 => 15,
        7 => 13,
        8 => 16,
        9 => 14,
        10 => 17,
        11 => 18,
        12 => 10,
        13 => 12,
        14 => 11,
        15 => 4,
        16 => 3,
        17 => 1,
        18 => 2,
        20 => 1,
        21 => 4,
        22 => 3,
        24 => 5,
        25 => 6,
        26 => 7,
        29 or 30 => 0,
        31 => 19,
        32 => 20,
        33 => 2,
        34 => 1,
        35 => 2,
        36 => 3,
        37 => 4,
        38 => 5,
        39 => 7,
        40 => 6,
        _ => -1,
    };

    /// <summary>Updates <see cref="MusicStreamSet"/> like the Saga stream selection (no streaming).</summary>
    /// <remarks>C: SelectFlightMusicTrack (0x42E3F0, music.c).</remarks>
    private void SelectFlightMusicTrack(int track)
    {
        int streamSet = GetStreamSet(track);
        if (streamSet == MusicStreamSet || streamSet == -1)
            return;
        MusicStreamSet = streamSet;
    }
}
