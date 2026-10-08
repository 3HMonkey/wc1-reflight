using WingCommander.Simulation.Data;

namespace WingCommander.Simulation;

/// <summary>Standalone <see cref="ICampaignState"/> (all pilots alive, no ace flags) for tests and tools.</summary>
public sealed class SimulationCampaignState : ICampaignState
{
    private readonly int[] _personalityDeathMission = new int[8];
    private readonly byte[] _aceFlags = new byte[4];

    public ObjectType PlayerShipType { get; set; }

    public short MissionScore { get; set; }

    public short PromotionScore { get; set; }

    public sbyte CurrentMission { get; set; }

    public sbyte CurrentSeries { get; set; }

    public int GetPersonalityDeathMission(int personality) => _personalityDeathMission[personality];

    public void SetPersonalityDeathMission(int personality, int missionIndex) => _personalityDeathMission[personality] = missionIndex;

    public byte GetAceFlags(int ace) => _aceFlags[ace];

    public void SetAceFlags(int ace, byte flags) => _aceFlags[ace] = flags;
}
