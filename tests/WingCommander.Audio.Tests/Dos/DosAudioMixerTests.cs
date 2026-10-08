using WingCommander.Audio.Dos;
using WingCommander.Audio.OriginFx;
using WingCommander.Tests;

namespace WingCommander.Audio.Tests.Dos;

public class DosAudioMixerTests
{
    private const int BlockFrames = DosAudioMixer.ReferenceBufferFrames;

    /// <summary>
    /// A fixed command sequence (music start/switch/stop, overlapping laser effects, stop-all,
    /// a glide, gain changes) rendered in 1470-frame callbacks. The golden hash comes from the
    /// C++ reference emulating SdlMixDosAdlibMusic with the same commands applied between
    /// callbacks (the reference takes the mutex there), so it pins the whole mix chain.
    /// </summary>
    [DataFact]
    public void FixedCommandSequence_MatchesTheReferenceMix()
    {
        Assert.Equal(0x50bf4ecd8ad66c4bUL, RenderScenario());
    }

    [DataFact]
    public void Rendering_IsDeterministic()
    {
        Assert.Equal(RenderScenario(), RenderScenario());
    }

    [DataFact]
    public void Render_DoesNotAllocate()
    {
        var bank = AudioTestData.Bank;
        var mixer = new DosAudioMixer(bank);
        var sequencer = new OriginFxSequencer(AudioTestData.Sequence(0), bank);
        var buffer = new short[BlockFrames * 2];
        mixer.PostMusic(sequencer);
        mixer.PostPlaySoundEffect(8, 127, 20, 64, 0);
        mixer.Render(buffer);
        mixer.Render(buffer);

        mixer.PostPlaySoundEffect(4, 127, 100, 3, 0);
        mixer.PostSoundGain(20000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++)
            mixer.Render(buffer);
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Null(mixer.Fault);
        Assert.Equal(before, after);
    }

    [DataFact]
    public void FullQueue_DropsCommands_AndRecovers()
    {
        var mixer = new DosAudioMixer(AudioTestData.Bank);
        int accepted = 0;
        for (int i = 0; i < DosAudioMixer.CommandCapacity + 10; i++)
            accepted += mixer.PostPlaySoundEffect(1, 127, 64, -1, 0) ? 1 : 0;
        Assert.Equal(DosAudioMixer.CommandCapacity, accepted);
        Assert.Equal(10, mixer.DroppedCommandCount);

        var buffer = new short[BlockFrames * 2];
        mixer.Render(buffer);
        Assert.True(mixer.PostStopSoundEffects());
        mixer.Render(buffer);
        Assert.Null(mixer.Fault);
    }

    [DataFact]
    public void Render_HandlesOddAndEmptyBuffers()
    {
        var mixer = new DosAudioMixer(AudioTestData.Bank);
        mixer.PostPlaySoundEffect(8, 127, 64, -1, 0);
        var odd = new short[101];
        mixer.Render(odd);
        Assert.Equal(0, odd[100]);
        mixer.Render(Span<short>.Empty);
        Assert.Equal(50, mixer.RenderedFrames);
        Assert.Null(mixer.Fault);
    }

    [DataFact]
    public void ProducersOnSeveralThreads_DoNotLoseCommands()
    {
        var mixer = new DosAudioMixer(AudioTestData.Bank);
        const int perThread = 200;
        Parallel.For(0, 4, _ =>
        {
            for (int i = 0; i < perThread; i++)
                Assert.True(mixer.PostMusicGain(0x7fff));
        });
        Assert.Equal(0, mixer.DroppedCommandCount);
        mixer.Render(new short[2]);
        Assert.True(mixer.PostStopSoundEffects());
    }

    private static ulong RenderScenario()
    {
        var bank = AudioTestData.Bank;
        var music21 = AudioTestData.Sequence(21);
        var music06 = AudioTestData.Sequence(6);
        var mixer = new DosAudioMixer(bank);
        var block = new short[BlockFrames * 2];
        var hash = new Fnv1a();
        for (int b = 0; b < 50; b++)
        {
            if (b == 0)
            {
                mixer.PostMusic(new OriginFxSequencer(music21, bank));
                mixer.PostMusicGain(DosAudioBackend.CalculateGain(16));
                mixer.PostSoundGain(DosAudioBackend.CalculateGain(12));
            }
            if (b == 2)
                mixer.PostPlaySoundEffect(8, 100, 30, 64, 0);
            if (b == 3)
                mixer.PostPlaySoundEffect(8, 100, 98, 65, 0);
            if (b == 5)
                mixer.PostPlaySoundEffect(4, 127, 64, 3, 0);
            if (b == 10)
                mixer.PostStopSoundEffects();
            if (b == 12)
                mixer.PostPlaySoundEffect(20, 90, 64, -1, 0);
            if (b == 20)
                mixer.PostMusic(new OriginFxSequencer(music06, bank));
            if (b == 30)
            {
                mixer.PostMusicGain(DosAudioBackend.CalculateGain(6));
                mixer.PostSoundGain(DosAudioBackend.CalculateGain(20));
            }
            if (b == 40)
                mixer.PostMusic(null);
            mixer.Render(block);
            hash.Add(block);
        }
        Assert.Null(mixer.Fault);
        return hash.Value;
    }
}
