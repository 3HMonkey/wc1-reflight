namespace WingCommander.Render.Vulkan.Tests;

/// <summary>
/// Collects the validation layer messages (and synchronization validation hazards) of every
/// renderer the tests create. When the Vulkan SDK is installed, <see cref="ValidationMode.Auto"/>
/// enables the layer and every test of the "Vulkan" collection fails if it produced a message
/// (see <see cref="VulkanTestBase"/>); without the layer the log simply stays empty.
/// </summary>
public static class ValidationLog
{
    private static readonly Lock Gate = new();
    private static readonly List<string> Messages = [];

    /// <summary>Use as <c>VulkanRendererOptions.Log</c>.</summary>
    public static Action<VulkanLogLevel, string> Sink { get; } = Record;

    /// <summary>Records validation messages and forwards everything to <paramref name="other"/>.</summary>
    public static Action<VulkanLogLevel, string> Combine(Action<VulkanLogLevel, string> other) => (level, message) =>
    {
        Record(level, message);
        other(level, message);
    };

    /// <summary>Returns and clears the recorded messages.</summary>
    public static string[] Drain()
    {
        lock (Gate)
        {
            string[] messages = [.. Messages];
            Messages.Clear();
            return messages;
        }
    }

    private static void Record(VulkanLogLevel level, string message)
    {
        if (level < VulkanLogLevel.Warning || !message.StartsWith("validation:", StringComparison.Ordinal))
            return;
        lock (Gate)
            Messages.Add($"{level}: {message}");
    }
}

/// <summary>
/// Base of the GPU tests: after each test the shared renderer is drained (sync validation reports
/// some hazards at submit or wait time) and the test fails if any validation message was logged.
/// </summary>
public abstract class VulkanTestBase(OffscreenRendererFixture? fixture = null) : IDisposable
{
    protected OffscreenRendererFixture Fixture => fixture ?? throw new InvalidOperationException("This test class has no fixture.");

    public void Dispose()
    {
        fixture?.WaitIdleIfCreated();
        string[] messages = ValidationLog.Drain();
        Assert.True(messages.Length == 0, $"{messages.Length} validation message(s):{Environment.NewLine}{string.Join(Environment.NewLine, messages)}");
        GC.SuppressFinalize(this);
    }
}
