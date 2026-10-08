using WingCommander.Audio.OriginFx;
using WingCommander.Tests;

namespace WingCommander.Audio.Tests.OriginFx;

public class SoundEffectEngineTests
{
    private const int BlockFrames = 1470;

    /// <summary>
    /// Every sound 1..36 for 6 blocks with alternating pans; golden hash from the C++ reference
    /// sound player (vendored ymfm + originfx.cpp) running the same script.
    /// </summary>
    [DataFact]
    public void EverySound_MatchesTheReferencePcm()
    {
        var engine = new OriginFxSoundEffectEngine(AudioTestData.Bank);
        var block = new short[BlockFrames * 2];
        var hash = new Fnv1a();
        int accepted = 0;
        for (int n = 1; n <= 36; n++)
        {
            engine.StopAll();
            int pan = (n % 3) switch { 0 => 0, 1 => 127, _ => 64 };
            accepted += engine.Play(n, 120, pan, -1, 0) ? 1 : 0;
            for (int b = 0; b < 6; b++)
            {
                Array.Clear(block);
                engine.Mix(block, 0x7fff);
                hash.Add(block);
            }
        }
        Assert.Equal(36, accepted);
        Assert.Equal(0xf344866357193490UL, hash.Value);
    }

    [DataFact]
    public void Sound20_GlidesUpOneSemitoneEveryTwoTicks_ThenEnds()
    {
        var engine = new OriginFxSoundEffectEngine(AudioTestData.Bank);
        Assert.True(engine.Play(20, 127, 64, -1, 0));
        Assert.True(engine.TryFindEffectByTag(-1, out var state));
        Assert.Equal(24, state.CurrentNote);
        Assert.Equal(2, state.RemainingTicks);

        for (int tick = 1; tick <= 207; tick++)
        {
            engine.ServiceSoundEffects();
            Assert.True(engine.TryFindEffectByTag(-1, out state), $"ended early at tick {tick}");
            Assert.Equal(Math.Min(127, 24 + tick / 2), state.CurrentNote);
        }

        // the next expiry finds the target reached: the note resets and the effect ends
        engine.ServiceSoundEffects();
        Assert.False(engine.TryFindEffectByTag(-1, out _));
        Assert.Equal(0, engine.ActiveEffectCount);
    }

    [DataFact]
    public void Sound2_GlidesDownToZero()
    {
        var engine = new OriginFxSoundEffectEngine(AudioTestData.Bank);
        Assert.True(engine.Play(2, 127, 64, 7, 0));
        for (int tick = 1; tick <= 64; tick++)
        {
            engine.ServiceSoundEffects();
            Assert.True(engine.TryFindEffectByTag(7, out var state));
            Assert.Equal(64 - tick, state.CurrentNote);
        }
        engine.ServiceSoundEffects();
        Assert.Equal(0, engine.ActiveEffectCount);
    }

    [DataFact]
    public void SustainedEffect_NeverExpires_RetriggerEffect_Restarts()
    {
        var engine = new OriginFxSoundEffectEngine(AudioTestData.Bank);
        Assert.True(engine.Play(12, 127, 64, 1, 0));
        Assert.True(engine.Play(14, 127, 64, 2, 0));
        for (int tick = 0; tick < 1000; tick++)
            engine.ServiceSoundEffects();
        Assert.True(engine.TryFindEffectByTag(1, out var afterburner));
        Assert.Equal(60, afterburner.RemainingTicks);
        Assert.True(engine.TryFindEffectByTag(2, out var klaxon));
        Assert.Equal(60 - 1000 % 60, klaxon.RemainingTicks);
        Assert.Equal(64, klaxon.CurrentNote);
    }

    [DataFact]
    public void SameTag_ReplacesTheEffect_KeepingChannelAndAge()
    {
        var engine = new OriginFxSoundEffectEngine(AudioTestData.Bank);
        Assert.True(engine.Play(1, 127, 64, 5, 0));
        Assert.True(engine.TryFindEffectByTag(5, out var first));
        Assert.True(engine.Play(4, 100, 20, 5, 3));
        Assert.Equal(1, engine.ActiveEffectCount);
        Assert.True(engine.TryFindEffectByTag(5, out var second));
        Assert.Equal(4, second.SoundNumber);
        Assert.Equal(first.Channel, second.Channel);
        Assert.Equal(first.Age, second.Age);
        Assert.Equal(first.Priority, second.Priority);
        Assert.Equal(100, second.Volume);
        Assert.Equal(20, second.Pan);
    }

    [DataFact]
    public void NinthEffect_StealsTheOldestChannel()
    {
        var engine = new OriginFxSoundEffectEngine(AudioTestData.Bank);
        for (int tag = 0; tag < 8; tag++)
            Assert.True(engine.Play(1, 127, 64, tag, 0));
        Assert.Equal(8, engine.ActiveEffectCount);
        var channels = Enumerable.Range(0, 8).Select(t => engine.TryFindEffectByTag(t, out var s) ? s.Channel : -1).ToList();
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], channels);

        Assert.True(engine.Play(4, 127, 64, 100, 0));
        Assert.Equal(8, engine.ActiveEffectCount);
        Assert.False(engine.TryFindEffectByTag(0, out _));
        Assert.True(engine.TryFindEffectByTag(100, out var stolen));
        Assert.Equal(1, stolen.Channel);
    }

    [DataFact]
    public void HigherPriorityEffects_AreNotStolen()
    {
        var engine = new OriginFxSoundEffectEngine(AudioTestData.Bank);
        for (int tag = 0; tag < 8; tag++)
            Assert.True(engine.Play(12, 127, 64, tag, 2));
        Assert.False(engine.Play(1, 127, 64, 50, 1));
        Assert.True(engine.Play(1, 127, 64, 51, 2));
        Assert.False(engine.TryFindEffectByTag(0, out _));
    }

    [DataFact]
    public void InvalidSoundNumbers_AreRejected()
    {
        var engine = new OriginFxSoundEffectEngine(AudioTestData.Bank);
        Assert.False(engine.Play(0, 127, 64, -1, 0));
        Assert.False(engine.Play(37, 127, 64, -1, 0));
        Assert.False(engine.Play(-1, 127, 64, -1, 0));
        Assert.Equal(0, engine.ActiveEffectCount);
    }

    [DataFact]
    public void StopAll_EndsEveryEffect_AndSilences()
    {
        var engine = new OriginFxSoundEffectEngine(AudioTestData.Bank);
        engine.Play(12, 127, 64, 1, 0);
        engine.Play(14, 127, 64, 2, 0);
        var block = new short[BlockFrames * 2];
        engine.Mix(block, 0x7fff);
        engine.StopAll();
        Assert.Equal(0, engine.ActiveEffectCount);
        Assert.Equal(0, engine.Synth.ActiveVoiceCount);
        for (int i = 0; i < 40; i++)
        {
            Array.Clear(block);
            engine.Mix(block, 0x7fff);
        }
        Assert.All(block, s => Assert.Equal(0, s));
    }

    [DataTheory]
    [InlineData(0)]
    [InlineData(64)]
    [InlineData(127)]
    public void Pan_AttenuatesTheOppositeSide(int pan)
    {
        var engine = new OriginFxSoundEffectEngine(AudioTestData.Bank);
        Assert.True(engine.Play(8, 127, pan, -1, 0));
        double left = 0, right = 0;
        var block = new short[BlockFrames * 2];
        for (int b = 0; b < 10; b++)
        {
            Array.Clear(block);
            engine.Mix(block, 0x7fff);
            for (int i = 0; i < block.Length; i += 2)
            {
                left += (double)block[i] * block[i];
                right += (double)block[i + 1] * block[i + 1];
            }
        }
        Assert.True(left > 0 || right > 0);
        if (pan == 64)
            Assert.Equal(left, right);
        else if (pan == 0)
            Assert.True(left > right * 10);
        else
            Assert.True(right > left * 10);
    }
}
