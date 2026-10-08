using Vortice.Vulkan;
using VorticeVulkan = Vortice.Vulkan.Vulkan;

namespace WingCommander.Render.Vulkan.Internal;

/// <summary>
/// Installs the global Vulkan entry points of Vortice.Vulkan. When the window system already
/// loaded Vulkan (SDL3 does for <c>SDL_WINDOW_VULKAN</c> windows) its <c>vkGetInstanceProcAddr</c>
/// is used, so SDL and the renderer talk to the same loader; otherwise the system loader is
/// opened (vulkan-1.dll, libvulkan.so.1, libvulkan.1.dylib or libMoltenVK.dylib).
/// </summary>
internal static unsafe class VulkanLoader
{
    private static readonly Lock Gate = new();
    private static nint s_installed;

    public static void EnsureLoaded(nint getInstanceProcAddr)
    {
        lock (Gate)
        {
            if (getInstanceProcAddr != 0)
            {
                if (s_installed == getInstanceProcAddr)
                    return;
                VorticeVulkan.vkGetInstanceProcAddr_ptr = (delegate* unmanaged<VkInstance, byte*, PFN_vkVoidFunction>)getInstanceProcAddr;
                VorticeVulkan.vkCreateInstance_ptr = VorticeVulkan.vkGetGlobalProcAddr("vkCreateInstance"u8);
                VorticeVulkan.vkEnumerateInstanceExtensionProperties_ptr = VorticeVulkan.vkGetGlobalProcAddr("vkEnumerateInstanceExtensionProperties"u8);
                VorticeVulkan.vkEnumerateInstanceLayerProperties_ptr = VorticeVulkan.vkGetGlobalProcAddr("vkEnumerateInstanceLayerProperties"u8);
                VorticeVulkan.vkEnumerateInstanceVersion_ptr = VorticeVulkan.vkGetGlobalProcAddr("vkEnumerateInstanceVersion"u8);
                s_installed = getInstanceProcAddr;
            }
            else if (s_installed == 0)
            {
                VkResult result;
                try
                {
                    result = VorticeVulkan.vkInitialize();
                }
                catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                {
                    throw new VulkanUnavailableException($"The Vulkan loader could not be loaded: {e.Message}");
                }
                if (result != VkResult.Success || VorticeVulkan.vkGetInstanceProcAddr_ptr == null)
                {
                    throw new VulkanUnavailableException(
                        "No Vulkan loader found (vulkan-1.dll on Windows, libvulkan.so.1 on Linux, libvulkan.1.dylib or " +
                        "libMoltenVK.dylib on macOS). Install or update the GPU driver (macOS: MoltenVK / Vulkan SDK).",
                        (int)result, VkNames.Result(result));
                }
                s_installed = (nint)VorticeVulkan.vkGetInstanceProcAddr_ptr;
            }

            if (VorticeVulkan.vkCreateInstance_ptr.Value == null || VorticeVulkan.vkEnumerateInstanceExtensionProperties_ptr.Value == null)
                throw new VulkanUnavailableException("The Vulkan loader does not export vkCreateInstance.");
        }
    }
}
