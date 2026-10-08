using WingCommander.Core.Rendering;
using WingCommander.Game.Config;
using WingCommander.Game.Input;
using WingCommander.Graphics.Palettes;

namespace WingCommander.Game.Screens.Ui;

/// <summary>
/// The settings of the pause menu (ADR-015): music and sound volume, fullscreen, picture filter,
/// aspect ratio, vertical sync, sharp text, fonts and the key help. Every change applies at once;
/// leaving the screen saves the settings (config.json and wc1.cfg). Rows the current run cannot
/// change (no window, no text renderer) are left out. Arrows select and change, Enter/Space
/// changes, Esc or "Back" leaves; the mouse selects rows, clicks change them and set the volume
/// bars directly.
/// </summary>
internal static class SettingsMenu
{
    private const int Left = 22;
    private const int Right = 297;
    private const int Top = 20;
    private const int ValueX = 166;
    private const int RowHeight = 13;
    private const int BarSegment = 9;

    private enum Row
    {
        Music,
        Sound,
        Fullscreen,
        Filter,
        Aspect,
        VSync,
        SharpText,
        Fonts,
        KeyHelp,
        Back,
    }

    public static async Task RunAsync(Wc1Game game, MenuSession session)
    {
        List<Row> rows = [Row.Music, Row.Sound];
        if (game.DisplayControl is not null)
            rows.AddRange([Row.Fullscreen, Row.Filter, Row.Aspect, Row.VSync]);
        if (game.SupportsSharpText)
            rows.Add(Row.SharpText);
        if (game.SupportsModernFonts)
            rows.Add(Row.Fonts);
        rows.AddRange([Row.KeyHelp, Row.Back]);

        int selected = 0;
        int bottom = Top + 24 + rows.Count * RowHeight + 4;
        try
        {
            while (true)
            {
                session.RestoreBackground();
                Draw(game, session, rows, selected, bottom);
                var (type, e) = await session.NextEventAsync();
                int pointed = RowAt(rows, e.X, e.Y);
                switch (type)
                {
                    case InputEventType.MouseMove:
                        if (pointed >= 0)
                            selected = pointed;
                        break;
                    case InputEventType.ButtonDown:
                        if (pointed < 0)
                            break;
                        selected = pointed;
                        if (rows[pointed] == Row.Back)
                            return;
                        if (rows[pointed] is Row.Music or Row.Sound && e.X >= ValueX - 4)
                            SetVolume(game, rows[pointed], Math.Clamp((e.X - ValueX) / BarSegment + 1, 0, UserSettings.MaxVolume));
                        else
                            Change(game, rows[pointed], +1);
                        break;
                    case InputEventType.KeyDown:
                        switch ((short)e.Value)
                        {
                            case 0x48: // Up
                                selected = (selected + rows.Count - 1) % rows.Count;
                                break;
                            case 0x50: // Down
                                selected = (selected + 1) % rows.Count;
                                break;
                            case 0x4b: // Left
                                Change(game, rows[selected], -1);
                                break;
                            case 0x4d: // Right
                                Change(game, rows[selected], +1);
                                break;
                            case 0x1c or 0x39: // Enter, Space
                                if (rows[selected] == Row.Back)
                                    return;
                                Change(game, rows[selected], +1);
                                break;
                            case 0x01: // Esc
                                return;
                        }
                        break;
                }
            }
        }
        finally
        {
            game.SaveSettings();
        }
    }

    private static void Change(Wc1Game game, Row row, int direction)
    {
        var display = game.DisplayControl;
        switch (row)
        {
            case Row.Music:
                SetVolume(game, row, game.Volumes.MusicVolume / 2 + direction);
                break;
            case Row.Sound:
                SetVolume(game, row, game.Volumes.SfxVolume / 2 + direction);
                break;
            case Row.Fullscreen when display is not null:
                display.Fullscreen = !display.Fullscreen;
                break;
            case Row.Filter when display is not null:
                display.Renderer.Filter = Cycle(display.Renderer.Filter, direction,
                    [ScalingFilter.SharpBilinear, ScalingFilter.Nearest, ScalingFilter.Linear]);
                break;
            case Row.Aspect when display is not null:
                display.Renderer.Aspect = display.Renderer.Aspect == AspectMode.FourByThree
                    ? AspectMode.SquarePixels
                    : AspectMode.FourByThree;
                break;
            case Row.VSync when display is not null:
                display.Renderer.VSync = !display.Renderer.VSync;
                break;
            case Row.SharpText:
                game.SetSharpText(!game.Preferences.SharpText);
                break;
            case Row.Fonts:
                game.SetModernFonts(!game.Preferences.ModernFonts);
                break;
            case Row.KeyHelp:
                game.SetKeyHelp(!game.Preferences.KeyHelp);
                break;
        }
    }

    private static void SetVolume(Wc1Game game, Row row, int level)
    {
        level = Math.Clamp(level, 0, UserSettings.MaxVolume);
        if (row == Row.Music)
            game.Volumes.MusicVolume = level * 2;
        else
            game.Volumes.SfxVolume = level * 2;
    }

    private static T Cycle<T>(T value, int direction, T[] order)
        where T : struct
    {
        int index = Array.IndexOf(order, value);
        return order[((index < 0 ? 0 : index) + direction + order.Length) % order.Length];
    }

    private static void Draw(Wc1Game game, MenuSession session, List<Row> rows, int selected, int bottom)
    {
        session.DrawPanel(Left, Top, Right, bottom);
        session.DrawCentredText(Left, Right, Top + 6, "SETTINGS", PaletteColours.Yellow);
        var display = game.DisplayControl;
        for (int i = 0; i < rows.Count; i++)
        {
            int y = RowTop(i);
            bool active = i == selected;
            byte labelColour = active ? PaletteColours.ViewportClear : PaletteColours.LightGrey;
            Row row = rows[i];
            if (row == Row.Back)
            {
                session.DrawCentredText(Left, Right, y, active ? "> Back <" : "Back", labelColour);
                continue;
            }
            session.DrawText(Left + 12, y, (active ? "> " : "") + Label(row), labelColour);
            switch (row)
            {
                case Row.Music:
                    DrawBar(session, y, game.Volumes.MusicVolume / 2);
                    break;
                case Row.Sound:
                    DrawBar(session, y, game.Volumes.SfxVolume / 2);
                    break;
                default:
                    session.DrawText(ValueX, y, Value(game, display, row), active ? PaletteColours.Yellow : PaletteColours.PrimaryText);
                    break;
            }
        }
    }

    private static string Label(Row row) => row switch
    {
        Row.Music => "Music volume",
        Row.Sound => "Sound volume",
        Row.Fullscreen => "Fullscreen",
        Row.Filter => "Picture filter",
        Row.Aspect => "Aspect ratio",
        Row.VSync => "Vertical sync",
        Row.SharpText => "Sharp text",
        Row.Fonts => "Fonts",
        _ => "Key help (F10)",
    };

    private static string Value(Wc1Game game, IDisplayControl? display, Row row) => row switch
    {
        Row.Fullscreen => OnOff(display?.Fullscreen ?? false),
        Row.Filter => display?.Renderer.Filter switch
        {
            ScalingFilter.Nearest => "Nearest",
            ScalingFilter.Linear => "Smooth",
            _ => "Sharp",
        },
        Row.Aspect => display?.Renderer.Aspect == AspectMode.SquarePixels ? "Square pixels" : "4:3",
        Row.VSync => OnOff(display?.Renderer.VSync ?? true),
        Row.SharpText => OnOff(game.Preferences.SharpText),
        Row.Fonts => game.Preferences.ModernFonts ? "Modern" : "Original",
        Row.KeyHelp => OnOff(game.Preferences.KeyHelp),
        _ => "",
    };

    private static string OnOff(bool value) => value ? "On" : "Off";

    private static void DrawBar(MenuSession session, int y, int level)
    {
        for (int i = 0; i < UserSettings.MaxVolume; i++)
        {
            int x = ValueX + i * BarSegment;
            session.Gfx.DrawFilledViewportRect(session.Screen, x, y + 2, x + BarSegment - 3, y + 8,
                i < level ? PaletteColours.PrimaryText : PaletteColours.DarkGrey);
        }
    }

    private static int RowTop(int index) => Top + 24 + index * RowHeight;

    private static int RowAt(List<Row> rows, short x, short y)
    {
        if (x < Left || x > Right)
            return -1;
        for (int i = 0; i < rows.Count; i++)
        {
            int top = RowTop(i) - 1;
            if (y >= top && y < top + RowHeight)
                return i;
        }
        return -1;
    }
}
