using WingCommander.Core.Numerics;
using WingCommander.Game.Input;
using WingCommander.Graphics.Palettes;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

// The flight loop: hudmsg.c RunSpaceFlight, RenderSpaceViewFrame, Draw_3Space_Frame,
// RefreshCockpitStatus; cockpt.c update_cockpit.
internal sealed partial class FlightSession
{
    /// <summary>Counts the space frames presented by the flight loop (tests).</summary>
    public int PresentedSpaceFrames { get; private set; }

    /// <summary>Called after every present of the flight loop (tests and tools capture frames here).</summary>
    public Action<FlightSession>? FramePresented { get; set; }

    /// <remarks>C: the previous nSystemKeyDown, for the Alt+N/M developer keys.</remarks>
    private int _previousSystemKey;

    /// <summary>
    /// Flies until the arcade state changes: entry (§1.2 of the analysis), the per-tick loop
    /// (controls, simulation tick, space view, cockpit, present at 20 fps) and the exit.
    /// Returns the arcade state: 1 landed, 2 ejected, 3 stranded, 4 killed, 5 aborted (Esc in the
    /// training simulator).
    /// </summary>
    /// <remarks>C: RunSpaceFlight (0x42A190, hudmsg.c).</remarks>
    public async Task<int> RunSpaceFlightAsync(short entryNavPoint)
    {
        var sim = Sim;
        var events = Events;
        sim.CockpitlessView = 0;
        if (!sim.TrainSimActive && !Game.Options.CockpitEnabled)
            sim.CockpitlessView = 1;
        sim.FrameSkipCounter = 1;
        events.InputMode = 1;
        events.SetMouseGrab(true);
        events.Pump = GetPlayerInput;
        var savedCursorViewport = Game.Cursor.Viewport;
        Game.Cursor.Viewport = SpaceBuffer;
        Audio.AttachSoundWorld();
        Audio.Music.InitInflightMusic();

        if (entryNavPoint == -1)
            entryNavPoint = sim.MissionShips[sim.PlayerMissionShipIndex].NavPoint;
        sim.SetUpActionSphere(entryNavPoint);

        if (sim.CockpitlessView != 0)
        {
            GetScreenUpdateFlag();
            SetSpaceBufferSize(sim.ScreenWidth, sim.ScreenHeight);
            InitializeViewBuffer();
            sim.NewView(0, 0);
            GetScreenUpdateFlag();
            SetSpaceBufferSize(320, 200);
            sbyte savedMode = ScreenViewportMode;
            ScreenViewportMode++;
            InitializeCockpit(savedMode);
            events.SetMousePosition((SpaceBuffer.Right - SpaceBuffer.Left) / 2 + 1, sim.ViewCenterY);
            _mouseAfterburnerControl = false;
            _mouseCursorVisible = false;
            InitializeViewBuffer();
            events.FlushInputEvents();
        }

        sim.CopyFrame(ObjectSlots.Player, ObjectSlots.SavedPlayerFrame);
        events.WarpMouseTo((short)((SpaceBuffer.Left + SpaceBuffer.Right) / 2), (short)((SpaceBuffer.Top + SpaceBuffer.Bottom) / 2));
        events.FlushInputEvents();
        _mouseAfterburnerControl = false;
        _mouseCursorVisible = false;
        sim.ArcadeState = 0;
        await Display.PresentAsync();
        Game.Timing.SetSpaceFlightFrameTiming();
        events.FlushInputEvents();
        events.ClearDebugPauseFlags();
        _mouseCursorVisible = false;
        events.PointerMovedByKeyboard = true;
        _previousSystemKey = events.SystemKeyDown;
        bool frameReady = true;
        ShowFlightKeyHelp();
        events.KeyTranslationActive = true;

        try
        {
            while (sim.ArcadeState == 0)
            {
                if (await HandleSpaceFlightControlsAsync() == -1)
                {
                    sim.ArcadeState = 5;
                    break;
                }
                CheckDeveloperFrameRateKeys();
                if (sim.ArcadeState == 0)
                {
                    sim.Update3Space();
                    frameReady = RenderSpaceViewFrame();
                    UpdateCockpit();
                }
                await Display.SettleDeferredPresentsAsync();
                if (frameReady)
                {
                    frameReady = false;
                    await PresentSpaceFrameAsync();
                }
            }
        }
        finally
        {
            events.KeyTranslationActive = false;
            Game.HideKeyHelp();
            ExitSpaceFlight(savedCursorViewport);
        }
        return sim.ArcadeState;
    }

    /// <summary>The key help of this flight with the player's keys (port addition, ADR-013, ADR-016).</summary>
    private void ShowFlightKeyHelp() =>
        Game.ShowKeyHelp(FlightKeyHelp.Title,
            FlightKeyHelp.For(Sim.TrainSimActive, Options.EscapePausesFlight, Game.Preferences.Controls));

    /// <summary>The flight's present (DIBslam + DIBslamReal: present, then the 50 ms throttle).</summary>
    private async Task PresentSpaceFrameAsync()
    {
        await Display.PresentAsync();
        PresentedSpaceFrames++;
        FramePresented?.Invoke(this);
    }

    /// <remarks>C: the tail of RunSpaceFlight (0x42A190, hudmsg.c). The SDL port stops the DOS sound effects
    /// (SdlStopDosSoundEffects); the port stops them through the sound-effect manager.</remarks>
    private void ExitSpaceFlight(Graphics.Raster.Viewport? savedCursorViewport)
    {
        var sim = Sim;
        var events = Events;
        events.SetMouseGrab(false);
        CancelSpaceSpriteFrame();
        Audio.Sfx.StopAllSounds();
        Game.Timing.SetCinematicFrameTiming();
        SetSpaceBufferSize(sim.ScreenWidth, sim.ScreenHeight);
        sim.CockpitlessView = 0;
        if (sim.ArcadeState == 1)
        {
            short objective = sim.FindObjective(1, -1);
            if (objective != -1)
                sim.FlagObjective(objective, 2);
        }
        ResetCockpitPaletteEntries();
        if (savedCursorViewport is not null)
            Game.Cursor.Viewport = savedCursorViewport;
        Audio.Music.FreeInflightMusic();
        Audio.DetachSoundWorld();
        events.Pump = null;
        _mouseCursorVisible = false;
        events.QueueInputEvent(InputEventType.MouseMove, 160, 100, 0, 0, 0);
        Game.Cursor.SetShape(Game.Cursor.Shape, 0);
    }

    /// <summary>Alt+N / Alt+M change the flight frame rate (8..32 fps) — developer keys behind the Origin switch (ADR-012).</summary>
    /// <remarks>C: the WM_SYSKEYDOWN 'N'/'M' branches of MainWindowProc (winmain.c) → ReportSpaceFlightMaxFps (dib.c).</remarks>
    private void CheckDeveloperFrameRateKeys()
    {
        int systemKey = Events.SystemKeyDown;
        if (systemKey == _previousSystemKey)
            return;
        _previousSystemKey = systemKey;
        if (!Game.Options.OriginDevUnlock || (systemKey != 'N' && systemKey != 'M'))
            return;
        string text = Game.Timing.AdjustSpaceFlightMaxFps(systemKey == 'N' ? -0.5f : 0.5f);
        _spaceFlightFpsMessage.Value = text;
        SetHudMessageText(_spaceFlightFpsMessage, PaletteColours.Red, 0x14);
    }

    /// <remarks>C: szSpaceFlightMaxFpsMessage.</remarks>
    private readonly HudText _spaceFlightFpsMessage = new("");

    /// <summary>
    /// The space view of a tick: the palette fade, then on rendered frames the projection
    /// (simulation), the sprites and the HUD.
    /// </summary>
    /// <returns>False on a skipped frame.</returns>
    /// <remarks>C: Draw_3Space_Frame (0x429DD0, hudmsg.c); the simulation half is PrepareSpaceView
    /// (including target_locking, the first statement of overlay_head_up_display).</remarks>
    private bool Draw3SpaceFrame()
    {
        Gfx.FlightPalette.UpdateSpacePaletteFade(Gfx.Palette);
        if (!Sim.PrepareSpaceView())
            return false;
        BeginSpaceSpriteFrame();
        DrawSortedObjectsToBuffer();
        if (Sim.CameraViewMode == 0)
            OverlayHeadUpDisplay();
        return true;
    }

    /// <summary>
    /// A rendered frame of the flight loop: space view and HUD, HUD message timer, simulator score,
    /// the dump onto the screen, the HUD clean-up, the simulator's wave bonus, and the buffer
    /// cleared for the next frame.
    /// </summary>
    /// <remarks>C: RenderSpaceViewFrame (0x429FC0, hudmsg.c).</remarks>
    private bool RenderSpaceViewFrame()
    {
        var sim = Sim;
        if (!Draw3SpaceFrame())
            return false;
        CheckMessage();
        UpdateArcadeScoreDisplay();
        RestoreCockpitExplosionIfVisible();
        DumpBufferToScreen();
        if (sim.CameraViewMode == 0)
            RestoreTransientCockpitGraphics();
        if (sim.CockpitlessView == 0 && sim.TrainSimActive)
        {
            Gfx.DrawFilledViewportRect(SpaceBuffer, 10, 10, SpaceBuffer.Right, 0x11, PaletteColours.PrimaryViewBuffer);
            var arcade = Game.Screens.TrainSim;
            if (arcade.ArcadeBonusCountdown != 0)
            {
                arcade.ArcadeBonusCountdown--;
                if (arcade.ArcadeBonusCountdown == 0)
                {
                    ref var player = ref sim.Objects[ObjectSlots.Player];
                    if (player.Position.Magnitude() > 0x271000)
                        player.Position = FixedVector.Zero;
                    sim.ArcadeScore += _arcadeWaveBonus;
                    if (sim.CurrentWave == -1)
                        sim.ArcadeState = 1;
                    else
                        arcade.ArcadeWave++;
                    Gfx.ClearViewport(SpaceBuffer, PaletteColours.PrimaryViewBuffer);
                }
            }
        }
        Gfx.ClearViewport(SpaceBuffer, PaletteColours.PrimaryViewBuffer);
        ResetSpaceBufferBackground();
        return true;
    }

    /// <summary>
    /// One tick and a space view for the scripted sequences: the buffer is cleared before the
    /// drawing; dump and present are left to the caller.
    /// </summary>
    /// <returns>False on a skipped frame.</returns>
    /// <remarks>C: RefreshCockpitStatus (0x42A0C0, hudmsg.c).</remarks>
    public bool RefreshCockpitStatus()
    {
        Sim.Update3Space();
        if (Sim.FrameSkipCounter <= 1)
        {
            ClearViewBuffer();
            ResetSpaceBufferBackground();
        }
        return Draw3SpaceFrame();
    }

    /// <summary>
    /// The cockpit of a tick: targeting and repairs (simulation), then in the front view the
    /// lights, missile warning, scanner, readouts, VDUs, pilot hand, bars, cockpit explosion and
    /// comm chatter; the weapon launch animation; the stranded check.
    /// </summary>
    /// <remarks>C: update_cockpit (0x417E70, cockpt.c).</remarks>
    private void UpdateCockpit()
    {
        var sim = Sim;
        sim.UpdateCockpitSimulation();
        if (sim.CameraViewMode == 0)
        {
            bool cockpitless = sim.CockpitlessView != 0;
            if (!cockpitless)
                RestoreCockpitExplosionBackground();
            UpdateLights();
            UpdateMissileWarning();
            Draw3dScanner();
            UpdateDigitalReadouts();
            UpdateVdus();
            if (!cockpitless)
                AnimatePilot();
            UpdateBars();
            DrawCockpitLights();
            if (!cockpitless)
                CockpitExplosion();
            NpcCommunication();
        }
        FireComputerGraphicMissile();
        sim.CheckStranded();
    }

    /// <summary>Whether the autopilot is available: light 4 and the A key.</summary>
    /// <remarks>C: auto_pilot_valid (0x414380, cockpt.c), the simulation's.</remarks>
    private bool AutoPilotValid(bool showReason) => Sim.AutoPilotValid(showReason);
}
