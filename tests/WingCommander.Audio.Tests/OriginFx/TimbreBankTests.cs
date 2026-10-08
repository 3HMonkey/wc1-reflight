using WingCommander.Audio.OriginFx;
using WingCommander.Core.Resources;
using WingCommander.Tests;

namespace WingCommander.Audio.Tests.OriginFx;

public class TimbreBankTests
{
    [DataFact]
    public void WingLdrTim_HasThreeSectionsWithDocumentedSizes()
    {
        var tim = GameData.Require().OpenPacket(OriginFxTimbreBank.FileName);
        Assert.Equal(3, tim.SectionCount);
        Assert.Equal(1 + 37 * 247, tim.GetSection(0).Length);
        Assert.Equal(1 + 79 * 48, tim.GetSection(1).Length);
        Assert.Equal(1 + 30 * 32, tim.GetSection(2).Length);
    }

    [DataFact]
    public void AdLibBank_Has79Records()
    {
        var bank = AudioTestData.Bank;
        Assert.Equal(79, bank.Count);
        Assert.Equal(1 + 79 * OriginFxTimbreBank.TimbreSize, bank.RawData.Length);
        Assert.Equal(2, bank.ProgramOf(0));
    }

    [DataFact]
    public void MissingPrograms_FallBackToTheFirstRecord()
    {
        var bank = AudioTestData.Bank;
        foreach (int program in new[] { 72, 73, 82, 255, 300, -1 })
            Assert.Equal(0, bank.FindTimbre(program));
        Assert.Equal(44, bank.ProgramOf(bank.FindTimbre(44)));
    }

    [DataFact]
    public void OnlyProgram44_UsesAnOplRhythmVoice()
    {
        var bank = AudioTestData.Bank;
        var drums = Enumerable.Range(0, bank.Count).Where(i => bank[i].RhythmVoice != 0).ToList();
        Assert.Single(drums);
        Assert.Equal(44, bank[drums[0]].Program);
        Assert.Equal(6, bank[drums[0]].RhythmVoice);
    }

    [DataFact]
    public void Program127_IsLayeredWithProgram149()
    {
        var bank = AudioTestData.Bank;
        int first = bank.FindTimbre(127);
        int layered = bank.NextTimbre(first);
        Assert.Equal(149, bank.ProgramOf(layered));
        Assert.Equal(-1, bank.NextTimbre(layered));
        Assert.Equal(1, Enumerable.Range(0, bank.Count).Count(i => bank[i].Link != 0));
    }

    [DataFact]
    public void PercussionPseudoChannelPrograms_ExistExceptTheBongos()
    {
        var bank = AudioTestData.Bank;
        foreach (int program in new[] { 128, 114, 131, 113, 134, 135, 133, 132, 129, 136, 141, 143, 144, 145, 147, 140 })
            Assert.Equal(program, bank.ProgramOf(bank.FindTimbre(program)));

        // channel 26 (GM notes 60/61, bongos) is set to program 139, which the bank lacks:
        // those notes play with the first record (program 2), as in the reference
        Assert.Equal(0, bank.FindTimbre(139));
    }

    [DataFact]
    public void LaserTimbre_HasTheDocumentedPitchEnvelope()
    {
        var laser = AudioTestData.Bank[AudioTestData.Bank.FindTimbre(9)];
        Assert.Equal(1000, laser.InitialPitch);
        Assert.Equal(((ushort)200, (short)-500), laser.EnvelopeStage(0));
        Assert.Equal(((ushort)100, (short)-1500), laser.EnvelopeStage(1));
    }

    [Fact]
    public void Parse_RejectsTruncatedBanks()
    {
        Assert.Throws<GameDataException>(() => OriginFxTimbreBank.Parse(new byte[10]));
        var oneRecordButTwoDeclared = new byte[1 + OriginFxTimbreBank.TimbreSize];
        oneRecordButTwoDeclared[0] = 2;
        Assert.Throws<GameDataException>(() => OriginFxTimbreBank.Parse(oneRecordButTwoDeclared));
        var zeroRecords = new byte[1 + OriginFxTimbreBank.TimbreSize];
        Assert.Throws<GameDataException>(() => OriginFxTimbreBank.Parse(zeroRecords));
    }

    [Fact]
    public void Parse_KeepsOnlyTheDeclaredRecords()
    {
        var data = new byte[1 + 3 * OriginFxTimbreBank.TimbreSize + 5];
        data[0] = 2;
        data[1 + 47] = 10;
        data[1 + OriginFxTimbreBank.TimbreSize + 47] = 11;
        var bank = OriginFxTimbreBank.Parse(data);
        Assert.Equal(2, bank.Count);
        Assert.Equal(1 + 2 * OriginFxTimbreBank.TimbreSize, bank.RawData.Length);
        Assert.Equal(1, bank.FindTimbre(11));
        Assert.Equal(0, bank.FindTimbre(12));
    }
}
