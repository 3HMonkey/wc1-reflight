using WingCommander.Core.Numerics;
using WingCommander.Game.Campaign;

namespace WingCommander.Game.Tests.Campaign;

public class HighScoreTests
{
    [Fact]
    public void Initial_ranking_has_five_distinct_pilots_and_the_player_last()
    {
        var scores = new TrainSimHighScores();
        scores.Initialize(new CRandom(99));
        var pilots = new HashSet<int>();
        for (int i = 0; i < 5; i++)
        {
            var e = scores.Entries[i];
            Assert.NotEqual(8, e.PilotIndex);
            Assert.InRange(e.PilotIndex, 0, 14);
            Assert.True(pilots.Add(e.PilotIndex));
            if (i > 0)
                Assert.True(scores.Entries[i - 1].Score > e.Score);
        }
        Assert.Equal(8, scores.Entries[5].PilotIndex);
        Assert.Equal(0u, scores.Entries[5].Score);
    }

    [Fact]
    public void Insert_keeps_the_table_sorted()
    {
        var scores = new TrainSimHighScores();
        scores.Initialize(new CRandom(5));
        int rank = scores.Insert(8, 50000);
        Assert.Equal(0, rank);
        Assert.Equal(8, scores.Entries[0].PilotIndex);
        for (int i = 1; i < TrainSimHighScores.EntryCount; i++)
            Assert.True((int)scores.Entries[i - 1].Score >= (int)scores.Entries[i].Score);
    }

    [Fact]
    public void Dead_wingmen_never_improve()
    {
        var scores = new TrainSimHighScores();
        var random = new CRandom(17);
        scores.Initialize(random);
        var initial = scores.Entries.Where(e => e.PilotIndex < 8).ToDictionary(e => e.PilotIndex, e => e.Score);
        var campaign = CampaignState.CreateInitial();
        for (int p = 0; p < 8; p++)
            campaign.PersonalityDeathMission[p] = 5;
        for (int round = 0; round < 20; round++)
            scores.AddRandomScores(random, campaign);
        foreach (var e in scores.Entries.Where(e => e.PilotIndex < 8 && e.PilotIndex != 8))
            Assert.Equal(initial[e.PilotIndex], e.Score);
    }

    [Fact]
    public void Built_in_names_cover_indices_nine_to_fourteen()
    {
        var scores = new TrainSimHighScores();
        var roster = PilotRecord.CreateInitialRoster();
        scores.Set(0, 9, 100);
        scores.Set(1, 14, 50);
        scores.Set(2, 3, 25);
        Assert.Equal("BISHOP", scores.GetName(0, roster));
        Assert.Equal("MONGO", scores.GetName(1, roster));
        Assert.Equal("ICEMAN", scores.GetName(2, roster));
    }
}
