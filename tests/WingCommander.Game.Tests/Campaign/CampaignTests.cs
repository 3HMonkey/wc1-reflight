using WingCommander.Core.Numerics;
using WingCommander.Core.Resources;
using WingCommander.Game.Campaign;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Campaign;

public class CampaignTests
{
    [DataFact]
    public void Camp000_series_tree_matches_the_vega_campaign()
    {
        var camp = CampaignFile.Load(GameData.Require(), 0);

        var s1 = camp.GetSeries(1);
        Assert.Equal(2, s1.MissionCount);
        Assert.Equal(10, s1.ScoreThreshold);
        Assert.Equal(2, s1.WinNextSeries);
        Assert.Equal(2, s1.WinShipType);      // Scimitar
        Assert.Equal(3, s1.LoseNextSeries);
        Assert.Equal(0, s1.LoseShipType);     // Hornet
        Assert.Equal(-1, s1.PostSeriesSequence);
        Assert.Equal(0, s1.DebriefPersonality);
        Assert.Equal([2, 1, 2, 1], s1.Missions[0].ObjectiveScores[..4].Select(v => (int)v));

        var s12 = camp.GetSeries(12);
        Assert.Equal(4, s12.MissionCount);
        Assert.Equal(-1, s12.WinNextSeries);
        Assert.Equal(0x40, s12.PostSeriesSequence);
        Assert.Equal(-1, camp.GetSeries(13).WinNextSeries);
        Assert.Equal(0x41, camp.GetSeries(13).PostSeriesSequence);

        Assert.Equal(new ConstellationObject(2, 0, 0, 0), camp.Constellations[0, 0]);
        Assert.Equal(new ConstellationObject(3, 150, 30, 0), camp.Constellations[0, 1]);
    }

    [DataTheory]
    [InlineData(0, 13)]
    [InlineData(1, 8)]
    [InlineData(2, 9)]
    public void Every_campaign_file_has_a_consistent_tree(int campaign, int usedSeries)
    {
        var camp = CampaignFile.Load(GameData.Require(), campaign);
        for (int s = 1; s <= usedSeries; s++)
        {
            var series = camp.GetSeries(s);
            Assert.InRange(series.MissionCount, 1, 4);
            Assert.InRange(series.WinNextSeries, -1, usedSeries);
            Assert.InRange(series.LoseNextSeries, -1, usedSeries);
            Assert.InRange(series.WinShipType, -1, 3);
        }
    }

    [DataFact]
    public void Gog_savegame_slots_round_trip_byte_exactly()
    {
        string path = GameData.Require().Resolve(SaveGameFile.FileName);
        byte[] original = File.ReadAllBytes(path);
        Assert.Equal(SaveGameFile.FileSize, original.Length);
        var buffer = new byte[SaveGameSlot.Size];
        for (int i = 0; i < SaveGameFile.SlotCount; i++)
        {
            var slot = SaveGameFile.ReadSlot(path, i)!;
            Assert.Equal($"game {i + 1}", slot.Description);
            Assert.False(slot.Occupied);
            slot.Write(buffer);
            Assert.True(original.AsSpan(i * SaveGameSlot.Size, SaveGameSlot.Size).SequenceEqual(buffer), $"slot {i}");
        }
        Assert.False(SaveGameFile.AnySavedGames(path, out _));
    }

    [Fact]
    public void Save_and_load_a_campaign()
    {
        string path = Path.Combine(Path.GetTempPath(), $"wc1-save-{Guid.NewGuid():N}.WLD");
        try
        {
            SaveGameFile.Ensure(path);
            Assert.Equal(SaveGameFile.FileSize, new FileInfo(path).Length);

            var session = new CampaignSession();
            session.Player.Name = "BLAIR";
            session.Player.Callsign = "MAVERICK";
            session.State.CurrentSeries = 4;
            session.State.CampaignIndex = 1;
            session.State.PersonalityDeathMission[3] = 17;
            var objectives = new SavedObjective[16];
            objectives[0] = new SavedObjective { Type = 1, Flags = 2, Position = new FixedVector(256, -512, 1024) };
            SaveGameFile.Save(path, 5, session.CreateSaveRecord("enyo run", objectives));

            var loaded = SaveGameFile.Load(path, 5)!;
            Assert.Equal("ENYO RUN", loaded.Description);
            Assert.Equal("MAVERICK", loaded.Pilots[8].Callsign);
            Assert.Equal(4, loaded.Campaign.CurrentSeries);
            Assert.Equal(1, loaded.Campaign.CampaignIndex);
            Assert.Equal(17, loaded.Campaign.PersonalityDeathMission[3]);
            Assert.Equal(new FixedVector(256, -512, 1024), loaded.Objectives[0].Position);
            Assert.Null(SaveGameFile.Load(path, 4));
            Assert.True(SaveGameFile.AnySavedGames(path, out bool secretMissions));
            Assert.True(secretMissions);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [DataFact]
    public void Completing_series_one_with_full_score_branches_to_mcauliffe()
    {
        var session = new CampaignSession();
        session.LoadCampaignData(GameData.Require(), 0);
        Assert.Equal(6, session.FullMissionScore());

        session.UpdateSeries(_ => true);       // mission 0, all objectives
        Assert.Equal(1, session.State.CurrentMission);
        Assert.Equal(6, session.State.SeriesScore);
        Assert.Equal(1, session.State.PromotionScore);

        session.UpdateSeries(_ => true);       // mission 1 ends series 1
        Assert.Equal(2, session.State.CurrentSeries);
        Assert.Equal(0, session.State.CurrentMission);
        Assert.Equal(2, session.State.PlayerShipType);
        Assert.False(session.SeriesFailed);
        Assert.True(session.PlayerShipTypeChanged);
        Assert.Equal(1, session.State.SeriesHistoryCount);
        Assert.Equal(1, session.State.SeriesHistory[0]);
    }

    [DataFact]
    public void Failing_series_one_goes_to_gateway_in_a_hornet()
    {
        var session = new CampaignSession();
        session.LoadCampaignData(GameData.Require(), 0);
        session.UpdateSeries(_ => false);
        session.UpdateSeries(_ => false);
        Assert.Equal(3, session.State.CurrentSeries);
        Assert.Equal(0, session.State.PlayerShipType);
        Assert.True(session.SeriesFailed);
        Assert.False(session.PlayerShipTypeChanged);
    }

    [Fact]
    public void PostMission_awards_badges_and_updates_the_roster()
    {
        var session = new CampaignSession();
        var random = new CRandom(42);
        session.PostMission(new MissionStatistics(6, 2, 0), random);
        Assert.Equal(1, session.Player.Missions);
        Assert.Equal(6, session.Player.Kills);
        Assert.Equal(1, session.State.Badges[CampaignBadge.FiveKills]);
        Assert.Equal(1, session.State.Badges[CampaignBadge.FirstMission]);
        Assert.Equal(1, session.State.Badges[CampaignBadge.FiveMissions]); // switch fall-through
        Assert.Equal(1, session.State.Badges[CampaignBadge.ShipTypeBase]);  // first Hornet flight
        Assert.Equal(1, session.State.PromotionScore);                      // crossed the 5-kill boundary
        Assert.Equal(12, session.Pilots[0].Missions);                        // Spirit flew as wingman
        Assert.Equal(16, session.Pilots[0].Kills);
    }

    [Fact]
    public void Calendar_wraps_into_the_next_year()
    {
        var session = new CampaignSession();
        session.State.CurrentDate = new CampaignDate(365, 2654);
        session.State.CurrentMission = 0;
        session.MoveNewCampaign(new CRandom(7));
        Assert.Equal(2655, session.State.CurrentDate.Year);
        Assert.InRange(session.State.CurrentDate.Day, 5, 6);
    }

    [Fact]
    public void Wing_status_tracks_deaths()
    {
        var session = new CampaignSession();
        session.State.CurrentSeries = 2;
        session.State.CurrentMission = 1;
        Assert.Equal(3, session.WingStatus(4));
        session.WingmanKilled(4);
        Assert.Equal(1, session.WingStatus(4));
        session.State.CurrentMission = 2;
        Assert.Equal(2, session.WingStatus(4));
    }
}
