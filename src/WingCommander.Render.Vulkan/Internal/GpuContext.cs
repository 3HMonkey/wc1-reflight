using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace WingCommander.Render.Vulkan.Internal;

/// <summary>
/// Instance, optional window surface, the selected physical device, the logical device with its
/// single graphics(+present) queue, the core-1.3-or-KHR entry points, and allocation helpers.
/// Everything here lives as long as the renderer.
/// </summary>
internal sealed unsafe class GpuContext : IDisposable
{
    public const string ValidationLayerName = "VK_LAYER_KHRONOS_validation";
    public const string DebugUtilsExtension = "VK_EXT_debug_utils";
    public const string PortabilityEnumerationExtension = "VK_KHR_portability_enumeration";
    public const string LayerSettingsExtension = "VK_EXT_layer_settings";
    public const string ValidationFeaturesExtension = "VK_EXT_validation_features";

    /// <summary>How synchronization validation was requested from the validation layer.</summary>
    private enum SyncValidationRequest
    {
        None,
        LayerSettings,      // VK_EXT_layer_settings: validate_sync = true
        ValidationFeatures, // VK_EXT_validation_features: SYNCHRONIZATION_VALIDATION
    }

    private readonly IVulkanSurfaceSource? _surfaceSource;
    private readonly RendererLog _log;
    private DebugMessenger? _debugMessenger;
    private VkDebugUtilsMessengerEXT _messenger;
    private VkPhysicalDeviceMemoryProperties _memory;

    // Entry points selected at device creation: core 1.3 names or the KHR aliases on 1.2.
    public delegate* unmanaged<VkCommandBuffer, VkRenderingInfo*, void> CmdBeginRendering;
    public delegate* unmanaged<VkCommandBuffer, void> CmdEndRendering;
    public delegate* unmanaged<VkCommandBuffer, VkDependencyInfo*, void> CmdPipelineBarrier2;
    public delegate* unmanaged<VkQueue, uint, VkSubmitInfo2*, VkFence, VkResult> QueueSubmit2;

    private GpuContext(IVulkanSurfaceSource? surfaceSource, RendererLog log)
    {
        _surfaceSource = surfaceSource;
        _log = log;
    }

    public VkInstance Instance { get; private set; }

    public VkInstanceApi InstanceApi { get; private set; } = null!;

    public VkSurfaceKHR Surface { get; private set; }

    public VkPhysicalDevice PhysicalDevice { get; private set; }

    public VkDevice Device { get; private set; }

    public VkDeviceApi DeviceApi { get; private set; } = null!;

    public uint QueueFamily { get; private set; }

    public VkQueue Queue { get; private set; }

    public bool DebugUtils { get; private set; }

    public VulkanCapabilities Capabilities { get; private set; } = null!;

    public static GpuContext Create(IVulkanSurfaceSource? surfaceSource, VulkanRendererOptions options, RendererLog log, VulkanRendererStatistics statistics)
    {
        var context = new GpuContext(surfaceSource, log);
        try
        {
            context.Initialize(options, statistics);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private void Initialize(VulkanRendererOptions options, VulkanRendererStatistics statistics)
    {
        VulkanLoader.EnsureLoaded(_surfaceSource?.GetInstanceProcAddr ?? 0);

        VkVersion loaderVersion = vkEnumerateInstanceVersion();
        if (loaderVersion < VkVersion.Version_1_2)
        {
            throw new VulkanUnavailableException(
                $"The Vulkan loader supports Vulkan {VkNames.Version(loaderVersion)} only; the renderer needs 1.2. Update the GPU driver.");
        }
        VkVersion apiVersion = loaderVersion >= VkVersion.Version_1_3 ? VkVersion.Version_1_3 : VkVersion.Version_1_2;
        if (options.MaxApiVersion is { } cap)
        {
            var capVersion = new VkVersion((uint)cap.Major, (uint)Math.Max(0, cap.Minor), 0);
            if (capVersion < VkVersion.Version_1_2)
                throw new ArgumentException("MaxApiVersion must be at least 1.2.", nameof(options));
            if (capVersion < apiVersion)
                apiVersion = capVersion;
        }

        bool portabilityEnumeration = CreateInstance(options, apiVersion, statistics);
        CreateSurface();
        PhysicalDeviceCandidate chosen = SelectPhysicalDevice(options, apiVersion, out List<string> available);
        CreateDevice(chosen);

        Capabilities = new VulkanCapabilities
        {
            DeviceName = chosen.Name,
            DeviceType = VkNames.DeviceType(chosen.Type),
            VendorId = chosen.VendorId,
            DeviceId = chosen.DeviceId,
            DriverName = chosen.DriverName,
            DriverInfo = chosen.DriverInfo,
            LoaderVersion = ToVersion(loaderVersion),
            InstanceApiVersion = ToVersion(apiVersion),
            DeviceApiVersion = ToVersion(chosen.DeviceApiVersion),
            ApiVersion = ToVersion(chosen.EffectiveApiVersion),
            UsesVulkan13Core = chosen.Core13,
            PortabilitySubset = chosen.HasPortabilitySubset,
            PortabilityEnumeration = portabilityEnumeration,
            ValidationEnabled = _validationEnabled,
            SynchronizationValidation = _synchronizationValidation,
            DebugUtils = DebugUtils,
            AccelerationStructure = chosen.AccelerationStructure,
            RayQuery = chosen.RayQuery,
            RayTracingPipeline = chosen.RayTracingPipeline,
            BufferDeviceAddress = chosen.BufferDeviceAddress,
            DescriptorIndexing = chosen.DescriptorIndexing,
            TimelineSemaphores = chosen.TimelineSemaphores,
            MaxColorSamples = chosen.MaxColorSamples,
            MaxImageDimension2D = chosen.MaxImageDimension2D,
            DeviceLocalMemoryBytes = chosen.DeviceLocalMemoryBytes,
            AvailableDevices = available,
        };
    }

    private bool _validationEnabled;
    private bool _synchronizationValidation;

    private bool CreateInstance(VulkanRendererOptions options, VkVersion apiVersion, VulkanRendererStatistics statistics)
    {
        HashSet<string> available = EnumerateInstanceExtensions(null);
        using var extensions = new Utf8StringList();
        using var layers = new Utf8StringList();

        if (_surfaceSource is not null)
        {
            var missing = new List<string>();
            foreach (string name in _surfaceSource.RequiredInstanceExtensions)
            {
                if (!available.Contains(name))
                    missing.Add(name);
                extensions.Add(name);
            }
            if (missing.Count > 0)
            {
                throw new VulkanUnavailableException(
                    $"The Vulkan driver lacks instance extension(s) the window system needs: {string.Join(", ", missing)}.");
            }
        }

        bool portability = available.Contains(PortabilityEnumerationExtension);
        if (portability)
            extensions.Add(PortabilityEnumerationExtension);

        var syncRequest = SyncValidationRequest.None;
        ValidationMode mode = ResolveValidationMode(options.Validation);
        if (mode != ValidationMode.Disabled)
        {
            if (EnumerateInstanceLayers().Contains(ValidationLayerName))
            {
                layers.Add(ValidationLayerName);
                _validationEnabled = true;
                HashSet<string> layerExtensions;
                fixed (byte* layerName = "VK_LAYER_KHRONOS_validation\0"u8)
                    layerExtensions = EnumerateInstanceExtensions(layerName);
                if (available.Contains(DebugUtilsExtension) || layerExtensions.Contains(DebugUtilsExtension))
                {
                    extensions.Add(DebugUtilsExtension);
                    DebugUtils = true;
                }

                // Synchronization validation (hazards between commands, submissions and the
                // presentation engine) is off by default in the layer: switch it on through the
                // layer settings extension, or the older validation features structure.
                if (options.SynchronizationValidation && !SyncValidationDisabledByEnvironment())
                {
                    if (layerExtensions.Contains(LayerSettingsExtension) || available.Contains(LayerSettingsExtension))
                    {
                        extensions.Add(LayerSettingsExtension);
                        syncRequest = SyncValidationRequest.LayerSettings;
                    }
                    else if (layerExtensions.Contains(ValidationFeaturesExtension) || available.Contains(ValidationFeaturesExtension))
                    {
                        extensions.Add(ValidationFeaturesExtension);
                        syncRequest = SyncValidationRequest.ValidationFeatures;
                    }
                    else
                    {
                        _log.Debug("The validation layer offers neither VK_EXT_layer_settings nor VK_EXT_validation_features; synchronization validation stays off.");
                    }
                }
            }
            else if (mode == ValidationMode.Required)
            {
                throw new VulkanUnavailableException(
                    "Validation is required (ValidationMode.Required or WC1_VULKAN_VALIDATION=1) but VK_LAYER_KHRONOS_validation " +
                    "is not installed (install the Vulkan SDK or point VK_ADD_LAYER_PATH at the layer).");
            }
            else
            {
                _log.Debug("VK_LAYER_KHRONOS_validation is not installed; running without validation.");
            }
        }

        using var names = new Utf8StringList();
        names.Add(options.ApplicationName);
        names.Add("WingCommander.Render.Vulkan");
        var applicationInfo = new VkApplicationInfo
        {
            pApplicationName = names.Pointer[0],
            applicationVersion = new VkVersion(0, 1, 0),
            pEngineName = names.Pointer[1],
            engineVersion = new VkVersion(0, 1, 0),
            apiVersion = apiVersion,
        };
        var createInfo = new VkInstanceCreateInfo
        {
            flags = portability ? VkInstanceCreateFlags.EnumeratePortabilityKHR : VkInstanceCreateFlags.None,
            pApplicationInfo = &applicationInfo,
            enabledLayerCount = (uint)layers.Count,
            ppEnabledLayerNames = layers.Pointer,
            enabledExtensionCount = (uint)extensions.Count,
            ppEnabledExtensionNames = extensions.Pointer,
        };

        VkDebugUtilsMessengerCreateInfoEXT messengerInfo = default;
        if (DebugUtils)
        {
            _debugMessenger = new DebugMessenger(_log, statistics);
            messengerInfo = _debugMessenger.CreateInfo();
            createInfo.pNext = &messengerInfo; // also reports problems of vkCreateInstance itself
        }

        VkResult result;
        VkInstance instance;
        fixed (byte* layerName = "VK_LAYER_KHRONOS_validation\0"u8)
        fixed (byte* validateSync = "validate_sync\0"u8)
        fixed (byte* messageLimit = "enable_message_limit\0"u8)
        {
            uint enabled = 1, disabled = 0;
            VkLayerSettingEXT* settings = stackalloc VkLayerSettingEXT[2];
            settings[0] = new VkLayerSettingEXT
            {
                pLayerName = layerName,
                pSettingName = validateSync,
                type = VkLayerSettingTypeEXT.Bool32,
                valueCount = 1,
                pValues = &enabled,
            };
            // Tests: report every occurrence, not only the first 10 of each message.
            settings[1] = new VkLayerSettingEXT
            {
                pLayerName = layerName,
                pSettingName = messageLimit,
                type = VkLayerSettingTypeEXT.Bool32,
                valueCount = 1,
                pValues = &disabled,
            };
            var layerSettings = new VkLayerSettingsCreateInfoEXT
            {
                settingCount = options.UnlimitedValidationMessages ? 2u : 1u,
                pSettings = settings,
            };
            VkValidationFeatureEnableEXT feature = VkValidationFeatureEnableEXT.SynchronizationValidation;
            var validationFeatures = new VkValidationFeaturesEXT { enabledValidationFeatureCount = 1, pEnabledValidationFeatures = &feature };
            if (syncRequest == SyncValidationRequest.LayerSettings)
            {
                layerSettings.pNext = createInfo.pNext;
                createInfo.pNext = &layerSettings;
            }
            else if (syncRequest == SyncValidationRequest.ValidationFeatures)
            {
                validationFeatures.pNext = createInfo.pNext;
                createInfo.pNext = &validationFeatures;
            }
            result = vkCreateInstance(&createInfo, null, &instance);
        }
        if (result != VkResult.Success)
        {
            string hint = result switch
            {
                VkResult.ErrorIncompatibleDriver => ": no driver supports the requested Vulkan version",
                VkResult.ErrorExtensionNotPresent => $": an instance extension is missing ({string.Join(", ", extensions.Names)})",
                VkResult.ErrorLayerNotPresent => ": the validation layer failed to load",
                _ => string.Empty,
            };
            throw new VulkanUnavailableException($"vkCreateInstance failed{hint}", (int)result, VkNames.Result(result));
        }
        Instance = instance;
        InstanceApi = new VkInstanceApi(instance);
        _synchronizationValidation = syncRequest != SyncValidationRequest.None;
        if (_synchronizationValidation)
        {
            _log.Debug(syncRequest == SyncValidationRequest.LayerSettings
                ? "Synchronization validation enabled (VK_EXT_layer_settings, validate_sync)."
                : "Synchronization validation enabled (VK_EXT_validation_features).");
        }

        if (DebugUtils)
        {
            messengerInfo.pNext = null;
            VkDebugUtilsMessengerEXT messenger;
            InstanceApi.vkCreateDebugUtilsMessengerEXT(&messengerInfo, null, &messenger).Check("vkCreateDebugUtilsMessengerEXT");
            _messenger = messenger;
        }
        return portability;
    }

    private void CreateSurface()
    {
        if (_surfaceSource is null)
            return;
        ulong handle;
        try
        {
            handle = _surfaceSource.CreateSurface(Instance.Handle);
        }
        catch (Exception e) when (e is not VulkanRendererException)
        {
            throw new VulkanUnavailableException($"The window system could not create a Vulkan surface: {e.Message}");
        }
        if (handle == 0)
            throw new VulkanUnavailableException("The window system returned a null Vulkan surface.");
        Surface = new VkSurfaceKHR(handle);
    }

    private PhysicalDeviceCandidate SelectPhysicalDevice(VulkanRendererOptions options, VkVersion apiVersion, out List<string> available)
    {
        uint count = 0;
        InstanceApi.vkEnumeratePhysicalDevices(&count, null).Check("vkEnumeratePhysicalDevices");
        if (count == 0)
            throw new VulkanUnavailableException("The Vulkan driver reports no physical device.");
        var handles = new VkPhysicalDevice[count];
        fixed (VkPhysicalDevice* p = handles)
            InstanceApi.vkEnumeratePhysicalDevices(&count, p).Check("vkEnumeratePhysicalDevices");

        var candidates = new List<PhysicalDeviceCandidate>((int)count);
        available = new List<string>((int)count);
        for (int i = 0; i < count; i++)
        {
            var candidate = PhysicalDeviceCandidate.Evaluate(InstanceApi, handles[i], i, apiVersion, Surface);
            candidates.Add(candidate);
            available.Add(candidate.Describe());
            _log.Debug(candidate.IsSuitable
                ? $"GPU {candidate.Describe()}: suitable, score {candidate.Score}"
                : $"GPU {candidate.Describe()}: unsuitable, {candidate.Rejection}");
        }

        string? preferred = options.PreferredDevice ?? Environment.GetEnvironmentVariable("WC1_VULKAN_DEVICE");
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            PhysicalDeviceCandidate? match = int.TryParse(preferred, out int index)
                ? candidates.Find(c => c.Index == index)
                : candidates.Find(c => c.Name.Contains(preferred, StringComparison.OrdinalIgnoreCase));
            if (match is { IsSuitable: true })
                return match;
            _log.Warning(match is null
                ? $"Preferred GPU '{preferred}' not found; selecting automatically."
                : $"Preferred GPU {match.Describe()} cannot be used ({match.Rejection}); selecting automatically.");
        }

        PhysicalDeviceCandidate? best = null;
        foreach (var candidate in candidates)
        {
            if (candidate.IsSuitable && (best is null || candidate.Score > best.Score))
                best = candidate;
        }
        if (best is null)
        {
            var reasons = new List<string>();
            foreach (var candidate in candidates)
                reasons.Add($"{candidate.Describe()}: {candidate.Rejection}");
            throw new VulkanUnavailableException($"No GPU can run the Vulkan renderer: {string.Join("; ", reasons)}.");
        }
        return best;
    }

    private void CreateDevice(PhysicalDeviceCandidate chosen)
    {
        PhysicalDevice = chosen.Handle;
        QueueFamily = chosen.QueueFamily;
        VkPhysicalDeviceMemoryProperties memoryProperties;
        InstanceApi.vkGetPhysicalDeviceMemoryProperties(PhysicalDevice, &memoryProperties);
        _memory = memoryProperties;

        using var extensions = new Utf8StringList();
        if (Surface.IsNotNull)
            extensions.Add(PhysicalDeviceCandidate.SwapchainExtension);
        if (!chosen.Core13)
        {
            extensions.Add(PhysicalDeviceCandidate.DynamicRenderingExtension);
            extensions.Add(PhysicalDeviceCandidate.Synchronization2Extension);
        }
        if (chosen.HasPortabilitySubset)
            extensions.Add(PhysicalDeviceCandidate.PortabilitySubsetExtension); // required whenever exposed

        var vulkan13 = new VkPhysicalDeviceVulkan13Features { dynamicRendering = true, synchronization2 = true };
        var synchronization2 = new VkPhysicalDeviceSynchronization2Features { synchronization2 = true };
        var dynamicRendering = new VkPhysicalDeviceDynamicRenderingFeatures { dynamicRendering = true, pNext = &synchronization2 };
        void* features = chosen.Core13 ? &vulkan13 : &dynamicRendering;

        float priority = 1.0f;
        var queueInfo = new VkDeviceQueueCreateInfo
        {
            queueFamilyIndex = QueueFamily,
            queueCount = 1,
            pQueuePriorities = &priority,
        };
        var createInfo = new VkDeviceCreateInfo
        {
            pNext = features,
            queueCreateInfoCount = 1,
            pQueueCreateInfos = &queueInfo,
            enabledExtensionCount = (uint)extensions.Count,
            ppEnabledExtensionNames = extensions.Pointer,
        };
        VkDevice device;
        VkResult result = InstanceApi.vkCreateDevice(PhysicalDevice, &createInfo, null, &device);
        if (result != VkResult.Success)
            throw new VulkanUnavailableException($"vkCreateDevice failed on {chosen.Name}", (int)result, VkNames.Result(result));
        Device = device;
        DeviceApi = new VkDeviceApi(InstanceApi, device);

        VkQueue queue;
        DeviceApi.vkGetDeviceQueue(QueueFamily, 0, &queue);
        Queue = queue;

        if (chosen.Core13)
        {
            CmdBeginRendering = (delegate* unmanaged<VkCommandBuffer, VkRenderingInfo*, void>)DeviceApi.vkCmdBeginRendering_ptr.Value;
            CmdEndRendering = (delegate* unmanaged<VkCommandBuffer, void>)DeviceApi.vkCmdEndRendering_ptr.Value;
            CmdPipelineBarrier2 = (delegate* unmanaged<VkCommandBuffer, VkDependencyInfo*, void>)DeviceApi.vkCmdPipelineBarrier2_ptr.Value;
            QueueSubmit2 = (delegate* unmanaged<VkQueue, uint, VkSubmitInfo2*, VkFence, VkResult>)DeviceApi.vkQueueSubmit2_ptr.Value;
        }
        else
        {
            CmdBeginRendering = (delegate* unmanaged<VkCommandBuffer, VkRenderingInfo*, void>)DeviceApi.vkCmdBeginRenderingKHR_ptr.Value;
            CmdEndRendering = (delegate* unmanaged<VkCommandBuffer, void>)DeviceApi.vkCmdEndRenderingKHR_ptr.Value;
            CmdPipelineBarrier2 = (delegate* unmanaged<VkCommandBuffer, VkDependencyInfo*, void>)DeviceApi.vkCmdPipelineBarrier2KHR_ptr.Value;
            QueueSubmit2 = (delegate* unmanaged<VkQueue, uint, VkSubmitInfo2*, VkFence, VkResult>)DeviceApi.vkQueueSubmit2KHR_ptr.Value;
        }
        if (CmdBeginRendering == null || CmdEndRendering == null || CmdPipelineBarrier2 == null || QueueSubmit2 == null)
            throw new VulkanUnavailableException($"The driver of {chosen.Name} did not provide the dynamic rendering / synchronization2 entry points.");
    }

    // ---- allocation helpers -------------------------------------------------------------

    public uint FindMemoryType(uint typeBits, VkMemoryPropertyFlags required, VkMemoryPropertyFlags preferred)
    {
        VkMemoryPropertyFlags wanted = required | preferred;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < _memory.memoryTypeCount; i++)
            {
                if ((typeBits & (1u << i)) != 0 && (_memory.memoryTypes[i].propertyFlags & wanted) == wanted)
                    return (uint)i;
            }
            wanted = required;
        }
        throw new VulkanRendererException($"No Vulkan memory type with properties {(int)required:x} (type bits {typeBits:x}).");
    }

    public VkMemoryPropertyFlags MemoryTypeFlags(uint memoryTypeIndex) => _memory.memoryTypes[(int)memoryTypeIndex].propertyFlags;

    /// <summary>Creates a buffer; host-visible buffers must be host-coherent and stay mapped for their lifetime.</summary>
    public GpuBuffer CreateBuffer(ulong size, VkBufferUsageFlags usage, bool hostVisible, bool preferHostCached, string name)
    {
        var createInfo = new VkBufferCreateInfo { size = size, usage = usage, sharingMode = VkSharingMode.Exclusive };
        VkBuffer buffer;
        DeviceApi.vkCreateBuffer(&createInfo, null, &buffer).Check("vkCreateBuffer");
        var result = new GpuBuffer { Buffer = buffer, Size = size };
        try
        {
            VkMemoryRequirements requirements;
            DeviceApi.vkGetBufferMemoryRequirements(buffer, &requirements);
            VkMemoryPropertyFlags required = hostVisible
                ? VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent
                : VkMemoryPropertyFlags.DeviceLocal;
            VkMemoryPropertyFlags preferred = hostVisible && preferHostCached ? VkMemoryPropertyFlags.HostCached : VkMemoryPropertyFlags.None;
            var allocateInfo = new VkMemoryAllocateInfo
            {
                allocationSize = requirements.size,
                memoryTypeIndex = FindMemoryType(requirements.memoryTypeBits, required, preferred),
            };
            VkDeviceMemory memory;
            DeviceApi.vkAllocateMemory(&allocateInfo, null, &memory).Check("vkAllocateMemory (buffer)");
            result.Memory = memory;
            DeviceApi.vkBindBufferMemory(buffer, memory, 0).Check("vkBindBufferMemory");
            if (hostVisible)
            {
                void* mapped;
                DeviceApi.vkMapMemory(memory, 0, ulong.MaxValue, 0, &mapped).Check("vkMapMemory");
                result.Mapped = (byte*)mapped;
            }
            SetName(VkObjectType.Buffer, buffer.Handle, name);
            return result;
        }
        catch
        {
            Destroy(ref result);
            throw;
        }
    }

    /// <summary>Creates a device-local 2D image (optimal tiling, initial layout UNDEFINED) and its colour view.</summary>
    public GpuImage CreateImage(uint width, uint height, VkFormat format, VkImageUsageFlags usage, string name)
    {
        var createInfo = new VkImageCreateInfo
        {
            imageType = VkImageType.Image2D,
            format = format,
            extent = new VkExtent3D { width = width, height = height, depth = 1 },
            mipLevels = 1,
            arrayLayers = 1,
            samples = VkSampleCountFlags.Count1,
            tiling = VkImageTiling.Optimal,
            usage = usage,
            sharingMode = VkSharingMode.Exclusive,
            initialLayout = VkImageLayout.Undefined,
        };
        VkImage image;
        DeviceApi.vkCreateImage(&createInfo, null, &image).Check($"vkCreateImage ({name}, {width}x{height} {VkNames.Format(format)})");
        var result = new GpuImage { Image = image, Format = format, Width = width, Height = height };
        try
        {
            VkMemoryRequirements requirements;
            DeviceApi.vkGetImageMemoryRequirements(image, &requirements);
            var allocateInfo = new VkMemoryAllocateInfo
            {
                allocationSize = requirements.size,
                memoryTypeIndex = FindMemoryType(requirements.memoryTypeBits, VkMemoryPropertyFlags.DeviceLocal, VkMemoryPropertyFlags.None),
            };
            VkDeviceMemory memory;
            DeviceApi.vkAllocateMemory(&allocateInfo, null, &memory).Check("vkAllocateMemory (image)");
            result.Memory = memory;
            DeviceApi.vkBindImageMemory(image, memory, 0).Check("vkBindImageMemory");
            result.View = CreateView(image, format);
            SetName(VkObjectType.Image, image.Handle, name);
            return result;
        }
        catch
        {
            Destroy(ref result);
            throw;
        }
    }

    public VkImageView CreateView(VkImage image, VkFormat format)
    {
        var createInfo = new VkImageViewCreateInfo
        {
            image = image,
            viewType = VkImageViewType.Image2D,
            format = format,
            subresourceRange = ColorRange,
        };
        VkImageView view;
        DeviceApi.vkCreateImageView(&createInfo, null, &view).Check("vkCreateImageView");
        return view;
    }

    public static VkImageSubresourceRange ColorRange => new()
    {
        aspectMask = VkImageAspectFlags.Color,
        baseMipLevel = 0,
        levelCount = 1,
        baseArrayLayer = 0,
        layerCount = 1,
    };

    public void Destroy(ref GpuBuffer buffer)
    {
        if (buffer.Buffer.IsNotNull)
            DeviceApi.vkDestroyBuffer(buffer.Buffer);
        if (buffer.Memory.IsNotNull)
            DeviceApi.vkFreeMemory(buffer.Memory); // also unmaps
        buffer = default;
    }

    public void Destroy(ref GpuImage image)
    {
        if (image.View.IsNotNull)
            DeviceApi.vkDestroyImageView(image.View);
        if (image.Image.IsNotNull)
            DeviceApi.vkDestroyImage(image.Image);
        if (image.Memory.IsNotNull)
            DeviceApi.vkFreeMemory(image.Memory);
        image = default;
    }

    /// <summary>Names an object for RenderDoc / validation messages (only with VK_EXT_debug_utils).</summary>
    public void SetName(VkObjectType type, ulong handle, string name)
    {
        if (DebugUtils && handle != 0)
            InstanceApi.vkSetDebugUtilsObjectNameEXT(Device, type, handle, name);
    }

    /// <summary>Sends a message through the debug messenger as if the layer reported it (tests of the reporting path).</summary>
    public bool SubmitDebugMessage(bool error, string text)
    {
        if (!DebugUtils || InstanceApi.vkSubmitDebugUtilsMessageEXT_ptr.Value == null)
            return false;
        using var strings = new Utf8StringList();
        strings.Add("WC1-Test-Message");
        strings.Add(text);
        var data = new VkDebugUtilsMessengerCallbackDataEXT
        {
            pMessageIdName = strings.Pointer[0],
            pMessage = strings.Pointer[1],
        };
        InstanceApi.vkSubmitDebugUtilsMessageEXT(
            error ? VkDebugUtilsMessageSeverityFlagsEXT.Error : VkDebugUtilsMessageSeverityFlagsEXT.Warning,
            VkDebugUtilsMessageTypeFlagsEXT.Validation, &data);
        return true;
    }

    public void WaitIdle()
    {
        if (Device.IsNotNull)
            DeviceApi.vkDeviceWaitIdle().Check("vkDeviceWaitIdle");
    }

    public void Dispose()
    {
        if (Device.IsNotNull)
        {
            DeviceApi.vkDestroyDevice();
            Device = default;
        }
        if (Surface.IsNotNull)
        {
            try
            {
                _surfaceSource?.DestroySurface(Instance.Handle, Surface.Handle);
            }
            catch (Exception e)
            {
                _log.Warning($"Destroying the window surface failed: {e.Message}");
            }
            Surface = default;
        }
        if (_messenger.IsNotNull)
        {
            InstanceApi.vkDestroyDebugUtilsMessengerEXT(_messenger);
            _messenger = default;
        }
        if (Instance.IsNotNull)
        {
            InstanceApi.vkDestroyInstance();
            Instance = default;
        }
        _debugMessenger?.Dispose();
        _debugMessenger = null;
    }

    private static ValidationMode ResolveValidationMode(ValidationMode requested)
    {
        string? environment = Environment.GetEnvironmentVariable("WC1_VULKAN_VALIDATION");
        return environment?.Trim().ToLowerInvariant() switch
        {
            "0" or "off" or "false" or "no" => ValidationMode.Disabled,
            "1" or "on" or "true" or "yes" => ValidationMode.Required,
            _ => requested,
        };
    }

    private static bool SyncValidationDisabledByEnvironment()
    {
        string? environment = Environment.GetEnvironmentVariable("WC1_VULKAN_VALIDATION_SYNC");
        return environment?.Trim().ToLowerInvariant() is "0" or "off" or "false" or "no";
    }

    private static HashSet<string> EnumerateInstanceExtensions(byte* layerName)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        uint count = 0;
        if (vkEnumerateInstanceExtensionProperties(layerName, &count, null) < VkResult.Success || count == 0)
            return names;
        var properties = new VkExtensionProperties[count];
        fixed (VkExtensionProperties* p = properties)
        {
            vkEnumerateInstanceExtensionProperties(layerName, &count, p).Check("vkEnumerateInstanceExtensionProperties");
            for (int i = 0; i < count; i++)
                names.Add(Utf8StringList.Read(p[i].extensionName));
        }
        return names;
    }

    private static HashSet<string> EnumerateInstanceLayers()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        uint count = 0;
        if (vkEnumerateInstanceLayerProperties(&count, null) < VkResult.Success || count == 0)
            return names;
        var properties = new VkLayerProperties[count];
        fixed (VkLayerProperties* p = properties)
        {
            vkEnumerateInstanceLayerProperties(&count, p).Check("vkEnumerateInstanceLayerProperties");
            for (int i = 0; i < count; i++)
                names.Add(Utf8StringList.Read(p[i].layerName));
        }
        return names;
    }

    private static Version ToVersion(VkVersion version) => new((int)version.Major, (int)version.Minor, (int)version.Patch);
}
