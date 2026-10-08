using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;

namespace WingCommander.Game.Screens.Ui;

/// <summary>
/// The viewports of a room screen: an alias of the screen to draw on, an off-screen 320x200
/// scene buffer (the room is composed there and copied to the screen) and the viewports the
/// cursor is drawn into.
/// </summary>
/// <remarks>C: InitializeRoomViewports (0x43F810, killbrd.c); stRoomScreenViewport, stSceneBuffer,
/// stRoomMouseViewport, stRoomDisplayViewport, nSavedRoomControllerX.</remarks>
public sealed class RoomViewports
{
    private RoomViewports(Viewport screen, Viewport sceneBuffer)
    {
        Screen = screen;
        SceneBuffer = sceneBuffer;
        Mouse = sceneBuffer.Clone();
        Display = screen.Clone();
    }

    /// <summary>Alias of the screen with the screen's rectangle at entry.</summary>
    /// <remarks>C: stRoomScreenViewport.</remarks>
    public Viewport Screen { get; }

    /// <summary>The room's 320x200 composition buffer.</summary>
    /// <remarks>C: stSceneBuffer.</remarks>
    public Viewport SceneBuffer { get; }

    /// <remarks>C: stRoomMouseViewport.</remarks>
    public Viewport Mouse { get; }

    /// <summary>Where the cursor is drawn while the room runs (a copy of <see cref="Screen"/>).</summary>
    /// <remarks>C: stRoomDisplayViewport.</remarks>
    public Viewport Display { get; }

    /// <summary>The menu repeat delay to restore when the room closes.</summary>
    /// <remarks>C: nSavedRoomControllerX.</remarks>
    public short SavedMenuInputRepeatDelay { get; set; }

    /// <summary>
    /// Aliases the screen, allocates the black scene buffer and sets the menu repeat delay to 6.
    /// The joystick menu pump (PollMenuInputDevices) is a no-op without a joystick and is not installed.
    /// </summary>
    /// <remarks>C: InitializeRoomViewports (0x43F810, killbrd.c). EventManagerHook is empty.</remarks>
    public static RoomViewports Initialize(Wc1Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var screen = game.Graphics.Screen!.Clone();
        var sceneBuffer = Viewport.Allocate(0, 0, 319, 199, PaletteColours.Black);
        var viewports = new RoomViewports(screen, sceneBuffer);
        game.Events.Pump = null;
        viewports.SavedMenuInputRepeatDelay = game.Events.MenuInputRepeatDelay;
        game.Events.MenuInputRepeatDelay = 6;
        return viewports;
    }

    /// <summary>Releases the scene buffer.</summary>
    /// <remarks>C: free_viewport(&amp;stSceneBuffer).</remarks>
    public void Free() => SceneBuffer.FreeViewport();
}
