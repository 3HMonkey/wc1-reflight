using WingCommander.Core.Runtime;
using WingCommander.Game.Timing;
using WingCommander.Game.Video;

namespace WingCommander.Game.Tests.Video;

public class DisplayTests
{
    private static (GameScheduler Scheduler, Display Display) Create()
    {
        var scheduler = new GameScheduler();
        var timing = new FrameTiming(scheduler);
        timing.SetCinematicFrameTiming();
        return (scheduler, new Display(timing));
    }

    private static double Run(GameScheduler scheduler, Func<Task> coroutine)
    {
        var task = scheduler.Start(coroutine);
        Assert.True(scheduler.RunToCompletion(task, 10_000));
        task.GetAwaiter().GetResult();
        return scheduler.Now;
    }

    [Fact]
    public void A_deferred_present_shows_now_and_waits_at_the_next_await()
    {
        var (scheduler, display) = Create();
        display.Working[3, 4] = 77;
        display.Slam();
        display.SlamRealNow();
        Assert.Equal(77, display.Front.Pixels[3, 4]);
        Assert.Equal(1, display.SlamCount);
        Assert.True(display.HasDeferredPresents);
        Assert.False(display.Graphics.ScreenDirty);

        Run(scheduler, async () => await display.SettleDeferredPresentsAsync());
        Assert.False(display.HasDeferredPresents);
    }

    [Fact]
    public void Deferred_and_immediate_presents_end_at_the_same_virtual_time()
    {
        var (s1, d1) = Create();
        double immediate = Run(s1, async () =>
        {
            await d1.PresentAsync();
            await d1.PresentAsync();
            await d1.PresentAsync();
        });

        var (s2, d2) = Create();
        double deferred = Run(s2, async () =>
        {
            await d2.PresentAsync();
            d2.Slam();
            d2.SlamRealNow();                    // inside synchronous code
            await d2.SettleDeferredPresentsAsync();
            await d2.PresentAsync();
        });

        var (s3, d3) = Create();
        double forgotten = Run(s3, async () =>
        {
            await d3.PresentAsync();
            d3.Slam();
            d3.SlamRealNow();
            await d3.PresentAsync();             // settles the owed wait first
        });

        Assert.Equal(immediate, deferred);
        Assert.Equal(immediate, forgotten);
        Assert.Equal(3, d2.SlamCount);
        Assert.Equal(3, d3.SlamCount);
    }
}
