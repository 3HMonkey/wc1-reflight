using WingCommander.Game.Campaign;

namespace WingCommander.Game.Scenes;

/// <summary>What the scenes need to know about the mission that was just flown (flight layer state).</summary>
public interface IMissionOutcome
{
    /// <remarks>C: nPlayerKillCount.</remarks>
    int PlayerKills { get; }

    /// <remarks>C: nWingmanKillCount.</remarks>
    int WingmanKills { get; }

    /// <remarks>C: achieved(objective) (flag 2 of aMissionObjectives[objective]).</remarks>
    bool Achieved(int objective);

    /// <remarks>C: sighted(objective) (flag 4).</remarks>
    bool Sighted(int objective);
}

/// <summary>
/// The live campaign as seen by the conversation engine: the branch conditions of the scene tests
/// (<see cref="ISceneConditions"/>) and the values of the subtitle macros
/// (<see cref="ITextMacroContext"/>), read from the <see cref="CampaignSession"/>, the mission
/// outcome of the flight layer and the loaded mission data.
/// </summary>
/// <remarks>C: the globals ParseTests and AddPCName read: stCampaignState, aPilotRecords,
/// nPlayerKillCount, nWingmanKillCount, aMissionObjectives, nConversationMedalIndex,
/// bOfficeVisitPending, bPromotionPending, bPlayerEjectedThisMission, bPlayerShipTypeChanged,
/// nPreviousPlayerShipType, stSavedCampaignDate, abSeriesAuxData; no_objectives_achieved (0x438090),
/// ace_status (0x422010), wing_status (0x4380D0).</remarks>
public sealed class CampaignSceneContext(CampaignSession session, IMissionOutcome outcome) : ISceneConditions, ITextMacroContext
{
    /// <summary>The campaign.</summary>
    public CampaignSession Session { get; } = session;

    /// <summary>The flown mission's results.</summary>
    public IMissionOutcome Outcome { get; } = outcome;

    /// <summary>The mission data last loaded (LoadMissionData): objective count and system name.</summary>
    public MissionBriefingData? Mission { get; set; }

    /// <summary>The medal being awarded.</summary>
    /// <remarks>C: nConversationMedalIndex.</remarks>
    public int MedalIndex { get; set; }

    // ------------------------------------------------------------------ ISceneConditions

    public int MissionScore => Session.State.MissionScore;

    public int WingStatus(int personality) =>
        (uint)personality < (uint)Session.State.PersonalityDeathMission.Length ? Session.WingStatus(personality) : 3;

    public int PlayerKills => Outcome.PlayerKills;

    public int WingmanKills => Outcome.WingmanKills;

    public bool OfficeVisitPending => Session.OfficeVisitPending;

    public bool Achieved(int objective) => Outcome.Achieved(objective);

    public bool Sighted(int objective) => Outcome.Sighted(objective);

    /// <summary>1 when all <paramref name="bits"/> are set in the ace's flags, else 0.</summary>
    /// <remarks>C: ace_status (0x422010, logic.c).</remarks>
    public int AceStatus(int ace, int bits)
    {
        var flags = Session.State.AceFlags;
        if ((uint)ace >= (uint)flags.Length)
            return 0;
        return (flags[ace] & bits) == bits ? 1 : 0;
    }

    public bool PromotionPending => Session.PromotionPending;

    public bool PlayerEjected => Session.PlayerEjectedThisMission;

    public int EjectionCount => Session.State.ElapsedDate.Year;

    public bool PlayerShipTypeChanged => Session.PlayerShipTypeChanged;

    public int PlayerShipType => Session.State.PlayerShipType;

    public int PreviousPlayerShipType => Session.PreviousPlayerShipType;

    public int PlayersMissionScore() => Session.Data is null ? 0 : Session.PlayersMissionScore(Outcome.Achieved);

    public int FullMissionScore() => Session.Data is null ? 0 : Session.FullMissionScore();

    /// <remarks>C: no_objectives_achieved (0x438090, screens.c).</remarks>
    public bool NoObjectivesAchieved()
    {
        int count = Mission?.ObjectiveListCount ?? 0;
        int objective = 0;
        while (objective < count)
        {
            if (Outcome.Achieved(objective))
                break;
            objective++;
        }
        return objective >= count;
    }

    // ------------------------------------------------------------------ ITextMacroContext

    public string MedalName => (uint)MedalIndex < (uint)TextMacros.MedalNames.Length ? TextMacros.MedalNames[MedalIndex] : "";

    public string Callsign => Session.Player.Callsign;

    public string PlayerName => Session.Player.Name;

    public string RankName =>
        (uint)Session.Player.Rank < (uint)TextMacros.RankNames.Length ? TextMacros.RankNames[Session.Player.Rank] : "";

    public string SystemName => Mission?.SystemName ?? "";

    public (int Year, int Day) CurrentDate => (Session.State.CurrentDate.Year, Session.State.CurrentDate.Day);

    public (int Year, int Day) SavedDate => (Session.SavedCampaignDate.Year, Session.SavedCampaignDate.Day);

    /// <summary>Signed bytes of the misnamed elapsed date: low byte hour, high byte minute.</summary>
    public (int Hour, int Minute) Time
    {
        get
        {
            short value = Session.State.ElapsedDate.Day;
            return (unchecked((sbyte)(value & 0xff)), unchecked((sbyte)((value >> 8) & 0xff)));
        }
    }

    public string WingmanName(int personality) =>
        (uint)personality < 8 ? Session.Pilots[personality].Name : "";
}
