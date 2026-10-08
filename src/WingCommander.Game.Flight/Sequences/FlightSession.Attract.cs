using WingCommander.Core.Resources;
using WingCommander.Graphics.Shapes;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

// The attract mode of the title: the first half of nav.c Title_Sequence, DrawTitleLogo.
internal sealed partial class FlightSession
{
    /// <summary>Eye at (-1000, 0, -4263), velocity 15, pitch 30, view 15 (letterbox), pitch goal -30, wait.</summary>
    /// <remarks>C: asIntroCameraSequence[20] (0x0046C090).</remarks>
    private static readonly short[] IntroCameraSequence = [0, -1000, 0, -4263, 2, 15, 1, 0, 30, 0, 3, 15, 4, 30, 1, 13, 14, 400, -1, 0];

    private const string IntroOpeningText = "In the distant future,\nmankind is locked in a deadly war...";

    /// <summary>The 11 DOS credit cards followed by the 8 Kilrathi Saga cards.</summary>
    /// <remarks>C: apszIntroCredits[20] (0x00468A38); the 20th entry is a null terminator.</remarks>
    private static readonly string[] IntroCredits =
    [
        "Design\nby\nChris Roberts",
        "Software Engineers\nChris Roberts\nKen Demarest III\nPaul C. Isaac\nSteve Muchow\nHerman Miller\nSteve Beeman\n",
        "Dogfight Intelligence\nKen Demarest III\n\nDogfight Choreography\nSteve Beeman\nErin Roberts",
        "3Space System\nby\nChris Roberts\n\nOriginFX Graphic System\nChris Roberts\nJohn Miles",
        "OriginFX Sound System\nby\nHerman Miller",
        "Artwork\nDenis Loubet\nGlen Johnson\nDaniel Bourbonnais\nKeith Berdak\nJohn Watson",
        "Screenplay by Jeff George\n\nAdditional Writing\nSteve Cantrell\nPhilip Brogden",
        "Soundtrack by\nGeorge A. Sanger and Dave Govett",
        "Sound Effects by Marc Schaefgen",
        "Produced by\nChris Roberts and Warren Spector",
        "Directed by\nChris Roberts",
        "Windows 95 Team",
        "Combat Programmers\n\nJeff Mangler Everett\nJeff jefftep Grills\nChuck Bishop Karpiak\nKris Goblin Pelley",
        "Sound System\n\nRichard Cupcake Lyle",
        "Soundtrack Rescored by\n\nI Need Names",
        "Head Whiner\n\nAnthony Sommers",
        "Whiners\n\nMonte Mathis\nHal Milton\nDieter Martin",
        "Richard Zinser\nKanon Lillemon\n",
        "Special Thanks To\n\nSocks\nand\nCaffeine",
    ];

    /// <remarks>C: nIntroCreditCount (11 initially).</remarks>
    private int _introCreditCount = 11;

    /// <summary>Credit cards shown by the attract mode (tests).</summary>
    public int AttractCreditCount => _introCreditCount;

    /// <summary>Presented frames of the last attract run (tests).</summary>
    public int AttractFrames { get; private set; }

    /// <summary>The title logo zooming in: three parts scaled by 0x1000 / distance around the view's centre column.</summary>
    /// <remarks>C: DrawTitleLogo (0x40FA40, nav.c).</remarks>
    private void DrawTitleLogo(ShapeTable? titleShape, short distance, short y)
    {
        if (distance <= 10)
            return;
        short scale = (short)(0x1000 / distance);
        short centreX = (short)(Sim.ScreenWidth >> 1);
        Span<short> bounds = stackalloc short[4];
        bounds.Clear();
        ShapeBounds.GetTransformedShapeBounds(SpaceBuffer, centreX, y, titleShape, 1, 0, scale, 0, bounds);
        if (!TryRecordSpaceSprite(titleShape, 0, bounds[0] - 1, y, 0, scale, 0, -1))
            Gfx.DrawSpriteScaled(SpaceBuffer, bounds[0] - 1, y, titleShape, 0, 0, scale, 0);
        if (!TryRecordSpaceSprite(titleShape, 1, centreX, y, 0, scale, 0, -1))
            Gfx.DrawSpriteScaled(SpaceBuffer, centreX, y, titleShape, 1, 0, scale, 0);
        if (!TryRecordSpaceSprite(titleShape, 2, bounds[2], y, 0, scale, 0, -1))
            Gfx.DrawSpriteScaled(SpaceBuffer, bounds[2], y, titleShape, 2, 0, scale, 0);
    }

    private async Task PresentAttractFrameAsync()
    {
        await Display.SettleDeferredPresentsAsync();
        await Display.PresentAsync();
        AttractFrames++;
    }

    /// <summary>
    /// The attract mode until a key, a button or Esc: the canned dogfight (action sphere 16) with
    /// the opening text, the title logo zooming in, then the asteroid flight (sphere 17) with the
    /// credits; repeats.
    /// </summary>
    /// <remarks>C: the first half of Title_Sequence (0x40FB70, nav.c), 16 fps. The Kilrathi Saga adds 9 to
    /// the credit count on every call with the Saga option (reading past the table); the port shows 19
    /// cards computed once (<see cref="FlightOptions.FixedCreditCount"/>) or clamps the literal count.</remarks>
    public async Task PlayAttractSequenceAsync()
    {
        var sim = Sim;
        var events = Events;
        AttractFrames = 0;
        if (Game.Options.ShowKilrathiSagaCredits)
        {
            if (Options.FixedCreditCount)
                _introCreditCount = IntroCredits.Length;
            else
                _introCreditCount += 9;
        }
        if (events.EscapePressed)
            return;
        int creditCount = Math.Min(_introCreditCount, IntroCredits.Length);

        Audio.Music.PreloadMusicTrackHook(0x17);
        events.Pump = null;
        IntroSceneResourcesActive = false;
        PrepareCampaignData(trainingSimulator: false);
        Init3SpaceObjects(0);
        sim.CannedSceneMode = 2;
        var introFont = Shapes.Get(LogicalFile.TitleVga, 1);
        // aIntroResourceDescriptors: explosion 3:2 and metal debris 3:5 (wing debris shares it).
        sim.TypeResources[(int)ObjectType.Explosion1].ShapeSet = sim.FetchShape(LogicalFile.ObjectsVga, 2);
        sim.TypeResources[(int)ObjectType.DebrisMetalSheet].ShapeSet = sim.FetchShape(LogicalFile.ObjectsVga, 5);
        sim.TypeResources[(int)ObjectType.DebrisWing].ShapeSet = sim.TypeResources[(int)ObjectType.DebrisMetalSheet].ShapeSet;
        events.ClearInputKeyStatePreservingModifiers();
        events.FlushInputEvents();
        events.EscapePressed = false;
        var openingText = System.Text.Encoding.Latin1.GetBytes(IntroOpeningText);

        bool escaped = false;
        try
        {
            while (!escaped)
            {
                events.PumpWindowMessages();
                for (int missionShip = 32; missionShip < 46; missionShip++)
                    sim.MissionShips[missionShip].State = 0;
                short titleDistance = 200;
                sim.RemoveAllHazards();
                sim.IntroSecondaryScene = false;
                sim.SetUpActionSphere(16);
                var titleShape = Shapes.Get(LogicalFile.TitleVga, 0);
                Audio.SpaceTrack(0x17, 2, 1);
                sim.InitializeScriptedView(IntroCameraSequence);
                sim.FrameSkipCounter = 1;

                for (int frame = 0; frame < 25; frame++)
                {
                    sim.Update3Space();
                    if (Draw3SpaceFrame())
                    {
                        if (introFont is not null)
                            Gfx.PrintSubtitle(SpaceBuffer, introFont, openingText);
                        DumpBufferToScreen();
                        await PresentAttractFrameAsync();
                        IntroDrawBackgroundShips();
                        if (events.CheckEscaped() != 0)
                        {
                            escaped = true;
                            break;
                        }
                    }
                }
                ClearViewBuffer();
                if (escaped)
                    break;

                for (int frame = 0; frame < 110; frame++)
                {
                    sim.Update3Space();
                    RenderSpaceViewFrame();
                    if (events.CheckEscaped() != 0)
                    {
                        escaped = true;
                        break;
                    }
                    await PresentAttractFrameAsync();
                }
                if (escaped)
                    break;

                for (int frame = 0; frame < 100; frame++)
                {
                    sim.Update3Space();
                    if (Draw3SpaceFrame())
                    {
                        DrawTitleLogo(titleShape, titleDistance, (short)(sim.ViewCenterY - 6));
                        DumpBufferToScreen();
                        await PresentAttractFrameAsync();
                        ClearViewBuffer();
                    }
                    if (titleDistance > 16)
                        titleDistance -= 4;
                    if (events.CheckEscaped() != 0)
                    {
                        escaped = true;
                        break;
                    }
                }
                if (escaped)
                    break;

                ref var eye = ref sim.Objects[ObjectSlots.Eye];
                eye.Velocity = VectorMath.Scale(eye.Forward, 0x9600);
                sim.SetUpActionSphere(17);
                sim.IntroSecondaryScene = true;
                ref var player = ref sim.Objects[ObjectSlots.Player];
                player.PitchRotation = 0;
                player.YawRotation = 0;
                player.RollRotation = 0;
                sim.StartHazardField(0);

                for (int credit = 0; credit < creditCount && !escaped; credit++)
                {
                    var cardText = System.Text.Encoding.Latin1.GetBytes(IntroCredits[credit]);
                    for (int frame = 0; frame < 70; frame++)
                    {
                        sim.Update3Space();
                        if (Draw3SpaceFrame())
                        {
                            if (introFont is not null)
                                Gfx.PrintSubtitle(SpaceBuffer, introFont, cardText);
                            DumpBufferToScreen();
                            await PresentAttractFrameAsync();
                            ClearViewBuffer();
                        }
                        if (events.CheckEscaped() != 0)
                        {
                            escaped = true;
                            break;
                        }
                    }
                    if (escaped)
                        break;
                    for (int frame = 0; frame < 40; frame++)
                    {
                        sim.Update3Space();
                        RenderSpaceViewFrame();
                        await PresentAttractFrameAsync();
                        if (events.CheckEscaped() != 0)
                        {
                            escaped = true;
                            break;
                        }
                    }
                }
                if (escaped)
                    break;

                for (int frame = 0; frame < 150; frame++)
                {
                    sim.Update3Space();
                    RenderSpaceViewFrame();
                    await PresentAttractFrameAsync();
                    if (events.CheckEscaped() != 0)
                    {
                        escaped = true;
                        break;
                    }
                }
            }
        }
        finally
        {
            CancelSpaceSpriteFrame();
            Audio.Music.StopMusicUnlessSuppressed();
            Audio.Sfx.ResetSoundState();
            sim.TypeResources[(int)ObjectType.Explosion1].ShapeSet = ShapeRef.None;
            sim.TypeResources[(int)ObjectType.DebrisMetalSheet].ShapeSet = ShapeRef.None;
            sim.TypeResources[(int)ObjectType.DebrisWing].ShapeSet = ShapeRef.None;
            sim.FreeAllSlots();
            sim.Free3Space();
            sim.IntroSecondaryScene = false;
            sim.CannedSceneMode = 0;
            sim.ScriptedView = false;
            IntroSceneResourcesActive = true;
            Audio.Music.ReleaseMusicTrackHook(0x17);
        }
    }
}
