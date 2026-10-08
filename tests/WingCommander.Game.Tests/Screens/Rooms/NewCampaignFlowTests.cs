using WingCommander.Game.Campaign;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Screens.Rooms;

/// <summary>GameMain through the rooms: new game, forced TrainSim with name entry, bar, barracks, save, quit.</summary>
public class NewCampaignFlowTests
{
    [DataFact]
    public void New_campaign_reaches_the_barracks_saves_and_quits()
    {
        var rig = new RoomScreenRig(new Wc1GameOptions { Audio = false, SkipIntro = true });
        var game = rig.Game;
        game.Start();
        rig.Key(1_000, 0x1f, 0x53);                     // title: S = new game
        Assert.False(rig.Runtime.RunHeadless(3_000));
        RoomsRig.Snap(rig, "flow-1-get-ready");
        Assert.True(game.Session.CampaignStartupMode);
        RoomsRig.Space(rig, 5_000);                      // placeholder TrainSim flight
        Assert.False(rig.Runtime.RunHeadless(11_000));
        RoomsRig.Snap(rig, "flow-2-name-entry");
        RoomsRig.Enter(rig, 11_500);                     // LAST NAME: Blair
        RoomsRig.Enter(rig, 12_500);                     // CALLSIGN : Maverick
        Assert.False(rig.Runtime.RunHeadless(13_500));
        RoomsRig.Snap(rig, "flow-3-ranking");
        RoomsRig.Space(rig, 14_000);                     // leave the ranking
        Assert.False(rig.Runtime.RunHeadless(16_000));
        RoomsRig.Snap(rig, "flow-4-rec-room");
        Assert.False(game.Session.CampaignStartupMode);
        Assert.NotNull(game.Session.Data);
        Assert.True(game.Session.CampaignActive);
        rig.Click(16_500, 300, 100);                     // barracks door
        Assert.False(rig.Runtime.RunHeadless(18_000));
        RoomsRig.Snap(rig, "flow-5-barracks");
        rig.Click(18_500, 120, 91);                      // save into bunk 0
        double at = RoomsRig.Type(rig, 19_500, "blair one");
        RoomsRig.Enter(rig, at += 200);
        rig.Click(at += 1_000, 233, 55);                 // airlock
        RoomsRig.Key(rig, at += 1_000, 'Y');
        Assert.True(rig.Runtime.RunHeadless(at + 2_000), "the game did not end");
        Assert.Null(rig.Runtime.Failure);

        var saved = SaveGameFile.Load(game.SaveGamePath, 0);
        Assert.NotNull(saved);
        Assert.Equal("BLAIR ONE", saved.Description);
        Assert.Equal("Blair", saved.Pilots[8].Name);
        Assert.Equal("Maverick", saved.Pilots[8].Callsign);
        Assert.Equal(1, saved.Campaign.CurrentSeries);
        Assert.Equal(0, saved.Campaign.CurrentMission);
        Assert.Equal(0, saved.Campaign.CampaignIndex);
    }

    [DataFact]
    public void Continue_opens_the_barracks_and_awakens_a_saved_game()
    {
        var rig = new RoomScreenRig(new Wc1GameOptions { Audio = false, SkipIntro = true });
        var game = rig.Game;
        var record = new CampaignSession().CreateSaveRecord("SAVED", new SavedObjective[16]);
        record.Campaign.CurrentSeries = 2;
        record.Campaign.CurrentMission = 1;
        record.Pilots[8].Name = "Saved";
        SaveGameFile.Save(game.SaveGamePath, 0, record);
        game.Start();
        rig.Click(1_000, 160, 110);                      // title: continue
        Assert.False(rig.Runtime.RunHeadless(3_000));
        RoomsRig.Snap(rig, "flow-continue-barracks");
        Assert.False(game.Session.CampaignActive);
        rig.Click(3_500, 143, 91);                        // wake bunk 0
        RoomsRig.Key(rig, 4_500, 'Y');
        rig.Click(6_000, 24, 60);                         // back to the bar
        Assert.False(rig.Runtime.RunHeadless(8_000));
        RoomsRig.Snap(rig, "flow-continue-rec-room");
        Assert.Null(rig.Runtime.Failure);
        Assert.True(game.Session.CampaignActive);
        Assert.False(game.Session.CampaignStartupMode);
        Assert.Equal(2, game.Session.State.CurrentSeries);
        Assert.Equal("Saved", game.Session.Player.Name);
    }
}
