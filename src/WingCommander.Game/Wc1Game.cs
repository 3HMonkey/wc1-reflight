using WingCommander.Audio.Director;
using WingCommander.Core.Fonts;
using WingCommander.Core.Numerics;
using WingCommander.Core.Rendering;
using WingCommander.Core.Resources;
using WingCommander.Core.Runtime;
using WingCommander.Game.Audio;
using WingCommander.Game.Campaign;
using WingCommander.Game.Config;
using WingCommander.Game.Flow;
using WingCommander.Game.Input;
using WingCommander.Game.Resources;
using WingCommander.Game.Runtime;
using WingCommander.Game.Screens;
using WingCommander.Game.Screens.Ui;
using WingCommander.Game.Timing;
using WingCommander.Game.Video;
using WingCommander.Graphics;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Text;

namespace WingCommander.Game;

/// <summary>How the game is started (command line of the wc1 executable, tests).</summary>
public sealed class Wc1GameOptions
{
    /// <summary>Original-style startup switches, appended to the WINGCMDR.CFG tokens.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Creates the DOS audio backend and starts host audio.</summary>
    public bool Audio { get; init; } = true;

    /// <summary>Skips the DOS startup intro.</summary>
    public bool SkipIntro { get; init; }

    /// <summary>
    /// Publishes the game's text for output-resolution drawing (<see cref="RenderFrame.Text"/>)
    /// and the flight key help (<see cref="RenderFrame.KeyHelp"/>); set when the renderer draws
    /// them (ADR-013).
    /// </summary>
    public bool HighResolutionText { get; init; }

    /// <summary>TrueType fonts that replace FONTS.FNT fonts in the output-resolution text (by font index).</summary>
    public IReadOnlyDictionary<int, ReplacementFont> ReplacementFonts { get; init; } = new Dictionary<int, ReplacementFont>();

    /// <summary>Messages about optional features (missing replacement fonts); null = standard error.</summary>
    public Action<string>? Log { get; init; }

    /// <summary>
    /// The configuration file the player's settings are read from and saved to (ADR-015); null =
    /// settings live only in memory and wc1.cfg (tests, tools).
    /// </summary>
    public GameConfiguration? Configuration { get; init; }

    /// <summary>Runtime control of the window and renderer for the settings menu (null = headless).</summary>
    public IDisplayControl? Display { get; init; }

    /// <summary>Overrides the saved "sharp text" setting for this run (command line); null = use the setting.</summary>
    public bool? SharpTextOverride { get; init; }

    /// <summary>Overrides the saved "modern fonts" setting for this run (command line); null = use the setting.</summary>
    public bool? ModernFontsOverride { get; init; }
}

/// <summary>
/// The game: composes runtime, resources, graphics, input, audio and settings, and runs
/// GameMain as the root coroutine. Screens receive this object and use its services.
/// </summary>
/// <remarks>C: GameMain (main.c); LoadOriginFxDrivers, EMStartUp, InitializeEventManagerResources,
/// LoadGamePaletteFile, InitializeGameTextContexts (logic.c).</remarks>
public sealed class Wc1Game
{
    /// <summary>Version string printed by the '?' switch.</summary>
    public const string GameVersion = "1.03F-95";

    private readonly Wc1GameOptions _options;
    private string? _saveGamePath;

    public Wc1Game(GameRuntime runtime, GameDirectory directory, Wc1GameOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(directory);
        Runtime = runtime;
        Directory = directory;
        Resources = new GameResources(directory);
        _options = options ?? new Wc1GameOptions();
        Cursor = new SoftwareCursor(Graphics, Events);
        Display.Cursor = Cursor;
        Title = new TitleSequence(this);
        Session = new CampaignSession(Random);
        Screens = new GameFlowScreens(this);
        Flow = new CampaignFlow(Session, Screens, Random, directory, Events);
    }

    /// <summary>The live campaign (roster, state, CAMP data, flow flags).</summary>
    public CampaignSession Session { get; }

    /// <summary>The screens the campaign flow drives.</summary>
    public GameFlowScreens Screens { get; }

    /// <summary>StartNewCampaign and GameFlow.</summary>
    public CampaignFlow Flow { get; }

    /// <summary>
    /// The space flight layer (WingCommander.Game.Flight), plugged in by the executable; null in
    /// builds and tests without the simulation, where flight shows a placeholder.
    /// </summary>
    public IFlightLayer? FlightLayer { get; set; }

    public GameRuntime Runtime { get; }

    public GameDirectory Directory { get; }

    public GameResources Resources { get; }

    public GameScheduler Scheduler => Runtime.Scheduler;

    public FrameTiming Timing => Runtime.Timing;

    public EventManager Events => Runtime.Events;

    public Display Display => Runtime.Display;

    /// <summary>Raster, text and palette state; its screen viewport is the display's working frame.</summary>
    public GraphicsContext Graphics => Runtime.Display.Graphics;

    /// <summary>The shared C rand() generator.</summary>
    public CRandom Random => Runtime.Random;

    public SoftwareCursor Cursor { get; }

    /// <summary>Startup switches (WINGCMDR.CFG and arguments).</summary>
    public StartupOptions Options { get; } = new();

    /// <summary>Volumes and the cheat flag (wc1.cfg in the user data directory).</summary>
    public GameSettings Settings { get; private set; } = GameSettings.FromText("");

    /// <summary>Volume settings shared with the audio layer (mirrors <see cref="Settings"/>).</summary>
    public AudioVolumeSettings Volumes { get; } = new();

    /// <summary>The player's settings of the pause menu (ADR-015), stored in config.json.</summary>
    public UserSettings Preferences { get; private set; } = new();

    /// <summary>Runtime control of the window and renderer (null in headless runs).</summary>
    public IDisplayControl? DisplayControl => _options.Display;

    /// <summary>True when the renderer draws text at output resolution (the "sharp text" setting applies).</summary>
    public bool SupportsSharpText => Display.TextTracker is not null;

    /// <summary>True when replacement fonts are available (the "fonts" setting applies).</summary>
    public bool SupportsModernFonts => SupportsSharpText && _options.ReplacementFonts.Count > 0;

    /// <summary>DOS audio (null when started without audio).</summary>
    public GameAudio? Audio { get; private set; }

    /// <summary>Music hooks of the startup intro (null: unsynchronised intro).</summary>
    public IIntroMusic? IntroMusic { get; set; }

    /// <summary>The default text context (font 1, primary text colour on black, over a screen alias).</summary>
    /// <remarks>C: stDefaultTextContext over stModalSourceViewport.</remarks>
    public TextContext DefaultText { get; } = new() { TextBuffer = new byte[512] };

    /// <summary>The title screen (keeps its menu regions between visits, like the original globals).</summary>
    public TitleSequence Title { get; }

    /// <summary>Glyph images for output-resolution text (null when it is off).</summary>
    public GlyphImageSource? Glyphs { get; private set; }

    /// <summary>
    /// The key reference the flight shows next to the picture (null without output-resolution
    /// text). Layers provide its content with <see cref="ShowKeyHelp"/>; F10 toggles it.
    /// </summary>
    public KeyHelpOverlay? KeyHelp { get; private set; }

    private bool _keyHelpActive;

    /// <summary>Makes <paramref name="sections"/> the key help content while a layer (the flight) is active.</summary>
    public void ShowKeyHelp(string title, IReadOnlyList<KeyHelpSection> sections)
    {
        if (KeyHelp is not { } help)
            return;
        if (!ReferenceEquals(help.Sections, sections))
            help.Sections = sections;
        if (help.Title != title)
            help.Title = title;
        _keyHelpActive = true;
        UpdateKeyHelpVisibility();
    }

    /// <summary>The layer that showed the key help ended.</summary>
    public void HideKeyHelp()
    {
        _keyHelpActive = false;
        UpdateKeyHelpVisibility();
    }

    /// <summary>F10: the player's choice whether the key help is shown (saved with the settings).</summary>
    public void ToggleKeyHelp() => SetKeyHelp(!Preferences.KeyHelp);

    /// <summary>Shows or hides the key help (while a layer provides it) and saves the choice.</summary>
    public void SetKeyHelp(bool visible)
    {
        Preferences.KeyHelp = visible;
        SaveSettings();
        UpdateKeyHelpVisibility();
    }

    private void UpdateKeyHelpVisibility()
    {
        if (KeyHelp is { } help)
            help.Visible = _keyHelpActive && Preferences.KeyHelp;
    }

    /// <summary>
    /// Opens the port's pause menu (Esc; ADR-015) over the current picture: resume, settings, end
    /// simulation (training simulator), quit.
    /// </summary>
    public Task<PauseMenuChoice> ShowPauseMenuAsync(PauseMenuContext context) => PauseMenu.ShowAsync(this, context);

    /// <summary>Turns the output-resolution text on or off (no effect without a supporting renderer).</summary>
    public void SetSharpText(bool sharp)
    {
        Preferences.SharpText = sharp;
        if (Display.TextTracker is not { } tracker)
            return;
        tracker.Enabled = sharp;
        Runtime.Frame.Text = sharp ? Display.Text : null;
    }

    /// <summary>Switches between the bundled replacement fonts and the vectorized originals.</summary>
    public void SetModernFonts(bool modern)
    {
        Preferences.ModernFonts = modern;
        if (Glyphs is not { } glyphs)
            return;
        foreach (var (fontIndex, font) in _options.ReplacementFonts)
        {
            if (!modern)
            {
                glyphs.SetReplacement(fontIndex, null);
                continue;
            }
            try
            {
                glyphs.SetReplacement(fontIndex, TrueTypeFont.Load(font.Data.Span), font.Name);
            }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException or IndexOutOfRangeException or ArgumentException)
            {
                (_options.Log ?? Console.Error.WriteLine)($"Replacement font for font {fontIndex} not used ({font.Name}): {e.Message}");
            }
        }
    }

    /// <summary>
    /// SAVEGAME.WLD in the user data directory. On first use an existing SAVEGAME.WLD from the
    /// game's GAMEDAT folder is copied there, so DOS saves carry over (the install itself is
    /// never written).
    /// </summary>
    public string SaveGamePath
    {
        get
        {
            if (_saveGamePath is null)
            {
                string path = Path.Combine(Runtime.Host.UserDataDirectory, SaveGameFile.FileName);
                if (!File.Exists(path) && Directory.TryResolve(SaveGameFile.FileName, out string original))
                {
                    System.IO.Directory.CreateDirectory(Runtime.Host.UserDataDirectory);
                    File.Copy(original, path);
                }
                _saveGamePath = path;
            }
            return _saveGamePath;
        }
    }

    /// <summary>Starts the game as the runtime's root coroutine.</summary>
    public void Start() => Runtime.Start(_ => RunAsync());

    /// <summary>GameMain: start-up, intro, then the title and campaign loop.</summary>
    public async Task RunAsync()
    {
        Initialize();
        if (!_options.SkipIntro)
            await DosIntro.PlayAsync(this);
        Events.EscapePressed = false;
        while (true)
        {
            Session.CampaignStartupMode = true;
            switch (await Title.RunAsync())
            {
                case TitleSelection.NewGame:
                    await Flow.StartNewCampaignAsync(0);
                    break;
                case TitleSelection.SecretMissions1:
                    await Flow.StartNewCampaignAsync(1);
                    break;
                case TitleSelection.SecretMissions2:
                    await Flow.StartNewCampaignAsync(2);
                    break;
            }
            // Continue goes straight to GameFlow: in start-up mode it opens the barracks (load a bunk).
            while (await Flow.RunAsync())
            {
            }
        }
    }

    /// <summary>Everything GameMain does before the intro.</summary>
    public void Initialize()
    {
        // Settings and switches (CheckLauncherAndConfig, LoadWingCmdrCfgFile, the argument loop).
        Settings = GameSettings.Load(Runtime.Host.UserDataDirectory);
        // config.json wins; wc1.cfg supplies what it does not have yet (older installs).
        var defaults = new UserSettings
        {
            MusicVolume = Settings.MusicVolume / 2,
            SoundVolume = Settings.SfxVolume / 2,
            KeyHelp = Settings.KeyHelp,
        };
        if (_options.Display is { } display)
            defaults.CaptureFrom(display);
        Preferences = UserSettings.Read(_options.Configuration, defaults);
        Volumes.MusicVolume = Preferences.MusicVolume * 2;
        Volumes.SfxVolume = Preferences.SoundVolume * 2;
        var configTokens = StartupOptions.ReadConfigTokens(Directory);
        Options.ApplyLauncherConfig(Settings.Cheater, configTokens);
        Options.ApplyArguments(StartupOptions.CombineArguments(configTokens, _options.Arguments), GameVersion);
        Timing.SetCinematicFrameTiming();

        // LoadOriginFxDrivers: audio, event manager, frame timer, palette, fonts, text contexts.
        if (_options.Audio)
        {
            Audio = GameAudio.Create(Directory, Volumes, Random, Scheduler);
            Audio.Music.MusicPlaybackMode = Options.MusicPlaybackMode;
            Runtime.Host.StartAudio(Audio.Backend.Mixer);
            Events.ServiceHook = Audio.Service;
            IntroMusic = Audio;
        }
        InitializeEventManager();
        Timing.SetFrameTimerPeriod(0x78);
        Graphics.LoadGamePaletteFile(Directory.ReadFile(GamePaletteFile.FileName));
        Graphics.Fonts = FontCache.FromGameDirectory(Directory);
        InitializeGameTextContexts();
        if (_options.HighResolutionText)
            EnableHighResolutionText();
    }

    /// <summary>
    /// Output-resolution text (ADR-013): glyph images with the configured replacement fonts, the
    /// display's text layer and the flight key help (font 1, prepared completely for its layout),
    /// published in the runtime's render frame; F10 toggles the key help.
    /// </summary>
    private void EnableHighResolutionText()
    {
        var glyphs = new GlyphImageSource(new GlyphImageCache());
        Glyphs = glyphs;
        SetModernFonts(_options.ModernFontsOverride ?? Preferences.ModernFonts);
        Display.EnableHighResolutionText(glyphs);
        Runtime.Frame.Text = Display.Text;
        bool sharp = _options.SharpTextOverride ?? Preferences.SharpText;
        SetSharpText(sharp);
        KeyHelp = new KeyHelpOverlay(glyphs.Cache) { Font = 1 };
        glyphs.AddFont(Graphics.Fonts!.Get(1));
        Runtime.Frame.KeyHelp = KeyHelp;
        UpdateKeyHelpVisibility();
        Events.PortHotkey = (scanCode, _) =>
        {
            if (scanCode != 0x44) // F10
                return false;
            ToggleKeyHelp();
            return true;
        };
    }

    /// <summary>True when a bunk of SAVEGAME.WLD holds a game.</summary>
    /// <remarks>C: AnySavedGames (0x41AD50). Its Secret Missions flag (DAT_005a7d9c) is already
    /// set unconditionally at start-up, so it is not tracked.</remarks>
    public bool AnySavedGames() => SaveGameFile.AnySavedGames(SaveGamePath, out _);

    /// <summary>Event manager with the ARROW.VGA cursor on the screen viewport (hidden until a screen shows it).</summary>
    /// <remarks>C: EMStartUp (0x421AB0) + InitializeEventManagerResources (0x421A60).</remarks>
    private void InitializeEventManager()
    {
        Events.Initialize(PointerBounds.FullScreen);
        Cursor.Viewport = Graphics.Screen;
        Cursor.SetShape(Resources.GetShape(LogicalFile.ArrowVga, 0), 0);
    }

    /// <remarks>C: InitializeGameTextContexts (0x421D80). The HUD message context follows with the
    /// flight screens.</remarks>
    private void InitializeGameTextContexts()
    {
        DefaultText.Viewport = Graphics.Screen!.Clone();
        Graphics.InitializeTextContextFromFont(DefaultText, 1, PaletteColours.PrimaryText, PaletteColours.Black);
    }

    /// <summary>Temporary screen for parts of the game that are not ported yet; any key returns.</summary>
    public async Task ShowNotPortedAsync(string what)
    {
        await Display.ClearViewportAsync(Graphics.Screen!, PaletteColours.Black);
        Graphics.DrawTextAt(DefaultText, 160, 80, what, TextContext.AlignCentre);
        Graphics.DrawTextAt(DefaultText, 160, 92, "is not ported yet.", TextContext.AlignCentre);
        Graphics.DrawTextAt(DefaultText, 160, 116, "Press any key.", TextContext.AlignCentre);
        await Display.PresentAsync();
        Events.FlushInputEvents();
        await Events.WaitForInputKeyAsync();
        await Display.ClearViewportAsync(Graphics.Screen!, PaletteColours.Black);
    }

    /// <summary>Writes the volume settings back (the original did it on every volume hotkey).</summary>
    public void SaveSettings()
    {
        Settings.MusicVolume = Volumes.MusicVolume;
        Settings.SfxVolume = Volumes.SfxVolume;
        Settings.KeyHelp = Preferences.KeyHelp;
        Settings.Save();
        Preferences.MusicVolume = Volumes.MusicVolume / 2;
        Preferences.SoundVolume = Volumes.SfxVolume / 2;
        if (_options.Display is { } display)
            Preferences.CaptureFrom(display);
        if (_options.Configuration is not { } configuration)
            return;
        Preferences.Write(configuration);
        try
        {
            configuration.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            (_options.Log ?? Console.Error.WriteLine)($"Settings not saved to {configuration.Path}: {e.Message}");
        }
    }
}
