using WingCommander.Core.Numerics;
using WingCommander.Core.Resources;

namespace WingCommander.Game.Campaign;

/// <summary>Outcome of a flown mission as far as the campaign rules need it.</summary>
/// <param name="PlayerKills">Kills by the player this mission (nPlayerKillCount).</param>
/// <param name="WingmanKills">Kills by the player's wingman (nWingmanKillCount).</param>
/// <param name="WingmanPersonality">Personality of the wingman (acShipRating[nYourWingman]); -1 when flying alone.</param>
public readonly record struct MissionStatistics(short PlayerKills, short WingmanKills, int WingmanPersonality);

/// <summary>
/// The live campaign: roster, persistent state, the loaded CAMP data and the transient flow
/// flags that GameFlow, debriefing and the office/medal scenes exchange. The rules are
/// literal ports of nav.c (PostMission, UpdateSeries, MoveNewCampaign, scores) and the
/// pilot bookkeeping helpers.
/// </summary>
/// <remarks>C: nav.c 0x40EFE0-0x40F4B0, killbrd.c ResetCampaignData/CorrectPointers, hudmsg.c personality_killed,
/// screens.c wing_status, globals stCampaignState/aPilotRecords/pMissionCampaignData and the flow flags.</remarks>
public sealed class CampaignSession
{
    private readonly CRandom _random;

    /// <param name="random">The game's shared random generator (ResetCampaignData seeds the TrainSim ranking with it).</param>
    public CampaignSession(CRandom? random = null)
    {
        _random = random ?? new CRandom(1);
        Reset();
    }

    /// <summary>The simulator ranking (regenerated with every new campaign, never saved).</summary>
    public TrainSimHighScores HighScores { get; } = new();

    public CampaignState State { get; private set; } = CampaignState.CreateInitial();

    /// <summary>Roster: 0..7 wingmen by personality, 8 = the player.</summary>
    public PilotRecord[] Pilots { get; private set; } = PilotRecord.CreateInitialRoster();

    public PilotRecord Player => Pilots[CampaignState.PlayerPilotIndex];

    /// <summary>The loaded CAMP.xxx (pMissionCampaignData etc.).</summary>
    public CampaignFile? Data { get; set; }

    /// <remarks>C: bCampaignActive.</remarks>
    public bool CampaignActive { get; set; }

    /// <remarks>C: nCampaignDataSet.</remarks>
    public short CampaignDataSet { get; set; }

    /// <remarks>C: nPendingCampaignIndex.</remarks>
    public short PendingCampaignIndex { get; set; } = -1;

    /// <remarks>C: bCampaignStartupMode.</remarks>
    public bool CampaignStartupMode { get; set; }

    /// <remarks>C: nPostSeriesSequence.</remarks>
    public short PostSeriesSequence { get; set; } = -1;

    /// <remarks>C: bSeriesFailed.</remarks>
    public bool SeriesFailed { get; set; }

    /// <remarks>C: bPlayerShipTypeChanged.</remarks>
    public bool PlayerShipTypeChanged { get; set; }

    /// <remarks>C: bOfficeVisitPending.</remarks>
    public bool OfficeVisitPending { get; set; }

    /// <remarks>C: bPromotionPending.</remarks>
    public bool PromotionPending { get; set; }

    /// <remarks>C: nPendingMedalIndex.</remarks>
    public short PendingMedalIndex { get; set; } = -1;

    /// <remarks>C: nMissionMedalScore.</remarks>
    public short MissionMedalScore { get; set; }

    /// <remarks>C: nPreviousPlayerShipType.</remarks>
    public short PreviousPlayerShipType { get; set; }

    /// <summary>Date shown by the $E text macro; starts at day 20, year 340 like the C global and is not reset by <see cref="Reset"/>.</summary>
    /// <remarks>C: stSavedCampaignDate = {20, 340} (0x0046e188).</remarks>
    public CampaignDate SavedCampaignDate { get; set; } = new(20, 340);

    /// <remarks>C: nWingmanKilledThisMission.</remarks>
    public bool WingmanKilledThisMission { get; set; }

    /// <remarks>C: bPlayerEjectedThisMission.</remarks>
    public bool PlayerEjectedThisMission { get; set; }

    /// <summary>Restores the initial campaign record and roster.</summary>
    /// <remarks>C: ResetCampaignData (0x440800), including InitializeTrainSimHighScores.</remarks>
    public void Reset()
    {
        State = CampaignState.CreateInitial();
        Pilots = PilotRecord.CreateInitialRoster();
        HighScores.Initialize(_random);
    }

    /// <summary>Loads CAMP.&lt;campaign&gt; and makes it current.</summary>
    public void LoadCampaignData(GameDirectory directory, int campaignIndex)
    {
        Data = CampaignFile.Load(directory, campaignIndex);
        CampaignDataSet = (short)campaignIndex;
    }

    /// <summary>Adopts a loaded save game (pilots, campaign, CAMP data).</summary>
    /// <remarks>C: LoadGameFromSlot (0x41B980) minus the UI; the cheater callsign is applied by the caller.</remarks>
    public void Apply(SaveGameSlot slot, GameDirectory directory)
    {
        for (int i = 0; i < 9; i++)
            Pilots[i] = slot.Pilots[i].Clone();
        State = slot.Campaign.Clone();
        PendingCampaignIndex = State.CampaignIndex;
        LoadCampaignData(directory, State.CampaignIndex);
        CampaignActive = true;
    }

    /// <summary>Builds a save record of the current campaign.</summary>
    public SaveGameSlot CreateSaveRecord(string description, IReadOnlyList<SavedObjective> objectives)
    {
        var slot = new SaveGameSlot { Occupied = true, Campaign = State.Clone(), Description = description.ToUpperInvariant() };
        for (int i = 0; i < 9; i++)
            slot.Pilots[i] = Pilots[i].Clone();
        for (int i = 0; i < 16 && i < objectives.Count; i++)
            slot.Objectives[i] = objectives[i];
        return slot;
    }

    private SeriesMission CurrentSeriesMission(int series, int mission) =>
        (Data ?? throw new InvalidOperationException("No campaign data loaded.")).GetSeries(series).Missions[mission];

    /// <summary>Sum of all objective points of the current mission.</summary>
    /// <remarks>C: FullMissionScore (0x40F190).</remarks>
    public int FullMissionScore()
    {
        short score = 0;
        foreach (sbyte points in CurrentSeriesMission(State.CurrentSeries, State.CurrentMission).ObjectiveScores)
            score = unchecked((short)(score + points));
        return score;
    }

    /// <summary>Sum of the points of the achieved objectives.</summary>
    /// <remarks>C: PlayersMissionScore (0x40F1E0).</remarks>
    public int PlayersMissionScore(Func<int, bool> achieved)
    {
        var scores = CurrentSeriesMission(State.CurrentSeries, State.CurrentMission).ObjectiveScores;
        short score = 0;
        for (int objective = 0; objective < 16; objective++)
        {
            if (achieved(objective))
                score = unchecked((short)(score + scores[objective]));
        }
        return score;
    }

    /// <summary>Badges, mission and kill counts after a mission, including random stats for the other pilots.</summary>
    /// <remarks>C: PostMission (0x40F010) and add_statistics (0x40EFE0).</remarks>
    public void PostMission(MissionStatistics stats, CRandom random)
    {
        var player = Player;
        short oldKills = player.Kills;
        if (oldKills < 5 && oldKills + stats.PlayerKills > 4)
            State.Badges[CampaignBadge.FiveKills] = 1;
        else if (oldKills < 25 && oldKills + stats.PlayerKills > 24)
            State.Badges[CampaignBadge.TwentyFiveKills] = 1;

        int shipBadge = CampaignBadge.ShipTypeBase + State.PlayerShipType;
        if ((uint)shipBadge < (uint)State.Badges.Length && State.Badges[shipBadge] == 0)
            State.Badges[shipBadge] = 1;

        player.Missions++;
        switch (player.Missions)
        {
            case 1:
                State.Badges[CampaignBadge.FirstMission] = 1;
                State.Badges[CampaignBadge.FiveMissions] = 1; // the retail switch falls through
                break;
            case 5:
                State.Badges[CampaignBadge.FiveMissions] = 1;
                break;
            case 10:
                State.Badges[CampaignBadge.TenMissions] = 1;
                break;
            case 15:
                State.Badges[CampaignBadge.FifteenMissions] = 1;
                break;
        }

        player.Kills = unchecked((short)(player.Kills + stats.PlayerKills));
        if (oldKills / 5 < player.Kills / 5)
            State.PromotionScore++;

        for (int pilot = 0; pilot < 8; pilot++)
        {
            short missions, kills;
            if (stats.WingmanPersonality == -1 || stats.WingmanPersonality != pilot)
            {
                if (State.PersonalityDeathMission[pilot] != 0)
                    continue;
                missions = random.InRange(0, 2);
                kills = missions == 0 ? (short)0 : random.InRange(0, stats.PlayerKills);
            }
            else
            {
                missions = 1;
                kills = stats.WingmanKills;
            }
            Pilots[pilot].Missions = unchecked((short)(Pilots[pilot].Missions + missions));
            Pilots[pilot].Kills = unchecked((short)(Pilots[pilot].Kills + kills));
        }
    }

    /// <summary>
    /// Scores the flown mission, advances to the next mission or, at the end of a series, picks
    /// the win/lose branch and ship, the post-series cutscene and the pending medal.
    /// </summary>
    /// <remarks>C: UpdateSeries (0x40F240). The post-series suppression check reads byte +5 of the
    /// record after the new series (as the original does); outside the table it never matches.</remarks>
    public void UpdateSeries(Func<int, bool> achieved)
    {
        var data = Data ?? throw new InvalidOperationException("No campaign data loaded.");
        SavedCampaignDate = State.CurrentDate;
        var series = data.GetSeries(State.CurrentSeries);
        var medal = series.Missions[State.CurrentMission];

        short fullScore = (short)FullMissionScore();
        short playerScore = (short)PlayersMissionScore(achieved);
        if (playerScore == fullScore)
            State.PromotionScore++;
        State.SeriesScore = unchecked((short)(State.SeriesScore + playerScore));
        State.CurrentMission++;

        if (State.CurrentMission >= series.MissionCount)
        {
            PreviousPlayerShipType = (short)State.PlayerShipType;
            PostSeriesSequence = series.PostSeriesSequence;
            if (State.SeriesHistoryCount < State.SeriesHistory.Length)
                State.SeriesHistory[State.SeriesHistoryCount] = State.CurrentSeries;
            State.SeriesHistoryCount++;
            bool failed = State.SeriesScore < series.ScoreThreshold;
            if (failed)
            {
                State.CurrentSeries = series.LoseNextSeries;
                State.PlayerShipType = series.LoseShipType;
            }
            else
            {
                State.CurrentSeries = series.WinNextSeries;
                State.PlayerShipType = series.WinShipType;
            }
            SeriesFailed = failed;
            if (PreviousPlayerShipType != State.PlayerShipType)
            {
                PlayerShipTypeChanged = true;
                OfficeVisitPending = true;
            }
            State.SeriesScore = 0;
            State.CurrentMission = 0;
            if (data.RawSeriesByte(State.CurrentSeries + 1, 5) is { } next &&
                next == PostSeriesSequence && PostSeriesSequence < 0x40)
                PostSeriesSequence = -1;
        }

        if (WingmanKilledThisMission)
            MissionMedalScore = Math.Max((short)0, unchecked((short)(State.MissionScore - 15)));
        if (medal.MedalThreshold <= MissionMedalScore && PendingMedalIndex == -1)
        {
            SavedCampaignDate = State.CurrentDate;
            PendingMedalIndex = medal.MedalIndex;
        }
    }

    /// <summary>Advances the calendar: 0..1 days between missions, 5..6 after a series.</summary>
    /// <remarks>C: MoveNewCampaign (0x40F3F0).</remarks>
    public void MoveNewCampaign(CRandom random)
    {
        short days = State.CurrentMission != 0
            ? random.InRange(0, 1)
            : unchecked((short)(random.InRange(0, 1) + 5));
        var date = State.CurrentDate;
        date.Day = unchecked((short)(date.Day + days));
        if (date.Day >= 366)
        {
            date.Day = unchecked((short)(date.Day - 365));
            date.Year++;
        }
        State.CurrentDate = date;
    }

    /// <summary>Records a wingman's death (personality &lt; 8).</summary>
    /// <remarks>C: personality_killed (0x42AC50); enemy aces (personality &gt;= 9) are handled by the flight code.</remarks>
    public void WingmanKilled(int personality)
    {
        State.PersonalityDeathMission[personality] = State.MissionNumber;
        State.PromotionScore = Math.Max((short)0, unchecked((short)(State.PromotionScore - 1)));
    }

    /// <summary>3 alive, 1 died this mission, 2 died earlier, otherwise the current mission number.</summary>
    /// <remarks>C: wing_status (0x4380D0).</remarks>
    public short WingStatus(int personality)
    {
        int deathMission = State.PersonalityDeathMission[personality];
        if (deathMission == 0)
            return 3;
        int current = State.MissionNumber;
        if (deathMission == current)
            return 1;
        if (current > deathMission)
            return 2;
        return unchecked((short)current);
    }
}
