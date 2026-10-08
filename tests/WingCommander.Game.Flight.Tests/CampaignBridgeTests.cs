using WingCommander.Game.Campaign;
using WingCommander.Game.Flight.Bridges;
using WingCommander.Simulation.Data;

namespace WingCommander.Game.Flight.Tests;

public class CampaignBridgeTests
{
    [Fact]
    public void Reads_and_writes_the_live_campaign_state()
    {
        var session = new CampaignSession();
        var bridge = new CampaignBridge(session);
        session.State.CurrentSeries = 3;
        session.State.CurrentMission = 2;
        bridge.PlayerShipType = ObjectType.Raptor;
        bridge.MissionScore = 120;
        bridge.PromotionScore = 5;
        bridge.SetPersonalityDeathMission(4, 14);
        bridge.SetAceFlags(2, 0x21);

        Assert.Equal((int)ObjectType.Raptor, session.State.PlayerShipType);
        Assert.Equal(120, session.State.MissionScore);
        Assert.Equal(5, session.State.PromotionScore);
        Assert.Equal(14, session.State.PersonalityDeathMission[4]);
        Assert.Equal(0x21, session.State.AceFlags[2]);
        Assert.Equal(3, bridge.CurrentSeries);
        Assert.Equal(2, bridge.CurrentMission);

        session.Reset(); // a new state object: the bridge follows it
        Assert.Equal(0, bridge.GetPersonalityDeathMission(4));
        Assert.Equal(session.State.CurrentSeries, bridge.CurrentSeries);
    }
}
