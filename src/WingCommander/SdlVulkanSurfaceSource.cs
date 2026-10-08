using WingCommander.Host.Sdl;
using WingCommander.Render.Vulkan;

namespace WingCommander;

/// <summary>Connects the Vulkan renderer to the SDL window; the renderer itself has no SDL dependency.</summary>
internal sealed class SdlVulkanSurfaceSource(SdlHost host) : IVulkanSurfaceSource
{
    public IReadOnlyList<string> RequiredInstanceExtensions => host.VulkanInstanceExtensions;

    public nint GetInstanceProcAddr => host.VulkanGetInstanceProcAddr;

    public ulong CreateSurface(nint instance) => host.CreateVulkanSurface(instance);

    public void DestroySurface(nint instance, ulong surface) => host.DestroyVulkanSurface(instance, surface);

    public (int Width, int Height) DrawableSize => host.DrawableSize;
}
