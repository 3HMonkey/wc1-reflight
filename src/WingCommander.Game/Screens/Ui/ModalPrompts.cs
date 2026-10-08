using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Text;

namespace WingCommander.Game.Screens.Ui;

/// <summary>
/// The modal prompts of the rooms: a centred one-line message box over the current page, the
/// "press a key" message, Y/N questions and the boxed text entry. All of them draw on the page
/// in place and restore the pixels underneath when they close.
/// </summary>
/// <remarks>C: ShowModalTextPanel (0x41AB90), ReleaseModalTextPanel (0x41AD10), geom.c;
/// ShowModalMessage (0x428F20, hudmsg.c); PromptForTextInput (0x41B420, barracks.c).
/// The original kept the open panel in the global pModalTextPanel; here the caller holds it.</remarks>
public static class ModalPrompts
{
    /// <summary>Top-left of the measuring panel (x 0x18, y 0x28).</summary>
    /// <remarks>C: dwModalBoundsTopLeft = 0x00280018.</remarks>
    public const short BoundsLeft = 0x18;

    /// <remarks>C: dwModalBoundsTopLeft.</remarks>
    public const short BoundsTop = 0x28;

    /// <summary>Bottom-right of the measuring panel (x 0x128, y 0x3C).</summary>
    /// <remarks>C: dwModalBoundsBottomRight = 0x003C0128.</remarks>
    public const short BoundsRight = 0x128;

    /// <remarks>C: dwModalBoundsBottomRight.</remarks>
    public const short BoundsBottom = 0x3c;

    /// <summary>Size of the original's formatting buffer (<c>char text[52]</c>); longer messages overflowed it.</summary>
    public const int MaximumMessageLength = 51;

    /// <summary>
    /// Draws <paramref name="text"/> centred in a red-bordered blue box (rows 40..60, as wide as
    /// the text) over the modal source page in font <paramref name="fontIndex"/> and presents.
    /// The panel's text context stays current until <see cref="ReleaseModalTextPanelAsync"/>.
    /// Returns null when the panel could not be created.
    /// </summary>
    /// <remarks>C: ShowModalTextPanel (0x41AB90, geom.c). The text is measured with a black panel
    /// at the default bounds first (drawn and immediately restored), exactly like the original.</remarks>
    public static async Task<ModalTextPanel?> ShowModalTextPanelAsync(Wc1Game game, int fontIndex, string text)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(text);
        var gfx = game.Graphics;
        var panel = new ModalTextPanel();
        if (!panel.Initialize(game, fontIndex, BoundsLeft, BoundsTop, BoundsRight, BoundsBottom,
                PaletteColours.Black, PaletteColours.Black, PaletteColours.Black))
            return null;
        int halfWidth = gfx.MeasureTextPixelWidthClamped(UiText.ToBytes(text));
        halfWidth = (short)((halfWidth * 8 + ((halfWidth * 8 >> 31) & 15)) >> 4);
        panel.Restore(gfx);
        if (!panel.Initialize(game, fontIndex, (short)(159 - halfWidth), BoundsTop, (short)(161 + halfWidth), BoundsBottom,
                PaletteColours.ViewportClear, PaletteColours.Blue, PaletteColours.Red))
            return null;
        panel.Draw(gfx, 0, 6, TextContext.AlignCentre, text);
        await game.Display.PresentAsync();
        return panel;
    }

    /// <summary>Restores the page under the panel and presents.</summary>
    /// <remarks>C: ReleaseModalTextPanel (0x41AD10, geom.c).</remarks>
    public static async Task ReleaseModalTextPanelAsync(Wc1Game game, ModalTextPanel? panel)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (panel is null)
            return;
        panel.Restore(game.Graphics);
        await game.Display.PresentAsync();
    }

    /// <summary>Shows a message in font 1 and waits until a key is released (P and two other codes do not count).</summary>
    /// <remarks>C: ShowModalMessage (0x428F20, hudmsg.c) with WaitForKeyAcknowledge(0). Mouse
    /// buttons do not acknowledge it, as in the original.</remarks>
    public static async Task ShowModalMessageAsync(Wc1Game game, string text)
    {
        ArgumentNullException.ThrowIfNull(game);
        var panel = await ShowModalTextPanelAsync(game, 1, text);
        await game.Events.WaitForKeyAcknowledgeAsync(0);
        if (panel is not null)
            await ReleaseModalTextPanelAsync(game, panel);
    }

    /// <summary>
    /// Asks a Y/N question in font 0 with the pointer hidden one level: true when the next key
    /// (virtual-key code, upper-cased) is 'Y'. Any other key or a mouse button answers no.
    /// </summary>
    /// <remarks>C: the common body of ConfirmQuitWingCommander (0x41BF10), ConfirmAwakenAfterBadData
    /// (0x41BF60) and ConfirmReplaceFaultyData (0x41BFE0), barracks.c:
    /// <c>LeaveAllocationScope; ShowModalTextPanel(0, ...); toupper(WaitForStreamInputKey()) == 'Y';
    /// ReleaseModalTextPanel; EnterAllocationScope</c>.</remarks>
    public static async Task<bool> ConfirmAsync(Wc1Game game, string question)
    {
        ArgumentNullException.ThrowIfNull(game);
        game.Events.HideCursor();
        bool confirmed = false;
        var panel = await ShowModalTextPanelAsync(game, 0, question);
        if (panel is not null)
        {
            short key = await game.Events.WaitForStreamInputKeyAsync();
            confirmed = UiText.ToUpper(key) == 'Y';
            await ReleaseModalTextPanelAsync(game, panel);
        }
        game.Events.ShowCursor();
        return confirmed;
    }

    /// <summary>
    /// A boxed prompt at (x, y), 20 rows high and wide enough for the prompt plus
    /// <paramref name="maximumLength"/> "M" glyphs (+1/15), with a text entry after the prompt.
    /// Returns the accepted text or null (Esc, empty Enter). The box is restored without a present.
    /// </summary>
    /// <remarks>C: PromptForTextInput (0x41B420, barracks.c); see <see cref="TextInput.ReadTextInputAsync"/>.</remarks>
    public static async Task<string?> PromptForTextInputAsync(Wc1Game game, short x, short y, string prompt,
        string initial, short maximumLength, short mode)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(prompt);
        var gfx = game.Graphics;
        var panel = new ModalTextPanel();
        string? result = null;
        short bottom = (short)(y + 20);
        // The original ignores the result of this measuring panel.
        panel.Initialize(game, 0, BoundsLeft, BoundsTop, BoundsRight, BoundsBottom,
            PaletteColours.Black, PaletteColours.Black, PaletteColours.Black);
        int widestCharacter = gfx.MeasureTextPixelWidthClamped("M"u8);
        widestCharacter *= maximumLength;
        short promptWidth = gfx.MeasureTextPixelWidthClamped(UiText.ToBytes(prompt));
        widestCharacter += promptWidth;
        short right = unchecked((short)(x + widestCharacter * 16 / 15));
        panel.Restore(gfx);
        if (panel.Initialize(game, 0, x, y, right, bottom, PaletteColours.ViewportClear, PaletteColours.Blue, PaletteColours.Red))
        {
            panel.Draw(gfx, 3, 6, 0, prompt);
            await game.Display.PresentAsync();
            result = await TextInput.ReadTextInputAsync(game, initial, maximumLength, mode);
            panel.Restore(gfx);
        }
        return result;
    }
}
