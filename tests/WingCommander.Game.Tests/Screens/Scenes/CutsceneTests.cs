using WingCommander.Game.Screens.Scenes;
using WingCommander.Tests;
using Xunit.Abstractions;

namespace WingCommander.Game.Tests.Screens.Scenes;

/// <summary>Every cutscene runs to its end on the virtual clock, without exceptions, and can be skipped.</summary>
[Collection(SceneRig.Collection)]
public class CutsceneTests(ITestOutputHelper output)
{
    private const int Esc = 0x01;

    private void Report(SceneDirector director)
    {
        foreach (var record in director.PlayedRecords)
            output.WriteLine(record.ToString());
    }

    private static void AssertLayoutReleased(SceneRig rig)
    {
        Assert.False(rig.Director.Stage.SceneBuffer.IsAllocated);
        Assert.Equal(0, rig.Game.Graphics.Screen!.Top);
        Assert.Equal(199, rig.Game.Graphics.Screen!.Bottom);
    }

    [DataFact]
    public void Debriefing_plays_to_the_end()
    {
        var rig = new SceneRig();
        rig.Start(director => director.DebriefingAsync(1, 0));
        Assert.True(rig.RunWithSnapshots("debriefing-1-0", 600_000, 1000), "the debriefing did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        Report(rig.Director);
        Assert.True(rig.Director.RecordsPlayed > 5);
        AssertLayoutReleased(rig);
    }

    [DataFact]
    public void Office_plays_to_the_end()
    {
        var rig = new SceneRig();
        rig.Game.Session.PromotionPending = true;
        rig.Start(director => director.OfficeAsync());
        Assert.True(rig.RunWithSnapshots("office", 600_000, 1000), "the office scene did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        Report(rig.Director);
        AssertLayoutReleased(rig);
    }

    [DataFact]
    public void Medal_ceremony_plays_to_the_end()
    {
        var rig = new SceneRig();
        rig.Start(director => director.AwardCampaignMedalAsync(1));
        Assert.True(rig.RunWithSnapshots("medal-1", 600_000, 1000), "the medal ceremony did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        Report(rig.Director);
        AssertLayoutReleased(rig);
    }

    [DataTheory]
    [InlineData("debriefing", 13)]
    [InlineData("office", 10)]
    [InlineData("medal", 8)]
    public void Space_skips_each_record_of_a_scene(string scene, int records)
    {
        var rig = new SceneRig();
        rig.Game.Session.PromotionPending = true;
        rig.Start(director => scene switch
        {
            "debriefing" => director.DebriefingAsync(1, 0),
            "office" => director.OfficeAsync(),
            _ => director.AwardCampaignMedalAsync(0),
        });
        rig.PressSpaceEvery(500, 60_000, 250);
        Assert.True(rig.RunUntil(60_000), $"the {scene} did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        Assert.Equal(records, rig.Director.RecordsPlayed);
        Assert.True(rig.Screen.Runtime.Scheduler.Now < 15_000, $"skipping took {rig.Screen.Runtime.Scheduler.Now} ms");
    }

    [DataFact]
    public void Wingman_funeral_plays_to_the_end_and_fades_out()
    {
        var rig = new SceneRig();
        rig.Start(director => director.FuneralSequenceAsync(playerFuneral: false));
        Assert.True(rig.RunWithSnapshots("funeral-wingman", 900_000, 1000), "the funeral did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        Report(rig.Director);
        AssertLayoutReleased(rig);
        Assert.Equal(0, ScreenRig.CountNonBlack(rig.Screen.Front, 0, 199));
        Assert.False(rig.Director.FuneralSequenceActive);
    }

    [DataFact]
    public void Player_funeral_plays_to_the_end()
    {
        var rig = new SceneRig();
        rig.Game.Session.State.CurrentSeries = 7;
        rig.Start(director => director.FuneralSequenceAsync(playerFuneral: true));
        Assert.True(rig.RunWithSnapshots("funeral-player", 900_000, 1000), "the funeral did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        Report(rig.Director);
        AssertLayoutReleased(rig);
    }

    [DataFact]
    public void With_music_the_funeral_casket_drifts_until_the_funeral_march_ends()
    {
        var rig = new SceneRig(audio: true);
        rig.Start(director => director.FuneralSequenceAsync(playerFuneral: false));
        Assert.NotNull(rig.Game.Audio);
        rig.PressSpaceEvery(1_000, 400_000, 200);
        Assert.True(rig.RunUntil(900_000), "the funeral did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        Assert.Equal(1, rig.Game.Audio!.Music.MusicTrackComplete);
        output.WriteLine($"ended after {rig.Screen.Runtime.Scheduler.Now / 1000:0.0} s");
    }

    [DataFact]
    public void Briefing_music_switches_from_the_middle_to_the_end_theme()
    {
        var rig = new SceneRig(audio: true);
        rig.Start(director => director.BriefingAsync(1, 0));
        var music = rig.Game.Audio!.Music;
        Assert.False(rig.RunUntil(2_000));
        Assert.Equal(25, music.CurrentMusicTrack);
        rig.PressSpaceEvery(2_500, 30_000, 300);
        double t = 2_000;
        while (!rig.Director.PlayedRecords.Any(r => r.Handler == 5) && t < 60_000)
            Assert.False(rig.RunUntil(t += 20));
        Assert.Equal(26, music.CurrentMusicTrack); // ReturnToBriefingLongShot ("Squadron dismissed")
        Assert.True(rig.RunUntil(60_000));
        Assert.Null(rig.Screen.Runtime.Failure);
        // Skipping the last shot stops the music, like the original.
        Assert.Equal(-1, music.CurrentMusicTrack);
    }

    [DataFact]
    public void Esc_ends_the_funeral_early()
    {
        var rig = new SceneRig();
        rig.Start(director => director.FuneralSequenceAsync(playerFuneral: false));
        rig.Screen.Key(3_000, Esc, 0x1b);
        Assert.True(rig.RunUntil(10_000), "Esc did not end the funeral");
        Assert.Null(rig.Screen.Runtime.Failure);
        AssertLayoutReleased(rig);
    }

    [DataFact]
    public void Scramble_hangar_scene_plays_72_frames()
    {
        var rig = new SceneRig();
        rig.Start(director => director.PlayScrambleHangarSceneAsync());
        Assert.True(rig.RunWithSnapshots("scramble", 60_000, 250), "the hangar scene did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        // 72 frames at 16 fps.
        Assert.InRange(rig.Screen.Runtime.Scheduler.Now, 4_000, 6_000);
        AssertLayoutReleased(rig);
    }

    [DataFact]
    public void Esc_skips_the_scramble()
    {
        var rig = new SceneRig();
        rig.Start(director => director.PlayScrambleHangarSceneAsync());
        rig.Screen.Key(500, Esc, 0x1b);
        Assert.True(rig.RunUntil(60_000));
        Assert.InRange(rig.Screen.Runtime.Scheduler.Now, 400, 1_500);
    }

    [DataFact]
    public void Victory_sequence_plays_to_the_end()
    {
        var rig = new SceneRig();
        rig.Start(director => director.CampaignVictorySequenceAsync());
        Assert.True(rig.RunWithSnapshots("victory", 300_000, 1000), "the victory sequence did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        Assert.Equal(0, ScreenRig.CountNonBlack(rig.Screen.Front, 0, 199));
    }

    [DataFact]
    public void Escape_scene_plays_to_the_end()
    {
        var rig = new SceneRig();
        rig.Start(director => director.TigerClawEscapeSceneAsync());
        Assert.True(rig.RunWithSnapshots("escape", 300_000, 1000), "the escape scene did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        // 260 frames at 16 fps.
        Assert.InRange(rig.Screen.Runtime.Scheduler.Now, 15_000, 18_000);
    }

    [DataTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_end_screen_shows_the_medals_then_plays_320_frames(bool fireworks)
    {
        var rig = new SceneRig();
        rig.Game.Session.State.Medals[0] = 2;
        rig.Game.Session.State.Medals[4] = 1;
        rig.Game.Session.Player.Rank = 2;
        rig.Start(director => director.TheEndScreenAsync(fireworks));
        rig.Screen.Key(5_000, 0x39, 0x20); // leaves the decorations
        Assert.True(rig.RunWithSnapshots(fireworks ? "theend-fireworks" : "theend", 120_000, 1000), "the end screen did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        AssertLayoutReleased(rig);
    }
}
