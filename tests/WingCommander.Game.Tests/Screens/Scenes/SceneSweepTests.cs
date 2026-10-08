using WingCommander.Game.Scenes;
using WingCommander.Game.Screens.Scenes;
using WingCommander.Tests;
using Xunit.Abstractions;

namespace WingCommander.Game.Tests.Screens.Scenes;

/// <summary>
/// Runs every conversation of the three campaigns (BRIEFING.000/001/002) through the real engine
/// with varied campaign states, skipping records with Space, and checks that nothing throws and
/// every scene ends.
/// </summary>
[Collection(SceneRig.Collection)]
public class SceneSweepTests(ITestOutputHelper output)
{
    private const double Limit = 2_000_000;

    /// <summary>A campaign state that varies with <paramref name="variant"/> so the scene tests take different branches.</summary>
    private static FakeOutcome Prepare(SceneRig rig, int series, int mission, int variant)
    {
        var session = rig.Game.Session;
        session.State.CurrentSeries = (sbyte)series;
        session.State.CurrentMission = (sbyte)mission;
        var outcome = new FakeOutcome { PlayerKills = variant % 3, WingmanKills = variant % 2 };
        for (int objective = 0; objective < 16; objective++)
        {
            if ((objective + variant) % 2 == 0)
                outcome.AchievedObjectives.Add(objective);
            if ((objective + variant) % 3 == 0)
                outcome.SightedObjectives.Add(objective);
        }
        if (variant % 4 == 0)
            session.WingmanKilled(variant % 8);
        session.PlayerEjectedThisMission = variant % 5 == 0;
        session.State.ElapsedDate.Year = (short)(variant % 3);
        session.PromotionPending = variant % 3 == 0;
        session.OfficeVisitPending = variant % 2 == 0;
        session.PlayerShipTypeChanged = variant % 4 == 1;
        session.State.PlayerShipType = variant % 4;
        session.PreviousPlayerShipType = (short)((variant + 1) % 4);
        session.State.AceFlags[variant % 4] = (byte)(variant % 7);
        session.State.MissionScore = (short)(variant * 7 % 60);
        return outcome;
    }

    private static void Run(SceneRig rig, Func<SceneDirector, Task> scene, FakeOutcome outcome, string what)
    {
        rig.Start(outcome, scene);
        rig.PressSpaceEvery(100, Limit, 150);
        Assert.True(rig.RunUntil(Limit), $"{what} did not end");
        Assert.True(rig.Screen.Runtime.Failure is null, $"{what}: {rig.Screen.Runtime.Failure}");
    }

    [DataTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Every_briefing_and_debriefing_plays_through(int campaign)
    {
        var file = BriefingFile.Load(GameData.Require(), campaign);
        int missions = 0;
        int records = 0;
        double virtualTime = 0;
        for (int series = 1; series <= 13; series++)
        {
            for (int mission = 0; mission < 4; mission++)
            {
                if (!file.HasMission(series, mission))
                    continue;
                int variant = missions++;
                var rig = new SceneRig(campaign);
                var outcome = Prepare(rig, series, mission, variant);
                Run(rig, async director =>
                {
                    await director.BriefingAsync(series, mission);
                    Assert.NotNull(director.Mission);
                    Assert.Equal(series, director.Mission!.Series);
                    Assert.True(director.RecordsPlayed >= 5, $"briefing {series}.{mission}: {director.RecordsPlayed} records");
                    records += director.RecordsPlayed;
                    await director.PlayScrambleHangarSceneAsync();
                    await director.DebriefingAsync(series, mission);
                    Assert.True(director.RecordsPlayed >= 3, $"debriefing {series}.{mission}: {director.RecordsPlayed} records");
                    records += director.RecordsPlayed;
                }, outcome, $"campaign {campaign} series {series} mission {mission}");
                virtualTime += rig.Screen.Runtime.Scheduler.Now;
            }
        }
        output.WriteLine($"{missions} missions, {records} records, {virtualTime / 1000:0} s of virtual time");
        Assert.True(missions >= (campaign == 0 ? 40 : 18));
    }

    [DataTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Office_and_medal_ceremonies_play_through(int campaign)
    {
        for (int variant = 0; variant < 6; variant++)
        {
            var rig = new SceneRig(campaign);
            var outcome = Prepare(rig, 2, 1, variant);
            int medal = variant % 5;
            Run(rig, async director =>
            {
                await director.OfficeAsync();
                await director.AwardCampaignMedalAsync(medal);
            }, outcome, $"campaign {campaign} office/medal variant {variant}");
        }
    }

    [DataTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Funerals_play_through(int campaign)
    {
        for (int series = 0; series <= 14; series += 2)
        {
            var rig = new SceneRig(campaign);
            var outcome = Prepare(rig, Math.Max(series, 1), 0, series);
            rig.Game.Session.State.CurrentSeries = (sbyte)series;
            Run(rig, async director =>
            {
                await director.FuneralSequenceAsync(playerFuneral: false);
                await director.FuneralSequenceAsync(playerFuneral: true);
            }, outcome, $"campaign {campaign} funerals series {series}");
        }
    }
}
