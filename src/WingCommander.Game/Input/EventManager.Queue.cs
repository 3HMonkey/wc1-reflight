namespace WingCommander.Game.Input;

public sealed partial class EventManager
{
    /// <summary>One pool record (stride 0x1C in the original).</summary>
    private struct QueuedEvent
    {
        public short Type;
        public short X;
        public short Y;
        public short Value;
        public uint Modifiers;
        public uint Timestamp;
        public short PrimaryButton;
        public short SecondaryButton;
        public int Next;
        public int Previous;
    }

    /// <summary>Number of queued events (diagnostics and tests).</summary>
    public int QueuedCount
    {
        get
        {
            int count = 0;
            for (int e = _head; e >= 0; e = _pool[e].Next)
                count++;
            return count;
        }
    }

    /// <remarks>C: AllocateInputEvent (0x4356E0): first free slot of 256, -1 when full.</remarks>
    private int AllocateInputEvent()
    {
        for (int i = 0; i < PoolSize; i++)
        {
            if (!_slotUsed[i])
            {
                _slotUsed[i] = true;
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Appends an event. Modifier bits are sampled now (Shift 0xE0, Ctrl 0x2000, Alt 0x700,
    /// buttons 2/4). When the pool is full the whole queue is dropped, new event included.
    /// </summary>
    /// <remarks>C: QueueInputEvent (0x435790); the timestamp argument is ignored by the original.</remarks>
    public void QueueInputEvent(short type, ushort x, ushort y, ushort value, int primaryButton, int secondaryButton)
    {
        uint modifiers = 0;
        if (GetShiftKeyState())
            modifiers = InputModifiers.Shift;
        if (GetControlKeyState())
            modifiers |= InputModifiers.Control;
        if (GetKeyboardModifiers() != 0)
            modifiers |= InputModifiers.Alt;
        if (primaryButton != 0)
            modifiers |= InputModifiers.PrimaryButton;
        if (secondaryButton != 0)
            modifiers |= InputModifiers.SecondaryButton;

        int slot = AllocateInputEvent();
        if (slot < 0)
        {
            ReleaseInputEventQueue();
            return;
        }
        ref var e = ref _pool[slot];
        e.Next = -1;
        if (_head < 0)
        {
            _head = slot;
            e.Previous = -1;
        }
        else
        {
            _pool[_tail].Next = slot;
            e.Previous = _tail;
        }
        _tail = slot;
        e.Type = type;
        e.Modifiers = modifiers;
        e.X = unchecked((short)x);
        e.Y = unchecked((short)y);
        e.Value = unchecked((short)value);
        e.Timestamp = 0;
        e.PrimaryButton = (short)primaryButton;
        e.SecondaryButton = (short)secondaryButton;
    }

    /// <remarks>C: QueueInputEventAtCursor (0x4356A0).</remarks>
    public void QueueInputEventAtCursor(short type, short primaryButton, short secondaryButton) =>
        QueueInputEvent(type, unchecked((ushort)Cursor.X), unchecked((ushort)Cursor.Y), 0, primaryButton, secondaryButton);

    /// <summary>Re-queues a synthetic event built from host state (mouse button, joystick sample, mouse move).</summary>
    /// <remarks>C: TranslatePolledInputEvent (0x4355F0).</remarks>
    public void TranslatePolledInputEvent(short type, uint value)
    {
        switch (type)
        {
            case InputEventType.ButtonDown:
                QueueInputEvent(type, (ushort)HostMouseMessageX, (ushort)HostMouseMessageY, 0,
                    HostPrimaryMouseButton, HostSecondaryMouseButton);
                return;
            case InputEventType.JoystickSample:
            {
                var sample = DeviceSamples[ActiveInputDevice];
                QueueInputEvent(type, unchecked((ushort)sample.X), unchecked((ushort)sample.Y), 0, 0, 0);
                return;
            }
            case InputEventType.MouseMove:
                QueueInputEvent(type, (ushort)HostMouseMessageX, (ushort)HostMouseMessageY, 0, 0, 0);
                return;
        }
    }

    /// <remarks>C: ReleaseInputEventQueue (0x4358B0) / FlushInputEvents (0x435DB0).</remarks>
    public void FlushInputEvents()
    {
        for (int e = _head; e >= 0;)
        {
            int next = _pool[e].Next;
            _slotUsed[e] = false;
            e = next;
        }
        _head = -1;
        _tail = -1;
    }

    private void ReleaseInputEventQueue() => FlushInputEvents();

    /// <summary>Drops every queued event whose type differs from <paramref name="type"/>.</summary>
    /// <remarks>C: RetainInputEventsOfType (0x4358E0).</remarks>
    public void RetainInputEventsOfType(short type)
    {
        for (int e = _head; e >= 0;)
        {
            int next = _pool[e].Next;
            if (_pool[e].Type != type)
                RemoveInputEvent(e);
            e = next;
        }
    }

    /// <remarks>C: RemoveInputEvent (0x435940).</remarks>
    private void RemoveInputEvent(int e)
    {
        int previous = _pool[e].Previous;
        int next = _pool[e].Next;
        if (previous >= 0)
            _pool[previous].Next = next;
        else
            _head = next;
        if (next >= 0)
            _pool[next].Previous = previous;
        else
            _tail = previous;
        _slotUsed[e] = false;
    }

    /// <summary>
    /// Pops the head event into <paramref name="state"/> (only the fields its type defines)
    /// after clamping its position to the cursor bounds, and updates the cursor for pointer
    /// events. Returns the type, or 0 when the queue is empty. Does not pump.
    /// </summary>
    /// <remarks>C: GetNextInputEvent (0x4359C0).</remarks>
    public short GetNextInputEvent(ref InputEventState state)
    {
        if (_head < 0)
            return 0;

        ref var e = ref _pool[_head];
        var bounds = Cursor.Bounds;
        if (bounds.Left > e.X)
            e.X = bounds.Left;
        else if (bounds.Right < e.X)
            e.X = bounds.Right;
        if (bounds.Top > e.Y)
            e.Y = bounds.Top;
        else if (bounds.Bottom < e.Y)
            e.Y = bounds.Bottom;

        state.Modifiers = unchecked((short)e.Modifiers);
        short type = 0;
        switch (e.Type)
        {
            case InputEventType.ButtonUp:
                Cursor.X = e.X;
                Cursor.Y = e.Y;
                Cursor.PrimaryButton = 0;
                state.X = e.X;
                state.Y = e.Y;
                type = InputEventType.ButtonUp;
                break;
            case InputEventType.ButtonDown:
                Cursor.X = e.X;
                Cursor.Y = e.Y;
                Cursor.PrimaryButton = (byte)e.PrimaryButton;
                Cursor.SecondaryButton = (byte)e.SecondaryButton;
                state.X = e.X;
                state.Y = e.Y;
                type = InputEventType.ButtonDown;
                state.Value = unchecked((uint)(e.SecondaryButton * 2 | (ushort)e.PrimaryButton));
                break;
            case InputEventType.KeyDown:
                type = InputEventType.KeyDown;
                state.Value = unchecked((uint)e.Value);
                state.X = Cursor.X;
                state.Y = Cursor.Y;
                break;
            case InputEventType.KeyUp:
                // The original stores the value into x and immediately overwrites it: the
                // key-up code is lost (readable only through PeekInputEvent/IsInputEventQueued).
                type = InputEventType.KeyUp;
                state.X = Cursor.X;
                state.Y = Cursor.Y;
                break;
            case InputEventType.Character:
                type = InputEventType.Character;
                state.X = e.Value;
                break;
            case InputEventType.JoystickSample:
            case 7:
            case 8:
            case 9:
            case InputEventType.JoystickButton:
                type = e.Type;
                state.X = e.X;
                state.Y = e.Y;
                break;
            case InputEventType.MouseMove:
                Cursor.X = e.X;
                Cursor.Y = e.Y;
                state.X = e.X;
                state.Y = e.Y;
                type = InputEventType.MouseMove;
                break;
        }
        RemoveInputEvent(_head);
        return type;
    }

    /// <summary>Pumps host messages, then pops the next event. The primary blocking-loop primitive.</summary>
    /// <remarks>C: PollInputEvent (0x435CC0); the filter argument was ignored.</remarks>
    public short PollInputEvent(ref InputEventState state)
    {
        PumpWindowMessages();
        return GetNextInputEvent(ref state);
    }

    /// <summary>
    /// Finds the first queued event of <paramref name="type"/> without removing it. Note the
    /// different encoding: Value receives the modifier word, Modifiers gets 1 (button event),
    /// 2 (primary) and 4 (secondary).
    /// </summary>
    /// <remarks>C: PeekInputEvent (0x435CE0).</remarks>
    public bool PeekInputEvent(ref InputEventState state, short type)
    {
        int e = _head;
        while (e >= 0 && _pool[e].Type != type)
            e = _pool[e].Next;
        if (e < 0)
            return false;
        ref var ev = ref _pool[e];
        state.Type = ev.Type;
        state.Value = ev.Modifiers;
        state.Timestamp = ev.Timestamp;
        int modifiers = ev.Type is InputEventType.ButtonDown or InputEventType.ButtonUp ? 1 : 0;
        if (ev.PrimaryButton != 0)
            modifiers |= 2;
        if (ev.SecondaryButton != 0)
            modifiers |= 4;
        state.Modifiers = (short)modifiers;
        state.X = ev.X;
        state.Y = ev.Y;
        return true;
    }

    /// <remarks>C: IsInputEventQueued (0x435D80).</remarks>
    public bool IsInputEventQueued(short type)
    {
        for (int e = _head; e >= 0; e = _pool[e].Next)
        {
            if (_pool[e].Type == type)
                return true;
        }
        return false;
    }
}
