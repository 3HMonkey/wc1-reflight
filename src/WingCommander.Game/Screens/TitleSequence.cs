using WingCommander.Core.Resources;
using WingCommander.Game.Input;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Game.Screens;

/// <summary>Values returned by the title menu (the original's menu option numbers).</summary>
public static class TitleSelection
{
    public const int NewGame = 0;
    public const int Continue = 1;
    public const int SecretMissions1 = 2;
    public const int SecretMissions2 = 3;
}

/// <summary>
/// The title: attract sequence (until a key or button), then the main menu built from TITLE.VGA
/// section 4: "start a new game" always, "continue" when SAVEGAME.WLD holds a game. Enter, Space
/// or a click activates the option under the pointer; S starts a new game; the cursor keys move
/// the pointer. Returns the selected option (<see cref="TitleSelection"/>).
/// </summary>
/// <remarks>
/// C: Title_Sequence (0x40FB70), UpdateTitleMenuCursor (0x40FB10), aTitleMenuRegions, nav.c;
/// FindMenuRegionAtPoint (0x43F7C0, killbrd.c). The attract part (canned 3D dogfight, title logo
/// zoom, credits) needs the flight engine and is skipped until it is ported, which is what the
/// original does after Esc. Joystick menu polling (PollMenuInputDevices) and the J joystick
/// calibration are not ported yet.
/// </remarks>
public sealed class TitleSequence(Wc1Game game)
{
    private const short Hidden = -1;

    /// <summary>
    /// Menu hit regions {frame, left, top, right, bottom}. Like the original global, they are
    /// rewritten with the bounds of the option frames every time the menu is built and keep them.
    /// </summary>
    private readonly MenuRegion[] _regions =
    [
        new(1, 49, 48, 283, 99),
        new(1, 49, 91, 283, 149),
        new(1, 49, 134, 283, 149),
        new(1, 49, 177, 283, 209),
        new(Hidden, 0, 0, 0, 0),
    ];

    private readonly short[] _menuOptions = new short[4];

    public async Task<int> RunAsync()
    {
        var events = game.Events;
        var gfx = game.Graphics;
        var display = game.Display;
        var screen = gfx.Screen!;
        int state = 0;

        if (!events.EscapePressed)
        {
            // The attract sequence (needs the flight layer) runs until a key or button.
            if (game.FlightLayer is { } flight)
                await flight.PlayAttractSequenceAsync();
            events.ClearInputKeyStatePreservingModifiers();
            events.FlushInputEvents();
        }
        events.EscapePressed = false;

        var menuShape = game.Resources.GetShape(LogicalFile.TitleVga, 4);
        ShapeTable? alternateMenuShape = null; // TITLE1.VGA frame 0: only for options >= 3, which the menu never offers
        _menuOptions.AsSpan().Fill(Hidden);
        _menuOptions[0] = TitleSelection.NewGame;
        if (game.AnySavedGames())
            _menuOptions[1] = TitleSelection.Continue;

        Span<short> bounds = stackalloc short[4];
        for (int i = 0; i < 4; i++)
        {
            ref var region = ref _regions[i];
            if (_menuOptions[i] == Hidden)
            {
                region.Frame = Hidden;
                continue;
            }
            region.Frame = 1;
            bounds[0] = region.Left;
            bounds[1] = region.Top;
            bounds[2] = region.Right;
            bounds[3] = region.Bottom;
            if (_menuOptions[i] < 3)
                ShapeBounds.GetShapeFrameBounds(bounds, region.Left, region.Top, menuShape, _menuOptions[i]);
            else
                ShapeBounds.GetShapeFrameBounds(bounds, region.Left, region.Top, alternateMenuShape, 0);
            (region.Left, region.Top, region.Right, region.Bottom) = (bounds[0], bounds[1], bounds[2], bounds[3]);
        }

        await display.ClearViewportAsync(screen, PaletteColours.Black);
        for (int i = 0; i < 4; i++)
        {
            if (_menuOptions[i] == Hidden)
                continue;
            if (_menuOptions[i] < 3)
                gfx.DrawSpriteDefault(screen, _regions[i].Left, _regions[i].Top, menuShape, _menuOptions[i]);
            else
                gfx.DrawSpriteDefault(screen, _regions[i].Left, _regions[i].Top, alternateMenuShape, 0);
        }
        await display.PresentAsync();

        game.Cursor.Viewport = screen;
        events.Pump = null;
        events.MenuInputRepeatDelay = 6;
        events.WarpMouseTo(160, 100);
        events.ShowCursor();
        events.InputMode = 1;
        events.KeyEventQueueEnabled = false;
        var e = new InputEventState();
        while (state == 0)
        {
            int selected = -1;
            bool activate = false;
            UpdateTitleMenuCursor();
            short type = events.PollInputEvent(ref e);
            if (type == InputEventType.ButtonDown)
            {
                activate = true;
            }
            else if (type is InputEventType.KeyDown or InputEventType.Character)
            {
                events.ClearInputKeyStatePreservingModifiers();
                short key = unchecked((short)e.Value);
                switch (key)
                {
                    case 0x1c: // Enter
                    case 0x1f: // S
                    case 0x2e: // C
                    case 0x39: // Space
                        if (key == 0x1f)
                            selected = 0;
                        // Original: C tests option 2 (never present), so it acts like Enter.
                        if (key == 0x2e && _menuOptions[2] != Hidden)
                            selected = 1;
                        activate = true;
                        break;
                    case 0x24: // J: joystick calibration (not ported)
                        break;
                    default:
                        events.MoveMenuPointerFromKeyboard(e);
                        break;
                }
            }
            if (activate)
            {
                if (selected == -1)
                    selected = FindMenuRegionAtPoint(e.X, e.Y);
                state = selected is < 0 or > 3 ? 0 : _menuOptions[selected] + 1;
            }
            await display.PresentAsync();
        }

        events.KeyEventQueueEnabled = true;
        events.Pump = null;
        events.HideCursor();
        var fade = gfx.BeginFadeViewportPaletteToColour(screen, PaletteColours.Black);
        while (fade.Step())
            await display.WaitForVerticalBlankAsync();
        await display.PresentAsync();
        await display.ClearViewportAsync(screen, PaletteColours.Black);
        await display.PresentAsync();
        gfx.Palette.RestoreGamePalette();
        return state - 1;
    }

    /// <summary>Cursor frame 1 over an active option, else 0 (scanning stops at the first hidden region).</summary>
    /// <remarks>C: UpdateTitleMenuCursor (0x40FB10).</remarks>
    private void UpdateTitleMenuCursor()
    {
        short frame = 0;
        short x = game.Events.HostMouse.X;
        short y = game.Events.HostMouse.Y;
        foreach (var region in _regions)
        {
            if (region.Frame == Hidden)
                break;
            if (region.Contains(x, y))
                frame = region.Frame;
        }
        game.Cursor.SetFrame(frame);
    }

    /// <remarks>C: FindMenuRegionAtPoint (0x43F7C0, killbrd.c).</remarks>
    private int FindMenuRegionAtPoint(short x, short y)
    {
        for (int i = 0; _regions[i].Frame != Hidden; i++)
        {
            if (_regions[i].Contains(x, y))
                return i;
        }
        return -1;
    }

    private struct MenuRegion(short frame, short left, short top, short right, short bottom)
    {
        public short Frame = frame;
        public short Left = left;
        public short Top = top;
        public short Right = right;
        public short Bottom = bottom;

        /// <remarks>C: IsPointInRect (0x435090, mathfp.c).</remarks>
        public readonly bool Contains(short x, short y) => Left <= x && x <= Right && Top <= y && y <= Bottom;
    }
}
