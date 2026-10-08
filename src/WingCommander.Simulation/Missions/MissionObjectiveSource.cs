namespace WingCommander.Simulation.Missions;

/// <summary>
/// One objective / flight-plan record as loaded from MODULE section 2: <see cref="Type"/> -1 ends
/// the list, 0 = nav point (<see cref="Index"/> = nav), 1..4 = mobile objective
/// (<see cref="Index"/> = mission ship; 1 home/carrier, 2 escort, 3 "green circle" reach,
/// 4 "red circle" destroy).
/// </summary>
/// <remarks>C: MissionObjectiveSource (0x42 bytes, include/wcdata.h), aMissionObjectiveSources[16].</remarks>
public struct MissionObjectiveSource
{
    /// <summary>+0x00 objective type (sign-extended disk short).</summary>
    public int Type;

    /// <summary>+0x04 nav point or mission ship index.</summary>
    public short Index;

    /// <summary>+0x06 description text (char[60]); a leading '.' hides the objective.</summary>
    public string Description;

    public override readonly string ToString() => $"type {Type} index {Index}: {Description}";
}
