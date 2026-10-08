using WingCommander.Core.Numerics;
using WingCommander.Core.Runtime;

namespace WingCommander.Game.Timing;

/// <summary>
/// The game's clocks on the deterministic virtual clock (ADR-009): the 60 Hz tick snapshot
/// taken by the event pump, the one-shot frame timer used by menus and cinematics (periods in
/// 1/60 s), the present throttle that paces every presented frame (16 fps cinematic, 20 fps
/// space flight), the 70 Hz vertical blank used by palette fades, and the cockpit's game clock.
/// Waiting is done with <c>await</c>; nothing blocks the thread.
/// </summary>
/// <remarks>C: hudmsg.c SetMultimediaTimerCallback, eventmgr.c frame timer, dib.c frame timing,
/// screen.c ThrottleFrameAndDrawFps, sysinput.c game clock.</remarks>
public sealed class FrameTiming
{
    /// <summary>VGA mode 13h refresh: palette fades step once per retrace.</summary>
    public const double VerticalBlankPeriod = 1000.0 / 70.0;

    private readonly GameScheduler _scheduler;
    private bool _frameTickPending;
    private double _frameTickDeadline;
    private double _frameDeadline;
    private uint _gameClockBase;
    private double _lastVerticalBlank;

    public FrameTiming(GameScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    public GameScheduler Scheduler => _scheduler;

    /// <summary>Virtual milliseconds (GetTickCount / timeGetTime).</summary>
    public uint Milliseconds => _scheduler.Milliseconds;

    /// <summary>60 Hz ticks, refreshed only when the event pump runs.</summary>
    /// <remarks>C: nTickCount60Hz.</remarks>
    public uint Ticks60Hz { get; private set; }

    /// <remarks>C: fSpaceFlightFrameRate (adjustable 8..32 with Alt+N/Alt+M in the Win32 build).</remarks>
    public float SpaceFlightFrameRate { get; private set; } = 20.0f;

    /// <remarks>C: fCinematicFrameRate.</remarks>
    public float CinematicFrameRate { get; set; } = 16.0f;

    /// <remarks>C: bSpaceFlightFrameTiming.</remarks>
    public bool IsSpaceFlightFrameTiming { get; private set; }

    /// <remarks>C: nFrameIntervalMs.</remarks>
    public int FrameIntervalMs { get; private set; } = 62;

    /// <summary>Recomputes <see cref="Ticks60Hz"/> exactly as the pump does (32-bit wrap included).</summary>
    public void UpdateTicks60Hz() => Ticks60Hz = unchecked(Milliseconds * 60u) / 1000u;

    /// <summary>Arms the one-shot frame timer for <paramref name="period"/> sixtieths of a second; 0 cancels it.</summary>
    /// <remarks>C: SetMultimediaTimerCallback / SetFrameTimerPeriod / SetFrameTimerPeriodDirect.</remarks>
    public void SetFrameTimerPeriod(int period)
    {
        if (period == 0)
        {
            _frameTickPending = false;
            return;
        }
        _frameTickPending = true;
        _frameTickDeadline = _scheduler.Now + period * 1000 / 60;
    }

    /// <remarks>C: IsFrameTickElapsed (0x436240).</remarks>
    public bool IsFrameTickElapsed()
    {
        if (_frameTickPending && _scheduler.Now >= _frameTickDeadline)
            _frameTickPending = false;
        return !_frameTickPending;
    }

    /// <summary>Waits until the frame timer fired (the original spun without pumping events).</summary>
    /// <remarks>C: WaitForFrameTick (0x436230).</remarks>
    public async Task WaitForFrameTickAsync()
    {
        if (!IsFrameTickElapsed())
            await _scheduler.Until(_frameTickDeadline);
        _frameTickPending = false;
    }

    /// <remarks>C: SetFrameTimerAndWait (0x4361F0).</remarks>
    public Task SetFrameTimerAndWaitAsync(int period)
    {
        SetFrameTimerPeriod(period);
        return WaitForFrameTickAsync();
    }

    /// <remarks>C: SetSpaceFlightFrameTiming (0x4320E0).</remarks>
    public void SetSpaceFlightFrameTiming()
    {
        IsSpaceFlightFrameTiming = true;
        FrameIntervalMs = (int)(1000.0 / SpaceFlightFrameRate);
        _frameDeadline = 0;
    }

    /// <remarks>C: SetCinematicFrameTiming (0x432110).</remarks>
    public void SetCinematicFrameTiming()
    {
        IsSpaceFlightFrameTiming = false;
        FrameIntervalMs = (int)(1000.0 / CinematicFrameRate);
        _frameDeadline = 0;
    }

    /// <summary>Adjusts the space-flight frame cap (clamped to 8..32) and returns the HUD message text.</summary>
    /// <remarks>C: ReportSpaceFlightMaxFps (0x432050).</remarks>
    public string AdjustSpaceFlightMaxFps(float adjustment)
    {
        SpaceFlightFrameRate += adjustment;
        if (SpaceFlightFrameRate < 8.0f)
            SpaceFlightFrameRate = 8.0f;
        else if (SpaceFlightFrameRate > 32.0f)
            SpaceFlightFrameRate = 32.0f;
        if (IsSpaceFlightFrameTiming)
            SetSpaceFlightFrameTiming();
        return $"Space Flight Max FPS : {SpaceFlightFrameRate:0.0}";
    }

    /// <summary>
    /// Waits for the previous frame's deadline, then starts the next interval. Called after every
    /// present, so presents are spaced at least one interval apart and late frames bank no credit.
    /// </summary>
    /// <remarks>C: ThrottleFrameAndDrawFps (0x431F00).</remarks>
    public async Task ThrottleFrameAsync()
    {
        if (_frameDeadline > _scheduler.Now)
            await _scheduler.Until(_frameDeadline);
        _frameDeadline = _scheduler.Now + FrameIntervalMs;
    }

    /// <summary>Waits for the next virtual 70 Hz vertical retrace (palette fades step on it).</summary>
    /// <remarks>C: DIBwaitForVerticalBlank / WaitForVerticalBlankThunk.</remarks>
    public async Task WaitForVerticalBlankAsync()
    {
        double next = _lastVerticalBlank + VerticalBlankPeriod;
        if (next <= _scheduler.Now)
            next = Math.Floor(_scheduler.Now / VerticalBlankPeriod + 1) * VerticalBlankPeriod;
        await _scheduler.Until(next);
        _lastVerticalBlank = next;
    }

    /// <remarks>C: InitGameClockEpoch (0x4030B0): base = now + (rand() &amp; 3600000).</remarks>
    public void InitGameClockEpoch(CRandom random) =>
        _gameClockBase = unchecked(Milliseconds + (uint)(random.Next() & 3600000));

    /// <summary>60 Hz ticks since the (randomised) epoch, used by the cockpit chronometer.</summary>
    /// <remarks>C: GetGameClockTicks (0x403090), unsigned 32-bit arithmetic.</remarks>
    public uint GameClockTicks => unchecked((Milliseconds - _gameClockBase) * 60u) / 1000u;
}
