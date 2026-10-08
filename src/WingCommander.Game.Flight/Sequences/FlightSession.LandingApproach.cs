using WingCommander.Core.Resources;
using WingCommander.Graphics.Palettes;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

// The landing approach on the Tiger's Claw: sound.c ShowCarrierLaunchSequence (despite its name it
// shows the landing).
internal sealed partial class FlightSession
{
    /// <remarks>C: asCarrierLaunchApproachDeltaX[24] (0x0046A550).</remarks>
    private static readonly short[] CarrierLaunchApproachDeltaX =
        [-1, -1, -1, -2, -2, -2, -2, -3, -3, -3, -3, -3, -2, -2, -2, -2, -1, -1, -1, -1, -1, 0, 0, 0];

    /// <remarks>C: acCarrierLaunchApproachFrames[24] (0x0046A580).</remarks>
    private static readonly sbyte[] CarrierLaunchApproachFrames =
        [36, 32, 32, 32, 32, 32, 32, 32, 25, 25, 25, 25, 25, 25, 25, 25, 18, 18, 18, 18, 18, 18, 18, 18];

    /// <remarks>C: aCarrierLaunchFighterPath[9] (0x0046A598).</remarks>
    private static readonly (short X, short Y)[] CarrierLaunchFighterPath =
        [(-2, 1), (-1, 1), (-1, 1), (-1, 0), (-1, 1), (-1, 0), (-1, 1), (-1, 0), (0, 0)];

    /// <remarks>C: asCarrierLaunchFighterDeltaY[16] (0x0046A5BC).</remarks>
    private static readonly short[] CarrierLaunchFighterDeltaY = [0, 0, 3, 3, 3, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 1];

    /// <summary>The landing camera script from its third entry: copy the carrier's position and frame, turn,
    /// add a velocity, switch to view 15.</summary>
    /// <remarks>C: asCarrierLaunchViewData + 2 (0x0046A5DC).</remarks>
    private static readonly short[] CarrierLaunchViewData = [0, 0, 12, 11, 1, 90, 90, 0, 9, -90, 0, 0, 20, 3, 15, -1];

    /// <summary>
    /// The landing approach: 100 frames of the fighter (hand-placed sprite) gliding in front of the
    /// carrier (letterbox), then the deck: 35 frames of the carrier halves, the deck crew and the
    /// fighter shrinking, and up to 50 frames of the fighter on its landing path. Esc skips.
    /// </summary>
    /// <remarks>C: ShowCarrierLaunchSequence (0x42BC00, sound.c); 16 fps, one simulation tick per frame in
    /// the deck phases.</remarks>
    public async Task ShowCarrierLaunchSequenceAsync(short sceneObject)
    {
        var sim = Sim;
        var events = Events;
        IntroSceneResourcesActive = false;
        short carrierScreenX = 180;
        sim.FreeShip(1);
        sim.FreeShip(2);
        sim.FreeShip(3);
        sim.RemoveNavPointObjects();
        Audio.Sfx.ResetSoundState();
        Audio.Music.PreloadMusicTrackHook(0x1c);
        Audio.SpaceTrack(0x1c, 2, 1);
        var carrierShape = Shapes.Get(LogicalFile.ScrambleVga, 8);
        var actorShape = Shapes.Get(LogicalFile.ScrambleVga, 4);
        _scrambleViewport = SpaceBuffer;
        short obj = (uint)sceneObject < ObjectSlots.Count ? sceneObject : (short)1;
        var fighterShape = sim.TypeResources[(int)sim.Campaign.PlayerShipType].ShapeSet;
        short fighterScreenY = 64;
        short fighterScreenX = 20;
        sim.ScriptedViewObject = obj;
        sim.InitializeScriptedView(CarrierLaunchViewData, 2);
        _scrambleBackgroundY = 64;
        _scrambleBackgroundRightX = 520;
        ref var fighter = ref sim.Objects[ObjectSlots.Player];
        fighter.Flip = 0;
        fighter.ViewFrame = 36;
        fighter.ScreenAngle = 180;
        fighter.Distance = 300;
        fighter.Shape = fighterShape;
        ref var carrier = ref sim.Objects[obj];
        carrier.Shape = new ShapeRef(LogicalFile.ScrambleVga, 8);
        carrier.Flip = 0;
        carrier.ViewFrame = 3;
        events.EscapePressed = false;
        carrier.ScreenAngle = 0;
        carrier.ScreenScale = 0x100;
        carrier.Distance = 2000;
        sim.FrameSkipCounter = 1;
        short approachDistance = 20;
        short approachScale = 0;
        short frame = 0;
        do
        {
            events.PumpWindowMessages();
            sim.Objects[ObjectSlots.Player].Class = ObjectClass.Null;
            sim.Objects[obj].Class = ObjectClass.Null;
            sim.SetEyeDirectionAndPosition();
            sim.FrameSkipCounter--;
            if (sim.FrameSkipCounter < 1)
            {
                sim.FrameSkipCounter = sim.FrameSkip;
                sim.RenderedSpaceFrame++;
                Gfx.FlightPalette.UpdateSpacePaletteFade(Gfx.Palette);
                ClearViewBuffer();
                sim.HouseKeepObjects();
                sim.UpdateObjectsInSpace();
                sim.TransformObjectsToYourView();
                sim.UpdateStarField();
                if (frame < 24)
                {
                    fighterScreenY = (short)(fighterScreenY + CarrierLaunchApproachDeltaX[frame]);
                    sim.Objects[ObjectSlots.Player].ViewFrame = CarrierLaunchApproachFrames[frame];
                }
                else if (frame < 48)
                {
                    fighterScreenY = (short)(fighterScreenY - CarrierLaunchApproachDeltaX[47 - frame]);
                    sim.Objects[ObjectSlots.Player].ViewFrame = CarrierLaunchApproachFrames[47 - frame];
                }
                ref var player = ref sim.Objects[ObjectSlots.Player];
                player.Class = ObjectClass.Ship;
                approachScale = (short)(((uint)(ushort)player.Scale << 4) / (uint)approachDistance);
                sim.Objects[obj].Class = ObjectClass.Ship;
                player.ScreenX = (short)(fighterScreenX - sim.ViewCenterX);
                player.ScreenY = (short)(fighterScreenY - sim.ViewCenterY);
                player.ScreenScale = approachScale;
                sim.Objects[obj].ScreenX = (short)(_scrambleBackgroundRightX - sim.ViewCenterX);
                sim.Objects[obj].ScreenY = (short)(_scrambleBackgroundY - sim.ViewCenterY);
                sim.SortObjectDepth();
                BeginSpaceSpriteFrame();
                DrawSortedObjectsToBuffer();
                DumpBufferToScreen();
            }
            fighterScreenX += 2;
            sim.SpaceFrame++;
            _scrambleBackgroundRightX -= 2;
            approachDistance += 2;
            sim.Objects[ObjectSlots.Player].Distance += 10;
            ref var eye = ref sim.Objects[ObjectSlots.Eye];
            eye.Position = Simulation.Geometry.VectorMath.Add(eye.Position, eye.Velocity);
            if (events.EscapePressed)
                break;
            frame++;
            await Display.SettleDeferredPresentsAsync();
            await Display.PresentAsync();
        }
        while (frame < 100);

        sim.Objects[ObjectSlots.Player].Class = ObjectClass.Null;
        sim.Objects[obj].Class = ObjectClass.Null;
        if (!events.EscapePressed)
        {
            sim.CopyFrame(obj, ObjectSlots.Eye);
            ref var eye = ref sim.Objects[ObjectSlots.Eye];
            eye.Position = sim.Objects[obj].Position;
            _scrambleBackgroundRightX = 0;
            fighterScreenX = 200;
            fighterScreenY = 32;
            _scrambleBackgroundY = 0;
            eye.CollisionRadius = sim.Objects[obj].CollisionRadius;
            approachDistance = 100;
            ConfigureScrambleActor(100, 80, 1, 0, actorShape, 0x100, 0, 0, 0);
            ConfigureScrambleActor(116, 130, 0, 0, actorShape, 0x100, 0, 0, 1);
            ConfigureScrambleActor(300, 110, -4, 0, actorShape, 0xc0, 0, 0x10, 3);
            ConfigureScrambleActor(301, 110, -4, 0, actorShape, 0xc0, 0, 0x10, 4);
            Audio.PlaySfx(18);
            short actorX = 60;
            short actorY = 10;
            var fighterSprite = Shapes.Get(fighterShape);
            sim.FrameSkipCounter = 1;
            for (frame = 0; frame < 35; frame++)
            {
                events.PumpWindowMessages();
                sim.AlterYaw(-1, ObjectSlots.Eye);
                if (RefreshCockpitStatus())
                {
                    Gfx.DrawSpriteDefault(SpaceBuffer, _scrambleBackgroundRightX + 239, _scrambleBackgroundY, carrierShape, 0);
                    Gfx.DrawSpriteDefault(SpaceBuffer, _scrambleBackgroundRightX + 240, _scrambleBackgroundY, carrierShape, 1);
                    DrawScrambleActor(0);
                    approachScale = (short)(0x6000 / approachDistance);
                    Gfx.DrawSpriteScaled(SpaceBuffer, fighterScreenX, fighterScreenY, fighterSprite, 16, 0, approachScale, 0);
                    DrawScrambleActor(3);
                    DrawScrambleActor(4);
                    DrawScrambleActor(1);
                    Gfx.DrawSpriteDefault(SpaceBuffer, _scrambleBackgroundRightX + 60, _scrambleBackgroundY + 10, actorShape, 16);
                    Gfx.DrawSpriteDefault(SpaceBuffer, _scrambleBackgroundRightX + 80, _scrambleBackgroundY + 134, actorShape, 8);
                    Gfx.DrawSpriteDefault(SpaceBuffer, carrierScreenX, _scrambleBackgroundY, carrierShape, 2);
                    await Display.WaitForVerticalBlankAsync();
                    DumpBufferToScreen();
                }
                _scrambleBackgroundRightX += 2;
                carrierScreenX += 4;
                fighterScreenX -= 2;
                fighterScreenY++;
                approachDistance--;
                if (events.EscapePressed)
                    break;
                await Display.SettleDeferredPresentsAsync();
                await Display.PresentAsync();
            }

            if (!events.EscapePressed)
            {
                sim.FrameSkipCounter = 1;
                frame = 0;
                do
                {
                    events.PumpWindowMessages();
                    if (RefreshCockpitStatus())
                    {
                        Gfx.DrawSpriteDefault(SpaceBuffer, _scrambleBackgroundRightX + 239, _scrambleBackgroundY, carrierShape, 0);
                        Gfx.DrawSpriteDefault(SpaceBuffer, _scrambleBackgroundRightX + 240, _scrambleBackgroundY, carrierShape, 1);
                        DrawScrambleActor(0);
                        Gfx.DrawSpriteScaled(SpaceBuffer, fighterScreenX, fighterScreenY, fighterSprite, 16, 0, approachScale, 0);
                        DrawScrambleActor(3);
                        DrawScrambleActor(4);
                        DrawScrambleActor(1);
                        Gfx.DrawSpriteDefault(SpaceBuffer, _scrambleBackgroundRightX + actorX, _scrambleBackgroundY + actorY, actorShape, 16);
                        Gfx.DrawSpriteDefault(SpaceBuffer, _scrambleBackgroundRightX + 80, _scrambleBackgroundY + 134, actorShape, 8);
                        Gfx.DrawSpriteDefault(SpaceBuffer, carrierScreenX, _scrambleBackgroundY, carrierShape, 2);
                        await Display.WaitForVerticalBlankAsync();
                        DumpBufferToScreen();
                    }
                    frame++;
                    if (frame < 9)
                    {
                        fighterScreenX += CarrierLaunchFighterPath[frame].X;
                        fighterScreenY += CarrierLaunchFighterPath[frame].Y;
                    }
                    else if (frame < 23)
                    {
                        if (frame == 9)
                            Audio.PlaySfx(11);
                        fighterScreenY += CarrierLaunchFighterDeltaY[frame - 7];
                    }
                    if (frame == 23)
                    {
                        Audio.Sfx.FlushSoundEffectsAndLog();
                        Audio.PlaySfx(19);
                    }
                    actorX++;
                    if (frame % 7 == 0)
                        actorY--;
                    await Display.SettleDeferredPresentsAsync();
                    await Display.PresentAsync();
                }
                while (!events.EscapePressed && frame < 50);
            }
        }

        events.EscapePressed = false;
        Audio.Sfx.ResetSoundState();
        Audio.Music.StopMusicUnlessSuppressed();
        Audio.Music.ReleaseMusicTrackHook(0x1c);
        sim.FreeShip(0);
        sim.ScriptedView = false;
        IntroSceneResourcesActive = true;
        CancelSpaceSpriteFrame();
    }
}
