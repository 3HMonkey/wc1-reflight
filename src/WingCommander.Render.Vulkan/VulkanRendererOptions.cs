namespace WingCommander.Render.Vulkan;

/// <summary>Whether the Khronos validation layer is enabled.</summary>
public enum ValidationMode
{
    /// <summary>Enabled when <c>VK_LAYER_KHRONOS_validation</c> is installed (Vulkan SDK), otherwise off.</summary>
    Auto,

    /// <summary>Never enabled (release builds, performance measurements).</summary>
    Disabled,

    /// <summary>Must be available; creation fails otherwise (CI with the SDK installed).</summary>
    Required,
}

/// <summary>Severity of a renderer log message.</summary>
public enum VulkanLogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>Creation options of <see cref="VulkanRenderer"/> (presentation settings live in <c>RendererSettings</c>).</summary>
public sealed class VulkanRendererOptions
{
    /// <summary>Name passed to the driver in <c>VkApplicationInfo</c>.</summary>
    public string ApplicationName { get; init; } = "Wing Commander";

    /// <summary>
    /// Validation layer policy. The environment variable <c>WC1_VULKAN_VALIDATION=0</c> forces it
    /// off, <c>=1</c> makes it required.
    /// </summary>
    public ValidationMode Validation { get; init; } = ValidationMode.Auto;

    /// <summary>
    /// When validation is active, also enable the layer's synchronization validation (through
    /// VK_EXT_layer_settings <c>validate_sync</c>, or VK_EXT_validation_features on older layers;
    /// silently skipped when the layer offers neither). <c>WC1_VULKAN_VALIDATION_SYNC=0</c> turns it off.
    /// </summary>
    public bool SynchronizationValidation { get; init; } = true;

    /// <summary>
    /// Device override: a case-insensitive substring of the device name or a zero-based index
    /// into the enumeration order. Defaults to the environment variable <c>WC1_VULKAN_DEVICE</c>;
    /// without either, a discrete GPU is preferred.
    /// </summary>
    public string? PreferredDevice { get; init; }

    /// <summary>
    /// Caps the Vulkan API version the renderer uses (minimum 1.2). <c>new Version(1, 2)</c>
    /// exercises the extension path (VK_KHR_dynamic_rendering, VK_KHR_synchronization2) that
    /// MoltenVK and older drivers take, even on a 1.3+ driver.
    /// </summary>
    public Version? MaxApiVersion { get; init; }

    /// <summary>Log sink; <see langword="null"/> writes warnings and errors to standard error.</summary>
    public Action<VulkanLogLevel, string>? Log { get; init; }

    /// <summary>
    /// Colour format of the offscreen target (tests use it to exercise the BGRA and sRGB swapchain
    /// paths without a window). Must be one of the formats <c>Readback</c> converts.
    /// </summary>
    internal Vortice.Vulkan.VkFormat OffscreenFormat { get; init; } = Vortice.Vulkan.VkFormat.R8G8B8A8Unorm;

    /// <summary>
    /// Tests: lift the validation layer's duplicate-message limit (10 per message) so a repeated
    /// error is reported in every test that causes it (VK_EXT_layer_settings only).
    /// </summary>
    internal bool UnlimitedValidationMessages { get; init; }
}
