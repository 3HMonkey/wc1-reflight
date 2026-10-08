using System.Runtime.InteropServices;
using Vortice.Vulkan;

namespace WingCommander.Render.Vulkan.Internal;

/// <summary>
/// Receives VK_EXT_debug_utils messages (validation layer) and forwards them to the log and the
/// statistics. The native callback finds this object through a GCHandle in <c>pUserData</c>.
/// </summary>
internal sealed unsafe class DebugMessenger : IDisposable
{
    private readonly RendererLog _log;
    private readonly VulkanRendererStatistics _statistics;
    private GCHandle _handle;

    public DebugMessenger(RendererLog log, VulkanRendererStatistics statistics)
    {
        _log = log;
        _statistics = statistics;
        _handle = GCHandle.Alloc(this);
    }

    /// <summary>Create info pointing at the static callback; also chained into vkCreateInstance.</summary>
    public VkDebugUtilsMessengerCreateInfoEXT CreateInfo() => new()
    {
        messageSeverity = VkDebugUtilsMessageSeverityFlagsEXT.Warning | VkDebugUtilsMessageSeverityFlagsEXT.Error,
        messageType = VkDebugUtilsMessageTypeFlagsEXT.General | VkDebugUtilsMessageTypeFlagsEXT.Validation | VkDebugUtilsMessageTypeFlagsEXT.Performance,
        pfnUserCallback = &OnMessage,
        pUserData = (void*)GCHandle.ToIntPtr(_handle),
    };

    public void Dispose()
    {
        if (_handle.IsAllocated)
            _handle.Free();
    }

    private void Report(VkDebugUtilsMessageSeverityFlagsEXT severity, VkDebugUtilsMessageTypeFlagsEXT types, VkDebugUtilsMessengerCallbackDataEXT* data)
    {
        string id = data->pMessageIdName is null ? string.Empty : Utf8StringList.Read(data->pMessageIdName);
        string message = data->pMessage is null ? string.Empty : Utf8StringList.Read(data->pMessage);
        bool validation = (types & VkDebugUtilsMessageTypeFlagsEXT.Validation) != 0;
        if (!validation && (severity & VkDebugUtilsMessageSeverityFlagsEXT.Error) == 0 && id == "Loader Message")
        {
            // The loader's notes about third-party implicit layers (overlays, capture hooks) are
            // about the user's system, not about this renderer.
            _log.Info($"loader: {message}");
            return;
        }
        if ((severity & VkDebugUtilsMessageSeverityFlagsEXT.Error) != 0)
        {
            if (validation)
                _statistics.ValidationErrors++;
            _log.Error($"validation: [{id}] {message}");
        }
        else
        {
            if (validation)
                _statistics.ValidationWarnings++;
            _log.Warning($"validation: [{id}] {message}");
        }
    }

    [UnmanagedCallersOnly]
    private static uint OnMessage(VkDebugUtilsMessageSeverityFlagsEXT severity, VkDebugUtilsMessageTypeFlagsEXT types,
        VkDebugUtilsMessengerCallbackDataEXT* data, void* userData)
    {
        try
        {
            if (data is not null && userData is not null && GCHandle.FromIntPtr((nint)userData).Target is DebugMessenger messenger)
                messenger.Report(severity, types, data);
        }
        catch
        {
            // Never let an exception cross into the driver.
        }
        return 0; // VK_FALSE: do not abort the call
    }
}
