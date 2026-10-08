using WingCommander.Core.Platform;
using WingCommander.Core.Resources;
using WingCommander.Game.Campaign;
using WingCommander.Game.Flow;
using WingCommander.Game.Input;
using WingCommander.Game.Screens.Ui;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Graphics.Text;

namespace WingCommander.Game.Screens.Rooms;

/// <summary>
/// The barracks: eight bunks (= the eight slots of SAVEGAME.WLD; a sleeper lies in every used
/// bunk), the mission hangar door, the way back to the bar, the airlock (quit) and the medal
/// locker. The left half of a used bunk wakes that game (load), the other half or an empty
/// bunk saves the current campaign into it. Animated: snoring sleepers (status lights), a drop
/// that falls from the ceiling into the bucket every 20 animation ticks, a flickering ceiling light.
/// </summary>
/// <remarks>C: BarracksScreen (0x41C170) and its helpers in barracks.c: InitializeBarracksAnimation
/// (0x41B070), FreeBarracksMenuLabel(s) (0x41B0E0/0x41B180), SetAwakenBarracksMenuLabel (0x41B110),
/// SaveGame (0x41B1E0), WarnLoadGameFirst (0x41B550), SaveGameWithNamePrompt (0x41B5C0), LoadGame
/// (0x41B710), LoadGameFromSlot (0x41B980), SetBunkMenuLabel (0x41BAD0), GetBunkInfo (0x41BB20),
/// DrawBarracksBunks (0x41BBD0), DrawBarracksStaticDetails (0x41BC90), AnimateBarracks (0x41BCE0),
/// ConfirmQuitWingCommander (0x41BF10), ConfirmAwakenAfterBadData (0x41BF60),
/// ConfirmReplaceFaultyData (0x41BFE0), HandleBarracksBunkSelection (0x41C090),
/// UpdateBarracksScreen (0x41C140).</remarks>
internal sealed class Barracks
{
    /// <summary>RECROOM.VGA section of the barracks.</summary>
    public const int BackgroundSection = 12;

    /// <summary>Music track of the barracks.</summary>
    private const int MusicTrack = 35;

    /// <summary>Sound of the drop hitting the bucket.</summary>
    private const int ImpactSound = 35;

    /// <summary>First region that is not a bunk half.</summary>
    private const int MissionHangarRegion = 16;
    private const int ReturnToBarRegion = 17;
    private const int QuitRegion = 18;
    private const int ViewMedalsRegion = 19;

    /// <summary>Frame of the ceiling light at (45, 0) while it is on (the C state calls it eyesOpen); also the "no splash" marker.</summary>
    private const short EyesOpenFrame = 49;
    private const short EyesClosedFrame = 26;
    private const short LabelStripFrame = 50;
    private const int FallingIdle = -99;

    /// <summary>"Save this campaign  " (two distinct label objects, as the original had two literals).</summary>
    /// <remarks>C: apszSaveCampaignMenuLabels (0x004693E8): szSaveCampaignMenuLabel, szSaveCampaignMenuLabelAlt.</remarks>
    public static readonly string SaveCampaignLabel = new("Save this campaign  ".AsSpan());

    /// <remarks>C: szSaveCampaignMenuLabelAlt (0x00469480).</remarks>
    public static readonly string SaveCampaignLabelAlt = new("Save this campaign  ".AsSpan());

    /// <remarks>C: szMissionHangarMenuLabel, szReturnToBarMenuLabel, szQuitGameMenuLabel, szViewMedalsMenuLabel.</remarks>
    private const string MissionHangarLabel = "Mission Hangar";
    private const string ReturnToBarLabel = "Return to the Bar";
    private const string QuitGameLabel = "Quit Wing Commander";
    private const string ViewMedalsLabel = "View your medals";

    /// <summary>Text the original passed to exit_squadron when the player quits.</summary>
    public const string QuitMessage = "You step out of the airlock and into...";

    /// <remarks>C: aBarracksBunkOrigins (0x004693C8).</remarks>
    private static readonly (short X, short Y)[] BunkOrigins =
        [(109, 86), (170, 86), (98, 95), (173, 95), (78, 110), (176, 110), (42, 136), (183, 136)];

    private readonly Wc1Game _game;
    private readonly RoomsState _rooms;
    private readonly string?[] _labels = new string?[20];
    private readonly BarracksAnimation _animation = new();
    private RoomMenu? _menu;
    private RoomViewports? _viewports;
    private ShapeTable? _background;

    public Barracks(Wc1Game game, RoomsState rooms)
    {
        _game = game;
        _rooms = rooms;
        _labels[MissionHangarRegion] = MissionHangarLabel;
        _labels[ReturnToBarRegion] = ReturnToBarLabel;
        _labels[QuitRegion] = QuitGameLabel;
        _labels[ViewMedalsRegion] = ViewMedalsLabel;
    }

    /// <summary>Hit regions: 0..15 bunk halves (slot = region / 2; even = wake, odd = save), 16 hangar, 17 bar, 18 airlock, 19 medals.</summary>
    /// <remarks>C: aBarracksMenuRegions (0x00463008).</remarks>
    public static MenuRegion[] CreateRegions() =>
    [
        new(1, 137, 88, 149, 94),
        new(1, 110, 88, 136, 94),
        new(1, 172, 88, 184, 94),
        new(1, 185, 88, 210, 94),
        new(1, 133, 98, 146, 107),
        new(1, 100, 98, 132, 107),
        new(1, 174, 98, 189, 107),
        new(1, 190, 98, 220, 107),
        new(1, 124, 114, 142, 128),
        new(1, 81, 114, 123, 128),
        new(1, 178, 114, 197, 128),
        new(1, 198, 114, 238, 128),
        new(1, 109, 141, 135, 164),
        new(1, 50, 141, 108, 164),
        new(1, 185, 141, 213, 164),
        new(1, 214, 141, 268, 164),
        new(1, 288, 39, 311, 85),
        new(1, 9, 33, 39, 95),
        new(1, 218, 37, 248, 78),
        new(1, 86, 44, 181, 78),
        new(MenuRegion.EndOfList, 0, 0, 0, 0),
    ];

    private string SavePath => _game.SaveGamePath;

    /// <summary>The bunk labels (0..15) and the four door labels (16..19).</summary>
    public IReadOnlyList<string?> Labels => _labels;

    /// <summary>Runs the barracks until the hangar (7) or the bar (8) is chosen; quitting throws <see cref="GameExitException"/>.</summary>
    /// <remarks>C: BarracksScreen (0x41C170, barracks.c).</remarks>
    public async Task<int> RunAsync()
    {
        var game = _game;
        var gfx = game.Graphics;
        var events = game.Events;
        var display = game.Display;
        var timing = game.Timing;
        var session = game.Session;

        short result = 0;
        int lastMedalsTick = 0;
        RoomSound.StartTrack(game, MusicTrack);
        var viewports = RoomViewports.Initialize(game);
        _viewports = viewports;
        _background = game.Resources.GetShape(LogicalFile.RecRoomVga, BackgroundSection);
        var menu = new RoomMenu(game, CreateRegions(), _labels, viewports.Screen, TextContext.AlignCentre);
        _menu = menu;
        SaveGameFile.Ensure(SavePath);
        InitializeBarracksAnimation();
        GetBunkInfo();
        DrawBarracksBunks(viewports.SceneBuffer);
        game.Cursor.Viewport = viewports.Screen;
        events.WarpMouseTo(160, 100);
        events.ShowCursor();
        timing.SetFrameTimerPeriod(0);
        events.FlushInputEvents();
        viewports.SavedMenuInputRepeatDelay = events.MenuInputRepeatDelay;
        events.InputMode = 1;
        // nMenuPointerSpeed = 1 only scales the joystick pointer (not ported).
        events.MenuInputRepeatDelay = 2;

        var e = new InputEventState();
        while (result == 0)
        {
            if (timing.IsFrameTickElapsed())
            {
                UpdateBarracksScreen(viewports.Screen);
                timing.SetFrameTimerPeriod(2);
            }

            short eventType = events.PollInputEvent(ref e);
            bool clicked = false;
            if (eventType is InputEventType.KeyDown or InputEventType.Character)
            {
                events.ClearInputKeyStatePreservingModifiers();
                short key = unchecked((short)e.Value);
                if (key is 0x1c or 0x39)
                    clicked = true;
                else if (key == 0x24)
                {
                    // J: CalibrateJoystickInteractive (joystick support is not ported).
                }
                else
                    events.MoveMenuPointerFromKeyboard(e);
            }
            else if (eventType is InputEventType.ButtonDown or InputEventType.JoystickButton)
            {
                clicked = true;
            }
            else if (eventType == InputEventType.MouseMove)
            {
                menu.UpdateCursor();
            }

            if (clicked)
            {
                int region = menu.FindRegionAtPoint(e.X, e.Y);
                if (region is >= 0 and < MissionHangarRegion)
                {
                    await HandleBarracksBunkSelectionAsync(viewports.SceneBuffer, region);
                }
                else if (region == MissionHangarRegion)
                {
                    if (!session.CampaignActive)
                        await WarnLoadGameFirstAsync();
                    else
                        result = BarracksResult.LaunchMission;
                }
                else if (region == ReturnToBarRegion)
                {
                    if (!session.CampaignActive)
                        await WarnLoadGameFirstAsync();
                    else
                        result = BarracksResult.ReturnToBar;
                }
                else if (region == QuitRegion)
                {
                    if (await ConfirmQuitWingCommanderAsync())
                    {
                        events.Shutdown();
                        throw new GameExitException(QuitMessage);
                    }
                }
                else if (region == ViewMedalsRegion)
                {
                    if (!session.CampaignActive)
                    {
                        await WarnLoadGameFirstAsync();
                    }
                    else
                    {
                        LoadMissionData(session.State.CurrentSeries, session.State.CurrentMission);
                        events.FlushInputEvents();
                        if (unchecked((int)(timing.Ticks60Hz - (uint)lastMedalsTick)) > events.InputTickScale)
                        {
                            events.HideCursor();
                            gfx.ClearViewport(viewports.Screen, PaletteColours.Black);
                            var screen = gfx.Screen!;
                            viewports.SceneBuffer.Bottom = 127;
                            screen.Top = 24;
                            screen.Bottom = 151;
                            await MedalsView.ShowAsync(game, viewports.SceneBuffer, _rooms.SystemName);
                            lastMedalsTick = unchecked((int)timing.Ticks60Hz);
                            gfx.ClearViewport(viewports.Screen, PaletteColours.Black);
                            screen.Top = 0;
                            screen.Bottom = 199;
                            viewports.SceneBuffer.Bottom = 199;
                            DrawBarracksBunks(viewports.SceneBuffer);
                            events.ShowCursor();
                            UpdateBarracksScreen(viewports.Screen);
                        }
                    }
                }
            }
            await display.PresentAsync();
        }

        events.HideCursor();
        events.MenuInputRepeatDelay = viewports.SavedMenuInputRepeatDelay;
        FreeBarracksMenuLabels();
        gfx.ReleaseTextFont(0);
        viewports.Free();
        RoomSound.StopTrack(game, MusicTrack);
        return result;
    }

    /// <summary>Bunk sleepers get a random snore phase and period; the first drop falls after 20 ticks.</summary>
    /// <remarks>C: InitializeBarracksAnimation (0x41B070).</remarks>
    private void InitializeBarracksAnimation()
    {
        var random = _game.Random;
        var state = _animation;
        for (int bunk = 0; bunk < 8; bunk++)
        {
            state.Bunks[bunk].AnimationFrame = random.InRange(0, 13);
            state.Bunks[bunk].AnimationPeriod = (short)(random.InRange(0, 12) + 13);
            state.Bunks[bunk].AnimationTick = 0;
        }
        state.FallingY = FallingIdle;
        state.FallingDelay = 20;
        state.ImpactFrame = EyesOpenFrame;
        state.BlinkDelay = 0;
        state.FallingVelocity = 0;
        state.AnimationTick = 0;
        state.MenuLabel = null;
        state.EyesOpen = 1;
    }

    /// <summary>Drops the bunk labels and the current label.</summary>
    /// <remarks>C: FreeBarracksMenuLabels (0x41B180).</remarks>
    private void FreeBarracksMenuLabels()
    {
        for (int bunk = 0; bunk < 8; bunk++)
        {
            if (!ReferenceEquals(_labels[bunk * 2], SaveCampaignLabel))
            {
                FreeBarracksMenuLabel(bunk * 2);
                FreeBarracksMenuLabel(bunk * 2 + 1);
            }
        }
        if (_menu is not null)
            _menu.CurrentLabel = null;
    }

    /// <remarks>C: FreeBarracksMenuLabel (0x41B0E0): only the allocated "Awaken" labels are freed.</remarks>
    private void FreeBarracksMenuLabel(int index)
    {
        string? label = _labels[index];
        if (label is not null && !ReferenceEquals(label, SaveCampaignLabel) && !ReferenceEquals(label, SaveCampaignLabelAlt))
            _labels[index] = null;
    }

    /// <summary>Reads every slot: occupancy and the two labels of each bunk.</summary>
    /// <remarks>C: GetBunkInfo (0x41BB20) with SetBunkMenuLabel (0x41BAD0) and SetAwakenBarracksMenuLabel (0x41B110).</remarks>
    private void GetBunkInfo()
    {
        FreeBarracksMenuLabels();
        for (int bunk = 0; bunk < 8; bunk++)
        {
            var record = LoadGame(bunk);
            short occupied = (short)(record is not null ? 1 : 0);
            _animation.Bunks[bunk].Occupied = occupied;
            _labels[bunk * 2] = occupied == 0 ? SaveCampaignLabel : $"Awaken {record!.Description}.";
            _labels[bunk * 2 + 1] = occupied == 0 ? SaveCampaignLabel : SaveCampaignLabelAlt;
        }
    }

    /// <summary>The slot when SAVEGAME.WLD could be read and the slot is used, else null.</summary>
    /// <remarks>C: LoadGame (0x41B710).</remarks>
    private SaveGameSlot? LoadGame(int slot)
    {
        try
        {
            return SaveGameFile.Load(SavePath, slot);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Draws the room and the occupied bunks into the scene buffer, then copies it to the screen.</summary>
    /// <remarks>C: DrawBarracksBunks (0x41BBD0).</remarks>
    private void DrawBarracksBunks(Viewport viewport)
    {
        var gfx = _game.Graphics;
        var shape = _background;
        gfx.DrawSpriteDefault(viewport, 0, 0, shape, 0);
        for (int bunk = 0; bunk < 8; bunk++)
        {
            short frame = 10;
            if (_animation.Bunks[bunk].Occupied != 0)
            {
                gfx.DrawSpriteDefault(viewport, BunkOrigins[bunk].X, BunkOrigins[bunk].Y, shape, bunk + 1);
                frame = 9;
            }
            gfx.DrawSpriteDefault(viewport, bunk % 2 * 31 + 143, bunk / 2 * 5 + 167, shape, frame);
        }
        gfx.CopyViewportContents(_viewports!.SceneBuffer, _viewports.Screen);
    }

    /// <remarks>C: DrawBarracksStaticDetails (0x41BC90); CheckCursor is empty.</remarks>
    private void DrawBarracksStaticDetails(Viewport viewport)
    {
        var gfx = _game.Graphics;
        gfx.DrawSpriteDefault(viewport, 147, 167, _background, 25);
        gfx.DrawSpriteDefault(viewport, 304, 144, _background, 36);
    }

    /// <summary>Status lights of the bunks, the falling drop and its splash, the flickering light, the label strip.</summary>
    /// <remarks>C: AnimateBarracks (0x41BCE0).</remarks>
    private void AnimateBarracks(Viewport viewport)
    {
        var gfx = _game.Graphics;
        var random = _game.Random;
        var shape = _background;
        var state = _animation;
        int frameTick = unchecked((int)_game.Timing.Ticks60Hz) / 3;
        short frame;
        for (int bunk = 0; bunk < 8; bunk++)
        {
            frame = 11;
            ref var bunkState = ref state.Bunks[bunk];
            if (bunkState.Occupied != 0)
            {
                int bunkTick = frameTick / 4;
                if (bunkState.AnimationTick != bunkTick)
                {
                    bunkState.AnimationTick = bunkTick;
                    bunkState.AnimationFrame++;
                    if (bunkState.AnimationTick % bunkState.AnimationPeriod == 0)
                        bunkState.AnimationFrame = 0;
                }
                frame = 24;
                if (bunkState.AnimationFrame < 13)
                    frame = (short)(24 - bunkState.AnimationFrame);
            }
            gfx.DrawSpriteDefault(viewport, bunk % 2 * 14 + 148, bunk / 2 * 5 + 166, shape, frame);
        }

        if (state.AnimationTick != frameTick)
        {
            state.AnimationTick = unchecked((short)frameTick);
            state.FallingDelay--;
            if (state.FallingDelay == 0)
            {
                state.FallingY = -5;
                state.FallingVelocity = 3;
            }
            if (state.FallingY != FallingIdle)
            {
                state.FallingY += state.FallingVelocity;
                state.FallingVelocity++;
                if (state.FallingY > 115)
                {
                    state.FallingY = FallingIdle;
                    state.ImpactFrame = 37;
                    state.FallingDelay = 20;
                    RoomSound.PlayInterfaceSound(_game, ImpactSound);
                }
            }
        }
        if (state.FallingY != FallingIdle)
        {
            gfx.DrawSpriteDefault(viewport, 298, state.FallingY, shape, state.FallingY / 40 + 27);
            gfx.DrawSpriteDefault(viewport, 305, 46, shape, 35);
        }
        frame = state.ImpactFrame;
        if (frame != EyesOpenFrame)
        {
            state.ImpactFrame++;
            gfx.DrawSpriteDefault(viewport, 298, 139, shape, frame);
        }

        if (state.BlinkDelay != 0)
        {
            state.EyesOpen = (short)(state.EyesOpen == 0 ? 1 : 0);
            state.BlinkDelay--;
            if (state.BlinkDelay == 0 && unchecked((ushort)random.InRange(0, 100)) < 90)
                state.EyesOpen = 1;
        }
        else if (random.InRange(0, 70) == 0)
        {
            state.BlinkDelay = (short)(random.InRange(0, 15) + 2);
        }
        frame = state.EyesOpen != 0 ? EyesOpenFrame : EyesClosedFrame;
        gfx.DrawSpriteDefault(viewport, 45, 0, shape, frame);
        if (!ReferenceEquals(state.MenuLabel, _menu!.CurrentLabel))
        {
            state.MenuLabel = _menu.CurrentLabel;
            gfx.DrawSpriteDefault(viewport, 319, 199, shape, LabelStripFrame);
        }
    }

    /// <remarks>C: UpdateBarracksScreen (0x41C140).</remarks>
    private void UpdateBarracksScreen(Viewport viewport)
    {
        DrawBarracksStaticDetails(viewport);
        AnimateBarracks(viewport);
        _menu!.RefreshLabel();
    }

    /// <summary>A bunk half was chosen: wake (load) a used bunk, or save into an empty one or over a used one.</summary>
    /// <remarks>C: HandleBarracksBunkSelection (0x41C090).</remarks>
    private async Task HandleBarracksBunkSelectionAsync(Viewport viewport, int region)
    {
        var events = _game.Events;
        events.HideCursor();
        int slot = region / 2;
        bool save = true;
        if (_animation.Bunks[slot].Occupied != 0)
        {
            if (region % 2 == 0)
            {
                if (await ConfirmAwakenAfterBadDataAsync(slot))
                    await LoadGameFromSlotAsync(slot);
                save = false;
            }
            else if (!await ConfirmReplaceFaultyDataAsync(slot))
            {
                save = false;
            }
        }
        if (save)
            await SaveGameWithNamePromptAsync(slot);
        GetBunkInfo();
        DrawBarracksBunks(viewport);
        events.ShowCursor();
    }

    /// <summary>"Load a game first." until a key or button; returns the key.</summary>
    /// <remarks>C: WarnLoadGameFirst (0x41B550).</remarks>
    private async Task<short> WarnLoadGameFirstAsync()
    {
        var game = _game;
        var events = game.Events;
        events.HideCursor();
        short key = 0;
        var panel = await ModalPrompts.ShowModalTextPanelAsync(game, 0, "Load a game first.");
        if (panel is not null)
        {
            var e = new InputEventState();
            while (events.PollInputEvent(ref e) != 0)
            {
            }
            key = await events.WaitForInputKeyAsync();
            while (events.PollInputEvent(ref e) != 0)
            {
            }
            await ModalPrompts.ReleaseModalTextPanelAsync(game, panel);
        }
        events.ShowCursor();
        return key;
    }

    /// <summary>
    /// Asks for a name (default: the bunk's current game name) and writes the campaign, the
    /// roster and the mission objectives into the slot.
    /// </summary>
    /// <remarks>C: SaveGameWithNamePrompt (0x41B5C0) and SaveGame (0x41B1E0).</remarks>
    private async Task SaveGameWithNamePromptAsync(int slot)
    {
        var game = _game;
        var session = game.Session;
        if (!session.CampaignActive)
        {
            await WarnLoadGameFirstAsync();
            return;
        }
        string oldLabel = _labels[slot * 2] ?? "";
        if (string.Equals(oldLabel, SaveCampaignLabel, StringComparison.Ordinal))
            oldLabel = "";
        int separator = oldLabel.IndexOf(' ', StringComparison.Ordinal);
        if (separator >= 0)
            oldLabel = oldLabel[(separator + 1)..];
        separator = oldLabel.IndexOf('.', StringComparison.Ordinal);
        if (separator >= 0)
            oldLabel = oldLabel[..separator];
        string? description = await ModalPrompts.PromptForTextInputAsync(game, 40, 24, "Game Name: ", oldLabel, 16, TextInput.ModeUpperCase);
        if (description is null)
            return;
        var record = session.CreateSaveRecord(description, _rooms.MissionObjectives);
        if (!SaveGame(slot, record))
            await ModalPrompts.ShowModalMessageAsync(game, $"Error: Game {description} not saved.");
    }

    /// <summary>Writes one slot; false when the file could not be written.</summary>
    /// <remarks>C: SaveGame (0x41B1E0).</remarks>
    private bool SaveGame(int slot, SaveGameSlot record)
    {
        try
        {
            SaveGameFile.Save(SavePath, slot, record);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>"Loading Game..." while the slot is read; adopts it (CAMP data incl. constellations, objectives).</summary>
    /// <remarks>C: LoadGameFromSlot (0x41B980): campaign, roster, nPendingCampaignIndex/nCampaignDataSet,
    /// CAMP sections 0 and 1, objectives, CorrectPointers, bCampaignActive; the developer unlock
    /// renames the player CHEATER.</remarks>
    private async Task LoadGameFromSlotAsync(int slot)
    {
        var game = _game;
        var events = game.Events;
        events.HideCursor();
        var panel = await ModalPrompts.ShowModalTextPanelAsync(game, 0, "Loading Game...");
        if (panel is not null)
        {
            var record = LoadGame(slot);
            if (record is not null)
            {
                game.Session.Apply(record, game.Directory);
                record.Objectives.CopyTo(_rooms.MissionObjectives, 0);
            }
            await ModalPrompts.ReleaseModalTextPanelAsync(game, panel);
            if (record is null)
                await ModalPrompts.ShowModalMessageAsync(game, $"Error: Game {slot} not loaded.");
            else if (game.Options.OriginDevUnlock)
                game.Session.Player.Callsign = "CHEATER";
        }
        events.ShowCursor();
    }

    /// <remarks>C: ConfirmQuitWingCommander (0x41BF10).</remarks>
    private Task<bool> ConfirmQuitWingCommanderAsync() => ModalPrompts.ConfirmAsync(_game, "Quit Wing Commander? (Y/N)");

    /// <summary>"Awaken name? (Y/N)", preceded by "Error: data may be bad." when the slot does not load.</summary>
    /// <remarks>C: ConfirmAwakenAfterBadData (0x41BF60).</remarks>
    private async Task<bool> ConfirmAwakenAfterBadDataAsync(int slot)
    {
        var record = LoadGame(slot);
        string description;
        if (record is null)
        {
            await ModalPrompts.ShowModalMessageAsync(_game, "Error: data may be bad.");
            description = ReadSlotDescription(slot);
        }
        else
        {
            description = record.Description;
        }
        return await ModalPrompts.ConfirmAsync(_game, $"Awaken {description}? (Y/N)");
    }

    /// <summary>"Replace name? (Y/N)" ("FAULTY DATA" when the slot does not load); warns without a campaign.</summary>
    /// <remarks>C: ConfirmReplaceFaultyData (0x41BFE0).</remarks>
    private async Task<bool> ConfirmReplaceFaultyDataAsync(int slot)
    {
        if (!_game.Session.CampaignActive)
        {
            await WarnLoadGameFirstAsync();
            return false;
        }
        var record = LoadGame(slot);
        string description = record?.Description ?? "FAULTY DATA";
        return await ModalPrompts.ConfirmAsync(_game, $"Replace {description}? (Y/N)");
    }

    /// <summary>The description bytes of a slot that did not load (the original printed whatever it read).</summary>
    private string ReadSlotDescription(int slot)
    {
        try
        {
            return SaveGameFile.ReadSlot(SavePath, slot)?.Description ?? "";
        }
        catch (IOException)
        {
            return "";
        }
    }

    /// <summary>Loads the system name of the current series for the medal view ($S).</summary>
    /// <remarks>C: LoadMissionData (0x4059B0, cmpgn.c), abSeriesAuxData part; the mission tables
    /// themselves belong to the flight layer.</remarks>
    private void LoadMissionData(int series, int mission)
    {
        if (MissionText.LoadSystemName(_game, _game.Session.CampaignDataSet, series, mission) is { } name)
            _rooms.SystemName = name;
    }

    /// <remarks>C: BarracksAnimationState (wcdata.h, 0x68 bytes).</remarks>
    private sealed class BarracksAnimation
    {
        public readonly BunkState[] Bunks = new BunkState[8];
        public short FallingDelay;
        public int FallingY;
        public int FallingVelocity;
        public short ImpactFrame;
        public short BlinkDelay;
        public short EyesOpen;
        public short AnimationTick;
        public string? MenuLabel;
    }

    /// <remarks>C: BarracksBunkState (wcdata.h, 0x0A bytes).</remarks>
    private struct BunkState
    {
        public short Occupied;
        public short AnimationFrame;
        public short AnimationPeriod;
        public int AnimationTick;
    }
}
