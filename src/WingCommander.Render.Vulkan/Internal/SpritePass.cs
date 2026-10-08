using System.Runtime.InteropServices;
using Vortice.Vulkan;
using WingCommander.Core.Rendering;
using WingCommander.Core.Video;

namespace WingCommander.Render.Vulkan.Internal;

/// <summary>Per-instance vertex data of <c>sprite.vert</c> (64 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SpriteInstanceData
{
    public float PositionX;
    public float PositionY;
    public float AxisXx;
    public float AxisXy;
    public float AxisYx;
    public float AxisYy;
    public float Left;
    public float Top;
    public float Width;
    public float Height;
    public float AtlasX;
    public float AtlasY;
    public float Magnification;
    public float Unused0;
    public float Unused1;
    public float Unused2;
}

/// <summary>Push constants of <c>sprite.frag</c> (48 bytes, std430).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SpritePushConstants
{
    public float DestX;
    public float DestY;
    public float DestWidth;
    public float DestHeight;
    public float ClipX0;
    public float ClipY0;
    public float ClipX1;
    public float ClipY1;
    public uint FilterMode;
    public uint EncodeLinear;
    public uint BackgroundIndex;
    public uint UseMask;
}

/// <summary>
/// R2 space sprites at output resolution, drawn after the classic layer inside the same
/// rendering scope. Images from <see cref="SpriteImageCache"/> are uploaded once into an R8_UINT
/// atlas (<see cref="ShelfAtlas{TKey}"/>); every frame writes one instance record per sprite into
/// the slot's host-visible vertex buffer and draws them with one instanced call. The fragment
/// shader keeps the classic layer on top wherever it does not show the space background inside
/// the window mask (see sprite.frag). Created on the first frame that carries sprites, so
/// classic-only frames never touch it.
/// </summary>
internal sealed unsafe class SpritePass : IDisposable
{
    public const int AtlasSize = 2048;
    private const int MaskBytes = Framebuffer.PixelCount;
    private const uint QueueFamilyIgnored = uint.MaxValue;

    private readonly GpuContext _gpu;
    private readonly ClassicPass _classic;
    private readonly RendererLog _log;
    private readonly ShelfAtlas<SpriteImageKey> _atlasPacker = new(AtlasSize);
    private readonly Slot[] _slots;
    private GpuImage _atlas;
    private GpuImage _mask;
    private bool _imagesInitialized;
    private VkDescriptorSetLayout _setLayout;
    private VkPipelineLayout _pipelineLayout;
    private VkDescriptorPool _descriptorPool;
    private VkDescriptorSet _descriptorSet;
    private VkShaderModule _vertexShader;
    private VkShaderModule _fragmentShader;
    private VkPipeline _pipeline;
    private VkFormat _pipelineFormat;
    private VkBufferImageCopy[] _regions = new VkBufferImageCopy[64];

    private SpriteImageCache? _cache;
    private int _cacheGeneration;
    private SpaceViewMask? _uploadedMask;
    private int _uploadedMaskVersion;
    private bool _warnedOverflow;
    private bool _warnedOversize;

    /// <summary>Per frame in flight: instance vertex buffer and upload staging (reused after the slot's fence).</summary>
    private struct Slot
    {
        public GpuBuffer Instances;
        public int InstanceCapacity;
        public GpuBuffer Staging;
        public int DrawCount;
        public SpritePushConstants Constants;
    }

    public SpritePass(GpuContext gpu, ClassicPass classic, RendererLog log, int framesInFlight)
    {
        _gpu = gpu;
        _classic = classic;
        _log = log;
        _slots = new Slot[framesInFlight];
        try
        {
            _atlas = gpu.CreateImage(AtlasSize, AtlasSize, VkFormat.R8Uint, VkImageUsageFlags.TransferDst | VkImageUsageFlags.Sampled, "sprite atlas");
            _mask = gpu.CreateImage(Framebuffer.Width, Framebuffer.Height, VkFormat.R8Uint, VkImageUsageFlags.TransferDst | VkImageUsageFlags.Sampled, "space window mask");
            CreateDescriptors();
            _vertexShader = ShaderLibrary.CreateModule(gpu, "sprite.vert.spv");
            _fragmentShader = ShaderLibrary.CreateModule(gpu, "sprite.frag.spv");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

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
    /// Before rendering begins: resolves every sprite to its atlas entry (uploading new images
    /// through the slot's staging buffer), uploads a changed window mask and writes the instance
    /// records. Returns the number of sprites to draw.
    /// </summary>
    /// <param name="interpolation">Where the display is between the previous and the latest tick (R2b, <see cref="RenderFrame.Interpolation"/>).</param>
    public int Prepare(VkCommandBuffer cmd, int slotIndex, SpaceView space, PresentationRect rect, ScalingFilter filter,
        bool encodeLinear, VulkanRendererStatistics statistics, float interpolation = 1f)
    {
        ref Slot slot = ref _slots[slotIndex];
        slot.DrawCount = 0;
        ReadOnlySpan<SpriteInstance> sprites = space.Sprites.Items;
        if (sprites.IsEmpty || rect.IsEmpty)
            return 0;

        if (!_imagesInitialized)
            InitializeImages(cmd);

        if (!ReferenceEquals(space.Images, _cache) || space.Images.Generation != _cacheGeneration)
        {
            if (_atlasPacker.Count > 0)
                statistics.AtlasResets++;
            _atlasPacker.Reset();
            _cache = space.Images;
            _cacheGeneration = space.Images.Generation;
        }

        SpaceViewMask? mask = space.WindowMask;
        bool uploadMask = mask is not null && (!ReferenceEquals(mask, _uploadedMask) || mask.Version != _uploadedMaskVersion);
        int stagingStart = 0;
        if (uploadMask)
        {
            EnsureStaging(ref slot, MaskBytes, 0);
            mask!.Mask.CopyTo(new Span<byte>(slot.Staging.Mapped, MaskBytes));
            stagingStart = MaskBytes;
        }

        EnsureInstanceCapacity(ref slot, sprites.Length);
        float outputScale = rect.Width / (float)Framebuffer.Width;
        int instanceCount = 0;
        int regionCount = 0;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            instanceCount = 0;
            regionCount = 0;
            int stagingUsed = stagingStart;
            bool full = false;
            var instances = (SpriteInstanceData*)slot.Instances.Mapped;
            foreach (ref readonly SpriteInstance sprite in sprites)
            {
                if (!_atlasPacker.TryGet(sprite.Image, out AtlasEntry entry))
                {
                    if (!space.Images.TryGet(sprite.Image, out SpriteImage? image) || image.IsEmpty)
                        continue; // nothing to draw (the Game draws such sprites in software)
                    if (image.Width > AtlasSize || image.Height > AtlasSize)
                    {
                        WarnOversize(sprite.Image, image);
                        continue;
                    }
                    if (!_atlasPacker.TryAllocate(image.Width, image.Height, out int x, out int y))
                    {
                        full = true;
                        break;
                    }
                    int bytes = image.Width * image.Height;
                    int offset = (stagingUsed + 3) & ~3;
                    EnsureStaging(ref slot, offset + bytes, stagingUsed);
                    image.Pixels.Span[..bytes].CopyTo(new Span<byte>(slot.Staging.Mapped + offset, bytes));
                    stagingUsed = offset + bytes;
                    AddRegion(ref regionCount, offset, x, y, image.Width, image.Height);
                    entry = new AtlasEntry(x, y, image.Width, image.Height, image.OriginX, image.OriginY);
                    _atlasPacker.Add(sprite.Image, entry);
                }
                SpriteInstance shown = sprite.At(interpolation);
                WriteInstance(instances + instanceCount, in shown, in entry, outputScale);
                instanceCount++;
            }
            if (!full)
                break;

            // Atlas full: start over with only this frame's images (cheap: they are re-uploaded once).
            statistics.AtlasResets++;
            _atlasPacker.Reset();
            if (attempt == 1 && !_warnedOverflow)
            {
                _warnedOverflow = true;
                _log.Warning($"The sprites of one frame do not fit into the {AtlasSize}x{AtlasSize} sprite atlas; some are not drawn.");
            }
        }

        if (uploadMask || regionCount > 0)
            RecordUploads(cmd, slot.Staging.Buffer, uploadMask, regionCount);
        if (uploadMask)
        {
            _uploadedMask = mask;
            _uploadedMaskVersion = mask!.Version;
            statistics.WindowMaskUploads++;
        }
        statistics.SpriteUploads += regionCount;
        statistics.SpritesDrawn += instanceCount;

        ScreenRect clip = space.Sprites.Clip;
        slot.DrawCount = instanceCount;
        slot.Constants = new SpritePushConstants
        {
            DestX = rect.X,
            DestY = rect.Y,
            DestWidth = rect.Width,
            DestHeight = rect.Height,
            ClipX0 = clip.X,
            ClipY0 = clip.Y,
            ClipX1 = clip.X + clip.Width,
            ClipY1 = clip.Y + clip.Height,
            FilterMode = filter switch
            {
                ScalingFilter.Nearest => 0u,
                ScalingFilter.Linear => 2u,
                _ => 1u,
            },
            EncodeLinear = encodeLinear ? 1u : 0u,
            BackgroundIndex = space.BackgroundIndex,
            UseMask = mask is null ? 0u : 1u,
        };
        return instanceCount;
    }

    /// <summary>Inside the rendering scope, after the classic draw.</summary>
    public void RecordDraw(VkCommandBuffer cmd, int slotIndex, PresentationRect rect)
    {
        ref Slot slot = ref _slots[slotIndex];
        if (slot.DrawCount == 0)
            return;
        var api = _gpu.DeviceApi;
        var viewport = new VkViewport { x = rect.X, y = rect.Y, width = rect.Width, height = rect.Height, minDepth = 0f, maxDepth = 1f };
        api.vkCmdSetViewport(cmd, 0, 1, &viewport);
        var scissor = new VkRect2D
        {
            offset = new VkOffset2D { x = rect.X, y = rect.Y },
            extent = new VkExtent2D { width = (uint)rect.Width, height = (uint)rect.Height },
        };
        api.vkCmdSetScissor(cmd, 0, 1, &scissor);
        api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _pipeline);
        VkDescriptorSet set = _descriptorSet;
        api.vkCmdBindDescriptorSets(cmd, VkPipelineBindPoint.Graphics, _pipelineLayout, 0, 1, &set);
        VkBuffer buffer = slot.Instances.Buffer;
        ulong offset = 0;
        api.vkCmdBindVertexBuffers(cmd, 0, 1, &buffer, &offset);
        SpritePushConstants constants = slot.Constants;
        api.vkCmdPushConstants(cmd, _pipelineLayout, VkShaderStageFlags.Fragment, 0, (uint)sizeof(SpritePushConstants), &constants);
        api.vkCmdDraw(cmd, 4, (uint)slot.DrawCount, 0, 0);
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
            api.vkDestroyDescriptorPool(_descriptorPool);
        if (_setLayout.IsNotNull)
            api.vkDestroyDescriptorSetLayout(_setLayout);
        if (_vertexShader.IsNotNull)
            api.vkDestroyShaderModule(_vertexShader);
        if (_fragmentShader.IsNotNull)
            api.vkDestroyShaderModule(_fragmentShader);
        _pipeline = VkPipeline.Null;
        _pipelineLayout = VkPipelineLayout.Null;
        _descriptorPool = VkDescriptorPool.Null;
        _setLayout = VkDescriptorSetLayout.Null;
        _vertexShader = VkShaderModule.Null;
        _fragmentShader = VkShaderModule.Null;
        for (int i = 0; i < _slots.Length; i++)
        {
            _gpu.Destroy(ref _slots[i].Instances);
            _gpu.Destroy(ref _slots[i].Staging);
        }
        _gpu.Destroy(ref _atlas);
        _gpu.Destroy(ref _mask);
    }

    // ---- per-frame helpers ----------------------------------------------------------------

    private static void WriteInstance(SpriteInstanceData* data, in SpriteInstance sprite, in AtlasEntry entry, float outputScale)
    {
        var (cos, sin) = SpriteTrig.Get(sprite.Angle);
        float scale = sprite.Scale;
        float scaleY = sprite.VerticalScale;
        float flipX = (sprite.Flip & SpriteFlip.Horizontal) != 0 ? -scale : scale;
        float flipY = (sprite.Flip & SpriteFlip.Vertical) != 0 ? -scaleY : scaleY;
        // Screen = hot-spot centre + R(angle) * diag(flipX, flipY) * local (y down: clockwise).
        data->PositionX = sprite.X;
        data->PositionY = sprite.Y;
        data->AxisXx = cos * flipX;
        data->AxisXy = sin * flipX;
        data->AxisYx = -sin * flipY;
        data->AxisYy = cos * flipY;
        data->Left = -entry.OriginX - 0.5f;
        data->Top = -entry.OriginY - 0.5f;
        data->Width = entry.Width;
        data->Height = entry.Height;
        data->AtlasX = entry.X;
        data->AtlasY = entry.Y;
        data->Magnification = outputScale * MathF.Min(MathF.Abs(scale), MathF.Abs(scaleY));
        data->Unused0 = 0f;
        data->Unused1 = 0f;
        data->Unused2 = 0f;
    }

    private void AddRegion(ref int count, int bufferOffset, int x, int y, int width, int height)
    {
        if (count == _regions.Length)
            Array.Resize(ref _regions, _regions.Length * 2);
        _regions[count++] = new VkBufferImageCopy
        {
            bufferOffset = (ulong)bufferOffset,
            bufferRowLength = 0,
            bufferImageHeight = 0,
            imageSubresource = new VkImageSubresourceLayers { aspectMask = VkImageAspectFlags.Color, mipLevel = 0, baseArrayLayer = 0, layerCount = 1 },
            imageOffset = new VkOffset3D { x = x, y = y, z = 0 },
            imageExtent = new VkExtent3D { width = (uint)width, height = (uint)height, depth = 1 },
        };
    }

    private void RecordUploads(VkCommandBuffer cmd, VkBuffer staging, bool mask, int regionCount)
    {
        VkImageMemoryBarrier2* barriers = stackalloc VkImageMemoryBarrier2[2];
        int count = 0;
        if (regionCount > 0)
            barriers[count++] = ToTransferDestination(_atlas.Image);
        if (mask)
            barriers[count++] = ToTransferDestination(_mask.Image);
        var dependency = new VkDependencyInfo { imageMemoryBarrierCount = (uint)count, pImageMemoryBarriers = barriers };
        _gpu.CmdPipelineBarrier2(cmd, &dependency);

        if (regionCount > 0)
        {
            fixed (VkBufferImageCopy* regions = _regions)
                _gpu.DeviceApi.vkCmdCopyBufferToImage(cmd, staging, _atlas.Image, VkImageLayout.TransferDstOptimal, (uint)regionCount, regions);
        }
        if (mask)
        {
            var region = new VkBufferImageCopy
            {
                bufferOffset = 0,
                imageSubresource = new VkImageSubresourceLayers { aspectMask = VkImageAspectFlags.Color, mipLevel = 0, baseArrayLayer = 0, layerCount = 1 },
                imageExtent = new VkExtent3D { width = Framebuffer.Width, height = Framebuffer.Height, depth = 1 },
            };
            _gpu.DeviceApi.vkCmdCopyBufferToImage(cmd, staging, _mask.Image, VkImageLayout.TransferDstOptimal, 1, &region);
        }

        count = 0;
        if (regionCount > 0)
            barriers[count++] = ToShaderRead(_atlas.Image);
        if (mask)
            barriers[count++] = ToShaderRead(_mask.Image);
        dependency = new VkDependencyInfo { imageMemoryBarrierCount = (uint)count, pImageMemoryBarriers = barriers };
        _gpu.CmdPipelineBarrier2(cmd, &dependency);
    }

    /// <summary>First use: atlas cleared to transparent (255), mask to "window everywhere" (1), both readable.</summary>
    private void InitializeImages(VkCommandBuffer cmd)
    {
        VkImageMemoryBarrier2* barriers = stackalloc VkImageMemoryBarrier2[2];
        barriers[0] = Barrier(_atlas.Image, VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal,
            VkPipelineStageFlags2.None, VkAccessFlags2.None, VkPipelineStageFlags2.Clear, VkAccessFlags2.TransferWrite);
        barriers[1] = Barrier(_mask.Image, VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal,
            VkPipelineStageFlags2.None, VkAccessFlags2.None, VkPipelineStageFlags2.Clear, VkAccessFlags2.TransferWrite);
        var dependency = new VkDependencyInfo { imageMemoryBarrierCount = 2, pImageMemoryBarriers = barriers };
        _gpu.CmdPipelineBarrier2(cmd, &dependency);

        VkImageSubresourceRange range = GpuContext.ColorRange;
        var transparent = new VkClearColorValue(255u, 0u, 0u, 0u);
        _gpu.DeviceApi.vkCmdClearColorImage(cmd, _atlas.Image, VkImageLayout.TransferDstOptimal, &transparent, 1, &range);
        var window = new VkClearColorValue(1u, 0u, 0u, 0u);
        _gpu.DeviceApi.vkCmdClearColorImage(cmd, _mask.Image, VkImageLayout.TransferDstOptimal, &window, 1, &range);

        barriers[0] = Barrier(_atlas.Image, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal,
            VkPipelineStageFlags2.Clear, VkAccessFlags2.TransferWrite,
            VkPipelineStageFlags2.FragmentShader | VkPipelineStageFlags2.AllTransfer, VkAccessFlags2.ShaderSampledRead | VkAccessFlags2.TransferWrite);
        barriers[1] = Barrier(_mask.Image, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal,
            VkPipelineStageFlags2.Clear, VkAccessFlags2.TransferWrite,
            VkPipelineStageFlags2.FragmentShader | VkPipelineStageFlags2.AllTransfer, VkAccessFlags2.ShaderSampledRead | VkAccessFlags2.TransferWrite);
        dependency = new VkDependencyInfo { imageMemoryBarrierCount = 2, pImageMemoryBarriers = barriers };
        _gpu.CmdPipelineBarrier2(cmd, &dependency);
        _imagesInitialized = true;
    }

    /// <summary>Earlier frames sampled the image (write-after-read) and earlier uploads wrote it (write-after-write).</summary>
    private static VkImageMemoryBarrier2 ToTransferDestination(VkImage image) => Barrier(image,
        VkImageLayout.ShaderReadOnlyOptimal, VkImageLayout.TransferDstOptimal,
        VkPipelineStageFlags2.FragmentShader | VkPipelineStageFlags2.AllTransfer, VkAccessFlags2.TransferWrite,
        VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite);

    private static VkImageMemoryBarrier2 ToShaderRead(VkImage image) => Barrier(image,
        VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal,
        VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite,
        VkPipelineStageFlags2.FragmentShader | VkPipelineStageFlags2.AllTransfer, VkAccessFlags2.ShaderSampledRead | VkAccessFlags2.TransferWrite);

    private static VkImageMemoryBarrier2 Barrier(VkImage image, VkImageLayout oldLayout, VkImageLayout newLayout,
        VkPipelineStageFlags2 srcStage, VkAccessFlags2 srcAccess, VkPipelineStageFlags2 dstStage, VkAccessFlags2 dstAccess) => new()
    {
        srcStageMask = srcStage,
        srcAccessMask = srcAccess,
        dstStageMask = dstStage,
        dstAccessMask = dstAccess,
        oldLayout = oldLayout,
        newLayout = newLayout,
        srcQueueFamilyIndex = QueueFamilyIgnored,
        dstQueueFamilyIndex = QueueFamilyIgnored,
        image = image,
        subresourceRange = GpuContext.ColorRange,
    };

    private void EnsureInstanceCapacity(ref Slot slot, int count)
    {
        if (slot.InstanceCapacity >= count && slot.Instances.Buffer.IsNotNull)
            return;
        int capacity = Math.Max(256, slot.InstanceCapacity);
        while (capacity < count)
            capacity *= 2;
        _gpu.Destroy(ref slot.Instances); // the slot's previous frame has completed
        slot.Instances = _gpu.CreateBuffer((ulong)(capacity * sizeof(SpriteInstanceData)), VkBufferUsageFlags.VertexBuffer,
            hostVisible: true, preferHostCached: false, "sprite instances");
        slot.InstanceCapacity = capacity;
    }

    /// <summary>Grows the slot's staging buffer to <paramref name="size"/> bytes, keeping the first <paramref name="keep"/> bytes.</summary>
    private void EnsureStaging(ref Slot slot, int size, int keep)
    {
        if (slot.Staging.Buffer.IsNotNull && slot.Staging.Size >= (ulong)size)
            return;
        ulong capacity = Math.Max(slot.Staging.Size, 256 * 1024);
        while (capacity < (ulong)size)
            capacity *= 2;
        GpuBuffer grown = _gpu.CreateBuffer(capacity, VkBufferUsageFlags.TransferSrc, hostVisible: true, preferHostCached: false, "sprite staging");
        if (keep > 0 && slot.Staging.Buffer.IsNotNull)
            new ReadOnlySpan<byte>(slot.Staging.Mapped, keep).CopyTo(new Span<byte>(grown.Mapped, keep));
        _gpu.Destroy(ref slot.Staging); // not referenced by any submitted work (the slot's fence signalled)
        slot.Staging = grown;
    }

    private void WarnOversize(SpriteImageKey key, SpriteImage image)
    {
        if (_warnedOversize)
            return;
        _warnedOversize = true;
        _log.Warning($"Sprite {key} ({image.Width}x{image.Height}) is larger than the {AtlasSize}x{AtlasSize} atlas and is not drawn.");
    }

    // ---- creation ---------------------------------------------------------------------------

    private void CreateDescriptors()
    {
        var api = _gpu.DeviceApi;
        VkDescriptorSetLayoutBinding* bindings = stackalloc VkDescriptorSetLayoutBinding[4];
        for (uint i = 0; i < 4; i++)
        {
            bindings[i] = new VkDescriptorSetLayoutBinding
            {
                binding = i,
                descriptorType = VkDescriptorType.CombinedImageSampler,
                descriptorCount = 1,
                stageFlags = VkShaderStageFlags.Fragment,
            };
        }
        var layoutInfo = new VkDescriptorSetLayoutCreateInfo { bindingCount = 4, pBindings = bindings };
        VkDescriptorSetLayout setLayout;
        api.vkCreateDescriptorSetLayout(&layoutInfo, null, &setLayout).Check("vkCreateDescriptorSetLayout (sprites)");
        _setLayout = setLayout;

        var pushRange = new VkPushConstantRange { stageFlags = VkShaderStageFlags.Fragment, offset = 0, size = (uint)sizeof(SpritePushConstants) };
        var pipelineLayoutInfo = new VkPipelineLayoutCreateInfo
        {
            setLayoutCount = 1,
            pSetLayouts = &setLayout,
            pushConstantRangeCount = 1,
            pPushConstantRanges = &pushRange,
        };
        VkPipelineLayout pipelineLayout;
        api.vkCreatePipelineLayout(&pipelineLayoutInfo, null, &pipelineLayout).Check("vkCreatePipelineLayout (sprites)");
        _pipelineLayout = pipelineLayout;

        var poolSize = new VkDescriptorPoolSize { type = VkDescriptorType.CombinedImageSampler, descriptorCount = 4 };
        var poolInfo = new VkDescriptorPoolCreateInfo { maxSets = 1, poolSizeCount = 1, pPoolSizes = &poolSize };
        VkDescriptorPool pool;
        api.vkCreateDescriptorPool(&poolInfo, null, &pool).Check("vkCreateDescriptorPool (sprites)");
        _descriptorPool = pool;

        var allocateInfo = new VkDescriptorSetAllocateInfo { descriptorPool = pool, descriptorSetCount = 1, pSetLayouts = &setLayout };
        VkDescriptorSet set;
        api.vkAllocateDescriptorSets(&allocateInfo, &set).Check("vkAllocateDescriptorSets (sprites)");
        _descriptorSet = set;

        VkDescriptorImageInfo* images = stackalloc VkDescriptorImageInfo[4];
        images[0] = new VkDescriptorImageInfo { sampler = _classic.Sampler, imageView = _atlas.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[1] = new VkDescriptorImageInfo { sampler = _classic.Sampler, imageView = _classic.PaletteView, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[2] = new VkDescriptorImageInfo { sampler = _classic.Sampler, imageView = _classic.IndexView, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[3] = new VkDescriptorImageInfo { sampler = _classic.Sampler, imageView = _mask.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        VkWriteDescriptorSet* writes = stackalloc VkWriteDescriptorSet[4];
        for (uint i = 0; i < 4; i++)
        {
            writes[i] = new VkWriteDescriptorSet
            {
                dstSet = set,
                dstBinding = i,
                descriptorCount = 1,
                descriptorType = VkDescriptorType.CombinedImageSampler,
                pImageInfo = images + i,
            };
        }
        api.vkUpdateDescriptorSets(4, writes, 0, null);
    }

    private VkPipeline CreatePipeline(VkFormat colorFormat)
    {
        ReadOnlySpan<byte> entryPoint = "main\0"u8;
        fixed (byte* main = entryPoint)
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _vertexShader, pName = main };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _fragmentShader, pName = main };

            var binding = new VkVertexInputBindingDescription
            {
                binding = 0,
                stride = (uint)sizeof(SpriteInstanceData),
                inputRate = VkVertexInputRate.Instance,
            };
            VkVertexInputAttributeDescription* attributes = stackalloc VkVertexInputAttributeDescription[4];
            attributes[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32Sfloat, offset = 0 };
            attributes[1] = new VkVertexInputAttributeDescription { location = 1, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 8 };
            attributes[2] = new VkVertexInputAttributeDescription { location = 2, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 24 };
            attributes[3] = new VkVertexInputAttributeDescription { location = 3, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 40 };
            var vertexInput = new VkPipelineVertexInputStateCreateInfo
            {
                vertexBindingDescriptionCount = 1,
                pVertexBindingDescriptions = &binding,
                vertexAttributeDescriptionCount = 4,
                pVertexAttributeDescriptions = attributes,
            };
            var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleStrip };
            var viewportState = new VkPipelineViewportStateCreateInfo { viewportCount = 1, scissorCount = 1 };
            var rasterization = new VkPipelineRasterizationStateCreateInfo
            {
                polygonMode = VkPolygonMode.Fill,
                cullMode = VkCullModeFlags.None, // flips and rotations change the winding
                frontFace = VkFrontFace.CounterClockwise,
                lineWidth = 1f,
            };
            var multisample = new VkPipelineMultisampleStateCreateInfo { rasterizationSamples = VkSampleCountFlags.Count1 };
            var blendAttachment = new VkPipelineColorBlendAttachmentState
            {
                blendEnable = true,
                srcColorBlendFactor = VkBlendFactor.One, // premultiplied alpha
                dstColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
                colorBlendOp = VkBlendOp.Add,
                srcAlphaBlendFactor = VkBlendFactor.One,
                dstAlphaBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
                alphaBlendOp = VkBlendOp.Add,
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
                .Check($"vkCreateGraphicsPipelines (sprites, {VkNames.Format(colorFormat)})");
            _gpu.SetName(VkObjectType.Pipeline, pipeline.Handle, "sprite pass");
            return pipeline;
        }
    }
}
