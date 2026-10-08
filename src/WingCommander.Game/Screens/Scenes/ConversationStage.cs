using WingCommander.Audio.Director;
using WingCommander.Core.Runtime;
using WingCommander.Game.Input;
using WingCommander.Game.Timing;
using WingCommander.Game.Video;
using WingCommander.Graphics;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Text;

namespace WingCommander.Game.Screens.Scenes;

/// <summary>
/// The layout every non-flight cutscene uses: an off-screen 320x128 scene buffer that is copied to
/// screen rows 24..151, and the subtitle strip below it (rows 152..199) with the conversation
/// text context (font 0, centred). Also the small presentation helpers the cutscenes share
/// (refresh, pan, fades, music and sound-effect requests), each mapped to the original's
/// blocking call as an await on the virtual clock (ADR-009).
/// </summary>
/// <remarks>C: stSceneBuffer, stConversationTextViewport, stConversationTextContext,
/// stModalSourceViewport; InitializeConversationViewport (0x427B20), ResetScreenClipToFullHeight
/// (0x427BA0), InitializeConversationText (0x427BC0), RefreshMemoryStatusOverlay (0x427C30), main.c;
/// PanToScreen (0x439430, screens.c); FadeViewportPaletteToColour (0x42A700, hudmsg.c);
/// RestoreGamePalette (0x401020, winmain.c).</remarks>
public sealed class ConversationStage
{
    /// <summary>First screen row of the scene picture.</summary>
    public const short SceneTop = 24;

    /// <summary>Last screen row of the scene picture.</summary>
    public const short SceneBottom = 151;

    /// <summary>First row of the subtitle strip.</summary>
    public const short TextTop = 152;

    /// <summary>Row the subtitles are printed at ("%X%Y" = 0, 160).</summary>
    public const short SubtitleY = 160;

    /// <summary>The subtitle format of every shot handler (szConversationTextFormat and its twins).</summary>
    public static ReadOnlySpan<byte> SubtitleFormat => "%X%Y%F%s%P"u8;

    private readonly Wc1Game _game;

    public ConversationStage(Wc1Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        _game = game;
        var screen = game.Graphics.Screen ?? throw new InvalidOperationException("The display has no screen viewport.");
        ModalSource = new Viewport(screen.Surface, 0, 0, 319, 199);
        TextViewport = new Viewport(screen.Surface, 0, TextTop, 319, 199);
        Text = new TextContext { Viewport = TextViewport, TextBuffer = new byte[TextBufferSize], Alignment = TextContext.AlignCentre };
    }

    /// <summary>
    /// Size of the string builder of the conversation context. The original used the 200-byte
    /// szDefaultTextBuffer; the port allows 512 bytes so a long expanded subtitle cannot be cut.
    /// </summary>
    public const int TextBufferSize = 512;

    /// <summary>The scene picture buffer (unallocated outside cutscenes).</summary>
    /// <remarks>C: stSceneBuffer.</remarks>
    public Viewport SceneBuffer { get; } = new();

    /// <summary>A full-screen alias of the screen (clearing it does not present).</summary>
    /// <remarks>C: stModalSourceViewport.</remarks>
    public Viewport ModalSource { get; }

    /// <summary>The subtitle strip, rows 152..199.</summary>
    /// <remarks>C: stConversationTextViewport.</remarks>
    public Viewport TextViewport { get; }

    /// <summary>The subtitle text context (font 0, colour 15 on black, centred).</summary>
    /// <remarks>C: stConversationTextContext.</remarks>
    public TextContext Text { get; }

    public Wc1Game Game => _game;

    public GraphicsContext Graphics => _game.Graphics;

    public Display Display => _game.Display;

    public EventManager Events => _game.Events;

    public FrameTiming Timing => _game.Timing;

    /// <summary>The screen viewport object (its rectangle is narrowed to rows 24..151 during scenes, like stScreen).</summary>
    public Viewport Screen => _game.Graphics.Screen!;

    /// <summary>The music director (null without audio).</summary>
    public MusicDirector? Music => _game.Audio?.Music;

    /// <summary>Sound effects for the cutscenes: the game-wide manager (null without audio).</summary>
    public SoundEffectManager? SoundEffects => _game.Audio?.Sfx;

    /// <summary>
    /// Clears the whole screen (without presenting), narrows the screen viewport to rows 24..151
    /// and allocates the 320x128 scene buffer cleared to black.
    /// </summary>
    /// <remarks>C: InitializeConversationViewport (0x427B20, main.c).</remarks>
    public void InitializeConversationViewport()
    {
        Graphics.ClearViewport(ModalSource, PaletteColours.Black);
        Screen.Top = SceneTop;
        Screen.Bottom = SceneBottom;
        SceneBuffer.SetViewportRect(0, 0, 319, 127);
        SceneBuffer.AllocateViewport(PaletteColours.Black);
    }

    /// <summary>Frees the scene buffer and gives the screen viewport its full height back.</summary>
    /// <remarks>C: ResetScreenClipToFullHeight (0x427BA0, main.c).</remarks>
    public void ResetScreenClipToFullHeight()
    {
        SceneBuffer.FreeViewport();
        Screen.Top = 0;
        Screen.Bottom = 199;
    }

    /// <summary>Sets up the subtitle strip and its text context (font 0, colour 15 on black, centred) and makes it current.</summary>
    /// <remarks>C: InitializeConversationText (0x427BC0, main.c).</remarks>
    public void InitializeConversationText()
    {
        TextViewport.CopyFrom(ModalSource);
        TextViewport.Top = TextTop;
        Text.Viewport = TextViewport;
        Text.Alignment = TextContext.AlignCentre;
        Graphics.InitializeTextContextFromFont(Text, 0, PaletteColours.ViewportClear, PaletteColours.Black);
        Graphics.SetTextContext(Text);
    }

    /// <summary>Waits for the vertical blank and copies the scene buffer into the screen viewport (no present).</summary>
    /// <remarks>C: RefreshMemoryStatusOverlay (0x427C30, main.c); its memory display is debug-only.</remarks>
    public async Task RefreshAsync()
    {
        await Display.WaitForVerticalBlankAsync();
        if (SceneBuffer.IsAllocated)
            Graphics.CopyViewportContents(SceneBuffer, Screen);
    }

    /// <summary>DIBslam + DIBslamReal: present and wait for the frame deadline.</summary>
    public Task PresentAsync() => Display.PresentAsync();

    /// <summary>Clears the subtitle strip and prints <paramref name="expandedText"/> centred at row 160 in <paramref name="colour"/>.</summary>
    /// <remarks>C: ClearViewport(&amp;stConversationTextViewport) + FormatTextBufferFromStart("%X%Y%F%s%P", 0, 160, colour, text),
    /// the opening of every shot handler. The current text context is used, like the original.</remarks>
    public void ShowSubtitle(string expandedText, int colour)
    {
        Graphics.ClearViewport(TextViewport, PaletteColours.Black);
        Graphics.FormatTextBufferFromStart(SubtitleFormat, 0, SubtitleY, colour, expandedText);
    }

    /// <summary>Clears the subtitle strip.</summary>
    public void ClearSubtitle() => Graphics.ClearViewport(TextViewport, PaletteColours.Black);

    /// <summary>
    /// Fades a new picture in: all non-black colours take the colour of the destination's top-left
    /// pixel, the source is copied and presented, then the palette steps back once per vertical blank.
    /// </summary>
    /// <remarks>C: PanToScreen (0x439430, screens.c).</remarks>
    public async Task PanToScreenAsync(Viewport source, Viewport destination)
    {
        var pan = Graphics.BeginPanToScreen(source, destination);
        await Display.WaitForVerticalBlankAsync();
        Display.PaletteChanged();
        await Display.PresentAsync();
        while (pan.Step())
        {
            await Display.WaitForVerticalBlankAsync();
            Display.PaletteChanged();
        }
        await Display.PresentAsync();
    }

    /// <summary>Fades every non-black colour to <paramref name="colourIndex"/>, one step per vertical blank, then presents.</summary>
    /// <remarks>C: FadeViewportPaletteToColour (0x42A700, hudmsg.c).</remarks>
    public async Task FadeToColourAsync(int colourIndex)
    {
        var fade = Graphics.BeginFadeViewportPaletteToColour(ModalSource, colourIndex);
        while (fade.Step())
            await Display.WaitForVerticalBlankAsync();
        await Display.PresentAsync();
    }

    /// <summary>Reloads the saved game palette after the original's two vertical-blank waits.</summary>
    /// <remarks>C: RestoreGamePalette (0x401020, winmain.c) -> DIBwholePaletteFromWords.</remarks>
    public async Task RestoreGamePaletteAsync()
    {
        await Display.WaitForVerticalBlankAsync();
        await Display.WaitForVerticalBlankAsync();
        Graphics.Palette.RestoreGamePalette();
        Display.PaletteChanged();
    }

    /// <summary>Requests a music track (no-op without audio).</summary>
    /// <remarks>C: spacetrack (0x42E880, music.c).</remarks>
    public void SpaceTrack(int track, int mode, short enabled) => Music?.SpaceTrack(track, mode, enabled);

    /// <remarks>C: PreloadMusicTrackHook (0x424CE0, logic.c).</remarks>
    public void PreloadMusicTrack(int track) => Music?.PreloadMusicTrackHook(track);

    /// <remarks>C: StopMusicUnlessSuppressed (0x42E8B0, music.c).</remarks>
    public void StopMusicUnlessSuppressed() => Music?.StopMusicUnlessSuppressed();

    /// <remarks>C: StopMusic (0x42E350, music.c).</remarks>
    public void StopMusic(short value) => Music?.StopMusic(value);

    /// <summary>Plays a non-positional sound effect (the cutscenes always pass source -1, priority 0).</summary>
    /// <remarks>C: PlaySfxWaveFileByNumber(number, -1, 0) (0x42EF30, music.c).</remarks>
    public void PlaySfx(int soundNumber) => SoundEffects?.PlaySfx(soundNumber, -1, 0);

    /// <remarks>C: FlushSoundEffectsAndLog (0x42EF10, music.c).</remarks>
    public void FlushSoundEffects() => SoundEffects?.FlushSoundEffectsAndLog();

    /// <summary>Yields one virtual millisecond inside a polling loop that consumed nothing (the original spun).</summary>
    public SchedulerAwaitable Idle() => _game.Scheduler.Delay(1);
}
