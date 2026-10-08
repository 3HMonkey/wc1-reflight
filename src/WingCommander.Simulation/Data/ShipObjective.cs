namespace WingCommander.Simulation.Data;

/// <summary>Second level of the AI hierarchy (<c>aeShipObjective</c>); for the player it is the
/// type of the current nav objective.</summary>
/// <remarks>C: enum ShipObjective (include/wcdata.h).</remarks>
public enum ShipObjective
{
    None = -1,
    NavPoint = 0,
    HomeBase = 1,
    Guard = 2,
    ReachShip = 3,
    DestroyShip = 4,
    Wander = 5,
    EngageEnemy = 6,
    EvadeEnemy = 7,
    HoldFormation = 8,
    BreakFormation = 9,
}
