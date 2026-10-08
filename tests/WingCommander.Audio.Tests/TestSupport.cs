using System.Buffers.Binary;
using WingCommander.Audio.Director;
using WingCommander.Audio.OriginFx;
using WingCommander.Core.Resources;
using WingCommander.Tests;

namespace WingCommander.Audio.Tests;

/// <summary>64-bit FNV-1a, the hash the C++ reference harness prints for its PCM.</summary>
internal sealed class Fnv1a
{
    private ulong _hash = 1469598103934665603UL;

    public ulong Value => _hash;

    public void Add(ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            _hash ^= b;
            _hash *= 1099511628211UL;
        }
    }

    public void Add(int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        Add(bytes);
    }

    public void Add(ReadOnlySpan<short> samples)
    {
        Span<byte> bytes = stackalloc byte[2];
        foreach (short s in samples)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes, s);
            Add(bytes);
        }
    }
}

/// <summary>Cached game data for the data-driven tests.</summary>
internal static class AudioTestData
{
    private static readonly Lazy<OriginFxTimbreBank> LazyBank = new(() => OriginFxTimbreBank.Load(GameData.Require()));
    private static readonly Lazy<PacketFile> LazyMusic = new(() => GameData.Require().OpenPacket(OriginFxSequence.FileName));

    public static OriginFxTimbreBank Bank => LazyBank.Value;

    public static PacketFile Music => LazyMusic.Value;

    public static OriginFxSequence Sequence(int section) => OriginFxSequence.Parse(Music.GetSection(section).Span);

    public static IEnumerable<object[]> AllSections()
    {
        for (int i = 0; i < MusicTrack.Count; i++)
            yield return [i];
    }
}

/// <summary>Records music backend calls; the test drives the "audio" side by hand.</summary>
internal sealed class FakeMusicBackend : IMusicBackend
{
    public readonly List<int> Started = [];
    public readonly HashSet<int> FailingTracks = [];

    public int ActiveTrack { get; set; } = -1;

    public bool IsActiveTrackFinished { get; set; }

    public int Position { get; set; }

    public int SequencePosition => ActiveTrack < 0 ? -1 : Position;

    public int StopCount { get; private set; }

    public int MusicVolumeSetting { get; private set; } = -1;

    public int SoundVolumeSetting { get; private set; } = -1;

    public readonly List<int> Preloaded = [];

    public TimeSpan LastUpdate { get; private set; }

    public void Update(TimeSpan now) => LastUpdate = now;

    public bool TryStartTrack(int track, TimeSpan now)
    {
        if (FailingTracks.Contains(track))
            return false;
        ActiveTrack = track;
        IsActiveTrackFinished = false;
        Position = 0;
        Started.Add(track);
        return true;
    }

    public void PreloadTrack(int track) => Preloaded.Add(track);

    public void StopTrack()
    {
        ActiveTrack = -1;
        StopCount++;
    }

    public void ApplyVolumeSettings(int musicVolumeSetting, int soundVolumeSetting)
    {
        MusicVolumeSetting = musicVolumeSetting;
        SoundVolumeSetting = soundVolumeSetting;
    }
}

/// <summary>Records sound-effect backend calls.</summary>
internal sealed class FakeSoundBackend : ISoundEffectBackend
{
    public readonly List<(int Number, int Volume, int Pan, int Tag, int Priority)> Played = [];

    public bool Accept { get; set; } = true;

    public int StopAllCount { get; private set; }

    public bool Play(int soundNumber, int volume, int pan, int tag, int priority)
    {
        if (!Accept)
            return false;
        Played.Add((soundNumber, volume, pan, tag, priority));
        return true;
    }

    public void StopAll() => StopAllCount++;
}

/// <summary>Settable flight state; counts the query calls.</summary>
internal sealed class FakeFlightState : IFlightMusicState
{
    public readonly Dictionary<int, int> Sides = [];
    public readonly Dictionary<int, int> Ratings = [];
    public readonly Dictionary<int, int> MissionIndices = [];

    public bool TrainingSimulatorActive { get; set; }

    public int PlayerMissionType { get; set; }

    public bool CurrentObjectiveIsHomeBase { get; set; }

    public int YourWingman { get; set; } = -1;

    public int PlayerMissionShip { get; set; }

    public bool TriumphResult { get; set; }

    public bool MissileOnTailResult { get; set; }

    public bool AnyEnemyOnTailResult { get; set; }

    public bool TailingTargetResult { get; set; }

    public int DamageLevel { get; set; }

    public bool EnemiesNear { get; set; }

    public bool EnemiesPresent { get; set; }

    public int QueryCount { get; private set; }

    public bool Triumph() => Count(TriumphResult);

    public bool MissileOnTail() => Count(MissileOnTailResult);

    public bool AnyEnemyOnTail() => Count(AnyEnemyOnTailResult);

    public bool IsShipTailingPlayerTarget() => Count(TailingTargetResult);

    public int CalculateDamageLevel()
    {
        QueryCount++;
        return DamageLevel;
    }

    public bool ReportKilrathiRout(int mode) => Count(mode == 1 ? EnemiesNear : EnemiesPresent);

    public int GetShipSide(int obj) => Sides.GetValueOrDefault(obj, MusicDirector.SideImperial);

    public int GetShipRating(int obj) => Ratings.GetValueOrDefault(obj, -1);

    public int GetShipMissionIndex(int obj) => MissionIndices.GetValueOrDefault(obj, -1);

    private bool Count(bool value)
    {
        QueryCount++;
        return value;
    }
}

/// <summary>Settable space-object state.</summary>
internal sealed class FakeWorld : IFlightSoundWorld
{
    public readonly int[] Classes = new int[SoundEffectManager.SpaceObjectCount];
    public readonly short[] Distances = new short[SoundEffectManager.SpaceObjectCount];
    public readonly short[] PreviousDistances = new short[SoundEffectManager.SpaceObjectCount];
    public readonly short[] ScreenX = new short[SoundEffectManager.SpaceObjectCount];
    public readonly int[] PassingDots = new int[SoundEffectManager.SpaceObjectCount];
    public readonly SoundSourceGeometry[] Geometry = new SoundSourceGeometry[SoundEffectManager.SpaceObjectCount];

    public FakeWorld()
    {
        Array.Fill(Distances, (short)30000);
        Array.Fill(PreviousDistances, (short)30000);
        Array.Fill(ScreenX, MusicDirector.OffScreen);
        Array.Fill(PassingDots, 0x7fff);
    }

    public int GetObjectClass(int obj) => Classes[obj];

    public short GetObjectDistance(int obj) => Distances[obj];

    public short GetPreviousObjectDistance(int obj) => PreviousDistances[obj];

    public short GetObjectScreenX(int obj) => ScreenX[obj];

    public int ComputePassingShipDot(int obj) => PassingDots[obj];

    public SoundSourceGeometry GetSoundSourceGeometry(int sourceObject) => Geometry[sourceObject];
}
