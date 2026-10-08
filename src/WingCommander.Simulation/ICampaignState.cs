using WingCommander.Simulation.Data;

namespace WingCommander.Simulation;

/// <summary>
/// The parts of the persistent campaign record the simulation reads and writes. Implemented by
/// the Game's campaign state (so no copying is needed); <see cref="SimulationCampaignState"/> is
/// a standalone implementation for tests and tools.
/// </summary>
/// <remarks>C: CampaignState stCampaignState (include/wcdata.h): playerShipType, currentMission,
/// currentSeries, personalityDeathMission[8], aceFlags[4], promotionScore, missionScore.</remarks>
public interface ICampaignState
{
    /// <summary>The player's ship type (set from the mission's player record by <c>prepare_mission</c>).</summary>
    ObjectType PlayerShipType { get; set; }

    /// <summary>Score of the current mission (reset by <c>prepare_mission</c>, raised by kills).</summary>
    short MissionScore { get; set; }

    /// <summary>Promotion score (a dead Confed pilot costs 1, a dead Kilrathi ace gains 1).</summary>
    short PromotionScore { get; set; }

    /// <summary>Mission number inside the current series (<c>currentMission</c>).</summary>
    sbyte CurrentMission { get; }

    /// <summary>Current series (<c>currentSeries</c>).</summary>
    sbyte CurrentSeries { get; }

    /// <summary>0 while Confed personality <paramref name="personality"/> (0..7) is alive, else the
    /// <c>mission + series * 4</c> of the death.</summary>
    int GetPersonalityDeathMission(int personality);

    /// <summary>Records the death of Confed personality <paramref name="personality"/> (0..7).</summary>
    void SetPersonalityDeathMission(int personality, int missionIndex);

    /// <summary>Flags of Kilrathi ace <paramref name="ace"/> (0..3): 1 alive/in play, 2 killed,
    /// 4 met, 8 greeted, 0x10, 0x20 "survives once".</summary>
    byte GetAceFlags(int ace);

    void SetAceFlags(int ace, byte flags);
}
