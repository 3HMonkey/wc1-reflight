using WingCommander.Core.Resources;
using WingCommander.Graphics;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Game.Screens.Rooms;

/// <summary>
/// The arcade captions over the simulator's space view: "Get Ready" zooming in before each
/// enemy (40 frames), "Victory" with fireworks after the last one (80 frames) and "Game Over"
/// over the exploding ship (80 frames, music 22). Esc skips each of them.
/// </summary>
/// <remarks>C: ShowGetReadyScreen (0x439840), ShowVictoryScreen (0x439910), ShowGameOverScreen
/// (0x439A80), screens.c; DrawCenteredScaledIntroText (0x4037A0) and MeasureScaledIntroTextWidth
/// (0x403710), mono.c; InitializeFireworks (0x42D270) and TheEndFireWorks (0x42D2A0), music.c.</remarks>
public sealed class TrainSimStatusScreens(Wc1Game game, ITrainSimFlight flight)
{
    /// <summary>TITLE.VGA section of the shape font (pIntroFont).</summary>
    private const int IntroFontSection = 1;

    /// <summary>TITLE.VGA section of the fireworks (pFireworkShape).</summary>
    private const int FireworkSection = 17;

    /// <summary>Music track of the game over screen.</summary>
    private const int GameOverTrack = 22;

    private readonly Fireworks _fireworks = new();

    /// <remarks>C: ShowGetReadyScreen (0x439840, screens.c).</remarks>
    public async Task ShowGetReadyScreenAsync()
    {
        var events = game.Events;
        short frame = 0;
        var introFont = game.Resources.GetShape(LogicalFile.TitleVga, IntroFontSection);
        short distance = 400;
        flight.BeginGetReady();
        events.EscapePressed = false;
        do
        {
            if (flight.RefreshCockpitStatus())
            {
                DrawCenteredScaledIntroText(game.Graphics, flight.SpaceBuffer, introFont, "Get Ready", flight.ViewCenterX,
                    flight.ViewCenterY, (short)(0xc800 / distance));
                flight.DumpBufferToScreen();
            }
            if (distance > 100)
                distance -= 10;
            if (events.EscapePressed)
                break;
            frame++;
            await game.Display.PresentAsync();
        }
        while (frame < 40);
        events.EscapePressed = false;
        flight.EndGetReady();
    }

    /// <remarks>C: ShowVictoryScreen (0x439910, screens.c).</remarks>
    public async Task ShowVictoryScreenAsync()
    {
        var events = game.Events;
        var random = game.Random;
        short emptyCount = 0;
        _fireworks.Initialize();
        _fireworks.Shape = game.Resources.GetShape(LogicalFile.TitleVga, FireworkSection);
        short distance = 500;
        var introFont = game.Resources.GetShape(LogicalFile.TitleVga, IntroFontSection);
        short frame = 0;
        events.EscapePressed = false;
        flight.BeginVictory();
        var space = flight.SpaceBuffer;
        do
        {
            if (random.BelowOrEqual(7) == 0 && emptyCount != 0)
                _fireworks.Launch(random, space.Right, space.Bottom);
            if (flight.RefreshCockpitStatus())
            {
                emptyCount = _fireworks.Draw(game, space, Fireworks.SlotCount);
                DrawCenteredScaledIntroText(game.Graphics, space, introFont, "Victory", flight.ViewCenterX, flight.ViewCenterY,
                    (short)(0xc800 / distance));
                flight.DumpBufferToScreen();
            }
            if (events.EscapePressed)
                break;
            if (distance > 100)
                distance -= 10;
            frame++;
            await game.Display.PresentAsync();
        }
        while (frame < 80);
        events.EscapePressed = false;
    }

    /// <remarks>C: ShowGameOverScreen (0x439A80, screens.c).</remarks>
    public async Task ShowGameOverScreenAsync()
    {
        var events = game.Events;
        short frame = 0;
        var introFont = game.Resources.GetShape(LogicalFile.TitleVga, IntroFontSection);
        flight.BeginGameOver();
        short distance = 700;
        RoomSound.SpaceTrack(game, GameOverTrack, 2, 1);
        events.EscapePressed = false;
        do
        {
            if (flight.RefreshCockpitStatus())
            {
                if (frame > 20)
                {
                    DrawCenteredScaledIntroText(game.Graphics, flight.SpaceBuffer, introFont, "Game Over", flight.ViewCenterX,
                        flight.ViewCenterY, (short)(0xc800 / distance));
                }
                flight.DumpBufferToScreen();
            }
            if (events.EscapePressed)
                break;
            if (distance > 100)
                distance -= 10;
            frame++;
            await game.Display.PresentAsync();
        }
        while (frame < 80);
        RoomSound.StopMusicUnlessSuppressed(game);
        events.EscapePressed = false;
    }

    /// <summary>
    /// Width of a caption in the shape font at <paramref name="scale"/> (8.8): letters 'A'..'z'
    /// advance by their transformed right bound + 1 + scale*2/256, a space by scale*6/256; stops at '\n'.
    /// </summary>
    /// <remarks>C: MeasureScaledIntroTextWidth (0x403710, mono.c).</remarks>
    public static short MeasureScaledIntroTextWidth(Viewport spaceBuffer, ShapeTable introFont, string text, short scale)
    {
        ArgumentNullException.ThrowIfNull(text);
        Span<short> bounds = stackalloc short[4];
        bounds.Clear();
        short width = 0;
        int scaled = scale;
        foreach (char c in text)
        {
            if (c == 0 || c == '\n')
                break;
            if (c is >= 'A' and <= 'z')
            {
                ShapeBounds.GetTransformedShapeBounds(spaceBuffer, 0, 0, introFont, c - 'A', 0, scale, 0, bounds);
                width = unchecked((short)(width + bounds[2] + 1));
                width = unchecked((short)(width + (scaled * 2 >> 8)));
            }
            else if (c == ' ')
            {
                width = unchecked((short)(width + (scaled * 6 >> 8)));
            }
        }
        return width;
    }

    /// <summary>Draws a caption centred on (centreX, baselineY) in the space buffer, scaled by <paramref name="scale"/> (8.8).</summary>
    /// <remarks>C: DrawCenteredScaledIntroText (0x4037A0, mono.c).</remarks>
    public static void DrawCenteredScaledIntroText(GraphicsContext gfx, Viewport spaceBuffer, ShapeTable introFont, string text,
        short centreX, short baselineY, short scale)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        ArgumentNullException.ThrowIfNull(text);
        Span<short> bounds = stackalloc short[4];
        bounds.Clear();
        int scaled = scale;
        short x = unchecked((short)(centreX - MeasureScaledIntroTextWidth(spaceBuffer, introFont, text, scale) / 2));
        short y = unchecked((short)(baselineY - (scaled * 16 >> 9)));
        foreach (char c in text)
        {
            if (c == 0 || c == '\n')
                break;
            if (c is >= 'A' and <= 'z')
            {
                gfx.DrawSpriteScaled(spaceBuffer, x, y, introFont, c - 'A', 0, scale, 0);
                ShapeBounds.GetTransformedShapeBounds(spaceBuffer, 0, 0, introFont, c - 'A', 0, scale, 0, bounds);
                x = unchecked((short)(x + bounds[2] + 1));
                x = unchecked((short)(x + (scaled * 2 >> 8)));
            }
            else if (c == ' ')
            {
                x = unchecked((short)(x + (scaled * 6 >> 8)));
            }
        }
    }
}
