using System.Buffers.Binary;
using Vortice.Vulkan;

namespace WingCommander.Render.Vulkan.Internal;

/// <summary>
/// SPIR-V embedded in this assembly (compiled from <c>Shaders/*.vert|frag</c> by
/// <c>tools/WingCommander.ShaderBuild</c>); no shader compiler is needed at runtime.
/// </summary>
internal static unsafe class ShaderLibrary
{
    public const string ResourcePrefix = "WingCommander.Render.Vulkan.Shaders.";
    private const uint SpirvMagic = 0x07230203;

    /// <summary>Reads <c>Shaders/Compiled/<paramref name="fileName"/></c> from the embedded resources.</summary>
    public static byte[] Load(string fileName)
    {
        using Stream? stream = typeof(ShaderLibrary).Assembly.GetManifestResourceStream(ResourcePrefix + fileName);
        if (stream is null)
            throw new VulkanRendererException($"Embedded shader '{fileName}' is missing; rebuild WingCommander.Render.Vulkan.");
        byte[] code = new byte[stream.Length];
        stream.ReadExactly(code);
        if (code.Length < 20 || code.Length % 4 != 0 || BinaryPrimitives.ReadUInt32LittleEndian(code) != SpirvMagic)
            throw new VulkanRendererException($"Embedded shader '{fileName}' is not valid SPIR-V.");
        return code;
    }

    public static VkShaderModule CreateModule(GpuContext gpu, string fileName)
    {
        byte[] code = Load(fileName);
        fixed (byte* p = code)
        {
            var createInfo = new VkShaderModuleCreateInfo { codeSize = (nuint)code.Length, pCode = (uint*)p };
            VkShaderModule module;
            gpu.DeviceApi.vkCreateShaderModule(&createInfo, null, &module).Check($"vkCreateShaderModule ({fileName})");
            gpu.SetName(VkObjectType.ShaderModule, module.Handle, fileName);
            return module;
        }
    }
}
