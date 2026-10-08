using Vortice.Vulkan;

namespace WingCommander.Render.Vulkan.Internal;

/// <summary>A buffer with its own memory allocation; <see cref="Mapped"/> is set for host-visible buffers (persistently mapped).</summary>
internal unsafe struct GpuBuffer
{
    public VkBuffer Buffer;
    public VkDeviceMemory Memory;
    public byte* Mapped;
    public ulong Size;

    public readonly bool IsNull => Buffer.IsNull;
}

/// <summary>A 2D image (one mip, one layer) with its own memory allocation and a colour view.</summary>
internal struct GpuImage
{
    public VkImage Image;
    public VkDeviceMemory Memory;
    public VkImageView View;
    public VkFormat Format;
    public uint Width;
    public uint Height;

    public readonly bool IsNull => Image.IsNull;
}
