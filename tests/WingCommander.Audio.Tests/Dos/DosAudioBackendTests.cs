using WingCommander.Audio.Director;
using WingCommander.Audio.Dos;
using WingCommander.Audio.OriginFx;
using WingCommander.Tests;

namespace WingCommander.Audio.Tests.Dos;

public class DosAudioBackendTests
{
    [Theory]
    [InlineData(20, 32767u)]
    [InlineData(21, 32767u)]
    [InlineData(100, 32767u)]
    [InlineData(16, 32255u)]
    [InlineData(12, 31487u)]
    [InlineData(6, 28159u)]
    [InlineData(2, 20479u)]
    [InlineData(1, 0u)]
    [InlineData(0, 0u)]
    [InlineData(-5, 0u)]
    public void CalculateGain_FollowsTheVolumeTable(int setting, uint expected) =>
        Assert.Equal(expected, DosAudioBackend.CalculateGain(setting));

    [DataFact]
    public void LaserSound_AlternatesTags64And65()
    {
        var (backend, engine) = Create();
        var buffer = new short[64];
        Assert.True(backend.Play(SoundEffectNumber.Laser, 127, 64, -1, 0));
        Assert.True(backend.Play(SoundEffectNumber.Laser, 127, 64, -1, 0));
        backend.Mixer.Render(buffer);
        Assert.True(engine.TryFindEffectByTag(64, out _));
        Assert.True(engine.TryFindEffectByTag(65, out _));
        Assert.False(engine.TryFindEffectByTag(-1, out _));

        Assert.True(backend.Play(SoundEffectNumber.MissileLaunch, 127, 64, -1, 0));
        backend.Mixer.Render(buffer);
        Assert.Equal(3, engine.ActiveEffectCount);
    }

    [DataFact]
    public void Play_RejectsInvalidNumbers_StopAllIsQueued()
    {
        var (backend, engine) = Create();
        Assert.False(backend.Play(0, 127, 64, -1, 0));
        Assert.False(backend.Play(37, 127, 64, -1, 0));
        Assert.True(backend.Play(12, 127, 64, -1, 0));
        backend.Mixer.Render(new short[2]);
        Assert.Equal(1, engine.ActiveEffectCount);
        backend.StopAll();
        backend.Mixer.Render(new short[2]);
        Assert.Equal(0, engine.ActiveEffectCount);
    }

    [DataFact]
    public void TryStartTrack_RejectsMissingSections_AndKeepsTheCurrentTrack()
    {
        var (backend, _) = Create();
        Assert.True(backend.TryStartTrack(19, TimeSpan.Zero));
        Assert.False(backend.TryStartTrack(41, TimeSpan.Zero));
        Assert.False(backend.TryStartTrack(-1, TimeSpan.Zero));
        Assert.Equal(19, backend.ActiveTrack);
        Assert.NotNull(backend.ActiveSequencer);
    }

    [DataFact]
    public void PlaybackState_FollowsTheVirtualClock()
    {
        var (backend, _) = Create();
        var start = TimeSpan.FromSeconds(100);
        Assert.Equal(-1, backend.SequencePosition);
        Assert.True(backend.TryStartTrack(19, start));
        Assert.Equal(0, backend.SequencePosition);

        backend.Update(start + TimeSpan.FromMilliseconds(5500));
        Assert.Equal(0, backend.SequencePosition);
        backend.Update(start + TimeSpan.FromMilliseconds(5530));
        Assert.Equal(1, backend.SequencePosition);
        backend.Update(start + TimeSpan.FromMilliseconds(17400));
        Assert.Equal(3, backend.SequencePosition);
        Assert.False(backend.IsActiveTrackFinished);

        backend.Update(start + TimeSpan.FromMilliseconds(25180));
        Assert.False(backend.IsActiveTrackFinished);
        backend.Update(start + TimeSpan.FromMilliseconds(25181));
        Assert.True(backend.IsActiveTrackFinished);
        Assert.Equal(6, backend.SequencePosition);

        backend.StopTrack();
        Assert.Equal(-1, backend.ActiveTrack);
        Assert.Equal(-1, backend.SequencePosition);
        Assert.True(backend.IsActiveTrackFinished);
    }

    [DataFact]
    public void PreloadAllTracks_ParsesEverySection()
    {
        var (backend, _) = Create();
        backend.PreloadAllTracks();
        for (int track = 0; track < backend.TrackCount; track++)
            Assert.Same(backend.GetSequence(track), backend.GetSequence(track));
    }

    [DataFact]
    public void ApplyVolumeSettings_PostsOnlyChanges()
    {
        var (backend, _) = Create();
        long before = backend.Mixer.DroppedCommandCount;
        for (int i = 0; i < 2000; i++)
            backend.ApplyVolumeSettings(20, 20);
        // only the first call posts (two commands); the queue would overflow otherwise
        Assert.Equal(before, backend.Mixer.DroppedCommandCount);
    }

    private static (DosAudioBackend Backend, OriginFxSoundEffectEngine Engine) Create()
    {
        var engine = new OriginFxSoundEffectEngine(AudioTestData.Bank);
        var backend = new DosAudioBackend(AudioTestData.Music, AudioTestData.Bank, new DosAudioMixer(engine));
        return (backend, engine);
    }
}
