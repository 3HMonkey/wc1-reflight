using WingCommander.Core.Resources;
using WingCommander.Graphics.Cockpit;
using WingCommander.Graphics.Palettes;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

// The sequences after a flight: GameFlow's landing, ejection, stranded and death branches
// (nav.c), cmpgn.c ejection_sequence / stranded_sequence, screens.c death_sequence.
internal sealed partial class FlightSession
{
    /// <remarks>C: asEjectionPrimaryFrames / asEjectionSecondaryFrames (0x00465550 / 0x00465560).</remarks>
    private static readonly short[] EjectionPrimaryFrames = [0, 1, 1, 3, 3, 0, 0, 0];
    private static readonly short[] EjectionSecondaryFrames = [-1, -1, 2, -1, 4, 0, 0, 0];

    /// <summary>Follow the pilot 70 ticks, ride with him 80, then the chase camera.</summary>
    /// <remarks>C: asEjectionViewScript (0x00465570).</remarks>
    private static readonly short[] EjectionViewScript = [3, 11, 14, 70, 3, 10, 14, 80, 3, 4, -1, 0];

    private const string StrandedMessage = "\nWith your carrier\ndestroyed, you drift\nendlessly through\nthe void...";

    /// <summary>Landing: the approach on the carrier, then the hangar scene with the damage of the ship.</summary>
    /// <remarks>C: GameFlow (nav.c) after nArcadeState 1: free_cockpit, ShowCarrierLaunchSequence(nPlayerCollisionObject),
    /// nArcadeState = 0, nPlayerCollisionObject = -1, free_3Space, landing(calculate_damage_level()) — the damage level
    /// is read after free_3Space, which leaves slot 0's armour and damage counters intact.</remarks>
    public async Task LandingSequenceAsync()
    {
        var sim = Sim;
        FreeCockpit();
        await ShowCarrierLaunchSequenceAsync(sim.PlayerCollisionObject);
        sim.ArcadeState = 0;
        sim.PlayerCollisionObject = -1;
        sim.Free3Space();
        await LandingAsync(sim.CalculateDamageLevel());
    }

    /// <summary>Ejection, then the stranded test; when stranded the stranded sequence follows. True when stranded.</summary>
    /// <remarks>C: GameFlow after nArcadeState 2: ejection_sequence, check_stranded, stranded_sequence, free_3Space.</remarks>
    public async Task<bool> EjectionSequenceAsync()
    {
        var sim = Sim;
        await EjectionAsync();
        sim.CheckStranded();
        bool stranded = sim.ArcadeState == 3;
        if (stranded)
            await StrandedAsync();
        sim.Free3Space();
        if (!stranded)
            sim.ArcadeState = 0;
        return stranded;
    }

    /// <remarks>C: GameFlow after nArcadeState 3: stranded_sequence, free_3Space.</remarks>
    public async Task StrandedAfterFlightAsync()
    {
        await StrandedAsync();
        Sim.Free3Space();
    }

    /// <remarks>C: GameFlow after nArcadeState 4: death_sequence, free_3Space (the funeral is a Game scene).</remarks>
    public async Task DeathAfterFlightAsync()
    {
        await DeathAsync();
        Sim.Free3Space();
    }

    /// <summary>Fades the whole screen to black on the vertical blank, presents, and restores the palette later.</summary>
    /// <remarks>C: FadeViewportPaletteToColour (0x42A700, hudmsg.c).</remarks>
    private async Task FadeScreenToBlackAsync()
    {
        var fade = Gfx.BeginFadeViewportPaletteToColour(Screen, PaletteColours.Black);
        while (fade.Step())
            await Display.WaitForVerticalBlankAsync();
        await Display.PresentAsync();
    }

    /// <summary>
    /// The ejection: the seat shoots up through the rear view (10 frames), the canopy falls away in
    /// the pod view (10 frames), then the pod drifts away while the fighter explodes (200 ticks of
    /// the scripted camera). Fades to black.
    /// </summary>
    /// <remarks>C: ejection_sequence (0x4046A0, cmpgn.c); 16 fps.</remarks>
    public async Task EjectionAsync()
    {
        var sim = Sim;
        var events = Events;
        sim.FreeAllSlots();
        FreeCockpit();
        Audio.Music.PreloadMusicTrackHook(0x1f);
        Audio.SpaceTrack(0x1f, 2, 1);
        sim.NewView(9, 0);
        var background = Shapes.Get(CockpitFile, 3);
        var ejectionShape = Shapes.Get(LogicalFile.PilotAnimVga, 1);
        Audio.PlaySfx(0x21);
        short y = 199;
        events.EscapePressed = false;
        short descentSpeed = 4;
        sim.FrameSkipCounter = 1;
        short frame = 0;
        do
        {
            if (RefreshCockpitStatus())
            {
                Gfx.DrawSpriteDefault(SpaceBuffer, 0, 0, background, 0);
                int spriteFrame = Math.Min((int)frame, 4);
                Gfx.DrawSpriteDefault(SpaceBuffer, 160, y, ejectionShape, EjectionPrimaryFrames[spriteFrame]);
                if (EjectionSecondaryFrames[spriteFrame] != -1)
                    Gfx.DrawSpriteDefault(SpaceBuffer, 160, y, ejectionShape, EjectionSecondaryFrames[spriteFrame]);
                Gfx.DrawSpriteDefault(SpaceBuffer, 160, y + 1, ejectionShape, 5);
                DumpBufferToScreen();
            }
            if (frame > 1)
            {
                y = (short)(y - descentSpeed);
                descentSpeed = (short)Math.Min(descentSpeed + 4, 20);
            }
            if (events.EscapePressed)
                break;
            frame++;
            await Display.SettleDeferredPresentsAsync();
            await Display.PresentAsync();
        }
        while (frame < 10);

        GetScreenUpdateFlag();
        if (!events.EscapePressed)
        {
            var templates = Shapes.GetSection(LogicalFile.CockpitVga, 8);
            ScreenViewportPacket = templates.IsEmpty ? null : ViewGeometrySet.Parse("COCKPIT.VGA view templates", templates.Span);
            sim.TypeResources[(int)ObjectType.EjectedPilot].ShapeSet = sim.FetchShape(LogicalFile.PilotAnimVga, 2);
            short pilot = sim.FindVacant3dObject();
            sim.EjectedPilotObject = pilot;
            if (pilot != -1)
            {
                sim.SetObjectsData(pilot, ObjectType.EjectedPilot, -1);
                ref var pod = ref sim.Objects[pilot];
                pod.Counter = 32000;
                sim.CopyFrame(ObjectSlots.Player, pilot);
                pod.Position = sim.Objects[ObjectSlots.Player].Position;
                pod.Velocity = VectorMath.Add(VectorMath.Scale(pod.Up, -0x500), sim.Objects[ObjectSlots.Player].Velocity);
                sim.NewView(10, pilot);
            }

            background = Shapes.Get(CockpitFile, 0);
            ejectionShape = Shapes.Get(CockpitFile, 5);
            y = 40;
            frame = 0;
            Audio.PlaySfx(0x22);
            sim.FrameSkipCounter = 1;
            do
            {
                if (RefreshCockpitStatus())
                {
                    Gfx.DrawSpriteDefault(SpaceBuffer, 0, y, background, 0);
                    Gfx.DrawSpriteDefault(SpaceBuffer, 0, y - 1, ejectionShape, 0);
                    DumpBufferToScreen();
                }
                if (events.EscapePressed)
                    break;
                y = (short)(y + descentSpeed);
                frame++;
                await Display.SettleDeferredPresentsAsync();
                await Display.PresentAsync();
            }
            while (frame < 10);

            if (!events.EscapePressed && pilot != -1)
            {
                sim.LoadAllSlots();
                ref var eye = ref sim.Objects[ObjectSlots.Eye];
                ref readonly var player = ref sim.Objects[ObjectSlots.Player];
                eye.Forward = player.Up;
                eye.Right = player.Right;
                eye.Up = VectorMath.Negate(player.Forward);
                var viewOffset = VectorMath.Scale(sim.Objects[pilot].Up, -0x25800);
                eye.Position = VectorMath.Add(sim.Objects[pilot].Position, viewOffset);
                sim.ScriptedViewObject = pilot;
                sim.InitializeScriptedView(EjectionViewScript);
                frame = 0;
                sim.FrameSkipCounter = 1;
                Audio.Music.SetMusBreakpt(0, 0);
                while (true)
                {
                    sim.Objects[pilot].AlterPitch(4);
                    if (RefreshCockpitStatus())
                        DumpBufferToScreen();
                    if (frame == 10)
                    {
                        sim.Explosion(ObjectSlots.Player);
                        Audio.PlaySfx(4);
                    }
                    frame++;
                    if (frame > 200 || events.EscapePressed)
                        break;
                    await Display.SettleDeferredPresentsAsync();
                    await Display.PresentAsync();
                }
            }
        }

        events.EscapePressed = false;
        sim.ScriptedView = false;
        ScreenViewportPacket = null;
        CancelSpaceSpriteFrame();
        await FadeScreenToBlackAsync();
        await Display.ClearViewportAsync(Screen, PaletteColours.Black);
        await Display.PresentAsync();
        Gfx.Palette.RestoreGamePalette();
        sim.FreeAllSlots();
        Audio.Music.StopMusicUnlessSuppressed();
        Audio.Music.ReleaseMusicTrackHook(0x1f);
    }

    /// <summary>The camera pulls away from the stranded pilot toward the carrier's wreck: 400 ticks, the text from
    /// tick 160, "THE END" from tick 300. Fades to black.</summary>
    /// <remarks>C: stranded_sequence (0x404BE0, cmpgn.c); 16 fps.</remarks>
    public async Task StrandedAsync()
    {
        var sim = Sim;
        var events = Events;
        sim.CannedSceneMode = 1;
        FreeCockpit();
        sim.ForceView(13, 0);
        var introFont = Shapes.Get(LogicalFile.TitleVga, 1);
        short frame = 0;
        do
        {
            if (RefreshCockpitStatus())
            {
                if (introFont is not null)
                {
                    if (frame >= 300)
                        Gfx.PrintSubtitle(SpaceBuffer, introFont, "THE END"u8);
                    else if (frame >= 160)
                        Gfx.PrintSubtitle(SpaceBuffer, introFont, System.Text.Encoding.Latin1.GetBytes(StrandedMessage));
                }
                DumpBufferToScreen();
            }
            if (events.EscapePressed)
                break;
            frame++;
            await Display.SettleDeferredPresentsAsync();
            await Display.PresentAsync();
        }
        while (frame < 400);
        CancelSpaceSpriteFrame();
        sim.FreeAllSlots();
        Screen.Top = 0;
        Screen.Bottom = 199;
        await FadeScreenToBlackAsync();
        await Display.ClearViewportAsync(Screen, PaletteColours.Black);
        Gfx.Palette.RestoreGamePalette();
        events.EscapePressed = false;
    }

    /// <summary>The player dies: the rear view with the death animation (the last frame on white), then the chase
    /// camera on the ship as it explodes (60 ticks). Fades to black.</summary>
    /// <remarks>C: death_sequence (0x439660, screens.c); 16 fps.</remarks>
    public async Task DeathAsync()
    {
        var sim = Sim;
        var events = Events;
        sim.CannedSceneMode = 1;
        sim.FreeAllSlots();
        FreeCockpit();
        Audio.Music.StopMusicUnlessSuppressed();
        Audio.SpaceTrack(0x20, 2, 1);
        var deathShape = Shapes.Get(LogicalFile.PilotAnimVga, 0);
        var background = Shapes.Get(CockpitFile, 3);
        Audio.PlaySfx(4);
        sim.NewView(9, 0);
        events.EscapePressed = false;
        sim.FrameSkipCounter = 1;
        for (short frame = 0; frame < 8; frame++)
        {
            if (frame == 7)
            {
                FlashSpaceBuffer();
            }
            else
            {
                RefreshCockpitStatus();
                Gfx.DrawSpriteDefault(SpaceBuffer, 0, 0, background, 0);
            }
            Gfx.DrawSpriteDefault(SpaceBuffer, 160, 199, deathShape, frame);
            DumpBufferToScreen();
            await Display.SettleDeferredPresentsAsync();
            await Display.PresentAsync();
            ResetSpaceBufferBackground();
            if (events.EscapePressed)
                break;
        }

        GetScreenUpdateFlag();
        if (!events.EscapePressed)
        {
            short frame = 0;
            sim.LoadAllSlots();
            sim.NewView(4, 0);
            sim.FrameSkipCounter = 1;
            do
            {
                if (RefreshCockpitStatus())
                    DumpBufferToScreen();
                if (frame == 2)
                    sim.Explosion(ObjectSlots.Player);
                if (events.EscapePressed)
                    break;
                frame++;
                await Display.SettleDeferredPresentsAsync();
                await Display.PresentAsync();
            }
            while (frame < 60);
        }

        events.EscapePressed = false;
        CancelSpaceSpriteFrame();
        sim.FreeAllSlots();
        Screen.Top = 0;
        Screen.Bottom = 199;
        await FadeScreenToBlackAsync();
        await Display.ClearViewportAsync(Screen, PaletteColours.Black);
        Gfx.Palette.RestoreGamePalette();
    }
}
