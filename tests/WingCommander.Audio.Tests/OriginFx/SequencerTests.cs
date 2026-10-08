using WingCommander.Audio.OriginFx;
using WingCommander.Tests;

namespace WingCommander.Audio.Tests.OriginFx;

public class SequencerTests
{
    private const int BlockFrames = 1470;

    public static IEnumerable<object[]> Sections() => AudioTestData.AllSections();

    /// <summary>
    /// Full renders in 1470-frame blocks (the reference's SDL buffer); golden hashes of the
    /// whole PCM stream come from the C++ reference (vendored ymfm + originfx.cpp, MSVC).
    /// All 41 sections were compared once (docs/progress/audio.md); the short ones and the
    /// intro are pinned here.
    /// </summary>
    [DataTheory]
    [InlineData(6, 0x833dba128470f7afUL, 105175)]
    [InlineData(9, 0x0a8b5af50fd3974bUL, 130096)]
    [InlineData(10, 0x4f9cc3855c555777UL, 156992)]
    [InlineData(19, 0x502af682d2985a5bUL, 555220)]
    [InlineData(21, 0xa3ad2dae7b58f843UL, 35832)]
    [InlineData(22, 0x5aa92b77a12ea1abUL, 37096)]
    public void FullRender_MatchesTheReferencePcm(int section, ulong expectedHash, long expectedFrames)
    {
        var player = new OriginFxSequencer(AudioTestData.Sequence(section), AudioTestData.Bank);
        var block = new short[BlockFrames * 2];
        var hash = new Fnv1a();
        while (!player.Finished)
        {
            player.Render(block, 0x7fff);
            hash.Add(block);
        }
        Assert.Equal(expectedFrames, player.CurrentFrame);
        Assert.Equal(expectedHash, hash.Value);
    }

    [DataTheory]
    [MemberData(nameof(Sections))]
    public void EverySection_RendersWithoutErrors_AndIsNotSilent(int section)
    {
        var sequence = AudioTestData.Sequence(section);
        var player = new OriginFxSequencer(sequence, AudioTestData.Bank);
        var block = new short[BlockFrames * 2];
        long nonZero = 0;
        long maxFrames = Math.Min(sequence.EndFrame, 4 * OriginFxSynth.OutputRate);
        while (player.CurrentFrame < maxFrames && !player.Finished)
        {
            player.Render(block, 0x7fff);
            for (int i = 0; i < block.Length; i += 2)
            {
                Assert.Equal(block[i], block[i + 1]);
                if (block[i] != 0)
                    nonZero++;
            }
        }
        Assert.True(nonZero > 1000, $"section {section} produced only {nonZero} non-zero frames");
    }

    [DataFact]
    public void Section19_RenderedPosition_MatchesTheClockPrediction()
    {
        var sequence = AudioTestData.Sequence(19);
        var player = new OriginFxSequencer(sequence, AudioTestData.Bank);
        var block = new short[BlockFrames * 2];
        var seen = new SortedSet<int>();
        while (!player.Finished)
        {
            player.Render(block, 0x7fff);
            Assert.Equal(sequence.GetSequencePositionAfter(player.CurrentFrame), player.SequencePosition);
            Assert.Equal(sequence.IsFinishedAfter(player.CurrentFrame), player.Finished);
            seen.Add(player.SequencePosition);
        }
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], seen);
    }

    [DataFact]
    public void Gain_ScalesTheOutput()
    {
        var full = Render(21, 0x7fff);
        var half = Render(21, 0x4000);
        var silent = Render(21, 0);
        Assert.All(silent, s => Assert.Equal(0, s));
        for (int i = 0; i < full.Length; i++)
            Assert.Equal((short)((long)full[i] * 0x4000 / 0x7fff), half[i]);
    }

    [DataFact]
    public void Mix_AddsWithSaturation()
    {
        var block = new short[BlockFrames * 2];
        Array.Fill(block, (short)32000);
        var player = new OriginFxSequencer(AudioTestData.Sequence(0), AudioTestData.Bank);
        for (int i = 0; i < 20; i++)
            player.Mix(block, 0x7fff);
        Assert.All(block, s => Assert.InRange((int)s, -32768, 32767));
        Assert.Contains(block, s => s == 32767);
    }

    private static short[] Render(int section, uint gain)
    {
        var player = new OriginFxSequencer(AudioTestData.Sequence(section), AudioTestData.Bank);
        var output = new List<short>();
        var block = new short[BlockFrames * 2];
        while (!player.Finished)
        {
            player.Render(block, gain);
            output.AddRange(block);
        }
        return output.ToArray();
    }
}
