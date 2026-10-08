using WingCommander.Core.Platform;
using WingCommander.Core.Runtime;
using WingCommander.Game.Timing;

namespace WingCommander.Game.Input;

/// <summary>
/// The DOS-era event manager: a fixed 256-record pool forming a doubly-linked FIFO of
/// input events, the pointer state, the keyboard level state and latches, and the message
/// pump that turns host events into queued events. Behaviour (including overflow, mouse
/// motion coalescing, VK duplicate events and the clamp to the cursor viewport) follows
/// the SDL port of the reference exactly; see docs/analysis/host-input-timing.md §2-4.
/// </summary>
/// <remarks>C: eventmgr.c, sdl/events.c, winmain.c (PumpWindowMessages, WarpMouseTo), sysinput.c.</remarks>
public sealed partial class EventManager
{
    private const int PoolSize = 0x100;
    private const int KeyStateSize = 0x80;

    private readonly IHostServices _host;
    private readonly FrameTiming _timing;
    private readonly QueuedEvent[] _pool = new QueuedEvent[PoolSize];
    private readonly bool[] _slotUsed = new bool[PoolSize];
    private readonly List<(double At, long Sequence, HostInputEvent Event)> _hostEvents = [];
    private long _hostEventSequence;
    private int _head = -1;
    private int _tail = -1;
    private bool _pumpActive;
    private bool _mouseGrabRequested;
    private int _mouseGrabSuspendDepth;

    /// <summary>Physical key-down counters by scan code (two physical keys share Ctrl/Alt and arrow/keypad codes).</summary>
    private readonly byte[] _physicalKeys = new byte[KeyStateSize];

    public EventManager(IHostServices host, FrameTiming timing)
    {
        _host = host;
        _timing = timing;
    }

    /// <summary>Host events delivered but not yet pumped into the game's queue.</summary>
    public int PendingHostEvents => _hostEvents.Count;

    /// <summary>Cursor state used by the game (stMouseCursorState).</summary>
    public MouseCursorState Cursor { get; } = new();

    /// <summary>Host-side pointer state used by the joystick/menu pumps (stHostMouseState).</summary>
    public MouseCursorState HostMouse { get; } = new();

    /// <summary>Last mouse message from the host (nHostMouseMessageX/Y, bHost*MouseButton).</summary>
    public int HostMouseMessageX { get; private set; }

    public int HostMouseMessageY { get; private set; }

    public int HostPrimaryMouseButton { get; private set; }

    public int HostSecondaryMouseButton { get; private set; }

    /// <summary>Level state by scan code, set by the pump and cleared by the game (abInputKeyState).</summary>
    public byte[] InputKeyState { get; } = new byte[KeyStateSize];

    /// <summary>Latched when Esc is pressed; cleared by the game (bEscapePressed).</summary>
    public bool EscapePressed { get; set; }

    /// <summary>F1 held and not auto-repeating (bF1KeyLatch, GetF1KeyLatch).</summary>
    public bool F1KeyLatch { get; private set; }

    /// <summary>VK of the key pressed while Alt is held, 0 otherwise (nSystemKeyDown, GetKeyboardModifiers).</summary>
    public int SystemKeyDown { get; private set; }

    /// <summary>VK of the last released key (dwDebugOverlayKey), consumed by <see cref="PumpMessagesDuringWaitAsync"/>.</summary>
    public int DebugOverlayKey { get; private set; }

    /// <summary>Latch of the last released key (dwDebugOverlayKeyLatch).</summary>
    public int DebugOverlayKeyLatch { get; private set; }

    /// <summary>When set, every key also queues a duplicate event carrying the VK code (bKeyEventQueueEnabled).</summary>
    public bool KeyEventQueueEnabled { get; set; }

    /// <summary>Ignore the next motion event: it is the echo of the game's own pointer warp (bPointerMovedByKeyboard).</summary>
    public bool PointerMovedByKeyboard { get; set; }

    /// <remarks>C: nEventManagerActive.</remarks>
    public bool IsActive { get; private set; }

    /// <remarks>C: bInputMode.</remarks>
    public byte InputMode { get; set; }

    /// <summary>Software cursor visibility counter (the misnamed EnterAllocationScope/LeaveAllocationScope).</summary>
    /// <remarks>C: nMouseCursorShowCount.</remarks>
    public int CursorShowCount { get; set; }

    /// <summary>Device pump run at the start of every message pump (joystick sampling).</summary>
    /// <remarks>C: pEventManagerPump / SetEventManagerPump.</remarks>
    public Action? Pump { get; set; }

    /// <summary>Called on every message pump before host events are drained (music servicing).</summary>
    /// <remarks>C: SdlServiceOriginFxMusic at the top of SdlPumpEvents.</remarks>
    public Action? ServiceHook { get; set; }

    /// <summary>Joystick samples of the two devices (aInputDeviceSamples).</summary>
    public InputDeviceSample[] DeviceSamples { get; } = new InputDeviceSample[2];

    /// <summary>Active joystick, -1 when none (nActiveInputDevice).</summary>
    public short ActiveInputDevice { get; set; } = -1;

    /// <remarks>C: nKeyboardPointerStep.</remarks>
    public short KeyboardPointerStep { get; set; } = 4;

    /// <remarks>C: nInputTickScale.</remarks>
    public int InputTickScale { get; set; }

    /// <remarks>C: nMenuInputRepeatDelay.</remarks>
    public short MenuInputRepeatDelay { get; set; }

    /// <remarks>C: bFilteredKeyWaitStarted.</remarks>
    public bool FilteredKeyWaitStarted { get; private set; }

    /// <remarks>C: bFilteredKeyWaitActive.</remarks>
    public bool FilteredKeyWaitActive { get; private set; }

    /// <summary>Activates the event manager (DOS EMStartUp: period 20, tick scale 20, repeat delay 6).</summary>
    /// <remarks>C: InitializeEventManager (0x435570) + InitializeEventManagerResources (0x421A60) + EMStartUp.</remarks>
    public void Initialize(IPointerBounds cursorBounds)
    {
        IsActive = true;
        InputTickScale = 20;
        Cursor.Frame = 0;
        Cursor.Bounds = cursorBounds;
        MenuInputRepeatDelay = 6;
    }

    /// <remarks>C: ShutdownEventManager.</remarks>
    public void Shutdown() => IsActive = false;

    // ------------------------------------------------------------------ message pump

    /// <summary>
    /// Delivers a host event that becomes visible to the game at the next pump at or after
    /// virtual time <paramref name="atMilliseconds"/> (the SDL host stamps the current time; tests
    /// script future times).
    /// </summary>
    public void EnqueueHostEvent(in HostInputEvent e, double atMilliseconds)
    {
        var entry = (atMilliseconds, _hostEventSequence++, e);
        int index = _hostEvents.Count;
        while (index > 0 && _hostEvents[index - 1].At > atMilliseconds)
            index--;
        _hostEvents.Insert(index, entry);
    }

    /// <summary>Delivers a host event stamped with the current virtual time.</summary>
    public void EnqueueHostEvent(in HostInputEvent e)
    {
        if (e.Kind is HostInputKind.MouseMove or HostInputKind.MouseButtonDown or HostInputKind.MouseButtonUp)
        {
            _hostPointerX = Math.Clamp(e.X, 0, 319);
            _hostPointerY = Math.Clamp(e.Y, 0, 199);
            _hostPointerKnown = true;
        }
        EnqueueHostEvent(e, _timing.Scheduler.Now);
    }

    private int _hostPointerX;
    private int _hostPointerY;
    private bool _hostPointerKnown;

    /// <summary>
    /// The pointer where the host last reported it, ahead of the events the game has read,
    /// clamped to the cursor bounds like the events will be (port addition: the display shows
    /// the cursor there between presents).
    /// </summary>
    public bool TryGetHostPointer(out int x, out int y)
    {
        x = _hostPointerX;
        y = _hostPointerY;
        if (!_hostPointerKnown)
            return false;
        var bounds = Cursor.Bounds;
        x = Math.Clamp(x, bounds.Left, Math.Max(bounds.Left, (int)bounds.Right));
        y = Math.Clamp(y, bounds.Top, Math.Max(bounds.Top, (int)bounds.Bottom));
        return true;
    }

    /// <summary>
    /// The only place host events enter the game: runs the device pump, services music, moves
    /// every host event that is due into the original queue and refreshes
    /// <see cref="FrameTiming.Ticks60Hz"/>. Never blocks.
    /// </summary>
    /// <remarks>C: PumpWindowMessages (0x402320) + SdlPumpEvents. Window close is handled by the
    /// runtime, which ends the game instead of the original's exit(0) inside the pump.</remarks>
    public bool PumpWindowMessages()
    {
        if (_pumpActive)
            return true;
        _pumpActive = true;
        try
        {
            Pump?.Invoke();
            ServiceHook?.Invoke();
            double now = _timing.Scheduler.Now;
            int due = 0;
            while (due < _hostEvents.Count && _hostEvents[due].At <= now)
                due++;
            for (int i = 0; i < due; i++)
            {
                var (at, _, e) = _hostEvents[i];
                EmitKeyRepeatsUntil(at);
                Dispatch(in e, at);
            }
            _hostEvents.RemoveRange(0, due);
            EmitKeyRepeatsUntil(now);
            _timing.UpdateTicks60Hz();
        }
        finally
        {
            _pumpActive = false;
        }
        return true;
    }

    /// <summary>Yields one virtual millisecond inside polling loops (the original spun at full speed).</summary>
    private SchedulerAwaitable Idle() => _timing.Scheduler.Delay(1);

    /// <summary>
    /// Port hotkeys outside the original controls (F10 = key help, ADR-013): called with the scan
    /// code and modifiers of every first key press; returning true consumes the key (press,
    /// repeats and release), so the game never sees it.
    /// </summary>
    public Func<int, HostModifiers, bool>? PortHotkey { get; set; }

    private readonly bool[] _consumedKeys = new bool[KeyStateSize];

    /// <summary>
    /// The player's key bindings (port addition, ADR-016), applied to every key press while
    /// <see cref="KeyTranslationActive"/> is set (flight); null = the original keys.
    /// </summary>
    public KeyTranslation? KeyTranslation { get; set; }

    /// <summary>Set by the flight while its controls are read; menus clear it so they get the plain keys.</summary>
    public bool KeyTranslationActive { get; set; }

    /// <summary>Per pressed key: what its press was translated to (0 = itself, -2 = nothing), used for its repeats and release.</summary>
    private readonly short[] _heldKeys = new short[KeyStateSize];

    /// <summary>
    /// Applies the key bindings to a key event; false when the key does nothing. The translation is
    /// chosen when the key goes down and kept until it goes up, so a key held while the flight
    /// starts, ends or opens a menu still releases what it pressed. Keys pressed with Ctrl or Alt
    /// are never translated (the fixed combinations such as Ctrl+E stay on their letters).
    /// </summary>
    private bool TranslateKey(ref HostInputEvent e)
    {
        int scanCode = e.Code;
        if (scanCode <= 0 || scanCode >= KeyStateSize)
            return true;
        int target;
        if (e.Kind == HostInputKind.KeyUp || e.Repeat)
        {
            target = _heldKeys[scanCode];
            if (e.Kind == HostInputKind.KeyUp)
                _heldKeys[scanCode] = 0;
        }
        else
        {
            target = 0;
            bool modifier = (e.Modifiers & (HostModifiers.Control | HostModifiers.Alt)) != 0
                || _physicalKeys[0x1d] > 0 || _physicalKeys[0x38] > 0;
            if (KeyTranslationActive && KeyTranslation is { } translation && !modifier)
            {
                int translated = translation.Translate(scanCode);
                target = translated == KeyTranslation.Blocked ? -2 : Math.Max(translated, 0);
            }
            _heldKeys[scanCode] = (short)target;
        }
        if (target == 0)
            return true;
        if (target < 0)
            return false;
        e = e with { Code = target, VirtualKey = GameKeys.VirtualKey(target) };
        return true;
    }

    private bool ConsumePortHotkey(in HostInputEvent e)
    {
        int scanCode = e.Code;
        if ((uint)scanCode >= KeyStateSize)
            return false;
        if (e.Kind == HostInputKind.KeyUp)
        {
            if (!_consumedKeys[scanCode])
                return false;
            _consumedKeys[scanCode] = false;
            return true;
        }
        if (_consumedKeys[scanCode])
            return true;
        if (e.Repeat || PortHotkey is not { } hotkey || !hotkey(scanCode, e.Modifiers))
            return false;
        _consumedKeys[scanCode] = true;
        return true;
    }

    private void Dispatch(in HostInputEvent e, double at)
    {
        switch (e.Kind)
        {
            case HostInputKind.KeyDown:
            case HostInputKind.KeyUp:
            {
                if (ConsumePortHotkey(in e))
                    break;
                HostInputEvent key = e;
                if (!TranslateKey(ref key))
                    break;
                if (NormalizeKeyRepeat)
                {
                    if (key.Repeat)
                        break; // replaced by the repeats generated on the virtual clock
                    TrackKeyRepeat(in key, at);
                }
                HandleKey(in key);
                break;
            }
            case HostInputKind.MouseWheel:
            {
                // player_input samples one transition before consuming the rest: release first.
                ushort scanCode = (ushort)(e.Y > 0 ? 0x0d : 0x0c);
                QueueInputEvent(InputEventType.KeyUp, 0, 0, scanCode, 0, 0);
                QueueInputEvent(InputEventType.KeyDown, 0, 0, scanCode, 0, 0);
                break;
            }
            case HostInputKind.MouseMove:
            case HostInputKind.MouseButtonDown:
            case HostInputKind.MouseButtonUp:
                HandleMouse(in e);
                break;
            case HostInputKind.FocusLost:
                Array.Clear(_physicalKeys);
                Array.Clear(_heldKeys);
                _keyRepeatActive = false;
                break;
        }
    }

    // ------------------------------------------------------------------ key repeat (ADR-012)

    /// <summary>
    /// When set (default), the operating system's key auto-repeat is ignored and the game
    /// repeats the most recently pressed key itself on the virtual clock: first after
    /// <see cref="KeyRepeatDelay"/>, then every <see cref="KeyRepeatInterval"/> milliseconds (the
    /// Windows defaults). Continuous fire and throttle then behave the same on every OS and
    /// recorded input replays exactly. Repeats are delivered like the OS repeats the original
    /// received (key-down events with the repeat flag).
    /// </summary>
    public bool NormalizeKeyRepeat { get; set; } = true;

    /// <summary>Milliseconds from a key press to its first repeat.</summary>
    public double KeyRepeatDelay { get; set; } = 500;

    /// <summary>Milliseconds between repeats (30 per second).</summary>
    public double KeyRepeatInterval { get; set; } = 1000.0 / 30;

    private HostInputEvent _keyRepeatEvent;
    private bool _keyRepeatActive;
    private double _keyRepeatPressedAt;
    private int _keyRepeatCount;

    /// <summary>Time of the next repeat, computed from the press time so no rounding accumulates.</summary>
    private double NextKeyRepeatAt => _keyRepeatPressedAt + KeyRepeatDelay + _keyRepeatCount * KeyRepeatInterval;

    private void TrackKeyRepeat(in HostInputEvent e, double at)
    {
        if (e.Kind == HostInputKind.KeyDown)
        {
            _keyRepeatEvent = e with { Repeat = true };
            _keyRepeatActive = true;
            _keyRepeatPressedAt = at;
            _keyRepeatCount = 0;
        }
        else if (_keyRepeatActive && e.Code == _keyRepeatEvent.Code)
        {
            _keyRepeatActive = false;
        }
    }

    private void EmitKeyRepeatsUntil(double time)
    {
        while (_keyRepeatActive && NextKeyRepeatAt <= time + 1e-6)
        {
            HandleKey(in _keyRepeatEvent);
            _keyRepeatCount++;
        }
    }

    /// <remarks>C: SdlHandleKeyboardEvent (sdl/events.c).</remarks>
    private void HandleKey(in HostInputEvent e)
    {
        bool pressed = e.Kind == HostInputKind.KeyDown;
        int scanCode = e.Code;
        int virtualKey = e.VirtualKey;

        // Alt+X quits from anywhere, like the Win32 build's WM_QUIT (ADR-012).
        if (pressed && !e.Repeat && scanCode == 0x2d && (e.Modifiers & HostModifiers.Alt) != 0)
            throw new GameExitException("Alt+X");

        if ((uint)scanCode < KeyStateSize && !e.Repeat)
        {
            if (pressed)
                _physicalKeys[scanCode]++;
            else if (_physicalKeys[scanCode] > 0)
                _physicalKeys[scanCode]--;
        }

        if ((e.Modifiers & HostModifiers.Alt) != 0 || scanCode == 0x38)
            SystemKeyDown = pressed ? virtualKey : 0;
        if (scanCode == 0x3b)
            F1KeyLatch = pressed && !e.Repeat;
        if (pressed && scanCode == 0x01)
            EscapePressed = true;
        if (scanCode != 0)
        {
            short type = pressed ? InputEventType.KeyDown : InputEventType.KeyUp;
            if (KeyEventQueueEnabled)
                QueueInputEvent(type, 0, 0, (ushort)virtualKey, 0, 0);
            QueueInputEvent(type, 0, 0, (ushort)scanCode, 0, 0);
            SetInputKeyState(scanCode, pressed);
        }
        if (!pressed)
        {
            DebugOverlayKey = virtualKey;
            DebugOverlayKeyLatch = virtualKey;
        }
    }

    /// <remarks>C: SdlHandleMouseEvent / SdlQueueMouseMotion (sdl/events.c).</remarks>
    private void HandleMouse(in HostInputEvent e)
    {
        int primary = (e.Buttons & MouseButtons.Left) != 0 ? 1 : 0;
        int secondary = (e.Buttons & MouseButtons.Right) != 0 ? 1 : 0;
        int x = Math.Clamp(e.X, 0, 319);
        int y = Math.Clamp(e.Y, 0, 199);

        if (e.Kind == HostInputKind.MouseMove)
        {
            if (PointerMovedByKeyboard)
            {
                PointerMovedByKeyboard = false;
                return;
            }
            if (_tail >= 0 && _pool[_tail].Type == InputEventType.MouseMove)
            {
                ref var queued = ref _pool[_tail];
                queued.X = (short)x;
                queued.Y = (short)y;
                queued.PrimaryButton = (short)primary;
                queued.SecondaryButton = (short)secondary;
                queued.Modifiers &= ~6u;
                if (primary != 0)
                    queued.Modifiers |= InputModifiers.PrimaryButton;
                if (secondary != 0)
                    queued.Modifiers |= InputModifiers.SecondaryButton;
            }
            else
            {
                QueueInputEvent(InputEventType.MouseMove, (ushort)x, (ushort)y, 0, primary, secondary);
            }
        }
        else
        {
            short type = e.Kind == HostInputKind.MouseButtonDown ? InputEventType.ButtonDown : InputEventType.ButtonUp;
            QueueInputEvent(type, (ushort)x, (ushort)y, 0, primary, secondary);
        }
        HostMouseMessageX = x;
        HostMouseMessageY = y;
        HostPrimaryMouseButton = primary;
        HostSecondaryMouseButton = secondary;
    }

    // ------------------------------------------------------------------ mouse grab / pointer

    /// <summary>Spaceflight asks for the pointer to be confined.</summary>
    /// <remarks>C: SdlSetMouseGrab.</remarks>
    public void SetMouseGrab(bool enabled)
    {
        _mouseGrabRequested = enabled;
        ApplyMouseGrab();
    }

    /// <summary>Modal waits free the pointer without changing what flight requested (depth counted).</summary>
    /// <remarks>C: SdlSuspendMouseGrab.</remarks>
    public void SuspendMouseGrab()
    {
        _mouseGrabSuspendDepth++;
        ApplyMouseGrab();
    }

    /// <remarks>C: SdlResumeMouseGrab.</remarks>
    public void ResumeMouseGrab()
    {
        if (_mouseGrabSuspendDepth > 0)
            _mouseGrabSuspendDepth--;
        ApplyMouseGrab();
    }

    private void ApplyMouseGrab() => _host.SetMouseGrab(_mouseGrabRequested && _mouseGrabSuspendDepth == 0);

    /// <summary>Moves the OS pointer (in frame coordinates).</summary>
    /// <remarks>C: SetMousePosition (0x402E80) / SetMousePositionDuplicate.</remarks>
    public void SetMousePosition(int x, int y) => _host.WarpMouse(x, y);

    /// <remarks>C: SetMouseHomePosition (0x436160).</remarks>
    public void SetMouseHomePosition(short x, short y)
    {
        Cursor.X = x;
        Cursor.Y = y;
        SetMousePosition(x, y);
    }

    /// <remarks>C: WarpMouseTo (0x401CE0).</remarks>
    public void WarpMouseTo(short x, short y)
    {
        HostMouse.X = x;
        HostMouse.Y = y;
        Cursor.X = x;
        Cursor.Y = y;
        SetMouseHomePosition(x, y);
    }

    /// <summary>Show the software cursor one level more.</summary>
    /// <remarks>C: EnterAllocationScope (0x4360D0) — the name is a mislabel.</remarks>
    public void ShowCursor() => CursorShowCount++;

    /// <remarks>C: LeaveAllocationScope (0x4360E0).</remarks>
    public void HideCursor() => CursorShowCount--;

    /// <remarks>C: ResetAllocationDepth (0x435DC0).</remarks>
    public void ResetCursorShowCount() => CursorShowCount = 0;

    // ------------------------------------------------------------------ keyboard

    /// <remarks>C: SetInputKeyState (0x436420); out-of-range codes exited the original.</remarks>
    public void SetInputKeyState(int scanCode, bool pressed)
    {
        if ((uint)scanCode >= KeyStateSize)
            throw new InvalidOperationException("keyboard almost messed up");
        InputKeyState[scanCode] = (byte)(pressed ? 1 : 0);
    }

    /// <remarks>C: ClearInputKeyState (0x4363E0).</remarks>
    public void ClearInputKeyState()
    {
        Array.Clear(InputKeyState);
        ClearDebugPauseFlags();
    }

    /// <summary>Clears the level state except Ctrl (0x1D) and Alt (0x38).</summary>
    /// <remarks>C: ClearInputKeyStatePreservingModifiers (0x4363A0).</remarks>
    public void ClearInputKeyStatePreservingModifiers()
    {
        byte control = InputKeyState[0x1d];
        byte alt = InputKeyState[0x38];
        Array.Clear(InputKeyState);
        InputKeyState[0x1d] = control;
        InputKeyState[0x38] = alt;
        ClearDebugPauseFlags();
    }

    /// <summary>Physical state of a scan code, independent of the game's clears (GetAsyncKeyState).</summary>
    public bool IsKeyPhysicallyDown(int scanCode) => (uint)scanCode < KeyStateSize && _physicalKeys[scanCode] != 0;

    /// <remarks>C: GetShiftKeyState (0x403060).</remarks>
    public bool GetShiftKeyState() => IsKeyPhysicallyDown(0x2a) || IsKeyPhysicallyDown(0x36);

    /// <summary>
    /// Ctrl state. Like the SDL port, Ctrl reads as released while a direction key is held so
    /// Ctrl+direction steers instead of changing the stored volume levels.
    /// </summary>
    /// <remarks>C: GetControlKeyState (0x403070) via SdlGetAsyncKeyState(VK_CONTROL).</remarks>
    public bool GetControlKeyState()
    {
        if (!IsKeyPhysicallyDown(0x1d))
            return false;
        foreach (int code in DirectionScanCodes)
        {
            if (IsKeyPhysicallyDown(code))
                return false;
        }
        return true;
    }

    private static readonly int[] DirectionScanCodes = [0x47, 0x48, 0x49, 0x4b, 0x4d, 0x4f, 0x50, 0x51];

    /// <remarks>C: GetKeyboardModifiers (0x403080).</remarks>
    public int GetKeyboardModifiers() => SystemKeyDown;

    /// <summary>
    /// Combines the held navigation keys into one DOS scan code for flight steering, with the
    /// original priority (Home, PgUp, End, PgDn, Ins/comma, Del/period, KP5, diagonals, arrows).
    /// Arrow keys and the numeric keypad share scan codes, as on DOS.
    /// </summary>
    /// <remarks>C: PollKeyboardState (0x402EA0).</remarks>
    public int PollKeyboardState()
    {
        bool home = IsKeyPhysicallyDown(0x47);
        bool up = IsKeyPhysicallyDown(0x48);
        bool pageUp = IsKeyPhysicallyDown(0x49);
        bool left = IsKeyPhysicallyDown(0x4b);
        bool right = IsKeyPhysicallyDown(0x4d);
        bool end = IsKeyPhysicallyDown(0x4f);
        bool down = IsKeyPhysicallyDown(0x50);
        bool pageDown = IsKeyPhysicallyDown(0x51);
        bool clear = IsKeyPhysicallyDown(0x4c);
        bool period = IsKeyPhysicallyDown(0x34);
        bool comma = IsKeyPhysicallyDown(0x33);
        bool insert = IsKeyPhysicallyDown(0x52);
        bool delete = IsKeyPhysicallyDown(0x53);

        if (home)
            return 0x47;
        if (pageUp)
            return 0x49;
        if (end)
            return 0x4f;
        if (pageDown)
            return 0x51;
        if (insert || comma)
            return 0x52;
        if (delete || period)
            return 0x53;
        if (clear)
            return 0x4c;
        if (up)
            return left ? 0x47 : right ? 0x49 : 0x48;
        if (down)
            return left ? 0x4f : right ? 0x51 : 0x50;
        if (left)
            return 0x4b;
        return right ? 0x4d : 0;
    }

    /// <remarks>C: ClearDebugPauseFlags (0x425C20).</remarks>
    public void ClearDebugPauseFlags()
    {
        DebugOverlayKeyLatch = 0;
        DebugOverlayKey = 0;
    }

    /// <remarks>C: TakeDebugStepFlag (0x425BD0).</remarks>
    public byte TakeDebugStepFlag()
    {
        byte value = (byte)DebugOverlayKeyLatch;
        DebugOverlayKeyLatch = 0;
        return value;
    }
}
