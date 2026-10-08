namespace WingCommander.Simulation;

/// <summary>The yellow messages of the right HUD message slot that the simulation raises
/// (<see cref="ISimulationEvents.ShowCockpitMessage"/>).</summary>
/// <remarks>C: the reason strings of auto_pilot_valid (0x414380, cockpt.c) and szWaitForFormat,
/// szObjectiveReached, szAlreadyVisited of flag_reached (0x415530, cockpt.c).</remarks>
public enum SimulationCockpitMessage
{
    /// <summary>"Already Near": autopilot refused, the current objective is within 8000.</summary>
    AlreadyNear,

    /// <summary>"Enemy Near": autopilot refused, a Kilrathi ship is within 16000.</summary>
    EnemyNear,

    /// <summary>"Hazard Near": autopilot refused, a hazard field is active.</summary>
    HazardNear,

    /// <summary>"Wait for %s" with the display name of the ship type passed along (the ship the player
    /// escorts, or the one coming home).</summary>
    WaitFor,

    /// <summary>"Objective Reached".</summary>
    ObjectiveReached,

    /// <summary>"Already Visited".</summary>
    AlreadyVisited,
}
