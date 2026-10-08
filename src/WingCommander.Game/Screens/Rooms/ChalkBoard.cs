using WingCommander.Core.Resources;
using WingCommander.Game.Input;
using WingCommander.Game.Scenes;
using WingCommander.Game.Screens.Ui;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Text;

namespace WingCommander.Game.Screens.Rooms;

/// <summary>
/// The kill board in the rec room ("CARRIER - TIGER'S CLAW"): the nine pilots ordered by kills
/// (ties: fewer sorties first) with rank, name, sorties and kills, "KIA" for dead wingmen.
/// Any key or button closes it.
/// </summary>
/// <remarks>C: ShowChalkBoard (0x440510, killbrd.c).</remarks>
internal static class ChalkBoard
{
    /// <summary>RECROOM.VGA section of the board.</summary>
    public const int BackgroundSection = 2;

    /// <summary>Size of the board context's string builder (szDefaultTextBuffer).</summary>
    private const int TextBufferSize = 0xc8;

    public static async Task ShowAsync(Wc1Game game, RoomsState rooms)
    {
        var gfx = game.Graphics;
        var events = game.Events;
        var pilots = game.Session.Pilots;
        var campaign = game.Session.State;
        var order = rooms.ChalkBoardPilotOrder;

        for (int index = 0; index < 9; index++)
        {
            for (int other = index; other < 9; other++)
            {
                short pilot = order[index];
                short swap = order[other];
                int score = pilots[pilot].Kills * 1000 - pilots[pilot].Missions + 1;
                int otherScore = pilots[swap].Kills * 1000 - pilots[swap].Missions + 1;
                if (score < otherScore)
                {
                    order[index] = swap;
                    order[other] = pilot;
                }
            }
        }

        var context = new TextContext();
        rooms.ChalkBoardDate = campaign.CurrentDate;
        var background = game.Resources.GetShape(LogicalFile.RecRoomVga, BackgroundSection);
        var previousContext = gfx.CurrentTextContext;
        var page = game.DefaultText.Viewport!;
        page.CopyFrom(gfx.Screen!);
        context.Viewport = page;
        context.TextBuffer = new byte[TextBufferSize];
        context.TextCursor = 0;
        gfx.InitializeTextContextFromFont(context, 3, PaletteColours.ViewportClear, 0xFF);
        byte savedInputMode = events.InputMode;
        events.InputMode = 1;
        var e = new InputEventState();
        bool done = false;
        do
        {
            events.PumpWindowMessages();
            if (events.PeekInputEvent(ref e, InputEventType.JoystickButton) ||
                events.PeekInputEvent(ref e, InputEventType.ButtonDown) ||
                events.PeekInputEvent(ref e, InputEventType.KeyDown))
                done = true;

            gfx.DrawSpriteDefault(page, 0, 0, background, 0);
            context.Alignment = TextContext.AlignCentre;
            gfx.SetTextContext(context);
            gfx.FormatTextBufferFromStart("%X%YCARRIER - TIGER'S CLAW%P"u8, 0, 10);
            gfx.DrawFormattedText("%X%YPILOT"u8, 60, 24);
            gfx.DrawFormattedText("%X%YSORTIES KILLS"u8, 185, 24);
            short rowY = 46;
            for (int index = 0; index < 9; index++, rowY += 16)
            {
                short pilot = order[index];
                string name = UiText.ToUpperAscii(pilots[pilot].Name);
                int rank = Math.Clamp((int)pilots[pilot].Rank, 0, 4);
                gfx.DrawFormattedText("%X%Y%s %s"u8, 10, rowY, TextMacros.RankNames[rank], name);
                if (pilot == 8 || campaign.PersonalityDeathMission[pilot] == 0)
                    gfx.DrawFormattedText("%X%Y%d%X%d"u8, 230, rowY, pilots[pilot].Missions, 280, pilots[pilot].Kills);
                else
                    gfx.DrawFormattedText("%X%Y     %s"u8, 230, rowY, "KIA");
            }
            await game.Display.PresentAsync();
        }
        while (!done);

        gfx.ReleaseTextFont(3);
        gfx.SetTextContext(previousContext);
        events.ClearInputKeyStatePreservingModifiers();
        events.InputMode = savedInputMode;
        events.FlushInputEvents();
    }
}
