using WingCommander.Core.Resources;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Game.Flight;

// Launch and autopilot: sound.c LaunchPlayerShip / DrawLaunchDoorFrame, auto.c visit_the_cinema and
// the UI half of auto_pilot_sequence (the travel itself is the simulation's).
internal sealed partial class FlightSession
{
    /// <summary>Draws the three parts of the launch door (SCRAMBLE.VGA section 7) scaled by 0x1A00 / distance.</summary>
    /// <remarks>C: DrawLaunchDoorFrame (0x42B9A0, sound.c).</remarks>
    private void DrawLaunchDoorFrame(ShapeTable? door, short distance)
    {
        if (distance <= 10)
            return;
        short scale = (short)(0x1a00 / distance);
        short centreX = (short)(Sim.ScreenWidth >> 1);
        short centreY = (short)(Sim.ScreenHeight >> 1);
        Span<short> bounds = stackalloc short[4];
        bounds.Clear();
        ShapeBounds.GetTransformedShapeBounds(SpaceBuffer, centreX, centreY, door, 1, 0, scale, 0, bounds);
        if (!TryRecordSpaceSprite(door, 0, bounds[0] - 1, centreY, 0, scale, 0, -1))
            Gfx.DrawSpriteScaled(SpaceBuffer, bounds[0] - 1, centreY, door, 0, 0, scale, 0);
        if (!TryRecordSpaceSprite(door, 1, centreX, centreY, 0, scale, 0, -1))
            Gfx.DrawSpriteScaled(SpaceBuffer, centreX, centreY, door, 1, 0, scale, 0);
        if (!TryRecordSpaceSprite(door, 2, bounds[2], centreY, 0, scale, 0, -1))
            Gfx.DrawSpriteScaled(SpaceBuffer, bounds[2], centreY, door, 2, 0, scale, 0);
    }

    /// <summary>
    /// The launch from the Tiger's Claw: mission music, the cockpit view, then 25 ticks in which the
    /// four launch-door frames rush past (faster every 5 frames) while the cockpit comes alive.
    /// Esc skips (and restarts the music).
    /// </summary>
    /// <remarks>C: LaunchPlayerShip (0x42BA90, sound.c); 16 fps cinematic pacing, one tick per present.</remarks>
    public async Task LaunchPlayerShipAsync()
    {
        var sim = Sim;
        var events = Events;
        short[] doorDistances = [50, 40, 30, 20];
        sbyte distanceStep = 1;
        Audio.SpaceTrack(Audio.ChangeTrack(), 1, 0);
        if (!events.EscapePressed)
        {
            var door = Shapes.Get(LogicalFile.ScrambleVga, 7);
            sim.CannedSceneMode = 1;
            sim.ForceView(0, 0);
            Audio.PlaySfx(20);
            sim.FrameSkipCounter = 1;
            events.EscapePressed = false;
            for (sbyte frame = 0; frame < 25; frame++)
            {
                events.PumpWindowMessages();
                if (RefreshCockpitStatus())
                {
                    for (int index = 0; index < 4; index++)
                    {
                        DrawLaunchDoorFrame(door, doorDistances[index]);
                        doorDistances[index] = (short)(doorDistances[index] - distanceStep);
                    }
                    DumpBufferToScreen();
                    UpdateCockpit();
                }
                await Display.SettleDeferredPresentsAsync();
                await Display.PresentAsync();
                if (events.EscapePressed)
                    break;
                if (frame % 5 == 0)
                    distanceStep++;
            }
            if (events.EscapePressed)
            {
                Audio.Music.StopMusicUnlessSuppressed();
                Audio.SpaceTrack(Audio.ChangeTrack(), 1, 0);
            }
        }
        else
        {
            sim.ForceView(0, 0);
        }
        await Display.SettleDeferredPresentsAsync();
        await Display.PresentAsync();
        ClearViewBuffer();
        sim.CannedSceneMode = 0;
        Audio.Sfx.ResetSoundState();
        events.EscapePressed = false;
    }

    /// <summary>
    /// A cinematic inside the flight: the player is invulnerable without collision response while
    /// <paramref name="frames"/> ticks are flown and presented with camera <paramref name="view"/>.
    /// </summary>
    /// <remarks>C: visit_the_cinema (0x403E50, auto.c); runs at the flight's 20 fps.</remarks>
    private async Task VisitTheCinemaAsync(int view, short obj, short frames)
    {
        var sim = Sim;
        bool savedOriginUnlock = Game.Options.OriginDevUnlock;
        bool savedVulnerable = sim.PlayerVulnerable;
        bool savedCollisionResponse = sim.PlayerCollisionResponse;
        Game.Options.OriginDevUnlock = true;
        sim.PlayerVulnerable = false;
        sim.PlayerCollisionResponse = false;
        sim.ForceView(view, obj);
        while (frames-- > 0)
        {
            sim.Update3Space();
            RenderSpaceViewFrame();
            await Display.SettleDeferredPresentsAsync();
            await PresentSpaceFrameAsync();
        }
        Game.Options.OriginDevUnlock = savedOriginUnlock;
        sim.PlayerVulnerable = savedVulnerable;
        sim.PlayerCollisionResponse = savedCollisionResponse;
    }

    /// <summary>
    /// The autopilot: the simulation checks and prepares the trip, the UI shows the 120-tick
    /// fly-by (camera 12), the simulation jumps to the destination and sets up the arrival, then
    /// the cockpit view returns.
    /// </summary>
    /// <remarks>C: auto_pilot_sequence (0x404050, auto.c), split per ADR-012.</remarks>
    private async Task AutoPilotSequenceAsync()
    {
        var sim = Sim;
        if (!sim.BeginAutopilot())
            return;
        await VisitTheCinemaAsync(12, 0, 120);
        sim.AutopilotTravel();
        sim.EndAutopilot();
        if (sim.CockpitlessView == 0)
        {
            sim.ForceView(0, 0);
            Events.SetMousePosition((SpaceBuffer.Right - SpaceBuffer.Left) / 2 + 1, (SpaceBuffer.Bottom - SpaceBuffer.Top) / 2);
        }
        else
        {
            GetScreenUpdateFlag();
            SetSpaceBufferSize(sim.ScreenWidth, sim.ScreenHeight);
            InitializeViewBuffer();
            sim.CockpitlessView = 1;
            sim.ForceView(0, 0);
            sim.CockpitlessView = 1;
            GetScreenUpdateFlag();
            SetSpaceBufferSize(320, 200);
            InitializeViewBuffer();
            Events.SetMousePosition((SpaceBuffer.Right - SpaceBuffer.Left) / 2, sim.ViewCenterY);
        }
    }

    /// <summary>The A key: navigation on the right VDU, the autopilot (in cockpitless mode with the buffer at
    /// the geometry size and the cockpitless flag at -2), then the input is flushed.</summary>
    /// <remarks>C: the 0x1E case of HandleSpaceFlightControls (hudmsg.c).</remarks>
    private async ValueTask AutopilotKeyAsync()
    {
        var sim = Sim;
        _mouseCursorVisible = false;
        if (GetVduMode(1) != 5)
            SelectCockpitVduMode(1, 5);
        if (sim.CockpitlessView == 0)
        {
            await AutoPilotSequenceAsync();
        }
        else
        {
            GetScreenUpdateFlag();
            SetSpaceBufferSize(sim.ScreenWidth, sim.ScreenHeight);
            InitializeViewBuffer();
            sim.CockpitlessView = -2;
            await AutoPilotSequenceAsync();
            sim.CockpitlessView = 1;
            GetScreenUpdateFlag();
            SetSpaceBufferSize(320, 200);
            InitializeViewBuffer();
        }
        Events.FlushInputEvents();
        Events.ClearDebugPauseFlags();
        _mouseCursorVisible = false;
        Events.PointerMovedByKeyboard = true;
    }
}
