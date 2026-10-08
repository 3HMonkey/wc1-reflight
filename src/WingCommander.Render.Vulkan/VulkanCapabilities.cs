using System.Text;

namespace WingCommander.Render.Vulkan;

/// <summary>
/// What the selected GPU and driver offer. R1 only needs the baseline (Vulkan 1.2 + dynamic
/// rendering + synchronization2); the roadmap flags (ray tracing, bindless, timeline semaphores,
/// MSAA) are detected at start-up and logged so later render stages can opt in. Detection only:
/// none of the optional features are enabled on the device yet.
/// </summary>
public sealed class VulkanCapabilities
{
    public required string DeviceName { get; init; }

    /// <summary>"discrete GPU", "integrated GPU", "virtual GPU", "CPU" or "other".</summary>
    public required string DeviceType { get; init; }

    public uint VendorId { get; init; }

    public uint DeviceId { get; init; }

    public required string DriverName { get; init; }

    public required string DriverInfo { get; init; }

    /// <summary>Highest instance version the loader supports.</summary>
    public required Version LoaderVersion { get; init; }

    /// <summary>Version requested in VkApplicationInfo (1.3 when available, else 1.2).</summary>
    public required Version InstanceApiVersion { get; init; }

    /// <summary>Version the device reports.</summary>
    public required Version DeviceApiVersion { get; init; }

    /// <summary>The version the renderer programs against: min(instance, device).</summary>
    public required Version ApiVersion { get; init; }

    /// <summary>Dynamic rendering and synchronization2 come from core 1.3 (false: the KHR extensions on 1.2).</summary>
    public bool UsesVulkan13Core { get; init; }

    /// <summary>VK_KHR_portability_subset is enabled (MoltenVK and other layered implementations).</summary>
    public bool PortabilitySubset { get; init; }

    /// <summary>The instance was created with VK_KHR_portability_enumeration.</summary>
    public bool PortabilityEnumeration { get; init; }

    /// <summary>VK_LAYER_KHRONOS_validation is active.</summary>
    public bool ValidationEnabled { get; init; }

    /// <summary>The validation layer's synchronization validation is active.</summary>
    public bool SynchronizationValidation { get; init; }

    /// <summary>VK_EXT_debug_utils is active (debug messenger, object names for RenderDoc).</summary>
    public bool DebugUtils { get; init; }

    /// <summary>VK_KHR_acceleration_structure (+ deferred host operations) with the accelerationStructure feature.</summary>
    public bool AccelerationStructure { get; init; }

    /// <summary>VK_KHR_ray_query with the rayQuery feature (R4: ray-traced shadows/reflections from raster shaders).</summary>
    public bool RayQuery { get; init; }

    /// <summary>VK_KHR_ray_tracing_pipeline with the rayTracingPipeline feature.</summary>
    public bool RayTracingPipeline { get; init; }

    /// <summary>Vulkan 1.2 bufferDeviceAddress (required by acceleration structures).</summary>
    public bool BufferDeviceAddress { get; init; }

    /// <summary>Vulkan 1.2 descriptor indexing with runtime arrays, partially bound and non-uniform sampled images (bindless textures).</summary>
    public bool DescriptorIndexing { get; init; }

    /// <summary>Vulkan 1.2 timeline semaphores.</summary>
    public bool TimelineSemaphores { get; init; }

    /// <summary>Largest MSAA sample count supported for colour attachments.</summary>
    public int MaxColorSamples { get; init; }

    public uint MaxImageDimension2D { get; init; }

    /// <summary>Sum of the device-local memory heaps.</summary>
    public ulong DeviceLocalMemoryBytes { get; init; }

    /// <summary>Every physical device that was enumerated, "index: name (type, API)".</summary>
    public required IReadOnlyList<string> AvailableDevices { get; init; }

    /// <summary>Everything R4 (ray query effects) needs.</summary>
    public bool SupportsRayTracedEffects => AccelerationStructure && RayQuery && BufferDeviceAddress && DescriptorIndexing;

    public override string ToString()
    {
        var text = new StringBuilder();
        text.Append($"{DeviceName} ({DeviceType}, vendor 0x{VendorId:x4}, device 0x{DeviceId:x4}), driver {DriverName} {DriverInfo}\n");
        text.Append($"  API {ApiVersion} (device {DeviceApiVersion}, instance {InstanceApiVersion}, loader {LoaderVersion}); ");
        text.Append(UsesVulkan13Core ? "dynamic rendering + synchronization2 from core 1.3" : "dynamic rendering + synchronization2 from KHR extensions");
        if (PortabilitySubset)
            text.Append("; portability subset");
        text.Append('\n');
        text.Append($"  validation {(ValidationEnabled ? SynchronizationValidation ? "on (with synchronization validation)" : "on" : "off")}, debug utils {(DebugUtils ? "on" : "off")}\n");
        text.Append($"  roadmap: acceleration structures {YesNo(AccelerationStructure)}, ray query {YesNo(RayQuery)}, " +
                    $"ray tracing pipeline {YesNo(RayTracingPipeline)}, buffer device address {YesNo(BufferDeviceAddress)}, " +
                    $"descriptor indexing {YesNo(DescriptorIndexing)}, timeline semaphores {YesNo(TimelineSemaphores)}, " +
                    $"MSAA up to {MaxColorSamples}x, max 2D image {MaxImageDimension2D}, device-local memory {DeviceLocalMemoryBytes / (1024 * 1024)} MiB");
        return text.ToString();
    }

    private static string YesNo(bool value) => value ? "yes" : "no";
}
