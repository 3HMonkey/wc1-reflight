using WingCommander.Audio.Director;
using WingCommander.Core.Resources;
using WingCommander.Game.Flow;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Game.Screens.Scenes;

public sealed partial class SceneDirector
{
    /// <remarks>C: apszCampaignVictoryText (0x0046AD90).</remarks>
    private static readonly string[] CampaignVictoryText =
    [
        "Destroying the remains of the Kilrathi naval power in the sector...",
        "The Tiger's Claw closes in for the kill...",
        "And the last Kilrathi planet in the sector falls!",
    ];

    /// <remarks>C: szTigerClawEscapeOpening / Jump / Closing (0x0046AE74..0x0046AEE4).</remarks>
    private const string TigerClawEscapeOpening = "Fleeing from the overwelming Kilrathi forces in the sector...";
    private const string TigerClawEscapeJump = "The Tiger's Claw manages to jump out. Barely.";
    private const string TigerClawEscapeClosing = "There'll be other sectors, other battles...";

    /// <summary>Caption format of the endings ("%X%Y%s%P": the colour stays as it is).</summary>
    private static ReadOnlySpan<byte> EndingCaptionFormat => "%X%Y%s%P"u8;

    /// <summary>Firework slots of the end screen.</summary>
    /// <remarks>C: aFireworks[30] (FireworkState, 0x005A6900).</remarks>
    private readonly (short Frame, short X, short Y, short Variant)[] _fireworks = new (short, short, short, short)[30];

    private ShapeTable? _fireworkShape;

    /// <summary>
    /// The Vega campaign is won: captions over the Tiger's Claw's final attack (250 frames, music
    /// 33), then the celebration animation (TITLE.VGA section 5) and a fade to black.
    /// </summary>
    /// <remarks>
    /// C: ShowCampaignVictorySequence (0x42FC00, screen.c).
    /// <para><b>Placeholder:</b> the attack is a canned 3D scene (action sphere 0x12, scripted
    /// camera, planet object) with 2D planet and projectile sprites placed by the 3D eye; it needs the
    /// flight engine. Until then the space view (rows 24..151) stays black while the captions,
    /// timing and Esc skip of the 250 frames are kept. The celebration animation is ported
    /// faithfully.</para>
    /// </remarks>
    public async Task CampaignVictorySequenceAsync()
    {
        var stage = Stage;
        stage.PreloadMusicTrack(MusicTrack.DebriefingSuccessful);
        stage.SpaceTrack(MusicTrack.DebriefingSuccessful, 2, 1);
        stage.InitializeConversationText();
        Events.EscapePressed = false;
        await Stage.Display.ClearViewportAsync(stage.Screen, Black);
        using var attack = _game.FlightLayer?.BeginCannedScene(CannedScene.CampaignVictory);
        for (short frame = 0; frame < 250; frame++)
        {
            int textIndex = frame switch
            {
                0 => 0,
                100 => 1,
                180 => 2,
                _ => -1,
            };
            if (textIndex != -1)
            {
                stage.ClearSubtitle();
                Gfx.SetTextContext(stage.Text);
                Gfx.FormatTextBufferFromStart(EndingCaptionFormat, 0, ConversationStage.SubtitleY, CampaignVictoryText[textIndex]);
            }
            // Update_3Space + Draw_3Space_Frame + dump_buffer_to_screen (black view without a flight layer).
            attack?.Step();
            Events.PumpWindowMessages();
            await stage.PresentAsync();
            if (Events.EscapePressed)
                break;
            await stage.PresentAsync();
        }

        if (!Events.EscapePressed)
        {
            var celebration = Shape(LogicalFile.TitleVga, 5);
            short animationFrame = 1;
            await stage.Display.ClearViewportAsync(stage.Screen, Black);
            await stage.Display.WaitForVerticalBlankAsync();
            Gfx.DrawSpriteDefault(stage.Screen, 0, 0, celebration, 0);
            int elapsed = 0;
            await Events.WaitForSceneAdvanceAsync(14);
            do
            {
                _game.Timing.SetFrameTimerPeriod(8);
                Gfx.DrawSpriteDefault(stage.Screen, 0, 0, celebration, animationFrame++);
                if (animationFrame > 17)
                    animationFrame = 12;
                while (!_game.Timing.IsFrameTickElapsed())
                {
                    if (Events.EscapePressed || Events.CheckEscaped() != 0)
                    {
                        elapsed = 1000;
                        break;
                    }
                    await stage.Idle();
                }
                elapsed++;
                await stage.PresentAsync();
            }
            while (elapsed < 40);
            await stage.FadeToColourAsync(Black);
            Gfx.ClearViewport(stage.ModalSource, Black);
            await stage.PresentAsync();
            await stage.RestoreGamePaletteAsync();
        }

        Events.EscapePressed = false;
        stage.StopMusicUnlessSuppressed();
        await stage.FadeToColourAsync(Black);
        Gfx.ClearViewport(stage.ModalSource, Black);
        await stage.PresentAsync();
        await stage.RestoreGamePaletteAsync();
    }

    /// <summary>
    /// The Vega campaign is lost: the Tiger's Claw flees and jumps out (260 frames, music 34) with
    /// three captions and the jump flash.
    /// </summary>
    /// <remarks>
    /// C: ShowTigerClawEscapeScene (0x430150, screen.c).
    /// <para><b>Placeholder:</b> the flight is a canned 3D scene (action sphere 0x13, scripted camera,
    /// hyperspace flash object) and needs the flight engine. The space view (rows 24..151) stays
    /// black except for the white frame of the jump flash (frame 198); captions at frames 0, 150 and
    /// 210, timing and Esc skip are kept.</para>
    /// </remarks>
    public async Task TigerClawEscapeSceneAsync()
    {
        var stage = Stage;
        stage.PreloadMusicTrack(MusicTrack.DebriefingUnsuccessful);
        stage.SpaceTrack(MusicTrack.DebriefingUnsuccessful, 2, 1);
        stage.InitializeConversationText();
        await Stage.Display.ClearViewportAsync(stage.Screen, Black);
        stage.ClearSubtitle();
        Gfx.SetTextContext(stage.Text);
        Gfx.FormatTextBufferFromStart(EndingCaptionFormat, 0, ConversationStage.SubtitleY, TigerClawEscapeOpening);
        Events.EscapePressed = false;
        var spaceView = new Viewport(stage.Screen.Surface, 0, ConversationStage.SceneTop, 319, ConversationStage.SceneBottom);
        using var escape = _game.FlightLayer?.BeginCannedScene(CannedScene.TigerClawEscape);
        short frame = 0;
        do
        {
            Events.PumpWindowMessages();
            if (escape is not null)
                escape.Step(); // the canned scene draws the jump flash itself
            else
                Gfx.ClearViewport(spaceView, frame == 199 ? PaletteColours.ViewportClear : Black);
            switch (frame)
            {
                case 150:
                    stage.ClearSubtitle();
                    Gfx.SetTextContext(stage.Text);
                    Gfx.FormatTextBufferFromStart(EndingCaptionFormat, 0, ConversationStage.SubtitleY, TigerClawEscapeJump);
                    break;
                case 210:
                    stage.ClearSubtitle();
                    Gfx.SetTextContext(stage.Text);
                    Gfx.FormatTextBufferFromStart(EndingCaptionFormat, 0, ConversationStage.SubtitleY, TigerClawEscapeClosing);
                    break;
            }
            if (Events.EscapePressed)
                break;
            frame++;
            await stage.PresentAsync();
        }
        while (frame < 260);
        stage.ClearSubtitle();
        stage.StopMusicUnlessSuppressed();
    }

    /// <summary>
    /// The end of a campaign: the pilot's decorations first, then "The End" fades in, fireworks burst
    /// when <paramref name="fireworks"/> is set, and "For Now..." follows while the music fades
    /// (320 frames, music 23).
    /// </summary>
    /// <remarks>C: ShowTheEndScreen (0x4304F0, screen.c), InitializeFireworks (0x42D270) and
    /// TheEndFireWorks (0x42D2A0, music.c). The firework sound of the Kilrathi Saga build (SoundFxTick)
    /// is a stub there and is not played. Firework positions use the cinematic space view bounds
    /// (319 x 127; the original read stSpaceBuffer's rectangle). The flight input pump the original
    /// installed is not ported.</remarks>
    public async Task TheEndScreenAsync(bool fireworks)
    {
        var stage = Stage;
        stage.PreloadMusicTrack(MusicTrack.TitleFanfare);
        stage.SpaceTrack(MusicTrack.TitleFanfare, 2, 1);
        stage.InitializeConversationViewport();
        await ViewMedalsAsync();
        Gfx.ClearViewport(stage.ModalSource, Black);
        Gfx.ClearViewport(Scene, Black);
        for (int i = 0; i < _fireworks.Length; i++)
            _fireworks[i].Frame = -1;
        _fireworkShape = Shape(LogicalFile.TitleVga, 0x11);
        _introFont = Shape(LogicalFile.TitleVga, 1);
        PrintCaption("The End"u8);
        await stage.PanToScreenAsync(Scene, stage.Screen);
        Events.EscapePressed = false;
        short activeFireworks = 0;
        short frame = 0;
        do
        {
            Gfx.ClearViewport(Scene, Black);
            if (fireworks && activeFireworks != 0 && (_game.Random.BelowOrEqual(100) < 40 || frame > 280))
            {
                for (int slot = 0; slot < _fireworks.Length; slot++)
                {
                    if (_fireworks[slot].Frame != -1)
                        continue;
                    _fireworks[slot].Frame = 0;
                    _fireworks[slot].X = _game.Random.InRange(0, 319);
                    _fireworks[slot].Y = _game.Random.InRange(0, 127);
                    _fireworks[slot].Variant = _game.Random.InRange(0, 2);
                    break;
                }
            }
            activeFireworks = TheEndFireworks(_fireworks.Length);
            if (frame < 160)
            {
                PrintCaption("The End"u8);
            }
            else if (frame > 190)
            {
                stage.StopMusic((short)(320 - frame));
                PrintCaption("For Now..."u8);
            }
            frame++;
            await stage.RefreshAsync();
            await stage.PresentAsync();
        }
        while (frame < 320);
        _fireworkShape = null;
        _introFont = null;
        stage.ResetScreenClipToFullHeight();
        stage.StopMusicUnlessSuppressed();
    }

    private void PrintCaption(ReadOnlySpan<byte> text)
    {
        if (_introFont is not null)
            Gfx.PrintSubtitle(Scene, _introFont, text);
    }

    /// <summary>Draws the running fireworks (8 frames each, 3 variants) and returns how many of the first <paramref name="count"/> slots are free.</summary>
    /// <remarks>C: TheEndFireWorks (0x42D2A0, music.c), slots scanned from the last to the first.</remarks>
    private short TheEndFireworks(int count)
    {
        short emptyCount = 0;
        for (int index = count - 1; index >= 0; index--)
        {
            ref var firework = ref _fireworks[index];
            if (firework.Frame == -1)
            {
                emptyCount++;
                continue;
            }
            Gfx.DrawSpriteDefault(Scene, firework.X, firework.Y, _fireworkShape, firework.Frame + firework.Variant * 8);
            if (firework.Frame++ == 7)
                firework.Frame = -1;
        }
        return emptyCount;
    }
}
