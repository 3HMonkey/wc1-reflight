namespace WingCommander.Render.Vulkan;

/// <summary>A Vulkan call failed. <see cref="ResultCode"/> is the raw <c>VkResult</c> (0 when not from a call).</summary>
public class VulkanRendererException : Exception
{
    public VulkanRendererException(string message)
        : base(message)
    {
    }

    public VulkanRendererException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public VulkanRendererException(string message, int resultCode, string resultName)
        : base($"{message} ({resultName})")
    {
        ResultCode = resultCode;
        ResultName = resultName;
    }

    /// <summary>The <c>VkResult</c> value, e.g. -4 for VK_ERROR_DEVICE_LOST.</summary>
    public int ResultCode { get; }

    /// <summary>The <c>VkResult</c> name, e.g. "VK_ERROR_DEVICE_LOST".</summary>
    public string? ResultName { get; }

    /// <summary>True when the GPU was lost (driver reset or crash); recreate the renderer or fall back.</summary>
    public bool IsDeviceLost => ResultCode == -4;
}

/// <summary>
/// Vulkan cannot be used on this system: no loader, no Vulkan 1.2 driver, no GPU with the
/// required features, or no presentation support for the window. The message says why; the
/// host falls back to its SDL_Renderer presenter.
/// </summary>
public sealed class VulkanUnavailableException : VulkanRendererException
{
    public VulkanUnavailableException(string message)
        : base(message)
    {
    }

    public VulkanUnavailableException(string message, int resultCode, string resultName)
        : base(message, resultCode, resultName)
    {
    }
}
