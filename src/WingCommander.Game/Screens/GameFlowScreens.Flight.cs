using WingCommander.Game.Campaign;
using WingCommander.Game.Flow;

namespace WingCommander.Game.Screens;

/// <summary>
/// The flight members delegate to <see cref="Wc1Game.FlightLayer"/> (the Game.Flight project);
/// without one they show the placeholder and end the flight as aborted.
/// </summary>
public sealed partial class GameFlowScreens
{
    private IFlightLayer? Flight => Game.FlightLayer;

    /// <summary>Type of the player ship in the mission just briefed (from the MODULE data the briefing loaded).</summary>
    /// <remarks>C: <c>aMissionShips[nPlayerMissionShipIndex].type</c> after Briefing.</remarks>
    public int BriefedPlayerShipType => Director.Mission?.PlayerShipType ?? 0;

    /// <summary>Kill counts and the wingman of the mission just flown.</summary>
    public MissionStatistics MissionStatistics => Flight?.MissionStatistics ?? default;

    /// <remarks>C: scramble.</remarks>
    public Task ScrambleAsync() => Flight?.ScrambleAsync() ?? Task.CompletedTask;

    /// <remarks>C: init_mission + LaunchPlayerShip + RunSpaceFlight(-1).</remarks>
    public async Task<FlightResult> FlyMissionAsync(int series, int mission)
    {
        if (Flight is { } flight)
            return await flight.FlyMissionAsync(series, mission);
        await NotPortedAsync("Space flight");
        return FlightResult.Aborted;
    }

    /// <summary>One TrainSim arcade mission (init_mission(0, mission), flight).</summary>
    /// <remarks>C: the flight part of RunTrainSim (system.c).</remarks>
    public async Task<FlightResult> FlyTrainSimMissionAsync(int mission)
    {
        if (Flight is { } flight)
            return await flight.FlyTrainSimMissionAsync(mission);
        await NotPortedAsync("TrainSim flight");
        return FlightResult.Killed;
    }

    /// <remarks>C: ShowCarrierLaunchSequence + landing.</remarks>
    public Task LandingSequenceAsync() => Flight?.LandingSequenceAsync() ?? Task.CompletedTask;

    /// <remarks>C: ejection_sequence + check_stranded.</remarks>
    public Task<bool> EjectionSequenceAsync() => Flight?.EjectionSequenceAsync() ?? Task.FromResult(false);

    /// <remarks>C: stranded_sequence.</remarks>
    public Task StrandedSequenceAsync() => Flight?.StrandedSequenceAsync() ?? Task.CompletedTask;

    /// <remarks>C: death_sequence, free_3Space (flight layer), then funeral_sequence(1) (0x408DE0, brains.c).</remarks>
    public async Task DeathSequenceAsync()
    {
        if (Flight is { } flight)
            await flight.DeathSequenceAsync();
        await Director.FuneralSequenceAsync(playerFuneral: true);
    }

    /// <remarks>C: the Alt-X clean-up after RunSpaceFlight.</remarks>
    public void AbortFlight() => Flight?.AbortFlight();

    /// <remarks>C: achieved(objective).</remarks>
    public bool ObjectiveAchieved(int objective) => Flight?.ObjectiveAchieved(objective) ?? false;

    /// <summary>Whether the objective was sighted in the mission just flown (objective flag 4).</summary>
    public bool ObjectiveSighted(int objective) => Flight?.ObjectiveSighted(objective) ?? false;
}
