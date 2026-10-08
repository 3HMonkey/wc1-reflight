using WingCommander.Game.Campaign;
using WingCommander.Game.Flow;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Screens.Rooms;

public class BarracksTests
{
    // Click positions inside the barracks regions (aBarracksMenuRegions).
    private static readonly (int X, int Y) Bunk0Awaken = (143, 91);   // region 0
    private static readonly (int X, int Y) Bunk0Save = (120, 91);     // region 1
    private static readonly (int X, int Y) Bunk1Save = (195, 91);     // region 3
    private static readonly (int X, int Y) Bunk2Awaken = (140, 102);  // region 4
    private static readonly (int X, int Y) Bunk3Save = (205, 102);    // region 7
    private static readonly (int X, int Y) Hangar = (300, 60);        // region 16
    private static readonly (int X, int Y) Bar = (24, 60);            // region 17
    private static readonly (int X, int Y) Airlock = (233, 55);       // region 18
    private static readonly (int X, int Y) Medals = (130, 60);        // region 19

    private static (RoomScreenRig Rig, Func<int> Result) StartBarracks(bool campaignActive = true, Action<RoomScreenRig>? setup = null)
    {
        var rig = RoomsRig.CreateCampaign(campaignActive);
        setup?.Invoke(rig);
        int result = int.MinValue;
        rig.Start(async game => result = await game.Screens.BarracksScreenAsync());
        return (rig, () => result);
    }

    private static void Click(RoomScreenRig rig, double at, (int X, int Y) point) => rig.Click(at, point.X, point.Y);

    private static SaveGameSlot? Slot(RoomScreenRig rig, int slot) => SaveGameFile.Load(rig.Game.SaveGamePath, slot);

    [DataFact]
    public void Barracks_are_drawn_and_wait_for_input()
    {
        var (rig, _) = StartBarracks();
        Assert.False(rig.Runtime.RunHeadless(3_000), "the barracks returned without input");
        Assert.Null(rig.Runtime.Failure);
        RoomsRig.Snap(rig, "barracks-empty");
        Assert.True(ScreenRig.CountNonBlack(rig.Front, 0, 199) > 40_000);
        Assert.Equal(1, rig.Game.Events.CursorShowCount);
    }

    [DataFact]
    public void Mission_hangar_returns_7()
    {
        var (rig, result) = StartBarracks();
        Click(rig, 1_500, Hangar);
        Assert.True(rig.Runtime.RunHeadless(4_000));
        Assert.Null(rig.Runtime.Failure);
        Assert.Equal(BarracksResult.LaunchMission, result());
        Assert.Equal(0, rig.Game.Events.CursorShowCount);
    }

    [DataFact]
    public void Return_to_the_bar_returns_8()
    {
        var (rig, result) = StartBarracks();
        Click(rig, 1_500, Bar);
        Assert.True(rig.Runtime.RunHeadless(4_000));
        Assert.Equal(BarracksResult.ReturnToBar, result());
    }

    [DataFact]
    public void Without_a_campaign_every_door_asks_to_load_a_game_first()
    {
        var (rig, _) = StartBarracks(campaignActive: false);
        Click(rig, 1_500, Hangar);
        Assert.False(rig.Runtime.RunHeadless(3_000));
        RoomsRig.Snap(rig, "barracks-load-a-game-first");
        // The blue message box with its red border sits on rows 40..60.
        Assert.True(RoomsRig.CountColour(rig, 0x25, 24, 40, 296, 60) > 1_000);
        RoomsRig.Space(rig, 3_200);
        Click(rig, 4_000, Bar);
        RoomsRig.Space(rig, 5_500);
        Click(rig, 6_500, Bunk0Save);
        RoomsRig.Space(rig, 8_000);
        Assert.False(rig.Runtime.RunHeadless(9_500), "the barracks must not be left without a campaign");
        Assert.Null(rig.Runtime.Failure);
        Assert.Equal(0, RoomsRig.CountColour(rig, 0x25, 24, 40, 296, 60));
        Assert.Null(Slot(rig, 0));
        Assert.Equal(1, rig.Game.Events.CursorShowCount);
    }

    [DataFact]
    public void Quit_with_Y_ends_the_game()
    {
        var (rig, _) = StartBarracks();
        Click(rig, 1_500, Airlock);
        Assert.False(rig.Runtime.RunHeadless(2_500));
        RoomsRig.Snap(rig, "barracks-quit-question");
        RoomsRig.Key(rig, 3_000, 'Y');
        Assert.True(rig.Runtime.RunHeadless(5_000), "Y did not quit");
        Assert.Null(rig.Runtime.Failure); // GameExitException ends the runtime normally
        Assert.False(rig.Game.Events.IsActive);
    }

    [DataFact]
    public void Quit_with_N_stays_in_the_barracks()
    {
        var (rig, result) = StartBarracks();
        Click(rig, 1_500, Airlock);
        RoomsRig.Key(rig, 3_000, 'N');
        Assert.False(rig.Runtime.RunHeadless(4_500));
        Assert.Null(rig.Runtime.Failure);
        Click(rig, 5_000, Bar);
        Assert.True(rig.Runtime.RunHeadless(7_000));
        Assert.Equal(BarracksResult.ReturnToBar, result());
    }

    [DataFact]
    public void Saving_into_an_empty_bunk_writes_the_campaign()
    {
        var (rig, _) = StartBarracks(setup: r =>
        {
            var state = r.Game.Session.State;
            state.CurrentSeries = 3;
            state.CurrentMission = 1;
            r.Game.Session.Player.Name = "Blair";
            r.Game.Session.Player.Callsign = "Maverick";
        });
        Click(rig, 1_500, Bunk0Save);
        Assert.False(rig.Runtime.RunHeadless(2_500));
        RoomsRig.Snap(rig, "barracks-game-name-prompt");
        double at = RoomsRig.Type(rig, 3_000, "test 1");
        Assert.False(rig.Runtime.RunHeadless(at));
        RoomsRig.Snap(rig, "barracks-game-name-typed");
        RoomsRig.Enter(rig, at + 200);
        Assert.False(rig.Runtime.RunHeadless(at + 2_000));
        Assert.Null(rig.Runtime.Failure);
        RoomsRig.Snap(rig, "barracks-after-save");

        var saved = Slot(rig, 0);
        Assert.NotNull(saved);
        Assert.Equal("TEST 1", saved.Description);
        Assert.Equal(3, saved.Campaign.CurrentSeries);
        Assert.Equal(1, saved.Campaign.CurrentMission);
        Assert.Equal("Blair", saved.Pilots[8].Name);
        Assert.Equal("Maverick", saved.Pilots[8].Callsign);
        for (int slot = 1; slot < 8; slot++)
            Assert.Null(Slot(rig, slot));
        Assert.Equal(1, rig.Game.Events.CursorShowCount);
    }

    [DataFact]
    public void Esc_cancels_the_name_prompt()
    {
        var (rig, _) = StartBarracks();
        Click(rig, 1_500, Bunk0Save);
        double at = RoomsRig.Type(rig, 3_000, "abc");
        RoomsRig.Escape(rig, at + 200);
        Assert.False(rig.Runtime.RunHeadless(at + 2_000));
        Assert.Null(rig.Runtime.Failure);
        Assert.Null(Slot(rig, 0));
        Assert.Equal(0, RoomsRig.CountColour(rig, 0x25, 24, 20, 296, 60));
    }

    [DataFact]
    public void Save_load_save_round_trip_is_byte_exact()
    {
        var (rig, _) = StartBarracks(setup: r =>
        {
            var session = r.Game.Session;
            var state = session.State;
            state.CurrentSeries = 4;
            state.CurrentMission = 2;
            state.PlayerShipType = 3;
            state.Medals[1] = 2;
            state.Badges[7] = 1;
            state.SeriesHistory[0] = 1;
            state.SeriesHistory[1] = 2;
            state.SeriesHistoryCount = 2;
            state.PersonalityDeathMission[6] = 9;
            state.CurrentDate = new CampaignDate(140, 2654);
            state.ElapsedDate = new CampaignDate(0x1e09, 1);
            state.PromotionScore = 3;
            state.MissionScore = 21;
            state.SeriesScore = 17;
            session.Player.Name = "Steele";
            session.Player.Callsign = "Ace";
            session.Player.Kills = 12;
            session.Player.Missions = 6;
            session.Player.Rank = 1;
        });
        Click(rig, 1_500, Bunk0Save);
        double at = RoomsRig.Type(rig, 2_500, "round trip");
        RoomsRig.Enter(rig, at += 200);

        // Forget the campaign, then wake the saved game.
        Assert.False(rig.Runtime.RunHeadless(at += 1_500));
        rig.Game.Session.Reset();
        Assert.Equal(1, rig.Game.Session.State.CurrentSeries);
        Click(rig, at += 200, Bunk0Awaken);
        Assert.False(rig.Runtime.RunHeadless(at += 1_000));
        RoomsRig.Snap(rig, "barracks-awaken-question");
        RoomsRig.Key(rig, at += 200, 'Y');
        Assert.False(rig.Runtime.RunHeadless(at += 1_500));
        Assert.Null(rig.Runtime.Failure);
        var session = rig.Game.Session;
        Assert.Equal(4, session.State.CurrentSeries);
        Assert.Equal("Steele", session.Player.Name);
        Assert.True(session.CampaignActive);
        Assert.Equal(0, session.PendingCampaignIndex);

        // Save the loaded campaign into the next bunk under the same name.
        Click(rig, at += 200, Bunk1Save);
        at = RoomsRig.Type(rig, at + 1_000, "round trip");
        RoomsRig.Enter(rig, at += 200);
        Assert.False(rig.Runtime.RunHeadless(at + 1_500));
        Assert.Null(rig.Runtime.Failure);
        RoomsRig.Snap(rig, "barracks-two-sleepers");

        var first = RoomsRig.ReadSlotBytes(rig.Game.SaveGamePath, 0);
        var second = RoomsRig.ReadSlotBytes(rig.Game.SaveGamePath, 1);
        Assert.Equal("ROUND TRIP", Slot(rig, 0)!.Description);
        Assert.Equal(first, second);
    }

    [DataFact]
    public void A_slot_written_by_another_build_loads_and_saves_back_byte_exact()
    {
        // A crafted record: garbage after the NULs of names and description, every field set.
        var bytes = new byte[SaveGameSlot.Size];
        new Random(1234).NextBytes(bytes);
        "CRAFTED\0"u8.CopyTo(bytes);
        bytes.AsSpan(8, 9).Clear();     // the typed description leaves zeros behind its NUL
        bytes[0x11] = 1;                 // occupied
        for (int pilot = 0; pilot < 9; pilot++)
        {
            int offset = 0x12 + pilot * PilotRecord.Size;
            "NAME\0"u8.CopyTo(bytes.AsSpan(offset));
            "CALL\0"u8.CopyTo(bytes.AsSpan(offset + 0x0e));
        }
        int campaign = 0x168;
        bytes[campaign + 0x15] = 1;      // mission
        bytes[campaign + 0x16] = 5;      // series
        bytes[campaign + 0x17] = 3;      // history count
        bytes[campaign + 0x42] = 1;      // campaign index (Secret Missions 1)
        bytes[campaign + 0x43] = 0;

        var (rig, _) = StartBarracks(setup: r =>
        {
            var file = File.ReadAllBytes(r.Game.SaveGamePath);
            bytes.CopyTo(file, 2 * SaveGameSlot.Size);
            File.WriteAllBytes(r.Game.SaveGamePath, file);
        });
        Click(rig, 1_500, Bunk2Awaken);
        RoomsRig.Key(rig, 2_500, 'Y');
        Assert.False(rig.Runtime.RunHeadless(4_000));
        Assert.Equal(1, rig.Game.Session.State.CampaignIndex);
        Assert.Equal(1, rig.Game.Session.CampaignDataSet);
        Assert.Equal(1, rig.Game.Session.PendingCampaignIndex);
        Assert.NotNull(rig.Game.Session.Data);
        Click(rig, 4_500, Bunk3Save);
        double at = RoomsRig.Type(rig, 5_500, "crafted");
        RoomsRig.Enter(rig, at + 200);
        Assert.False(rig.Runtime.RunHeadless(at + 2_000));
        Assert.Null(rig.Runtime.Failure);
        Assert.Equal(bytes, RoomsRig.ReadSlotBytes(rig.Game.SaveGamePath, 3));
    }

    [DataFact]
    public void Replacing_a_game_asks_first_and_offers_its_name()
    {
        var (rig, _) = StartBarracks(setup: r =>
        {
            var slot = r.Game.Session.CreateSaveRecord("FIRST", new SavedObjective[16]);
            SaveGameFile.Save(r.Game.SaveGamePath, 0, slot);
        });
        Click(rig, 1_500, Bunk0Save);
        RoomsRig.Key(rig, 2_500, 'N');                 // "Replace FIRST? (Y/N)" -> no
        Assert.False(rig.Runtime.RunHeadless(3_500));
        Assert.Equal(0, RoomsRig.CountColour(rig, 0x25, 24, 20, 296, 60));
        rig.Game.Session.State.CurrentSeries = 2;
        Click(rig, 4_000, Bunk0Save);
        Assert.False(rig.Runtime.RunHeadless(5_000));
        RoomsRig.Snap(rig, "barracks-replace-question");
        RoomsRig.Key(rig, 5_500, 'Y');
        Assert.False(rig.Runtime.RunHeadless(6_500));
        RoomsRig.Snap(rig, "barracks-replace-prompt");
        RoomsRig.Enter(rig, 7_000);                    // keep the offered name
        Assert.False(rig.Runtime.RunHeadless(8_500));
        Assert.Null(rig.Runtime.Failure);
        var saved = Slot(rig, 0);
        Assert.NotNull(saved);
        Assert.Equal("FIRST", saved.Description);
        Assert.Equal(2, saved.Campaign.CurrentSeries);
    }

    [DataFact]
    public void Awaken_with_N_keeps_the_current_campaign()
    {
        var (rig, _) = StartBarracks(setup: r =>
        {
            var record = r.Game.Session.CreateSaveRecord("OTHER", new SavedObjective[16]);
            record.Campaign.CurrentSeries = 7;
            SaveGameFile.Save(r.Game.SaveGamePath, 0, record);
        });
        Click(rig, 1_500, Bunk0Awaken);
        RoomsRig.Key(rig, 2_500, 'N');
        Assert.False(rig.Runtime.RunHeadless(4_000));
        Assert.Equal(1, rig.Game.Session.State.CurrentSeries);
    }

    [DataFact]
    public void Developer_unlock_renames_the_loaded_pilot_cheater()
    {
        var (rig, _) = StartBarracks(campaignActive: false, setup: r =>
        {
            r.Game.Options.OriginDevUnlock = true;
            var record = r.Game.Session.CreateSaveRecord("CHEAT", new SavedObjective[16]);
            record.Pilots[8].Callsign = "Maverick";
            SaveGameFile.Save(r.Game.SaveGamePath, 0, record);
        });
        Click(rig, 1_500, Bunk0Awaken);
        RoomsRig.Key(rig, 2_500, 'Y');
        Assert.False(rig.Runtime.RunHeadless(4_000));
        Assert.True(rig.Game.Session.CampaignActive);
        Assert.Equal("CHEATER", rig.Game.Session.Player.Callsign);
    }

    [DataFact]
    public void View_medals_shows_the_chest_and_returns()
    {
        var (rig, result) = StartBarracks(setup: r =>
        {
            var state = r.Game.Session.State;
            state.Medals[0] = 2;
            state.Medals[3] = 1;
            state.Badges[2] = 1;
            state.Badges[3] = 1;
            state.Badges[7] = 1;
            r.Game.Session.Player.Rank = 2;
            r.Game.Session.Player.Name = "Blair";
            r.Game.Session.Player.Callsign = "Maverick";
        });
        Click(rig, 1_500, Medals);
        Assert.False(rig.Runtime.RunHeadless(3_000));
        RoomsRig.Snap(rig, "barracks-medals");
        Assert.Equal("Enyo", rig.Game.Screens.RoomState.SystemName);
        Assert.Equal(24, rig.Game.Graphics.Screen!.Top);
        Assert.True(ScreenRig.CountNonBlack(rig.Front, 24, 151) > 10_000);
        Assert.True(ScreenRig.CountNonBlack(rig.Front, 152, 199) > 100); // the pilot summary line
        RoomsRig.Space(rig, 3_500);
        Assert.False(rig.Runtime.RunHeadless(5_000));
        Assert.Null(rig.Runtime.Failure);
        RoomsRig.Snap(rig, "barracks-after-medals");
        Assert.Equal(0, rig.Game.Graphics.Screen!.Top);
        Assert.Equal(199, rig.Game.Graphics.Screen!.Bottom);
        Assert.Equal(1, rig.Game.Events.CursorShowCount);
        Click(rig, 5_500, Hangar);
        Assert.True(rig.Runtime.RunHeadless(8_000));
        Assert.Equal(BarracksResult.LaunchMission, result());
    }

    [DataFact]
    public void A_failed_save_shows_an_error_until_a_key_is_released()
    {
        var (rig, _) = StartBarracks();
        Assert.False(rig.Runtime.RunHeadless(1_000));
        File.SetAttributes(rig.Game.SaveGamePath, FileAttributes.ReadOnly);
        try
        {
            Click(rig, 1_500, Bunk0Save);
            double at = RoomsRig.Type(rig, 2_500, "lost");
            // The message is acknowledged by the next key release: hold Enter to see it.
            RoomsRig.HoldKey(rig, at += 200, 0x1c, 0x0d, 1_500);
            Assert.False(rig.Runtime.RunHeadless(at += 1_000));
            RoomsRig.Snap(rig, "barracks-save-error");
            Assert.True(RoomsRig.CountColour(rig, 0x25, 24, 40, 296, 60) > 500); // "Error: Game LOST not saved."
            Assert.False(rig.Runtime.RunHeadless(at + 1_500));
            Assert.Null(rig.Runtime.Failure);
            Assert.Equal(0, RoomsRig.CountColour(rig, 0x25, 24, 40, 296, 60));
            Assert.Null(Slot(rig, 0));
        }
        finally
        {
            File.SetAttributes(rig.Game.SaveGamePath, FileAttributes.Normal);
        }
    }

    [DataFact]
    public void A_bunk_that_no_longer_loads_reports_bad_data_before_asking()
    {
        var (rig, _) = StartBarracks(setup: r =>
            SaveGameFile.Save(r.Game.SaveGamePath, 0, r.Game.Session.CreateSaveRecord("GONE", new SavedObjective[16])));
        Assert.False(rig.Runtime.RunHeadless(1_000));
        // Another program clears the bunk while the barracks show it as used.
        var bytes = File.ReadAllBytes(rig.Game.SaveGamePath);
        bytes[0x11] = 0;
        File.WriteAllBytes(rig.Game.SaveGamePath, bytes);
        Click(rig, 1_500, Bunk0Awaken);
        Assert.False(rig.Runtime.RunHeadless(2_500));
        RoomsRig.Snap(rig, "barracks-data-may-be-bad");
        RoomsRig.Space(rig, 3_000);                     // acknowledge (on release)
        Assert.False(rig.Runtime.RunHeadless(4_000));
        RoomsRig.Snap(rig, "barracks-awaken-bad-data");
        RoomsRig.Key(rig, 4_500, 'N');
        Assert.False(rig.Runtime.RunHeadless(6_000));
        Assert.Null(rig.Runtime.Failure);
        Assert.Equal(1, rig.Game.Session.State.CurrentSeries);
        Assert.Equal(1, rig.Game.Events.CursorShowCount);
    }

    [DataFact]
    public void Barracks_animation_runs_for_a_minute()
    {
        var (rig, _) = StartBarracks(setup: r =>
        {
            for (int slot = 0; slot < 8; slot += 3)
                SaveGameFile.Save(r.Game.SaveGamePath, slot, r.Game.Session.CreateSaveRecord($"GAME {slot}", new SavedObjective[16]));
        });
        for (int at = 1_000; at <= 3_000; at += 125)
        {
            Assert.False(rig.Runtime.RunHeadless(at));
            RoomsRig.Snap(rig, $"barracks-anim-{at}");
        }
        Assert.False(rig.Runtime.RunHeadless(60_000));
        Assert.Null(rig.Runtime.Failure);
        RoomsRig.Snap(rig, "barracks-sleepers");
    }
}
