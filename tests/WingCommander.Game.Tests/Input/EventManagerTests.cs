using WingCommander.Core.Platform;
using WingCommander.Core.Runtime;
using WingCommander.Game.Input;
using WingCommander.Game.Timing;

namespace WingCommander.Game.Tests.Input;

public class EventManagerTests
{
    private sealed record Rig(GameScheduler Scheduler, FrameTiming Timing, EventManager Events, HeadlessServices Services)
    {
        public void At(double ms, HostInputEvent e) => Events.EnqueueHostEvent(e, ms);

        public void KeyPress(double ms, int scan, int vk = 0, double hold = 50)
        {
            At(ms, Key(true, scan, vk));
            At(ms + hold, Key(false, scan, vk));
        }

        /// <summary>Runs a coroutine to completion on the virtual clock and returns its result.</summary>
        public T Run<T>(Func<Task<T>> coroutine, double limit = 60_000)
        {
            var task = Scheduler.Start(coroutine);
            Assert.True(Scheduler.RunToCompletion(task, limit), "coroutine did not finish");
            return task.Result;
        }

        public void Run(Func<Task> coroutine, double limit = 60_000)
        {
            var task = Scheduler.Start(coroutine);
            Assert.True(Scheduler.RunToCompletion(task, limit), "coroutine did not finish");
            task.GetAwaiter().GetResult();
        }
    }

    private static Rig Create()
    {
        var scheduler = new GameScheduler();
        var timing = new FrameTiming(scheduler);
        var services = new HeadlessServices();
        var events = new EventManager(services, timing);
        events.Initialize(PointerBounds.FullScreen);
        return new Rig(scheduler, timing, events, services);
    }

    private static HostInputEvent Key(bool down, int scan, int vk = 0, HostModifiers mods = HostModifiers.None, bool repeat = false) =>
        new(down ? HostInputKind.KeyDown : HostInputKind.KeyUp, scan, vk, 0, 0, 0, mods, repeat);

    private static HostInputEvent Mouse(HostInputKind kind, int x, int y, int buttons, int button = 0) =>
        new(kind, button, 0, x, y, buttons, HostModifiers.None, false);

    [Fact]
    public void Key_press_queues_scan_code_and_sets_level_state()
    {
        var r = Create();
        r.At(0, Key(true, 0x1e, 'A'));
        r.Events.PumpWindowMessages();

        var state = new InputEventState();
        Assert.Equal(InputEventType.KeyDown, r.Events.GetNextInputEvent(ref state));
        Assert.Equal(0x1eu, state.Value);
        Assert.Equal(1, r.Events.InputKeyState[0x1e]);
        Assert.Equal(0, r.Events.GetNextInputEvent(ref state));
    }

    [Fact]
    public void Host_events_only_enter_the_queue_when_pumped_and_due()
    {
        var r = Create();
        r.At(100, Key(true, 0x1e, 'A'));
        r.Events.PumpWindowMessages();
        Assert.Equal(0, r.Events.QueuedCount);
        Assert.Equal(1, r.Events.PendingHostEvents);
        r.Scheduler.Advance(100);
        Assert.Equal(0, r.Events.QueuedCount);       // not pumped yet
        r.Events.PumpWindowMessages();
        Assert.Equal(1, r.Events.QueuedCount);
    }

    [Fact]
    public void Vk_duplicate_is_queued_before_the_scan_code_when_enabled()
    {
        var r = Create();
        r.Events.KeyEventQueueEnabled = true;
        r.At(0, Key(true, 0x15, 'Y'));
        r.Events.PumpWindowMessages();

        var state = new InputEventState();
        Assert.Equal(InputEventType.KeyDown, r.Events.GetNextInputEvent(ref state));
        Assert.Equal((uint)'Y', state.Value);
        Assert.Equal(InputEventType.KeyDown, r.Events.GetNextInputEvent(ref state));
        Assert.Equal(0x15u, state.Value);
    }

    [Fact]
    public void Escape_sets_latch_and_key_up_sets_debug_key()
    {
        var r = Create();
        r.At(0, Key(true, 0x01, 0x1b));
        r.At(0, Key(false, 0x01, 0x1b));
        r.Events.PumpWindowMessages();
        Assert.True(r.Events.EscapePressed);
        Assert.Equal(0x1b, r.Events.DebugOverlayKey);
    }

    [Fact]
    public void Queue_overflow_drops_everything()
    {
        var r = Create();
        for (int i = 0; i < 256; i++)
            r.Events.QueueInputEvent(InputEventType.KeyDown, 0, 0, 0x1e, 0, 0);
        Assert.Equal(256, r.Events.QueuedCount);
        r.Events.QueueInputEvent(InputEventType.KeyDown, 0, 0, 0x1e, 0, 0);
        Assert.Equal(0, r.Events.QueuedCount);
    }

    [Fact]
    public void Consecutive_mouse_moves_are_coalesced()
    {
        var r = Create();
        r.At(0, Mouse(HostInputKind.MouseMove, 10, 10, 0));
        r.At(0, Mouse(HostInputKind.MouseMove, 20, 30, MouseButtons.Right));
        r.Events.PumpWindowMessages();
        Assert.Equal(1, r.Events.QueuedCount);

        var state = new InputEventState();
        Assert.Equal(InputEventType.MouseMove, r.Events.GetNextInputEvent(ref state));
        Assert.Equal(20, state.X);
        Assert.Equal(30, state.Y);
        Assert.Equal((short)InputModifiers.SecondaryButton, state.Modifiers);
        Assert.Equal(20, r.Events.Cursor.X);
    }

    [Fact]
    public void Events_are_clamped_to_the_cursor_bounds()
    {
        var r = Create();
        r.Events.Cursor.Bounds = new PointerBounds(10, 24, 309, 151);
        r.At(0, Mouse(HostInputKind.MouseButtonDown, 2, 199, MouseButtons.Left, MouseButtons.Left));
        r.Events.PumpWindowMessages();

        var state = new InputEventState();
        Assert.Equal(InputEventType.ButtonDown, r.Events.GetNextInputEvent(ref state));
        Assert.Equal(10, state.X);
        Assert.Equal(151, state.Y);
        Assert.Equal(1u, state.Value);
    }

    [Fact]
    public void Warp_echo_is_ignored_once()
    {
        var r = Create();
        r.Events.PointerMovedByKeyboard = true;
        r.At(0, Mouse(HostInputKind.MouseMove, 50, 50, 0));
        r.At(0, Mouse(HostInputKind.MouseMove, 60, 60, 0));
        r.Events.PumpWindowMessages();
        var state = new InputEventState();
        Assert.Equal(InputEventType.MouseMove, r.Events.GetNextInputEvent(ref state));
        Assert.Equal(60, state.X);
        Assert.False(r.Events.PointerMovedByKeyboard);
    }

    [Fact]
    public void Wheel_queues_release_then_press()
    {
        var r = Create();
        r.At(0, new HostInputEvent(HostInputKind.MouseWheel, 0, 0, 0, 1, 0, HostModifiers.None, false));
        r.Events.PumpWindowMessages();
        var state = new InputEventState();
        Assert.Equal(InputEventType.KeyUp, r.Events.GetNextInputEvent(ref state));
        Assert.Equal(InputEventType.KeyDown, r.Events.GetNextInputEvent(ref state));
        Assert.Equal(0x0du, state.Value);
    }

    [Fact]
    public void Modifiers_are_sampled_at_queue_time()
    {
        var r = Create();
        r.At(0, Key(true, 0x2a));
        r.At(0, Key(true, 0x1d));
        r.At(0, Key(true, 0x12, 'E'));
        r.Events.PumpWindowMessages();
        var state = new InputEventState();
        r.Events.GetNextInputEvent(ref state);
        r.Events.GetNextInputEvent(ref state);
        r.Events.GetNextInputEvent(ref state);
        Assert.Equal(0x12u, state.Value);
        Assert.Equal(unchecked((short)(InputModifiers.Shift | InputModifiers.Control)), state.Modifiers);
    }

    [Fact]
    public void Control_reads_released_while_a_direction_key_is_held()
    {
        var r = Create();
        r.At(0, Key(true, 0x1d));
        r.Events.PumpWindowMessages();
        Assert.True(r.Events.GetControlKeyState());
        r.At(0, Key(true, 0x48));
        r.Events.PumpWindowMessages();
        Assert.False(r.Events.GetControlKeyState());
    }

    [Theory]
    [InlineData(new[] { 0x48 }, 0x48)]
    [InlineData(new[] { 0x48, 0x4b }, 0x47)]
    [InlineData(new[] { 0x48, 0x4d }, 0x49)]
    [InlineData(new[] { 0x50, 0x4b }, 0x4f)]
    [InlineData(new[] { 0x33 }, 0x52)]
    [InlineData(new[] { 0x34, 0x48 }, 0x53)]
    [InlineData(new[] { 0x4d }, 0x4d)]
    [InlineData(new int[0], 0)]
    public void PollKeyboardState_combines_held_keys(int[] held, int expected)
    {
        var r = Create();
        foreach (int code in held)
            r.At(0, Key(true, code));
        r.Events.PumpWindowMessages();
        Assert.Equal(expected, r.Events.PollKeyboardState());
    }

    [Fact]
    public void WaitForInputKey_waits_on_the_virtual_clock_and_skips_ctrl()
    {
        var r = Create();
        r.At(100, Key(true, 0x1d));
        r.At(250, Key(true, 0x39, ' '));
        short key = r.Run(r.Events.WaitForInputKeyAsync);
        Assert.Equal(0x39, key);
        Assert.InRange(r.Scheduler.Now, 250, 252);
        Assert.Equal(0, r.Events.QueuedCount);
    }

    [Fact]
    public void WaitForInputKey_maps_buttons_to_enter()
    {
        var r = Create();
        r.At(50, Mouse(HostInputKind.MouseButtonDown, 5, 5, MouseButtons.Left, MouseButtons.Left));
        Assert.Equal(0x1c, r.Run(r.Events.WaitForInputKeyAsync));
    }

    [Fact]
    public void CheckEscaped_reports_and_flushes_a_pending_key()
    {
        var r = Create();
        Assert.Equal(0, r.Events.CheckEscaped());
        r.At(0, Key(true, 0x39));
        Assert.NotEqual(0, r.Events.CheckEscaped());
        Assert.Equal(0, r.Events.QueuedCount);
    }

    [Fact]
    public void WaitForSceneAdvance_times_out_after_the_duration()
    {
        var r = Create();
        r.Run(() => r.Events.WaitForSceneAdvanceAsync(60));
        Assert.InRange(r.Scheduler.Now, 1000, 1002);
    }

    [Fact]
    public void WaitForSceneAdvance_ends_early_on_input()
    {
        var r = Create();
        r.KeyPress(300, 0x1c, 0x0d);
        r.Run(() => r.Events.WaitForSceneAdvanceAsync(600));
        Assert.InRange(r.Scheduler.Now, 300, 302);
    }

    [Fact]
    public void PumpMessagesDuringWait_returns_on_key_release()
    {
        var r = Create();
        r.KeyPress(100, 0x1f, 'S', hold: 200);
        short key = r.Run(r.Events.PumpMessagesDuringWaitAsync);
        Assert.Equal((short)'S', key);
        Assert.InRange(r.Scheduler.Now, 300, 302);
    }

    [Fact]
    public void WaitForKeyAcknowledge_frees_and_restores_the_mouse_grab()
    {
        var r = Create();
        r.Events.SetMouseGrab(true);
        Assert.True(r.Services.MouseGrabbed);
        r.KeyPress(10, 0x39, ' ');
        var task = r.Scheduler.Start(() => r.Events.WaitForKeyAcknowledgeAsync(0));
        r.Scheduler.RunUntil(5);
        Assert.False(r.Services.MouseGrabbed);     // freed while waiting
        Assert.True(r.Scheduler.RunToCompletion(task, 1000));
        Assert.True(r.Services.MouseGrabbed);
    }

    [Fact]
    public void Keyboard_pointer_moves_diagonally_and_requeues_a_move()
    {
        var r = Create();
        r.Events.Cursor.X = 100;
        r.Events.Cursor.Y = 100;
        var state = new InputEventState { Value = 0x49 };
        r.Events.MoveMenuPointerFromKeyboard(in state);
        Assert.Equal(108, r.Events.Cursor.X);
        Assert.Equal(92, r.Events.Cursor.Y);
        Assert.True(r.Events.IsInputEventQueued(InputEventType.MouseMove));
        Assert.True(r.Events.PointerMovedByKeyboard);
        Assert.Equal((108, 92), r.Services.LastWarp);
    }

    // ------------------------------------------------------------------ key repeat (ADR-012)

    private static int CountKeyDowns(Rig r, int scan)
    {
        var state = new InputEventState();
        int count = 0;
        short type;
        while ((type = r.Events.GetNextInputEvent(ref state)) != 0)
        {
            if (type == InputEventType.KeyDown && state.Value == (uint)scan)
                count++;
        }
        return count;
    }

    private static void PumpAt(Rig r, double ms)
    {
        r.Scheduler.Advance(ms - r.Scheduler.Now);
        r.Events.PumpWindowMessages();
    }

    [Fact]
    public void A_held_key_repeats_after_500_ms_at_30_per_second()
    {
        var r = Create();
        r.At(0, Key(true, 0x39));
        r.At(1_200, Key(false, 0x39));
        PumpAt(r, 1_300);
        // The press plus repeats at 500 + k * 33.3 ms up to the release at 1200 (k = 0..21).
        Assert.Equal(1 + 22, CountKeyDowns(r, 0x39));
    }

    [Fact]
    public void Operating_system_repeats_are_ignored()
    {
        var r = Create();
        r.At(0, Key(true, 0x39));
        r.At(100, Key(true, 0x39, repeat: true));
        r.At(200, Key(true, 0x39, repeat: true));
        r.At(300, Key(false, 0x39));
        PumpAt(r, 400);
        Assert.Equal(1, CountKeyDowns(r, 0x39));
    }

    [Fact]
    public void Only_the_most_recently_pressed_key_repeats()
    {
        var r = Create();
        r.At(0, Key(true, 0x1e));      // A
        r.At(300, Key(true, 0x30));    // B while A is held
        r.At(1_000, Key(false, 0x30)); // B released: A does not resume repeating
        r.At(1_500, Key(false, 0x1e));
        PumpAt(r, 1_600);
        var state = new InputEventState();
        int a = 0, b = 0;
        short type;
        while ((type = r.Events.GetNextInputEvent(ref state)) != 0)
        {
            if (type != InputEventType.KeyDown)
                continue;
            if (state.Value == 0x1e)
                a++;
            else if (state.Value == 0x30)
                b++;
        }
        Assert.Equal(1, a);
        Assert.Equal(1 + 7, b); // repeats at 800 + k * 33.3 ms up to 1000
    }

    [Fact]
    public void Repeats_are_spread_over_pumps_by_time()
    {
        var r = Create();
        r.At(0, Key(true, 0x48));
        PumpAt(r, 499);
        Assert.Equal(1, CountKeyDowns(r, 0x48));
        PumpAt(r, 500);
        Assert.Equal(1, CountKeyDowns(r, 0x48));
        PumpAt(r, 600); // 533.3, 566.7, 600
        Assert.Equal(3, CountKeyDowns(r, 0x48));
    }

    [Fact]
    public void Without_normalisation_the_host_repeats_pass_through()
    {
        var r = Create();
        r.Events.NormalizeKeyRepeat = false;
        r.At(0, Key(true, 0x39));
        r.At(600, Key(true, 0x39, repeat: true));
        r.At(700, Key(true, 0x39, repeat: true));
        r.At(800, Key(false, 0x39));
        PumpAt(r, 900);
        Assert.Equal(3, CountKeyDowns(r, 0x39));
    }

    [Fact]
    public void Losing_focus_stops_the_repeat()
    {
        var r = Create();
        r.At(0, Key(true, 0x39));
        r.At(100, new HostInputEvent(HostInputKind.FocusLost, 0, 0, 0, 0, 0, HostModifiers.None, false));
        PumpAt(r, 2_000);
        Assert.Equal(1, CountKeyDowns(r, 0x39));
    }

    private static KeyTranslation GunsOnF()
    {
        var bindings = new KeyBindings();
        bindings.Bind(FlightAction.FireGuns, 0x21);
        return new KeyTranslation(bindings);
    }

    [Fact]
    public void Key_bindings_apply_only_while_the_flight_reads_them()
    {
        var r = Create();
        r.Events.KeyTranslation = GunsOnF();
        r.KeyPress(0, 0x21, 'F');
        PumpAt(r, 100);
        Assert.Equal(1, CountKeyDowns(r, 0x21)); // not active: F is F

        r.Events.KeyTranslationActive = true;
        r.At(200, Key(true, 0x21, 'F'));
        PumpAt(r, 210);
        Assert.Equal(1, r.Events.InputKeyState[0x39]);
        Assert.True(r.Events.IsKeyPhysicallyDown(0x39));
        Assert.Equal(1, CountKeyDowns(r, 0x39)); // F fires the guns like Space
        r.At(250, Key(false, 0x21, 'F'));
        PumpAt(r, 260);
        Assert.Equal(0, r.Events.InputKeyState[0x39]);
        CountKeyDowns(r, 0x39); // drains the release

        r.KeyPress(300, 0x39, ' ');
        PumpAt(r, 400);
        Assert.Equal(0, r.Events.QueuedCount); // Space moved away: it does nothing
        Assert.Equal(0, r.Events.InputKeyState[0x39]);
    }

    [Fact]
    public void Keys_pressed_with_ctrl_or_alt_are_not_translated()
    {
        var r = Create();
        r.Events.KeyTranslation = GunsOnF();
        r.Events.KeyTranslationActive = true;
        r.At(0, Key(true, 0x1d, 0x11));
        r.At(10, Key(true, 0x21, 'F', HostModifiers.Control));
        PumpAt(r, 20);
        Assert.Equal(1, CountKeyDowns(r, 0x21));
    }

    [Fact]
    public void A_key_held_while_the_bindings_switch_off_releases_what_it_pressed()
    {
        var r = Create();
        r.Events.KeyTranslation = GunsOnF();
        r.Events.KeyTranslationActive = true;
        r.At(0, Key(true, 0x21, 'F'));
        PumpAt(r, 10);
        r.Events.KeyTranslationActive = false; // the flight ended or a menu opened
        r.At(100, Key(false, 0x21, 'F'));
        PumpAt(r, 110);
        Assert.False(r.Events.IsKeyPhysicallyDown(0x39));
        Assert.False(r.Events.IsKeyPhysicallyDown(0x21));
        Assert.Equal(0, r.Events.InputKeyState[0x39]);
    }
}
