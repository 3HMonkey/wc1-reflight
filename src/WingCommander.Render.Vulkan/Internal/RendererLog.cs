namespace WingCommander.Render.Vulkan.Internal;

/// <summary>Forwards renderer messages to the host's sink (or stderr for warnings and errors).</summary>
internal sealed class RendererLog(Action<VulkanLogLevel, string>? sink)
{
    private readonly Action<VulkanLogLevel, string> _sink = sink ?? WriteToStandardError;

    public void Debug(string message) => _sink(VulkanLogLevel.Debug, message);

    public void Info(string message) => _sink(VulkanLogLevel.Info, message);

    public void Warning(string message) => _sink(VulkanLogLevel.Warning, message);

    public void Error(string message) => _sink(VulkanLogLevel.Error, message);

    public void Write(VulkanLogLevel level, string message) => _sink(level, message);

    private static void WriteToStandardError(VulkanLogLevel level, string message)
    {
        if (level >= VulkanLogLevel.Warning)
            Console.Error.WriteLine($"[vulkan] {(level == VulkanLogLevel.Error ? "error" : "warning")}: {message}");
    }
}
