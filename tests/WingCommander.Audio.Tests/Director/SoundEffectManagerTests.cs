using WingCommander.Audio.Director;

namespace WingCommander.Audio.Tests.Director;

public class SoundEffectManagerTests
{
    private readonly FakeSoundBackend _backend = new();
    private readonly FakeWorld _world = new();

    private SoundEffectManager Create() => new(_backend, _world);

    [Fact]
    public void PlayerSound_IsFullVolume_Centred_WithTagMinusOne()
    {
        var sfx = Create();
        Assert.True(sfx.PlaySfx(SoundEffectNumber.VduSelect, -1, 0));
        Assert.Equal((25, 127, 64, -1, 0), _backend.Played[0]);
        Assert.True(sfx.IsSourceActive(-1));
    }

    [Theory]
    [InlineData(0, 127)]
    [InlineData(500 * 256 - 1, 127)]
    [InlineData(500 * 256, 126)]
    [InlineData(500 * 256 * 20, 107)]
    [InlineData(500 * 256 * 117, 10)]
    public void PositionalSound_LosesOneVolumeStepPer500Metres(int magnitude, int expectedVolume)
    {
        var sfx = Create();
        _world.Geometry[4] = new SoundSourceGeometry(magnitude, 0);
        Assert.True(sfx.PlaySfx(SoundEffectNumber.Explosion, 4, 0));
        Assert.Equal(expectedVolume, _backend.Played[0].Volume);
        Assert.Equal(4, _backend.Played[0].Tag);
        Assert.True(sfx.IsSourceActive(4));
    }

    [Fact]
    public void DistantSound_IsNotPlayed()
    {
        var sfx = Create();
        _world.Geometry[4] = new SoundSourceGeometry(500 * 256 * 118, 0);
        Assert.False(sfx.PlaySfx(SoundEffectNumber.Explosion, 4, 0));
        Assert.Empty(_backend.Played);
        Assert.False(sfx.IsSourceActive(4));
    }

    [Theory]
    [InlineData(0, 64)]
    [InlineData(1, 64)]
    [InlineData(-1, 65)]
    [InlineData(100, 39)]
    [InlineData(-100, 89)]
    [InlineData(256, 0)]
    [InlineData(-256, 127)]
    [InlineData(400, 0)]
    public void Pan_FollowsTheEyeRightVector(int stereoOffset, int expectedPan)
    {
        var sfx = Create();
        _world.Geometry[7] = new SoundSourceGeometry(0, stereoOffset);
        sfx.PlaySfx(SoundEffectNumber.Laser, 7, 0);
        Assert.Equal(expectedPan, _backend.Played[0].Pan);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(64)]
    [InlineData(100)]
    public void InvalidSourceObjects_AreRejected(int source)
    {
        Assert.False(Create().PlaySfx(SoundEffectNumber.Explosion, source, 0));
        Assert.Empty(_backend.Played);
    }

    [Fact]
    public void RejectedEffects_DoNotMarkTheSource()
    {
        var sfx = Create();
        _backend.Accept = false;
        Assert.False(sfx.PlaySfx(SoundEffectNumber.Explosion, 3, 0));
        Assert.False(sfx.IsSourceActive(3));
    }

    [Fact]
    public void AfterburnerFlag_TracksTheLastPlayerSound()
    {
        var sfx = Create();
        sfx.PlaySfx(SoundEffectNumber.Afterburner, -1, 0);
        Assert.True(sfx.AfterburnerSfxActive);
        sfx.PlaySfx(SoundEffectNumber.Explosion, 3, 0);
        Assert.True(sfx.AfterburnerSfxActive);
        sfx.PlaySfx(SoundEffectNumber.VduSelect, -1, 0);
        Assert.False(sfx.AfterburnerSfxActive);

        sfx.PlaySfx(SoundEffectNumber.Afterburner, -1, 0);
        sfx.OnAfterburnerExpired();
        Assert.False(sfx.AfterburnerSfxActive);
        Assert.Equal(1, _backend.StopAllCount);
        sfx.OnAfterburnerExpired();
        Assert.Equal(1, _backend.StopAllCount);
    }

    [Fact]
    public void AfterburnerSound_IsRefiredAfterTheDeadline()
    {
        var sfx = Create();
        var fired = new List<int>();
        for (short frame = 0; frame <= 20; frame++)
        {
            int before = _backend.Played.Count;
            sfx.ServiceAfterburnerSound(frame);
            if (_backend.Played.Count > before)
                fired.Add(frame);
        }
        Assert.Equal([1, 8, 15], fired);
        Assert.All(_backend.Played, p => Assert.Equal(12, p.Number));
    }

    [Fact]
    public void AfterburnerDeadline_ResetsWhenTheFrameCounterWraps()
    {
        var sfx = Create();
        sfx.AfterburnerSoundDeadline = 32767;
        sfx.ServiceAfterburnerSound(short.MinValue);
        Assert.Equal(0, sfx.AfterburnerSoundDeadline);
        Assert.Empty(_backend.Played);
    }

    [Fact]
    public void DamageAlarm_PlaysEveryFrame_BecauseTheHandleIsNeverSet()
    {
        var sfx = Create();
        for (short frame = 1; frame <= 5; frame++)
            Assert.False(sfx.ServiceDamageAlarm(true, frame));
        Assert.Equal(5, _backend.Played.Count);
        Assert.All(_backend.Played, p => Assert.Equal(32, p.Number));
        Assert.False(sfx.ServiceDamageAlarm(false, 6));
        Assert.Equal(0, _backend.StopAllCount);

        sfx.DamageAlarmSfxHandle = 7;
        Assert.False(sfx.ServiceDamageAlarm(true, 11));
        Assert.False(sfx.ServiceDamageAlarm(true, 20));
        Assert.Equal(6, _backend.Played.Count);
        Assert.True(sfx.ServiceDamageAlarm(false, 21));
        Assert.Equal(0, sfx.DamageAlarmSfxHandle);
        Assert.Equal(1, _backend.StopAllCount);
    }

    [Fact]
    public void ResetHelpers_FlushAndSetTheFlightGate()
    {
        var sfx = Create();
        sfx.PlaySfx(SoundEffectNumber.Afterburner, -1, 0);
        sfx.DamageAlarmSfxHandle = 3;
        sfx.ResetSoundStateForScene();
        Assert.False(sfx.FlightSoundEffectsEnabled);
        Assert.False(sfx.AfterburnerSfxActive);
        Assert.Equal(0, sfx.DamageAlarmSfxHandle);
        Assert.Equal(1, _backend.StopAllCount);
        sfx.ResetSoundStateForFlight();
        Assert.True(sfx.FlightSoundEffectsEnabled);
        Assert.Equal(2, _backend.StopAllCount);
    }

    [Fact]
    public void FlushVariants_StopEverything()
    {
        var sfx = Create();
        sfx.FlushSoundEffect();
        sfx.FlushSoundEffects();
        sfx.FlushSoundEffectsAndLog();
        sfx.StopAllSounds();
        Assert.Equal(4, _backend.StopAllCount);
    }

    [Fact]
    public void CockpitSelection_AlwaysPlaysSound25_AndStaticIsSilentWithDosData()
    {
        var sfx = Create();
        sfx.PlayCockpitSelectionSfx(3);
        sfx.PlaySnowStaticSound();
        Assert.Single(_backend.Played);
        Assert.Equal(25, _backend.Played[0].Number);
        Assert.Equal(0u, sfx.SoundFxTick());
    }
}
