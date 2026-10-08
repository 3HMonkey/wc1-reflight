using WingCommander.Core.Resources;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Graphics.Text;
using WingCommander.Simulation.Data;

namespace WingCommander.Game.Flight;

// The 2D hangar scenes around a flight: brains.c scramble, landing, DrawScrambleFrame and the
// scramble actors (ConfigureScrambleActor, DrawScrambleActor); main.c InitializeConversationViewport,
// ResetScreenClipToFullHeight, RefreshMemoryStatusOverlay, InitializeConversationText.
internal sealed partial class FlightSession
{
    /// <remarks>C: ScrambleAnimationActor (include/wcdata.h).</remarks>
    private sealed class ScrambleActor(sbyte baseFrame, sbyte animationFrame, ushort[]? animation)
    {
        public short X;
        public short Y;
        public short DeltaX;
        public short DeltaY;
        public readonly sbyte BaseFrame = baseFrame;
        public sbyte AnimationFrame = animationFrame;
        public sbyte AnimationState;
        public readonly ushort[]? Animation = animation;
        public ShapeTable? Shape;
        public short Angle;
        public short Scale;
        public sbyte Flip;
    }

    /// <remarks>C: ausScrambleActorAnimationA / ausScrambleActorAnimationB (0x00465788 / 0x00465798).</remarks>
    private static readonly ushort[] ScrambleActorAnimationA = [0, 1, 2, 3, 4, 5, 0x80, 0];
    private static readonly ushort[] ScrambleActorAnimationB = [0, 0, 1, 2, 2, 2, 2, 2, 1, 1, 0x80];

    /// <remarks>C: aScrambleAnimationActors[5] (0x004657B0); the actor state persists between scenes.</remarks>
    private readonly ScrambleActor[] _scrambleActors =
    [
        new(2, 0, ScrambleActorAnimationA),
        new(10, 0, ScrambleActorAnimationB),
        new(13, 0, ScrambleActorAnimationB),
        new(0, -1, null),
        new(1, -1, null),
    ];

    /// <summary>Damage details of each fighter: (frame, x, y) relative to the ship.</summary>
    /// <remarks>C: aaScrambleShipDetails[4][32] (0x00465828).</remarks>
    private static readonly (sbyte Frame, short X, short Y)[][] ScrambleShipDetails =
    [
        [
            (2, 23, 5), (3, -112, 16), (6, -94, -23), (6, -26, 5), (7, -58, -11), (7, -98, 5), (7, -14, 13), (7, 60, 6),
            (7, 149, 24), (9, -112, -24), (9, -99, -9), (9, -116, 18), (9, -81, 15), (9, -41, 9), (9, -54, 1), (9, -12, -14),
            (9, 45, 18), (9, 75, 11), (9, 130, 14), (9, 166, 24), (0, -68, -35), (0, -19, -9), (0, 68, 13), (8, -76, -18),
            (8, -33, -1), (8, 97, 31), (10, -86, 10), (10, -117, 47), (10, 9, 0), (1, -111, 52), (1, -50, 15), (1, 75, 5),
        ],
        [
            (3, -88, 20), (6, -57, 29), (7, -139, 63), (7, -72, 5), (7, -14, 28), (9, -91, -34), (9, -113, -14), (9, -132, 33),
            (9, -95, 34), (9, -60, 12), (9, -155, 52), (9, -66, 51), (9, -56, 44), (9, 44, 35), (9, 57, 25), (9, 59, -1),
            (9, 123, 41), (9, 148, 30), (0, -135, -2), (0, -100, 59), (8, -40, 11), (10, -157, -19), (10, -77, 35), (6, 248, 17),
            (7, 300, 41), (9, 172, 27), (9, 194, 12), (9, 291, 17), (9, 290, 35), (0, 233, 20), (8, 206, 17), (10, 187, 42),
        ],
        [
            (3, -97, -6), (3, 89, 9), (6, 151, -4), (7, -91, -39), (7, -78, -16), (7, -56, 41), (7, 124, 1), (7, -118, 0),
            (9, -107, -37), (9, -127, 38), (9, -102, 21), (9, -67, 35), (9, -54, -14), (9, 12, 34), (9, 35, 39), (9, 31, 21),
            (9, 130, 35), (9, 185, 10), (0, -42, -3), (0, 75, 35), (0, 177, 42), (8, -126, -39), (10, -16, 37), (10, 148, 5),
            (1, -3, 45), (6, 160, 38), (7, 154, 16), (9, 188, 3), (9, 150, 37), (9, 214, 31), (9, 302, 16), (8, 200, 15),
        ],
        [
            (6, 103, 18), (7, -114, 4), (7, -142, 39), (7, 48, 23), (7, 88, 44), (9, -67, -30), (9, -134, -24), (9, -82, -1),
            (9, -45, 19), (9, -153, 46), (9, -8, 48), (9, 11, 24), (9, 64, 27), (9, 141, 38), (9, 154, 12), (0, -105, -20),
            (0, 18, 14), (0, 134, 20), (8, -153, -11), (10, -76, 64), (10, 33, 18), (1, -85, -28), (1, -154, 14), (1, 148, 21),
            (3, 198, 23), (6, 260, 14), (7, 238, 19), (9, 165, 14), (9, 221, 22), (0, 246, 16), (8, 221, 33), (1, 282, 22),
        ],
    ];

    /// <remarks>C: anLandingDamageDetailCounts, apLandingCanopyFrames, apszLandingDamageComments (globals.c).</remarks>
    private static readonly int[] LandingDamageDetailCounts = [0, 8, 16, 24];

    private static readonly sbyte[] LandingCanopyFramesLight = [0, 1, 2, 3, 11, 12, 13, 14, 14, 14, 14, 14, 14, 14, 13, 12, 11, 3, 2, 1, 0, 0x40, 0, 0];
    private static readonly sbyte[] LandingCanopyFramesModerate = [0, 15, 16, 17, 18, 19, 19, 18, 17, 16, 15, 0, 0x40, 0, 0, 0];
    private static readonly sbyte[] LandingCanopyFramesHeavy = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 10, 10, 10, 8, 7, 6, 5, 4, 3, 2, 1, 0, 0x40];
    private static readonly sbyte[][] LandingCanopyFrames =
        [LandingCanopyFramesLight, LandingCanopyFramesModerate, LandingCanopyFramesHeavy, LandingCanopyFramesHeavy];

    private static readonly string[] LandingDamageComments =
    [
        "You got away pretty clean, sir!",
        "Looks like it got a little hot out there, sir!",
        "You sure got yourself shot up, sir!",
        "Glad to see you made it back alive, sir.",
    ];

    /// <remarks>C: stSceneBuffer (320x128), pScrambleViewport, the nScramble*/pScramble* globals.</remarks>
    private readonly Viewport _sceneBuffer = new();
    private Viewport _scrambleViewport = new();
    private short _scrambleBackgroundRightX;
    private short _scrambleBackgroundY;
    private short _scrambleShipX;
    private short _scrambleShipY;
    private short _scrambleCockpitDetailX;
    private short _scrambleCockpitDetailY;
    private short _scrambleCockpitScale;
    private short _scrambleCanopyOffset;
    private short _scrambleCanopyFrame;
    private short _scrambleOverlayX = -1000;
    private short _scrambleOverlayY;
    private bool _scrambleCanopyClosed;
    private short _scrambleShipDetailCount;
    private readonly sbyte[] _scrambleShipDetailIndices = new sbyte[32];
    private ShapeTable? _scrambleCockpitShape;
    private ShapeTable? _scrambleBackgroundShape;
    private ShapeTable? _scrambleCanopyShape;
    private ShapeTable? _scrambleShipShape;
    private ShapeTable? _scrambleDetailShape;
    private ShapeTable? _scrambleOverlayShape;

    /// <remarks>C: stConversationTextContext over stConversationTextViewport.</remarks>
    private readonly TextContext _conversationTextContext = new();
    private readonly Viewport _conversationTextViewport = new();

    /// <summary>The fighter type the hangar scenes draw (the SM2 Dralthi uses the Hornet's, like its cockpit).</summary>
    private int ScrambleShipType
    {
        get
        {
            int type = Game.Session.State.PlayerShipType;
            return type is >= 0 and <= 3 ? type : 0;
        }
    }

    /// <summary>Black screen, screen rows 24..151, a black 320x128 scene buffer.</summary>
    /// <remarks>C: InitializeConversationViewport (0x427B20, main.c).</remarks>
    private void InitializeConversationViewport()
    {
        var wholeScreen = Screen.Clone();
        wholeScreen.SetViewportRect(0, 0, 319, 199);
        Gfx.ClearViewport(wholeScreen, PaletteColours.Black);
        Screen.Top = 24;
        Screen.Bottom = 151;
        _sceneBuffer.SetViewportRect(0, 0, 319, 127);
        _sceneBuffer.AllocateViewport(PaletteColours.Black);
    }

    /// <remarks>C: ResetScreenClipToFullHeight (0x427BA0, main.c).</remarks>
    private void ResetScreenClipToFullHeight()
    {
        _sceneBuffer.FreeViewport();
        Screen.Top = 0;
        Screen.Bottom = 199;
    }

    /// <summary>The scene buffer onto the screen (rows 24..151) on the next vertical blank.</summary>
    /// <remarks>C: RefreshMemoryStatusOverlay (0x427C30, main.c).</remarks>
    private async Task RefreshSceneBufferAsync()
    {
        await Display.WaitForVerticalBlankAsync();
        Gfx.CopyViewportContents(_sceneBuffer, Screen);
    }

    /// <summary>The subtitle area under the scene: font 0, centred, rows 152..199.</summary>
    /// <remarks>C: InitializeConversationText (0x427BC0, main.c).</remarks>
    private void InitializeConversationText()
    {
        _conversationTextViewport.CopyFrom(Game.DefaultText.Viewport ?? Screen);
        _conversationTextViewport.SetViewportRect(0, 152, 319, 199);
        _conversationTextContext.Viewport = _conversationTextViewport;
        _conversationTextContext.TextBuffer = DefaultTextBuffer;
        _conversationTextContext.Alignment = TextContext.AlignCentre;
        Gfx.InitializeTextContextFromFont(_conversationTextContext, 0, PaletteColours.ViewportClear, PaletteColours.Black);
        Gfx.SetTextContext(_conversationTextContext);
    }

    /// <remarks>C: ConfigureScrambleActor (0x407D90, brains.c).</remarks>
    private void ConfigureScrambleActor(short x, short y, short deltaX, short deltaY, ShapeTable? shape, short scale, short angle,
        sbyte flip, int index)
    {
        var actor = _scrambleActors[index];
        actor.X = x;
        actor.Y = y;
        actor.DeltaX = deltaX;
        actor.DeltaY = deltaY;
        actor.Shape = shape;
        if (actor.AnimationFrame != -1)
            actor.AnimationFrame = 0;
        actor.Scale = scale;
        actor.Angle = angle;
        actor.Flip = flip;
    }

    /// <summary>Advances an actor's frame script and position and draws it.</summary>
    /// <remarks>C: DrawScrambleActor (0x407C90, brains.c). RE-CHECK: the 0xA000 animation-state test compares a
    /// signed char and is never true, so the script always runs.</remarks>
    private void DrawScrambleActor(int index)
    {
        var actor = _scrambleActors[index];
        sbyte frame = 0;
        sbyte animationFrame = actor.AnimationFrame;
        if (animationFrame != -1 && actor.Animation is { } animation)
        {
            animationFrame++;
            int control;
            do
            {
                frame = (uint)animationFrame < (uint)animation.Length ? unchecked((sbyte)animation[animationFrame]) : (sbyte)0;
                control = (byte)frame & 0xc0;
                switch (control)
                {
                    case 0:
                        actor.AnimationFrame++;
                        break;
                    case 0x40:
                        actor.AnimationState = 0;
                        animationFrame--;
                        break;
                    case 0x80:
                        animationFrame = (sbyte)(frame & 0x3f);
                        actor.AnimationFrame = animationFrame;
                        break;
                }
            }
            while (control != 0);
        }
        actor.X = (short)(actor.DeltaX + actor.X);
        actor.Y = (short)(actor.DeltaY + actor.Y);
        Gfx.DrawSpriteScaled(_scrambleViewport, _scrambleBackgroundRightX + actor.X, _scrambleBackgroundY + actor.Y, actor.Shape,
            frame + actor.BaseFrame, actor.Angle, actor.Scale, actor.Flip);
    }

    /// <summary>One frame of the hangar scenes: hangar, actors, canopy, the fighter and its cockpit parts, damage
    /// details, the closed canopy; copied to the screen and presented (nothing on skipped frames).</summary>
    /// <remarks>C: DrawScrambleFrame (0x407E10, brains.c).</remarks>
    private async Task DrawScrambleFrameAsync()
    {
        var sim = Sim;
        sim.FrameSkipCounter--;
        if (sim.FrameSkipCounter > 0)
            return;
        sim.FrameSkipCounter = sim.FrameSkip;
        var scene = _sceneBuffer;
        Gfx.DrawSpriteDefault(scene, _scrambleBackgroundRightX - 1, _scrambleBackgroundY, _scrambleBackgroundShape, 0);
        Gfx.DrawSpriteDefault(scene, _scrambleBackgroundRightX, _scrambleBackgroundY, _scrambleBackgroundShape, 1);
        DrawScrambleActor(0);
        DrawScrambleActor(3);
        DrawScrambleActor(4);
        DrawScrambleActor(2);
        if (!_scrambleCanopyClosed)
            Gfx.DrawSpriteDefault(scene, _scrambleShipX + 40, _scrambleShipY - 40, _scrambleCanopyShape, _scrambleCanopyOffset);

        switch (ScrambleShipType)
        {
            case 0:
                Gfx.DrawSpriteDefault(scene, _scrambleShipX + 10, _scrambleShipY - 25, _scrambleShipShape, _scrambleCanopyFrame);
                Gfx.DrawSpriteDefault(scene, _scrambleShipX, _scrambleShipY, _scrambleCockpitShape, 0);
                Gfx.DrawSpriteScaled(scene, _scrambleCockpitDetailX, _scrambleCockpitDetailY, _scrambleCockpitShape, 1, 0,
                    _scrambleCockpitScale, 0);
                break;
            case 1:
                Gfx.DrawSpriteDefault(scene, _scrambleShipX + 10, _scrambleShipY - 16, _scrambleShipShape, _scrambleCanopyFrame);
                Gfx.DrawSpriteDefault(scene, _scrambleShipX, _scrambleShipY, _scrambleCockpitShape, 0);
                Gfx.DrawSpriteDefault(scene, _scrambleShipX + 153, _scrambleShipY + 5, _scrambleCockpitShape, 2);
                Gfx.DrawSpriteDefault(scene, _scrambleCockpitDetailX, _scrambleCockpitDetailY, _scrambleCockpitShape, 1);
                break;
            case 2:
                Gfx.DrawSpriteDefault(scene, _scrambleShipX + 10, _scrambleShipY - 15, _scrambleShipShape, _scrambleCanopyFrame);
                Gfx.DrawSpriteDefault(scene, _scrambleShipX, _scrambleShipY, _scrambleCockpitShape, 0);
                Gfx.DrawSpriteDefault(scene, _scrambleShipX + 148, _scrambleShipY, _scrambleCockpitShape, 2);
                Gfx.DrawSpriteDefault(scene, _scrambleCockpitDetailX, _scrambleCockpitDetailY, _scrambleCockpitShape, 1);
                break;
            case 3:
                Gfx.DrawSpriteDefault(scene, _scrambleShipX + 10, _scrambleShipY - 11, _scrambleShipShape, _scrambleCanopyFrame);
                Gfx.DrawSpriteDefault(scene, _scrambleShipX, _scrambleShipY, _scrambleCockpitShape, 0);
                Gfx.DrawSpriteDefault(scene, _scrambleShipX + 158, _scrambleShipY + 6, _scrambleCockpitShape, 2);
                Gfx.DrawSpriteDefault(scene, _scrambleCockpitDetailX, _scrambleCockpitDetailY, _scrambleCockpitShape, 1);
                break;
        }

        var details = ScrambleShipDetails[ScrambleShipType];
        for (int index = 0; index < _scrambleShipDetailCount; index++)
        {
            var detail = details[_scrambleShipDetailIndices[index]];
            Gfx.DrawSpriteDefault(scene, _scrambleShipX + detail.X, _scrambleShipY + detail.Y, _scrambleDetailShape, detail.Frame);
        }

        if (_scrambleCanopyClosed)
        {
            if (_scrambleOverlayX != -1000)
                Gfx.DrawSpriteDefault(scene, _scrambleOverlayX, _scrambleOverlayY, _scrambleOverlayShape, 0);
            Gfx.DrawSpriteDefault(scene, 100, 127, _scrambleCanopyShape, _scrambleCanopyOffset);
        }
        await RefreshSceneBufferAsync();
        await Display.PresentAsync();
    }

    /// <summary>
    /// The pilot climbs into his fighter in the hangar: 10 frames approach, 27 frames canopy
    /// opening, 23 frames boarding, then a short pause. Esc skips.
    /// </summary>
    /// <remarks>C: scramble (0x408200, brains.c); 16 fps.</remarks>
    public async Task ScrambleAsync()
    {
        var sim = Sim;
        var events = Events;
        _scrambleCanopyOffset = 0;
        _scrambleOverlayX = -1000;
        _scrambleCanopyFrame = 0;
        _scrambleShipDetailCount = 0;
        if (!events.EscapePressed)
        {
            InitializeConversationViewport();
            int shipType = ScrambleShipType;
            _scrambleCockpitShape = Shapes.Get(FlightShapes.CockpitFile(shipType), 8);
            _scrambleBackgroundShape = Shapes.Get(LogicalFile.ScrambleVga, 1);
            _scrambleCanopyClosed = false;
            _scrambleCanopyShape = Shapes.Get(LogicalFile.ScrambleVga, 2);
            _scrambleShipShape = Shapes.Get(LogicalFile.ScrambleVga, 3);
            var actorShape = Shapes.Get(LogicalFile.ScrambleVga, 4);
            _scrambleBackgroundY = 0;
            _scrambleViewport = _sceneBuffer;
            _scrambleBackgroundRightX = 64;
            ConfigureScrambleActor(130, 94, 1, 0, actorShape, 0x100, 0, 0, 0);
            ConfigureScrambleActor(160, 120, 0, 0, actorShape, 0x100, 0, 0, 2);
            ConfigureScrambleActor(260, 100, -3, 0, actorShape, 0xff, 0, 0x10, 3);
            ConfigureScrambleActor(260, 100, -3, 0, actorShape, 0xff, 0, 0x10, 4);
            Audio.PlaySfx(17);
            events.EscapePressed = false;
            switch (shipType)
            {
                case 0:
                    (_scrambleShipX, _scrambleShipY, _scrambleCockpitDetailX, _scrambleCockpitDetailY) = ((short)-40, (short)96, (short)-95, (short)71);
                    _scrambleCockpitScale = 316;
                    break;
                case 1:
                    (_scrambleShipX, _scrambleShipY, _scrambleCockpitDetailX, _scrambleCockpitDetailY) = ((short)-30, (short)80, (short)-15, (short)76);
                    break;
                case 2:
                    (_scrambleShipX, _scrambleShipY, _scrambleCockpitDetailX, _scrambleCockpitDetailY) = ((short)-40, (short)86, (short)4, (short)83);
                    break;
                case 3:
                    (_scrambleShipX, _scrambleShipY, _scrambleCockpitDetailX, _scrambleCockpitDetailY) = ((short)-40, (short)80, (short)-22, (short)67);
                    break;
            }

            sim.FrameSkipCounter = 1;
            for (int frame = 0; frame < 10; frame++)
            {
                events.PumpWindowMessages();
                await DrawScrambleFrameAsync();
                _scrambleBackgroundRightX--;
                _scrambleShipY -= 2;
                _scrambleCockpitDetailY -= 2;
                _scrambleShipX += 4;
                _scrambleCockpitDetailX += 4;
                if (events.EscapePressed)
                    break;
            }
            if (!events.EscapePressed)
            {
                sim.FrameSkipCounter = 1;
                for (int frame = 0; frame < 27; frame++)
                {
                    events.PumpWindowMessages();
                    await DrawScrambleFrameAsync();
                    _scrambleCanopyOffset++;
                    _scrambleShipX += 4;
                    _scrambleCockpitDetailX += 4;
                    _scrambleBackgroundRightX--;
                    if (_scrambleCanopyFrame < 25)
                        _scrambleCanopyFrame++;
                    if (events.EscapePressed)
                        break;
                }
            }
            if (!events.EscapePressed)
            {
                Audio.Sfx.FlushSoundEffectsAndLog();
                _scrambleCanopyOffset--;
                Audio.PlaySfx(15);
                sim.FrameSkipCounter = 1;
                for (int frame = 0; frame < 23; frame++)
                {
                    events.PumpWindowMessages();
                    if (frame == 22)
                        sim.FrameSkipCounter = 1;
                    await DrawScrambleFrameAsync();
                    switch (shipType)
                    {
                        case 0:
                            _scrambleCockpitScale += 2;
                            break;
                        case 1:
                        case 3:
                            _scrambleCockpitDetailX -= 2;
                            break;
                        case 2:
                            if (frame == 21)
                                _scrambleCockpitDetailY++;
                            else
                                _scrambleCockpitDetailX -= 2;
                            break;
                    }
                    if (_scrambleCanopyFrame < 35)
                        _scrambleCanopyFrame++;
                    if (events.EscapePressed)
                        break;
                }
                Audio.Sfx.FlushSoundEffectsAndLog();
                Audio.PlaySfx(16);
                if (!events.EscapePressed)
                    await events.WaitForSceneAdvanceAsync(60);
            }
            events.EscapePressed = false;
            Audio.Sfx.FlushSoundEffects();
            ResetScreenClipToFullHeight();
        }
        // The music of the hangar (0x1b) is only stopped here with nMemoryConfiguration 0; KS runs with 2.
    }

    /// <summary>
    /// Back in the hangar: the fighter is lowered (30 frames), the canopy opens with the damage
    /// details of the landing damage level (random positions), then the deck officer's comment.
    /// Esc skips.
    /// </summary>
    /// <remarks>C: landing (0x408650, brains.c); random draws RandomInRange(0, 31) per detail without repetition.</remarks>
    public async Task LandingAsync(int damageLevel)
    {
        var sim = Sim;
        var events = Events;
        damageLevel = Math.Clamp(damageLevel, 0, 3);
        Audio.Music.PreloadMusicTrackHook(0x1d);
        Audio.SpaceTrack(0x1d, 2, 1);
        events.Pump = null;
        InitializeConversationViewport();
        _scrambleShipDetailCount = (short)LandingDamageDetailCounts[damageLevel];
        for (int frame = 0; frame < _scrambleShipDetailCount; frame++)
        {
            sbyte detail;
            int prior;
            do
            {
                detail = (sbyte)sim.Random.InRange(0, 31);
                for (prior = 0; prior < frame; prior++)
                {
                    if (_scrambleShipDetailIndices[prior] == detail)
                        break;
                }
            }
            while (prior < frame);
            _scrambleShipDetailIndices[frame] = detail;
        }

        int shipType = ScrambleShipType;
        _scrambleCockpitShape = Shapes.Get(FlightShapes.CockpitFile(shipType), 8);
        _scrambleBackgroundShape = Shapes.Get(LogicalFile.ScrambleVga, 1);
        _scrambleShipShape = Shapes.Get(LogicalFile.ScrambleVga, 3);
        var actorShape = Shapes.Get(LogicalFile.ScrambleVga, 4);
        _scrambleDetailShape = Shapes.Get(LogicalFile.ScrambleVga, 9);
        _scrambleOverlayShape = Shapes.Get(LogicalFile.ScrambleVga, 5);
        _scrambleCanopyClosed = true;
        _scrambleCanopyShape = Shapes.Get(LogicalFile.ScrambleVga, 6);
        InitializeConversationText();
        _scrambleBackgroundY = 0;
        _scrambleViewport = _sceneBuffer;
        _scrambleBackgroundRightX = 32;
        ConfigureScrambleActor(140, 88, 2, 0, actorShape, 0x80, 0, 0, 3);
        ConfigureScrambleActor(139, 88, 2, 0, actorShape, 0x80, 0, 0, 4);
        ConfigureScrambleActor(240, 94, -1, 0, actorShape, 0x100, 0, 0x10, 0);
        ConfigureScrambleActor(160, 120, 0, 0, actorShape, 0x100, 0, 0, 2);
        _scrambleCanopyOffset = 0;
        _scrambleCanopyFrame = 34;
        _scrambleOverlayX = -1000;
        switch (shipType)
        {
            case 0:
                (_scrambleShipX, _scrambleShipY, _scrambleCockpitDetailX, _scrambleCockpitDetailY) = ((short)124, (short)140, (short)69, (short)115);
                _scrambleCockpitScale = 360;
                break;
            case 1:
                (_scrambleShipX, _scrambleShipY, _scrambleCockpitDetailX, _scrambleCockpitDetailY) = ((short)124, (short)130, (short)94, (short)125);
                break;
            case 2:
                (_scrambleShipX, _scrambleShipY, _scrambleCockpitDetailX, _scrambleCockpitDetailY) = ((short)124, (short)134, (short)124, (short)132);
                break;
            case 3:
                (_scrambleShipX, _scrambleShipY, _scrambleCockpitDetailX, _scrambleCockpitDetailY) = ((short)124, (short)126, (short)96, (short)113);
                break;
        }

        events.EscapePressed = false;
        Audio.PlaySfx(17);
        sim.FrameSkipCounter = 1;
        for (int frame = 0; frame < 30; frame++)
        {
            events.PumpWindowMessages();
            await DrawScrambleFrameAsync();
            _scrambleShipY -= 2;
            _scrambleCockpitDetailY -= 2;
            if (events.EscapePressed)
                break;
        }
        Audio.Sfx.FlushSoundEffectsAndLog();
        if (!events.EscapePressed)
        {
            _scrambleOverlayX = (short)(_scrambleShipX + 180);
            var canopyFrames = LandingCanopyFrames[damageLevel];
            int canopyIndex = 0;
            _scrambleOverlayY = (short)(_scrambleShipY + 50);
            Audio.PlaySfx(15);
            sim.FrameSkipCounter = 1;
            for (int frame = 0; frame < 30; frame++)
            {
                events.PumpWindowMessages();
                if (sim.RenderedSpaceFrame == 29)
                    sim.FrameSkipCounter = 1;
                await DrawScrambleFrameAsync();
                _scrambleOverlayY--;
                _scrambleOverlayX -= 4;
                switch (shipType)
                {
                    case 0:
                        _scrambleCockpitScale -= 2;
                        break;
                    case 1:
                    case 3:
                        _scrambleCockpitDetailX += 2;
                        break;
                    case 2:
                        if (frame == 0)
                            _scrambleCockpitDetailY--;
                        else
                            _scrambleCockpitDetailX += 2;
                        break;
                }
                if (frame > 6 && canopyIndex < canopyFrames.Length && canopyFrames[canopyIndex] != 0x40)
                {
                    _scrambleCanopyOffset = canopyFrames[canopyIndex];
                    canopyIndex++;
                }
                if (events.EscapePressed)
                    break;
            }
            Audio.Sfx.FlushSoundEffectsAndLog();
            if (!events.EscapePressed)
            {
                Gfx.ClearViewport(_conversationTextViewport, PaletteColours.Black);
                Gfx.SetTextContext(_conversationTextContext);
                Gfx.FormatTextBufferFromStart("%X%Y%F%s%P"u8, 0, 160, PaletteColours.Blue, LandingDamageComments[damageLevel]);
                await Display.PresentAsync();
                await events.WaitForSceneAdvanceAsync(300);
            }
        }
        events.EscapePressed = false;
        ResetScreenClipToFullHeight();
        Gfx.ClearViewport(_conversationTextViewport, PaletteColours.Black);
        Audio.Music.StopMusicUnlessSuppressed();
        Audio.Music.ReleaseMusicTrackHook(0x1d);
    }
}
