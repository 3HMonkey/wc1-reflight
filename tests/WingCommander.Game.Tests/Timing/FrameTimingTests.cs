using WingCommander.Core.Numerics;
using WingCommander.Core.Runtime;
using WingCommander.Game.Timing;

namespace WingCommander.Game.Tests.Timing;

public class FrameTimingTests
{
    [Fact]
    public void Frame_timer_fires_after_the_period_in_sixtieths()
    {
        var s = new GameScheduler();
        var timing = new FrameTiming(s);
        timing.SetFrameTimerPeriod(30);
        Assert.False(timing.IsFrameTickElapsed());
        s.Advance(499);
        Assert.False(timing.IsFrameTickElapsed());
        s.Advance(1);
        Assert.True(timing.IsFrameTickElapsed());
    }

    [Fact]
    public void Period_zero_cancels_the_timer()
    {
        var timing = new FrameTiming(new GameScheduler());
        timing.SetFrameTimerPeriod(30);
        timing.SetFrameTimerPeriod(0);
        Assert.True(timing.IsFrameTickElapsed());
    }

    [Fact]
    public void WaitForFrameTick_resumes_at_the_deadline()
    {
        var s = new GameScheduler();
        var timing = new FrameTiming(s);
        var task = s.Start(() => timing.SetFrameTimerAndWaitAsync(20));
        Assert.True(s.RunToCompletion(task, 10_000));
        Assert.Equal(333, s.Now);
    }

    [Fact]
    public void Throttle_spaces_presents_by_the_interval_without_banking_credit()
    {
        var s = new GameScheduler();
        var timing = new FrameTiming(s);
        timing.SetSpaceFlightFrameTiming();
        Assert.Equal(50, timing.FrameIntervalMs);
        var presents = new List<double>();

        async Task Loop()
        {
            for (int frame = 0; frame < 4; frame++)
            {
                await s.Delay(frame == 2 ? 70 : 10);   // drawing cost; frame 2 is slow
                presents.Add(s.Now);
                await timing.ThrottleFrameAsync();
            }
        }

        var task = s.Start(Loop);
        Assert.True(s.RunToCompletion(task, 10_000));
        // Present happens right after drawing; the throttle then waits for the previous deadline.
        Assert.Equal([10.0, 20.0, 130.0, 140.0], presents);

        timing.SetCinematicFrameTiming();
        Assert.Equal(62, timing.FrameIntervalMs);
    }

    [Fact]
    public void Vertical_blank_follows_a_70_hz_grid()
    {
        var s = new GameScheduler();
        var timing = new FrameTiming(s);
        var times = new List<double>();

        async Task Fade()
        {
            for (int step = 0; step < 3; step++)
            {
                await timing.WaitForVerticalBlankAsync();
                times.Add(Math.Round(s.Now, 3));
            }
        }

        var task = s.Start(Fade);
        Assert.True(s.RunToCompletion(task, 1000));
        Assert.Equal([14.286, 28.571, 42.857], times);
    }

    [Fact]
    public void Max_fps_adjustment_is_clamped()
    {
        var timing = new FrameTiming(new GameScheduler());
        timing.SetSpaceFlightFrameTiming();
        for (int i = 0; i < 40; i++)
            timing.AdjustSpaceFlightMaxFps(0.5f);
        Assert.Equal(32.0f, timing.SpaceFlightFrameRate);
        Assert.Equal(31, timing.FrameIntervalMs);
        string text = "";
        for (int i = 0; i < 80; i++)
            text = timing.AdjustSpaceFlightMaxFps(-0.5f);
        Assert.Equal("Space Flight Max FPS : 8.0", text);
    }

    [Fact]
    public void Ticks_follow_the_virtual_clock()
    {
        var s = new GameScheduler();
        var timing = new FrameTiming(s);
        s.Advance(1000);
        timing.UpdateTicks60Hz();
        Assert.Equal(60u, timing.Ticks60Hz);
    }

    [Fact]
    public void Game_clock_starts_at_a_random_epoch_in_the_future()
    {
        var s = new GameScheduler();
        var timing = new FrameTiming(s);
        int expectedOffset = new CRandom(12345).Next() & 3600000;
        timing.InitGameClockEpoch(new CRandom(12345));
        s.Advance(expectedOffset + 1000);
        Assert.Equal(60u, timing.GameClockTicks);
    }
}
