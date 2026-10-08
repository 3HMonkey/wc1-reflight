namespace WingCommander.Simulation.Data;

/// <summary>Third level of the AI hierarchy (<c>aeShipTactic</c>).</summary>
/// <remarks>C: enum ShipTactic (include/wcdata.h); original spelling "TARGETTING".</remarks>
public enum ShipTactic
{
    None = -1,
    Cruise = 0,
    SitStill = 1,
    ScoutAhead = 2,
    LagBehind = 3,
    Ram = 4,
    AvoidObject = 5,
    WarpOut = 6,
    WarpIn = 7,
    HeadHome = 8,
    Chase = 9,
    LookOut = 10,
    ApproachTarget = 11,
    Targetting = 12,
    ShakeEnemy = 13,
    ZipAway = 14,
    Retreat = 15,
    SelfDefense = 16,
    PickAttack = 17,
}
