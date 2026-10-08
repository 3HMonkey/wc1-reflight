using WingCommander.Game.Campaign;
using WingCommander.Game.Flow;
using WingCommander.Game.Screens.Rooms;
using WingCommander.Graphics.Raster;
using WingCommander.Simulation;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

/// <summary>
/// The space flight layer of the game (<see cref="IFlightLayer"/>, and the TrainSim menus' flight
/// seam <see cref="ITrainSimFlight"/>): owns the flight state of the game (<see cref="FlightSession"/>:
/// the simulation and the cockpit) and runs the flight loop, cockpit, HUD and flight sequences as
/// coroutines.
/// </summary>
public sealed class FlightLayer : IFlightLayer, ITrainSimFlight
{
    private FlightSession? _session;

    public FlightLayer(Wc1Game game, FlightOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        Game = game;
        Options = options ?? new FlightOptions();
    }

    public Wc1Game Game { get; }

    /// <summary>Presentation switches (ADR-012).</summary>
    public FlightOptions Options { get; }

    /// <summary>The flight state, created on first use (after the game initialised its fonts and palette).</summary>
    internal FlightSession Session => _session ??= new FlightSession(Game, Options);

    /// <summary>The space simulation of the game.</summary>
    public SpaceSimulation Simulation => Session.Sim;

    /// <summary>
    /// Kill counts and the wingman's personality, read from the simulation when the campaign flow
    /// asks (after the landing sequence, like the original's PostMission).
    /// </summary>
    public MissionStatistics MissionStatistics
    {
        get
        {
            if (_session is null)
                return default;
            var sim = _session.Sim;
            int wingman = sim.YourWingman;
            int personality = (uint)wingman < ObjectSlots.ShipSlotCount ? sim.Ships[wingman].Rating : -1;
            return new MissionStatistics(sim.PlayerKillCount, sim.WingmanKillCount, personality);
        }
    }

    /// <remarks>C: scramble (0x408200, brains.c) — milestone 5.</remarks>
    public Task ScrambleAsync() => Session.ScrambleAsync();

    /// <summary>init_mission, the launch and the flight; the arcade state becomes the result.</summary>
    /// <remarks>C: GameFlow (nav.c): init_mission(series, mission); LaunchPlayerShip(); RunSpaceFlight(-1).
    /// bKeyEventQueueEnabled is 1 from the briefing on.</remarks>
    public async Task<FlightResult> FlyMissionAsync(int series, int mission)
    {
        var session = Session;
        Game.Events.KeyEventQueueEnabled = true;
        session.PrepareCampaignData(trainingSimulator: false);
        if (!session.InitMission((short)series, (short)mission))
            return FlightResult.Aborted;
        await session.LaunchPlayerShipAsync();
        int state = await session.RunSpaceFlightAsync(-1);
        return (FlightResult)state;
    }

    /// <summary>One simulator mission (set up before "Get Ready" through <see cref="ITrainSimFlight"/>).</summary>
    public async Task<FlightResult> FlyTrainSimMissionAsync(int mission) =>
        (FlightResult)await Session.FlyTrainSimMissionAsync();

    /// <remarks>C: GameFlow after state 1: free_cockpit, ShowCarrierLaunchSequence(nPlayerCollisionObject),
    /// nArcadeState = 0, nPlayerCollisionObject = -1, free_3Space, landing(calculate_damage_level()).</remarks>
    public Task LandingSequenceAsync() => Session.LandingSequenceAsync();

    /// <remarks>C: GameFlow after state 2: ejection_sequence, check_stranded, stranded_sequence, free_3Space.</remarks>
    public Task<bool> EjectionSequenceAsync() => Session.EjectionSequenceAsync();

    /// <remarks>C: GameFlow after state 3: stranded_sequence, free_3Space.</remarks>
    public Task StrandedSequenceAsync() => Session.StrandedAfterFlightAsync();

    /// <remarks>C: GameFlow after state 4: death_sequence, free_3Space (the funeral is a Game scene).</remarks>
    public Task DeathSequenceAsync() => Session.DeathAfterFlightAsync();

    /// <remarks>C: GameFlow default branch: free_cockpit, free_all_slots, free_3Space.</remarks>
    public void AbortFlight()
    {
        if (_session is null)
            return;
        _session.FreeCockpit();
        _session.Sim.FreeAllSlots();
        _session.Sim.Free3Space();
    }

    public bool ObjectiveAchieved(int objective) =>
        _session is not null && (uint)objective < SpaceSimulation.ObjectiveCount && _session.Sim.Achieved((short)objective);

    public bool ObjectiveSighted(int objective) =>
        _session is not null && (uint)objective < SpaceSimulation.ObjectiveCount && _session.Sim.Sighted((short)objective);

    /// <summary>A canned 3D scene for the campaign endings (milestone 5).</summary>
    public ICannedSpaceScene? BeginCannedScene(int actionSphere) => Session.BeginCannedScene(actionSphere);

    /// <summary>The attract sequence of the title (milestone 5).</summary>
    public Task PlayAttractSequenceAsync() => Session.PlayAttractSequenceAsync();

    // ------------------------------------------------------------------ ITrainSimFlight

    Viewport ITrainSimFlight.SpaceBuffer => Session.SpaceBuffer;

    short ITrainSimFlight.ViewCenterX => Session.Sim.ViewCenterX;

    short ITrainSimFlight.ViewCenterY => Session.Sim.ViewCenterY;

    void ITrainSimFlight.BeginSession() => Session.BeginTrainSimSession();

    void ITrainSimFlight.InitializeMission(short mission) => Session.InitializeTrainSimMission(mission);

    void ITrainSimFlight.PrepareFlight(bool campaignStartup) => Session.PrepareTrainSimFlight(campaignStartup);

    void ITrainSimFlight.EndSession() => Session.EndTrainSimSession();

    void ITrainSimFlight.BeginGetReady() => Session.BeginGetReady();

    void ITrainSimFlight.EndGetReady() => Session.EndGetReady();

    void ITrainSimFlight.BeginVictory() => Session.Sim.FrameSkipCounter = 1;

    void ITrainSimFlight.BeginGameOver() => Session.BeginGameOver();

    bool ITrainSimFlight.RefreshCockpitStatus() => Session.RefreshCockpitStatus();

    void ITrainSimFlight.DumpBufferToScreen() => Session.DumpBufferToScreen();
}
