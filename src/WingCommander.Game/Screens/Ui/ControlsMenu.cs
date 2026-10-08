using WingCommander.Game.Input;
using WingCommander.Graphics.Palettes;

namespace WingCommander.Game.Screens.Ui;

/// <summary>
/// The flight keys page of the settings (ADR-016): every flight control with its key, grouped
/// like the key help, in a scrolling list. Enter or a click waits for the new key (Esc cancels);
/// a key another control has is swapped with it, so every control keeps one key. Del restores a
/// control's original key, "Reset to original keys" all of them. The changes apply at once; the
/// settings screen saves them when it is left.
/// </summary>
internal static class ControlsMenu
{
    private const int Left = 22;
    private const int Right = 297;
    private const int Top = 6;
    private const int Bottom = 193;
    private const int ListTop = Top + 22;
    private const int RowHeight = 12;
    private const int VisibleRows = 12;
    private const int KeyX = 190;
    private const int HintY = Bottom - 16;

    private enum RowKind
    {
        Heading,
        Action,
        Reset,
        Back,
    }

    private readonly record struct Row(RowKind Kind, string Text, FlightAction Action);

    private sealed class State
    {
        public int Selected;
        public int Scroll;
        public bool Capturing;
        public bool WaitForRelease;
        public string? Message;
        public byte MessageColour;
    }

    public static async Task RunAsync(Wc1Game game, MenuSession session)
    {
        List<Row> rows = BuildRows();
        var state = new State { Selected = Next(rows, -1, +1) };
        while (true)
        {
            session.RestoreBackground();
            Draw(game, session, rows, state);
            var (type, e) = await session.NextEventAsync(keyReleases: state.Capturing);
            if (state.Capturing)
            {
                Capture(game, rows, state, type, (short)e.Value);
                continue;
            }
            state.Message = null;
            int pointed = RowAt(state, rows, e.X, e.Y);
            switch (type)
            {
                case InputEventType.MouseMove:
                    if (pointed >= 0 && rows[pointed].Kind != RowKind.Heading)
                        state.Selected = pointed;
                    break;
                case InputEventType.ButtonDown:
                    if (pointed < 0 || rows[pointed].Kind == RowKind.Heading)
                        break;
                    state.Selected = pointed;
                    if (Activate(game, rows, state, byKey: false))
                        return;
                    break;
                case InputEventType.KeyDown:
                    switch ((short)e.Value)
                    {
                        case 0x48: // Up
                            Select(rows, state, Next(rows, state.Selected, -1));
                            break;
                        case 0x50: // Down
                            Select(rows, state, Next(rows, state.Selected, +1));
                            break;
                        case 0x49: // PgUp
                            Select(rows, state, Page(rows, state.Selected, -1));
                            break;
                        case 0x51: // PgDn
                            Select(rows, state, Page(rows, state.Selected, +1));
                            break;
                        case 0x47: // Home
                            Select(rows, state, Next(rows, -1, +1));
                            break;
                        case 0x4f: // End
                            Select(rows, state, rows.Count - 1);
                            break;
                        case 0x0d: // the mouse wheel arrives as = and -
                            ScrollBy(rows, state, -3);
                            break;
                        case 0x0c:
                            ScrollBy(rows, state, +3);
                            break;
                        case 0x53 or 0x0e: // Del, Backspace: the original key
                            if (rows[state.Selected].Kind == RowKind.Action)
                                Bind(game, state, rows[state.Selected].Action, KeyBindings.Info(rows[state.Selected].Action).DefaultKey);
                            break;
                        case 0x1c or 0x39: // Enter, Space
                            if (Activate(game, rows, state, byKey: true))
                                return;
                            break;
                        case 0x01: // Esc
                            return;
                    }
                    break;
            }
        }
    }

    private static List<Row> BuildRows()
    {
        var rows = new List<Row>();
        string? section = null;
        foreach (var info in KeyBindings.Actions)
        {
            if (info.Section != section)
            {
                section = info.Section;
                rows.Add(new Row(RowKind.Heading, section, default));
            }
            rows.Add(new Row(RowKind.Action, info.Name, info.Action));
        }
        rows.Add(new Row(RowKind.Reset, "Reset to original keys", default));
        rows.Add(new Row(RowKind.Back, "Back", default));
        return rows;
    }

    /// <summary>Enter or a click on the selected row; true when the page is left.</summary>
    private static bool Activate(Wc1Game game, List<Row> rows, State state, bool byKey)
    {
        switch (rows[state.Selected].Kind)
        {
            case RowKind.Action:
                state.Capturing = true;
                state.WaitForRelease = byKey; // the key that started the wait repeats until released
                break;
            case RowKind.Reset:
                game.Preferences.Controls.Reset();
                game.ApplyControls();
                state.Message = "The original keys are back";
                state.MessageColour = PaletteColours.Yellow;
                break;
            case RowKind.Back:
                return true;
        }
        return false;
    }

    /// <summary>An event while waiting for the new key of the selected control.</summary>
    private static void Capture(Wc1Game game, List<Row> rows, State state, short type, short scanCode)
    {
        switch (type)
        {
            case InputEventType.KeyUp:
                state.WaitForRelease = false;
                break;
            case InputEventType.ButtonDown:
                state.Capturing = false;
                state.Message = null;
                break;
            case InputEventType.KeyDown:
                if (state.WaitForRelease)
                    break;
                if (scanCode == 0x01)
                {
                    state.Capturing = false;
                    state.Message = null;
                    break;
                }
                if (!KeyBindings.CanBind(scanCode))
                {
                    state.Message = $"{GameKeys.Name(scanCode)} cannot be used here";
                    state.MessageColour = PaletteColours.Red;
                    break;
                }
                state.Capturing = false;
                Bind(game, state, rows[state.Selected].Action, scanCode);
                break;
        }
    }

    /// <summary>Puts the control on the key; tells which control took the old key in exchange.</summary>
    private static void Bind(Wc1Game game, State state, FlightAction action, int scanCode)
    {
        var controls = game.Preferences.Controls;
        int old = controls[action];
        FlightAction? swapped = controls.Bind(action, scanCode);
        game.ApplyControls();
        if (swapped is { } other)
        {
            state.Message = $"{KeyBindings.Info(other).Name} is now on {GameKeys.Name(old)}";
            state.MessageColour = PaletteColours.Yellow;
        }
    }

    private static bool Selectable(List<Row> rows, int index) =>
        (uint)index < (uint)rows.Count && rows[index].Kind != RowKind.Heading;

    /// <summary>The next selectable row after <paramref name="from"/> in <paramref name="direction"/>, wrapping around.</summary>
    private static int Next(List<Row> rows, int from, int direction)
    {
        int index = from;
        for (int i = 0; i < rows.Count; i++)
        {
            index = (index + direction + rows.Count) % rows.Count;
            if (Selectable(rows, index))
                return index;
        }
        return Math.Max(from, 0);
    }

    /// <summary>A page up or down, stopping at the first and last selectable rows.</summary>
    private static int Page(List<Row> rows, int from, int direction)
    {
        int index = Math.Clamp(from + direction * (VisibleRows - 1), 0, rows.Count - 1);
        while (!Selectable(rows, index))
            index += index == 0 ? 1 : direction;
        return index;
    }

    private static void Select(List<Row> rows, State state, int index)
    {
        state.Selected = index;
        if (index < state.Scroll)
            state.Scroll = index > 0 && rows[index - 1].Kind == RowKind.Heading ? index - 1 : index;
        else if (index >= state.Scroll + VisibleRows)
            state.Scroll = index - VisibleRows + 1;
        state.Scroll = Math.Clamp(state.Scroll, 0, Math.Max(0, rows.Count - VisibleRows));
    }

    /// <summary>Scrolls the list; the selection moves along when it would leave the visible rows.</summary>
    private static void ScrollBy(List<Row> rows, State state, int delta)
    {
        state.Scroll = Math.Clamp(state.Scroll + delta, 0, Math.Max(0, rows.Count - VisibleRows));
        if (state.Selected < state.Scroll)
            state.Selected = Next(rows, state.Scroll - 1, +1);
        else if (state.Selected >= state.Scroll + VisibleRows)
            state.Selected = Next(rows, state.Scroll + VisibleRows, -1);
    }

    private static int RowAt(State state, List<Row> rows, short x, short y)
    {
        if (x < Left || x > Right || y < ListTop - 1)
            return -1;
        int index = state.Scroll + (y - (ListTop - 1)) / RowHeight;
        return index < state.Scroll + VisibleRows && index < rows.Count ? index : -1;
    }

    private static void Draw(Wc1Game game, MenuSession session, List<Row> rows, State state)
    {
        session.DrawPanel(Left, Top, Right, Bottom);
        session.DrawCentredText(Left, Right, Top + 6, "FLIGHT KEYS", PaletteColours.Yellow);
        var controls = game.Preferences.Controls;
        for (int i = 0; i < VisibleRows && state.Scroll + i < rows.Count; i++)
        {
            int index = state.Scroll + i;
            Row row = rows[index];
            int y = ListTop + i * RowHeight;
            bool active = index == state.Selected;
            byte labelColour = active ? PaletteColours.ViewportClear : PaletteColours.LightGrey;
            switch (row.Kind)
            {
                case RowKind.Heading:
                    session.DrawText(Left + 10, y, row.Text, PaletteColours.Yellow);
                    break;
                case RowKind.Action:
                    session.DrawText(Left + 18, y, (active ? "> " : "") + row.Text, labelColour);
                    if (active && state.Capturing)
                        session.DrawText(KeyX, y, "Press a key", PaletteColours.Yellow);
                    else
                        session.DrawText(KeyX, y, GameKeys.Name(controls[row.Action]),
                            active ? PaletteColours.Yellow : PaletteColours.PrimaryText);
                    break;
                default:
                    session.DrawCentredText(Left, Right, y, active ? $"> {row.Text} <" : row.Text, labelColour);
                    break;
            }
        }
        DrawScrollBar(session, rows.Count, state.Scroll);

        string hint;
        byte hintColour = PaletteColours.LightGrey;
        if (state.Message is { } message)
        {
            hint = message;
            hintColour = state.MessageColour;
        }
        else if (state.Capturing)
        {
            hint = "Press the new key   Esc: cancel";
        }
        else
        {
            hint = "Enter: change   Del: original key";
        }
        session.DrawCentredText(Left, Right, HintY, hint, hintColour);
    }

    private static void DrawScrollBar(MenuSession session, int rowCount, int scroll)
    {
        if (rowCount <= VisibleRows)
            return;
        const int x = Right - 8;
        const int top = ListTop;
        const int height = VisibleRows * RowHeight - 2;
        int thumb = Math.Max(8, height * VisibleRows / rowCount);
        int thumbTop = top + (height - thumb) * scroll / (rowCount - VisibleRows);
        session.Gfx.DrawFilledViewportRect(session.Screen, x, top, x + 1, top + height - 1, PaletteColours.DarkGrey);
        session.Gfx.DrawFilledViewportRect(session.Screen, x, thumbTop, x + 1, thumbTop + thumb - 1, PaletteColours.LightGrey);
    }
}
