using WingCommander.Game.Campaign;
using WingCommander.Simulation;
using WingCommander.Simulation.Data;

namespace WingCommander.Game.Flight.Bridges;

/// <summary>
/// The simulation's view of the live campaign record: reads and writes
/// <see cref="CampaignSession.State"/> directly (the session may swap the state object on reset
/// or load, so it is looked up on every access).
/// </summary>
/// <remarks>C: stCampaignState fields used by the flight code (include/wcdata.h).</remarks>
internal sealed class CampaignBridge(CampaignSession session) : ICampaignState
{
    private CampaignState State => session.State;

    public ObjectType PlayerShipType
    {
        get => (ObjectType)State.PlayerShipType;
        set => State.PlayerShipType = (int)value;
    }

    public short MissionScore
    {
        get => State.MissionScore;
        set => State.MissionScore = value;
    }

    public short PromotionScore
    {
        get => State.PromotionScore;
        set => State.PromotionScore = value;
    }

    public sbyte CurrentMission => State.CurrentMission;

    public sbyte CurrentSeries => State.CurrentSeries;

    public int GetPersonalityDeathMission(int personality) => State.PersonalityDeathMission[personality];

    public void SetPersonalityDeathMission(int personality, int missionIndex) =>
        State.PersonalityDeathMission[personality] = missionIndex;

    public byte GetAceFlags(int ace) => State.AceFlags[ace];

    public void SetAceFlags(int ace, byte flags) => State.AceFlags[ace] = flags;
}
