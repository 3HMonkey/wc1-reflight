using System.Runtime.InteropServices;
using Vortice.Vulkan;
using WingCommander.Core.Rendering;
using WingCommander.Core.Video;

namespace WingCommander.Render.Vulkan.Internal;

/// <summary>Push constants of <c>classic.frag</c> (32 bytes, std430 layout).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ClassicPushConstants
{
    public float DestX;
    public float DestY;
    public float DestWidth;
    public float DestHeight;
    public float PrescaleX;
    public float PrescaleY;
    public uint FilterMode;
    public uint EncodeLinear;

    public static ClassicPushConstants For(PresentationRect rect, ScalingFilter filter, bool encodeLinear) => new()
    {
        DestX = rect.X,
        DestY = rect.Y,
        DestWidth = rect.Width,
        DestHeight = rect.Height,
        // Sharp bilinear: the integer nearest prescale that still fits, per axis (>= 1).
        PrescaleX = Math.Max(1, rect.Width / Framebuffer.Width),
        PrescaleY = Math.Max(1, rect.Height / Framebuffer.Height),
        FilterMode = filter switch
        {
            ScalingFilter.Nearest => 0u,
            ScalingFilter.Linear => 2u,
            _ => 1u,
        },
        EncodeLinear = encodeLinear ? 1u : 0u,
    };
}

/// <summary>
/// R1 classic layer: the 320x200 index image (R8_UINT) and the 256x1 palette image (RGBA8),
/// both device-local and uploaded through a persistently mapped staging buffer with one region
/// per frame in flight, only when the pixel source or its version (<c>ClassicLayer.PixelsVersion</c>,
/// or <c>TextLayer.Version</c> while the frame carries output-resolution text) or
/// <c>Palette.Version</c> changed; plus the fullscreen-triangle pipeline that does the palette
/// lookup and filtering.
/// </summary>
internal sealed unsafe class ClassicPass : IDisposable
{
    public const int IndexBytes = Framebuffer.PixelCount;              // 64000
    public const int PaletteBytes = Palette.EntryCount * 4;            // 1024
    public const int StagingSlotSize = 65536;                          // >= IndexBytes + PaletteBytes, 4-byte aligned parts
    private const uint QueueFamilyIgnored = uint.MaxValue;

    private readonly GpuContext _gpu;
    private GpuImage _indices;
    private GpuImage _palette;
    private GpuBuffer _staging;
    private VkSampler _sampler;
    private VkDescriptorSetLayout _setLayout;
    private VkPipelineLayout _pipelineLayout;
    private VkDescriptorPool _descriptorPool;
    private VkDescriptorSet _descriptorSet;
    private VkShaderModule _vertexShader;
    private VkShaderModule _fragmentShader;
    private VkPipeline _pipeline;
    private VkFormat _pipelineFormat;

    // What the GPU images hold (layout UNDEFINED until the first upload). The pixel source is the
    // ClassicLayer or, while the frame carries text, the TextLayer (its text-free pixels).
    private bool _indicesInitialized;
    private bool _paletteInitialized;
    private object? _uploadedSource;
    private int _uploadedPixelsVersion;
    private Palette? _uploadedPalette;
    private int _uploadedPaletteVersion;

    public ClassicPass(GpuContext gpu, int framesInFlight)
    {
        _gpu = gpu;
        try
        {
            _indices = gpu.CreateImage(Framebuffer.Width, Framebuffer.Height, VkFormat.R8Uint,
                VkImageUsageFlags.TransferDst | VkImageUsageFlags.Sampled, "classic indices 320x200");
            _palette = gpu.CreateImage(Palette.EntryCount, 1, VkFormat.R8G8B8A8Unorm,
                VkImageUsageFlags.TransferDst | VkImageUsageFlags.Sampled, "classic palette 256x1");
            _staging = gpu.CreateBuffer((ulong)(framesInFlight * StagingSlotSize), VkBufferUsageFlags.TransferSrc,
                hostVisible: true, preferHostCached: false, "classic staging");
            CreateDescriptors();
            _vertexShader = ShaderLibrary.CreateModule(gpu, "classic.vert.spv");
            _fragmentShader = ShaderLibrary.CreateModule(gpu, "classic.frag.spv");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// The 320x200 index image (SHADER_READ_ONLY_OPTIMAL whenever a frame draws); sampled by the
    /// sprite pass, which therefore sees the text-free pixels while the frame carries text.
    /// </summary>
    public VkImageView IndexView => _indices.View;

    /// <summary>The 256x1 palette image; the sprite pass looks its texels up in it too.</summary>
    public VkImageView PaletteView => _palette.View;

    /// <summary>Nearest, clamp-to-edge sampler (texelFetch only).</summary>
    public VkSampler Sampler => _sampler;

    /// <summary>(Re)creates the pipeline for a colour attachment format. The device must be idle when it changes.</summary>
    public void EnsurePipeline(VkFormat colorFormat)
    {
        if (_pipeline.IsNotNull && _pipelineFormat == colorFormat)
            return;
        if (_pipeline.IsNotNull)
        {
            _gpu.DeviceApi.vkDestroyPipeline(_pipeline);
            _pipeline = VkPipeline.Null;
        }
        _pipeline = CreatePipeline(colorFormat);
        _pipelineFormat = colorFormat;
    }

    /// <summary>
    /// Writes changed pixels/palette into the staging region of <paramref name="slot"/> (whose
    /// previous frame has completed) and records the copies with their layout transitions. While
    /// <paramref name="text"/> is set its pixels (the frame without the glyph foreground) replace
    /// the classic layer's (ADR-013); switching between the two sources re-uploads.
    /// </summary>
    public void RecordUploads(VkCommandBuffer cmd, int slot, ClassicLayer layer, TextLayer? text, VulkanRendererStatistics statistics)
    {
        Palette palette = layer.Palette;
        object source = text is null ? layer : text;
        int sourceVersion = text?.Version ?? layer.PixelsVersion;
        bool pixels = !_indicesInitialized || !ReferenceEquals(source, _uploadedSource) || sourceVersion != _uploadedPixelsVersion;
        bool colours = !_paletteInitialized || !ReferenceEquals(palette, _uploadedPalette) || palette.Version != _uploadedPaletteVersion;
        if (!pixels && !colours)
            return;

        ulong slotOffset = (ulong)slot * StagingSlotSize;
        byte* region = _staging.Mapped + slotOffset;
        if (pixels)
            (text?.Pixels ?? layer.Pixels).Pixels.AsSpan(0, IndexBytes).CopyTo(new Span<byte>(region, IndexBytes));
        if (colours)
        {
            ReadOnlySpan<byte> rgb = palette.Rgb;
            byte* rgba = region + IndexBytes;
            for (int i = 0; i < Palette.EntryCount; i++)
            {
                rgba[i * 4] = rgb[i * 3];
                rgba[i * 4 + 1] = rgb[i * 3 + 1];
                rgba[i * 4 + 2] = rgb[i * 3 + 2];
                rgba[i * 4 + 3] = 255;
            }
        }
        // Host writes to HOST_COHERENT memory are visible to the device once the command buffer is submitted.

        VkImageMemoryBarrier2* barriers = stackalloc VkImageMemoryBarrier2[2];
        int count = 0;
        if (pixels)
            barriers[count++] = ToTransferDestination(_indices.Image, _indicesInitialized);
        if (colours)
            barriers[count++] = ToTransferDestination(_palette.Image, _paletteInitialized);
        var dependency = new VkDependencyInfo { imageMemoryBarrierCount = (uint)count, pImageMemoryBarriers = barriers };
        _gpu.CmdPipelineBarrier2(cmd, &dependency);

        if (pixels)
            CopyToImage(cmd, slotOffset, _indices.Image, Framebuffer.Width, Framebuffer.Height);
        if (colours)
            CopyToImage(cmd, slotOffset + IndexBytes, _palette.Image, Palette.EntryCount, 1);

        count = 0;
        if (pixels)
            barriers[count++] = ToShaderRead(_indices.Image);
        if (colours)
            barriers[count++] = ToShaderRead(_palette.Image);
        dependency = new VkDependencyInfo { imageMemoryBarrierCount = (uint)count, pImageMemoryBarriers = barriers };
        _gpu.CmdPipelineBarrier2(cmd, &dependency);

        if (pixels)
        {
            _indicesInitialized = true;
            _uploadedSource = source;
            _uploadedPixelsVersion = sourceVersion;
            statistics.PixelUploads++;
        }
        if (colours)
        {
            _paletteInitialized = true;
            _uploadedPalette = palette;
            _uploadedPaletteVersion = palette.Version;
            statistics.PaletteUploads++;
        }
    }

    /// <summary>Draws the letterboxed picture; the caller has begun rendering with the black clear.</summary>
    public void RecordDraw(VkCommandBuffer cmd, PresentationRect rect, ScalingFilter filter, bool encodeLinear)
    {
        if (rect.IsEmpty)
            return;
        var viewport = new VkViewport { x = rect.X, y = rect.Y, width = rect.Width, height = rect.Height, minDepth = 0f, maxDepth = 1f };
        _gpu.DeviceApi.vkCmdSetViewport(cmd, 0, 1, &viewport);
        var scissor = new VkRect2D
        {
            offset = new VkOffset2D { x = rect.X, y = rect.Y },
            extent = new VkExtent2D { width = (uint)rect.Width, height = (uint)rect.Height },
        };
        _gpu.DeviceApi.vkCmdSetScissor(cmd, 0, 1, &scissor);
        _gpu.DeviceApi.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _pipeline);
        VkDescriptorSet set = _descriptorSet;
        _gpu.DeviceApi.vkCmdBindDescriptorSets(cmd, VkPipelineBindPoint.Graphics, _pipelineLayout, 0, 1, &set);
        ClassicPushConstants constants = ClassicPushConstants.For(rect, filter, encodeLinear);
        _gpu.DeviceApi.vkCmdPushConstants(cmd, _pipelineLayout, VkShaderStageFlags.Fragment, 0, (uint)sizeof(ClassicPushConstants), &constants);
        _gpu.DeviceApi.vkCmdDraw(cmd, 3, 1, 0, 0);
    }

    public void Dispose()
    {
        var api = _gpu.DeviceApi;
        if (api is null)
            return;
        if (_pipeline.IsNotNull)
            api.vkDestroyPipeline(_pipeline);
        if (_pipelineLayout.IsNotNull)
            api.vkDestroyPipelineLayout(_pipelineLayout);
        if (_descriptorPool.IsNotNull)
            api.vkDestroyDescriptorPool(_descriptorPool); // frees the set
        if (_setLayout.IsNotNull)
            api.vkDestroyDescriptorSetLayout(_setLayout);
        if (_sampler.IsNotNull)
            api.vkDestroySampler(_sampler);
        if (_vertexShader.IsNotNull)
            api.vkDestroyShaderModule(_vertexShader);
        if (_fragmentShader.IsNotNull)
            api.vkDestroyShaderModule(_fragmentShader);
        _pipeline = VkPipeline.Null;
        _pipelineLayout = VkPipelineLayout.Null;
        _descriptorPool = VkDescriptorPool.Null;
        _setLayout = VkDescriptorSetLayout.Null;
        _sampler = VkSampler.Null;
        _vertexShader = VkShaderModule.Null;
        _fragmentShader = VkShaderModule.Null;
        _gpu.Destroy(ref _indices);
        _gpu.Destroy(ref _palette);
        _gpu.Destroy(ref _staging);
    }

    private void CreateDescriptors()
    {
        var api = _gpu.DeviceApi;
        var samplerInfo = new VkSamplerCreateInfo
        {
            magFilter = VkFilter.Nearest,
            minFilter = VkFilter.Nearest,
            mipmapMode = VkSamplerMipmapMode.Nearest,
            addressModeU = VkSamplerAddressMode.ClampToEdge,
            addressModeV = VkSamplerAddressMode.ClampToEdge,
            addressModeW = VkSamplerAddressMode.ClampToEdge,
            maxLod = 0f,
            borderColor = VkBorderColor.IntOpaqueBlack,
        };
        VkSampler sampler;
        api.vkCreateSampler(&samplerInfo, null, &sampler).Check("vkCreateSampler");
        _sampler = sampler;

        VkDescriptorSetLayoutBinding* bindings = stackalloc VkDescriptorSetLayoutBinding[2];
        bindings[0] = new VkDescriptorSetLayoutBinding
        {
            binding = 0,
            descriptorType = VkDescriptorType.CombinedImageSampler,
            descriptorCount = 1,
            stageFlags = VkShaderStageFlags.Fragment,
        };
        bindings[1] = new VkDescriptorSetLayoutBinding
        {
            binding = 1,
            descriptorType = VkDescriptorType.CombinedImageSampler,
            descriptorCount = 1,
            stageFlags = VkShaderStageFlags.Fragment,
        };
        var layoutInfo = new VkDescriptorSetLayoutCreateInfo { bindingCount = 2, pBindings = bindings };
        VkDescriptorSetLayout setLayout;
        api.vkCreateDescriptorSetLayout(&layoutInfo, null, &setLayout).Check("vkCreateDescriptorSetLayout");
        _setLayout = setLayout;

        var pushRange = new VkPushConstantRange
        {
            stageFlags = VkShaderStageFlags.Fragment,
            offset = 0,
            size = (uint)sizeof(ClassicPushConstants),
        };
        var pipelineLayoutInfo = new VkPipelineLayoutCreateInfo
        {
            setLayoutCount = 1,
            pSetLayouts = &setLayout,
            pushConstantRangeCount = 1,
            pPushConstantRanges = &pushRange,
        };
        VkPipelineLayout pipelineLayout;
        api.vkCreatePipelineLayout(&pipelineLayoutInfo, null, &pipelineLayout).Check("vkCreatePipelineLayout");
        _pipelineLayout = pipelineLayout;

        var poolSize = new VkDescriptorPoolSize { type = VkDescriptorType.CombinedImageSampler, descriptorCount = 2 };
        var poolInfo = new VkDescriptorPoolCreateInfo { maxSets = 1, poolSizeCount = 1, pPoolSizes = &poolSize };
        VkDescriptorPool pool;
        api.vkCreateDescriptorPool(&poolInfo, null, &pool).Check("vkCreateDescriptorPool");
        _descriptorPool = pool;

        var allocateInfo = new VkDescriptorSetAllocateInfo { descriptorPool = pool, descriptorSetCount = 1, pSetLayouts = &setLayout };
        VkDescriptorSet set;
        api.vkAllocateDescriptorSets(&allocateInfo, &set).Check("vkAllocateDescriptorSets");
        _descriptorSet = set;

        // The images are fixed for the lifetime of the pass, so the set is written once.
        var indexInfo = new VkDescriptorImageInfo { sampler = sampler, imageView = _indices.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        var paletteInfo = new VkDescriptorImageInfo { sampler = sampler, imageView = _palette.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        VkWriteDescriptorSet* writes = stackalloc VkWriteDescriptorSet[2];
        writes[0] = new VkWriteDescriptorSet
        {
            dstSet = set,
            dstBinding = 0,
            descriptorCount = 1,
            descriptorType = VkDescriptorType.CombinedImageSampler,
            pImageInfo = &indexInfo,
        };
        writes[1] = new VkWriteDescriptorSet
        {
            dstSet = set,
            dstBinding = 1,
            descriptorCount = 1,
            descriptorType = VkDescriptorType.CombinedImageSampler,
            pImageInfo = &paletteInfo,
        };
        api.vkUpdateDescriptorSets(2, writes, 0, null);
    }

    private VkPipeline CreatePipeline(VkFormat colorFormat)
    {
        ReadOnlySpan<byte> entryPoint = "main\0"u8;
        fixed (byte* main = entryPoint)
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _vertexShader, pName = main };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _fragmentShader, pName = main };

            var vertexInput = new VkPipelineVertexInputStateCreateInfo();
            var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
            var viewportState = new VkPipelineViewportStateCreateInfo { viewportCount = 1, scissorCount = 1 };
            var rasterization = new VkPipelineRasterizationStateCreateInfo
            {
                polygonMode = VkPolygonMode.Fill,
                cullMode = VkCullModeFlags.None,
                frontFace = VkFrontFace.CounterClockwise,
                lineWidth = 1f,
            };
            var multisample = new VkPipelineMultisampleStateCreateInfo { rasterizationSamples = VkSampleCountFlags.Count1 };
            var blendAttachment = new VkPipelineColorBlendAttachmentState
            {
                blendEnable = false,
                colorWriteMask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B | VkColorComponentFlags.A,
            };
            var colorBlend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttachment };
            VkDynamicState* dynamicStates = stackalloc VkDynamicState[2];
            dynamicStates[0] = VkDynamicState.Viewport;
            dynamicStates[1] = VkDynamicState.Scissor;
            var dynamicState = new VkPipelineDynamicStateCreateInfo { dynamicStateCount = 2, pDynamicStates = dynamicStates };
            VkFormat format = colorFormat;
            var rendering = new VkPipelineRenderingCreateInfo { colorAttachmentCount = 1, pColorAttachmentFormats = &format };

            var createInfo = new VkGraphicsPipelineCreateInfo
            {
                pNext = &rendering,
                stageCount = 2,
                pStages = stages,
                pVertexInputState = &vertexInput,
                pInputAssemblyState = &inputAssembly,
                pViewportState = &viewportState,
                pRasterizationState = &rasterization,
                pMultisampleState = &multisample,
                pColorBlendState = &colorBlend,
                pDynamicState = &dynamicState,
                layout = _pipelineLayout,
            };
            VkPipeline pipeline;
            _gpu.DeviceApi.vkCreateGraphicsPipelines(VkPipelineCache.Null, 1, &createInfo, null, &pipeline)
                .Check($"vkCreateGraphicsPipelines (classic, {VkNames.Format(colorFormat)})");
            _gpu.SetName(VkObjectType.Pipeline, pipeline.Handle, "classic pass");
            return pipeline;
        }
    }

    private void CopyToImage(VkCommandBuffer cmd, ulong bufferOffset, VkImage image, uint width, uint height)
    {
        var region = new VkBufferImageCopy
        {
            bufferOffset = bufferOffset,
            bufferRowLength = 0, // tightly packed
            bufferImageHeight = 0,
            imageSubresource = new VkImageSubresourceLayers { aspectMask = VkImageAspectFlags.Color, mipLevel = 0, baseArrayLayer = 0, layerCount = 1 },
            imageOffset = default,
            imageExtent = new VkExtent3D { width = width, height = height, depth = 1 },
        };
        _gpu.DeviceApi.vkCmdCopyBufferToImage(cmd, _staging.Buffer, image, VkImageLayout.TransferDstOptimal, 1, &region);
    }

    private static VkImageMemoryBarrier2 ToTransferDestination(VkImage image, bool initialized) => new()
    {
        // Write-after-read on the previous frame's sampling: an execution dependency suffices.
        srcStageMask = initialized ? VkPipelineStageFlags2.FragmentShader : VkPipelineStageFlags2.None,
        srcAccessMask = VkAccessFlags2.None,
        dstStageMask = VkPipelineStageFlags2.Copy,
        dstAccessMask = VkAccessFlags2.TransferWrite,
        oldLayout = initialized ? VkImageLayout.ShaderReadOnlyOptimal : VkImageLayout.Undefined,
        newLayout = VkImageLayout.TransferDstOptimal,
        srcQueueFamilyIndex = QueueFamilyIgnored,
        dstQueueFamilyIndex = QueueFamilyIgnored,
        image = image,
        subresourceRange = GpuContext.ColorRange,
    };

    private static VkImageMemoryBarrier2 ToShaderRead(VkImage image) => new()
    {
        srcStageMask = VkPipelineStageFlags2.Copy,
        srcAccessMask = VkAccessFlags2.TransferWrite,
        dstStageMask = VkPipelineStageFlags2.FragmentShader,
        dstAccessMask = VkAccessFlags2.ShaderSampledRead,
        oldLayout = VkImageLayout.TransferDstOptimal,
        newLayout = VkImageLayout.ShaderReadOnlyOptimal,
        srcQueueFamilyIndex = QueueFamilyIgnored,
        dstQueueFamilyIndex = QueueFamilyIgnored,
        image = image,
        subresourceRange = GpuContext.ColorRange,
    };
}
