using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Text;

namespace WingCommander.Game.Screens.Ui;

/// <summary>
/// The pointer menu of a room (rec room, barracks): hit regions, one label per region shown
/// centred at the bottom of the screen while the pointer is over the region, and the cursor
/// frame. Labels are compared by reference, like the original compared label pointers (the
/// barracks only redraws its label strip when the label object changes).
/// </summary>
/// <remarks>C: InitializeRoomMenu (0x43F750), ClearRoomMenuLabel (0x43F690), IsRoomMenuLabelEmpty
/// (0x43F6A0), DrawRoomMenuLabel (0x43F6B0), RefreshRoomMenuLabel (0x43F6F0),
/// ClearRoomMenuCursorFrame (0x43F720), SelectRoomMenuLabel (0x43F730), killbrd.c;
/// UpdateRoomMenuCursor (0x42A680, hudmsg.c); globals pRoomMenuRegions, ppszRoomMenuLabels,
/// pszCurrentRoomMenuLabel, stRoomMenuTextContext, nRoomMenuCursorFrame.</remarks>
public sealed class RoomMenu
{
    /// <summary>The label shown when the pointer is over no region.</summary>
    /// <remarks>C: szBlankRoomMenuLabel = " " (via pszBlankRoomMenuLabel).</remarks>
    public static readonly string BlankLabel = new(' ', 1);

    /// <summary>Screen row of the label text.</summary>
    public const short LabelY = 188;

    /// <summary>Size of the label context's string builder (szDefaultTextBuffer).</summary>
    private const int TextBufferSize = 0xc8;

    private readonly Wc1Game _game;

    public RoomMenu(Wc1Game game, MenuRegion[] regions, string?[] labels, Viewport viewport, byte alignment)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(viewport);
        _game = game;
        Regions = regions;
        Labels = labels;
        CurrentLabel = BlankLabel;
        LabelContext.Viewport = viewport;
        LabelContext.TextBuffer = new byte[TextBufferSize];
        LabelContext.TextCursor = 0;
        LabelContext.Alignment = alignment;
        LabelContext.TextBuffer[0] = 0;
        game.Graphics.InitializeTextContextFromFont(LabelContext, 0, PaletteColours.ViewportClear, 0xFF);
        ClearCursorFrame();
    }

    /// <remarks>C: pRoomMenuRegions.</remarks>
    public MenuRegion[] Regions { get; }

    /// <summary>Label per region (null: the region keeps the previous label).</summary>
    /// <remarks>C: ppszRoomMenuLabels.</remarks>
    public string?[] Labels { get; }

    /// <summary>The label to draw; null after <see cref="ClearLabel"/>.</summary>
    /// <remarks>C: pszCurrentRoomMenuLabel.</remarks>
    public string? CurrentLabel { get; set; }

    /// <summary>Font 0, colour 15, transparent background, over the room's screen viewport.</summary>
    /// <remarks>C: stRoomMenuTextContext.</remarks>
    public TextContext LabelContext { get; } = new();

    /// <summary>Cursor frame used when the pointer is over no region.</summary>
    /// <remarks>C: nRoomMenuCursorFrame.</remarks>
    public short CursorFrame { get; set; }

    /// <remarks>C: ClearRoomMenuLabel (0x43F690).</remarks>
    public void ClearLabel() => CurrentLabel = null;

    /// <remarks>C: IsRoomMenuLabelEmpty (0x43F6A0).</remarks>
    public bool IsLabelEmpty => CurrentLabel is null;

    /// <remarks>C: ClearRoomMenuCursorFrame (0x43F720).</remarks>
    public void ClearCursorFrame() => CursorFrame = 0;

    /// <summary>Makes the label of region <paramref name="index"/> current unless it is null.</summary>
    /// <remarks>C: SelectRoomMenuLabel (0x43F730).</remarks>
    public void SelectLabel(int index)
    {
        string? label = Labels[index];
        if (label is not null)
            CurrentLabel = label;
    }

    /// <summary>Draws a label centred on row 188 with the given context (transparent background).</summary>
    /// <remarks>C: DrawRoomMenuLabel (0x43F6B0): <c>FormatTextBufferFromStart("%X%Y%s%P", 0, 188, label)</c>.</remarks>
    public void DrawLabel(TextContext context, string label)
    {
        var gfx = _game.Graphics;
        gfx.SetTextContext(context);
        gfx.FormatTextBufferFromStart("%X%Y%s%P"u8, 0, LabelY, label);
    }

    /// <summary>Draws the current label (the blank label when none is selected).</summary>
    /// <remarks>C: RefreshRoomMenuLabel (0x43F6F0).</remarks>
    public void RefreshLabel()
    {
        if (IsLabelEmpty)
            CurrentLabel = BlankLabel;
        DrawLabel(LabelContext, CurrentLabel!);
    }

    /// <summary>
    /// Selects the label and cursor frame of the region under the cursor (the last matching
    /// region wins); outside every region the label is cleared and the frame is <see cref="CursorFrame"/>.
    /// </summary>
    /// <remarks>C: UpdateRoomMenuCursor (0x42A680, hudmsg.c), using stMouseCursorState.x/y.</remarks>
    public void UpdateCursor()
    {
        short x = _game.Events.Cursor.X;
        short y = _game.Events.Cursor.Y;
        short frame = CursorFrame;
        ClearLabel();
        for (int index = 0; index < Regions.Length && Regions[index].Frame != MenuRegion.EndOfList; index++)
        {
            if (!Regions[index].Contains(x, y))
                continue;
            frame = Regions[index].Frame;
            if (index >= 20 || index < 0)
                return;
            SelectLabel(index);
        }
        _game.Cursor.SetFrame(frame);
    }

    /// <remarks>C: FindMenuRegionAtPoint (0x43F7C0, killbrd.c).</remarks>
    public int FindRegionAtPoint(short x, short y) => MenuRegion.FindMenuRegionAtPoint(Regions, x, y);
}
