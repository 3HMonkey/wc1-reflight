using Vortice.Vulkan;

namespace WingCommander.Render.Vulkan.Internal;

/// <summary>
/// The window's swapchain: images, views and one "render finished" semaphore per image (a
/// present waits on it; it may only be reused once the same image index is acquired again).
/// </summary>
/// <remarks>
/// Colour: the palette holds sRGB-encoded 8-bit values meant to be shown as they are (like the
/// VGA DAC on a CRT, and like the SDL_Renderer path). A UNORM format with SRGB_NONLINEAR colour
/// space stores the shader's output unchanged, so palette bytes reach the display exactly.
/// B8G8R8A8_UNORM / R8G8B8A8_UNORM are preferred, 10-bit UNORM next. Only if the surface offers
/// nothing but *_SRGB formats is one used, and the shader then decodes to linear first so the
/// hardware re-encoding restores the palette values (within rounding).
/// </remarks>
internal sealed unsafe class Swapchain : IDisposable
{
    private readonly GpuContext _gpu;

    private Swapchain(GpuContext gpu)
    {
        _gpu = gpu;
    }

    public VkSwapchainKHR Handle { get; private set; }

    public VkFormat Format { get; private set; }

    public VkColorSpaceKHR ColorSpace { get; private set; }

    public VkPresentModeKHR PresentMode { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>The settings value this swapchain was created for (FIFO vs MAILBOX/IMMEDIATE).</summary>
    public bool VSync { get; private set; }

    /// <summary>Drawable size reported by the window when this swapchain was created.</summary>
    public (int Width, int Height) RequestedSize { get; private set; }

    /// <summary>The images allow TRANSFER_SRC and the format can be converted to RGBA for capture.</summary>
    public bool SupportsCapture { get; private set; }

    /// <summary>The format is *_SRGB: the shader outputs linear values.</summary>
    public bool EncodeLinear { get; private set; }

    public VkImage[] Images { get; private set; } = [];

    public VkImageView[] Views { get; private set; } = [];

    public VkSemaphore[] RenderFinished { get; private set; } = [];

    /// <summary>
    /// Creates a swapchain for the current surface size, or returns null while the surface has
    /// no area (minimised window). <paramref name="previous"/> is passed as oldSwapchain; the
    /// caller destroys it afterwards.
    /// </summary>
    public static Swapchain? Create(GpuContext gpu, (int Width, int Height) drawable, bool vsync, Swapchain? previous, RendererLog log)
    {
        VkSurfaceCapabilitiesKHR caps;
        gpu.InstanceApi.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(gpu.PhysicalDevice, gpu.Surface, &caps)
            .Check("vkGetPhysicalDeviceSurfaceCapabilitiesKHR");

        uint width, height;
        if (caps.currentExtent.width != uint.MaxValue)
        {
            width = caps.currentExtent.width;
            height = caps.currentExtent.height;
        }
        else
        {
            // The window system lets the swapchain decide (Wayland): use the drawable pixel size.
            width = Math.Clamp((uint)Math.Max(drawable.Width, 0), caps.minImageExtent.width, caps.maxImageExtent.width);
            height = Math.Clamp((uint)Math.Max(drawable.Height, 0), caps.minImageExtent.height, caps.maxImageExtent.height);
        }
        if (width == 0 || height == 0)
            return null;

        VkSurfaceFormatKHR surfaceFormat = ChooseFormat(gpu, out bool encodeLinear);
        VkPresentModeKHR presentMode = ChoosePresentMode(gpu, vsync);

        uint imageCount = caps.minImageCount + 1;
        if (caps.maxImageCount > 0 && imageCount > caps.maxImageCount)
            imageCount = caps.maxImageCount;

        bool transferSource = (caps.supportedUsageFlags & VkImageUsageFlags.TransferSrc) != 0;
        VkImageUsageFlags usage = VkImageUsageFlags.ColorAttachment | (transferSource ? VkImageUsageFlags.TransferSrc : VkImageUsageFlags.None);

        VkCompositeAlphaFlagsKHR compositeAlpha = VkCompositeAlphaFlagsKHR.Opaque;
        foreach (var candidate in (ReadOnlySpan<VkCompositeAlphaFlagsKHR>)[VkCompositeAlphaFlagsKHR.Opaque, VkCompositeAlphaFlagsKHR.Inherit, VkCompositeAlphaFlagsKHR.PreMultiplied, VkCompositeAlphaFlagsKHR.PostMultiplied])
        {
            if ((caps.supportedCompositeAlpha & candidate) != 0)
            {
                compositeAlpha = candidate;
                break;
            }
        }

        var createInfo = new VkSwapchainCreateInfoKHR
        {
            surface = gpu.Surface,
            minImageCount = imageCount,
            imageFormat = surfaceFormat.format,
            imageColorSpace = surfaceFormat.colorSpace,
            imageExtent = new VkExtent2D { width = width, height = height },
            imageArrayLayers = 1,
            imageUsage = usage,
            imageSharingMode = VkSharingMode.Exclusive,
            preTransform = (caps.supportedTransforms & VkSurfaceTransformFlagsKHR.Identity) != 0 ? VkSurfaceTransformFlagsKHR.Identity : caps.currentTransform,
            compositeAlpha = compositeAlpha,
            presentMode = presentMode,
            clipped = true,
            oldSwapchain = previous?.Handle ?? VkSwapchainKHR.Null,
        };

        var swapchain = new Swapchain(gpu)
        {
            Format = surfaceFormat.format,
            ColorSpace = surfaceFormat.colorSpace,
            PresentMode = presentMode,
            Width = (int)width,
            Height = (int)height,
            VSync = vsync,
            RequestedSize = drawable,
            EncodeLinear = encodeLinear,
            SupportsCapture = transferSource && Readback.CanConvert(surfaceFormat.format),
        };
        try
        {
            VkSwapchainKHR handle;
            gpu.DeviceApi.vkCreateSwapchainKHR(&createInfo, null, &handle).Check("vkCreateSwapchainKHR");
            swapchain.Handle = handle;

            uint count = 0;
            gpu.DeviceApi.vkGetSwapchainImagesKHR(handle, &count, null).Check("vkGetSwapchainImagesKHR");
            var images = new VkImage[count];
            fixed (VkImage* p = images)
                gpu.DeviceApi.vkGetSwapchainImagesKHR(handle, &count, p).Check("vkGetSwapchainImagesKHR");
            swapchain.Images = images;
            swapchain.Views = new VkImageView[count];
            swapchain.RenderFinished = new VkSemaphore[count];
            for (int i = 0; i < count; i++)
            {
                swapchain.Views[i] = gpu.CreateView(images[i], surfaceFormat.format);
                var semaphoreInfo = new VkSemaphoreCreateInfo();
                VkSemaphore semaphore;
                gpu.DeviceApi.vkCreateSemaphore(&semaphoreInfo, null, &semaphore).Check("vkCreateSemaphore");
                swapchain.RenderFinished[i] = semaphore;
            }
        }
        catch
        {
            swapchain.Dispose();
            throw;
        }

        log.Info($"Swapchain {width}x{height} {VkNames.Format(surfaceFormat.format)}/{VkNames.ColorSpace(surfaceFormat.colorSpace)}, " +
                 $"{VkNames.PresentMode(presentMode)}{(vsync ? " (vsync)" : string.Empty)}, {swapchain.Images.Length} images" +
                 (encodeLinear ? ", sRGB format: shader decodes palette colours to linear" : string.Empty));
        return swapchain;
    }

    public void Dispose()
    {
        foreach (var view in Views)
        {
            if (view.IsNotNull)
                _gpu.DeviceApi.vkDestroyImageView(view);
        }
        foreach (var semaphore in RenderFinished)
        {
            if (semaphore.IsNotNull)
                _gpu.DeviceApi.vkDestroySemaphore(semaphore);
        }
        if (Handle.IsNotNull)
            _gpu.DeviceApi.vkDestroySwapchainKHR(Handle);
        Views = [];
        RenderFinished = [];
        Images = [];
        Handle = VkSwapchainKHR.Null;
    }

    private static VkSurfaceFormatKHR ChooseFormat(GpuContext gpu, out bool encodeLinear)
    {
        uint count = 0;
        gpu.InstanceApi.vkGetPhysicalDeviceSurfaceFormatsKHR(gpu.PhysicalDevice, gpu.Surface, &count, null).Check("vkGetPhysicalDeviceSurfaceFormatsKHR");
        var formats = new VkSurfaceFormatKHR[count];
        fixed (VkSurfaceFormatKHR* p = formats)
            gpu.InstanceApi.vkGetPhysicalDeviceSurfaceFormatsKHR(gpu.PhysicalDevice, gpu.Surface, &count, p).Check("vkGetPhysicalDeviceSurfaceFormatsKHR");

        encodeLinear = false;
        if (count == 1 && formats[0].format == VkFormat.Undefined)
            return new VkSurfaceFormatKHR { format = VkFormat.B8G8R8A8Unorm, colorSpace = VkColorSpaceKHR.SrgbNonLinear };

        ReadOnlySpan<VkFormat> exact = [VkFormat.B8G8R8A8Unorm, VkFormat.R8G8B8A8Unorm, VkFormat.A2B10G10R10UnormPack32, VkFormat.A2R10G10B10UnormPack32];
        foreach (VkFormat wanted in exact)
        {
            foreach (var format in formats)
            {
                if (format.format == wanted && format.colorSpace == VkColorSpaceKHR.SrgbNonLinear)
                    return format;
            }
        }
        foreach (var format in formats)
        {
            if (format.colorSpace == VkColorSpaceKHR.SrgbNonLinear && IsSrgb(format.format))
            {
                encodeLinear = true;
                return format;
            }
        }
        foreach (var format in formats)
        {
            if (format.colorSpace == VkColorSpaceKHR.SrgbNonLinear)
            {
                encodeLinear = IsSrgb(format.format);
                return format;
            }
        }
        encodeLinear = IsSrgb(formats[0].format);
        return formats[0];
    }

    private static VkPresentModeKHR ChoosePresentMode(GpuContext gpu, bool vsync)
    {
        if (vsync)
            return VkPresentModeKHR.Fifo; // always supported
        uint count = 0;
        gpu.InstanceApi.vkGetPhysicalDeviceSurfacePresentModesKHR(gpu.PhysicalDevice, gpu.Surface, &count, null).Check("vkGetPhysicalDeviceSurfacePresentModesKHR");
        var modes = new VkPresentModeKHR[count];
        fixed (VkPresentModeKHR* p = modes)
            gpu.InstanceApi.vkGetPhysicalDeviceSurfacePresentModesKHR(gpu.PhysicalDevice, gpu.Surface, &count, p).Check("vkGetPhysicalDeviceSurfacePresentModesKHR");
        if (Array.IndexOf(modes, VkPresentModeKHR.Mailbox) >= 0)
            return VkPresentModeKHR.Mailbox; // no tearing, newest frame wins
        if (Array.IndexOf(modes, VkPresentModeKHR.Immediate) >= 0)
            return VkPresentModeKHR.Immediate;
        return VkPresentModeKHR.Fifo;
    }

    private static bool IsSrgb(VkFormat format) =>
        format is VkFormat.B8G8R8A8Srgb or VkFormat.R8G8B8A8Srgb or VkFormat.A8B8G8R8SrgbPack32;
}
