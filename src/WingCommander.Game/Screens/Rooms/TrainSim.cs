using System.Globalization;
using WingCommander.Game.Flow;
using WingCommander.Game.Input;
using WingCommander.Game.Screens.Ui;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Graphics.Text;

namespace WingCommander.Game.Screens.Rooms;

/// <summary>
/// The TrainSim arcade with its menus. Outside the campaign start it shows the ranking
/// ("SQUADRON: TRAINSIM", high scores and the scrolling title until a key), the enemy
/// selection (four portraits; Esc cancels), then flies the chosen enemy and the following ones
/// (each preceded by "Get Ready"; a lost fight shows "Game Over", winning the last one
/// "Victory"), updates the ranking and shows it again. The forced first session of a new
/// campaign starts at enemy 2 with 4000 points and ends with the name and callsign entry.
/// </summary>
/// <remarks>C: RunTrainSim (0x427080, system.c); ShowTrainSimHighScores (0x4268E0),
/// DisplayTrainSimHighScoreTable (0x425C60), AnimateTrainSimTitle (0x425D00), SelectTrainSimMission
/// (0x426C70), LoadTrainSimOpponentShape (0x426C50), UpdateTrainSimHighScores (0x426820),
/// EnterPilotNameAndCallsign (0x426750), InitializeTrainSimTextPanel (0x426660),
/// ShowTrainSimTextMessage (0x426700), PromptForPilotField (0x426600), pilot.cpp;
/// UpdateTrainSimMenuCursor (0x42A610, hudmsg.c); AlignSpriteFrameToRectCorner (0x42E1D0, music.c).</remarks>
internal sealed class TrainSim
{
    /// <summary>Score of the forced first session (in tens) and the enemy it starts with.</summary>
    public const int StartupScore = 4000;
    public const short StartupMission = 2;

    /// <summary>Text colour of the simulator screens.</summary>
    /// <remarks>C: cDefaultTextColour = 0xA8.</remarks>
    private const byte DefaultTextColour = 0xa8;

    /// <summary>The console screen of the simulator art.</summary>
    /// <remarks>C: stTrainSimPanelBounds (0x00469DC0) = {0x30, 0x1D, 0x110, 0x6D}.</remarks>
    private const short PanelLeft = 0x30, PanelTop = 0x1d, PanelRight = 0x110, PanelBottom = 0x6d;

    /// <summary>First SHIPTYPE logical file minus 9: enemy n uses logical file n + 0x16.</summary>
    private const int OpponentFileBase = 0x16;

    /// <remarks>C: szTrainSimTitle.</remarks>
    private const string Title = "SQUADRON: TRAINSIM";

    /// <remarks>C: szNewPilotPrompt (0x00469E70).</remarks>
    private const string NewPilotPrompt = "CONGRATULATIONS!\nYOU HAVE A TOP SCORE!\nPLEASE ENTER YOUR\nNAME AND CALLSIGN:\n";

    /// <remarks>C: szDefaultPilotName, szPilotNameLabel, szDefaultCallsign, szCallsignLabel.</remarks>
    private const string DefaultPilotName = "Blair";
    private const string PilotNameLabel = "LAST NAME: ";
    private const string DefaultCallsign = "Maverick";
    private const string CallsignLabel = "CALLSIGN : ";

    /// <summary>Music tracks preloaded for the arcade flight.</summary>
    private static readonly int[] FlightTracks = [20, 21, 22];

    /// <summary>Size of szDefaultTextBuffer.</summary>
    private const int TextBufferSize = 0xc8;

    private readonly Wc1Game _game;
    private readonly GameFlowScreens _screens;
    private readonly TrainSimSession _session;
    private readonly byte[] _defaultTextBuffer = new byte[TextBufferSize];

    /// <remarks>C: stTrainSimTextContext.</remarks>
    private readonly TextContext _textContext = new();

    /// <remarks>C: stTrainSimHighScoreTextContext.</remarks>
    private readonly TextContext _highScoreContext = new();

    /// <summary>The title buffer of the ranking, later the console text panel (one global in the original).</summary>
    /// <remarks>C: stTrainSimPanelViewport.</remarks>
    private readonly Viewport _panel = new();

    /// <remarks>C: stTrainSimTitleDisplayViewport.</remarks>
    private readonly Viewport _titleDisplay = new();

    /// <remarks>C: stTrainSimHighScoreBufferViewport.</remarks>
    private readonly Viewport _highScoreBuffer = new();

    /// <remarks>C: stTrainSimHighScoreDisplayViewport.</remarks>
    private readonly Viewport _highScoreDisplay = new();

    /// <summary>Hit regions of the four enemy portraits.</summary>
    /// <remarks>C: aTrainSimMissionRegions (0x00469DF8).</remarks>
    private readonly MenuRegion[] _missionRegions =
    [
        new(1, 47, 29, 67, 49),
        new(1, 47, 89, 67, 109),
        new(1, 251, 29, 271, 49),
        new(1, 251, 89, 271, 109),
        new(MenuRegion.EndOfList, 0, 0, 0, 0),
    ];

    public TrainSim(Wc1Game game, GameFlowScreens screens, TrainSimSession session)
    {
        _game = game;
        _screens = screens;
        _session = session;
    }

    /// <remarks>C: RunTrainSim (0x427080, system.c).</remarks>
    public async Task RunAsync()
    {
        var game = _game;
        var events = game.Events;
        var campaignSession = game.Session;
        var sim = _session;
        bool proceed = true;
        sim.ArcadeWave = 0;
        sim.Mission = 0;
        events.InputMode = 1;
        events.Pump = null;
        sim.ArcadeScore = 0;
        sim.ArcadeBonusCountdown = 0;
        sim.CockpitView = 4;
        sim.CockpitLogicalFile = (sbyte)FallbackTrainSimFlight.SimulatorCockpitFile;

        if (!campaignSession.CampaignStartupMode)
        {
            await ShowTrainSimHighScoresAsync();
            (proceed, short mission) = await SelectTrainSimMissionAsync();
            sim.Mission = mission;
        }
        else
        {
            sim.ArcadeScore = StartupScore;
            sim.Mission = StartupMission;
        }

        if (proceed)
        {
            var flight = _screens.TrainSimFlight ?? game.FlightLayer as ITrainSimFlight ?? new FallbackTrainSimFlight(game);
            sim.Active = true;
            foreach (int track in FlightTracks)
                RoomSound.Preload(game, track);
            flight.BeginSession();
            short savedDataSet = campaignSession.CampaignDataSet;
            short savedCampaign = campaignSession.State.CampaignIndex;
            campaignSession.State.CampaignIndex = 0;
            campaignSession.CampaignDataSet = 0;
            var statusScreens = new TrainSimStatusScreens(game, flight);

            while (sim.Mission < 4)
            {
                sim.Active = true;
                flight.InitializeMission(sim.Mission);
                await statusScreens.ShowGetReadyScreenAsync();
                flight.PrepareFlight(campaignSession.CampaignStartupMode);
                await game.Display.PresentAsync();
                bool savedKeyEventQueue = events.KeyEventQueueEnabled;
                events.KeyEventQueueEnabled = true;
                var result = await _screens.FlyTrainSimMissionAsync(sim.Mission);
                if (result == FlightResult.Landed)
                {
                    if (sim.Mission < 3)
                        sim.ArcadeWave = 0;
                    else
                        await statusScreens.ShowVictoryScreenAsync();
                    sim.Mission++;
                }
                else
                {
                    await statusScreens.ShowGameOverScreenAsync();
                    sim.Mission = 4;
                }
                events.KeyEventQueueEnabled = savedKeyEventQueue;
            }

            campaignSession.State.CampaignIndex = savedCampaign;
            campaignSession.CampaignDataSet = savedDataSet;
            flight.EndSession();
            foreach (int track in FlightTracks)
                RoomSound.Release(game, track);
            await UpdateTrainSimHighScoresAsync(sim.ArcadeScore);
            await ShowTrainSimHighScoresAsync();
        }
        sim.Active = false;
    }

    /// <summary>
    /// The ranking on the simulator console: the table for 12 s, then the title scrolling up
    /// from the bottom of the console, repeated until a key, a button or Esc.
    /// </summary>
    /// <remarks>C: ShowTrainSimHighScores (0x4268E0, pilot.cpp). The joystick button pump is not ported.</remarks>
    private async Task ShowTrainSimHighScoresAsync()
    {
        var game = _game;
        var gfx = game.Graphics;
        var screen = gfx.Screen!;
        game.Events.Pump = null;
        await game.Display.ClearViewportAsync(screen, PaletteColours.Black);
        var backdrop = game.Resources.GetShape(_session.CockpitLogicalFile, 0);
        gfx.DrawSpriteDefault(screen, 0, 0, backdrop, 0);
        gfx.InitializeTextContextFromFont(_textContext, 1, DefaultTextColour, PaletteColours.Black);
        gfx.SetTextContext(_textContext);
        _panel.SetViewportRect(0, 0, 319, 199);
        _textContext.TextBuffer = _defaultTextBuffer;
        ModalTextPanel.ResetStringBuilder(_textContext);
        short titleWidth = (short)((gfx.MeasureTextPixelWidthClamped(UiText.ToBytes(Title)) & 0xfff8) + 8);
        _panel.Right = titleWidth;
        short fontHeight = _textContext.Font!.Height;
        _panel.Bottom = (short)(fontHeight + 2);
        _panel.AllocateViewport(PaletteColours.Black);
        _textContext.Viewport = _panel;
        ModalTextPanel.EraseTextContextBackground(gfx, _textContext);
        gfx.SetTextCursor(0, 1);
        gfx.DrawFormattedText(Title);
        short lineHeight = fontHeight;

        _titleDisplay.CopyFrom(screen);
        _titleDisplay.SetViewportRect(PanelLeft, PanelTop, PanelRight, PanelBottom);
        gfx.ClearViewport(_titleDisplay, PaletteColours.Black);
        short titleLeft = (short)((160 - titleWidth / 2) & 0xfffe);
        _titleDisplay.Left = titleLeft;
        _titleDisplay.Right = (short)(titleLeft + titleWidth);
        _titleDisplay.Top = (short)(game.Random.InRange(0, 0x4e) + 0x1d);
        _titleDisplay.Bottom = (short)(_titleDisplay.Top + lineHeight + 2);
        if (_titleDisplay.Bottom > 109)
            _titleDisplay.Bottom = 109;

        gfx.InitializeTextContextFromFont(_highScoreContext, 1, DefaultTextColour, PaletteColours.Black);
        _highScoreBuffer.SetViewportRect(PanelLeft, PanelTop, PanelRight, PanelBottom);
        _highScoreBuffer.AllocateViewport(PaletteColours.Black);
        _highScoreDisplay.CopyFrom(screen);
        lineHeight += 3;
        _highScoreContext.Viewport = _highScoreBuffer;
        _highScoreContext.TextBuffer = _defaultTextBuffer;
        _highScoreDisplay.SetViewportRect(PanelLeft, PanelTop, PanelRight, PanelBottom);
        gfx.SetTextContext(_highScoreContext);
        ModalTextPanel.ResetStringBuilder(_highScoreContext);
        ModalTextPanel.EraseTextContextBackground(gfx, _highScoreContext);
        gfx.SetTextCursor(_highScoreBuffer.Left, _highScoreBuffer.Top + 1);
        gfx.FormatTextBufferFromStart("%JHIGH SCORES%P"u8, 2);
        _highScoreContext.Alignment = 0;
        var scores = game.Session.HighScores;
        for (int row = 0; row < 6; row++)
        {
            if (scores.Entries[row].PilotIndex == -1)
                continue;
            ModalTextPanel.ResetStringBuilder(_highScoreContext);
            string score = unchecked((int)scores.Entries[row].Score).ToString(CultureInfo.InvariantCulture);
            gfx.DrawFormattedText("%X%Y%d. %s%X%s0"u8,
                _highScoreBuffer.Left + 10, lineHeight * (row + 1) + _highScoreBuffer.Top + 1,
                row + 1, scores.GetName(row, game.Session.Pilots),
                _highScoreBuffer.Left + 150, score);
        }
        game.Events.FlushInputEvents();
        await game.Display.PresentAsync();
        while (await DisplayTrainSimHighScoreTableAsync() && await AnimateTrainSimTitleAsync())
        {
        }
        gfx.ReleaseTextFont(1);
        _panel.FreeViewport();
        _highScoreBuffer.FreeViewport();
    }

    /// <summary>Shows the ranking for 720 ticks; false when a key or button cut it short.</summary>
    /// <remarks>C: DisplayTrainSimHighScoreTable (0x425C60, pilot.cpp).</remarks>
    private async Task<bool> DisplayTrainSimHighScoreTableAsync()
    {
        var game = _game;
        var gfx = game.Graphics;
        bool completed = true;
        gfx.DrawFilledViewportRect(ModalSourcePage, PanelLeft, PanelTop, PanelRight, PanelBottom, PaletteColours.Black);
        _highScoreDisplay.SetViewportRect(PanelLeft, PanelTop, PanelRight, PanelBottom);
        gfx.CopyViewportContents(_highScoreBuffer, _highScoreDisplay);
        game.Timing.SetFrameTimerPeriod(0x2d0);
        while (!game.Timing.IsFrameTickElapsed())
        {
            await game.Display.PresentAsync();
            if (game.Events.CheckEscaped() != 0)
            {
                completed = false;
                break;
            }
        }
        return completed;
    }

    /// <summary>Scrolls the title up the empty console one row per frame; false when a key or button interrupts it.</summary>
    /// <remarks>C: AnimateTrainSimTitle (0x425D00, pilot.cpp).</remarks>
    private async Task<bool> AnimateTrainSimTitleAsync()
    {
        var game = _game;
        var gfx = game.Graphics;
        bool completed = false;
        short y = 0x6b;
        gfx.DrawFilledViewportRect(ModalSourcePage, PanelLeft, PanelTop, PanelRight, PanelBottom, PaletteColours.Black);
        await game.Display.PresentAsync();
        short fontHeight = _textContext.Font!.Height;
        y = (short)(y - fontHeight);
        _titleDisplay.Top = y;
        _titleDisplay.Bottom = (short)(y + fontHeight + 2);
        if (game.Events.CheckEscaped() == 0)
        {
            while (true)
            {
                game.Timing.SetFrameTimerPeriod(3);
                if (_titleDisplay.Top <= PanelTop)
                    break;
                _titleDisplay.Top--;
                _titleDisplay.Bottom--;
                gfx.CopyViewportContents(_panel, _titleDisplay);
                await game.Display.PresentAsync();
                await game.Timing.WaitForFrameTickAsync();
                if (game.Events.CheckEscaped() != 0)
                    return completed;
            }
            completed = true;
        }
        return completed;
    }

    /// <summary>"SELECT ENEMY" with the four enemy portraits in the console corners; returns (false, -1) after Esc.</summary>
    /// <remarks>C: SelectTrainSimMission (0x426C70, pilot.cpp).</remarks>
    private async Task<(bool Proceed, short Mission)> SelectTrainSimMissionAsync()
    {
        var game = _game;
        var gfx = game.Graphics;
        var events = game.Events;
        bool cancelled = false;
        sbyte selection = 0;
        gfx.SetTextContext(_textContext);
        gfx.InitializeTextContextFromFont(_textContext, 1, DefaultTextColour, PaletteColours.Black);
        _titleDisplay.SetViewportRect(PanelLeft, PanelTop, PanelRight, PanelBottom);
        _textContext.Viewport = _titleDisplay;
        ModalTextPanel.EraseTextContextBackground(gfx, _textContext);
        gfx.SetTextCursor(_titleDisplay.Left, _titleDisplay.Top + 30);
        _textContext.Alignment = TextContext.AlignCentre;
        gfx.FormatTextBufferFromStart("SELECT\nENEMY%P"u8);

        var menuViewport = gfx.Screen!.Clone();
        menuViewport.SetViewportRect(PanelLeft, PanelTop, PanelRight, PanelBottom);
        var topLeftShape = LoadTrainSimOpponentShape(9);
        var bottomLeftShape = LoadTrainSimOpponentShape(10);
        var topRightShape = LoadTrainSimOpponentShape(11);
        var bottomRightShape = LoadTrainSimOpponentShape(12);
        ShapeTable[] shapes = [topLeftShape, bottomLeftShape, topRightShape, bottomRightShape];
        ReadOnlySpan<int> corners = [0, 2, 1, 3];
        var positions = new (short X, short Y)[4];
        Span<short> bounds = stackalloc short[4];
        for (int i = 0; i < 4; i++)
        {
            positions[i] = AlignSpriteFrameToRectCorner(corners[i], shapes[i], 0);
            ref var region = ref _missionRegions[i];
            bounds[0] = region.Left;
            bounds[1] = region.Top;
            bounds[2] = region.Right;
            bounds[3] = region.Bottom;
            ShapeBounds.GetShapeFrameBounds(bounds, positions[i].X, positions[i].Y, shapes[i], 0);
            region.SetRect(bounds);
        }
        for (int i = 0; i < 4; i++)
        {
            gfx.DrawSpriteDefault(menuViewport, positions[i].X, positions[i].Y, shapes[i], 0);
            gfx.DrawSpriteDefault(menuViewport, positions[i].X, positions[i].Y, shapes[i], 2);
        }

        game.Cursor.Viewport = _titleDisplay;
        events.Pump = null;
        events.MenuInputRepeatDelay = 6;
        events.WarpMouseTo(160, 100);
        events.ShowCursor();
        byte savedInputMode = events.InputMode;
        events.InputMode = 1;
        var e = new InputEventState();
        do
        {
            if (events.EscapePressed)
                break;
            bool select = false;
            switch (events.PollInputEvent(ref e))
            {
                case InputEventType.ButtonDown:
                    select = true;
                    break;
                case InputEventType.KeyDown:
                case InputEventType.Character:
                {
                    events.ClearInputKeyStatePreservingModifiers();
                    short key = unchecked((short)e.Value);
                    if (key is 0x1c or 0x39)
                        select = true;
                    else
                        events.MoveMenuPointerFromKeyboard(e);
                    break;
                }
                case InputEventType.MouseMove:
                    UpdateTrainSimMenuCursor();
                    break;
            }
            if (select)
            {
                int region = MenuRegion.FindMenuRegionAtPoint(_missionRegions, e.X, e.Y);
                if (region is >= 0 and < 4)
                    selection = (sbyte)(region + 1);
            }
            await game.Display.PresentAsync();
        }
        while (selection == 0);

        if (events.EscapePressed)
            cancelled = true;
        events.InputMode = savedInputMode;
        events.Pump = null;
        events.HideCursor();
        gfx.ReleaseTextFont(1);
        return (!cancelled, (short)(selection - 1));
    }

    /// <summary>Cursor frame 1 over an enemy portrait, else 0.</summary>
    /// <remarks>C: UpdateTrainSimMenuCursor (0x42A610, hudmsg.c).</remarks>
    private void UpdateTrainSimMenuCursor()
    {
        short frame = 0;
        short x = _game.Events.Cursor.X;
        short y = _game.Events.Cursor.Y;
        foreach (var region in _missionRegions)
        {
            if (region.Frame == MenuRegion.EndOfList)
                break;
            if (region.Contains(x, y))
                frame = region.Frame;
        }
        _game.Cursor.SetFrame(frame);
    }

    /// <summary>Portrait of an enemy (SHIPTYPE section 1 of logical file opponent + 0x16).</summary>
    /// <remarks>C: LoadTrainSimOpponentShape (0x426C50, pilot.cpp); its cObjectResourceLogicalFile
    /// side effect is overwritten by the mission set-up before it matters.</remarks>
    private ShapeTable LoadTrainSimOpponentShape(int opponent) =>
        _game.Resources.GetShape(opponent + OpponentFileBase, 1);

    /// <summary>Hot spot that puts the frame's corner on the console corner (0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right).</summary>
    /// <remarks>C: AlignSpriteFrameToRectCorner (0x42E1D0, music.c).</remarks>
    private static (short X, short Y) AlignSpriteFrameToRectCorner(int corner, ShapeTable shape, int frame)
    {
        short x = corner is 0 or 2 ? PanelLeft : PanelRight;
        short y = corner is 0 or 1 ? PanelTop : PanelBottom;
        Span<short> frameBounds = stackalloc short[4];
        frameBounds.Clear();
        ShapeBounds.GetShapeFrameBounds(frameBounds, x, y, shape, frame);
        short px = corner is 0 or 2 ? (short)(x * 2 - frameBounds[0]) : (short)(x * 2 - frameBounds[2]);
        short py = corner is 0 or 1 ? (short)(y * 2 - frameBounds[1]) : (short)(y * 2 - frameBounds[3]);
        return (px, py);
    }

    /// <summary>
    /// Enters the player's score in the ranking when it beats the player's last one. In the
    /// campaign start the player then enters name and callsign; otherwise the console shows the
    /// rank or "YOUR SCORE IS ONLY ..." until a key.
    /// </summary>
    /// <remarks>C: UpdateTrainSimHighScores (0x426820, pilot.cpp).</remarks>
    private async Task UpdateTrainSimHighScoresAsync(int score)
    {
        var game = _game;
        var scores = game.Session.HighScores;
        int slot = scores.Find(8);
        uint previousScore = slot == -1 ? scores.Entries[5].Score : scores.Entries[slot].Score;
        slot = -1;
        if (score > unchecked((int)previousScore))
            slot = scores.Insert(8, unchecked((uint)score));

        if (game.Session.CampaignStartupMode)
        {
            await EnterPilotNameAndCallsignAsync();
            return;
        }
        InitializeTrainSimTextPanel();
        string message = slot != -1
            ? $"*******\nCONGRATULATIONS!\nYOU HAVE SCORE NUMBER\n>>>> {slot + 1} <<<<\n*******"
            : $"> SORRY <\n\nYOUR SCORE IS ONLY\n{score}0\n\nPLEASE PLAY AGAIN!";
        await ShowTrainSimTextMessageAsync(message);
        game.Events.Pump = null;
        await game.Display.PresentAsync();
        await game.Events.WaitForInputKeyAsync();
        game.Events.Pump = null;
    }

    /// <summary>The new pilot types the last name and the callsign (defaults "Blair" and "Maverick").</summary>
    /// <remarks>C: EnterPilotNameAndCallsign (0x426750, pilot.cpp). With the developer unlock the
    /// callsign becomes CHEATER.</remarks>
    private async Task EnterPilotNameAndCallsignAsync()
    {
        var game = _game;
        var gfx = game.Graphics;
        await game.Display.ClearViewportAsync(gfx.Screen!, PaletteColours.Black);
        var backdrop = game.Resources.GetShape(_session.CockpitLogicalFile, 0);
        gfx.DrawSpriteDefault(gfx.Screen!, 0, 0, backdrop, 0);
        InitializeTrainSimTextPanel();
        await ShowTrainSimTextMessageAsync(NewPilotPrompt);
        var player = game.Session.Player;
        await PromptForPilotFieldAsync(10, (short)(_textContext.CursorY + 2), PilotNameLabel, 13, DefaultPilotName,
            text => player.Name = text);
        await PromptForPilotFieldAsync(10, (short)(_textContext.CursorY + 10), CallsignLabel, 13, DefaultCallsign,
            text => player.Callsign = text);
        if (game.Options.OriginDevUnlock)
            player.Callsign = "CHEATER";
    }

    /// <summary>Prints the label and edits the field until a non-empty text is accepted (the default is restored before every try).</summary>
    /// <remarks>C: PromptForPilotField (0x426600, pilot.cpp).</remarks>
    private async Task PromptForPilotFieldAsync(short x, short y, string label, int maximumLength, string defaultText,
        Action<string> store)
    {
        var gfx = _game.Graphics;
        _textContext.Alignment = 0;
        gfx.SetTextCursor(unchecked((ushort)(_panel.Left + x)), unchecked((ushort)y));
        gfx.DrawFormattedText(label);
        string? text;
        do
        {
            store(defaultText);
            text = await TextInput.ReadTextInputAsync(_game, defaultText, maximumLength, TextInput.ModeAnyCase);
        }
        while (text is null);
        store(text);
    }

    /// <summary>Clears the console screen and makes the simulator text context (font 1, colour 0xA8 on black) current.</summary>
    /// <remarks>C: InitializeTrainSimTextPanel (0x426660, pilot.cpp).</remarks>
    private void InitializeTrainSimTextPanel()
    {
        var gfx = _game.Graphics;
        _panel.CopyFrom(gfx.Screen!);
        _panel.SetViewportRect(PanelLeft, PanelTop, PanelRight, PanelBottom);
        gfx.ClearViewport(_panel, PaletteColours.Black);
        _textContext.TextBuffer = _defaultTextBuffer;
        ModalTextPanel.ResetStringBuilder(_textContext);
        gfx.SetTextContext(_textContext);
        gfx.InitializeTextContextFromFont(_textContext, 1, DefaultTextColour, PaletteColours.Black);
        _textContext.Viewport = _panel;
        ModalTextPanel.EraseTextContextBackground(gfx, _textContext);
    }

    /// <summary>Prints a message centred from the top of the console panel and presents.</summary>
    /// <remarks>C: ShowTrainSimTextMessage (0x426700, pilot.cpp).</remarks>
    private async Task ShowTrainSimTextMessageAsync(string message)
    {
        var gfx = _game.Graphics;
        gfx.SetTextCursor(_panel.Left, _panel.Top + 2);
        _textContext.Alignment = TextContext.AlignCentre;
        gfx.FormatTextBufferFromStart(UiText.ToBytes(message));
        gfx.FormatTextBufferFromStart("%P"u8);
        await _game.Display.PresentAsync();
    }

    /// <summary>The modal source page (stModalSourceViewport).</summary>
    private Viewport ModalSourcePage => _game.DefaultText.Viewport ?? _game.Graphics.Screen!;
}
