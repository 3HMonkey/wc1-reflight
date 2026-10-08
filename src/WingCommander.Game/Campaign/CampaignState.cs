namespace WingCommander.Game.Campaign;

/// <summary>Campaign calendar date (day of year 1..365, year).</summary>
/// <remarks>C: CampaignDate.</remarks>
public struct CampaignDate
{
    public short Day;
    public short Year;

    public CampaignDate(short day, short year)
    {
        Day = day;
        Year = year;
    }
}

/// <summary>Medal kinds (frame order of the medal sprites).</summary>
public enum Medal
{
    BronzeStar = 0,
    SilverStar = 1,
    GoldStar = 2,
    GoldenSun = 3,
    TerranMedalOfValor = 4,
}

/// <summary>Badge slots in <see cref="CampaignState.Badges"/>.</summary>
/// <remarks>C: CampaignBadgeIndex.</remarks>
public static class CampaignBadge
{
    public const int FirstMission = 2;
    public const int ShipTypeBase = 3;
    public const int FiveKills = 7;
    public const int TwentyFiveKills = 8;
    public const int FiveMissions = 9;
    public const int TenMissions = 10;
    public const int FifteenMissions = 11;
}

/// <summary>
/// The complete persistent campaign record (runtime layout of the original is 0x58 bytes).
/// The current pilot is always roster entry 8 (the original kept a pointer that
/// CorrectPointers re-aimed there after loading).
/// </summary>
/// <remarks>C: CampaignState (wcdata.h), stCampaignState, stInitialCampaignState (0x004700B0).</remarks>
public sealed class CampaignState
{
    public const int PlayerPilotIndex = 8;

    /// <summary>The truncated pointer stored in save games; ignored on load, kept for byte-exact round trips.</summary>
    public short CurrentPilotRaw { get; set; }

    /// <summary>Object type of the player's fighter (0 Hornet, 1 Rapier, 2 Scimitar, 3 Raptor).</summary>
    public int PlayerShipType { get; set; }

    /// <summary>Count per <see cref="Medal"/> (stars stack).</summary>
    public byte[] Medals { get; } = new byte[5];

    public byte[] Badges { get; } = new byte[12];

    /// <summary>Mission within the series, 0..3.</summary>
    public sbyte CurrentMission { get; set; }

    /// <summary>Series 1..13 (-1 when the campaign is over).</summary>
    public sbyte CurrentSeries { get; set; } = 1;

    public sbyte SeriesHistoryCount { get; set; }

    public sbyte[] SeriesHistory { get; } = new sbyte[8];

    /// <summary>Per wingman personality: 0 alive, else (mission + series * 4) of the death.</summary>
    public int[] PersonalityDeathMission { get; } = new int[8];

    /// <summary>Per enemy ace: bit 1 alive, bit 2 killed, 4/8/0x20 comm-greeting state.</summary>
    public byte[] AceFlags { get; } = new byte[4];

    public CampaignDate CurrentDate;

    /// <summary>Misnamed in the reference: Day low byte = hour, high byte = minute; Year = ejection count.</summary>
    public CampaignDate ElapsedDate;

    public short PromotionScore { get; set; }

    public short MissionScore { get; set; }

    public short SeriesScore { get; set; }

    /// <summary>0 Vega campaign, 1 Secret Missions, 2 Secret Missions 2 (selects CAMP/BRIEFING/MODULE).</summary>
    public short CampaignIndex { get; set; }

    /// <remarks>C: stInitialCampaignState.</remarks>
    public static CampaignState CreateInitial()
    {
        var s = new CampaignState
        {
            PlayerShipType = 0,
            CurrentMission = 0,
            CurrentSeries = 1,
            CurrentDate = new CampaignDate(110, 2654),
            ElapsedDate = new CampaignDate(6, 0),
        };
        s.Badges[0] = 1;
        s.Badges[1] = 1;
        s.AceFlags.AsSpan().Fill(1);
        return s;
    }

    public CampaignState Clone()
    {
        var c = new CampaignState
        {
            CurrentPilotRaw = CurrentPilotRaw,
            PlayerShipType = PlayerShipType,
            CurrentMission = CurrentMission,
            CurrentSeries = CurrentSeries,
            SeriesHistoryCount = SeriesHistoryCount,
            CurrentDate = CurrentDate,
            ElapsedDate = ElapsedDate,
            PromotionScore = PromotionScore,
            MissionScore = MissionScore,
            SeriesScore = SeriesScore,
            CampaignIndex = CampaignIndex,
        };
        Medals.CopyTo(c.Medals, 0);
        Badges.CopyTo(c.Badges, 0);
        SeriesHistory.CopyTo(c.SeriesHistory, 0);
        PersonalityDeathMission.CopyTo(c.PersonalityDeathMission, 0);
        AceFlags.CopyTo(c.AceFlags, 0);
        return c;
    }

    /// <summary>Global mission number used by death records and briefing sections: mission + series * 4.</summary>
    public int MissionNumber => CurrentMission + CurrentSeries * 4;
}
