namespace WingCommander.Render.Vulkan;

/// <summary>
/// What the Vulkan renderer needs from the window system. The SDL3 host implements it
/// (SDL_Vulkan_GetInstanceExtensions, SDL_Vulkan_CreateSurface, SDL_GetWindowSizeInPixels), so the
/// renderer itself has no SDL dependency. Handles are passed as raw pointer-sized values.
/// </summary>
public interface IVulkanSurfaceSource
{
    /// <summary>Instance extensions the window system needs (e.g. VK_KHR_surface, VK_KHR_win32_surface).</summary>
    IReadOnlyList<string> RequiredInstanceExtensions { get; }

    /// <summary>
    /// The loader's vkGetInstanceProcAddr if the window system already loaded Vulkan
    /// (SDL_Vulkan_GetVkGetInstanceProcAddr), or 0 to let the renderer load the system loader.
    /// </summary>
    nint GetInstanceProcAddr { get; }

    /// <summary>Creates a VkSurfaceKHR for <paramref name="instance"/> (a VkInstance handle); returns the surface handle.</summary>
    ulong CreateSurface(nint instance);

    /// <summary>Destroys the surface created by <see cref="CreateSurface"/> (called before the instance is destroyed).</summary>
    void DestroySurface(nint instance, ulong surface);

    /// <summary>Current drawable size in physical pixels.</summary>
    (int Width, int Height) DrawableSize { get; }
}
