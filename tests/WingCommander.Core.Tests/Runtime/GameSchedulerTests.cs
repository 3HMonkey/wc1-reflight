using WingCommander.Core.Runtime;

namespace WingCommander.Core.Tests.Runtime;

public class GameSchedulerTests
{
    [Fact]
    public void Delays_resume_in_due_order_with_exact_virtual_time()
    {
        var s = new GameScheduler();
        var log = new List<string>();

        async Task A()
        {
            await s.Delay(50);
            log.Add($"A@{s.Now}");
            await s.Delay(50);
            log.Add($"A@{s.Now}");
        }

        async Task B()
        {
            await s.Delay(70);
            log.Add($"B@{s.Now}");
        }

        s.Start(A);
        s.Start(B);
        s.Advance(16);
        Assert.Empty(log);
        s.Advance(200);
        Assert.Equal(["A@50", "B@70", "A@100"], log);
        Assert.Equal(216, s.Now);
    }

    [Fact]
    public void Nested_coroutines_resume_through_the_scheduler()
    {
        var s = new GameScheduler();
        var log = new List<string>();

        async Task<int> Inner()
        {
            await s.Delay(10);
            log.Add("inner");
            return 42;
        }

        async Task Outer()
        {
            int value = await Inner();
            log.Add($"outer {value} @{s.Now}");
        }

        var task = s.Start(Outer);
        Assert.True(s.RunToCompletion(task, 1000));
        Assert.Equal(["inner", "outer 42 @10"], log);
        Assert.Equal(Environment.CurrentManagedThreadId, Environment.CurrentManagedThreadId);
    }

    [Fact]
    public void Continuations_never_leave_the_calling_thread()
    {
        var s = new GameScheduler();
        int thread = Environment.CurrentManagedThreadId;
        var threads = new HashSet<int>();

        async Task Deep(int depth)
        {
            threads.Add(Environment.CurrentManagedThreadId);
            await s.Delay(1);
            if (depth > 0)
                await Deep(depth - 1);
            threads.Add(Environment.CurrentManagedThreadId);
        }

        var task = s.Start(() => Deep(500));
        Assert.True(s.RunToCompletion(task, 10_000));
        Assert.Equal([thread], threads);
    }

    [Fact]
    public void Exceptions_surface_on_the_root_task()
    {
        var s = new GameScheduler();

        async Task Fails()
        {
            await s.Delay(5);
            throw new InvalidOperationException("boom");
        }

        var task = s.Start(Fails);
        s.Advance(10);
        Assert.True(task.IsFaulted);
        Assert.IsType<InvalidOperationException>(task.Exception!.InnerException);
    }

    [Fact]
    public void Until_in_the_past_resumes_after_current_work()
    {
        var s = new GameScheduler();
        s.Advance(100);
        var log = new List<double>();

        async Task Late()
        {
            await s.Until(20);
            log.Add(s.Now);
        }

        s.Start(Late);
        s.Advance(0);
        Assert.Equal([100.0], log);
    }
}
