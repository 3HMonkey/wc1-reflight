namespace WingCommander.Simulation.Data;

/// <summary>
/// Special flight state (<c>aeSpecialManeuver</c>). <see cref="BlowingUp"/> is the tumbling
/// "lost control" state in practice; <see cref="Unknown9"/> marks a dying (exploding) ship.
/// Higher values have priority in <c>set_special</c>.
/// </summary>
/// <remarks>C: enum SpecialManeuver (include/wcdata.h).</remarks>
public enum SpecialManeuver
{
    None = -1,
    Normal = 0,
    Afterburner = 1,
    BogusLoop = 2,
    SuperBrake = 3,
    BogusPush = 4,
    KillEngines = 5,
    StopDrift = 6,
    LostControl = 7,
    BlowingUp = 8,
    /// <summary>Dying: the ship is exploding, <c>asObjectCounter</c> drives the sequence.</summary>
    Unknown9 = 9,
}
