using WingCommander.Core.Resources;
using WingCommander.Game.Scenes;
using WingCommander.Tests;
using Xunit.Abstractions;

namespace WingCommander.Game.Tests.Screens.Scenes;

[Collection(SceneRig.Collection)]
public class BriefingSceneTests(ITestOutputHelper output)
{
    private const int Esc = 0x01;

    [DataFact]
    public void Briefing_of_the_first_mission_plays_to_the_end()
    {
        var rig = new SceneRig();
        rig.Start(director => director.BriefingAsync(1, 0));
        Assert.True(rig.RunWithSnapshots("briefing-1-0", 600_000, 1000), "the briefing did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        foreach (var record in rig.Director.PlayedRecords)
            output.WriteLine(record.ToString());
        // Records 0..23; record 24 ends the scene.
        Assert.Equal(Enumerable.Range(0, 24), rig.Director.PlayedRecords.Select(r => r.Index));
        Assert.Equal([0, 1, 2, 2, 20, 20, 20, 20, 21, 20, 29, 20, 3, 4, 4, 4, 21, 20, 20, 29, 2, 20, 1, 5],
            rig.Director.PlayedRecords.Select(r => r.Handler));
        Assert.False(rig.Director.Stage.SceneBuffer.IsAllocated);
        Assert.Equal(0, rig.Game.Graphics.Screen!.Top);
        Assert.Equal(199, rig.Game.Graphics.Screen!.Bottom);
    }

    [DataFact]
    public void Briefing_map_shows_the_mission_and_labels_the_objectives()
    {
        var rig = new SceneRig();
        rig.Start(director => director.BriefingAsync(1, 0));
        // Record 13 (the first map) starts after about 115 s; look at it 2 s later.
        Assert.False(rig.RunUntil(117_000));
        rig.Snapshot("briefing-map", 3);
        var mission = rig.Director.Mission!;
        Assert.Equal("Enyo", mission.SystemName);
        Assert.Equal("Alpha Wing", mission.MissionName);
        Assert.Equal(0, mission.CurrentObjective);
        var map = rig.Director.Map;
        var labels = Enumerable.Range(0, map.LabelCount).Select(map.LabelText).ToArray();
        output.WriteLine(string.Join(" | ", labels));
        Assert.Contains("Nav 1", labels);
        Assert.Contains("Tiger's Claw", labels);
        Assert.Contains("Asteroids", labels);
        // The map picture sits at screen row 4 (260 x 148 visible), the subtitle below row 152.
        Assert.Equal(4, rig.Game.Graphics.Screen!.Top);
        Assert.True(ScreenRig.CountNonBlack(rig.Screen.Front, 4, 151) > 5_000);
        Assert.True(ScreenRig.CountNonBlack(rig.Screen.Front, 152, 199) > 200);
    }

    [DataFact]
    public void Space_skips_record_by_record()
    {
        var rig = new SceneRig();
        rig.Start(director => director.BriefingAsync(1, 0));
        rig.PressSpaceEvery(500, 60_000, 300);
        Assert.True(rig.RunUntil(60_000), "the briefing did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        Assert.Equal(24, rig.Director.RecordsPlayed);
        Assert.True(rig.Screen.Runtime.Scheduler.Now < 20_000, $"skipping took {rig.Screen.Runtime.Scheduler.Now} ms");
    }

    [DataFact]
    public void Esc_ends_the_whole_briefing()
    {
        var rig = new SceneRig();
        rig.Start(director => director.BriefingAsync(1, 0));
        rig.Screen.Key(8_000, Esc, 0x1b);
        Assert.True(rig.RunUntil(20_000), "Esc did not end the briefing");
        Assert.Null(rig.Screen.Runtime.Failure);
        Assert.InRange(rig.Director.RecordsPlayed, 2, 4);
        Assert.False(rig.Game.Events.EscapePressed);
        Assert.False(rig.Director.Stage.SceneBuffer.IsAllocated);
    }

    [DataFact]
    public void Subtitles_expand_the_campaign_macros()
    {
        var rig = new SceneRig();
        var session = rig.Game.Session;
        session.Player.Name = "BLAIR";
        session.Player.Callsign = "MAVERICK";
        session.Player.Rank = 1;
        rig.Director.LoadMissionData(1, 0);
        var context = rig.Director.Context;
        Assert.Equal("Mission Briefing,\nEnyo System, 06:00 hours, 2654.110.",
            TextMacros.Expand("Mission Briefing,\n$S System, $T hours, $D.", context));
        Assert.Equal("MAVERICK, you're leading Alpha wing.", TextMacros.Expand("$C, you're leading Alpha wing.", context));
        Assert.Equal("1ST LT. BLAIR.", TextMacros.Expand("$R $N.", context));
        Assert.Equal("TANAKA", TextMacros.Expand("$W0", context));
    }

    [DataTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Rec_room_talk_runs_in_the_conversation_layout_and_restores_the_screen(int talk)
    {
        var rig = new SceneRig();
        var directory = GameData.Require();
        var script = BriefingFile.Load(directory, 0).GetMission(1, 0).RecRoom[talk];
        rig.Screen.Start(async game =>
        {
            var backdrop = game.Resources.GetShape(LogicalFile.RecRoomVga, 1);
            await game.Screens.PlayConversationAsync(2, script, backdrop);
        });
        Assert.False(rig.RunUntil(3_000));
        rig.Snapshot($"recroom-talk-{talk}", 2);
        Assert.Equal(24, rig.Game.Graphics.Screen!.Top);
        Assert.Equal(151, rig.Game.Graphics.Screen!.Bottom);
        Assert.True(ScreenRig.CountNonBlack(rig.Screen.Front, 24, 151) > 10_000);
        Assert.Equal(0, ScreenRig.CountNonBlack(rig.Screen.Front, 0, 23));
        Assert.True(rig.RunUntil(600_000), "the talk did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        Assert.True(rig.Director.RecordsPlayed > 0);
        Assert.Equal(0, rig.Game.Graphics.Screen!.Top);
        Assert.Equal(199, rig.Game.Graphics.Screen!.Bottom);
        Assert.False(rig.Director.Stage.SceneBuffer.IsAllocated);
    }
}
