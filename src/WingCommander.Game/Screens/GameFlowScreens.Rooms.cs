using WingCommander.Game.Screens.Rooms;

namespace WingCommander.Game.Screens;

public sealed partial class GameFlowScreens
{
    private RoomsState? _roomState;

    /// <summary>Room state that lives as long as the game (kill board order, saved objectives, TrainSim session).</summary>
    public RoomsState RoomState => _roomState ??= new RoomsState();

    /// <summary>The TrainSim session (arcade score, enemy, cockpit file) shared with the flight layer.</summary>
    public TrainSimSession TrainSim => RoomState.TrainSim;

    /// <summary>
    /// The flight engine's part of a TrainSim session (mission set-up, the space view behind the
    /// Get Ready / Victory / Game Over captions, clean-up). When null, a
    /// <see cref="Wc1Game.FlightLayer"/> that implements <see cref="ITrainSimFlight"/> is used,
    /// otherwise <see cref="FallbackTrainSimFlight"/>.
    /// </summary>
    public ITrainSimFlight? TrainSimFlight { get; set; }

    /// <summary>Rec room (bar): talk to pilots, kill board, barracks door, simulator.</summary>
    /// <remarks>C: RecRoom (0x43F940, killbrd.c). Returns 5 for the simulator, 4 for the barracks door.</remarks>
    public Task<int> RecRoomAsync() => new RecRoom(Game, this, RoomState).RunAsync();

    /// <summary>Barracks: bunks (save and load), quit, mission hangar (7), back to the bar (8).</summary>
    /// <remarks>C: BarracksScreen (0x41C170, barracks.c). Quitting throws GameExitException.</remarks>
    public Task<int> BarracksScreenAsync() => new Barracks(Game, RoomState).RunAsync();

    /// <summary>TrainSim arcade with its menus (the forced first session ends with name entry).</summary>
    /// <remarks>C: RunTrainSim (0x427080, system.c).</remarks>
    public Task RunTrainSimAsync() => new TrainSim(Game, this, RoomState.TrainSim).RunAsync();
}
