using WingCommander.Graphics;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Text;

namespace WingCommander.Game.Screens.Ui;

/// <summary>
/// Single-line text entry at the current text cursor of the current text context: the
/// existing text is shown with a solid block cursor after it; letters, digits and (not as the
/// first character) spaces are appended up to the maximum length, Backspace deletes, Enter
/// accepts a non-empty text and Esc cancels.
/// </summary>
/// <remarks>C: ReadTextInput (0x426200), DrawTextInputCursor (0x4260E0), ClearTextInputCharacter
/// (0x426140), ClearNextTextInputCharacter (0x4261D0), pilot.cpp; EraseLastTextInputCharacter
/// (0x41DDF0, disk.c).</remarks>
public static class TextInput
{
    /// <summary>Letters are lower case unless Shift is held (the pilot name entry).</summary>
    public const int ModeAnyCase = 0;

    /// <summary>Letters are upper-cased (save game names).</summary>
    public const int ModeUpperCase = 1;

    /// <summary>Only digits are accepted.</summary>
    public const int ModeDigits = 2;

    /// <summary>Size of the original's input buffer (<c>char input[40]</c>).</summary>
    private const int InputBufferSize = 40;

    private const byte KeyEnter = 13;
    private const byte KeyEscape = 27;
    private const byte KeyBackspace = 8;

    /// <summary>
    /// Edits <paramref name="initial"/> and returns the accepted text, or null when the player
    /// pressed Esc or Enter on an empty line. Keys are read with
    /// <see cref="Input.EventManager.WaitForStreamInputKeyAsync"/> (virtual-key codes).
    /// </summary>
    /// <remarks>
    /// C: ReadTextInput (0x426200, pilot.cpp). Side effects are reproduced: the context's text
    /// buffer and viewport point at the input line during the edit and are only restored on
    /// success (Esc restores them but not the background colour), and the key event duplicates
    /// (bKeyEventQueueEnabled) stay disabled after a cancelled edit.
    /// </remarks>
    public static async Task<string?> ReadTextInputAsync(Wc1Game game, string initial, int maximumLength, int mode)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(initial);
        var gfx = game.Graphics;
        var events = game.Events;
        var display = game.Display;
        TextContext context = gfx.CurrentTextContext ?? throw new InvalidOperationException("No current text context.");
        BitmapFont font = context.Font ?? throw new InvalidOperationException("The text context has no font.");

        byte savedBackground = context.BackgroundColour;
        if (savedBackground == 0xFF)
            context.BackgroundColour = PaletteColours.Black;
        var savedViewport = context.Viewport ?? throw new InvalidOperationException("The text context has no viewport.");
        byte[]? savedText = context.TextBuffer;
        var input = new byte[InputBufferSize];
        context.TextBuffer = input;
        short savedX = context.CursorX;
        short savedY = context.CursorY;
        var inputViewport = savedViewport.Clone();
        context.Viewport = inputViewport;
        int inputLength = CopyString(input, initial);

        inputViewport.Left = savedX;
        inputViewport.Top = savedY;
        inputViewport.Bottom = unchecked((short)(inputViewport.Top + font.Height));
        inputViewport.Right = unchecked((short)(inputViewport.Left + gfx.MeasureTextPixelWidthClamped(input)));
        gfx.ClearViewport(inputViewport, context.BackgroundColour);
        inputViewport.Right = savedViewport.Right;
        gfx.DrawFormattedText(input);
        DrawTextInputCursor(gfx, (byte)' ');

        bool savedKeyEventQueue = events.KeyEventQueueEnabled;
        events.KeyEventQueueEnabled = false;
        bool accepted = false;
        do
        {
            bool handled = false;
            do
            {
                await display.PresentAsync();
                byte key = unchecked((byte)await events.WaitForStreamInputKeyAsync());
                if (key == KeyEnter)
                {
                    handled = true;
                    if (input[0] == 0)
                        return null;
                    accepted = true;
                    ClearNextTextInputCharacter(game, (byte)' ');
                }
                else if (key == KeyEscape)
                {
                    ClearNextTextInputCharacter(game, (byte)' ');
                    inputViewport.Left = savedX;
                    inputViewport.Top = savedY;
                    inputViewport.Bottom = unchecked((short)(inputViewport.Top + font.Height));
                    inputViewport.Right = unchecked((short)(inputViewport.Left + gfx.MeasureTextPixelWidthClamped(input)));
                    gfx.ClearViewport(inputViewport, context.BackgroundColour);
                    context.CursorX = savedX;
                    context.TextBuffer = savedText;
                    context.Viewport = savedViewport;
                    return null;
                }
                else if (key == KeyBackspace && inputLength != 0)
                {
                    inputLength--;
                    handled = true;
                    ClearNextTextInputCharacter(game, (byte)' ');
                    EraseLastTextInputCharacter(game);
                    DrawTextInputCursor(gfx, (byte)' ');
                    input[inputLength] = 0;
                }
                else
                {
                    bool skip = false;
                    if (inputLength < maximumLength &&
                        (key is >= (byte)'A' and <= (byte)'Z' || key is >= (byte)'a' and <= (byte)'z' ||
                         key is >= (byte)'0' and <= (byte)'9' || (key == ' ' && inputLength != 0)))
                    {
                        byte character = key;
                        if (mode == ModeUpperCase)
                        {
                            character = (byte)UiText.ToUpper(key);
                        }
                        else if (mode == ModeDigits)
                        {
                            if (key is < (byte)'0' or > (byte)'9')
                                character = 0;
                        }
                        else if (key is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z')
                        {
                            character = (byte)(key | 0x20);
                            if (events.GetShiftKeyState())
                                character &= 0xDF;
                        }
                        if (character == 0)
                        {
                            skip = true; // the original jumps straight to the redraw (handled stays 0)
                        }
                        else
                        {
                            ClearNextTextInputCharacter(game, (byte)' ');
                            input[inputLength++] = character;
                            input[inputLength] = 0;
                            gfx.SetTextCursor(unchecked((ushort)savedX), unchecked((ushort)savedY));
                            gfx.DrawFormattedText(input);
                            DrawTextInputCursor(gfx, (byte)' ');
                        }
                    }
                    if (!skip)
                        handled = true;
                }
                await display.PresentAsync();
            }
            while (!handled);
        }
        while (!accepted);

        events.KeyEventQueueEnabled = savedKeyEventQueue;
        string result = UiText.FromBytes(input);
        context.TextBuffer = savedText;
        context.Viewport = savedViewport;
        context.BackgroundColour = savedBackground;
        return result;
    }

    /// <summary>Draws <paramref name="character"/> one pixel right of the cursor with ink as background (a solid block for ' ').</summary>
    /// <remarks>C: DrawTextInputCursor (0x4260E0, pilot.cpp).</remarks>
    public static void DrawTextInputCursor(GraphicsContext gfx, byte character)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        TextContext context = gfx.CurrentTextContext ?? throw new InvalidOperationException("No current text context.");
        byte savedBackground = context.BackgroundColour;
        byte colour = context.Colour;
        short savedX = context.CursorX;
        context.CursorX = unchecked((short)(savedX + 1));
        context.BackgroundColour = colour;
        ReadOnlySpan<byte> cursor = [character, 0];
        gfx.DrawFormattedText(cursor);
        context.BackgroundColour = savedBackground;
        context.CursorX = savedX;
    }

    /// <summary>Clears one character cell at the cursor with the background colour.</summary>
    /// <remarks>C: ClearTextInputCharacter (0x426140, pilot.cpp).</remarks>
    public static void ClearTextInputCharacter(Wc1Game game, byte character)
    {
        ArgumentNullException.ThrowIfNull(game);
        var gfx = game.Graphics;
        TextContext context = gfx.CurrentTextContext ?? throw new InvalidOperationException("No current text context.");
        short characterWidth = (short)gfx.GetFontCharWidth(character);
        var clearArea = (context.Viewport ?? throw new InvalidOperationException("The text context has no viewport.")).Clone();
        clearArea.Left = context.CursorX;
        clearArea.Right = unchecked((short)(clearArea.Left + characterWidth - 1));
        clearArea.Top = context.CursorY;
        clearArea.Bottom = unchecked((short)(clearArea.Top + context.Font!.Height - 1));
        game.Events.HideCursor();
        gfx.ClearViewport(clearArea, context.BackgroundColour);
        game.Events.ShowCursor();
    }

    /// <summary>Clears the character cell one pixel right of the cursor (the block cursor).</summary>
    /// <remarks>C: ClearNextTextInputCharacter (0x4261D0, pilot.cpp).</remarks>
    public static void ClearNextTextInputCharacter(Wc1Game game, byte character)
    {
        ArgumentNullException.ThrowIfNull(game);
        TextContext context = game.Graphics.CurrentTextContext ?? throw new InvalidOperationException("No current text context.");
        short savedX = context.CursorX;
        context.CursorX++;
        ClearTextInputCharacter(game, character);
        context.CursorX = savedX;
    }

    /// <summary>Clears the last character of the context's text and moves the cursor back by its width.</summary>
    /// <remarks>C: EraseLastTextInputCharacter (0x41DDF0, disk.c).</remarks>
    public static void EraseLastTextInputCharacter(Wc1Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var gfx = game.Graphics;
        TextContext context = gfx.CurrentTextContext ?? throw new InvalidOperationException("No current text context.");
        ReadOnlySpan<byte> text = context.GetBufferText();
        short textWidth = gfx.MeasureTextPixelWidthClamped(text);
        int length = text.Length;
        if (length == 0)
            return;
        short characterWidth = (short)gfx.GetFontCharWidth(text[length - 1]);
        var clearArea = (context.Viewport ?? throw new InvalidOperationException("The text context has no viewport.")).Clone();
        clearArea.Left = unchecked((short)(clearArea.Left + textWidth - characterWidth));
        clearArea.Right = unchecked((short)(clearArea.Left + characterWidth - 1));
        clearArea.Top = context.CursorY;
        clearArea.Bottom = unchecked((short)(clearArea.Top + context.Font!.Height - 1));
        game.Events.HideCursor();
        gfx.ClearViewport(clearArea, context.BackgroundColour);
        game.Events.ShowCursor();
        context.CursorX = unchecked((short)(context.CursorX - characterWidth));
    }

    /// <summary>strcpy into a fixed buffer (truncated so the NUL fits); returns the length.</summary>
    private static int CopyString(byte[] destination, string text)
    {
        int length = Math.Min(text.Length, destination.Length - 1);
        for (int i = 0; i < length; i++)
            destination[i] = text[i] <= 0xFF ? (byte)text[i] : (byte)'?';
        destination[length] = 0;
        return length;
    }
}
