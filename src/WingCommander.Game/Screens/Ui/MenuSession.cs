using WingCommander.Game.Input;
using WingCommander.Graphics;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Text;

namespace WingCommander.Game.Screens.Ui;

/// <summary>
/// The frame around a port menu screen (ADR-015): saves the picture, the cursor and the input
/// state of the screen underneath, gives the menu the arrow cursor on the whole screen and plain
/// scan codes, and puts everything back afterwards. Also draws the WC1-style panels and text the
/// menus are made of (font 0, so the output-resolution text shows them in the replacement font).
/// </summary>
internal sealed class MenuSession
{
    /// <summary>Line distance of menu text (font 0 is 11 pixels high).</summary>
    public const int LineHeight = 14;

    private readonly Wc1Game _game;
    private readonly Viewport _saved;
    private readonly Viewport? _cursorViewport;
    private readonly short _cursorFrame;
    private readonly int _cursorShowCount;
    private readonly bool _keyEventQueue;
    private readonly byte _inputMode;
    private readonly TextContext? _textContext;
    private readonly bool _releaseMouse;

    private MenuSession(Wc1Game game, bool releaseMouse)
    {
        _game = game;
        var gfx = game.Graphics;
        var events = game.Events;
        Screen = new Viewport(gfx.ScreenSurface, 0, 0, 319, 199);
        _saved = Viewport.Allocate(0, 0, 319, 199);
        gfx.CopyViewportContents(Screen, _saved);
        _cursorViewport = game.Cursor.Viewport;
        _cursorFrame = events.Cursor.Frame;
        _cursorShowCount = events.CursorShowCount;
        _keyEventQueue = events.KeyEventQueueEnabled;
        _inputMode = events.InputMode;
        _textContext = gfx.CurrentTextContext;
        _releaseMouse = releaseMouse;

        events.FlushInputEvents();
        events.KeyEventQueueEnabled = false;
        events.InputMode = 1;
        if (releaseMouse)
            events.SetMouseGrab(false);
        game.Cursor.Viewport = Screen.Clone();
        game.Cursor.SetFrame(0);
        if (events.CursorShowCount <= 0)
            events.CursorShowCount = 1;
        Text = new TextContext { Viewport = Screen.Clone(), TextBuffer = new byte[256] };
        gfx.InitializeTextContextFromFont(Text, 0, PaletteColours.ViewportClear, PaletteColours.Transparent);
    }

    /// <summary>A full-screen viewport over the screen surface.</summary>
    public Viewport Screen { get; }

    /// <summary>The menu's text context (font 0, transparent background).</summary>
    public TextContext Text { get; }

    public GraphicsContext Gfx => _game.Graphics;

    /// <param name="releaseMouse">Free the mouse from the flight's grab while the menu is open.</param>
    public static MenuSession Begin(Wc1Game game, bool releaseMouse) => new(game, releaseMouse);

    /// <summary>Puts picture, cursor and input state back.</summary>
    public void End()
    {
        var gfx = _game.Graphics;
        var events = _game.Events;
        gfx.CopyViewportContents(_saved, Screen);
        _game.Cursor.Viewport = _cursorViewport;
        _game.Cursor.SetFrame(_cursorFrame);
        events.CursorShowCount = _cursorShowCount;
        events.KeyEventQueueEnabled = _keyEventQueue;
        events.InputMode = _inputMode;
        if (_releaseMouse)
            events.SetMouseGrab(true);
        events.FlushInputEvents();
        events.EscapePressed = false;
        gfx.SetTextContext(_textContext);
    }

    /// <summary>The picture underneath, for redrawing the menu over it.</summary>
    public void RestoreBackground() => Gfx.CopyViewportContents(_saved, Screen);

    /// <summary>A panel: black with a two-tone blue frame.</summary>
    public void DrawPanel(int left, int top, int right, int bottom)
    {
        Gfx.DrawFilledViewportRect(Screen, left, top, right, bottom, PaletteColours.Black);
        Gfx.DrawViewportBorder(Screen, left, top, right, bottom, PaletteColours.DarkBlue);
        Gfx.DrawViewportBorder(Screen, left + 1, top + 1, right - 1, bottom - 1, PaletteColours.Blue);
    }

    /// <summary>Left-aligned text with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public void DrawText(int x, int y, string text, byte colour)
    {
        Text.Colour = colour;
        Text.Viewport!.SetViewportRect(0, 0, 319, 199);
        Gfx.DrawTextAt(Text, x, y, text, 0);
    }

    /// <summary>Text centred between <paramref name="left"/> and <paramref name="right"/> (inclusive).</summary>
    public void DrawCentredText(int left, int right, int y, string text, byte colour) =>
        DrawText((left + right + 1 - TextWidth(text)) / 2, y, text, colour);

    /// <summary>Width of <paramref name="text"/> in the menu font.</summary>
    public int TextWidth(string text)
    {
        int width = 0;
        foreach (char c in text)
            width += Text.Font!.GetWidth(c <= 0xFF ? (byte)c : (byte)'?');
        return width;
    }

    /// <summary>
    /// The next key press, mouse button or mouse move; while none is queued the frame is presented
    /// (16 or 20 fps), so a menu redrawn before the call becomes visible.
    /// </summary>
    public async Task<(short Type, InputEventState Event)> NextEventAsync()
    {
        var e = new InputEventState();
        while (true)
        {
            short type = _game.Events.PollInputEvent(ref e);
            if (type is InputEventType.KeyDown or InputEventType.ButtonDown or InputEventType.MouseMove)
                return (type, e);
            await _game.Display.PresentAsync();
        }
    }
}
