using Vortice.Vulkan;

namespace WingCommander.Render.Vulkan.Internal;

/// <summary>
/// Spec names for the enum values the renderer logs. Explicit tables instead of
/// <c>Enum.ToString()</c>: stable spellings in logs and no reflection under NativeAOT.
/// </summary>
internal static class VkNames
{
    public static string Result(VkResult result) => result switch
    {
        VkResult.Success => "VK_SUCCESS",
        VkResult.NotReady => "VK_NOT_READY",
        VkResult.Timeout => "VK_TIMEOUT",
        VkResult.Incomplete => "VK_INCOMPLETE",
        VkResult.SuboptimalKHR => "VK_SUBOPTIMAL_KHR",
        VkResult.ErrorOutOfHostMemory => "VK_ERROR_OUT_OF_HOST_MEMORY",
        VkResult.ErrorOutOfDeviceMemory => "VK_ERROR_OUT_OF_DEVICE_MEMORY",
        VkResult.ErrorInitializationFailed => "VK_ERROR_INITIALIZATION_FAILED",
        VkResult.ErrorDeviceLost => "VK_ERROR_DEVICE_LOST",
        VkResult.ErrorMemoryMapFailed => "VK_ERROR_MEMORY_MAP_FAILED",
        VkResult.ErrorLayerNotPresent => "VK_ERROR_LAYER_NOT_PRESENT",
        VkResult.ErrorExtensionNotPresent => "VK_ERROR_EXTENSION_NOT_PRESENT",
        VkResult.ErrorFeatureNotPresent => "VK_ERROR_FEATURE_NOT_PRESENT",
        VkResult.ErrorIncompatibleDriver => "VK_ERROR_INCOMPATIBLE_DRIVER",
        VkResult.ErrorTooManyObjects => "VK_ERROR_TOO_MANY_OBJECTS",
        VkResult.ErrorFormatNotSupported => "VK_ERROR_FORMAT_NOT_SUPPORTED",
        VkResult.ErrorFragmentedPool => "VK_ERROR_FRAGMENTED_POOL",
        VkResult.ErrorUnknown => "VK_ERROR_UNKNOWN",
        VkResult.ErrorOutOfPoolMemory => "VK_ERROR_OUT_OF_POOL_MEMORY",
        VkResult.ErrorSurfaceLostKHR => "VK_ERROR_SURFACE_LOST_KHR",
        VkResult.ErrorNativeWindowInUseKHR => "VK_ERROR_NATIVE_WINDOW_IN_USE_KHR",
        VkResult.ErrorOutOfDateKHR => "VK_ERROR_OUT_OF_DATE_KHR",
        VkResult.ErrorIncompatibleDisplayKHR => "VK_ERROR_INCOMPATIBLE_DISPLAY_KHR",
        VkResult.ErrorValidationFailed => "VK_ERROR_VALIDATION_FAILED",
        VkResult.ErrorFullScreenExclusiveModeLostEXT => "VK_ERROR_FULL_SCREEN_EXCLUSIVE_MODE_LOST_EXT",
        _ => $"VkResult {(int)result}",
    };

    public static string Format(VkFormat format) => format switch
    {
        VkFormat.Undefined => "UNDEFINED",
        VkFormat.R8Uint => "R8_UINT",
        VkFormat.R8Unorm => "R8_UNORM",
        VkFormat.R8G8B8A8Unorm => "R8G8B8A8_UNORM",
        VkFormat.R8G8B8A8Srgb => "R8G8B8A8_SRGB",
        VkFormat.B8G8R8A8Unorm => "B8G8R8A8_UNORM",
        VkFormat.B8G8R8A8Srgb => "B8G8R8A8_SRGB",
        VkFormat.A2B10G10R10UnormPack32 => "A2B10G10R10_UNORM_PACK32",
        VkFormat.A2R10G10B10UnormPack32 => "A2R10G10B10_UNORM_PACK32",
        VkFormat.R16G16B16A16Sfloat => "R16G16B16A16_SFLOAT",
        _ => $"VkFormat {(int)format}",
    };

    public static string ColorSpace(VkColorSpaceKHR colorSpace) => colorSpace switch
    {
        VkColorSpaceKHR.SrgbNonLinear => "SRGB_NONLINEAR",
        VkColorSpaceKHR.ExtendedSrgbLinearEXT => "EXTENDED_SRGB_LINEAR",
        VkColorSpaceKHR.Hdr10St2084EXT => "HDR10_ST2084",
        _ => $"VkColorSpaceKHR {(int)colorSpace}",
    };

    public static string PresentMode(VkPresentModeKHR mode) => mode switch
    {
        VkPresentModeKHR.Immediate => "IMMEDIATE",
        VkPresentModeKHR.Mailbox => "MAILBOX",
        VkPresentModeKHR.Fifo => "FIFO",
        VkPresentModeKHR.FifoRelaxed => "FIFO_RELAXED",
        VkPresentModeKHR.FifoLatestReady => "FIFO_LATEST_READY",
        _ => $"VkPresentModeKHR {(int)mode}",
    };

    public static string DeviceType(VkPhysicalDeviceType type) => type switch
    {
        VkPhysicalDeviceType.DiscreteGpu => "discrete GPU",
        VkPhysicalDeviceType.IntegratedGpu => "integrated GPU",
        VkPhysicalDeviceType.VirtualGpu => "virtual GPU",
        VkPhysicalDeviceType.Cpu => "CPU",
        _ => "other",
    };

    public static string Version(VkVersion version) => $"{version.Major}.{version.Minor}.{version.Patch}";
}

/// <summary>Result checking for Vulkan calls.</summary>
internal static class VkCheck
{
    /// <summary>Throws <see cref="VulkanRendererException"/> for error results (negative VkResult).</summary>
    public static void Check(this VkResult result, string operation)
    {
        if (result < VkResult.Success)
            Throw(result, operation);
    }

    public static VulkanRendererException Error(VkResult result, string operation) =>
        new($"{operation} failed", (int)result, VkNames.Result(result));

    private static void Throw(VkResult result, string operation) => throw Error(result, operation);
}
