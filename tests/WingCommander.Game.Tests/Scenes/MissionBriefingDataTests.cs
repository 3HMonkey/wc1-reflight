using WingCommander.Game.Campaign;
using WingCommander.Game.Scenes;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Scenes;

public class MissionBriefingDataTests
{
    private sealed class Outcome : IMissionOutcome
    {
        public int PlayerKills { get; set; }

        public int WingmanKills { get; set; }

        public HashSet<int> AchievedObjectives { get; } = [];

        public bool Achieved(int objective) => AchievedObjectives.Contains(objective);

        public bool Sighted(int objective) => false;
    }

    [DataFact]
    public void First_mission_of_the_vega_campaign()
    {
        var data = MissionBriefingData.Load(GameData.Require(), 0, 1, 0);
        Assert.NotNull(data);
        Assert.Equal("Enyo", data.SystemName);
        Assert.Equal("Alpha Wing", data.MissionName);
        Assert.Equal(0, data.PlayerShipType);        // Hornet
        Assert.Equal(0, data.PlayerMissionType);     // Patrol
        Assert.True(data.ObjectiveListCount >= 4);
        Assert.Equal("Nav 1", data.ObjectiveName(0));
        Assert.Equal("Tiger's Claw", data.ObjectiveName(data.ObjectiveListCount - 1));
        Assert.Equal("NONE", data.ObjectiveName(data.ObjectiveListCount));
        Assert.Equal(-1, data.Objectives[data.ObjectiveListCount].Type);
        Assert.Equal(0, data.CurrentObjective);
        Assert.Equal(0, data.CurrentNavPointIndex);
        Assert.False(data.HiddenObjective(0));
    }

    [DataTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Every_mission_with_a_briefing_has_mission_data(int campaign)
    {
        var directory = GameData.Require();
        var briefings = BriefingFile.Load(directory, campaign);
        int missions = 0;
        for (int series = 1; series <= 13; series++)
        {
            for (int mission = 0; mission < 4; mission++)
            {
                if (!briefings.HasMission(series, mission))
                    continue;
                var data = MissionBriefingData.Load(directory, campaign, series, mission);
                Assert.NotNull(data);
                missions++;
                Assert.False(string.IsNullOrEmpty(data.SystemName), $"{series}.{mission} has no system name");
                Assert.InRange(data.PlayerShipType, 0, 21); // Secret Missions 2 lets the player fly a captured Dralthi (10)
                Assert.InRange(data.ObjectiveListCount, 1, 16);
                for (int i = 0; i < data.ObjectiveListCount; i++)
                {
                    Assert.InRange(data.Objectives[i].Type, 0, 4);
                    Assert.NotNull(data.ObjectiveName(i));
                }
                for (int i = 0; data.FlightPath[i] != -1; i++)
                    Assert.InRange(data.FlightPath[i], 0, data.ObjectiveListCount - 1);
                Assert.InRange(data.CurrentObjective, 0, data.ObjectiveListCount - 1);
            }
        }
        Assert.True(missions >= 18);
    }

    [Fact]
    public void Scene_context_reads_the_campaign_state()
    {
        var session = new CampaignSession();
        var outcome = new Outcome { PlayerKills = 4, WingmanKills = 1 };
        var context = new CampaignSceneContext(session, outcome);
        session.State.AceFlags[2] = 0b0110;
        Assert.Equal(1, context.AceStatus(2, 2));
        Assert.Equal(1, context.AceStatus(2, 6));
        Assert.Equal(0, context.AceStatus(2, 1));
        Assert.Equal(0, context.AceStatus(9, 1));
        session.State.ElapsedDate = new CampaignDate(0x1e08, 2); // 08:30, two ejections
        Assert.Equal((8, 30), context.Time);
        Assert.Equal(2, context.EjectionCount);
        Assert.Equal(4, context.PlayerKills);
        Assert.Equal("ST.JOHN", context.WingmanName(1));
        Assert.Equal("2ND LT.", context.RankName);
        context.MedalIndex = 3;
        Assert.Equal("Golden Sun", context.MedalName);
        // No mission loaded: no objective counts, so "no objective achieved" holds.
        Assert.True(context.NoObjectivesAchieved());
        Assert.Equal(3, context.WingStatus(0));
        Assert.Equal(3, context.WingStatus(12));
    }

    [DataFact]
    public void No_objectives_achieved_counts_the_loaded_mission_objectives()
    {
        var session = new CampaignSession();
        var outcome = new Outcome();
        var context = new CampaignSceneContext(session, outcome)
        {
            Mission = MissionBriefingData.Load(GameData.Require(), 0, 1, 0),
        };
        Assert.True(context.NoObjectivesAchieved());
        outcome.AchievedObjectives.Add(2);
        Assert.False(context.NoObjectivesAchieved());
        outcome.AchievedObjectives.Clear();
        outcome.AchievedObjectives.Add(15);
        Assert.True(context.NoObjectivesAchieved()); // beyond the mission's objectives
    }
}
