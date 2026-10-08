using WingCommander.Game.Campaign;

namespace WingCommander.Game.Flow;

/// <summary>
/// Everything that needs the space simulation: missions, the TrainSim arcade, the flight
/// sequences and the attract scenes of the title. Implemented by the WingCommander.Game.Flight
/// project (which references the Simulation library) and plugged into
/// <see cref="Wc1Game.FlightLayer"/> by the executable, so the Game project builds and tests
/// without the simulation. Members mirror the flight part of the campaign flow.
/// </summary>
public interface IFlightLayer
{
    /// <summary>Kill counts and the wingman of the mission just flown.</summary>
    MissionStatistics MissionStatistics { get; }

    /// <remarks>C: scramble.</remarks>
    Task ScrambleAsync();

    /// <summary>init_mission + LaunchPlayerShip + RunSpaceFlight(-1).</summary>
    Task<FlightResult> FlyMissionAsync(int series, int mission);

    /// <summary>
    /// The flight of one TrainSim arcade mission (RunSpaceFlight). The mission is set up before
    /// through <see cref="Screens.Rooms.ITrainSimFlight"/> (init_mission before "Get Ready"), which
    /// the flight layer also implements.
    /// </summary>
    Task<FlightResult> FlyTrainSimMissionAsync(int mission);

    /// <summary>free_cockpit, ShowCarrierLaunchSequence, free_3Space, landing(calculate_damage_level()).</summary>
    Task LandingSequenceAsync();

    /// <summary>ejection_sequence + check_stranded; true when stranded (then stranded_sequence ran).</summary>
    Task<bool> EjectionSequenceAsync();

    Task StrandedSequenceAsync();

    /// <summary>death_sequence and free_3Space; the player funeral that follows is a Game scene.</summary>
    Task DeathSequenceAsync();

    /// <summary>Flight quit with Alt-X: free cockpit, slots and 3D space.</summary>
    void AbortFlight();

    /// <summary>achieved(objective) of the mission just flown.</summary>
    bool ObjectiveAchieved(int objective);

    /// <summary>Whether objective <paramref name="objective"/> was sighted (objective flag 4) in the mission just flown.</summary>
    bool ObjectiveSighted(int objective);

    /// <summary>Starts a canned 3D scene for a cutscene (see <see cref="CannedScene"/>); null when the layer cannot show it.</summary>
    ICannedSpaceScene? BeginCannedScene(int actionSphere);

    /// <summary>
    /// The attract sequence of the title (canned dogfight, title logo, credits) until a key or
    /// button; returns when the player wants the menu.
    /// </summary>
    /// <remarks>C: the first half of Title_Sequence (nav.c).</remarks>
    Task PlayAttractSequenceAsync();
}
