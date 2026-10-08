using WingCommander.Game.Input;
using WingCommander.Graphics.Palettes;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;
using WingCommander.Game.Screens.Ui;

namespace WingCommander.Game.Flight;

// The key table of the flight: hudmsg.c HandleSpaceFlightControls.
internal sealed partial class FlightSession
{
    /// <summary>
    /// One tick of controls: input, flight dynamics, then the key of the tick. The first table
    /// (views, comm, autopilot, eject, orders, toggles) is skipped in the training simulator;
    /// the second always runs. Modal keys (pause, version banner, nav map, autopilot) await
    /// their screens. Returns -1 when the flight ends (Esc in the training simulator).
    /// </summary>
    /// <remarks>C: HandleSpaceFlightControls (0x429160, hudmsg.c). Random draws: Accelerate and the
    /// afterburner (malf 0), VDU keys (malf 3/4), Ctrl+E, view changes (stars), orders (simulation).</remarks>
    private async ValueTask<int> HandleSpaceFlightControlsAsync()
    {
        var sim = Sim;
        PlayerInput();
        sim.PlayersFlightDynamics(_pitchInput, _yawInput, _rollInput);
        bool notRepeated = (sbyte)_currentKey != _previousKey;
        bool control = Events.GetControlKeyState();
        Events.GetKeyboardModifiers();
        HandleFleetOverviewInput();

        bool restoreNormalViewport = false;
        if (!sim.TrainSimActive)
        {
            switch ((sbyte)_currentKey)
            {
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                case 7:
                case 8:
                case 9:
                case 10:
                    if (notRepeated && GetVduMode(1) == 4 && sim.CameraViewMode == 0 &&
                        (sbyte)_currentKey <= _commMenuChoiceCount + 2)
                        ChosenCommunicateOption((sbyte)_currentKey - 2);
                    break;
                case 0x12:
                    if (notRepeated && (control || (_currentInputModifiers & 0x2000) != 0))
                        sim.TryEject();
                    break;
                case 0x1e:
                    if (notRepeated)
                        await AutopilotKeyAsync();
                    break;
                case 0x1f:
                    if (control && notRepeated)
                    {
                        if (Audio.Sfx.FlightSoundEffectsEnabled)
                            Audio.Sfx.ResetSoundStateForScene();
                        else
                            Audio.Sfx.ResetSoundStateForFlight();
                    }
                    break;
                case 0x23:
                    if (notRepeated && sim.YourWingman != -1 &&
                        sim.Ships[sim.YourWingman].Objective != ShipObjective.HoldFormation)
                        sim.Request(ObjectSlots.Player, sim.YourWingman, CommCommand.FormOnMyWing);
                    break;
                case 0x2e:
                    if (notRepeated)
                    {
                        if (!MessageShowing())
                        {
                            if (GetVduMode(1) == 4)
                                CloseCommChoiceMenu();
                            else
                                SelectCockpitVduMode(1, 4);
                        }
                        else
                        {
                            EndCommMenu();
                        }
                    }
                    break;
                case 0x2f:
                    if (notRepeated && !control)
                    {
                        VideoImagesSuppressed = !VideoImagesSuppressed;
                        if (VideoImagesSuppressed)
                            SetHudMessageText(VideoSuppressedText, PaletteColours.Red, 20);
                        else
                            SetHudMessageText(VideoEnabledText, PaletteColours.PrimaryText, 20);
                    }
                    break;
                case 0x30:
                    if (notRepeated && sim.YourWingman != -1 &&
                        sim.Ships[sim.YourWingman].Objective == ShipObjective.HoldFormation &&
                        sim.AnyEnemy(ObjectSlots.Player, 14000))
                        sim.Request(ObjectSlots.Player, sim.YourWingman, CommCommand.BreakAndAttack);
                    break;
                case 0x31:
                    if (notRepeated)
                        await SelectCockpitVduModeAsync(1, 5);
                    InitPlayerInput();
                    break;
                case 0x32:
                    if (notRepeated && !control)
                        SetMessageDisplaySpeed();
                    break;
                case 0x3b:
                    if (Events.F1KeyLatch)
                    {
                        ToggleCockpitlessOrFrontView();
                        Events.FlushInputEvents();
                        Events.ClearDebugPauseFlags();
                        _mouseCursorVisible = false;
                        Events.PointerMovedByKeyboard = true;
                    }
                    break;
                case 0x3c:
                    restoreNormalViewport = ChangeView(2, 0, force: false);
                    break;
                case 0x3d:
                    restoreNormalViewport = ChangeView(1, 0, force: false);
                    break;
                case 0x3e:
                    restoreNormalViewport = ChangeView(3, 0, force: false);
                    break;
                case 0x3f:
                    restoreNormalViewport = ChangeView(4, 0, force: false);
                    break;
                case 0x40:
                    restoreNormalViewport = ChangeView(14, 0, force: false);
                    break;
                case 0x41:
                    if (sim.Ships[ObjectSlots.Player].Target != -1)
                        restoreNormalViewport = ChangeView(7, 0, force: false);
                    break;
                case 0x42:
                    if (notRepeated)
                    {
                        _mouseCursorVisible = false;
                        sim.MissileCameraEnabled = !sim.MissileCameraEnabled;
                        if (sim.MissileCameraEnabled)
                            SetHudMessageText(MissileCameraOnText, PaletteColours.Red, 20);
                        else
                            SetHudMessageText(MissileCameraOffText, PaletteColours.PrimaryText, 20);
                    }
                    break;
                case 0x43:
                    if (notRepeated)
                    {
                        _mouseCursorVisible = false;
                        SelectNextExternalViewObject();
                        restoreNormalViewport = ChangeView(4, sim.ViewObject, force: true);
                    }
                    break;
            }
        }
        if (restoreNormalViewport)
            EndCockpitlessViewChange();

        switch ((sbyte)_currentKey)
        {
            case 0x01:
                Events.EscapePressed = false;
                if (sim.TrainSimActive)
                {
                    if (!Options.EscapePausesFlight)
                        return -1;
                    return await ShowPauseMenuAsync(PauseMenuContext.TrainingSimulator) == PauseMenuChoice.EndSimulation ? -1 : 0;
                }
                if (GetVduMode(1) == 4)
                {
                    CloseCommChoiceMenu();
                    return 0;
                }
                if (Options.EscapePausesFlight)
                {
                    await ShowPauseMenuAsync(PauseMenuContext.Flight);
                    return 0;
                }
                break;
            case 0x0c:
            case 0x4a:
                if (control)
                {
                    // Developer frame skip only with the Origin switch (ADR-012); inert otherwise.
                    if (Game.Options.OriginDevUnlock)
                        ReportFramesSkipped(-1);
                    return 0;
                }
                sim.Accelerate(-1);
                return 0;
            case 0x0d:
            case 0x4e:
                if (control)
                {
                    // Developer frame skip only with the Origin switch (ADR-012); inert otherwise.
                    if (Game.Options.OriginDevUnlock)
                        ReportFramesSkipped(1);
                    return 0;
                }
                sim.Accelerate(1);
                return 0;
            case 0x0e:
                sim.ZeroPlayerSpeed();
                return 0;
            case 0x0f:
            case 0x37:
                sim.YourAfterburner();
                return 0;
            case 0x11:
                if (notRepeated)
                {
                    SelectCockpitVduMode(0, 1);
                    return 0;
                }
                break;
            case 0x14:
                if (notRepeated)
                {
                    SelectCockpitVduMode(1, 3);
                    return 0;
                }
                break;
            case 0x19:
                await ShowGamePausedBannerAsync(!control);
                return 0;
            case 0x1c:
                if (notRepeated && sim.SelectedReleaseWeaponIndex != -1)
                {
                    int previousView = sim.CameraViewMode;
                    sim.PlayerReleaseWeapon();
                    if (sim.CameraViewMode != previousView)
                        return 0;
                }
                break;
            case 0x1f:
                if (control && notRepeated)
                {
                    Game.Volumes.ToggleSfxVolume();
                    ShowOnScreenMessage($"SFX VOLUME: {Game.Volumes.SfxVolume / 2}.");
                    return 0;
                }
                break;
            case 0x20:
                if (notRepeated)
                {
                    SelectCockpitVduMode(0, 2);
                    return 0;
                }
                break;
            case 0x22:
                if (notRepeated)
                {
                    SelectCockpitVduMode(0, 1);
                    return 0;
                }
                break;
            case 0x24:
                // Ctrl+J: CalibrateJoystickInteractive; the joystick layer is not ported (request).
                break;
            case 0x26:
                if (!notRepeated)
                {
                    InitPlayerInput();
                    return 0;
                }
                sim.ToggleTargetLockMode();
                if (GetVduMode(1) == 3)
                {
                    InvalidateVduMode(1);
                    return 0;
                }
                break;
            case 0x2b:
                sim.Accelerate(9000);
                return 0;
            case 0x2f:
                if (notRepeated && control)
                {
                    await ShowVersionBannerAsync();
                    return 0;
                }
                break;
            case 0x32:
                if (notRepeated && control)
                {
                    Game.Volumes.ToggleMusicVolume();
                    ShowOnScreenMessage($"MUSIC VOLUME: {Game.Volumes.MusicVolume / 2}.");
                    return 0;
                }
                break;
            case 0x39:
                sim.FirePlayersLasers();
                return 0;
        }
        return 0;
    }

    /// <summary>
    /// F2..F7, F9: a camera view. In cockpitless mode the space buffer is resized to the geometry
    /// around the change; the caller then restores the 320x200 buffer.
    /// </summary>
    /// <returns>True when the cockpitless buffer must be restored afterwards.</returns>
    private bool ChangeView(int view, short obj, bool force)
    {
        var sim = Sim;
        _mouseCursorVisible = false;
        if (sim.CockpitlessView == 0)
        {
            if (force)
                sim.ForceView(view, obj);
            else
                sim.NewView(view, obj);
            return false;
        }
        BeginCockpitlessViewChange();
        if (force)
            sim.ForceView(view, obj);
        else
            sim.NewView(view, obj);
        return true;
    }

    /// <summary>F1: in the front cockpit view toggles the cockpitless view; in any other view returns to the cockpit.</summary>
    /// <remarks>C: the 0x3B case of HandleSpaceFlightControls (hudmsg.c).</remarks>
    private void ToggleCockpitlessOrFrontView()
    {
        var sim = Sim;
        if (ScreenViewportMode == 0)
        {
            sim.CockpitlessView = sim.CockpitlessView == 0 ? 1 : 0;
            GetScreenUpdateFlag();
            sbyte mode = ScreenViewportMode;
            if (sim.CockpitlessView != 0)
            {
                SetSpaceBufferSize(320, 200);
                ScreenViewportMode++;
                InitializeCockpit(mode);
                Events.SetMousePosition((SpaceBuffer.Right - SpaceBuffer.Left) / 2 + 1, sim.ViewCenterY);
            }
            else
            {
                SetSpaceBufferSize(sim.ScreenWidth, sim.ScreenHeight);
                ScreenViewportMode++;
                InitializeCockpit(mode);
                Events.SetMousePosition((SpaceBuffer.Right - SpaceBuffer.Left) / 2 + 1,
                    (SpaceBuffer.Bottom - SpaceBuffer.Top) / 2);
            }
        }
        else
        {
            _mouseCursorVisible = false;
            NewViewWithBuffer(0, 0);
        }
    }
}
