using System.Runtime.CompilerServices;

namespace WingCommander.Core.Runtime;

/// <summary>
/// Deterministic virtual clock and coroutine scheduler (ADR-009). Game code is written as
/// sequential <c>async</c> methods; every <c>await</c> on <see cref="Delay"/>/<see cref="Until"/>
/// hands control back to the scheduler, which resumes continuations strictly in due-time order
/// (FIFO for equal times) on the calling thread. Virtual time only moves inside
/// <see cref="Advance"/>: the SDL host advances it by real elapsed time, tests by scripted steps,
/// so a run is reproducible to the millisecond regardless of the display rate.
/// </summary>
/// <remarks>
/// While the scheduler runs it installs its own <see cref="SynchronizationContext"/>, so awaiting a
/// <see cref="Task"/> returned by another coroutine also resumes through the queue (never on the
/// thread pool). Game code must not await anything else (no Task.Delay, no I/O tasks) and must not
/// use ConfigureAwait(false).
/// </remarks>
public sealed class GameScheduler
{
    private readonly PriorityQueue<Action, (double Due, long Sequence)> _queue = new();
    private readonly SchedulerContext _context;
    private long _sequence;
    private double _now;
    private bool _running;

    public GameScheduler()
    {
        _context = new SchedulerContext(this);
    }

    /// <summary>Virtual time in milliseconds (fractional while advancing by real time).</summary>
    public double Now => _now;

    /// <summary>Virtual time as a 32-bit millisecond counter (wraps like GetTickCount).</summary>
    public uint Milliseconds => unchecked((uint)(long)_now);

    /// <summary>Number of continuations waiting.</summary>
    public int PendingCount => _queue.Count;

    /// <summary>Due time of the next continuation, or null when idle.</summary>
    public double? NextDue => _queue.TryPeek(out _, out var priority) ? priority.Due : null;

    /// <summary>Resumes after <paramref name="milliseconds"/> of virtual time (0 = after everything already due now).</summary>
    public SchedulerAwaitable Delay(double milliseconds) => new(this, _now + Math.Max(0, milliseconds));

    /// <summary>Resumes at virtual time <paramref name="dueMilliseconds"/> (immediately-after-now if that is in the past).</summary>
    public SchedulerAwaitable Until(double dueMilliseconds) => new(this, Math.Max(_now, dueMilliseconds));

    /// <summary>Starts a coroutine with a result: runs its synchronous prefix now, inside the scheduler context.</summary>
    public Task<T> Start<T>(Func<Task<T>> coroutine)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_context);
        try
        {
            return coroutine();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>Starts a coroutine: runs its synchronous prefix now, inside the scheduler context.</summary>
    public Task Start(Func<Task> coroutine)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_context);
        try
        {
            return coroutine();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>Advances virtual time by <paramref name="elapsedMilliseconds"/>, resuming everything that falls due.</summary>
    public void Advance(double elapsedMilliseconds)
    {
        double target = _now + Math.Max(0, elapsedMilliseconds);
        RunUntil(target);
        _now = target;
    }

    /// <summary>
    /// Resumes continuations with due time &lt;= <paramref name="target"/> in order; virtual time jumps
    /// to each continuation's due time before it runs. Time ends at the last continuation's due
    /// time (callers that want wall-clock alignment use <see cref="Advance"/>).
    /// </summary>
    public void RunUntil(double target)
    {
        if (_running)
            throw new InvalidOperationException("GameScheduler is not re-entrant.");
        _running = true;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_context);
        try
        {
            while (_queue.TryPeek(out var continuation, out var priority) && priority.Due <= target)
            {
                _queue.Dequeue();
                if (priority.Due > _now)
                    _now = priority.Due;
                continuation();
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            _running = false;
        }
    }

    /// <summary>Runs until <paramref name="task"/> completes or virtual time passes <paramref name="limitMilliseconds"/>. For tests and tools.</summary>
    public bool RunToCompletion(Task task, double limitMilliseconds)
    {
        while (!task.IsCompleted)
        {
            double? next = NextDue;
            if (next is null || next.Value > limitMilliseconds)
                return false;
            RunUntil(next.Value);
        }
        return true;
    }

    internal void Post(Action continuation, double due) => _queue.Enqueue(continuation, (due, _sequence++));

    private sealed class SchedulerContext(GameScheduler scheduler) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => scheduler.Post(() => d(state), scheduler._now);

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public override SynchronizationContext CreateCopy() => this;
    }
}

/// <summary>Awaitable returned by <see cref="GameScheduler.Delay"/> and <see cref="GameScheduler.Until"/>.</summary>
public readonly struct SchedulerAwaitable(GameScheduler scheduler, double due)
{
    public Awaiter GetAwaiter() => new(scheduler, due);

    public readonly struct Awaiter(GameScheduler scheduler, double due) : INotifyCompletion
    {
        /// <summary>Always false: awaiting always yields, which keeps ordering deterministic.</summary>
        public bool IsCompleted => false;

        public void OnCompleted(Action continuation) => scheduler.Post(continuation, due);

        public void GetResult()
        {
        }
    }
}
