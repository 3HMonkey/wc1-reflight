namespace WingCommander.Simulation.Data;

/// <summary>Top level of the AI hierarchy (<c>aeShipMissionType</c>).</summary>
/// <remarks>C: enum ShipMissionType (include/wcdata.h).</remarks>
public enum ShipMissionType
{
    None = -1,
    Patrol = 0,
    Escort = 1,
    Strike = 2,
    Defend = 3,
    Wingman = 4,
    /// <summary>Flee.</summary>
    Rout = 5,
    GotoWarp = 6,
    WarpArrive = 7,
    CannedSequence = 8,
    Rendezvous = 9,
    ComeHome = 10,
    BogusAvoidCrash = 11,
}
