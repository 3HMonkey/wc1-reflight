using WingCommander.Core.Resources;
using WingCommander.Game.Campaign;
using WingCommander.Game.Input;
using WingCommander.Game.Scenes;
using WingCommander.Game.Screens.Scenes;
using WingCommander.Game.Screens.Ui;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Graphics.Text;

namespace WingCommander.Game.Screens.Rooms;

/// <summary>
/// The rec room (bar) of the Tiger's Claw: Shotglass behind the bar and up to two pilots of the
/// current mission seated at a table, each with an idle animation; the drifting stars in the
/// window; the label of the region under the pointer at the bottom. Talking to someone plays
/// their rec-room conversation, the chalkboard opens the kill board, the door returns 4
/// (barracks) and the simulator console returns 5. Only the dirty parts (the seated pilots,
/// the bar with the window, the label strip) are copied to the screen every 9 ticks.
/// </summary>
/// <remarks>C: RecRoom (0x43F940, killbrd.c) with InitializeRoomViewports and the room menu
/// helpers; tables apRecRoomAnimations (0x00470458), aRecRoomCharacterOrigins (0x00470490),
/// aRecRoomMenuRegions (0x004704A0), apszRecRoomBaseLabels/apszRecRoomMenuLabels.</remarks>
internal sealed class RecRoom
{
    /// <summary>RECROOM.VGA sections.</summary>
    private const int BackgroundSection = 0;
    private const int ConversationBackdropSection = 1;
    private const int ShotglassSection = 11;
    private const int FirstPilotSection = 3;

    /// <summary>Music track of the bar.</summary>
    private const int MusicTrack = 30;

    /// <summary>Rec room result: the barracks door.</summary>
    public const int Barracks = 4;

    /// <summary>Rec room result: the simulator console.</summary>
    public const int Simulator = 5;

    /// <summary>Conversation scene type of the rec room (nConversationSceneType).</summary>
    private const int RecRoomSceneType = 2;

    // apRecRoomAnimations: per personality 0..7 the idle animation of the seated pilot, then
    // Shotglass (9 idle, 10 glass, 11 pour, 12 wipe); -1 restarts. C: 0x00470260-0x00470440.
    private static readonly sbyte[] SpiritAnimation =
    [
        0, 0, 0, 0, 1, 1, 2, 2, 1, 1, 2, 3, 4, 3, 3, 4,
        3, 4, 3, 4, 5, 3, 4, 5, 3, 4, 3, 4, 3, 4, 5, 3,
        2, 2, 2, 3, 2, 2, 2, 1, 1, 1, -1, 0, 0, 0, 0, 0,
    ];

    private static readonly sbyte[] HunterAnimation =
    [
        0, 0, 0, 1, 1, 0, 0, 1, 0, 0, 1, 0, 1, 1, 2, 2,
        3, 3, 4, 4, 5, 3, 3, 4, 4, 5, 5, 5, 2, 2, 1, 1,
        0, 0, -1, 0, 0, 0, 0, 0,
    ];

    private static readonly sbyte[] AngelAnimation =
    [
        3, 4, 3, 3, 4, 3, 4, 4, 3, 4, 3, 4, 0, 0, 0, 2,
        0, 2, 0, 1, 0, 2, 0, 0, 2, 0, 1, 0, 2, 3, 3, 4,
        3, 4, 4, 5, 5, 5, 5, 5, -1, 0, 0, 0, 0, 0, 0, 0,
    ];

    private static readonly sbyte[] KnightAnimation =
    [
        0, 0, 0, 0, 0, 1, 0, 1, 0, 5, 0, 0, 1, 2, 3, 4,
        4, 3, 2, 2, 2, 3, 4, 4, 5, 5, 0, 0, 1, 0, 1, 2,
        1, 0, 1, -1, 0, 0, 0, 0,
    ];

    private static readonly sbyte[] IcemanAnimation =
    [
        0, 0, 0, 0, 0, 0, 0, 1, 2, 2, 2, 2, 3, 3, 3, 3,
        4, 4, 4, 5, 4, 4, 5, 4, 4, 5, 4, 4, 5, 4, 4, 3,
        3, 2, 2, 1, 0, 0, -1, 0,
    ];

    private static readonly sbyte[] ManiacAnimation =
    [
        0, 1, 2, 0, 1, 2, 0, 3, 2, 0, 1, 1,
        0, 4, 2, 0, 1, 2, 0, 5, 5, 5, 5, -1,
    ];

    private static readonly sbyte[] PaladinAnimation =
    [
        0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 2, 2, 2, 3, 2,
        2, 3, 2, 3, 2, 3, 2, 4, 4, 2, 3, 2, 3, 2, 3, 2,
        3, 2, 3, 4, 4, 5, 5, 4, 4, 4, 4, 5, 5, 4, 2, 2,
        2, -1, 0, 0, 0, 0, 0, 0,
    ];

    private static readonly sbyte[] BossmanAnimation =
    [
        3, 3, 3, 2, 3, 2, 3, 2, 3, 3, 2, 3, 1, 3, 1, 3,
        0, 3, 0, 3, 0, 2, 1, 2, 1, 3, 3, 4, 5, 5, 4, 5,
        4, 5, 1, 3, 1, 3, 1, 3, -1, 0, 0, 0, 0, 0, 0, 0,
    ];

    private static readonly sbyte[] ShotglassIdleAnimation =
    [
        0, 0, 0, 0, 0, 1, 1, 0, 0, 2, 2, 0,
        0, 3, 3, 3, -1, 0, 0, 0, 0, 0, 0, 0,
    ];

    private static readonly sbyte[] ShotglassGlassAnimation =
    [
        3, 6, 7, 8, 8, 9, 9, 10, 10, 10, 12, 11, 10, 12, 11, 10,
        12, 11, 10, 12, 11, 10, 13, 13, 13, 10, 9, 8, 8, 3, 3, -1,
    ];

    private static readonly sbyte[] ShotglassPourAnimation =
    [
        3, 3, 6, 6, 7, 8, 8, 14, 15, 15, 16, 17, 18, 18, 18, 18,
        17, 19, 19, 20, 20, 21, 20, 21, 20, 21, 20, 21, 20, 21, 20, 21,
        22, 22, 23, 24, 24, 25, 25, 26, 26, 27, 28, 28, 28, 29, 30, 31,
        31, 31, 31, 31, 32, 32, 31, 31, 31, 33, 33, 32, 34, 34, 35, 35,
        36, 36, 37, 37, 37, 38, 38, 39, 40, 3, 3, 3, -1, 0, 0, 0,
    ];

    private static readonly sbyte[] ShotglassWipeAnimation =
    [
        3, 4, 3, 4, 5, 3, 4, 3, 5, 6, 3, 3,
        3, 3, 4, 3, 4, 3, -1, 0, 0, 0, 0, 0,
    ];

    /// <remarks>C: apRecRoomAnimations[14] (0x00470458).</remarks>
    private static readonly sbyte[]?[] Animations =
    [
        SpiritAnimation, HunterAnimation, BossmanAnimation, IcemanAnimation,
        AngelAnimation, PaladinAnimation, ManiacAnimation, KnightAnimation,
        null, ShotglassIdleAnimation, ShotglassGlassAnimation, ShotglassPourAnimation,
        ShotglassWipeAnimation, null,
    ];

    /// <summary>Hot spots of Shotglass and the two seats.</summary>
    /// <remarks>C: aRecRoomCharacterOrigins (0x00470490).</remarks>
    private static readonly (short X, short Y)[] CharacterOrigins = [(94, 59), (161, 79), (202, 79)];

    /// <remarks>C: szTalkToShotglass, szCheckPilotScores, szEnterBarracks, szFlyTrainingMission.</remarks>
    private const string TalkToShotglass = "Talk to SHOTGLASS.";
    private const string CheckPilotScores = "Check pilot scores";
    private const string EnterBarracks = "Enter barracks";
    private const string FlyTrainingMission = "Fly training mission";

    private readonly Wc1Game _game;
    private readonly GameFlowScreens _screens;
    private readonly RoomsState _rooms;

    public RecRoom(Wc1Game game, GameFlowScreens screens, RoomsState rooms)
    {
        _game = game;
        _screens = screens;
        _rooms = rooms;
    }

    /// <summary>Initial menu regions: Shotglass, left seat, right seat, chalkboard, barracks door, simulator.</summary>
    /// <remarks>C: aRecRoomMenuRegions (0x004704A0); the three character regions are replaced by
    /// the frame bounds of the seated characters on every visit.</remarks>
    private static MenuRegion[] CreateRegions() =>
    [
        new(1, 94, 59, 130, 95),
        new(1, 161, 79, 180, 95),
        new(1, 210, 79, 240, 95),
        new(1, 180, 50, 250, 75),
        new(1, 275, 50, 319, 135),
        new(1, 0, 100, 120, 190),
        new(MenuRegion.EndOfList, 0, 0, 0, 0),
    ];

    /// <summary>Runs the room until the barracks door (4) or the simulator (5) is chosen.</summary>
    /// <remarks>C: RecRoom (0x43F940, killbrd.c).</remarks>
    public async Task<int> RunAsync()
    {
        var game = _game;
        var gfx = game.Graphics;
        var events = game.Events;
        var display = game.Display;
        var timing = game.Timing;
        var random = game.Random;
        var session = game.Session;
        var campaign = session.State;
        var resources = game.Resources;

        short result = 0;
        int lastChalkboardTick = 0;
        bool firstFrame = false;
        short characterMask = 0;

        RoomSound.StartTrack(game, MusicTrack);
        events.FlushInputEvents();
        MissionConversations? conversations = LoadBriefingData(campaign.CurrentSeries, campaign.CurrentMission);
        var roster = CampaignFile.Parse(resources.GetPacket(CampaignFile.LogicalFileFor(session.CampaignDataSet)))
            .GetRecRoomPilots(campaign.CurrentSeries, campaign.CurrentMission);

        var animationIds = new sbyte[3];
        animationIds[0] = (sbyte)(random.InRange(0, 3) + 9);
        var shapes = new ShapeTable?[3];
        var regions = CreateRegions();
        for (int i = 0; i < 3; i++)
            regions[i] = new MenuRegion(regions[i].Frame, 400, 400, 401, 401);
        Span<short> bounds = stackalloc short[4];

        var shotglass = resources.GetShape(LogicalFile.RecRoomVga, ShotglassSection);
        shapes[0] = shotglass;
        SetRegionToFrameBounds(ref regions[0], 0, shotglass, 0, bounds);

        animationIds[1] = roster.First;
        if (animationIds[1] != -1)
            _rooms.TalkToFirstPilot = SeatPilot(1, animationIds[1], shapes, regions, bounds);
        animationIds[2] = roster.Second;
        // The original tests the first seat's id here too; no campaign data has only a second pilot.
        if (animationIds[1] != -1)
            _rooms.TalkToSecondPilot = SeatPilot(2, animationIds[2], shapes, regions, bounds);

        if (shapes[2] is not null)
            characterMask = 1;
        if (shapes[1] is not null)
        {
            characterMask = 2;
            if (shapes[2] is not null)
                characterMask = 3;
        }

        var viewports = RoomViewports.Initialize(game);
        var sceneBuffer = viewports.SceneBuffer;
        // init_constellation(0): the shared Scenes implementation (InitializeConstellationField / DrawConstellationField).
        var field = ConstellationField.Create(game);
        var constellationViewport = sceneBuffer.Clone();
        constellationViewport.SetViewportRect(54, 35, 146, 72);
        field.Initialize(constellationViewport, -1, 6);

        var animations = new AnimationCursor[3];
        animations[0] = new AnimationCursor(ShotglassIdleAnimation);
        animations[1] = new AnimationCursor(AnimationFor(animationIds[1]));
        animations[2] = new AnimationCursor(AnimationFor(animationIds[2]));
        string?[] labels = [TalkToShotglass, _rooms.TalkToFirstPilot, _rooms.TalkToSecondPilot,
            CheckPilotScores, EnterBarracks, FlyTrainingMission];
        var menu = new RoomMenu(game, regions, labels, viewports.Screen, TextContext.AlignCentre);

        var bottomSource = sceneBuffer.Clone();
        bottomSource.SetViewportRect(0, 187, 319, 199);
        var bottomDestination = viewports.Screen.Clone();
        bottomDestination.SetViewportRect(0, 187, 319, 199);
        ShapeTable? background = resources.GetShape(LogicalFile.RecRoomVga, BackgroundSection);
        // nMenuPointerSpeed = 1 only scales the joystick pointer (not ported).
        events.InputMode = 1;
        game.Cursor.Viewport = viewports.Display;

        var pilotWork = sceneBuffer.Clone();
        var shotglassWork = sceneBuffer.Clone();
        var pilotDestination = viewports.Screen.Clone();
        var shotglassDestination = viewports.Screen.Clone();
        Span<short> firstPilotBounds = stackalloc short[4];
        Span<short> secondPilotBounds = stackalloc short[4];
        if (shapes[2] is not null)
            ShapeBounds.GetShapeFrameBounds(secondPilotBounds, CharacterOrigins[2].X, CharacterOrigins[2].Y, shapes[2], animations[2].Current);
        if (shapes[1] is not null)
        {
            ShapeBounds.GetShapeFrameBounds(firstPilotBounds, CharacterOrigins[1].X, CharacterOrigins[1].Y, shapes[1], animations[1].Current);
            SetRect(pilotWork, firstPilotBounds);
            if (shapes[2] is not null)
                UnionRectBounds(pilotWork, firstPilotBounds, secondPilotBounds);
        }
        else if (shapes[2] is not null)
        {
            SetRect(pilotWork, secondPilotBounds);
        }
        if (shapes[1] is not null || shapes[2] is not null)
            CopyRect(pilotDestination, pilotWork);

        events.WarpMouseTo(160, 100);
        ShapeBounds.GetShapeFrameBounds(bounds, CharacterOrigins[0].X, CharacterOrigins[0].Y, shapes[0], animations[0].Current);
        SetRect(shotglassWork, bounds);

        var e = new InputEventState();
        while (result == 0)
        {
            if (!firstFrame)
            {
                gfx.DrawSpriteDefault(sceneBuffer, 0, 0, background, 0);
                if (characterMask != 0)
                    gfx.DrawSpriteDefault(sceneBuffer, 158, 128, background, characterMask);
                timing.SetFrameTimerPeriod(0);
            }

            if (timing.IsFrameTickElapsed())
            {
                gfx.DrawSpriteDefault(pilotWork, 0, 0, background, 0);
                for (int index = 0; index < 3; index++)
                {
                    var shape = shapes[index];
                    if (shape is null)
                        continue;
                    if (animations[index].Current == -1)
                    {
                        if (index == 0)
                        {
                            animationIds[0] = (sbyte)(random.InRange(0, 3) + 9);
                            if (animationIds[0] == 11 && random.InRange(0, 3) != 0)
                                animationIds[0]--;
                        }
                        animations[index] = new AnimationCursor(AnimationFor(animationIds[index]));
                    }

                    var (x, y) = CharacterOrigins[index];
                    if (index > 0)
                    {
                        gfx.DrawSpriteDefault(pilotWork, x, y, shape, 0);
                        gfx.DrawSpriteDefault(pilotWork, x, y, shape, animations[index].Next());
                    }
                    else
                    {
                        UnionRectBounds(shotglassWork, shotglassWork, constellationViewport);
                        field.Draw();
                        gfx.DrawSpriteDefault(shotglassWork, 0, 0, background, 0);
                        gfx.DrawSpriteDefault(shotglassWork, x, y, shape, animations[index].Next());
                        // The bounds of the next frame only fed ShouldSuspendCursorForRect, which always returns 0.
                        if (firstFrame)
                        {
                            CopyRect(shotglassDestination, shotglassWork);
                            gfx.CopyViewportContents(shotglassWork, shotglassDestination);
                        }
                    }
                }

                if (!firstFrame)
                {
                    firstFrame = true;
                    if (game.Flow.PanRoomTransition)
                    {
                        await ScreenTransitions.PanToScreenAsync(game, sceneBuffer, viewports.Screen);
                        game.Flow.PanRoomTransition = false;
                    }
                    else
                    {
                        gfx.CopyViewportContents(sceneBuffer, viewports.Screen);
                    }
                    events.ShowCursor();
                }
                else if (shapes[1] is not null || shapes[2] is not null)
                {
                    gfx.CopyViewportContents(pilotWork, pilotDestination);
                }

                events.HideCursor();
                gfx.CopyViewportContents(bottomSource, bottomDestination);
                menu.RefreshLabel();
                events.ShowCursor();
                timing.SetFrameTimerPeriod(9);
            }

            bool clicked = false;
            short eventType = events.PollInputEvent(ref e);
            if (eventType is InputEventType.KeyDown or InputEventType.Character)
            {
                events.ClearInputKeyStatePreservingModifiers();
                short key = unchecked((short)e.Value);
                if (key == 0x01)
                {
                    // Esc: the port's pause menu (settings, quit); the rooms had no use for Esc.
                    await game.ShowPauseMenuAsync(PauseMenuContext.Menu);
                    events.EscapePressed = false;
                }
                else if (key is 0x1c or 0x39)
                    clicked = true;
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
                events.HideCursor();
                if (region is >= 0 and <= 2)
                {
                    if (shapes[region] is not null)
                    {
                        background = null;
                        sceneBuffer.Bottom = 127;
                        var screen = gfx.Screen!;
                        screen.Top = 24;
                        screen.Bottom = 151;
                        // InitializeConversationText belongs to the conversation layer.
                        gfx.ClearViewport(viewports.Screen, PaletteColours.Black);
                        var backdrop = resources.GetShape(LogicalFile.RecRoomVga, ConversationBackdropSection);
                        if (conversations is not null)
                            await _screens.PlayConversationAsync(RecRoomSceneType, conversations.RecRoom[region], backdrop);
                        events.EscapePressed = false;
                        events.Pump = null;
                        timing.SetFrameTimerPeriod(1);
                        screen.Top = 0;
                        screen.Bottom = 199;
                        sceneBuffer.Bottom = 199;
                        constellationViewport.CopyFrom(sceneBuffer);
                        constellationViewport.SetViewportRect(54, 35, 146, 72);
                        field.Initialize(constellationViewport, -1, 6);
                        background = resources.GetShape(LogicalFile.RecRoomVga, BackgroundSection);
                        gfx.ClearViewport(viewports.Screen, PaletteColours.Black);
                    }
                }
                else if (region == 3)
                {
                    events.FlushInputEvents();
                    if (unchecked((int)(timing.Ticks60Hz - (uint)lastChalkboardTick)) > events.InputTickScale)
                    {
                        await ChalkBoard.ShowAsync(game, _rooms);
                        gfx.ClearViewport(sceneBuffer, PaletteColours.Black);
                        lastChalkboardTick = unchecked((int)timing.Ticks60Hz);
                    }
                }
                else if (region is Barracks or Simulator)
                {
                    result = (short)region;
                }
                else
                {
                    clicked = false;
                    events.ShowCursor();
                }

                viewports.Mouse.CopyFrom(gfx.Screen!);
                game.Cursor.Viewport = viewports.Mouse;
                events.InputMode = 1;
                if (clicked)
                    firstFrame = false;
            }

            await display.PresentAsync();
        }

        events.MenuInputRepeatDelay = viewports.SavedMenuInputRepeatDelay;
        gfx.ReleaseTextFont(0);
        await display.ClearViewportAsync(gfx.Screen!, PaletteColours.Black);
        viewports.Free();
        events.EscapePressed = false;
        RoomSound.StopTrack(game, MusicTrack);
        return result;
    }

    /// <summary>The conversations of the current mission (null when the briefing file has none).</summary>
    /// <remarks>C: LoadBriefingData (0x405910, cmpgn.c) for the rec-room pointers.</remarks>
    private MissionConversations? LoadBriefingData(int series, int mission)
    {
        var briefing = BriefingFile.Parse(_game.Resources.GetPacket(BriefingFile.LogicalFileFor(_game.Session.CampaignDataSet)));
        return briefing.HasMission(series, mission) ? briefing.GetMission(series, mission) : null;
    }

    /// <summary>
    /// Seats a pilot when alive: loads the pilot's shape, sets the menu region to frame 0 and
    /// returns the "Talk to ..." label ("" for a dead pilot). Personalities outside 0..7 have no
    /// rec-room art and stay empty (CAMP.002 lists ids 11 and 12).
    /// </summary>
    private string SeatPilot(int seat, sbyte personality, ShapeTable?[] shapes, MenuRegion[] regions, Span<short> bounds)
    {
        if (personality is < 0 or > 7 || _game.Session.State.PersonalityDeathMission[personality] != 0)
            return "";
        var shape = _game.Resources.GetShape(LogicalFile.RecRoomVga, personality + FirstPilotSection);
        shapes[seat] = shape;
        SetRegionToFrameBounds(ref regions[seat], seat, shape, 0, bounds);
        return $"Talk to {_game.Session.Pilots[personality].Callsign}.";
    }

    private static void SetRegionToFrameBounds(ref MenuRegion region, int character, ShapeTable shape, int frame, Span<short> bounds)
    {
        bounds[0] = region.Left;
        bounds[1] = region.Top;
        bounds[2] = region.Right;
        bounds[3] = region.Bottom;
        ShapeBounds.GetShapeFrameBounds(bounds, CharacterOrigins[character].X, CharacterOrigins[character].Y, shape, frame);
        region.SetRect(bounds);
    }

    private static sbyte[]? AnimationFor(sbyte id) => id >= 0 && id < Animations.Length ? Animations[id] : null;

    private static void SetRect(Viewport viewport, ReadOnlySpan<short> bounds) =>
        viewport.SetViewportRect(bounds[0], bounds[1], bounds[2], bounds[3]);

    private static void CopyRect(Viewport destination, Viewport source) =>
        destination.SetViewportRect(source.Left, source.Top, source.Right, source.Bottom);

    /// <remarks>C: UnionRectBounds (0x431EA0, screen.c).</remarks>
    private static void UnionRectBounds(Viewport destination, ReadOnlySpan<short> first, ReadOnlySpan<short> second) =>
        destination.SetViewportRect(
            Math.Min(first[0], second[0]), Math.Min(first[1], second[1]),
            Math.Max(first[2], second[2]), Math.Max(first[3], second[3]));

    /// <remarks>C: UnionRectBounds (0x431EA0, screen.c) on viewport rectangles.</remarks>
    private static void UnionRectBounds(Viewport destination, Viewport first, Viewport second) =>
        destination.SetViewportRect(
            Math.Min(first.Left, second.Left), Math.Min(first.Top, second.Top),
            Math.Max(first.Right, second.Right), Math.Max(first.Bottom, second.Bottom));

    /// <summary>A position in an animation table (the original's <c>signed char *</c>).</summary>
    private struct AnimationCursor(sbyte[]? script)
    {
        private readonly sbyte[]? _script = script;
        private int _position;

        /// <summary>The frame at the cursor (-1 = restart; 0 for a missing table).</summary>
        public readonly sbyte Current => _script is null ? (sbyte)0 : _script[_position];

        /// <summary><c>*animation++</c>.</summary>
        public sbyte Next() => _script is null ? (sbyte)0 : _script[_position++];
    }
}
