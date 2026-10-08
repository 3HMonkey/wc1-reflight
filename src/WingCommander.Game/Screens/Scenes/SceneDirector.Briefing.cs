using WingCommander.Audio.Director;
using WingCommander.Core.Resources;
using WingCommander.Game.Scenes;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Game.Screens.Scenes;

public sealed partial class SceneDirector
{
    /// <summary>Layout of a pilot seated in the briefing room: body origin, portrait origin, scale (176 rear row, 256 front row) and portrait frames.</summary>
    /// <remarks>C: BriefingCharacterLayout (wcdata.h); the static "visible" byte is always 1.</remarks>
    private readonly record struct BriefingCharacter(short BodyX, short BodyY, short PortraitX, short PortraitY, short Scale,
        sbyte FirstPortraitFrame, sbyte PortraitFrameCount, bool LargeAnimation);

    /// <remarks>C: aBriefingCharacters (0x0046E218).</remarks>
    private static readonly BriefingCharacter[] BriefingCharacters =
    [
        new(60, 123, 10, 95, 176, 0, 2, false),
        new(316, 123, 264, 94, 176, 2, 1, false),
        new(193, 123, 141, 95, 176, 3, 1, false),
        new(250, 124, 199, 93, 176, 4, 1, false),
        new(124, 123, 71, 94, 176, 5, 1, false),
        new(103, 122, 29, 76, 256, 6, 2, true),
        new(191, 122, 118, 76, 256, 8, 1, true),
        new(287, 122, 212, 76, 256, 9, 1, true),
    ];

    /// <summary>Idle portrait animation of the rear row over the 22-frame establishing shot.</summary>
    /// <remarks>C: abBriefingSmallCharacterAnimation (0x0046E1E8).</remarks>
    private static ReadOnlySpan<sbyte> SmallCharacterAnimation => [1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 0, 0];

    /// <remarks>C: abBriefingLargeCharacterAnimation (0x0046E200).</remarks>
    private static ReadOnlySpan<sbyte> LargeCharacterAnimation => [0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 0, 0];

    /// <summary>Portrait offsets per character and stand-up pose.</summary>
    /// <remarks>C: aBriefingPortraitOffsetX (0x0046E300).</remarks>
    private static ReadOnlySpan<sbyte> PortraitOffsetX =>
    [
        0, 0, -2, -2, -1, -2, -4, -3, -5, -7, -7, -4,
        -1, 0, -2, -3, -3, -4, -3, -4, -6, -8, -7, -6,
        0, 1, 0, 0, 0, 0, 0, -1, -2, -4, -5, -3,
        0, -1, -2, -3, -1, -3, -2, -3, -4, -4, -5, -4,
        2, 0, 0, 0, 0, 0, 0, 0, -2, -3, -4, -2,
        1, 1, 1, 0, 1, 0, -6, -4, -4, -9, -8, -6,
        -2, -1, -4, -4, -4, -7, -8, -7, -5, -8, -9, -7,
        0, 0, 0, 0, 0, -3, -3, -7, -5, -7, -8, -7,
    ];

    /// <remarks>C: aBriefingPortraitOffsetY (0x0046E360).</remarks>
    private static ReadOnlySpan<sbyte> PortraitOffsetY =>
    [
        -6, -2, -3, -1, -4, -10, -12, -20, -32, -38, -42, -43,
        -5, 0, -4, -4, -7, -12, -16, -23, -32, -37, -42, -42,
        -5, -3, 0, 1, 0, -7, -13, -23, -33, -38, -42, -42,
        -3, -2, -1, 0, -5, -10, -16, -25, -33, -38, -41, -41,
        -6, 0, 2, 0, -6, -11, -18, -25, -33, -39, -42, -42,
        -8, -2, 1, -3, -6, -16, -23, -35, -46, -55, -61, -61,
        -6, -4, -3, -5, -9, -14, -23, -34, -49, -57, -61, -62,
        -10, -5, 0, 0, -3, -7, -24, -32, -47, -55, -59, -60,
    ];

    /// <summary>Portrait rotation in degrees per character and pose (the table is misnamed "scale" in the reference; it is the angle argument).</summary>
    /// <remarks>C: aBriefingPortraitScale (0x0046E3C0).</remarks>
    private static ReadOnlySpan<short> PortraitAngle =>
    [
        357, 357, 357, 359, 355, 0, 352, 355, 353, 353, 355, 1,
        355, 0, 354, 354, 352, 354, 355, 355, 352, 352, 353, 350,
        354, 354, 359, 0, 358, 357, 357, 356, 357, 0, 0, 2,
        354, 354, 353, 356, 358, 356, 356, 355, 356, 0, 357, 357,
        0, 0, 0, 0, 358, 0, 358, 0, 0, 4, 0, 359,
        0, 358, 358, 0, 357, 0, 350, 354, 357, 354, 0, 358,
        356, 356, 352, 354, 352, 349, 350, 352, 0, 0, 0, 0,
        0, 0, 0, 359, 0, 350, 354, 350, 356, 357, 357, 357,
    ];

    /// <remarks>C: aiBriefingLeftPanelVelocity (0x0046E480).</remarks>
    private static ReadOnlySpan<short> LeftPanelVelocity => [1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4];

    /// <remarks>C: aiBriefingPodiumVelocity (0x0046E4B0).</remarks>
    private static ReadOnlySpan<short> PodiumVelocity => [1, 2, 2, 3, 3, 4, 4, 4, 5, 5, 5, 6];

    /// <remarks>C: aiBriefingRightPanelVelocity (0x0046E4E0).</remarks>
    private static ReadOnlySpan<short> RightPanelVelocity => [2, 2, 3, 4, 4, 4, 5, 5, 5, 6, 7, 8];

    /// <remarks>C: abBriefingPodiumFrames (0x0046E510).</remarks>
    private static ReadOnlySpan<sbyte> PodiumFrames =>
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9,
        10, 11, 12, 13, 14, 15, 15, 15, 15, 15,
        15, 15, 15, 15, 14, 13, 12, 11, 10, 9,
        8, 7, 6, 5, 4, 0, 0, 0, 0, 0,
    ];

    private BriefingMap? _briefingMap;
    private BriefingMap? _boardMap;

    /// <summary>The wall screen in backdrop frame 1 (art coordinates): inside of the grey frame.</summary>
    private const int BoardLeft = 13, BoardTop = 11, BoardRight = 306, BoardBottom = 116;

    /// <summary>Where the art's shrunken map picture lies: the 260x156 nav map at 154x88 puts its frame on the art's.</summary>
    private const int BoardMapX = 85, BoardMapY = 23, BoardMapWidth = 154, BoardMapHeight = 88;

    /// <summary>The briefing's nav map (keeps the original's label tables between maps).</summary>
    public BriefingMap Map => _briefingMap ??= new BriefingMap(Stage);

    /// <summary>
    /// The mission briefing: loads the mission and its conversation, then plays the briefing room
    /// scene (music 25/26).
    /// </summary>
    /// <remarks>C: Briefing (0x405660, cmpgn.c) and LoadBriefingRoom (0x436D00, screens.c).</remarks>
    public async Task BriefingAsync(int series, int mission)
    {
        Events.EscapePressed = false;
        Stage.PreloadMusicTrack(MusicTrack.BriefingIntro);
        Stage.PreloadMusicTrack(MusicTrack.BriefingMiddle);
        Stage.PreloadMusicTrack(MusicTrack.BriefingEnd);
        LoadMissionData(series, mission);
        var file = BriefingFile.Load(_game.Directory, Session.CampaignDataSet);
        if (!Events.EscapePressed && file.HasMission(series, mission))
            await LoadBriefingRoomAsync(file.GetMission(series, mission).Briefing);
        Events.EscapePressed = false;
    }

    /// <summary>Sets up the conversation layout, loads the briefing room shapes and plays the scene.</summary>
    /// <remarks>C: LoadBriefingRoom (0x436D00, screens.c).</remarks>
    private async Task LoadBriefingRoomAsync(ConversationScript script)
    {
        _backdrop = null;
        _briefingAnimation = null;
        _briefingCloseup = null;
        _briefingBody = null;
        _briefingPortrait = null;
        Stage.InitializeConversationViewport();
        Stage.InitializeConversationText();
        Gfx.SetTextContext(Stage.Text);
        Stage.SpaceTrack(MusicTrack.BriefingMiddle, 2, 1);
        _backdrop = Shape(LogicalFile.BriefingVga, 0);
        _briefingAnimation = Shape(LogicalFile.BriefingVga, 1);
        _briefingCloseup = Shape(LogicalFile.BriefingVga, 3);
        _briefingBody = Shape(LogicalFile.BriefingVga, 4);
        _briefingPortrait = Shape(LogicalFile.BriefingVga, 5);
        await RunAsync(SceneType.Briefing, script);
        _briefingPortrait = null;
        _briefingBody = null;
        _briefingCloseup = null;
        _briefingAnimation = null;
        _backdrop = null;
        Stage.ResetScreenClipToFullHeight();
        if (Events.EscapePressed)
        {
            Stage.StopMusicUnlessSuppressed();
            Events.EscapePressed = false;
        }
    }

    /// <summary>Shot 0: the room long shot with the clock animation and the pilots' idle animation (22 frames), then music 25.</summary>
    /// <remarks>C: EstablishingShot (0x437770, screens.c).</remarks>
    private async Task EstablishingShotAsync(string text, short duration)
    {
        _briefingBody ??= Shape(LogicalFile.BriefingVga, 4);
        _briefingPortrait ??= Shape(LogicalFile.BriefingVga, 5);
        ShowText(text);
        short frame = 0;
        Events.FlushInputEvents();
        do
        {
            Gfx.DrawSpriteDefault(Scene, 0, 0, _backdrop, 0);
            Gfx.DrawSpriteDefault(Scene, 241, 60, _briefingAnimation, frame);
            Gfx.DrawSpriteDefault(Scene, 241, 64, _briefingAnimation, 22);
            for (short character = 0; character < BriefingCharacters.Length; character++)
            {
                var animation = BriefingCharacters[character].LargeAnimation ? LargeCharacterAnimation : SmallCharacterAnimation;
                DrawBriefingCharacter(character, 0, animation[frame]);
            }
            await Stage.RefreshAsync();
            if (Events.CheckEscaped() != 0)
            {
                frame = 21;
                duration = -1;
            }
            frame++;
            await Stage.PresentAsync();
        }
        while (frame < 22);
        await Events.WaitForSceneAdvanceAsync(duration);
        Stage.SpaceTrack(MusicTrack.BriefingMiddle, 1, -1);
    }

    /// <summary>Prepares shot 1: the static long shot of the room with all pilots seated.</summary>
    /// <remarks>C: DrawBriefingLongShot (0x4378D0, screens.c).</remarks>
    private async Task DrawBriefingLongShotAsync()
    {
        _briefingBody ??= Shape(LogicalFile.BriefingVga, 4);
        _briefingPortrait ??= Shape(LogicalFile.BriefingVga, 5);
        Gfx.DrawSpriteDefault(Scene, 0, 0, _backdrop, 0);
        Gfx.DrawSpriteDefault(Scene, 241, 60, _briefingAnimation, 21);
        Gfx.DrawSpriteDefault(Scene, 241, 64, _briefingAnimation, 22);
        for (short character = 0; character < BriefingCharacters.Length; character++)
            DrawBriefingCharacter(character, 0, 0);
        await Stage.RefreshAsync();
    }

    /// <summary>Shot 5 ("Squadron dismissed"): the pilots stand up one after another over 40 frames; music 26.</summary>
    /// <remarks>C: ReturnToBriefingLongShot (0x437980, screens.c).</remarks>
    private async Task ReturnToBriefingLongShotAsync(string text, short duration)
    {
        var activeState = new bool[BriefingCharacters.Length];
        var phaseState = new short[BriefingCharacters.Length];
        Stage.SpaceTrack(MusicTrack.BriefingEnd, 1, -1);
        _briefingBody ??= Shape(LogicalFile.BriefingVga, 4);
        _briefingPortrait ??= Shape(LogicalFile.BriefingVga, 5);
        ShowText(text);
        short frame = 0;
        while (true)
        {
            Gfx.DrawSpriteDefault(Scene, 0, 0, _backdrop, 0);
            Gfx.DrawSpriteDefault(Scene, 241, 60, _briefingAnimation, 0);
            Gfx.DrawSpriteDefault(Scene, 241, 64, _briefingAnimation, 22);
            for (short character = 0; character < BriefingCharacters.Length; character++)
            {
                if (!activeState[character] && _game.Random.BelowOrEqual(5) == 0)
                    activeState[character] = true;
                DrawBriefingCharacter(character, phaseState[character], 0);
                if (activeState[character] && phaseState[character] < 11)
                    phaseState[character]++;
            }
            await Stage.RefreshAsync();
            if (Events.CheckEscaped() != 0)
            {
                duration = 0;
                Stage.StopMusicUnlessSuppressed();
                break;
            }
            frame++;
            await Stage.PresentAsync();
            if (frame > 39)
                break;
        }
        await Events.WaitForSceneAdvanceAsync(duration);
        Stage.ClearSubtitle();
    }

    /// <summary>
    /// Shot 3 ("Computer, display ..."): the wall panels slide apart and the Colonel steps aside
    /// to reveal the map screen (32 frames); a key jumps to the final positions. Afterwards the
    /// shot counts as the map shot.
    /// </summary>
    /// <remarks>C: Dismissed (0x437B80, screens.c). The panel deltas of the original start
    /// uninitialised; they are only read after being set (or after a skip, when nothing is drawn any
    /// more), so they start at 0 here.</remarks>
    private async Task DismissedAsync(string text, short duration)
    {
        short podiumFrame = 0;
        short rightX = 252;
        short leftX = -96;
        short podiumX = 240;
        _talkingHead = null;
        _briefingBody = null;
        _briefingPortrait = null;
        ShowText(text);
        short frame = 0;
        _talkingHead = Shape(LogicalFile.BriefingVga, 2);
        int frameSkipCounter = 1;
        short leftDelta = 0, podiumDelta = 0, rightDelta = 0;
        do
        {
            if (Events.CheckEscaped() != 0)
            {
                rightX = 348;
                leftX = 0;
                frame = 31;
                podiumFrame = 34;
                podiumX = 336;
            }
            frameSkipCounter--;
            if (frameSkipCounter < 1)
            {
                frameSkipCounter = FrameSkip;
                Gfx.DrawSpriteDefault(Scene, leftX, 0, _backdrop, 1);
                Gfx.DrawSpriteDefault(Scene, leftX + 320, 0, _backdrop, 2);
                Gfx.DrawSpriteDefault(Scene, podiumX, 127, _talkingHead, PodiumFrames[podiumFrame]);
                Gfx.DrawSpriteDefault(Scene, rightX, 127, _backdrop, 3);
                await Stage.RefreshAsync();
            }
            if (podiumFrame < 34)
                podiumFrame++;
            if (frame < 12)
            {
                leftDelta = LeftPanelVelocity[frame];
                podiumDelta = PodiumVelocity[frame];
                rightDelta = RightPanelVelocity[frame];
            }
            if (frame > 24)
                leftDelta = LeftPanelVelocity[11 - (frame - 25)];
            podiumX = (short)(podiumX + podiumDelta);
            frame++;
            leftX = (short)(leftX + leftDelta);
            rightX = (short)(rightX + rightDelta);
            await Stage.PresentAsync();
        }
        while (frame < 32);
        await Events.WaitForSceneAdvanceAsync(duration);
        _talkingHead = null;
        Gfx.SetTextContext(Stage.Text);
    }

    /// <summary>Prepares shot 2: the Colonel at the podium (the mouth frames are drawn by <see cref="CloseLookAsync"/>).</summary>
    /// <remarks>C: DrawPodiumShot (0x439070, screens.c).</remarks>
    private async Task DrawPodiumShotAsync()
    {
        _talkingHead = null;
        _briefingBody = null;
        _briefingPortrait = null;
        _talkingHead = Shape(LogicalFile.BriefingVga, 2);
        Gfx.DrawSpriteDefault(Scene, -96, 0, _backdrop, 1);
        DrawMissionOnBoard(-96);
        Gfx.DrawSpriteDefault(Scene, 224, 0, _backdrop, 2);
        Gfx.DrawSpriteDefault(Scene, 240, 127, _talkingHead, 0);
        Gfx.DrawSpriteDefault(Scene, 252, 127, _backdrop, 3);
        await Stage.RefreshAsync();
        _talkingHead = null;
    }

    /// <summary>
    /// Reflight: the wall screen behind the Colonel shows the map of this mission instead of the
    /// picture in the art (the same shrunken map of the first mission in every briefing, its text
    /// unreadable), while text is drawn at output resolution: readout and labels are then sharp.
    /// With the classic text the art stays as it is.
    /// </summary>
    /// <param name="boardX">Where backdrop frame 1 was drawn.</param>
    private void DrawMissionOnBoard(int boardX)
    {
        if (!_game.SharpTextActive || Mission is not { } mission)
            return;
        Gfx.DrawFilledViewportRect(Scene, boardX + BoardLeft, BoardTop, boardX + BoardRight, BoardBottom, Black);
        (_boardMap ??= new BriefingMap(Stage)).DrawOnBoard(mission, Scene, boardX + BoardMapX, BoardMapY, BoardMapWidth, BoardMapHeight);
    }

    /// <summary>
    /// One seated pilot: portrait (frame by the idle animation, offset and tilted by the stand-up
    /// pose) and body (frame = pose), both scaled by the row's scale.
    /// </summary>
    /// <remarks>C: DrawBriefingCharacter (0x439150, screens.c); its two offset-table arguments are unused.</remarks>
    private void DrawBriefingCharacter(short character, short pose, int animationFrame)
    {
        var layout = BriefingCharacters[character];
        int frame = layout.FirstPortraitFrame;
        if (animationFrame < layout.PortraitFrameCount)
            frame += animationFrame;
        int offsetIndex = character * 12 + pose;
        Gfx.DrawSpriteScaled(Scene, layout.PortraitX + PortraitOffsetX[offsetIndex], layout.PortraitY + PortraitOffsetY[offsetIndex],
            _briefingPortrait, frame, PortraitAngle[offsetIndex], layout.Scale, 0);
        Gfx.DrawSpriteScaled(Scene, layout.BodyX, layout.BodyY + 10, _briefingBody, pose, 0, layout.Scale, 0);
    }

    /// <summary>
    /// Shots 1, 2 and 11: the subtitle over the prepared picture; for the podium (2) and the
    /// debriefing officer (11) the mouth script animates the speaker (two or three presents per
    /// loop iteration, like the original). A key ends the record.
    /// </summary>
    /// <remarks>C: CloseLook (0x405DE0, cmpgn.c). Its shot-0 branch (a 22-frame room animation
    /// drawing 14 characters from an 8-entry table) is unreachable: SceneDirector sends shot 0 to
    /// EstablishingShot.</remarks>
    private async Task CloseLookAsync(ShapeTable? shape, short shot, short[] animation, string text, short duration)
    {
        bool finished = false;
        ShowText(text);
        int frameSkipCounter = 1;
        int cursor = 0;
        if (shot == 2 || shot == 11)
        {
            if (At(animation, cursor) != -1)
            {
                short countdown = 0;
                short frame = -1;
                do
                {
                    if (countdown-- == 0)
                    {
                        if (At(animation, cursor) != -1)
                            cursor += 2;
                        if (At(animation, cursor) == -2)
                        {
                            cursor = 0;
                        }
                        else if (At(animation, cursor) == -1)
                        {
                            frame = -1;
                            if (!finished)
                            {
                                finished = true;
                                _game.Timing.SetFrameTimerPeriod(duration);
                            }
                        }
                        else
                        {
                            frame = At(animation, cursor);
                            countdown = unchecked((short)(At(animation, cursor + 1) * 2));
                        }
                    }
                    frameSkipCounter--;
                    if (frameSkipCounter < 1)
                    {
                        frameSkipCounter = FrameSkip;
                        if (shot == 11)
                        {
                            await DrawDebriefingLongShotAsync();
                            if (frame > -1)
                                Gfx.DrawSpriteDefault(Scene, _debriefPodiumX, 53, _backdrop, frame + 17);
                        }
                        else if (frame > -1)
                        {
                            Gfx.DrawSpriteDefault(Scene, 225, 34, shape, frame);
                        }
                        await Stage.RefreshAsync();
                        await Stage.PresentAsync();
                    }
                    if (Events.CheckEscaped() != 0)
                    {
                        while (Events.CheckEscaped() != 0)
                            await Stage.Idle();
                        return;
                    }
                    if (finished && _game.Timing.IsFrameTickElapsed())
                        return;
                    await Stage.PresentAsync();
                }
                while (At(animation, cursor) != -1);
            }
        }
        await Stage.PresentAsync();
        await Events.WaitForSceneAdvanceAsync(duration);
    }

    /// <summary>
    /// Shot 4: the subtitle and the nav map with objective |talker| highlighted, held for the
    /// record's duration; the screen is cleared afterwards.
    /// </summary>
    /// <remarks>C: UpdateMap (0x405CC0, cmpgn.c). The screen and scene-buffer copies it saves are
    /// restored before the map is drawn (no effect), so the map's top edge (row 4) stays in force for
    /// the rest of the briefing, as in the reference.</remarks>
    private async Task UpdateMapAsync(string text, short duration)
    {
        await Stage.Display.ClearViewportAsync(Stage.Screen, Black);
        if (Scene.IsAllocated)
            Gfx.ClearViewport(Scene, Black);
        ShowText(text);
        if (Mission is { } mission)
            await Map.DisplayAsync(mission);
        await Events.WaitForSceneAdvanceAsync(duration);
        await Stage.Display.ClearViewportAsync(Stage.Screen, Black);
        Gfx.SetTextContext(Stage.Text);
        await Stage.Display.ClearViewportAsync(Stage.Screen, Black);
    }
}
