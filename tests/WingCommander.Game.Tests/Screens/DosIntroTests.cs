using WingCommander.Game.Screens;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Screens;

public class DosIntroTests
{
    private static ScreenRig StartIntro(bool audio)
    {
        var rig = new ScreenRig(audio);
        rig.Start(DosIntro.PlayAsync);
        return rig;
    }

    [DataFact]
    public void Unsynchronised_intro_plays_every_stage_and_ends_black()
    {
        var rig = StartIntro(audio: false);
        Assert.True(rig.Runtime.RunHeadless(120_000), "the intro did not finish");
        Assert.Null(rig.Runtime.Failure);
        // 73 orchestra, 20 cue, 17 push and 41 logo frames, at least 11 firework frames, the
        // 8-frame final burst and the closing screen clear.
        Assert.InRange(rig.Game.Display.SlamCount, 171, 400);
        Assert.Equal(0, ScreenRig.CountNonBlack(rig.Front, 0, 199));
    }

    [DataFact]
    public void Intro_draws_only_into_screen_rows_24_to_151()
    {
        var rig = StartIntro(audio: false);
        Assert.False(rig.Runtime.RunHeadless(2_000));
        Assert.Equal(0, ScreenRig.CountNonBlack(rig.Front, 0, 23));
        Assert.Equal(0, ScreenRig.CountNonBlack(rig.Front, 152, 199));
        Assert.True(ScreenRig.CountNonBlack(rig.Front, 24, 151) > 10_000);
    }

    [DataFact]
    public void A_key_press_skips_the_rest_of_the_intro()
    {
        var rig = StartIntro(audio: false);
        rig.Key(1_000, 0x39, 0x20);
        Assert.True(rig.Runtime.RunHeadless(2_000), "the key press did not end the intro");
        Assert.Equal(0, ScreenRig.CountNonBlack(rig.Front, 0, 199));
    }

    [DataFact]
    public void Synchronised_intro_waits_for_the_music_cues()
    {
        var rig = StartIntro(audio: true);
        Assert.True(rig.Runtime.RunHeadless(120_000), "the intro did not finish");
        Assert.Null(rig.Runtime.Failure);
        Assert.NotNull(rig.Game.Audio);
        // The fireworks only finish after cue 5 of the 25 s intro track.
        Assert.True(rig.Runtime.Scheduler.Now > 20_000, $"the intro ended after {rig.Runtime.Scheduler.Now} ms");
    }
}
