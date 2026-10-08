using WingCommander.Graphics;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Text;

namespace WingCommander.Game.Screens.Ui;

/// <summary>
/// A boxed text panel drawn over the current page: it saves the pixels under its rectangle,
/// erases the rectangle with the text background colour, draws a one-pixel border and owns a
/// text context (font, colours, string builder) that becomes the current one until the panel
/// is restored. Used by the modal messages, the Y/N questions and the text prompts.
/// </summary>
/// <remarks>C: ModalTextPanel (include/wc1.h), InitializeModalTextPanel (0x41A9D0),
/// DrawModalTextPanel (0x41AAE0), RestoreModalTextPanel (0x41AB60), geom.c.</remarks>
public sealed class ModalTextPanel
{
    /// <summary>Size of the panel's string builder (szTextScratchBuffer).</summary>
    private const int ScratchSize = 256;

    private readonly byte[] _scratch = new byte[ScratchSize];

    /// <summary>The panel's text context (a copy of the default context with the panel's font and colours).</summary>
    public TextContext Context { get; } = new();

    /// <summary>Copy of the screen pixels under the panel.</summary>
    public Viewport SavedBackground { get; } = new();

    /// <summary>The panel rectangle on the modal source page (a screen alias).</summary>
    public Viewport Viewport { get; } = new();

    /// <summary>The text context that was current before the panel was initialised.</summary>
    public TextContext? PreviousContext { get; private set; }

    public short Left { get; private set; }

    public short Top { get; private set; }

    public short Right { get; private set; }

    public short Bottom { get; private set; }

    /// <summary>
    /// Saves the background of the rectangle, erases it with <paramref name="backgroundColour"/>
    /// (black for 0xFF), draws the border and makes the panel's context current (font
    /// <paramref name="fontIndex"/>, -1 = font 1; ink = <paramref name="clearColour"/>). Returns
    /// false when the background buffer cannot be allocated (empty rectangle).
    /// </summary>
    /// <remarks>C: InitializeModalTextPanel (0x41A9D0, geom.c). The panel draws into a copy of
    /// stModalSourceViewport (<see cref="Wc1Game.DefaultText"/>'s viewport).</remarks>
    public bool Initialize(Wc1Game game, int fontIndex, short left, short top, short right, short bottom,
        byte clearColour, byte backgroundColour, byte borderColour)
    {
        ArgumentNullException.ThrowIfNull(game);
        var gfx = game.Graphics;
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
        PreviousContext = gfx.CurrentTextContext;
        gfx.SetTextContext(Context);
        Context.CopyFrom(game.DefaultText);
        if (fontIndex == -1)
            fontIndex = 1;
        gfx.InitializeTextContextFromFont(Context, fontIndex, clearColour, backgroundColour);
        Viewport.CopyFrom(game.DefaultText.Viewport ?? gfx.Screen!);
        SavedBackground.SetViewportRect(left, top, right, bottom);
        Viewport.SetViewportRect(left, top, right, bottom);
        if (!SavedBackground.AllocateViewport(clearColour))
            return false;
        gfx.CopyViewportContents(Viewport, SavedBackground);
        Context.TextBuffer = _scratch;
        Context.Viewport = Viewport;
        ResetStringBuilder(Context);
        EraseTextContextBackground(gfx, Context);
        gfx.DrawViewportBorder(Viewport, Left, Top, Right, Bottom, borderColour);
        return true;
    }

    /// <summary>
    /// Formats <paramref name="text"/> (it is interpreted as a token format, like the original's
    /// vsprintf result) into the string builder at (left + x, top + y) of the panel and draws it
    /// with <paramref name="alignment"/> (2 = centred in the panel).
    /// </summary>
    /// <remarks>C: DrawModalTextPanel (0x41AAE0, geom.c): <c>SetTextCursor</c> on the current context,
    /// <c>strcat(text, "%P")</c>, <c>FormatTextBufferFromStart(text)</c>.</remarks>
    public void Draw(GraphicsContext gfx, int x, int y, byte alignment, string text)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        ArgumentNullException.ThrowIfNull(text);
        gfx.SetTextCursor(unchecked((ushort)(Left + x)), unchecked((ushort)(Top + y)));
        Context.Alignment = alignment;
        gfx.FormatTextBufferFromStart(UiText.ToBytes(text + "%P"));
    }

    /// <summary>Copies the saved pixels back, frees them and restores the previous text context (does not present).</summary>
    /// <remarks>C: RestoreModalTextPanel (0x41AB60, geom.c).</remarks>
    public void Restore(GraphicsContext gfx)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        if (SavedBackground.IsAllocated)
            gfx.CopyViewportContents(SavedBackground, Viewport);
        SavedBackground.FreeViewport();
        gfx.SetTextContext(PreviousContext);
    }

    /// <summary>Empties a context's string builder.</summary>
    /// <remarks>C: ResetStringBuilder (0x403E40, mono.c): <c>textCursor = text; *text = 0</c>.</remarks>
    public static void ResetStringBuilder(TextContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.TextCursor = 0;
        if (context.TextBuffer is { Length: > 0 } buffer)
            buffer[0] = 0;
    }

    /// <summary>
    /// Clears the context's viewport with its background colour (black when transparent).
    /// Returns true when the viewport is the screen object itself, where the original presented.
    /// </summary>
    /// <remarks>C: EraseTextContextBackground (0x425C30, pilot.cpp).</remarks>
    public static bool EraseTextContextBackground(GraphicsContext gfx, TextContext context)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        ArgumentNullException.ThrowIfNull(context);
        byte colour = context.BackgroundColour;
        if (colour == 0xFF)
            colour = PaletteColours.Black;
        return gfx.ClearViewport(context.Viewport ?? throw new InvalidOperationException("The text context has no viewport."), colour);
    }
}
