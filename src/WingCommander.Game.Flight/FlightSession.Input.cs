using WingCommander.Game.Flight.Cockpit;
using WingCommander.Game.Input;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

// Flight input of one tick: main.c player_input, process_player_input, init_player_input,
// get_player_input, HandleFleetOverviewInput, Select{Next,Previous}ExternalViewObject.
internal sealed partial class FlightSession
{
    /// <summary>The key code of this tick (bit 7 set = no new key).</summary>
    /// <remarks>C: bCurrentKey (0x80 initially).</remarks>
    private byte _currentKey = 0x80;

    /// <summary>Last tick's key code.</summary>
    /// <remarks>C: cPreviousKey ((signed char)0x80 initially).</remarks>
    private sbyte _previousKey = unchecked((sbyte)0x80);

    /// <remarks>C: nYawInput / nPitchInput / nRollInput (-10..10) and their previous values.</remarks>
    private short _yawInput;
    private short _pitchInput;
    private short _rollInput;
    private short _previousYawInput;
    private short _previousPitchInput;
    private short _previousRollInput;

    /// <remarks>C: wCurrentInputModifiers (the modifiers of the event polled first this tick).</remarks>
    private ushort _currentInputModifiers;

    /// <remarks>C: bFlightRollLatch, bMouseAfterburnerControl, bAfterburnerButtonLatched,
    /// dwLastSecondaryButtonPress, nMouseYawInput, nMousePitchInput.</remarks>
    private byte _flightRollLatch;
    private bool _mouseAfterburnerControl;
    private bool _afterburnerButtonLatched;
    private uint _lastSecondaryButtonPress;
    private short _mouseYawInput;
    private short _mousePitchInput;

    /// <remarks>C: stLastPolledFlightInput / stPreviousFlightInput (joystick samples), bInputPollingGuard.</remarks>
    private InputDeviceSample _lastPolledFlightInput;
    private InputDeviceSample _previousFlightInput;
    private byte _inputPollingGuard;

    /// <summary>Stick input of this tick (tests and the pilot hand).</summary>
    public (short Pitch, short Yaw, short Roll) StickInput => (_pitchInput, _yawInput, _rollInput);

    /// <summary>The key code dispatched this tick.</summary>
    public byte CurrentKey => _currentKey;

    /// <summary>Recentres the mouse pointer on the view and switches back to keyboard steering.</summary>
    /// <remarks>C: init_player_input (0x427DF0, main.c).</remarks>
    private void InitPlayerInput()
    {
        Events.SetMousePosition((SpaceBuffer.Right - SpaceBuffer.Left) / 2 + 1, Sim.ViewCenterY);
        Events.ClearDebugPauseFlags();
        _mouseCursorVisible = false;
        Events.PointerMovedByKeyboard = true;
    }

    /// <summary>The event-manager pump of the flight: queues joystick samples (the joystick layer is not
    /// ported yet, so no device is ever active).</summary>
    /// <remarks>C: get_player_input (0x427E40, main.c).</remarks>
    private void GetPlayerInput()
    {
        if (Events.ActiveInputDevice == -1 || _inputPollingGuard != 0)
            return;
        _inputPollingGuard++;
        var sample = Events.DeviceSamples[Events.ActiveInputDevice];
        if (sample.X == 0 && sample.Y == 0 && sample.Buttons == 0)
        {
            if (sample.X != _lastPolledFlightInput.X || sample.Y != _lastPolledFlightInput.Y ||
                sample.Buttons != _lastPolledFlightInput.Buttons)
            {
                Events.TranslatePolledInputEvent(InputEventType.JoystickSample, 0);
                _lastPolledFlightInput = sample;
            }
        }
        else
        {
            Events.TranslatePolledInputEvent(InputEventType.JoystickSample, 0);
            _lastPolledFlightInput = sample;
        }
        _inputPollingGuard--;
    }

    /// <summary>
    /// Keyboard steering of one key code: roll (comma/Ins, period/Del), pitch (Up/Down), yaw
    /// (Left/Right), the diagonals as two keys, KP5 centres. Inputs ramp by one per call up to
    /// ±9 (Shift jumps to 9); at 9 they oscillate unless the key just went down. Ctrl with the
    /// arrows changes the volumes (dead in practice: Ctrl reads released while an arrow is held).
    /// </summary>
    /// <remarks>C: process_player_input (0x427F20, main.c).</remarks>
    private void ProcessPlayerInput()
    {
        bool shift = Events.GetShiftKeyState();
        bool control = Events.GetControlKeyState();
        Span<short> keys = stackalloc short[3];
        switch ((sbyte)_currentKey)
        {
            case 0x47:
                keys[0] = 0x48;
                keys[1] = 0x4b;
                keys[2] = -1;
                break;
            case 0x49:
                keys[0] = 0x48;
                keys[1] = 0x4d;
                keys[2] = -1;
                break;
            case 0x4f:
                keys[0] = 0x50;
                keys[1] = 0x4b;
                keys[2] = -1;
                break;
            case 0x51:
                keys[0] = 0x50;
                keys[1] = 0x4d;
                keys[2] = -1;
                break;
            default:
                keys[0] = (sbyte)_currentKey;
                keys[1] = -1;
                break;
        }

        for (int index = 0; ; index++)
        {
            switch (keys[index])
            {
                case -1:
                    return;
                case 0x33:
                case 0x52:
                    _mouseCursorVisible = false;
                    if (_rollInput > 0)
                    {
                        _rollInput = 0;
                    }
                    else
                    {
                        if (shift)
                            _rollInput = -9;
                        if (_rollInput > -9 || _previousKey < 0)
                            _rollInput--;
                        else
                            _rollInput++;
                    }
                    break;
                case 0x34:
                case 0x53:
                    _mouseCursorVisible = false;
                    if (_rollInput < 0)
                    {
                        _rollInput = 0;
                    }
                    else
                    {
                        if (shift)
                            _rollInput = 9;
                        if (_rollInput < 9 || _previousKey < 0)
                            _rollInput++;
                        else
                            _rollInput--;
                    }
                    break;
                case 0x48:
                    _mouseCursorVisible = false;
                    if (_pitchInput < 0)
                    {
                        _pitchInput = 0;
                    }
                    else if (!control)
                    {
                        if (shift)
                            _pitchInput = 9;
                        if (_pitchInput < 9 || _previousKey < 0)
                            _pitchInput++;
                        else
                            _pitchInput--;
                    }
                    else
                    {
                        ChangeSfxVolume(+1);
                    }
                    break;
                case 0x4b:
                    if (control)
                    {
                        ChangeMusicVolume(-1);
                        return;
                    }
                    _mouseCursorVisible = false;
                    if (_yawInput > 0)
                    {
                        _yawInput = 0;
                    }
                    else
                    {
                        if (shift)
                            _yawInput = -9;
                        if (_yawInput > -9 || _previousKey < 0)
                            _yawInput--;
                        else
                            _yawInput++;
                    }
                    break;
                case 0x4c:
                    Events.WarpMouseTo((short)((SpaceBuffer.Left + SpaceBuffer.Right) / 2),
                        (short)((SpaceBuffer.Top + SpaceBuffer.Bottom) / 2));
                    _rollInput = 0;
                    _pitchInput = 0;
                    _yawInput = 0;
                    InitPlayerInput();
                    break;
                case 0x4d:
                    if (control)
                    {
                        ChangeMusicVolume(+1);
                        return;
                    }
                    _mouseCursorVisible = false;
                    if (_yawInput < 0)
                    {
                        _yawInput = 0;
                    }
                    else
                    {
                        if (shift)
                            _yawInput = 9;
                        if (_yawInput < 9 || _previousKey < 0)
                            _yawInput++;
                        else
                            _yawInput--;
                    }
                    break;
                case 0x50:
                    _mouseCursorVisible = false;
                    if (_pitchInput > 0)
                    {
                        _pitchInput = 0;
                    }
                    else if (!control)
                    {
                        if (shift)
                            _pitchInput = -9;
                        if (_pitchInput > -9 || _previousKey < 0)
                            _pitchInput--;
                        else
                            _pitchInput++;
                    }
                    else
                    {
                        ChangeSfxVolume(-1);
                    }
                    break;
            }
        }
    }

    /// <summary>Ctrl+Up/Down: sound-effect volume in steps of one (0..20), saved, with an on-screen message.</summary>
    private void ChangeSfxVolume(int step)
    {
        var volumes = Game.Volumes;
        volumes.SfxVolume = Math.Clamp(volumes.SfxVolume + step, 0, 20);
        Game.SaveSettings();
        ShowOnScreenMessage($"SFX VOLUME: {volumes.SfxVolume / 2}.");
    }

    /// <summary>Ctrl+Left/Right: music volume in steps of one (0..20), saved, with an on-screen message.</summary>
    private void ChangeMusicVolume(int step)
    {
        var volumes = Game.Volumes;
        volumes.MusicVolume = Math.Clamp(volumes.MusicVolume + step, 0, 20);
        Game.SaveSettings();
        ShowOnScreenMessage($"MUSIC VOLUME: {volumes.MusicVolume / 2}.");
    }

    /// <summary>
    /// The input of one tick: pops the head event (it is lost unless it is a mouse or joystick
    /// sample, which is re-queued), polls the held steering keys, handles held mouse buttons, then
    /// drains the queue: buttons (left = guns, both = release weapon, right double click =
    /// afterburner), keys (the last one of the tick wins), joystick samples and mouse moves
    /// (steering buckets, edges, right-button throttle).
    /// </summary>
    /// <remarks>C: player_input (0x4285D0, main.c).</remarks>
    private void PlayerInput()
    {
        var sim = Sim;
        var events = Events;
        _previousKey = (sbyte)_currentKey;
        _previousYawInput = _yawInput;
        _previousPitchInput = _pitchInput;
        _previousRollInput = _rollInput;
        bool keyboardRoll = false;
        var e = default(InputEventState);
        short type = events.PollInputEvent(ref e);
        short modifiers = e.Modifiers;
        _currentInputModifiers = unchecked((ushort)modifiers);
        events.TranslatePolledInputEvent(type, e.Value);
        bool mouseButtonEventQueued = events.IsInputEventQueued(InputEventType.ButtonDown);
        _currentKey |= 0x80;

        if (!_mouseCursorVisible)
        {
            _currentKey = (byte)events.PollKeyboardState();
            if (_currentKey == 0)
            {
                _rollInput = 0;
                _flightRollLatch = 0;
                _pitchInput = 0;
                _yawInput = 0;
            }
            else
            {
                _mouseAfterburnerControl = false;
                ProcessPlayerInput();
                if (_currentKey is 0x33 or 0x34 or 0x52 or 0x53)
                    keyboardRoll = true;
            }
        }

        if (!mouseButtonEventQueued)
        {
            int buttons = events.HostSecondaryMouseButton * 2 | events.HostPrimaryMouseButton;
            if (buttons == 0)
            {
                _afterburnerButtonLatched = false;
            }
            else
            {
                if (buttons == 3)
                {
                    _currentKey = 0x1c;
                }
                else if (buttons == 1)
                {
                    _currentKey = 0x39;
                    sim.FirePlayersLasers();
                }
                if ((buttons & 2) == 0)
                    _previousKey = 0;
                if (_previousKey == 0x0f && buttons == 2)
                    _currentKey = 0x0f;
                if (buttons == 1)
                    sim.FirePlayersLasers();
            }
        }

        while ((type = events.GetNextInputEvent(ref e)) != 0)
        {
            switch (type)
            {
                case InputEventType.ButtonDown:
                {
                    short value = (short)e.Value;
                    if (value == 1)
                    {
                        _currentKey = 0x39;
                        if ((e.Modifiers & 4) != 0)
                        {
                            if (sim.Ships[ObjectSlots.Player].SpecialManeuver == SpecialManeuver.Afterburner)
                                sim.FirePlayersLasers();
                            else
                                _currentKey = 0x1c;
                        }
                    }
                    if (value == 3)
                        _currentKey = 0x1c;
                    if (value == 2 && !_afterburnerButtonLatched)
                    {
                        if ((int)(Game.Timing.Ticks60Hz - _lastSecondaryButtonPress) <= events.InputTickScale)
                            _currentKey = 0x0f;
                        _afterburnerButtonLatched = true;
                    }
                    if (_previousKey == 0x0f && value == 2)
                        _currentKey = 0x0f;
                    if (value == 1)
                        sim.FirePlayersLasers();
                    _lastSecondaryButtonPress = Game.Timing.Ticks60Hz;
                    break;
                }
                case InputEventType.KeyDown:
                case InputEventType.Character:
                    _mouseAfterburnerControl = false;
                    _currentInputModifiers = unchecked((ushort)e.Modifiers);
                    _currentKey = unchecked((byte)e.Value);
                    ProcessPlayerInput();
                    break;
                case InputEventType.JoystickSample:
                    HandleJoystickSample(keyboardRoll);
                    break;
                case InputEventType.MouseMove:
                    HandleMouseMove(ref e, modifiers);
                    break;
            }
        }
        _previousFlightInput = _lastPolledFlightInput;
    }

    /// <remarks>C: the type-6 case of player_input (the joystick layer that queues samples is not ported).</remarks>
    private void HandleJoystickSample(bool keyboardRoll)
    {
        var sim = Sim;
        _mouseAfterburnerControl = false;
        _mouseCursorVisible = false;
        bool afterburning = sim.Ships[ObjectSlots.Player].SpecialManeuver == SpecialManeuver.Afterburner;
        if ((_lastPolledFlightInput.Buttons & 3) == 3)
        {
            if (afterburning)
                sim.FirePlayersLasers();
            else
                _currentKey = 0x1c;
        }
        else if ((_lastPolledFlightInput.Buttons & 1) != 0)
        {
            sim.FirePlayersLasers();
        }
        uint secondButton = (_lastPolledFlightInput.Buttons & 2) >> 1;
        if (secondButton != 0 && afterburning)
            secondButton = 0;
        if (secondButton != 0)
        {
            _rollInput = (short)_lastPolledFlightInput.X;
            sim.Accelerate((short)-(_lastPolledFlightInput.Y / 2));
        }
        else
        {
            if (_rollInput != 0 && _flightRollLatch == 0 && !keyboardRoll)
            {
                _previousFlightInput.X = -1;
                _rollInput = 0;
            }
            if (_previousFlightInput.X != _lastPolledFlightInput.X || _previousFlightInput.Y != _lastPolledFlightInput.Y ||
                _lastPolledFlightInput.X != 0 || _lastPolledFlightInput.Y != 0)
            {
                _pitchInput = (short)-_lastPolledFlightInput.Y;
                _yawInput = (short)_lastPolledFlightInput.X;
            }
        }
        if (_previousKey == 0x0f && (_lastPolledFlightInput.Buttons & 2) != 0)
            _currentKey = 0x0f;
    }

    /// <summary>Mouse steering: buckets of the offset from the view centre, edges give full deflection,
    /// the right button turns the mouse into roll and throttle.</summary>
    /// <remarks>C: the type-13 case of player_input (0x4285D0, main.c).</remarks>
    private void HandleMouseMove(ref InputEventState e, short modifiers)
    {
        var sim = Sim;
        bool afterburnerControl = (ushort)(modifiers & 4) >= 1;
        if (afterburnerControl && sim.Ships[ObjectSlots.Player].SpecialManeuver == SpecialManeuver.Afterburner)
            afterburnerControl = false;
        if (!_mouseCursorVisible)
        {
            _mouseCursorVisible = true;
            Events.Cursor.Frame = 2;
        }
        var buffer = SpaceBuffer;
        short horizontal;
        short vertical;
        if (sim.CockpitlessView == 0)
        {
            horizontal = (short)(e.X + (buffer.Left - buffer.Right) / 2 + 1);
            vertical = (short)(e.Y + (buffer.Top - buffer.Bottom) / 2);
        }
        else
        {
            if (sim.CockpitView == 0)
                e.Y = (short)(e.Y - 10);
            else if (sim.CockpitView == 1)
                e.Y = (short)(e.Y - 25);
            horizontal = (short)(e.X + (buffer.Left - buffer.Right) / 2 + 1);
            vertical = (short)(e.Y - sim.ViewCenterY);
        }
        Events.Cursor.X = e.X;
        Events.Cursor.Y = e.Y;
        short yawInput = 0;
        while (CockpitTables.MouseYawThresholds[yawInput] <= Math.Abs((int)horizontal))
            yawInput++;
        if (horizontal < 0)
            yawInput = (short)-yawInput;
        short pitchInput = 0;
        while (CockpitTables.MousePitchThresholds[pitchInput] <= Math.Abs((int)vertical))
            pitchInput++;
        if (vertical < 0)
            pitchInput = (short)-pitchInput;
        int viewportLeft = buffer.Left;
        if (e.X - 4 <= viewportLeft)
            yawInput = -8;
        if (buffer.Right <= e.X + 4)
            yawInput = 8;
        if (e.Y - 4 <= buffer.Top)
            pitchInput = -8;
        if (buffer.Bottom <= e.Y + 4)
            pitchInput = 8;
        yawInput = Math.Clamp(yawInput, (short)-8, (short)8);
        pitchInput = Math.Clamp(pitchInput, (short)-8, (short)8);
        if (afterburnerControl)
        {
            _mouseAfterburnerControl = true;
            pitchInput = (short)-pitchInput;
            _mouseYawInput = yawInput;
            _rollInput = yawInput;
            _mousePitchInput = pitchInput;
            sim.Accelerate((short)(pitchInput / 2));
        }
        else if (_mouseAfterburnerControl)
        {
            _rollInput = 0;
            _mouseAfterburnerControl = false;
            _mouseYawInput = 0;
            _yawInput = 0;
            _mousePitchInput = 0;
            _pitchInput = 0;
            Events.WarpMouseTo((short)((viewportLeft + buffer.Right) / 2), (short)((buffer.Bottom + buffer.Top) / 2));
        }
        else
        {
            _rollInput = 0;
            _mouseYawInput = yawInput;
            _yawInput = yawInput;
            _mousePitchInput = pitchInput;
            _pitchInput = pitchInput;
        }
    }

    /// <summary>The capital-ship chase camera (view 8): Enter steps the view object back, Home/End change
    /// the distance, the arrows rotate the eye, Ins resets it. Handled keys become 0.</summary>
    /// <remarks>C: HandleFleetOverviewInput (0x428D10, main.c).</remarks>
    private void HandleFleetOverviewInput()
    {
        var sim = Sim;
        sbyte key = (sbyte)_currentKey;
        if (sim.CameraViewMode != 8)
            return;
        _currentKey = 0;
        ref var eye = ref sim.Objects[ObjectSlots.Eye];
        switch (key)
        {
            case 0x1c:
                sim.ViewObject--;
                _currentKey = 0x29;
                break;
            case 0x47:
                sim.CapitalShipViewDistance -= 0x3200;
                break;
            case 0x48:
                VectorMath.RotateAboutI(-7, ref eye.Up, ref eye.Forward);
                break;
            case 0x4b:
                VectorMath.RotateAboutJ(7, ref eye.Right, ref eye.Forward);
                break;
            case 0x4d:
                VectorMath.RotateAboutJ(-7, ref eye.Right, ref eye.Forward);
                break;
            case 0x4f:
                sim.CapitalShipViewDistance += 0x3200;
                break;
            case 0x50:
                VectorMath.RotateAboutI(7, ref eye.Up, ref eye.Forward);
                break;
            case 0x52:
                eye.Up.Z = -0x100;
                eye.Forward.Y = 0x100;
                eye.Right.X = 0x100;
                eye.Forward.Z = 0;
                eye.Forward.X = 0;
                eye.Up.Y = 0;
                eye.Up.X = 0;
                eye.Right.Z = 0;
                eye.Right.Y = 0;
                break;
            default:
                _currentKey = (byte)key;
                break;
        }
    }

    /// <summary>The next slot 0..9 holding a ship (wrapping).</summary>
    /// <remarks>C: SelectNextExternalViewObject (0x428C90, main.c).</remarks>
    private void SelectNextExternalViewObject()
    {
        var sim = Sim;
        short obj = sim.ViewObject;
        sim.ViewObject = -1;
        do
        {
            obj++;
            if (obj > 9)
                obj = 0;
            if (sim.Objects[obj].Class >= ObjectClass.Ship)
                sim.ViewObject = (sbyte)obj;
        }
        while (sim.ViewObject == -1);
    }
}
