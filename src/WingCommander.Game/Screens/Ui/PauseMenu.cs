using WingCommander.Core.Platform;
using WingCommander.Game.Input;
using WingCommander.Graphics.Palettes;

namespace WingCommander.Game.Screens.Ui;

/// <summary>Where the pause menu was opened; decides its items.</summary>
public enum PauseMenuContext
{
    /// <summary>Campaign flight (the simulation is paused while the menu is open).</summary>
    Flight,

    /// <summary>Training simulator flight: adds "End simulation" (the original's Esc).</summary>
    TrainingSimulator,

    /// <summary>Rec room, barracks and the title.</summary>
    Menu,
}

/// <summary>What the player chose in the pause menu.</summary>
public enum PauseMenuChoice
{
    Resume,

    /// <summary>Leave the training simulator (only offered there).</summary>
    EndSimulation,
}

/// <summary>
/// The port's pause menu (ADR-015), opened with Esc in flight, in the rooms and on the title:
/// Resume, Settings, End simulation (training simulator) and Quit game. Keyboard (arrows,
/// Enter, Space, Esc) and mouse. Quitting asks first, like the barracks' quit question, and ends
/// the game with <see cref="GameExitException"/>.
/// </summary>
public static class PauseMenu
{
    private const int Left = 85;
    private const int Right = 234;
    private const int Top = 52;

    private enum Item
    {
        Resume,
        Settings,
        EndSimulation,
        Quit,
    }

    /// <summary>Shows the menu over the current picture and returns the player's choice.</summary>
    public static async Task<PauseMenuChoice> ShowAsync(Wc1Game game, PauseMenuContext context)
    {
        ArgumentNullException.ThrowIfNull(game);
        Item[] items = context == PauseMenuContext.TrainingSimulator
            ? [Item.Resume, Item.Settings, Item.EndSimulation, Item.Quit]
            : [Item.Resume, Item.Settings, Item.Quit];
        var session = MenuSession.Begin(game, releaseMouse: context != PauseMenuContext.Menu);
        try
        {
            int selected = 0;
            int bottom = Top + 26 + items.Length * MenuSession.LineHeight + 4;
            while (true)
            {
                Draw(session, items, selected, bottom);
                var (type, e) = await session.NextEventAsync();
                int pointed = ItemAt(items, e.X, e.Y);
                Item? chosen = null;
                switch (type)
                {
                    case InputEventType.MouseMove:
                        if (pointed >= 0)
                            selected = pointed;
                        continue;
                    case InputEventType.ButtonDown:
                        if (pointed < 0)
                            continue;
                        selected = pointed;
                        chosen = items[pointed];
                        break;
                    case InputEventType.KeyDown:
                        switch ((short)e.Value)
                        {
                            case 0x48: // Up
                                selected = (selected + items.Length - 1) % items.Length;
                                continue;
                            case 0x50: // Down
                                selected = (selected + 1) % items.Length;
                                continue;
                            case 0x01: // Esc
                                return PauseMenuChoice.Resume;
                            case 0x1c or 0x39: // Enter, Space
                                chosen = items[selected];
                                break;
                            default:
                                continue;
                        }
                        break;
                }

                switch (chosen)
                {
                    case Item.Resume:
                        return PauseMenuChoice.Resume;
                    case Item.EndSimulation:
                        return PauseMenuChoice.EndSimulation;
                    case Item.Settings:
                        session.RestoreBackground();
                        await SettingsMenu.RunAsync(game, session);
                        session.RestoreBackground();
                        break;
                    case Item.Quit:
                        session.RestoreBackground();
                        if (await ModalPrompts.ConfirmAsync(game, "Quit Wing Commander? (Y/N)"))
                            throw new GameExitException("Quit from the pause menu");
                        game.Events.CursorShowCount = Math.Max(1, game.Events.CursorShowCount);
                        session.RestoreBackground();
                        break;
                }
            }
        }
        finally
        {
            session.End();
        }
    }

    private static void Draw(MenuSession session, Item[] items, int selected, int bottom)
    {
        session.DrawPanel(Left, Top, Right, bottom);
        session.DrawCentredText(Left, Right, Top + 7, "GAME PAUSED", PaletteColours.Yellow);
        for (int i = 0; i < items.Length; i++)
        {
            bool active = i == selected;
            string label = items[i] switch
            {
                Item.Resume => "Resume",
                Item.Settings => "Settings",
                Item.EndSimulation => "End simulation",
                _ => "Quit game",
            };
            session.DrawCentredText(Left, Right, ItemTop(i), active ? $"> {label} <" : label,
                active ? PaletteColours.ViewportClear : PaletteColours.LightGrey);
        }
    }

    private static int ItemTop(int index) => Top + 26 + index * MenuSession.LineHeight;

    private static int ItemAt(Item[] items, short x, short y)
    {
        if (x < Left || x > Right)
            return -1;
        for (int i = 0; i < items.Length; i++)
        {
            int top = ItemTop(i) - 2;
            if (y >= top && y < top + MenuSession.LineHeight)
                return i;
        }
        return -1;
    }
}
