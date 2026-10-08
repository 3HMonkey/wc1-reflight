using WingCommander.Core.Resources;
using WingCommander.Graphics.Cockpit;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Game.Screens.Rooms;

/// <summary>
/// Stand-in for the flight engine while the flight layer is not integrated: the mission
/// set-up and clean-up do nothing, and the captions are shown over the simulator console art
/// (PCSHIP.V04 section 0) with an empty space view (its single view geometry, 232x81 at 40,29)
/// filled with the space colour. Every rendered frame pumps the event queue, as the flight
/// frame does, so Esc can skip the captions.
/// </summary>
public sealed class FallbackTrainSimFlight : ITrainSimFlight
{
    /// <summary>Logical file of the simulator cockpit (cCockpitLogicalFile = 21, PCSHIP.V04).</summary>
    public const int SimulatorCockpitFile = LogicalFile.PcShipV00 + 4;

    private readonly Wc1Game _game;
    private readonly ShapeTable _cockpit;
    private readonly ViewGeometry _geometry;

    public FallbackTrainSimFlight(Wc1Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        _game = game;
        _cockpit = game.Resources.GetShape(SimulatorCockpitFile, 0);
        _geometry = ViewGeometrySet.Parse("PCSHIP.V04", game.Resources.GetSection(SimulatorCockpitFile, ViewGeometrySet.PcShipSection).Span)[0];
        SpaceBuffer = Viewport.Allocate(0, 0, _geometry.Width - 1, _geometry.Height - 1, PaletteColours.PrimaryViewBuffer);
        ViewCenterX = (short)(_geometry.Width / 2);
        ViewCenterY = (short)(_geometry.Height / 2);
    }

    public Viewport SpaceBuffer { get; }

    public short ViewCenterX { get; }

    public short ViewCenterY { get; }

    public void BeginSession()
    {
    }

    public void InitializeMission(short mission)
    {
    }

    public void PrepareFlight(bool campaignStartup)
    {
    }

    public void EndSession()
    {
    }

    public void BeginGetReady() => DrawCockpit();

    public void EndGetReady()
    {
        _game.Graphics.ClearViewport(SpaceBuffer, PaletteColours.PrimaryViewBuffer);
        RoomSound.StopAllSounds(_game);
    }

    public void BeginVictory() => DrawCockpit();

    public void BeginGameOver() => DrawCockpit();

    public bool RefreshCockpitStatus()
    {
        _game.Events.PumpWindowMessages();
        _game.Graphics.ClearViewport(SpaceBuffer, PaletteColours.PrimaryViewBuffer);
        return true;
    }

    public void DumpBufferToScreen() => _game.Graphics.FizzleFade(SpaceBuffer, _game.Graphics.Screen!, _geometry);

    private void DrawCockpit() => _game.Graphics.DrawSpriteDefault(_game.Graphics.Screen!, 0, 0, _cockpit, 0);
}
