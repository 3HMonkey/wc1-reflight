namespace WingCommander.Render.Vulkan.Tests;

/// <summary>Probes once whether a Vulkan renderer can be created on this machine.</summary>
public static class VulkanEnvironment
{
    private static readonly Lazy<(bool Available, string Description)> Probe = new(() =>
    {
        bool available = VulkanRenderer.IsAvailable(out string description);
        return (available, description);
    });

    public static bool IsAvailable => Probe.Value.Available;

    /// <summary>Device name when available, otherwise the reason it is not.</summary>
    public static string Description => Probe.Value.Description;
}

/// <summary>A fact that is skipped (not failed) on machines without a usable Vulkan device.</summary>
public sealed class VulkanFactAttribute : FactAttribute
{
    public VulkanFactAttribute()
    {
        if (!VulkanEnvironment.IsAvailable)
            Skip = $"No usable Vulkan device: {VulkanEnvironment.Description}";
    }
}

/// <summary>A theory that is skipped (not failed) on machines without a usable Vulkan device.</summary>
public sealed class VulkanTheoryAttribute : TheoryAttribute
{
    public VulkanTheoryAttribute()
    {
        if (!VulkanEnvironment.IsAvailable)
            Skip = $"No usable Vulkan device: {VulkanEnvironment.Description}";
    }
}
