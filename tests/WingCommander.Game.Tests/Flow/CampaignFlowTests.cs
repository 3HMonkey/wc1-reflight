using WingCommander.Core.Numerics;
using WingCommander.Core.Runtime;
using WingCommander.Game.Campaign;
using WingCommander.Game.Flow;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Flow;

public class CampaignFlowTests
{
    /// <summary>Screens that take some virtual time and then report scripted results.</summary>
    private sealed class ScriptedScreens(GameScheduler scheduler, CampaignSession session, bool winEverything) : IGameFlowScreens
    {
        public List<string> Log { get; } = [];
        public List<int> SeriesFlown { get; } = [];
        public FlightResult NextFlight { get; set; } = FlightResult.Landed;

        private async Task Take(double ms, string? entry = null)
        {
            await scheduler.Delay(ms);
            if (entry is not null)
                Log.Add(entry);
        }

        public async Task<int> RecRoomAsync()
        {
            await Take(500);
            return 4;
        }

        public Task RunTrainSimAsync() => Take(1000, "trainsim");

        public async Task<int> BarracksScreenAsync()
        {
            await Take(500);
            return BarracksResult.LaunchMission;
        }

        public void PumpWindowMessages() { }
        public Task BriefingAsync(int series, int mission) => Take(2000, $"briefing {series}.{mission}");
        public Task PlayScrambleHangarSceneAsync() => Take(300);
        public int BriefedPlayerShipType => session.State.PlayerShipType;
        public Task ScrambleAsync() => Take(300);

        public async Task<FlightResult> FlyMissionAsync(int series, int mission)
        {
            if (mission == 0)
                SeriesFlown.Add(series);
            await Take(60_000);
            return NextFlight;
        }

        public Task LandingSequenceAsync() => Take(400);
        public async Task<bool> EjectionSequenceAsync()
        {
            await Take(400);
            return false;
        }

        public Task StrandedSequenceAsync() => Take(400, "stranded");
        public Task DeathSequenceAsync() => Take(400, "death");
        public void AbortFlight() => Log.Add("abort");
        public MissionStatistics MissionStatistics => new(2, 1, 0);
        public bool ObjectiveAchieved(int objective) => winEverything;
        public Task DebriefingAsync(int series, int mission) => Take(1500);
        public Task AwardCampaignMedalAsync(int medal) => Take(800, $"medal {medal}");
        public Task CampaignVictorySequenceAsync() => Take(5000, "victory");
        public Task TigerClawEscapeSceneAsync() => Take(5000, "escape");
        public Task MeanwhileTransitionAsync(int sequence, bool seriesFailed) => Take(3000, $"meanwhile {sequence} {seriesFailed}");
        public Task TheEndScreenAsync(bool fireworks) => Take(2000, $"the end {fireworks}");
        public Task WingmanFuneralAsync() => Take(3000, "funeral");
        public Task OfficeAsync() => Take(1000, "office");
    }

    private sealed record Rig(GameScheduler Scheduler, CampaignFlow Flow, ScriptedScreens Screens, CampaignSession Session)
    {
        public T Run<T>(Func<Task<T>> coroutine)
        {
            var task = Scheduler.Start(coroutine);
            Assert.True(Scheduler.RunToCompletion(task, double.MaxValue));
            return task.Result;
        }
    }

    private static Rig Start(bool win)
    {
        var scheduler = new GameScheduler();
        var random = new CRandom(1234);
        var session = new CampaignSession(random);
        var screens = new ScriptedScreens(scheduler, session, win);
        var flow = new CampaignFlow(session, screens, random, GameData.Require());
        var start = scheduler.Start(() => flow.StartNewCampaignAsync(0));
        Assert.True(scheduler.RunToCompletion(start, double.MaxValue));
        return new Rig(scheduler, flow, screens, session);
    }

    private static int RunToEnd(Rig rig)
    {
        int missions = 0;
        while (rig.Run(rig.Flow.RunAsync))
        {
            missions++;
            Assert.True(missions < 40, "campaign did not end");
        }
        return missions + 1;
    }

    [DataFact]
    public void Winning_every_mission_follows_the_victory_path()
    {
        var rig = Start(win: true);
        int missions = RunToEnd(rig);
        Assert.Equal([1, 2, 4, 7, 9, 12], rig.Screens.SeriesFlown);
        Assert.Equal(18, missions);
        Assert.Contains("victory", rig.Screens.Log);
        Assert.Equal("the end True", rig.Screens.Log[^1]);
        Assert.False(rig.Session.CampaignActive);
        Assert.Equal(18, rig.Session.Player.Missions);
        Assert.True(rig.Scheduler.Now > 18 * 60_000);   // the whole campaign ran on the virtual clock
    }

    [DataFact]
    public void Losing_every_mission_ends_with_the_escape()
    {
        var rig = Start(win: false);
        RunToEnd(rig);
        Assert.Equal([1, 3, 6, 8, 11, 13], rig.Screens.SeriesFlown);
        Assert.Contains("escape", rig.Screens.Log);
        Assert.Equal("the end False", rig.Screens.Log[^1]);
    }

    [DataFact]
    public void Ejection_counts_and_awards_the_golden_sun_once()
    {
        var rig = Start(win: true);
        rig.Screens.NextFlight = FlightResult.Ejected;
        Assert.True(rig.Run(rig.Flow.RunAsync));
        Assert.Equal(1, rig.Session.State.ElapsedDate.Year);
        Assert.Contains("medal 3", rig.Screens.Log);
        Assert.Contains("office", rig.Screens.Log);
        rig.Screens.Log.Clear();
        Assert.True(rig.Run(rig.Flow.RunAsync));
        Assert.Equal(2, rig.Session.State.ElapsedDate.Year);
        Assert.DoesNotContain("medal 3", rig.Screens.Log);
    }

    [DataFact]
    public void Death_ends_the_campaign()
    {
        var rig = Start(win: true);
        rig.Screens.NextFlight = FlightResult.Killed;
        Assert.False(rig.Run(rig.Flow.RunAsync));
        Assert.Contains("death", rig.Screens.Log);
        Assert.False(rig.Session.CampaignActive);
    }
}
