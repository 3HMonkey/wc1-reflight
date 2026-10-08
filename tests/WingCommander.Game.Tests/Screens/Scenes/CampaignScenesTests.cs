using WingCommander.Game.Campaign;
using WingCommander.Game.Flow;
using WingCommander.Game.Screens;
using WingCommander.Tests;
using Xunit.Abstractions;

namespace WingCommander.Game.Tests.Screens.Scenes;

/// <summary>
/// Whole campaigns through <see cref="CampaignFlow"/> with the real scenes: rooms and flight are
/// scripted, every scene member (briefing, scramble, debriefing, funeral, office, medal ceremony,
/// MIDGAME transition, endings) runs the ported implementation.
/// </summary>
[Collection(SceneRig.Collection)]
public class CampaignScenesTests(ITestOutputHelper output)
{
    /// <summary>Scripted rooms and flight; the scenes are the real <see cref="GameFlowScreens"/> members.</summary>
    private sealed class ScenesWithScriptedFlight(Wc1Game game, bool win) : IGameFlowScreens
    {
        private GameFlowScreens Real => game.Screens;

        public List<string> Log { get; } = [];

        public int WingmanDeaths { get; private set; }

        private async Task Take(double milliseconds) => await game.Scheduler.Delay(milliseconds);

        public async Task<int> RecRoomAsync()
        {
            await Take(100);
            return 4;
        }

        public Task RunTrainSimAsync() => Take(100);

        public async Task<int> BarracksScreenAsync()
        {
            await Take(100);
            return BarracksResult.LaunchMission;
        }

        public void PumpWindowMessages() => game.Events.PumpWindowMessages();

        public async Task BriefingAsync(int series, int mission)
        {
            Log.Add($"briefing {series}.{mission}");
            await Real.BriefingAsync(series, mission);
        }

        public Task PlayScrambleHangarSceneAsync() => Real.PlayScrambleHangarSceneAsync();

        public int BriefedPlayerShipType => Real.Director.Mission?.PlayerShipType ?? 0;

        public Task ScrambleAsync() => Take(100);

        public async Task<FlightResult> FlyMissionAsync(int series, int mission)
        {
            await Take(1000);
            var session = game.Session;
            // A wingman dies on the second mission of every second series.
            session.WingmanKilledThisMission = mission == 1 && series % 2 == 0;
            if (session.WingmanKilledThisMission)
            {
                int personality = (series / 2) % 8;
                if (session.State.PersonalityDeathMission[personality] == 0)
                {
                    session.WingmanKilled(personality);
                    WingmanDeaths++;
                }
            }
            session.State.MissionScore = (short)(win ? 40 : 5);
            session.MissionMedalScore = (short)(win ? 300 : 0);
            return FlightResult.Landed;
        }

        public Task LandingSequenceAsync() => Take(100);

        public Task<bool> EjectionSequenceAsync() => Task.FromResult(false);

        public Task StrandedSequenceAsync() => Take(100);

        public Task DeathSequenceAsync() => Take(100);

        public void AbortFlight()
        {
        }

        public MissionStatistics MissionStatistics => new((short)(win ? 3 : 0), 1, 0);

        public bool ObjectiveAchieved(int objective) => win;

        public async Task DebriefingAsync(int series, int mission)
        {
            Log.Add("debriefing");
            await Real.DebriefingAsync(series, mission);
        }

        public async Task AwardCampaignMedalAsync(int medal)
        {
            Log.Add($"medal {medal}");
            await Real.AwardCampaignMedalAsync(medal);
        }

        public async Task CampaignVictorySequenceAsync()
        {
            Log.Add("victory");
            await Real.CampaignVictorySequenceAsync();
        }

        public async Task TigerClawEscapeSceneAsync()
        {
            Log.Add("escape");
            await Real.TigerClawEscapeSceneAsync();
        }

        public async Task MeanwhileTransitionAsync(int sequence, bool seriesFailed)
        {
            Log.Add($"meanwhile {sequence} {seriesFailed}");
            await Real.MeanwhileTransitionAsync(sequence, seriesFailed);
        }

        public async Task TheEndScreenAsync(bool fireworks)
        {
            Log.Add($"the end {fireworks}");
            await Real.TheEndScreenAsync(fireworks);
        }

        public async Task WingmanFuneralAsync()
        {
            Log.Add("funeral");
            await Real.WingmanFuneralAsync();
        }

        public async Task OfficeAsync()
        {
            Log.Add("office");
            await Real.OfficeAsync();
        }
    }

    private (SceneRig Rig, ScenesWithScriptedFlight Screens, Func<bool> Finished) RunCampaign(bool win)
    {
        var rig = new SceneRig();
        var screens = new ScenesWithScriptedFlight(rig.Game, win);
        bool finished = false;
        rig.Screen.Start(async game =>
        {
            var flow = new CampaignFlow(game.Session, screens, game.Random, game.Directory);
            await flow.StartNewCampaignAsync(0);
            int missions = 0;
            while (await flow.RunAsync())
            {
                if (++missions > 40)
                    throw new InvalidOperationException("The campaign did not end.");
            }
            finished = true;
        });
        // Space every 200 ms skips every skippable record; the end screen and the medals wait for it too.
        rig.PressSpaceEvery(500, 3_000_000, 200);
        return (rig, screens, () => finished);
    }

    [DataFact]
    public void Winning_campaign_plays_every_scene_up_to_the_victory_and_the_end()
    {
        var (rig, screens, finished) = RunCampaign(win: true);
        Assert.True(rig.RunUntil(3_000_000), "the campaign did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        Assert.True(finished());
        output.WriteLine(string.Join(Environment.NewLine, screens.Log));
        output.WriteLine($"{rig.Screen.Runtime.Scheduler.Now / 1000:0} s of virtual time, {screens.WingmanDeaths} wingman deaths");
        Assert.Contains("victory", screens.Log);
        Assert.Equal("the end True", screens.Log[^1]);
        Assert.Contains(screens.Log, entry => entry.StartsWith("meanwhile", StringComparison.Ordinal));
        Assert.Contains("funeral", screens.Log);
        Assert.Contains("office", screens.Log);
        Assert.Contains(screens.Log, entry => entry.StartsWith("medal", StringComparison.Ordinal));
        Assert.Equal(18, screens.Log.Count(entry => entry.StartsWith("briefing", StringComparison.Ordinal)));
        Assert.False(rig.Game.Session.CampaignActive);
        Assert.Equal(0, rig.Game.Graphics.Screen!.Top);
        Assert.False(rig.Director.Stage.SceneBuffer.IsAllocated);
    }

    [DataFact]
    public void Losing_campaign_plays_every_scene_up_to_the_escape_and_the_end()
    {
        var (rig, screens, finished) = RunCampaign(win: false);
        Assert.True(rig.RunUntil(3_000_000), "the campaign did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        Assert.True(finished());
        output.WriteLine(string.Join(Environment.NewLine, screens.Log));
        Assert.Contains("escape", screens.Log);
        Assert.Equal("the end False", screens.Log[^1]);
        Assert.Contains(screens.Log, entry => entry.StartsWith("meanwhile", StringComparison.Ordinal) && entry.EndsWith("True", StringComparison.Ordinal));
    }
}
