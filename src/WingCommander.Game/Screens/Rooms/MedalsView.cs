using WingCommander.Core.Resources;
using WingCommander.Game.Campaign;
using WingCommander.Game.Input;
using WingCommander.Game.Scenes;
using WingCommander.Graphics;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Graphics.Text;

namespace WingCommander.Game.Screens.Rooms;

/// <summary>
/// "View your medals": the player's chest with rank insignia, badges and medals in the
/// 320x128 scene area (screen rows 24..151) and the line "$R $N, aka $C. $S system, dateline $D."
/// below. Any key or button closes it.
/// </summary>
/// <remarks>C: ViewMedals (0x436E30) and DrawMedals (0x4375C0), screens.c; InitializeConversationText
/// (0x427BC0) and RefreshMemoryStatusOverlay (0x427C30), main.c; pszMedalsPilotSummary,
/// szViewMedalsTextFormat, asMedalDisplayX. The caller sets the screen rows 24..151 and the scene
/// buffer rows 0..127 first, like the barracks do.</remarks>
public static class MedalsView
{
    /// <summary>BRIEFING.VGA section of the medal ceremony shapes.</summary>
    public const int MedalShapeSection = 8;

    /// <remarks>C: szMedalsPilotSummary (0x0046e5dc).</remarks>
    public const string PilotSummary = "$R $N, aka $C.\n$S system, dateline $D.";

    /// <remarks>C: asMedalDisplayX (0x0046e2d0).</remarks>
    private static ReadOnlySpan<short> MedalDisplayX => [191, 199, 207, 216, 228];

    /// <summary>Size of the conversation text context's string builder (szDefaultTextBuffer).</summary>
    private const int TextBufferSize = 0xc8;

    /// <param name="game">The game.</param>
    /// <param name="sceneBuffer">The 320x200 room buffer with its bottom set to row 127 (stSceneBuffer).</param>
    /// <param name="systemName">Name of the current system (MODULE section 5, abSeriesAuxData) for $S.</param>
    public static async Task ShowAsync(Wc1Game game, Viewport sceneBuffer, string systemName)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(sceneBuffer);
        var gfx = game.Graphics;
        var events = game.Events;
        var display = game.Display;
        var medalShape = game.Resources.GetShape(LogicalFile.BriefingVga, MedalShapeSection);
        ShapeTable? backdrop = null;

        var (textViewport, textContext) = InitializeConversationText(game);
        gfx.ClearViewport(sceneBuffer, PaletteColours.Black);
        byte savedInputMode = events.InputMode;
        events.InputMode = 1;
        var e = new InputEventState();
        var macros = new SummaryMacros(game.Session, systemName);
        bool clicked = false;
        while (true)
        {
            events.PumpWindowMessages();
            if (events.PeekInputEvent(ref e, InputEventType.JoystickButton) ||
                events.PeekInputEvent(ref e, InputEventType.ButtonDown) ||
                events.PeekInputEvent(ref e, InputEventType.KeyDown))
                clicked = true;
            await DrawMedalsAsync(game, sceneBuffer, medalShape, backdrop);
            string summary = TextMacros.Expand(PilotSummary, macros);
            await display.WaitForVerticalBlankAsync();
            gfx.CopyViewportContents(sceneBuffer, gfx.Screen!);
            gfx.ClearViewport(textViewport, PaletteColours.Black);
            gfx.SetTextContext(textContext);
            gfx.FormatTextBufferFromStart("%X%Y%F%s%P"u8, 0, 160, PaletteColours.ViewportClear, summary);
            await display.PresentAsync();
            if (clicked)
            {
                await events.WaitForInputKeyAsync();
                events.ClearInputKeyStatePreservingModifiers();
                events.InputMode = savedInputMode;
                events.FlushInputEvents();
                return;
            }
        }
    }

    /// <summary>Draws the chest, rank insignia (also mirrored on the left collar), badges and medals into the scene buffer and presents.</summary>
    /// <remarks>C: DrawMedals (0x4375C0, screens.c). The backdrop frame 1 is drawn first when a
    /// conversation backdrop is loaded (null from the barracks).</remarks>
    public static async Task DrawMedalsAsync(Wc1Game game, Viewport sceneBuffer, ShapeTable medalShape, ShapeTable? backdrop)
    {
        ArgumentNullException.ThrowIfNull(game);
        var gfx = game.Graphics;
        var campaign = game.Session.State;
        short rank = game.Session.Player.Rank;
        short rowY = 78;
        short x = 188;
        gfx.DrawSpriteDefault(sceneBuffer, 0, 0, backdrop, 1);
        gfx.DrawSpriteDefault(sceneBuffer, 0, 0, medalShape, 11);
        gfx.DrawSpriteDefault(sceneBuffer, 253, 38, medalShape, rank + 33);
        gfx.DrawSpriteScaled(sceneBuffer, 67, 38, medalShape, rank + 33, 0, 255, GraphicsContext.FlipHorizontal);
        for (int badge = 0; badge < 12; badge++)
        {
            if (campaign.Badges[badge] == 0)
                continue;
            if (x > 231)
            {
                rowY += 3;
                x = 188;
            }
            gfx.DrawSpriteDefault(sceneBuffer, x, rowY, medalShape, badge + 13);
            x += 11;
        }
        rowY += 5;
        for (int medal = 0; medal < 5; medal++)
        {
            if (campaign.Medals[medal] == 0)
                continue;
            x = MedalDisplayX[medal];
            short stack = rowY;
            if (medal < 3)
            {
                int count = unchecked((sbyte)campaign.Medals[medal]);
                for (int awarded = 0; awarded < count; awarded++)
                {
                    gfx.DrawSpriteDefault(sceneBuffer, x, stack, medalShape, medal + 25);
                    stack += 2;
                }
            }
            gfx.DrawSpriteDefault(sceneBuffer, x, stack, medalShape, medal + 28);
        }
        await game.Display.PresentAsync();
    }

    /// <summary>
    /// The conversation text area: a copy of the modal source page from row 152 down, font 0,
    /// colour 15 on black, centred; it becomes the current text context.
    /// </summary>
    /// <remarks>C: InitializeConversationText (0x427BC0, main.c).</remarks>
    private static (Viewport Viewport, TextContext Context) InitializeConversationText(Wc1Game game)
    {
        var viewport = (game.DefaultText.Viewport ?? game.Graphics.Screen!).Clone();
        viewport.Top = 152;
        var context = new TextContext
        {
            Viewport = viewport,
            TextBuffer = new byte[TextBufferSize],
            Alignment = TextContext.AlignCentre,
        };
        game.Graphics.InitializeTextContextFromFont(context, 0, PaletteColours.ViewportClear, PaletteColours.Black);
        return (viewport, context);
    }

    /// <summary>The macros the summary line uses ($R $N $C $S $D).</summary>
    private sealed class SummaryMacros(CampaignSession session, string systemName) : ITextMacroContext
    {
        public string MedalName => "";

        public string Callsign => session.Player.Callsign;

        public string PlayerName => session.Player.Name;

        public string RankName => TextMacros.RankNames[Math.Clamp((int)session.Player.Rank, 0, TextMacros.RankNames.Length - 1)];

        public string SystemName => systemName;

        public (int Year, int Day) CurrentDate => (session.State.CurrentDate.Year, session.State.CurrentDate.Day);

        public (int Year, int Day) SavedDate => (session.SavedCampaignDate.Year, session.SavedCampaignDate.Day);

        public (int Hour, int Minute) Time =>
            (unchecked((sbyte)(session.State.ElapsedDate.Day & 0xFF)), unchecked((sbyte)(session.State.ElapsedDate.Day >> 8)));

        public int PlayerKills => 0;

        public int WingmanKills => 0;

        public string WingmanName(int personality) =>
            (uint)personality < 8 ? session.Pilots[personality].Name : "";
    }
}
