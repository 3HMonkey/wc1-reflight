using WingCommander.Audio.Director;
using WingCommander.Core.Numerics;

namespace WingCommander.Audio.Tests.Director;

public class MusicDirectorTests
{
    private readonly FakeMusicBackend _backend = new();
    private readonly AudioVolumeSettings _volumes = new();
    private readonly FakeFlightState _flight = new();
    private readonly FakeWorld _world = new();
    private readonly FakeSoundBackend _sounds = new();
    private TimeSpan _now = TimeSpan.Zero;

    private MusicDirector CreateDirector(uint seed = 1) => new(_backend, _volumes, new CRandom(seed));

    private void Tick(MusicDirector director, int milliseconds = 50)
    {
        _now += TimeSpan.FromMilliseconds(milliseconds);
        director.Service(_now);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Requests_AreIgnored_WhenMusicIsDisabled(short mode)
    {
        var director = CreateDirector();
        director.MusicPlaybackMode = mode;
        Assert.Equal(1u, director.SpaceTrack(MusicTrack.Barracks, 2, 1));
        Assert.Equal(-1, director.CurrentMusicTrack);
        Assert.False(director.IsMusicEnabled);
        Assert.Equal(0, director.GetMusicMode());
    }

    [Fact]
    public void Requests_SetTheCurrentTrack_AndServiceStartsIt()
    {
        var director = CreateDirector();
        director.SpaceTrack(MusicTrack.Barracks, 2, 1);
        Assert.Equal(MusicTrack.Barracks, director.CurrentMusicTrack);
        Assert.Equal(1, director.MusicTrackComplete);

        Tick(director);
        Assert.Equal([MusicTrack.Barracks], _backend.Started);
        Assert.Equal(0, director.MusicTrackComplete);
        Assert.Equal(0, director.GetMusicMode());

        // servicing again does not restart
        Tick(director);
        Assert.Single(_backend.Started);
    }

    [Fact]
    public void Service_PassesTheVolumeSettings()
    {
        var director = CreateDirector();
        _volumes.MusicVolume = 7;
        _volumes.SfxVolume = 13;
        Tick(director);
        Assert.Equal(7, _backend.MusicVolumeSetting);
        Assert.Equal(13, _backend.SoundVolumeSetting);
    }

    [Fact]
    public void TrackEnd_SetsComplete_AndClearsTheRequest()
    {
        var director = CreateDirector();
        director.SpaceTrack(MusicTrack.Funeral, 2, 1);
        Tick(director);
        _backend.IsActiveTrackFinished = true;
        Tick(director);
        Assert.Equal(1, director.MusicTrackComplete);
        Assert.Equal(-1, director.CurrentMusicTrack);
        Assert.Equal(-1, _backend.ActiveTrack);
        Assert.Equal(1, director.GetMusicMode());
    }

    [Fact]
    public void FinishedTrack_KeepsANewerRequest()
    {
        var director = CreateDirector();
        director.SpaceTrack(MusicTrack.Funeral, 2, 1);
        Tick(director);
        _backend.IsActiveTrackFinished = true;
        director.SpaceTrack(MusicTrack.Barracks, 2, 1);
        Tick(director);
        Assert.Equal(MusicTrack.Barracks, director.CurrentMusicTrack);
        Assert.Equal([MusicTrack.Funeral, MusicTrack.Barracks], _backend.Started);
        Assert.Equal(0, director.MusicTrackComplete);
    }

    [Fact]
    public void StartFailure_DropsTheRequest()
    {
        var director = CreateDirector();
        _backend.FailingTracks.Add(30);
        director.SpaceTrack(30, 2, 1);
        Tick(director);
        Assert.Equal(-1, director.CurrentMusicTrack);
        Assert.Equal(1, director.MusicTrackComplete);
    }

    [Fact]
    public void StopRequests_StopThePlayer()
    {
        var director = CreateDirector();
        director.SpaceTrack(MusicTrack.RecRoom, 2, 1);
        Tick(director);
        director.SpaceTrack(MusicTrack.RecRoom, 4, 0);
        Assert.Equal(-1, director.CurrentMusicTrack);
        Tick(director);
        Assert.Equal(-1, _backend.ActiveTrack);
        Assert.Equal(1, director.MusicTrackComplete);

        director.SpaceTrack(MusicTrack.RecRoom, 2, 1);
        director.StopMusicUnlessSuppressed();
        Assert.Equal(-1, director.CurrentMusicTrack);
    }

    [Fact]
    public void IgnoredRequests_MinusOneAndSuppressed()
    {
        var director = CreateDirector();
        director.SpaceTrack(-1, 2, 1);
        Assert.Equal(-1, director.CurrentMusicTrack);
        director.MusicCommandSuppressed = true;
        director.SpaceTrack(MusicTrack.Barracks, 2, 1);
        Assert.Equal(-1, director.CurrentMusicTrack);
    }

    [Fact]
    public void QaTracks_AreNotReRequested_WhileCurrent()
    {
        var director = CreateDirector();
        director.SpaceTrack(MusicTrack.BriefingMiddle, 2, 1);
        Assert.Equal(0, director.MusicStreamSet);
        director.SpaceTrack(MusicTrack.BriefingMiddle, 2, 1);
        Assert.Equal(MusicTrack.BriefingMiddle, director.CurrentMusicTrack);
        director.SpaceTrack(MusicTrack.MedalPurpleHeart, 2, 1);
        Assert.Equal(1, director.MusicStreamSet);
        Assert.Equal(MusicTrack.MedalPurpleHeart, director.CurrentMusicTrack);
    }

    [Fact]
    public void FlightMusic_LoopsByRestartingTheFinishedTrack()
    {
        var director = CreateDirector();
        director.InitInflightMusic();
        director.GameTrack(_flight, 0);
        Assert.Equal(MusicTrack.FlyingToDogfight, director.CurrentMusicTrack);
        Tick(director);
        Assert.Equal([MusicTrack.FlyingToDogfight], _backend.Started);

        _backend.IsActiveTrackFinished = true;
        Tick(director);
        Assert.Equal(-1, director.CurrentMusicTrack);
        director.GameTrack(_flight, 5);
        Assert.Equal(MusicTrack.FlyingToDogfight, director.CurrentMusicTrack);
        Tick(director);
        Assert.Equal([MusicTrack.FlyingToDogfight, MusicTrack.FlyingToDogfight], _backend.Started);
    }

    [Fact]
    public void GameTrack_ReEvaluatesEvery16thFrame()
    {
        var director = CreateDirector();
        director.InitInflightMusic();
        director.MusicTrackComplete = 0;
        director.GameTrack(_flight, 1);
        Assert.Equal(-1, director.CurrentMusicTrack);
        Assert.Equal(0, _flight.QueryCount);
        director.GameTrack(_flight, 32);
        Assert.Equal(MusicTrack.FlyingToDogfight, director.CurrentMusicTrack);

        director.FreeInflightMusic();
        Assert.False(director.InFlightMusicActive);
        Assert.Equal(-1, director.CurrentMusicTrack);
        director.GameTrack(_flight, 48);
        Assert.Equal(-1, director.CurrentMusicTrack);
    }

    [Theory]
    [InlineData(true, false, false, 0, MusicTrack.MissileTrackingYou)]
    [InlineData(false, true, true, 0, MusicTrack.BeingTailed)]
    [InlineData(false, false, true, 2, MusicTrack.TailingAnEnemy)]
    [InlineData(false, false, false, 0, MusicTrack.RegularCombat)]
    [InlineData(false, false, false, 1, MusicTrack.IntenseCombat)]
    [InlineData(false, false, false, 2, MusicTrack.SeverelyDamaged)]
    [InlineData(false, false, false, 5, MusicTrack.SeverelyDamaged)]
    public void CombatMusic_FollowsTheThreatOrder(bool missile, bool tailed, bool tailing, int damage, int expected)
    {
        var director = CreateDirector();
        director.InitInflightMusic();
        director.CombatMusicActive = true;
        _flight.MissileOnTailResult = missile;
        _flight.AnyEnemyOnTailResult = tailed;
        _flight.TailingTargetResult = tailing;
        _flight.DamageLevel = damage;
        _flight.EnemiesNear = true;
        director.GameTrack(_flight, 16);
        Assert.Equal(expected, director.CurrentMusicTrack);
        Assert.True(director.CombatMusicActive);
        Assert.False(director.InitialFlightMusicPending);
    }

    [Fact]
    public void CombatMusic_EndsWhenNoEnemyIsNear_AndStartsWhenEnemiesArrive()
    {
        var director = CreateDirector();
        director.InitInflightMusic();
        _flight.EnemiesPresent = true;
        director.GameTrack(_flight, 0);
        Assert.True(director.CombatMusicActive);
        Assert.Equal(MusicTrack.FlyingToDogfight, director.CurrentMusicTrack);

        _flight.EnemiesNear = false;
        director.GameTrack(_flight, 16);
        Assert.Equal(MusicTrack.RegularCombat, director.CurrentMusicTrack);
        Assert.False(director.CombatMusicActive);
    }

    [Fact]
    public void TrainingSimulator_KeepsRequestingTheArcadeTheme()
    {
        var director = CreateDirector();
        director.InitInflightMusic();
        _flight.TrainingSimulatorActive = true;
        director.GameTrack(_flight, 3);
        Assert.Equal(MusicTrack.ArcadeTheme, director.CurrentMusicTrack);
        Assert.Equal(0, director.MusicStreamSet);
        director.CurrentMusicTrack = -1;
        director.GameTrack(_flight, 4);
        Assert.Equal(MusicTrack.ArcadeTheme, director.CurrentMusicTrack);
    }

    [Theory]
    [InlineData(MusicDirector.MissionTypeEscort, false, false, MusicTrack.EscortMission)]
    [InlineData(MusicDirector.MissionTypeStrike, false, false, MusicTrack.StrikeMission)]
    [InlineData(MusicDirector.MissionTypeDefend, false, false, MusicTrack.DefendingTheClaw)]
    [InlineData(MusicDirector.MissionTypeRendezvous, false, false, MusicTrack.DefendingTheClaw)]
    [InlineData(MusicDirector.MissionTypePatrol, false, false, MusicTrack.FlyingToDogfight)]
    [InlineData(5, false, false, MusicTrack.FlyingToDogfight)]
    [InlineData(MusicDirector.MissionTypePatrol, true, true, MusicTrack.ReturningNormal)]
    [InlineData(MusicDirector.MissionTypeStrike, true, true, MusicTrack.ReturningTriumphant)]
    [InlineData(MusicDirector.MissionTypeStrike, true, false, MusicTrack.ReturningDefeated)]
    public void ChangeTrack_SelectsTheCruiseTrack(int missionType, bool homeBase, bool triumph, int expected)
    {
        _flight.PlayerMissionType = missionType;
        _flight.CurrentObjectiveIsHomeBase = homeBase;
        _flight.TriumphResult = triumph;
        Assert.Equal(expected, CreateDirector().ChangeTrack(_flight));
    }

    [Fact]
    public void KillCues_FollowTheVictim()
    {
        var director = CreateDirector();
        director.InitInflightMusic();
        _flight.Sides[5] = MusicDirector.SideKilrathi;
        _flight.Ratings[5] = 9;
        _flight.EnemiesNear = true;

        director.NewSpaceMusicChanges(_flight, 0, 5);
        Assert.Equal(MusicTrack.EnemyAceKilled, director.CurrentMusicTrack);

        director.CurrentMusicTrack = -1;
        director.NewSpaceMusicChanges(_flight, 3, 5);
        Assert.Equal(-1, director.CurrentMusicTrack);

        _flight.EnemiesNear = false;
        director.NewSpaceMusicChanges(_flight, 3, 5);
        Assert.Equal(MusicTrack.OverallVictory, director.CurrentMusicTrack);

        _flight.YourWingman = 2;
        director.NewSpaceMusicChanges(_flight, 7, 2);
        Assert.Equal(MusicTrack.WingmanHit, director.CurrentMusicTrack);

        _flight.PlayerMissionType = MusicDirector.MissionTypeEscort;
        _flight.PlayerMissionShip = 4;
        _flight.MissionIndices[6] = 4;
        director.NewSpaceMusicChanges(_flight, 7, 6);
        Assert.Equal(MusicTrack.OverallDefeat, director.CurrentMusicTrack);

        director.NewSpaceMusicChanges(_flight, 7, 8);
        Assert.Equal(MusicTrack.AllyKilled, director.CurrentMusicTrack);

        _flight.Sides[9] = 2;
        director.CurrentMusicTrack = -1;
        director.NewSpaceMusicChanges(_flight, 7, 9);
        Assert.Equal(-1, director.CurrentMusicTrack);
    }

    [Fact]
    public void UnratedKills_UseTheSharedRandomGenerator()
    {
        var director = CreateDirector(seed: 1234);
        var mirror = new CRandom(1234);
        director.InitInflightMusic();
        _flight.Sides[5] = MusicDirector.SideKilrathi;
        _flight.EnemiesNear = true;
        for (int i = 0; i < 20; i++)
        {
            director.NewSpaceMusicChanges(_flight, 0, 5);
            int expected = mirror.InRange(0, 3) != 0 ? MusicTrack.TargetHit : MusicTrack.EnemyAceKilled;
            Assert.Equal(expected, director.CurrentMusicTrack);
        }
    }

    [Fact]
    public void KillCues_AreIgnoredOutsideFlight_AndInTheSimulator()
    {
        var director = CreateDirector();
        director.NewSpaceMusicChanges(_flight, 0, 5);
        Assert.Equal(-1, director.CurrentMusicTrack);
        director.InitInflightMusic();
        _flight.TrainingSimulatorActive = true;
        director.NewSpaceMusicChanges(_flight, 0, 5);
        Assert.Equal(-1, director.CurrentMusicTrack);
    }

    [Fact]
    public void ServiceTrack_PlaysTheAsteroidSoundOncePerObject()
    {
        var director = CreateDirector();
        var sfx = new SoundEffectManager(_sounds, _world);
        _world.Classes[10] = MusicDirector.ObjectClassAsteroid;
        _world.Distances[10] = 0;
        _world.PreviousDistances[10] = 40;
        director.ServiceTrack(_flight, _world, sfx, 1);
        director.ServiceTrack(_flight, _world, sfx, 2);
        Assert.Single(_sounds.Played);
        Assert.Equal((6, 127, 64, 10, 0), _sounds.Played[0]);

        sfx.ClearSourceActive(10);
        director.ServiceTrack(_flight, _world, sfx, 3);
        Assert.Equal(2, _sounds.Played.Count);

        sfx.FlightSoundEffectsEnabled = false;
        sfx.ClearSourceActive(10);
        director.ServiceTrack(_flight, _world, sfx, 4);
        Assert.Equal(2, _sounds.Played.Count);
    }

    [Fact]
    public void ServiceTrack_PassingShipSound_IsRateLimitedByTheCooldownOnly()
    {
        var director = CreateDirector();
        var sfx = new SoundEffectManager(_sounds, _world);
        _world.Classes[3] = MusicDirector.ObjectClassShip;
        _world.ScreenX[3] = 100;
        _world.Distances[3] = 1000;
        _world.PassingDots[3] = 0x10;

        var played = new List<int>();
        for (short frame = 1; frame <= 20; frame++)
        {
            int before = _sounds.Played.Count;
            director.ServiceTrack(_flight, _world, sfx, frame);
            if (_sounds.Played.Count > before)
                played.Add(frame);
        }
        // the always-true class test releases the tracked object every frame, so only the
        // 6-frame cooldown limits the sound: frames 1, 8, 15
        Assert.Equal([1, 8, 15], played);
        Assert.All(_sounds.Played, p => Assert.Equal(2, p.Number));

        _world.Distances[3] = 0x55a;
        int count = _sounds.Played.Count;
        director.ServiceTrack(_flight, _world, sfx, 100);
        Assert.Equal(count, _sounds.Played.Count);
    }

    [Fact]
    public void WaitForEndOfMusic_IsSplitIntoNonBlockingSteps()
    {
        var director = CreateDirector();
        director.SpaceTrack(MusicTrack.Funeral, 2, 1);
        Tick(director);
        Assert.True(director.BeginWaitForEndOfMusic());
        Assert.False(director.ContinueWaitForEndOfMusic(false));
        _backend.IsActiveTrackFinished = true;
        Tick(director);
        Assert.True(director.ContinueWaitForEndOfMusic(false));

        director.SpaceTrack(MusicTrack.Funeral, 2, 1);
        Tick(director);
        Assert.True(director.BeginWaitForEndOfMusic());
        Assert.True(director.ContinueWaitForEndOfMusic(true));
        Assert.Equal(-1, director.CurrentMusicTrack);

        director.SpaceTrack(MusicTrack.Funeral, 2, 1);
        director.WaitForMusicEnabled = false;
        Assert.False(director.BeginWaitForEndOfMusic());
        Assert.Equal(-1, director.CurrentMusicTrack);
        director.EnableMusicForScene();
        Assert.True(director.WaitForMusicEnabled);
    }

    [Fact]
    public void StartupIntro_PlaysTrack19_AndRestoresThePreviousRequest()
    {
        var director = CreateDirector();
        director.SpaceTrack(MusicTrack.TitleFanfare, 2, 1);
        int previous = director.BeginStartupIntroMusic(_now);
        Assert.Equal(MusicTrack.TitleFanfare, previous);
        Assert.Equal(MusicTrack.StartupIntro, _backend.ActiveTrack);
        Assert.Equal(0, director.SequencePosition);
        _backend.Position = 3;
        Assert.Equal(3, director.SequencePosition);
        director.EndStartupIntroMusic(previous, _now);
        Assert.Equal(MusicTrack.TitleFanfare, _backend.ActiveTrack);

        director.MusicPlaybackMode = 3;
        Assert.Equal(MusicDirector.IntroMusicDisabled, director.BeginStartupIntroMusic(_now));
    }

    [Fact]
    public void StreamSetAndTriggerTables_MatchTheReference()
    {
        Assert.Equal(2, MusicDirector.GetStreamSet(0));
        Assert.Equal(2, MusicDirector.GetStreamSet(32));
        Assert.Equal(-1, MusicDirector.GetStreamSet(19));
        Assert.Equal(0, MusicDirector.GetStreamSet(35));
        Assert.Equal(1, MusicDirector.GetStreamSet(36));
        Assert.Equal(-1, MusicDirector.GetStreamSet(41));
        Assert.Equal(19, MusicDirector.MapMusicTrackToStreamerCommand(31));
        Assert.Equal(-1, MusicDirector.MapMusicTrackToStreamerCommand(23));
        Assert.Equal(0, MusicDirector.MapMusicTrackToStreamerCommand(29));
    }
}
