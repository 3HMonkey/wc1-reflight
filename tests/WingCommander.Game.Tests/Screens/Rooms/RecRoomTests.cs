using WingCommander.Game.Campaign;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Screens.Rooms;

public class RecRoomTests
{
    private static (RoomScreenRig Rig, Func<int> Result) StartRecRoom(Action<RoomScreenRig>? setup = null)
    {
        var rig = RoomsRig.CreateCampaign();
        setup?.Invoke(rig);
        int result = int.MinValue;
        rig.Start(async game => result = await game.Screens.RecRoomAsync());
        return (rig, () => result);
    }

    [DataFact]
    public void Bar_is_drawn_and_waits_for_input()
    {
        var (rig, _) = StartRecRoom();
        Assert.False(rig.Runtime.RunHeadless(3_000), "the rec room returned without input");
        Assert.Null(rig.Runtime.Failure);
        RoomsRig.Snap(rig, "recroom-series1-mission0");
        Assert.True(ScreenRig.CountNonBlack(rig.Front, 0, 199) > 40_000);
        Assert.Equal(1, rig.Game.Events.CursorShowCount);
    }

    [DataFact]
    public void Barracks_door_returns_4_and_clears_the_screen()
    {
        var (rig, result) = StartRecRoom();
        rig.Click(1_500, 300, 100);
        Assert.True(rig.Runtime.RunHeadless(4_000), "the door did not leave the room");
        Assert.Null(rig.Runtime.Failure);
        Assert.Equal(4, result());
        Assert.Equal(0, ScreenRig.CountNonBlack(rig.Front, 0, 199));
        Assert.Equal(0, rig.Game.Events.CursorShowCount);
    }

    [DataFact]
    public void Simulator_console_returns_5()
    {
        var (rig, result) = StartRecRoom();
        rig.Click(1_500, 60, 150);
        Assert.True(rig.Runtime.RunHeadless(4_000));
        Assert.Equal(5, result());
    }

    [DataFact]
    public void Keyboard_moves_the_pointer_and_enter_activates()
    {
        var (rig, result) = StartRecRoom();
        // The pointer starts at (160, 100); 15 steps of 8 pixels right reach the door (x >= 275).
        double at = 1_000;
        for (int i = 0; i < 15; i++, at += 120)
            rig.Key(at, 0x4d, 0x27);
        RoomsRig.Enter(rig, at + 300);
        Assert.True(rig.Runtime.RunHeadless(at + 3_000), "Enter did not activate the door");
        Assert.Equal(4, result());
    }

    [DataFact]
    public void Pointer_over_a_region_shows_its_label()
    {
        var (rig, _) = StartRecRoom();
        Assert.False(rig.Runtime.RunHeadless(1_500));
        int blank = RoomsRig.CountColour(rig, 15, 0, 187, 319, 199);
        RoomsRig.Move(rig, 1_600, 215, 60); // chalkboard: "Check pilot scores"
        Assert.False(rig.Runtime.RunHeadless(3_000));
        RoomsRig.Snap(rig, "recroom-label-chalkboard");
        int labelled = RoomsRig.CountColour(rig, 15, 0, 187, 319, 199);
        Assert.True(labelled > blank + 50, $"label pixels {labelled} vs blank {blank}");
        RoomsRig.Move(rig, 3_100, 160, 20); // no region: the label disappears again
        Assert.False(rig.Runtime.RunHeadless(4_500));
        Assert.Equal(blank, RoomsRig.CountColour(rig, 15, 0, 187, 319, 199));
    }

    [DataFact]
    public void Chalkboard_shows_the_kill_board_until_a_key()
    {
        var (rig, result) = StartRecRoom();
        rig.Click(1_500, 215, 60);
        Assert.False(rig.Runtime.RunHeadless(3_000));
        RoomsRig.Snap(rig, "recroom-killboard");
        // The board is a full-screen picture: the bar's simulator console is gone.
        int boardPixels = ScreenRig.CountNonBlack(rig.Front, 0, 199);
        Assert.True(boardPixels > 20_000);
        RoomsRig.Space(rig, 3_500);
        Assert.False(rig.Runtime.RunHeadless(5_000));
        RoomsRig.Snap(rig, "recroom-after-killboard");
        rig.Click(5_500, 300, 100);
        Assert.True(rig.Runtime.RunHeadless(8_000));
        Assert.Equal(4, result());
        Assert.Null(rig.Runtime.Failure);
    }

    [DataFact]
    public void Kill_board_orders_pilots_by_kills()
    {
        var (rig, _) = StartRecRoom(r =>
        {
            var pilots = r.Game.Session.Pilots;
            pilots[8].Kills = 99;
            pilots[8].Missions = 3;
            r.Game.Session.State.PersonalityDeathMission[3] = 6; // Iceman is dead: KIA
        });
        rig.Click(1_500, 215, 60);
        Assert.False(rig.Runtime.RunHeadless(3_000));
        RoomsRig.Snap(rig, "recroom-killboard-player-first");
        var order = rig.Game.Screens.RoomState.ChalkBoardPilotOrder;
        Assert.Equal(8, order[0]); // 99 kills
        Assert.Equal(3, order[1]); // Iceman 43 kills
        Assert.Equal(2, order[2]); // Bossman 37
        Assert.Equal(5, order[3]); // Paladin 34
    }

    [DataFact]
    public void Talking_to_a_pilot_plays_the_conversation_and_returns_to_the_bar()
    {
        var (rig, result) = StartRecRoom();
        Assert.False(rig.Runtime.RunHeadless(1_000));
        // Series 1 mission 0 seats Paladin (left) and Angel (right).
        Assert.Equal("Talk to PALADIN.", rig.Game.Screens.RoomState.TalkToFirstPilot);
        Assert.Equal("Talk to ANGEL.", rig.Game.Screens.RoomState.TalkToSecondPilot);
        rig.Click(1_200, 170, 85);
        Assert.False(rig.Runtime.RunHeadless(2_500));
        RoomsRig.Snap(rig, "recroom-talk-paladin");
        // Leave the conversation (Esc ends a scene; the placeholder takes any key). Only while it runs:
        // in the bar, Esc opens the pause menu.
        for (int i = 0; i < 6 && rig.Game.Graphics.Screen!.Top != 0; i++)
        {
            RoomsRig.Escape(rig, 3_000 + i * 700);
            Assert.False(rig.Runtime.RunHeadless(3_000 + i * 700 + 650));
        }
        Assert.False(rig.Runtime.RunHeadless(9_000));
        Assert.Null(rig.Runtime.Failure);
        RoomsRig.Snap(rig, "recroom-after-talk");
        Assert.Equal(0, rig.Game.Graphics.Screen!.Top);
        Assert.Equal(199, rig.Game.Graphics.Screen!.Bottom);
        rig.Click(9_500, 300, 100);
        Assert.True(rig.Runtime.RunHeadless(12_000));
        Assert.Equal(4, result());
    }

    [DataFact]
    public void Pan_transition_fades_the_room_in_once()
    {
        var (rig, result) = StartRecRoom(r => r.Game.Flow.PanRoomTransition = true);
        Assert.False(rig.Runtime.RunHeadless(3_000));
        Assert.False(rig.Game.Flow.PanRoomTransition);
        RoomsRig.Snap(rig, "recroom-after-pan");
        Assert.True(ScreenRig.CountNonBlack(rig.Front, 0, 199) > 40_000);
        rig.Click(3_500, 300, 100);
        Assert.True(rig.Runtime.RunHeadless(6_000));
        Assert.Equal(4, result());
    }

    [DataFact]
    public void Dead_pilots_are_not_seated()
    {
        var (rig, _) = StartRecRoom(r =>
        {
            r.Game.Session.State.PersonalityDeathMission[5] = 4; // Paladin
            r.Game.Session.State.PersonalityDeathMission[4] = 4; // Angel
        });
        Assert.False(rig.Runtime.RunHeadless(2_000));
        RoomsRig.Snap(rig, "recroom-empty-table");
        Assert.Equal("", rig.Game.Screens.RoomState.TalkToFirstPilot);
        Assert.Equal("", rig.Game.Screens.RoomState.TalkToSecondPilot);
    }

    [DataFact]
    public void Every_campaign_mission_draws_the_bar()
    {
        for (int campaign = 0; campaign < 3; campaign++)
        {
            var data = CampaignFile.Load(Core.Resources.GameDirectory.Open(GameData.Require().DataPath), campaign);
            for (int series = 1; series <= 13; series++)
            {
                var record = data.GetSeries(series);
                for (int mission = 0; mission < record.MissionCount; mission++)
                {
                    var rig = new RoomScreenRig();
                    var session = rig.Game.Session;
                    session.LoadCampaignData(rig.Game.Directory, campaign);
                    session.CampaignActive = true;
                    session.State.CampaignIndex = (short)campaign;
                    session.State.CurrentSeries = (sbyte)series;
                    session.State.CurrentMission = (sbyte)mission;
                    int result = 0;
                    rig.Start(async game => result = await game.Screens.RecRoomAsync());
                    rig.Click(800, 300, 100);
                    Assert.True(rig.Runtime.RunHeadless(3_000), $"campaign {campaign} series {series} mission {mission}");
                    Assert.Null(rig.Runtime.Failure);
                    Assert.Equal(4, result);
                }
            }
        }
    }
}
