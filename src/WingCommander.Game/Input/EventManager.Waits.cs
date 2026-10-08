namespace WingCommander.Game.Input;

public sealed partial class EventManager
{
    /// <summary>
    /// Pumps once and reports whether the player wants to skip: a queued button (type 10/2) or
    /// key-down (3) yields that event's modifier word + 1 (non-zero); key-downs also drain the
    /// queue. Any hit flushes the queue. Does not wait.
    /// </summary>
    /// <remarks>C: CheckEscaped (0x41DA10, disk.c).</remarks>
    public short CheckEscaped()
    {
        var state = new InputEventState();
        PumpWindowMessages();
        short escaped = 0;
        if (IsInputEventQueued(InputEventType.JoystickButton))
        {
            PeekInputEvent(ref state, InputEventType.JoystickButton);
            escaped = unchecked((short)((short)state.Value + 1));
        }
        else if (IsInputEventQueued(InputEventType.ButtonDown))
        {
            PeekInputEvent(ref state, InputEventType.ButtonDown);
            escaped = unchecked((short)((short)state.Value + 1));
        }
        else if (IsInputEventQueued(InputEventType.KeyDown))
        {
            PeekInputEvent(ref state, InputEventType.KeyDown);
            escaped = unchecked((short)((short)state.Value + 1));
            while (PollInputEvent(ref state) != 0)
            {
            }
        }
        if (escaped != 0)
            FlushInputEvents();
        return escaped;
    }

    /// <summary>
    /// Waits for a key or button. Buttons return 0x1C (Enter); keys return the queued value as
    /// a signed char (scan code, or VK when duplicates are enabled); Ctrl (0x1D) is ignored.
    /// </summary>
    /// <remarks>C: WaitForInputKey (0x41DAA0, disk.c).</remarks>
    public async Task<short> WaitForInputKeyAsync()
    {
        var state = new InputEventState();
        sbyte key = 0;
        if (!IsActive)
            return await PumpMessagesDuringWaitAsync();

        byte savedMode = InputMode;
        InputMode = 1;
        do
        {
            switch (PollInputEvent(ref state))
            {
                case InputEventType.ButtonDown:
                case InputEventType.JoystickButton:
                    key = 0x1c;
                    while (PollInputEvent(ref state) != 0)
                    {
                    }
                    break;
                case InputEventType.KeyDown:
                case InputEventType.Character:
                    key = unchecked((sbyte)state.Value);
                    if (key == 0x1d)
                    {
                        key = 0;
                    }
                    else
                    {
                        while (PollInputEvent(ref state) != 0)
                        {
                        }
                    }
                    break;
                case 0:
                    await Idle();
                    break;
            }
        }
        while (key == 0);
        ClearInputKeyStatePreservingModifiers();
        InputMode = savedMode;
        FlushInputEvents();
        return key;
    }

    /// <summary>
    /// Waits <paramref name="duration"/> sixtieths of a second or until a key/button. With -1 it
    /// first waits until nothing is pressed any more, then returns at the next input.
    /// </summary>
    /// <remarks>C: WaitForSceneAdvance (0x41DBA0, disk.c). Like the original, the saved input
    /// mode is only restored when the wait was cut short.</remarks>
    public async Task WaitForSceneAdvanceAsync(short duration)
    {
        var state = new InputEventState();
        bool advanced = false;
        byte savedMode = InputMode;
        InputMode = 1;
        if (duration != -1)
        {
            _timing.SetFrameTimerPeriod(duration);
        }
        else if (CheckEscaped() != 0)
        {
            while (CheckEscaped() != 0)
                await Idle();
            _timing.SetFrameTimerPeriod(0);
        }
        while (!_timing.IsFrameTickElapsed() && !advanced)
        {
            short type = PollInputEvent(ref state);
            switch (type)
            {
                case InputEventType.ButtonDown:
                case InputEventType.KeyDown:
                case InputEventType.Character:
                case InputEventType.JoystickButton:
                    advanced = true;
                    InputMode = savedMode;
                    FlushInputEvents();
                    while (PollInputEvent(ref state) != 0)
                    {
                    }
                    ClearInputKeyStatePreservingModifiers();
                    break;
                case 0:
                    await Idle();
                    break;
            }
        }
    }

    /// <summary>Waits for a non-zero key with VK duplicates enabled (text entry).</summary>
    /// <remarks>C: WaitForStreamInputKey (0x41DEB0, disk.c).</remarks>
    public async Task<short> WaitForStreamInputKeyAsync()
    {
        bool saved = KeyEventQueueEnabled;
        KeyEventQueueEnabled = true;
        short key;
        do
        {
            key = await WaitForInputKeyAsync();
        }
        while (key == 0);
        KeyEventQueueEnabled = saved;
        return key;
    }

    /// <summary>
    /// The "press any key" primitive: pumps until a key is released and returns its VK code as a
    /// signed char. Keys on release, so a held key does not satisfy it.
    /// </summary>
    /// <remarks>C: PumpMessagesDuringWait (0x425BC0) -> DebugOverlayConsole::WaitForKey (SDL).</remarks>
    public async Task<short> PumpMessagesDuringWaitAsync()
    {
        while (true)
        {
            PumpWindowMessages();
            if (DebugOverlayKey != 0)
                break;
            await Idle();
        }
        sbyte key = unchecked((sbyte)DebugOverlayKey);
        DebugOverlayKey = 0;
        return key;
    }

    /// <summary>
    /// Modal acknowledge. Mode != 0: wait for any key release, then any key press. Mode 0: wait
    /// for a released key other than the values 0x19, 0x50 ('P') and 0x0C. The pointer is freed meanwhile.
    /// </summary>
    /// <remarks>C: WaitForKeyAcknowledge (0x428EA0, hudmsg.c) with the SDL grab suspension.</remarks>
    public async Task WaitForKeyAcknowledgeAsync(int mode)
    {
        SuspendMouseGrab();
        try
        {
            if (mode != 0)
            {
                while (true)
                {
                    PumpWindowMessages();
                    if (IsInputEventQueued(InputEventType.KeyUp))
                        break;
                    await Idle();
                }
                FlushInputEvents();
                ClearDebugPauseFlags();
                while (true)
                {
                    PumpWindowMessages();
                    if (IsInputEventQueued(InputEventType.KeyDown))
                        break;
                    await Idle();
                }
                FlushInputEvents();
                ClearDebugPauseFlags();
                return;
            }
            FlushInputEvents();
            ClearDebugPauseFlags();
            short key;
            do
            {
                key = await PumpMessagesDuringWaitAsync();
            }
            while (key == 0x19 || key == 0x50 || key == 0x0c);
            FlushInputEvents();
        }
        finally
        {
            ResumeMouseGrab();
        }
    }

    /// <summary>Waits for a released key other than 'X' and F12, then clears all input state.</summary>
    /// <remarks>C: WaitForKeyExceptXOrF12 (0x425730, pilot.cpp).</remarks>
    public async Task WaitForKeyExceptXOrF12Async()
    {
        FilteredKeyWaitStarted = true;
        FilteredKeyWaitActive = true;
        short key;
        do
        {
            key = await PumpMessagesDuringWaitAsync();
        }
        while (key == 'X' || key == 0x7b);
        FilteredKeyWaitActive = false;
        FlushInputEvents();
        ClearInputKeyState();
    }

    /// <summary>
    /// Keyboard-driven menu pointer: KP5 toggles the step between 1 and 4, the cursor block moves
    /// the pointer by twice the step (diagonals via fall-through), clamped to 0..320 on both
    /// axes (sic). A move re-queues a mouse-move event and warps the OS pointer.
    /// </summary>
    /// <remarks>C: MoveMenuPointerFromKeyboard (0x41DC70, disk.c).</remarks>
    public void MoveMenuPointerFromKeyboard(in InputEventState state)
    {
        int delta = KeyboardPointerStep * 2;
        bool moved = false;
        short key = unchecked((short)state.Value);
        if (key == 0x4c)
        {
            KeyboardPointerStep = (short)(KeyboardPointerStep == 1 ? 4 : 1);
        }
        else
        {
            moved = true;
            switch (key)
            {
                case 0x47:
                    Cursor.Y -= (short)delta;
                    Cursor.X -= (short)delta;
                    break;
                case 0x4b:
                    Cursor.X -= (short)delta;
                    break;
                case 0x49:
                    Cursor.X += (short)delta;
                    Cursor.Y -= (short)delta;
                    break;
                case 0x48:
                    Cursor.Y -= (short)delta;
                    break;
                case 0x4f:
                    Cursor.X -= (short)delta;
                    Cursor.Y += (short)delta;
                    break;
                case 0x50:
                    Cursor.Y += (short)delta;
                    break;
                case 0x51:
                    Cursor.Y += (short)delta;
                    Cursor.X += (short)delta;
                    break;
                case 0x4d:
                    Cursor.X += (short)delta;
                    break;
                default:
                    moved = false;
                    break;
            }
        }

        if (Cursor.X < 0)
            Cursor.X = 0;
        else if (Cursor.X > 320)
            Cursor.X = 320;
        if (Cursor.Y < 0)
            Cursor.Y = 0;
        else if (Cursor.Y > 320)
            Cursor.Y = 320;

        HostMouse.X = Cursor.X;
        HostMouse.Y = Cursor.Y;
        if (moved)
        {
            RetainInputEventsOfType(InputEventType.KeyDown);
            QueueInputEvent(InputEventType.MouseMove, unchecked((ushort)Cursor.X), unchecked((ushort)Cursor.Y), 0, 0, 0);
            PointerMovedByKeyboard = true;
            SetMousePosition(HostMouse.X, HostMouse.Y);
        }
    }
}
