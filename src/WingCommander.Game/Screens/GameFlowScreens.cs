using WingCommander.Game.Flow;

namespace WingCommander.Game.Screens;

/// <summary>
/// The screens and sequences the campaign flow drives (<see cref="IGameFlowScreens"/>), split
/// into partial files by area so they can be ported independently:
/// <list type="bullet">
/// <item>GameFlowScreens.Rooms.cs: rec room, barracks (bunks, save and load), TrainSim menus.</item>
/// <item>GameFlowScreens.Scenes.cs: briefing, debriefing, office, funerals, medals, MIDGAME
/// scenes, hangar scene, endings.</item>
/// <item>GameFlowScreens.Flight.cs: scramble, space flight, landing, ejection, death.</item>
/// </list>
/// Members that are not ported yet show a placeholder and lead back to the title.
/// </summary>
public sealed partial class GameFlowScreens(Wc1Game game) : IGameFlowScreens
{
    /// <summary>The game the screens run in.</summary>
    public Wc1Game Game { get; } = game;

    public void PumpWindowMessages() => Game.Events.PumpWindowMessages();

    /// <summary>Shows the placeholder for a part of the game that is not ported yet.</summary>
    private Task NotPortedAsync(string what) => Game.ShowNotPortedAsync(what);
}
