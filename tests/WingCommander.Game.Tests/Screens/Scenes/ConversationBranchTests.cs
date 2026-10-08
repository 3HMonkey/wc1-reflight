using WingCommander.Game.Screens.Scenes;
using WingCommander.Tests;
using Xunit.Abstractions;

namespace WingCommander.Game.Tests.Screens.Scenes;

/// <summary>
/// The scene tests pick the records the original would play for a given campaign state. The
/// expected record sequences were traced by hand through the BRIEFING.000 scripts (see the record
/// dumps in docs/progress/screens-scenes.md).
/// </summary>
[Collection(SceneRig.Collection)]
public class ConversationBranchTests(ITestOutputHelper output)
{
    private int[] Play(SceneRig rig, SceneDirector director, double limit = 1_200_000)
    {
        Assert.True(rig.RunUntil(limit), "the scene did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        var indices = director.PlayedRecords.Select(r => r.Index).ToArray();
        output.WriteLine(string.Join(", ", indices));
        return indices;
    }

    [DataFact]
    public void Debriefing_without_objectives_or_kills_takes_the_poor_result_branch()
    {
        var rig = new SceneRig();
        var outcome = new FakeOutcome();
        var director = rig.Start(outcome, d => d.DebriefingAsync(1, 0));
        Assert.Equal([0, 10, 11, 12, 13, 14, 15, 16, 17, 23, 25, 27, 30], Play(rig, director));
    }

    [DataFact]
    public void Debriefing_with_all_objectives_and_kills_takes_the_praise_branch()
    {
        var rig = new SceneRig();
        var outcome = new FakeOutcome { PlayerKills = 3, WingmanKills = 2 };
        for (int objective = 0; objective < 16; objective++)
            outcome.AchievedObjectives.Add(objective);
        var director = rig.Start(outcome, d => d.DebriefingAsync(1, 0));
        Assert.Equal([0, 1, 2, 3, 9, 23, 24, 26, 30], Play(rig, director));
    }

    [DataFact]
    public void Debriefing_mourns_a_wingman_who_died_this_mission()
    {
        var rig = new SceneRig();
        rig.Game.Session.WingmanKilled(0); // Spirit, mission number 4 (series 1, mission 0)
        Assert.Equal(1, rig.Game.Session.WingStatus(0));
        var director = rig.Start(new FakeOutcome(), d => d.DebriefingAsync(1, 0));
        Assert.Equal([0, 10, 18, 19, 20, 21, 22, 23, 25, 27, 28, 30], Play(rig, director));
    }

    [DataTheory]
    [InlineData(0, new[] { 0, 1, 8, 9, 10, 13, 14, 15 })]
    [InlineData(3, new[] { 0, 6, 7, 8, 9, 12, 13, 14, 15 })]
    [InlineData(4, new[] { 0, 2, 3, 4, 5, 8, 9, 11, 13, 14, 15 })]
    public void Medal_ceremony_reads_the_citation_of_the_medal(int medal, int[] expected)
    {
        var rig = new SceneRig();
        var director = rig.Start(new FakeOutcome(), d => d.AwardCampaignMedalAsync(medal));
        Assert.Equal(expected, Play(rig, director));
        Assert.Equal(1, rig.Game.Session.State.Medals[medal]);
    }

    [DataFact]
    public void A_second_golden_sun_is_never_awarded()
    {
        var rig = new SceneRig();
        rig.Game.Session.State.Medals[3] = 1;
        var director = rig.Start(new FakeOutcome(), d => d.AwardCampaignMedalAsync(3));
        Assert.True(rig.RunUntil(1_000));
        Assert.Equal(0, director.RecordsPlayed);
        Assert.Equal(1, rig.Game.Session.State.Medals[3]);
    }

    [DataFact]
    public void Office_announces_a_promotion()
    {
        var rig = new SceneRig();
        rig.Game.Session.PromotionPending = true;
        rig.Game.Session.OfficeVisitPending = true;
        var director = rig.Start(new FakeOutcome(), d => d.OfficeAsync());
        Assert.Equal([0, 2, 3, 4, 5, 6, 7, 8, 33, 34], Play(rig, director));
    }
}
