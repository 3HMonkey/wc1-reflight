using System.Runtime.InteropServices;
using Vortice.Vulkan;
using WingCommander.Core.Rendering;
using WingCommander.Core.Video;

namespace WingCommander.Render.Vulkan.Internal;

/// <summary>Per-instance vertex data of <c>text.vert</c> (48 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TextInstanceData
{
    /// <summary>Quad: game text in logical 320x200 pixels (the glyph cell), overlay in target pixels.</summary>
    public float X;
    public float Y;
    public float Width;
    public float Height;

    /// <summary>The glyph's distance field in the atlas (texels); unused by rectangles.</summary>
    public float FieldX;
    public float FieldY;
    public float FieldWidth;
    public float FieldHeight;

    /// <summary>Game text: palette index of the text colour. Overlay: RGBA8, red in the lowest byte, straight alpha.</summary>
    public uint Colour;

    /// <summary>Game text: the instance's index in <see cref="TextLayer.Instances"/> (mask test).</summary>
    public uint ListIndex;

    /// <summary><see cref="TextPass.FlagMulticolour"/>, <see cref="TextPass.FlagRectangle"/>.</summary>
    public uint Flags;

    /// <summary>The glyph's <see cref="GlyphImage.InkIndex"/> (multicolour glyphs).</summary>
    public uint InkIndex;
}

/// <summary>Push constants of <c>text.vert</c> and <c>text.frag</c> (32 bytes, both stages).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TextPushConstants
{
    public float DestX;
    public float DestY;
    public float DestWidth;
    public float DestHeight;
    public float SpaceWidth;
    public float SpaceHeight;
    public uint Mode;
    public uint EncodeLinear;
}

/// <summary>
/// Output-resolution text (ADR-013): the game's font glyphs (<see cref="RenderFrame.Text"/>) and
/// the key help overlay (<see cref="RenderFrame.KeyHelp"/>), drawn after the classic layer and the
/// space sprites inside the same rendering scope. Glyph images (<see cref="GlyphImage"/>: signed
/// distance field + palette index per texel) are uploaded once into an RG8_UNORM atlas, the 320x200
/// text mask (R16_UINT) whenever the text layer changes; every frame writes one instance record per
/// glyph or overlay item into the slot's host-visible vertex buffer and draws them with two
/// instanced calls: the game text inside the letterbox rectangle in logical pixels (mask test,
/// live palette), then the overlay over the whole target in target pixels (RGBA colours).
/// Created on the first frame that needs it, so frames without text never touch it.
/// </summary>
/// <remarks>
/// Atlas entries are keyed by <see cref="GlyphImage"/> identity, so glyphs of different caches
/// never mix; like the sprite atlas, it is still reset when the text layer's or the overlay's
/// cache is replaced or changes <see cref="GlyphImageCache.Generation"/> (stale glyphs would only
/// fill it), and when it is full (the frame is retried with only its own glyphs).
/// </remarks>
internal sealed unsafe class TextPass : IDisposable
{
    public const int AtlasSize = 2048;
    public const uint FlagMulticolour = 1;
    public const uint FlagRectangle = 2;
    private const int MaskBytes = Framebuffer.PixelCount * sizeof(ushort);
    private const uint ModeGameText = 0;
    private const uint ModeOverlay = 1;
    private const uint QueueFamilyIgnored = uint.MaxValue;
    private const VkShaderStageFlags PushConstantStages = VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment;

    private readonly GpuContext _gpu;
    private readonly ClassicPass _classic;
    private readonly RendererLog _log;
    private readonly ShelfAtlas<GlyphImage> _atlasPacker = new(AtlasSize);
    private readonly Slot[] _slots;
    private GpuImage _atlas;
    private GpuImage _mask;
    private bool _imagesInitialized;
    private VkSampler _linearSampler;
    private VkDescriptorSetLayout _setLayout;
    private VkPipelineLayout _pipelineLayout;
    private VkDescriptorPool _descriptorPool;
    private VkDescriptorSet _descriptorSet;
    private VkShaderModule _vertexShader;
    private VkShaderModule _fragmentShader;
    private VkPipeline _pipeline;
    private VkFormat _pipelineFormat;
    private VkBufferImageCopy[] _regions = new VkBufferImageCopy[64];

    private GlyphImageCache? _textCache;
    private int _textCacheGeneration;
    private GlyphImageCache? _overlayCache;
    private int _overlayCacheGeneration;
    private TextLayer? _uploadedMask;
    private int _uploadedMaskVersion;
    private bool _warnedOverflow;
    private bool _warnedOversize;

    /// <summary>Per frame in flight: instance vertex buffer and upload staging (reused after the slot's fence).</summary>
    private struct Slot
    {
        public GpuBuffer Instances;
        public int InstanceCapacity;
        public GpuBuffer Staging;
        public int TextCount;    // game text instances first ...
        public int OverlayCount; // ... then the overlay items
        public TextPushConstants TextConstants;
        public TextPushConstants OverlayConstants;
    }

    public TextPass(GpuContext gpu, ClassicPass classic, RendererLog log, int framesInFlight)
    {
        _gpu = gpu;
        _classic = classic;
        _log = log;
        _slots = new Slot[framesInFlight];
        try
        {
            _atlas = gpu.CreateImage(AtlasSize, AtlasSize, VkFormat.R8G8Unorm, VkImageUsageFlags.TransferDst | VkImageUsageFlags.Sampled, "glyph atlas");
            _mask = gpu.CreateImage(Framebuffer.Width, Framebuffer.Height, VkFormat.R16Uint, VkImageUsageFlags.TransferDst | VkImageUsageFlags.Sampled, "text mask");
            CreateDescriptors();
            _vertexShader = ShaderLibrary.CreateModule(gpu, "text.vert.spv");
            _fragmentShader = ShaderLibrary.CreateModule(gpu, "text.frag.spv");
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
    /// Before rendering begins: resolves the glyphs of <paramref name="text"/> (null: no game text)
    /// and the items of <paramref name="overlay"/> to atlas entries, uploading new glyph fields and
    /// a changed text mask through the slot's staging buffer, and writes the instance records.
    /// Returns false when nothing is drawn.
    /// </summary>
    public bool Prepare(VkCommandBuffer cmd, int slotIndex, TextLayer? text, OverlayDrawList overlay, PresentationRect rect,
        int targetWidth, int targetHeight, bool encodeLinear, VulkanRendererStatistics statistics)
    {
        ref Slot slot = ref _slots[slotIndex];
        slot.TextCount = 0;
        slot.OverlayCount = 0;
        ReadOnlySpan<GlyphInstance> glyphs = text is not null && !rect.IsEmpty ? text.Instances : default;
        ReadOnlySpan<OverlayItem> items = overlay.Items;
        if (glyphs.IsEmpty && items.IsEmpty)
            return false;

        if (!_imagesInitialized)
            InitializeImages(cmd);
        GlyphImageCache? textGlyphs = glyphs.IsEmpty ? null : text!.Glyphs;
        GlyphImageCache? overlayGlyphs = items.IsEmpty ? null : overlay.Glyphs;
        TrackCaches(textGlyphs, overlayGlyphs, statistics);

        bool uploadMask = !glyphs.IsEmpty && (!ReferenceEquals(text, _uploadedMask) || text!.Version != _uploadedMaskVersion);
        int stagingStart = 0;
        if (uploadMask)
        {
            EnsureStaging(ref slot, MaskBytes, 0);
            MemoryMarshal.AsBytes(text!.Mask.AsSpan()).CopyTo(new Span<byte>(slot.Staging.Mapped, MaskBytes));
            stagingStart = MaskBytes;
        }

        EnsureInstanceCapacity(ref slot, glyphs.Length + items.Length);
        int textCount = 0;
        int overlayCount = 0;
        int regionCount = 0;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            textCount = 0;
            overlayCount = 0;
            regionCount = 0;
            int stagingUsed = stagingStart;
            bool full = false;
            var instances = (TextInstanceData*)slot.Instances.Mapped;
            for (int i = 0; i < glyphs.Length && !full; i++)
            {
                ref readonly GlyphInstance glyph = ref glyphs[i];
                if (!textGlyphs!.TryGet(glyph.Glyph, out GlyphImage? image) || !image.HasForeground)
                    continue; // nothing to draw (space), or not published: the classic pixels have no foreground there either
                if (!TryResolve(ref slot, glyph.Glyph, image, ref stagingUsed, ref regionCount, ref full, out AtlasEntry entry))
                    continue;
                instances[textCount++] = GameGlyph(in glyph, i, image, in entry);
            }
            TextInstanceData* overlayInstances = instances + textCount;
            for (int i = 0; i < items.Length && !full; i++)
            {
                ref readonly OverlayItem item = ref items[i];
                if (!(item.Width > 0f) || !(item.Height > 0f))
                    continue;
                if (item.Kind == OverlayItemKind.Rectangle)
                {
                    overlayInstances[overlayCount++] = OverlayRectangle(in item);
                    continue;
                }
                if (overlayGlyphs is null || !overlayGlyphs.TryGet(item.Glyph, out GlyphImage? image) || !image.HasForeground)
                    continue;
                if (!TryResolve(ref slot, item.Glyph, image, ref stagingUsed, ref regionCount, ref full, out AtlasEntry entry))
                    continue;
                overlayInstances[overlayCount++] = OverlayGlyph(in item, image, in entry);
            }
            if (!full)
                break;

            // Atlas full: start over with only this frame's glyphs (cheap: they are re-uploaded once).
            statistics.TextAtlasResets++;
            _atlasPacker.Reset();
            if (attempt == 1 && !_warnedOverflow)
            {
                _warnedOverflow = true;
                _log.Warning($"The glyphs of one frame do not fit into the {AtlasSize}x{AtlasSize} glyph atlas; some are not drawn.");
            }
        }

        if (uploadMask || regionCount > 0)
            RecordUploads(cmd, slot.Staging.Buffer, uploadMask, regionCount);
        if (uploadMask)
        {
            _uploadedMask = text;
            _uploadedMaskVersion = text!.Version;
            statistics.TextMaskUploads++;
        }
        statistics.GlyphUploads += regionCount;
        statistics.GlyphsDrawn += textCount;
        statistics.OverlayItemsDrawn += overlayCount;

        slot.TextCount = textCount;
        slot.OverlayCount = overlayCount;
        slot.TextConstants = new TextPushConstants
        {
            DestX = rect.X,
            DestY = rect.Y,
            DestWidth = rect.Width,
            DestHeight = rect.Height,
            SpaceWidth = Framebuffer.Width,
            SpaceHeight = Framebuffer.Height,
            Mode = ModeGameText,
            EncodeLinear = encodeLinear ? 1u : 0u,
        };
        slot.OverlayConstants = new TextPushConstants
        {
            DestX = 0f,
            DestY = 0f,
            DestWidth = targetWidth,
            DestHeight = targetHeight,
            SpaceWidth = targetWidth,
            SpaceHeight = targetHeight,
            Mode = ModeOverlay,
            EncodeLinear = encodeLinear ? 1u : 0u,
        };
        return textCount + overlayCount > 0;
    }

    /// <summary>
    /// Inside the rendering scope, after the classic and sprite draws: the game text inside the
    /// letterbox rectangle, then the overlay over the whole <paramref name="targetWidth"/> x
    /// <paramref name="targetHeight"/> target.
    /// </summary>
    public void RecordDraw(VkCommandBuffer cmd, int slotIndex, PresentationRect rect, int targetWidth, int targetHeight)
    {
        ref Slot slot = ref _slots[slotIndex];
        if (slot.TextCount == 0 && slot.OverlayCount == 0)
            return;
        var api = _gpu.DeviceApi;
        api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _pipeline);
        VkDescriptorSet set = _descriptorSet;
        api.vkCmdBindDescriptorSets(cmd, VkPipelineBindPoint.Graphics, _pipelineLayout, 0, 1, &set);
        VkBuffer buffer = slot.Instances.Buffer;
        ulong offset = 0;
        api.vkCmdBindVertexBuffers(cmd, 0, 1, &buffer, &offset);
        if (slot.TextCount > 0)
        {
            SetViewport(cmd, rect.X, rect.Y, rect.Width, rect.Height);
            TextPushConstants constants = slot.TextConstants;
            api.vkCmdPushConstants(cmd, _pipelineLayout, PushConstantStages, 0, (uint)sizeof(TextPushConstants), &constants);
            api.vkCmdDraw(cmd, 4, (uint)slot.TextCount, 0, 0);
        }
        if (slot.OverlayCount > 0)
        {
            SetViewport(cmd, 0, 0, targetWidth, targetHeight);
            TextPushConstants constants = slot.OverlayConstants;
            api.vkCmdPushConstants(cmd, _pipelineLayout, PushConstantStages, 0, (uint)sizeof(TextPushConstants), &constants);
            api.vkCmdDraw(cmd, 4, (uint)slot.OverlayCount, 0, (uint)slot.TextCount);
        }
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
        if (_linearSampler.IsNotNull)
            api.vkDestroySampler(_linearSampler);
        if (_vertexShader.IsNotNull)
            api.vkDestroyShaderModule(_vertexShader);
        if (_fragmentShader.IsNotNull)
            api.vkDestroyShaderModule(_fragmentShader);
        _pipeline = VkPipeline.Null;
        _pipelineLayout = VkPipelineLayout.Null;
        _descriptorPool = VkDescriptorPool.Null;
        _setLayout = VkDescriptorSetLayout.Null;
        _linearSampler = VkSampler.Null;
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

    /// <summary>
    /// Resets the atlas when the text layer's or the overlay's glyph cache was replaced or changed
    /// generation since it was last used; a role absent from this frame keeps its record.
    /// </summary>
    private void TrackCaches(GlyphImageCache? text, GlyphImageCache? overlay, VulkanRendererStatistics statistics)
    {
        bool stale = IsStale(text, _textCache, _textCacheGeneration) || IsStale(overlay, _overlayCache, _overlayCacheGeneration);
        if (stale)
        {
            if (_atlasPacker.Count > 0)
                statistics.TextAtlasResets++;
            _atlasPacker.Reset();
        }
        if (text is not null)
            _textCache = text;
        if (overlay is not null)
            _overlayCache = overlay;
        if (stale || text is not null)
            _textCacheGeneration = _textCache?.Generation ?? 0;
        if (stale || overlay is not null)
            _overlayCacheGeneration = _overlayCache?.Generation ?? 0;
    }

    private static bool IsStale(GlyphImageCache? cache, GlyphImageCache? tracked, int generation) =>
        cache is not null && tracked is not null && (!ReferenceEquals(cache, tracked) || cache.Generation != generation);

    /// <summary>
    /// Finds or uploads <paramref name="image"/>. False when it cannot be drawn: larger than the
    /// atlas (warned once), or the atlas is full (<paramref name="full"/> set, the caller retries).
    /// </summary>
    private bool TryResolve(ref Slot slot, GlyphKey key, GlyphImage image, ref int stagingUsed, ref int regionCount, ref bool full,
        out AtlasEntry entry)
    {
        if (_atlasPacker.TryGet(image, out entry))
            return true;
        int width = image.FieldWidth;
        int height = image.FieldHeight;
        if (width > AtlasSize || height > AtlasSize)
        {
            WarnOversize(key, image);
            return false;
        }
        if (!_atlasPacker.TryAllocate(width, height, out int x, out int y))
        {
            full = true;
            return false;
        }
        int bytes = width * height * GlyphImage.BytesPerTexel;
        int offset = (stagingUsed + 3) & ~3;
        EnsureStaging(ref slot, offset + bytes, stagingUsed);
        image.Texels.Span[..bytes].CopyTo(new Span<byte>(slot.Staging.Mapped + offset, bytes));
        stagingUsed = offset + bytes;
        AddRegion(ref regionCount, offset, x, y, width, height);
        entry = new AtlasEntry(x, y, width, height, 0, 0);
        _atlasPacker.Add(image, entry);
        return true;
    }

    private static TextInstanceData GameGlyph(in GlyphInstance glyph, int listIndex, GlyphImage image, in AtlasEntry entry) => new()
    {
        X = glyph.X,
        Y = glyph.Y,
        Width = image.Width * glyph.ScaleX,
        Height = image.Height * glyph.ScaleY,
        FieldX = entry.X,
        FieldY = entry.Y,
        FieldWidth = entry.Width,
        FieldHeight = entry.Height,
        Colour = glyph.Colour,
        ListIndex = (uint)listIndex,
        Flags = image.Multicolour ? FlagMulticolour : 0u,
        InkIndex = image.InkIndex,
    };

    private static TextInstanceData OverlayGlyph(in OverlayItem item, GlyphImage image, in AtlasEntry entry) => new()
    {
        X = item.X,
        Y = item.Y,
        Width = item.Width,
        Height = item.Height,
        FieldX = entry.X,
        FieldY = entry.Y,
        FieldWidth = entry.Width,
        FieldHeight = entry.Height,
        Colour = item.Colour,
        InkIndex = image.InkIndex,
    };

    private static TextInstanceData OverlayRectangle(in OverlayItem item) => new()
    {
        X = item.X,
        Y = item.Y,
        Width = item.Width,
        Height = item.Height,
        Colour = item.Colour,
        Flags = FlagRectangle,
    };

    private void SetViewport(VkCommandBuffer cmd, int x, int y, int width, int height)
    {
        var viewport = new VkViewport { x = x, y = y, width = width, height = height, minDepth = 0f, maxDepth = 1f };
        _gpu.DeviceApi.vkCmdSetViewport(cmd, 0, 1, &viewport);
        var scissor = new VkRect2D
        {
            offset = new VkOffset2D { x = x, y = y },
            extent = new VkExtent2D { width = (uint)width, height = (uint)height },
        };
        _gpu.DeviceApi.vkCmdSetScissor(cmd, 0, 1, &scissor);
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

    /// <summary>First use: atlas cleared to distance 0 (far outside every glyph), mask to 0 (no glyph draws), both readable.</summary>
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
        var outside = new VkClearColorValue(0f, 0f, 0f, 0f);
        _gpu.DeviceApi.vkCmdClearColorImage(cmd, _atlas.Image, VkImageLayout.TransferDstOptimal, &outside, 1, &range);
        var nothing = new VkClearColorValue(0u, 0u, 0u, 0u);
        _gpu.DeviceApi.vkCmdClearColorImage(cmd, _mask.Image, VkImageLayout.TransferDstOptimal, &nothing, 1, &range);

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
        slot.Instances = _gpu.CreateBuffer((ulong)(capacity * sizeof(TextInstanceData)), VkBufferUsageFlags.VertexBuffer,
            hostVisible: true, preferHostCached: false, "text instances");
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
        GpuBuffer grown = _gpu.CreateBuffer(capacity, VkBufferUsageFlags.TransferSrc, hostVisible: true, preferHostCached: false, "text staging");
        if (keep > 0 && slot.Staging.Buffer.IsNotNull)
            new ReadOnlySpan<byte>(slot.Staging.Mapped, keep).CopyTo(new Span<byte>(grown.Mapped, keep));
        _gpu.Destroy(ref slot.Staging); // not referenced by any submitted work (the slot's fence signalled)
        slot.Staging = grown;
    }

    private void WarnOversize(GlyphKey key, GlyphImage image)
    {
        if (_warnedOversize)
            return;
        _warnedOversize = true;
        _log.Warning($"Glyph {key} ({image.FieldWidth}x{image.FieldHeight} field texels) is larger than the {AtlasSize}x{AtlasSize} glyph atlas and is not drawn.");
    }

    // ---- creation ---------------------------------------------------------------------------

    private void CreateDescriptors()
    {
        var api = _gpu.DeviceApi;
        // The distance channel is filtered (bilinear, like GlyphRasterizer.SampleDistance); the
        // shader clamps every lookup to the glyph's own field, so neighbours never bleed in.
        var samplerInfo = new VkSamplerCreateInfo
        {
            magFilter = VkFilter.Linear,
            minFilter = VkFilter.Linear,
            mipmapMode = VkSamplerMipmapMode.Nearest,
            addressModeU = VkSamplerAddressMode.ClampToEdge,
            addressModeV = VkSamplerAddressMode.ClampToEdge,
            addressModeW = VkSamplerAddressMode.ClampToEdge,
            maxLod = 0f,
            borderColor = VkBorderColor.FloatTransparentBlack,
        };
        VkSampler linear;
        api.vkCreateSampler(&samplerInfo, null, &linear).Check("vkCreateSampler (glyph atlas)");
        _linearSampler = linear;

        VkDescriptorSetLayoutBinding* bindings = stackalloc VkDescriptorSetLayoutBinding[3];
        for (uint i = 0; i < 3; i++)
        {
            bindings[i] = new VkDescriptorSetLayoutBinding
            {
                binding = i,
                descriptorType = VkDescriptorType.CombinedImageSampler,
                descriptorCount = 1,
                stageFlags = VkShaderStageFlags.Fragment,
            };
        }
        var layoutInfo = new VkDescriptorSetLayoutCreateInfo { bindingCount = 3, pBindings = bindings };
        VkDescriptorSetLayout setLayout;
        api.vkCreateDescriptorSetLayout(&layoutInfo, null, &setLayout).Check("vkCreateDescriptorSetLayout (text)");
        _setLayout = setLayout;

        var pushRange = new VkPushConstantRange { stageFlags = PushConstantStages, offset = 0, size = (uint)sizeof(TextPushConstants) };
        var pipelineLayoutInfo = new VkPipelineLayoutCreateInfo
        {
            setLayoutCount = 1,
            pSetLayouts = &setLayout,
            pushConstantRangeCount = 1,
            pPushConstantRanges = &pushRange,
        };
        VkPipelineLayout pipelineLayout;
        api.vkCreatePipelineLayout(&pipelineLayoutInfo, null, &pipelineLayout).Check("vkCreatePipelineLayout (text)");
        _pipelineLayout = pipelineLayout;

        var poolSize = new VkDescriptorPoolSize { type = VkDescriptorType.CombinedImageSampler, descriptorCount = 3 };
        var poolInfo = new VkDescriptorPoolCreateInfo { maxSets = 1, poolSizeCount = 1, pPoolSizes = &poolSize };
        VkDescriptorPool pool;
        api.vkCreateDescriptorPool(&poolInfo, null, &pool).Check("vkCreateDescriptorPool (text)");
        _descriptorPool = pool;

        var allocateInfo = new VkDescriptorSetAllocateInfo { descriptorPool = pool, descriptorSetCount = 1, pSetLayouts = &setLayout };
        VkDescriptorSet set;
        api.vkAllocateDescriptorSets(&allocateInfo, &set).Check("vkAllocateDescriptorSets (text)");
        _descriptorSet = set;

        VkDescriptorImageInfo* images = stackalloc VkDescriptorImageInfo[3];
        images[0] = new VkDescriptorImageInfo { sampler = linear, imageView = _atlas.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[1] = new VkDescriptorImageInfo { sampler = _classic.Sampler, imageView = _mask.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[2] = new VkDescriptorImageInfo { sampler = _classic.Sampler, imageView = _classic.PaletteView, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        VkWriteDescriptorSet* writes = stackalloc VkWriteDescriptorSet[3];
        for (uint i = 0; i < 3; i++)
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
        api.vkUpdateDescriptorSets(3, writes, 0, null);
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
                stride = (uint)sizeof(TextInstanceData),
                inputRate = VkVertexInputRate.Instance,
            };
            VkVertexInputAttributeDescription* attributes = stackalloc VkVertexInputAttributeDescription[3];
            attributes[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 0 };
            attributes[1] = new VkVertexInputAttributeDescription { location = 1, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 16 };
            attributes[2] = new VkVertexInputAttributeDescription { location = 2, binding = 0, format = VkFormat.R32G32B32A32Uint, offset = 32 };
            var vertexInput = new VkPipelineVertexInputStateCreateInfo
            {
                vertexBindingDescriptionCount = 1,
                pVertexBindingDescriptions = &binding,
                vertexAttributeDescriptionCount = 3,
                pVertexAttributeDescriptions = attributes,
            };
            var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleStrip };
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
                .Check($"vkCreateGraphicsPipelines (text, {VkNames.Format(colorFormat)})");
            _gpu.SetName(VkObjectType.Pipeline, pipeline.Handle, "text pass");
            return pipeline;
        }
    }
}
