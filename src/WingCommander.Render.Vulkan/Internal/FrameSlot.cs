using Vortice.Vulkan;

namespace WingCommander.Render.Vulkan.Internal;

/// <summary>
/// Per-frame-in-flight objects. A slot is reused only after its fence signalled, which also
/// makes its staging region, command pool, acquire semaphore and capture buffer free again.
/// </summary>
internal sealed unsafe class FrameSlot
{
    private FrameSlot(int index)
    {
        Index = index;
    }

    public int Index { get; }

    public VkCommandPool CommandPool { get; private set; }

    public VkCommandBuffer CommandBuffer { get; private set; }

    /// <summary>Signalled when the slot's last submission completed (created unsignalled).</summary>
    public VkFence Fence { get; private set; }

    /// <summary>Signalled by vkAcquireNextImageKHR, waited on by the submission (swapchain mode only).</summary>
    public VkSemaphore ImageAvailable { get; private set; }

    /// <summary>A submission is pending on <see cref="Fence"/>.</summary>
    public bool Submitted { get; set; }

    /// <summary>Frame number of the last submission.</summary>
    public long FrameNumber { get; set; }

    /// <summary>Host-visible buffer receiving a captured frame (allocated on the first capture).</summary>
    public GpuBuffer Readback;

    public bool CapturePending { get; set; }

    public int CaptureWidth { get; set; }

    public int CaptureHeight { get; set; }

    public VkFormat CaptureFormat { get; set; }

    public static FrameSlot Create(GpuContext gpu, int index, bool presenting)
    {
        var slot = new FrameSlot(index);
        try
        {
            var api = gpu.DeviceApi;
            var poolInfo = new VkCommandPoolCreateInfo { flags = VkCommandPoolCreateFlags.Transient, queueFamilyIndex = gpu.QueueFamily };
            VkCommandPool pool;
            api.vkCreateCommandPool(&poolInfo, null, &pool).Check("vkCreateCommandPool");
            slot.CommandPool = pool;

            var allocateInfo = new VkCommandBufferAllocateInfo { commandPool = pool, level = VkCommandBufferLevel.Primary, commandBufferCount = 1 };
            VkCommandBuffer commandBuffer;
            api.vkAllocateCommandBuffers(&allocateInfo, &commandBuffer).Check("vkAllocateCommandBuffers");
            slot.CommandBuffer = commandBuffer;

            var fenceInfo = new VkFenceCreateInfo();
            VkFence fence;
            api.vkCreateFence(&fenceInfo, null, &fence).Check("vkCreateFence");
            slot.Fence = fence;

            if (presenting)
            {
                var semaphoreInfo = new VkSemaphoreCreateInfo();
                VkSemaphore semaphore;
                api.vkCreateSemaphore(&semaphoreInfo, null, &semaphore).Check("vkCreateSemaphore");
                slot.ImageAvailable = semaphore;
            }
            gpu.SetName(VkObjectType.CommandBuffer, (ulong)commandBuffer.Handle, $"frame slot {index}");
            return slot;
        }
        catch
        {
            slot.Destroy(gpu);
            throw;
        }
    }

    public void Destroy(GpuContext gpu)
    {
        var api = gpu.DeviceApi;
        if (api is null)
            return;
        if (CommandPool.IsNotNull)
            api.vkDestroyCommandPool(CommandPool); // frees the command buffer
        if (Fence.IsNotNull)
            api.vkDestroyFence(Fence);
        if (ImageAvailable.IsNotNull)
            api.vkDestroySemaphore(ImageAvailable);
        gpu.Destroy(ref Readback);
        CommandPool = VkCommandPool.Null;
        CommandBuffer = VkCommandBuffer.Null;
        Fence = VkFence.Null;
        ImageAvailable = VkSemaphore.Null;
        Submitted = false;
        CapturePending = false;
    }
}
