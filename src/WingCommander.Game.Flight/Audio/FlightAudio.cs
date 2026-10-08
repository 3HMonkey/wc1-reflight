using WingCommander.Audio.Director;
using WingCommander.Core.Numerics;
using WingCommander.Simulation;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight.Audio;

/// <summary>
/// The flight's view of the audio layer: the music director and the sound-effect manager, plus
/// the two game-state interfaces they query (<see cref="IFlightMusicState"/>,
/// <see cref="IFlightSoundWorld"/>) implemented over the simulation. Without host audio
/// (<c>Wc1Game.Audio == null</c>, e.g. headless tests) a private director and manager over
/// silent backends run the same logic, so the shared random sequence (the music director draws
/// in <c>new_space_music_changes</c>) and every game-visible audio state are identical with and
/// without sound.
/// </summary>
/// <remarks>C: music.c spacetrack/changetrack/servicetrack/new_space_music_changes and
/// PlaySfxWaveFileByNumber, with the C globals they read (aeShipMissionType, nYourWingman,
/// aeShipSide, acShipRating, nShipMissionIndices, aShipPosition, asObjectDistance, ...).</remarks>
internal sealed class FlightAudio : IFlightMusicState, IFlightSoundWorld
{
    private readonly Wc1Game _game;
    private readonly Func<SpaceSimulation> _simulation;
    private MusicDirector? _silentMusic;
    private SoundEffectManager? _silentSfx;

    public FlightAudio(Wc1Game game, Func<SpaceSimulation> simulation)
    {
        _game = game;
        _simulation = simulation;
    }

    private SpaceSimulation Sim => _simulation();

    /// <summary>The music director (the game's, or a silent stand-in without host audio).</summary>
    public MusicDirector Music
    {
        get
        {
            if (_game.Audio is { } audio)
                return audio.Music;
            return _silentMusic ??= new MusicDirector(SilentMusicBackend.Instance, _game.Volumes, _game.Random)
            {
                MusicPlaybackMode = _game.Options.MusicPlaybackMode,
            };
        }
    }

    /// <summary>The game-wide sound-effect manager (or a silent stand-in without host audio).</summary>
    public SoundEffectManager Sfx
    {
        get
        {
            if (_game.Audio is { } audio)
                return audio.Sfx;
            return _silentSfx ??= new SoundEffectManager(SilentSoundEffectBackend.Instance, this);
        }
    }

    /// <summary>Makes this world the positional sound world of the game's audio (while flying).</summary>
    public void AttachSoundWorld()
    {
        if (_game.Audio is { } audio)
            audio.SoundWorld.Current = this;
    }

    /// <summary>Detaches the flight world from the game's audio.</summary>
    public void DetachSoundWorld()
    {
        if (_game.Audio is { } audio && ReferenceEquals(audio.SoundWorld.Current, this))
            audio.SoundWorld.Current = null;
    }

    /// <remarks>C: PlaySfxWaveFileByNumber(number, source, 0).</remarks>
    public void PlaySfx(int number, int source = -1) => Sfx.PlaySfx(number, source, 0);

    /// <remarks>C: spacetrack(track, mode, enabled).</remarks>
    public void SpaceTrack(int track, int mode, short enabled) => Music.SpaceTrack(track, mode, enabled);

    /// <remarks>C: changetrack().</remarks>
    public int ChangeTrack() => Music.ChangeTrack(this);

    // ------------------------------------------------------------------ IFlightMusicState

    public bool TrainingSimulatorActive => Sim.TrainSimActive;

    public int PlayerMissionType => (int)Sim.Ships[ObjectSlots.Player].MissionType;

    /// <remarks>C: <c>aMissionObjectives[cCurrentObjective].type == OBJECTIVE_HOME_BASE</c>; an objective
    /// index of -1 read before the table in the original and counts as "not home base" here.</remarks>
    public bool CurrentObjectiveIsHomeBase
    {
        get
        {
            var sim = Sim;
            int objective = sim.CurrentObjective;
            return (uint)objective < (uint)sim.MissionObjectives.Length &&
                sim.MissionObjectives[objective].Type == (int)ShipObjective.HomeBase;
        }
    }

    public int YourWingman => Sim.YourWingman;

    public int PlayerMissionShip => Sim.Ships[ObjectSlots.Player].MissionShip;

    public bool Triumph() => Sim.Triumph(ObjectSlots.Player);

    public bool MissileOnTail() => Sim.MissileOnTail(ObjectSlots.Player);

    public bool AnyEnemyOnTail() => Sim.AnyEnemyTail(ObjectSlots.Player);

    public bool IsShipTailingPlayerTarget() => Sim.IsShipTailingPlayerTarget(ObjectSlots.Player);

    public int CalculateDamageLevel() => Sim.CalculateDamageLevel();

    public bool ReportKilrathiRout(int mode) => Sim.ReportKilrathiRout(mode);

    public int GetShipSide(int obj) => IsShip(obj) ? (int)Sim.Ships[obj].Side : (int)Side.Neutral;

    public int GetShipRating(int obj) => IsShip(obj) ? Sim.Ships[obj].Rating : -1;

    public int GetShipMissionIndex(int obj) => IsShip(obj) ? Sim.Ships[obj].MissionIndex : -1;

    private static bool IsShip(int obj) => (uint)obj < ObjectSlots.ShipSlotCount;

    // ------------------------------------------------------------------ IFlightSoundWorld

    public int GetObjectClass(int obj) => IsSlot(obj) ? (int)Sim.Objects[obj].Class : 0;

    public short GetObjectDistance(int obj) => IsSlot(obj) ? Sim.Objects[obj].Distance : short.MaxValue;

    public short GetPreviousObjectDistance(int obj) => IsSlot(obj) ? Sim.Objects[obj].PreviousDistance : short.MaxValue;

    public short GetObjectScreenX(int obj) => IsSlot(obj) ? Sim.Objects[obj].ScreenX : MusicDirector.OffScreen;

    /// <remarks>C: the passing-ship test of servicetrack (0x42ECB0, music.c).</remarks>
    public int ComputePassingShipDot(int obj)
    {
        if (!IsSlot(obj))
            return 0;
        var sim = Sim;
        ref readonly var o = ref sim.Objects[obj];
        ref readonly var eye = ref sim.Objects[ObjectSlots.Eye];
        var travel = VectorMath.Scale(o.Velocity, 0x1400);
        var future = VectorMath.Add(o.Position, travel);
        return VectorMath.Dot(VectorMath.Delta(eye.Position, future), VectorMath.Delta(eye.Position, o.Position));
    }

    /// <remarks>C: the geometry part of SdlPlayGameSoundEffect (sdl/music.c).</remarks>
    public SoundSourceGeometry GetSoundSourceGeometry(int sourceObject)
    {
        if (!IsSlot(sourceObject))
            return default;
        var sim = Sim;
        ref readonly var eye = ref sim.Objects[ObjectSlots.Eye];
        FixedVector delta = VectorMath.Delta(eye.Position, sim.Objects[sourceObject].Position);
        int magnitude = delta.Magnitude();
        VectorMath.Normalize(ref delta);
        return new SoundSourceGeometry(magnitude, VectorMath.Dot(delta, eye.Right));
    }

    private static bool IsSlot(int obj) => (uint)obj < ObjectSlots.Count;

    /// <summary>Music backend without output: nothing plays, every track counts as finished.</summary>
    private sealed class SilentMusicBackend : IMusicBackend
    {
        public static readonly SilentMusicBackend Instance = new();

        public int ActiveTrack => -1;

        public bool IsActiveTrackFinished => true;

        public int SequencePosition => -1;

        public void Update(TimeSpan now)
        {
        }

        public bool TryStartTrack(int track, TimeSpan now) => true;

        public void StopTrack()
        {
        }

        public void PreloadTrack(int track)
        {
        }

        public void ApplyVolumeSettings(int musicVolumeSetting, int soundVolumeSetting)
        {
        }
    }

    /// <summary>Sound-effect backend without output; accepts every valid effect like the DOS engine.</summary>
    private sealed class SilentSoundEffectBackend : ISoundEffectBackend
    {
        public static readonly SilentSoundEffectBackend Instance = new();

        public bool Play(int soundNumber, int volume, int pan, int tag, int priority) =>
            soundNumber is >= 1 and <= SoundEffectNumber.Count;

        public void StopAll()
        {
        }
    }
}
