using Vortice.Vulkan;

namespace WingCommander.Render.Vulkan.Internal;

/// <summary>
/// One enumerated physical device: whether it can run the renderer (and if not, why), its
/// score for automatic selection, and the roadmap capabilities it would offer.
/// </summary>
internal sealed unsafe class PhysicalDeviceCandidate
{
    public const string SwapchainExtension = "VK_KHR_swapchain";
    public const string DynamicRenderingExtension = "VK_KHR_dynamic_rendering";
    public const string Synchronization2Extension = "VK_KHR_synchronization2";
    public const string PortabilitySubsetExtension = "VK_KHR_portability_subset";
    public const string AccelerationStructureExtension = "VK_KHR_acceleration_structure";
    public const string DeferredHostOperationsExtension = "VK_KHR_deferred_host_operations";
    public const string RayQueryExtension = "VK_KHR_ray_query";
    public const string RayTracingPipelineExtension = "VK_KHR_ray_tracing_pipeline";

    private PhysicalDeviceCandidate(int index, VkPhysicalDevice handle)
    {
        Index = index;
        Handle = handle;
    }

    public int Index { get; }

    public VkPhysicalDevice Handle { get; }

    public string Name { get; private set; } = "?";

    public VkPhysicalDeviceType Type { get; private set; }

    public uint VendorId { get; private set; }

    public uint DeviceId { get; private set; }

    public VkVersion DeviceApiVersion { get; private set; }

    /// <summary>min(instance API version, device API version).</summary>
    public VkVersion EffectiveApiVersion { get; private set; }

    /// <summary>Dynamic rendering and synchronization2 are used from core 1.3 (else from the KHR extensions).</summary>
    public bool Core13 { get; private set; }

    public HashSet<string> Extensions { get; } = new(StringComparer.Ordinal);

    /// <summary>Queue family with graphics (and presentation to the surface, when there is one).</summary>
    public uint QueueFamily { get; private set; }

    /// <summary>Why the device cannot be used, or null when it can.</summary>
    public string? Rejection { get; private set; }

    public bool IsSuitable => Rejection is null;

    public string DriverName { get; private set; } = string.Empty;

    public string DriverInfo { get; private set; } = string.Empty;

    public bool AccelerationStructure { get; private set; }

    public bool RayQuery { get; private set; }

    public bool RayTracingPipeline { get; private set; }

    public bool BufferDeviceAddress { get; private set; }

    public bool DescriptorIndexing { get; private set; }

    public bool TimelineSemaphores { get; private set; }

    public int MaxColorSamples { get; private set; } = 1;

    public uint MaxImageDimension2D { get; private set; }

    public ulong DeviceLocalMemoryBytes { get; private set; }

    public bool HasPortabilitySubset => Extensions.Contains(PortabilitySubsetExtension);

    /// <summary>Automatic selection score: discrete over integrated over virtual over CPU, core 1.3 as a tie breaker.</summary>
    public int Score => Type switch
    {
        VkPhysicalDeviceType.DiscreteGpu => 1000,
        VkPhysicalDeviceType.IntegratedGpu => 500,
        VkPhysicalDeviceType.VirtualGpu => 200,
        VkPhysicalDeviceType.Cpu => 50,
        _ => 10,
    } + (Core13 ? 10 : 0);

    public string Describe() =>
        $"{Index}: {Name} ({VkNames.DeviceType(Type)}, Vulkan {VkNames.Version(DeviceApiVersion)})";

    public static PhysicalDeviceCandidate Evaluate(VkInstanceApi api, VkPhysicalDevice device, int index, VkVersion instanceApiVersion, VkSurfaceKHR surface)
    {
        var candidate = new PhysicalDeviceCandidate(index, device);
        candidate.Inspect(api, instanceApiVersion, surface);
        return candidate;
    }

    private void Inspect(VkInstanceApi api, VkVersion instanceApiVersion, VkSurfaceKHR surface)
    {
        VkPhysicalDeviceProperties properties;
        api.vkGetPhysicalDeviceProperties(Handle, &properties);
        Name = Utf8StringList.Read(properties.deviceName);
        Type = properties.deviceType;
        VendorId = properties.vendorID;
        DeviceId = properties.deviceID;
        DeviceApiVersion = properties.apiVersion;
        MaxImageDimension2D = properties.limits.maxImageDimension2D;
        MaxColorSamples = HighestSampleCount(properties.limits.framebufferColorSampleCounts);

        if (DeviceApiVersion < VkVersion.Version_1_2)
        {
            Rejection = $"supports Vulkan {VkNames.Version(DeviceApiVersion)} only (1.2 required)";
            return;
        }
        EffectiveApiVersion = DeviceApiVersion < instanceApiVersion ? DeviceApiVersion : instanceApiVersion;
        Core13 = EffectiveApiVersion >= VkVersion.Version_1_3;

        ReadExtensions(api);
        ReadMemory(api);
        ReadDriver(api);

        if (!FindQueueFamily(api, surface, out uint family))
        {
            Rejection = surface.IsNull
                ? "no graphics queue family"
                : "no queue family with both graphics and presentation to this window";
            return;
        }
        QueueFamily = family;

        if (surface.IsNotNull)
        {
            if (!Extensions.Contains(SwapchainExtension))
            {
                Rejection = $"{SwapchainExtension} missing";
                return;
            }
            uint formatCount = 0, modeCount = 0;
            api.vkGetPhysicalDeviceSurfaceFormatsKHR(Handle, surface, &formatCount, null);
            api.vkGetPhysicalDeviceSurfacePresentModesKHR(Handle, surface, &modeCount, null);
            if (formatCount == 0 || modeCount == 0)
            {
                Rejection = "the window surface offers no formats or present modes";
                return;
            }
        }

        if (!Core13 && (!Extensions.Contains(DynamicRenderingExtension) || !Extensions.Contains(Synchronization2Extension)))
        {
            Rejection = $"Vulkan {VkNames.Version(EffectiveApiVersion)} without {DynamicRenderingExtension} and {Synchronization2Extension}";
            return;
        }

        ReadFeatures(api, out bool dynamicRendering, out bool synchronization2);
        if (!dynamicRendering || !synchronization2)
            Rejection = "dynamicRendering or synchronization2 feature not supported";
    }

    private void ReadExtensions(VkInstanceApi api)
    {
        uint count = 0;
        api.vkEnumerateDeviceExtensionProperties(Handle, null, &count, null).Check("vkEnumerateDeviceExtensionProperties");
        if (count == 0)
            return;
        var properties = new VkExtensionProperties[count];
        fixed (VkExtensionProperties* p = properties)
        {
            api.vkEnumerateDeviceExtensionProperties(Handle, null, &count, p).Check("vkEnumerateDeviceExtensionProperties");
            for (int i = 0; i < count; i++)
                Extensions.Add(Utf8StringList.Read(p[i].extensionName));
        }
    }

    private void ReadMemory(VkInstanceApi api)
    {
        VkPhysicalDeviceMemoryProperties memory;
        api.vkGetPhysicalDeviceMemoryProperties(Handle, &memory);
        ulong total = 0;
        for (int i = 0; i < memory.memoryHeapCount; i++)
        {
            if ((memory.memoryHeaps[i].flags & VkMemoryHeapFlags.DeviceLocal) != 0)
                total += memory.memoryHeaps[i].size;
        }
        DeviceLocalMemoryBytes = total;
    }

    private void ReadDriver(VkInstanceApi api)
    {
        // VkPhysicalDeviceVulkan12Properties is valid: the effective API version is at least 1.2.
        var vulkan12 = new VkPhysicalDeviceVulkan12Properties();
        var properties2 = new VkPhysicalDeviceProperties2 { pNext = &vulkan12 };
        api.vkGetPhysicalDeviceProperties2(Handle, &properties2);
        DriverName = Utf8StringList.Read(vulkan12.driverName);
        DriverInfo = Utf8StringList.Read(vulkan12.driverInfo);
    }

    private bool FindQueueFamily(VkInstanceApi api, VkSurfaceKHR surface, out uint family)
    {
        family = 0;
        uint count = 0;
        api.vkGetPhysicalDeviceQueueFamilyProperties(Handle, &count, null);
        if (count == 0)
            return false;
        var families = new VkQueueFamilyProperties[count];
        fixed (VkQueueFamilyProperties* p = families)
            api.vkGetPhysicalDeviceQueueFamilyProperties(Handle, &count, p);
        for (uint i = 0; i < count; i++)
        {
            if ((families[i].queueFlags & VkQueueFlags.Graphics) == 0 || families[i].queueCount == 0)
                continue;
            if (surface.IsNotNull)
            {
                VkBool32 supported = false;
                if (api.vkGetPhysicalDeviceSurfaceSupportKHR(Handle, i, surface, &supported) != VkResult.Success || !supported)
                    continue;
            }
            family = i;
            return true;
        }
        return false;
    }

    private void ReadFeatures(VkInstanceApi api, out bool dynamicRendering, out bool synchronization2)
    {
        // Only structures of supported versions/extensions may be chained (VUID-VkPhysicalDeviceFeatures2-pNext).
        void* chain = null;
        var vulkan12 = new VkPhysicalDeviceVulkan12Features { pNext = chain };
        chain = &vulkan12;
        var vulkan13 = new VkPhysicalDeviceVulkan13Features();
        var dynamicRenderingKhr = new VkPhysicalDeviceDynamicRenderingFeatures();
        var synchronization2Khr = new VkPhysicalDeviceSynchronization2Features();
        if (Core13)
        {
            vulkan13.pNext = chain;
            chain = &vulkan13;
        }
        else
        {
            dynamicRenderingKhr.pNext = chain;
            chain = &dynamicRenderingKhr;
            synchronization2Khr.pNext = chain;
            chain = &synchronization2Khr;
        }

        var acceleration = new VkPhysicalDeviceAccelerationStructureFeaturesKHR();
        bool hasAcceleration = Extensions.Contains(AccelerationStructureExtension) && Extensions.Contains(DeferredHostOperationsExtension);
        if (hasAcceleration)
        {
            acceleration.pNext = chain;
            chain = &acceleration;
        }
        var rayQuery = new VkPhysicalDeviceRayQueryFeaturesKHR();
        bool hasRayQuery = Extensions.Contains(RayQueryExtension);
        if (hasRayQuery)
        {
            rayQuery.pNext = chain;
            chain = &rayQuery;
        }
        var rayPipeline = new VkPhysicalDeviceRayTracingPipelineFeaturesKHR();
        bool hasRayPipeline = Extensions.Contains(RayTracingPipelineExtension);
        if (hasRayPipeline)
        {
            rayPipeline.pNext = chain;
            chain = &rayPipeline;
        }

        var features2 = new VkPhysicalDeviceFeatures2 { pNext = chain };
        api.vkGetPhysicalDeviceFeatures2(Handle, &features2);

        dynamicRendering = Core13 ? vulkan13.dynamicRendering : dynamicRenderingKhr.dynamicRendering;
        synchronization2 = Core13 ? vulkan13.synchronization2 : synchronization2Khr.synchronization2;

        BufferDeviceAddress = vulkan12.bufferDeviceAddress;
        TimelineSemaphores = vulkan12.timelineSemaphore;
        DescriptorIndexing = vulkan12.descriptorIndexing && vulkan12.runtimeDescriptorArray
            && vulkan12.descriptorBindingPartiallyBound && vulkan12.shaderSampledImageArrayNonUniformIndexing;
        AccelerationStructure = hasAcceleration && acceleration.accelerationStructure;
        RayQuery = hasRayQuery && rayQuery.rayQuery;
        RayTracingPipeline = hasRayPipeline && rayPipeline.rayTracingPipeline;
    }

    private static int HighestSampleCount(VkSampleCountFlags counts)
    {
        for (int samples = 64; samples > 1; samples >>= 1)
        {
            if (((int)counts & samples) != 0)
                return samples;
        }
        return 1;
    }
}
