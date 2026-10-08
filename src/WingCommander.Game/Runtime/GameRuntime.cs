using WingCommander.Core.Numerics;
using WingCommander.Core.Platform;
using WingCommander.Core.Rendering;
using WingCommander.Core.Runtime;
using WingCommander.Game.Input;
using WingCommander.Game.Timing;
using WingCommander.Game.Video;

namespace WingCommander.Game.Runtime;

/// <summary>
/// The game as seen by the host (ADR-009): owns the virtual clock, the event manager, the
/// display and the root coroutine. The host calls <see cref="HandleEvent"/>, <see cref="Update"/>
/// and renders <see cref="Frame"/>; nothing here blocks.
/// </summary>
public sealed class GameRuntime : IGameApp
{
    /// <summary>Longest real-time step applied at once; longer stalls (window drag, debugger) pause the game instead of fast-forwarding it.</summary>
    public static readonly TimeSpan MaxStep = TimeSpan.FromMilliseconds(100);

    private Task? _root;
    private bool _quitRequested;

    public GameRuntime(IHostServices host, CRandom? random = null, string title = "Wing Commander")
    {
        Host = host;
        Title = title;
        Scheduler = new GameScheduler();
        Timing = new FrameTiming(Scheduler);
        Events = new EventManager(host, Timing);
        Display = new Display(Timing);
        Random = random ?? new CRandom(unchecked((uint)DateTime.UtcNow.Ticks));
        Frame = new RenderFrame(Display.Front);
    }

    public string Title { get; }

    public IHostServices Host { get; }

    public GameScheduler Scheduler { get; }

    public FrameTiming Timing { get; }

    public EventManager Events { get; }

    public Display Display { get; }

    /// <summary>The game's shared rand() generator (srand(time) at start-up, like WinMain).</summary>
    public CRandom Random { get; }

    public RenderFrame Frame { get; }

    public bool IsFinished => _quitRequested || (_root?.IsCompleted ?? false);

    /// <summary>The exception that ended the game abnormally, if any (GameExitException counts as normal).</summary>
    public Exception? Failure
    {
        get
        {
            if (_root is not { IsFaulted: true } root)
                return null;
            var inner = root.Exception!.InnerException ?? root.Exception;
            return inner is GameExitException ? null : inner;
        }
    }

    /// <summary>Starts the root coroutine (GameMain).</summary>
    public void Start(Func<GameRuntime, Task> main)
    {
        if (_root is not null)
            throw new InvalidOperationException("The game is already running.");
        _root = Scheduler.Start(() => main(this));
    }

    public void HandleEvent(in HostInputEvent e)
    {
        if (e.Kind == HostInputKind.Quit)
        {
            _quitRequested = true;
            return;
        }
        Events.EnqueueHostEvent(e);
    }

    public void Update(TimeSpan elapsed)
    {
        if (IsFinished)
            return;
        if (elapsed > MaxStep)
            elapsed = MaxStep;
        Scheduler.Advance(elapsed.TotalMilliseconds);
        AfterUpdate?.Invoke();
    }

    /// <summary>
    /// Called by the host loop after every update, while the game waits (port addition: the
    /// cursor follows the mouse between presents). Headless runs never call it.
    /// </summary>
    public Action? AfterUpdate { get; set; }

    /// <summary>Runs until the game finishes or <paramref name="limitMilliseconds"/> of virtual time passed (tests, tools).</summary>
    public bool RunHeadless(double limitMilliseconds)
    {
        if (_root is null)
            throw new InvalidOperationException("Start the game first.");
        return Scheduler.RunToCompletion(_root, limitMilliseconds);
    }
}
