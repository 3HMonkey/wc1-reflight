using WingCommander.Tests;

namespace WingCommander.Game.Tests.Screens;

public class GameMainTests
{
    [DataFact]
    public void S_on_the_title_starts_a_new_campaign()
    {
        var rig = new ScreenRig(copySaves: false);
        var game = new Wc1Game(rig.Runtime, GameData.Require(), new Wc1GameOptions { Audio = false, SkipIntro = true });
        Assert.Null(game.FlightLayer);
        game.Start();
        rig.Key(1_000, 0x1f, 0x53); // S: start a new game
        Assert.False(rig.Runtime.RunHeadless(3_000), "the game ended");
        Assert.Null(rig.Runtime.Failure);
        // StartNewCampaign ran: the campaign is active and the forced first TrainSim session
        // (start-up mode) is waiting for the player.
        Assert.True(game.Session.CampaignActive);
        Assert.True(game.Session.CampaignStartupMode);
    }

    [DataFact]
    public void Alt_X_quits_the_game_normally()
    {
        var rig = new ScreenRig(copySaves: false);
        var game = new Wc1Game(rig.Runtime, GameData.Require(), new Wc1GameOptions { Audio = false, SkipIntro = true });
        game.Start();
        rig.Runtime.Events.EnqueueHostEvent(new WingCommander.Core.Platform.HostInputEvent(WingCommander.Core.Platform.HostInputKind.KeyDown, 0x2d, 0x58, 0, 0, 0, WingCommander.Core.Platform.HostModifiers.Alt, false), 1_000);
        Assert.True(rig.Runtime.RunHeadless(3_000), "Alt+X did not end the game");
        Assert.Null(rig.Runtime.Failure); // GameExitException is a normal end
    }
}
