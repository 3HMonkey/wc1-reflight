using WingCommander.Game.Campaign;

namespace WingCommander.Game.Screens.Rooms;

/// <summary>
/// State of the rooms that the original kept in globals and that therefore survives from one
/// visit to the next.
/// </summary>
public sealed class RoomsState
{
    /// <summary>Kill board order; each visit re-sorts the previous order (the sort is not stable).</summary>
    /// <remarks>C: asChalkBoardPilotOrder (0x00470518), initially 0..8.</remarks>
    public short[] ChalkBoardPilotOrder { get; } = [0, 1, 2, 3, 4, 5, 6, 7, 8];

    /// <summary>Campaign date of the last kill board visit (written, never read by the original).</summary>
    /// <remarks>C: stChalkBoardDate (0x00470514), initially {-1, -1}.</remarks>
    public CampaignDate ChalkBoardDate { get; set; } = new(-1, -1);

    /// <summary>"Talk to ...", the label of the left seat; only rewritten when the seat is assigned.</summary>
    /// <remarks>C: szTalkToFirstPilot (0x00470570).</remarks>
    public string TalkToFirstPilot { get; set; } = "Talk to ??????????????";

    /// <remarks>C: szTalkToSecondPilot (0x00470588).</remarks>
    public string TalkToSecondPilot { get; set; } = "Talk to ??????????????";

    /// <summary>
    /// The mission objectives written into a save game and read back by a load. The original
    /// saves aMissionObjectives, which the briefing and init_mission rebuild; until the flight
    /// layer provides them they are the ones of the last loaded save (all zero for a new game).
    /// </summary>
    /// <remarks>C: aMissionObjectives (0x1F0 bytes copied by SaveGameWithNamePrompt and LoadGameFromSlot).</remarks>
    public SavedObjective[] MissionObjectives { get; } = new SavedObjective[16];

    /// <summary>System name of the current series as last loaded from MODULE section 5 ($S).</summary>
    /// <remarks>C: abSeriesAuxData (written by LoadMissionData).</remarks>
    public string SystemName { get; set; } = "";

    /// <summary>The TrainSim session shared with the flight layer.</summary>
    public TrainSimSession TrainSim { get; } = new();
}
