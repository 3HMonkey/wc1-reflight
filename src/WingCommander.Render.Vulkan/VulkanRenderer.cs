using Vortice.Vulkan;
using WingCommander.Core.Rendering;
using WingCommander.Render.Vulkan.Internal;

namespace WingCommander.Render.Vulkan;

/// <summary>
/// The modern renderer (ADR-010): Vulkan 1.2+ with dynamic rendering and synchronization2,
/// two frames in flight. R1 draws the classic 320x200 indexed layer with a palette lookup in a
/// fragment shader, letterboxed with <see cref="PresentationLayout"/> (the same rectangle the
/// host uses for mouse mapping) and filtered per <see cref="RendererSettings.Filter"/>. On top,
/// in the same rendering scope: the space sprites (R2), the game's text and the key help at
/// output resolution (ADR-013).
/// </summary>
/// <remarks>
/// <para>Two modes: <see cref="Create"/> presents to a window through
/// <see cref="IVulkanSurfaceSource"/>; <see cref="CreateOffscreen"/> renders into an image
/// (tests, screenshots, headless tools).</para>
/// <para>Frame flow: wait for the slot's fence (the frame two submissions ago), acquire a
/// swapchain image, upload changed pixels/palette from the slot's staging region, render, submit
/// with synchronization2, present. All calls must come from one thread (the host's main loop).
/// Creation failures throw <see cref="VulkanUnavailableException"/> so the host can fall back to
/// its SDL_Renderer presenter.</para>
/// </remarks>
public sealed unsafe class VulkanRenderer : IRenderer
{
    /// <summary>Frames the CPU may record ahead of the GPU.</summary>
    public const int FramesInFlight = 2;

    private const ulong FenceTimeoutNs = 5_000_000_000;   // a frame taking 5 s means a hung GPU
    private const ulong AcquireTimeoutNs = 1_000_000_000; // hidden windows may never release an image
    private const uint QueueFamilyIgnored = uint.MaxValue;

    private readonly RendererLog _log;
    private readonly IVulkanSurfaceSource? _surfaceSource;
    private readonly GpuContext _gpu;
    private readonly ClassicPass _classic;
    private readonly FrameSlot[] _slots = new FrameSlot[FramesInFlight];
    private readonly Queue<CapturedImage> _completedCaptures = new();
    private readonly bool _offscreen;
    private readonly VkFormat _offscreenFormat;

    private readonly KeyHelpLayoutCache _keyHelpLayout = new();

    private SpritePass? _sprites; // created by the first frame that carries space sprites
    private TextPass? _text;      // created by the first frame that carries text glyphs or a visible key help
    private Swapchain? _swapchain;
    private bool _swapchainDirty;

    private GpuImage _offscreenTarget;
    private int _offscreenWidth;
    private int _offscreenHeight;
    private bool _offscreenHasContent;
    private long _offscreenFrameNumber;

    // One-shot readback of the offscreen target (CaptureLastFrame), created on first use.
    private VkCommandPool _oneShotPool;
    private VkCommandBuffer _oneShotBuffer;
    private VkFence _oneShotFence;
    private GpuBuffer _oneShotReadback;

    private long _frameNumber;
    private bool _captureRequested;
    private bool _disposed;

    private VulkanRenderer(IVulkanSurfaceSource? surfaceSource, int width, int height, RendererSettings? settings, VulkanRendererOptions? options)
    {
        options ??= new VulkanRendererOptions();
        Settings = settings ?? new RendererSettings();
        _log = new RendererLog(options.Log);
        _surfaceSource = surfaceSource;
        _offscreen = surfaceSource is null;
        _offscreenFormat = options.OffscreenFormat;
        if (_offscreen && !Readback.CanConvert(_offscreenFormat))
            throw new ArgumentException($"Unsupported offscreen format {VkNames.Format(_offscreenFormat)}.", nameof(options));

        _gpu = GpuContext.Create(surfaceSource, options, _log, Statistics);
        try
        {
            _classic = new ClassicPass(_gpu, FramesInFlight);
            for (int i = 0; i < FramesInFlight; i++)
                _slots[i] = FrameSlot.Create(_gpu, i, presenting: !_offscreen);
            if (_offscreen)
            {
                _classic.EnsurePipeline(_offscreenFormat);
                ResizeOffscreenTarget(width, height, countAsRecreation: false);
            }
            else
            {
                _swapchainDirty = true; // created on the first Render, when the drawable size is known
            }
        }
        catch
        {
            ReleaseResources();
            throw;
        }

        Name = $"Vulkan ({_gpu.Capabilities.DeviceName})";
        _log.Info($"Vulkan renderer: {_gpu.Capabilities}");
    }

    /// <summary>"Vulkan (NVIDIA GeForce RTX 2060 SUPER)".</summary>
    public string Name { get; }

    /// <summary>Read every frame: filter, aspect and integer scaling apply immediately; a VSync change recreates the swapchain.</summary>
    public RendererSettings Settings { get; }

    /// <summary>The selected GPU and the detected roadmap capabilities.</summary>
    public VulkanCapabilities Capabilities => _gpu.Capabilities;

    public VulkanRendererStatistics Statistics { get; } = new();

    public bool IsOffscreen => _offscreen;

    /// <summary>
    /// The renderer draws <see cref="RenderFrame.Space"/> sprites at output resolution (R2), so the
    /// game may leave them out of the classic layer.
    /// </summary>
    public bool SupportsSpaceSprites => true;

    /// <summary>
    /// The renderer draws <see cref="RenderFrame.Text"/> (the game's font glyphs over the text layer's
    /// pixels) and <see cref="RenderFrame.KeyHelp"/> at output resolution (ADR-013), so the game may
    /// publish them.
    /// </summary>
    public bool SupportsText => true;

    /// <summary>Pixel size of the render target (swapchain extent or offscreen image); (0, 0) before the first frame or while minimised.</summary>
    public (int Width, int Height) TargetSize => _offscreen
        ? (_offscreenTarget.IsNull ? (0, 0) : (_offscreenWidth, _offscreenHeight))
        : (_swapchain is null ? (0, 0) : (_swapchain.Width, _swapchain.Height));

    /// <summary>Format of the render target, e.g. "B8G8R8A8_UNORM" (diagnostics).</summary>
    public string TargetFormat => VkNames.Format(_offscreen ? _offscreenFormat : _swapchain?.Format ?? VkFormat.Undefined);

    /// <summary>Present mode of the swapchain ("FIFO", "MAILBOX", "IMMEDIATE"), or "offscreen".</summary>
    public string PresentMode => _offscreen ? "offscreen" : _swapchain is null ? "none" : VkNames.PresentMode(_swapchain.PresentMode);

    /// <summary>Creates a renderer that presents to a window.</summary>
    /// <exception cref="VulkanUnavailableException">Vulkan cannot be used; the message says why.</exception>
    public static VulkanRenderer Create(IVulkanSurfaceSource surfaceSource, RendererSettings? settings = null, VulkanRendererOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(surfaceSource);
        return new VulkanRenderer(surfaceSource, 0, 0, settings, options);
    }

    /// <summary>Creates a renderer without a window that draws into a <paramref name="width"/> x <paramref name="height"/> image.</summary>
    /// <exception cref="VulkanUnavailableException">Vulkan cannot be used; the message says why.</exception>
    public static VulkanRenderer CreateOffscreen(int width, int height, RendererSettings? settings = null, VulkanRendererOptions? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        return new VulkanRenderer(null, width, height, settings, options);
    }

    /// <summary>
    /// Whether a Vulkan renderer can be created on this machine (creates and disposes a tiny
    /// offscreen renderer). <paramref name="description"/> is the device name or the reason.
    /// </summary>
    public static bool IsAvailable(out string description, VulkanRendererOptions? options = null)
    {
        try
        {
            using var renderer = CreateOffscreen(8, 8, null, options ?? new VulkanRendererOptions { Log = static (_, _) => { } });
            description = renderer.Name;
            return true;
        }
        catch (VulkanRendererException e)
        {
            description = e.Message;
            return false;
        }
    }

    public void Render(RenderFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        if (_offscreen)
            RenderOffscreen(frame);
        else
            RenderToSwapchain(frame);
    }

    /// <summary>The window's drawable size changed: the swapchain is recreated before the next frame.</summary>
    public void SurfaceResized()
    {
        if (!_disposed)
            _swapchainDirty = true;
    }

    /// <summary>Changes the offscreen target size (0 x 0 is allowed: frames are then skipped).</summary>
    public void ResizeOffscreen(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_offscreen)
            throw new InvalidOperationException("ResizeOffscreen applies to offscreen renderers; a window renderer follows the drawable size.");
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (width == _offscreenWidth && height == _offscreenHeight)
            return;
        ResizeOffscreenTarget(width, height, countAsRecreation: true);
    }

    /// <summary>
    /// Copies the output of the next rendered frame into a host-visible buffer. Collect it with
    /// <see cref="TakeCapture"/> after <see cref="Render"/>; works for swapchain and offscreen targets.
    /// </summary>
    public void RequestCapture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_offscreen && _swapchain is { SupportsCapture: false })
            throw new NotSupportedException($"The swapchain ({VkNames.Format(_swapchain.Format)}) does not allow reading its images back.");
        _captureRequested = true;
    }

    /// <summary>
    /// The oldest completed capture, or null. With <paramref name="waitForGpu"/> the call waits
    /// for frames whose capture is still on the GPU; otherwise it only returns finished ones.
    /// </summary>
    public CapturedImage? TakeCapture(bool waitForGpu = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completedCaptures.Count == 0)
        {
            FrameSlot? first = null;
            FrameSlot? second = null;
            foreach (var slot in _slots)
            {
                if (!slot.CapturePending)
                    continue;
                if (first is null || slot.FrameNumber < first.FrameNumber)
                {
                    second = first;
                    first = slot;
                }
                else
                {
                    second = slot;
                }
            }
            TryCompleteSlot(first, waitForGpu);
            TryCompleteSlot(second, waitForGpu);
        }
        return _completedCaptures.Count > 0 ? _completedCaptures.Dequeue() : null;
    }

    /// <summary>
    /// Offscreen mode: reads back the image of the most recent <see cref="Render"/> (waits for
    /// the GPU). Swapchain images belong to the presentation engine once presented; use
    /// <see cref="RequestCapture"/> + <see cref="TakeCapture"/> there.
    /// </summary>
    public CapturedImage CaptureLastFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_offscreen)
            throw new InvalidOperationException("Swapchain images cannot be read after presentation; call RequestCapture() before Render() and then TakeCapture().");
        if (!_offscreenHasContent)
            throw new InvalidOperationException("Nothing has been rendered into the offscreen target since it was created or resized.");

        foreach (var slot in _slots)
            WaitForSlot(slot);

        int width = _offscreenWidth, height = _offscreenHeight;
        EnsureOneShotResources();
        EnsureBuffer(ref _oneShotReadback, (ulong)width * (ulong)height * Readback.BytesPerPixel, "one-shot readback");

        var api = _gpu.DeviceApi;
        VkCommandBuffer cmd = _oneShotBuffer;
        api.vkResetCommandPool(_oneShotPool, VkCommandPoolResetFlags.None).Check("vkResetCommandPool");
        var begin = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        api.vkBeginCommandBuffer(cmd, &begin).Check("vkBeginCommandBuffer");
        // The target is in TRANSFER_SRC_OPTIMAL and its writes were made visible to copies by the frame's final barrier.
        RecordImageToBuffer(cmd, _offscreenTarget.Image, _oneShotReadback, width, height);
        api.vkEndCommandBuffer(cmd).Check("vkEndCommandBuffer");

        var commandInfo = new VkCommandBufferSubmitInfo { commandBuffer = cmd };
        var submit = new VkSubmitInfo2 { commandBufferInfoCount = 1, pCommandBufferInfos = &commandInfo };
        VkFence fence = _oneShotFence;
        api.vkResetFences(1, &fence).Check("vkResetFences");
        _gpu.QueueSubmit2(_gpu.Queue, 1, &submit, fence).Check("vkQueueSubmit2 (capture)");
        WaitFence(fence, "capture");

        byte[] rgba = Readback.ToRgba(_oneShotReadback.Mapped, width, height, _offscreenFormat);
        Statistics.CapturesCompleted++;
        return new CapturedImage(width, height, rgba, _offscreenFrameNumber);
    }

    /// <summary>Tests: injects a message into the debug messenger (false when debug utils are off).</summary>
    internal bool SubmitDebugMessage(bool error, string text) => _gpu.SubmitDebugMessage(error, text);

    /// <summary>Waits until the GPU finished all submitted frames.</summary>
    public void WaitIdle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var slot in _slots)
            WaitForSlot(slot);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            _gpu.WaitIdle();
        }
        catch (VulkanRendererException e)
        {
            _log.Warning($"vkDeviceWaitIdle during shutdown failed: {e.Message}");
        }
        ReleaseResources();
    }

    // ---- frame flow ---------------------------------------------------------------------

    private void RenderToSwapchain(RenderFrame frame)
    {
        (int Width, int Height) drawable = _surfaceSource!.DrawableSize;
        if (drawable.Width <= 0 || drawable.Height <= 0)
        {
            Statistics.FramesSkipped++; // minimised: nothing to draw, nothing to recreate
            return;
        }

        if (_swapchain is null || _swapchainDirty || _swapchain.VSync != Settings.VSync || _swapchain.RequestedSize != drawable)
        {
            if (!RecreateSwapchain(drawable))
            {
                Statistics.FramesSkipped++;
                return;
            }
        }

        FrameSlot slot = _slots[(int)(_frameNumber % FramesInFlight)];
        WaitForSlot(slot);

        uint imageIndex = 0;
        VkResult acquired = _gpu.DeviceApi.vkAcquireNextImageKHR(_swapchain!.Handle, AcquireTimeoutNs, slot.ImageAvailable, VkFence.Null, &imageIndex);
        if (acquired == VkResult.ErrorOutOfDateKHR)
        {
            if (!RecreateSwapchain(drawable))
            {
                Statistics.FramesSkipped++;
                return;
            }
            acquired = _gpu.DeviceApi.vkAcquireNextImageKHR(_swapchain!.Handle, AcquireTimeoutNs, slot.ImageAvailable, VkFence.Null, &imageIndex);
        }
        switch (acquired)
        {
            case VkResult.Success:
                break;
            case VkResult.SuboptimalKHR:
                _swapchainDirty = true; // the image is acquired: render and present it, recreate afterwards
                break;
            case VkResult.Timeout:
            case VkResult.NotReady:
            case VkResult.ErrorOutOfDateKHR:
                _swapchainDirty |= acquired == VkResult.ErrorOutOfDateKHR;
                Statistics.FramesSkipped++;
                return;
            default:
                throw VkCheck.Error(acquired, "vkAcquireNextImageKHR");
        }

        Swapchain swapchain = _swapchain!;
        bool capture = _captureRequested && swapchain.SupportsCapture;
        VkSemaphore renderFinished = swapchain.RenderFinished[imageIndex];
        RecordAndSubmit(slot, frame, swapchain.Images[imageIndex], swapchain.Views[imageIndex], swapchain.Format,
            swapchain.Width, swapchain.Height, swapchain.EncodeLinear, presenting: true, capture, slot.ImageAvailable, renderFinished);

        VkSwapchainKHR handle = swapchain.Handle;
        var presentInfo = new VkPresentInfoKHR
        {
            waitSemaphoreCount = 1,
            pWaitSemaphores = &renderFinished,
            swapchainCount = 1,
            pSwapchains = &handle,
            pImageIndices = &imageIndex,
        };
        VkResult presented = _gpu.DeviceApi.vkQueuePresentKHR(_gpu.Queue, &presentInfo);
        if (presented == VkResult.ErrorOutOfDateKHR || presented == VkResult.SuboptimalKHR)
            _swapchainDirty = true;
        else
            presented.Check("vkQueuePresentKHR");
    }

    private void RenderOffscreen(RenderFrame frame)
    {
        if (_offscreenTarget.IsNull)
        {
            Statistics.FramesSkipped++; // 0 x 0 target
            return;
        }
        FrameSlot slot = _slots[(int)(_frameNumber % FramesInFlight)];
        WaitForSlot(slot);
        bool encodeLinear = _offscreenFormat is VkFormat.R8G8B8A8Srgb or VkFormat.B8G8R8A8Srgb;
        RecordAndSubmit(slot, frame, _offscreenTarget.Image, _offscreenTarget.View, _offscreenFormat,
            _offscreenWidth, _offscreenHeight, encodeLinear, presenting: false, _captureRequested, VkSemaphore.Null, VkSemaphore.Null);
        _offscreenHasContent = true;
        _offscreenFrameNumber = slot.FrameNumber;
    }

    /// <summary>Records the frame into the slot's command buffer and submits it (no heap allocations).</summary>
    private void RecordAndSubmit(FrameSlot slot, RenderFrame frame, VkImage target, VkImageView view, VkFormat format,
        int width, int height, bool encodeLinear, bool presenting, bool capture, VkSemaphore wait, VkSemaphore signal)
    {
        var api = _gpu.DeviceApi;
        VkCommandBuffer cmd = slot.CommandBuffer;
        api.vkResetCommandPool(slot.CommandPool, VkCommandPoolResetFlags.None).Check("vkResetCommandPool");
        var begin = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        api.vkBeginCommandBuffer(cmd, &begin).Check("vkBeginCommandBuffer");

        _classic.RecordUploads(cmd, slot.Index, frame.Classic, frame.Text, Statistics);

        PresentationRect rect = PresentationLayout.Compute(width, height, Settings.Aspect, Settings.IntegerScaling);
        int spriteCount = 0;
        if (frame.Space is { } space && space.Sprites.Count > 0 && !rect.IsEmpty)
        {
            _sprites ??= new SpritePass(_gpu, _classic, _log, FramesInFlight);
            _sprites.EnsurePipeline(format);
            spriteCount = _sprites.Prepare(cmd, slot.Index, space, rect, Settings.Filter, encodeLinear, Statistics);
        }

        bool text = false;
        OverlayDrawList overlay = _keyHelpLayout.Update(frame.KeyHelp, width, height, rect);
        TextLayer? glyphs = frame.Text is { Count: > 0 } layer && !rect.IsEmpty ? layer : null;
        if (glyphs is not null || overlay.Count > 0)
        {
            _text ??= new TextPass(_gpu, _classic, _log, FramesInFlight);
            _text.EnsurePipeline(format);
            text = _text.Prepare(cmd, slot.Index, glyphs, overlay, rect, width, height, encodeLinear, Statistics);
        }

        // Target -> colour attachment. Old contents are discarded (cleared below). Swapchain: the
        // acquire semaphore is waited at COLOR_ATTACHMENT_OUTPUT, which this barrier chains to.
        // Offscreen: orders after the previous frame's writes and copies of the same image.
        var toAttachment = new VkImageMemoryBarrier2
        {
            srcStageMask = presenting ? VkPipelineStageFlags2.ColorAttachmentOutput : VkPipelineStageFlags2.ColorAttachmentOutput | VkPipelineStageFlags2.Copy,
            srcAccessMask = presenting ? VkAccessFlags2.None : VkAccessFlags2.ColorAttachmentWrite,
            dstStageMask = VkPipelineStageFlags2.ColorAttachmentOutput,
            dstAccessMask = VkAccessFlags2.ColorAttachmentWrite,
            oldLayout = VkImageLayout.Undefined,
            newLayout = VkImageLayout.ColorAttachmentOptimal,
            srcQueueFamilyIndex = QueueFamilyIgnored,
            dstQueueFamilyIndex = QueueFamilyIgnored,
            image = target,
            subresourceRange = GpuContext.ColorRange,
        };
        ImageBarrier(cmd, &toAttachment);

        var colorAttachment = new VkRenderingAttachmentInfo
        {
            imageView = view,
            imageLayout = VkImageLayout.ColorAttachmentOptimal,
            loadOp = VkAttachmentLoadOp.Clear, // black letterbox bars
            storeOp = VkAttachmentStoreOp.Store,
            clearValue = new VkClearValue { color = new VkClearColorValue(0f, 0f, 0f, 1f) },
        };
        var renderingInfo = new VkRenderingInfo
        {
            renderArea = new VkRect2D { offset = default, extent = new VkExtent2D { width = (uint)width, height = (uint)height } },
            layerCount = 1,
            colorAttachmentCount = 1,
            pColorAttachments = &colorAttachment,
        };
        _gpu.CmdBeginRendering(cmd, &renderingInfo);
        _classic.RecordDraw(cmd, rect, Settings.Filter, encodeLinear);
        if (spriteCount > 0)
            _sprites!.RecordDraw(cmd, slot.Index, rect);
        if (text)
            _text!.RecordDraw(cmd, slot.Index, rect, width, height); // game text, then the key help over everything
        _gpu.CmdEndRendering(cmd);

        if (capture)
            RecordCapture(cmd, slot, target, format, width, height); // leaves the image in TRANSFER_SRC_OPTIMAL

        if (presenting)
        {
            // -> PRESENT_SRC. dstStage ALL_COMMANDS chains to the semaphore signal (also ALL_COMMANDS),
            // so the transition completes before the presentation engine waits on the semaphore.
            var toPresent = new VkImageMemoryBarrier2
            {
                srcStageMask = capture ? VkPipelineStageFlags2.Copy : VkPipelineStageFlags2.ColorAttachmentOutput,
                srcAccessMask = capture ? VkAccessFlags2.None : VkAccessFlags2.ColorAttachmentWrite,
                dstStageMask = VkPipelineStageFlags2.AllCommands,
                dstAccessMask = VkAccessFlags2.None,
                oldLayout = capture ? VkImageLayout.TransferSrcOptimal : VkImageLayout.ColorAttachmentOptimal,
                newLayout = VkImageLayout.PresentSrcKHR,
                srcQueueFamilyIndex = QueueFamilyIgnored,
                dstQueueFamilyIndex = QueueFamilyIgnored,
                image = target,
                subresourceRange = GpuContext.ColorRange,
            };
            ImageBarrier(cmd, &toPresent);
        }
        else if (!capture)
        {
            // Offscreen frames end in TRANSFER_SRC_OPTIMAL so CaptureLastFrame can copy at any time.
            var toTransfer = TransferSourceBarrier(target);
            ImageBarrier(cmd, &toTransfer);
        }

        api.vkEndCommandBuffer(cmd).Check("vkEndCommandBuffer");

        var commandInfo = new VkCommandBufferSubmitInfo { commandBuffer = cmd };
        var waitInfo = new VkSemaphoreSubmitInfo { semaphore = wait, stageMask = VkPipelineStageFlags2.ColorAttachmentOutput };
        var signalInfo = new VkSemaphoreSubmitInfo { semaphore = signal, stageMask = VkPipelineStageFlags2.AllCommands };
        var submit = new VkSubmitInfo2
        {
            waitSemaphoreInfoCount = wait.IsNull ? 0u : 1u,
            pWaitSemaphoreInfos = &waitInfo,
            commandBufferInfoCount = 1,
            pCommandBufferInfos = &commandInfo,
            signalSemaphoreInfoCount = signal.IsNull ? 0u : 1u,
            pSignalSemaphoreInfos = &signalInfo,
        };
        VkFence fence = slot.Fence;
        api.vkResetFences(1, &fence).Check("vkResetFences");
        _gpu.QueueSubmit2(_gpu.Queue, 1, &submit, fence).Check("vkQueueSubmit2");

        slot.Submitted = true;
        slot.FrameNumber = _frameNumber;
        _frameNumber++;
        Statistics.FramesRendered++;
    }

    private void RecordCapture(VkCommandBuffer cmd, FrameSlot slot, VkImage target, VkFormat format, int width, int height)
    {
        EnsureBuffer(ref slot.Readback, (ulong)width * (ulong)height * Readback.BytesPerPixel, $"capture slot {slot.Index}");
        var toTransfer = TransferSourceBarrier(target);
        ImageBarrier(cmd, &toTransfer);
        RecordImageToBuffer(cmd, target, slot.Readback, width, height);
        slot.CapturePending = true;
        slot.CaptureWidth = width;
        slot.CaptureHeight = height;
        slot.CaptureFormat = format;
        _captureRequested = false;
    }

    /// <summary>Copies a TRANSFER_SRC_OPTIMAL image into a buffer and makes the data visible to the host.</summary>
    private void RecordImageToBuffer(VkCommandBuffer cmd, VkImage image, GpuBuffer buffer, int width, int height)
    {
        var region = new VkBufferImageCopy
        {
            bufferOffset = 0,
            bufferRowLength = 0,
            bufferImageHeight = 0,
            imageSubresource = new VkImageSubresourceLayers { aspectMask = VkImageAspectFlags.Color, mipLevel = 0, baseArrayLayer = 0, layerCount = 1 },
            imageOffset = default,
            imageExtent = new VkExtent3D { width = (uint)width, height = (uint)height, depth = 1 },
        };
        _gpu.DeviceApi.vkCmdCopyImageToBuffer(cmd, image, VkImageLayout.TransferSrcOptimal, buffer.Buffer, 1, &region);

        // A fence wait alone does not make device writes visible to the host.
        var toHost = new VkBufferMemoryBarrier2
        {
            srcStageMask = VkPipelineStageFlags2.Copy,
            srcAccessMask = VkAccessFlags2.TransferWrite,
            dstStageMask = VkPipelineStageFlags2.Host,
            dstAccessMask = VkAccessFlags2.HostRead,
            srcQueueFamilyIndex = QueueFamilyIgnored,
            dstQueueFamilyIndex = QueueFamilyIgnored,
            buffer = buffer.Buffer,
            offset = 0,
            size = ulong.MaxValue,
        };
        var dependency = new VkDependencyInfo { bufferMemoryBarrierCount = 1, pBufferMemoryBarriers = &toHost };
        _gpu.CmdPipelineBarrier2(cmd, &dependency);
    }

    private static VkImageMemoryBarrier2 TransferSourceBarrier(VkImage image) => new()
    {
        srcStageMask = VkPipelineStageFlags2.ColorAttachmentOutput,
        srcAccessMask = VkAccessFlags2.ColorAttachmentWrite,
        dstStageMask = VkPipelineStageFlags2.Copy,
        dstAccessMask = VkAccessFlags2.TransferRead,
        oldLayout = VkImageLayout.ColorAttachmentOptimal,
        newLayout = VkImageLayout.TransferSrcOptimal,
        srcQueueFamilyIndex = QueueFamilyIgnored,
        dstQueueFamilyIndex = QueueFamilyIgnored,
        image = image,
        subresourceRange = GpuContext.ColorRange,
    };

    private void ImageBarrier(VkCommandBuffer cmd, VkImageMemoryBarrier2* barrier)
    {
        var dependency = new VkDependencyInfo { imageMemoryBarrierCount = 1, pImageMemoryBarriers = barrier };
        _gpu.CmdPipelineBarrier2(cmd, &dependency);
    }

    /// <summary>Waits for the slot's previous submission (if any) and collects its capture.</summary>
    private void WaitForSlot(FrameSlot slot)
    {
        if (!slot.Submitted)
            return;
        WaitFence(slot.Fence, "frame");
        slot.Submitted = false;
        if (slot.CapturePending)
            CompleteCapture(slot);
    }

    private void TryCompleteSlot(FrameSlot? slot, bool wait)
    {
        if (slot is null || !slot.CapturePending)
            return;
        if (wait)
        {
            WaitForSlot(slot);
        }
        else if (_gpu.DeviceApi.vkGetFenceStatus(slot.Fence) == VkResult.Success)
        {
            slot.Submitted = false;
            CompleteCapture(slot);
        }
    }

    private void CompleteCapture(FrameSlot slot)
    {
        byte[] rgba = Readback.ToRgba(slot.Readback.Mapped, slot.CaptureWidth, slot.CaptureHeight, slot.CaptureFormat);
        _completedCaptures.Enqueue(new CapturedImage(slot.CaptureWidth, slot.CaptureHeight, rgba, slot.FrameNumber));
        slot.CapturePending = false;
        Statistics.CapturesCompleted++;
    }

    private void WaitFence(VkFence fence, string what)
    {
        VkResult result = _gpu.DeviceApi.vkWaitForFences(1, &fence, true, FenceTimeoutNs);
        if (result == VkResult.Timeout)
            throw new VulkanRendererException($"The GPU did not finish a {what} within 5 seconds", (int)result, VkNames.Result(result));
        result.Check("vkWaitForFences");
    }

    private bool RecreateSwapchain((int Width, int Height) drawable)
    {
        // Resizes are rare: idle the queue instead of tracking which old images are still in use.
        _gpu.WaitIdle();
        Swapchain? previous = _swapchain;
        Swapchain? next = Swapchain.Create(_gpu, drawable, Settings.VSync, previous, _log);
        if (next is null)
        {
            _swapchainDirty = true; // surface has no area (minimised); keep the old swapchain and retry next frame
            return false;
        }
        if (previous is not null)
        {
            previous.Dispose();
            Statistics.TargetRecreations++;
        }
        _swapchain = next;
        _swapchainDirty = false;
        _classic.EnsurePipeline(next.Format);
        _sprites?.EnsurePipeline(next.Format);
        _text?.EnsurePipeline(next.Format);
        if (_captureRequested && !next.SupportsCapture)
        {
            _log.Warning("The new swapchain cannot be read back; the pending capture request is dropped.");
            _captureRequested = false;
        }
        return true;
    }

    private void ResizeOffscreenTarget(int width, int height, bool countAsRecreation)
    {
        uint limit = _gpu.Capabilities.MaxImageDimension2D;
        if ((uint)width > limit || (uint)height > limit)
            throw new ArgumentOutOfRangeException(nameof(width), $"{width}x{height} exceeds the device limit of {limit} pixels.");
        foreach (var slot in _slots)
            WaitForSlot(slot);
        _gpu.Destroy(ref _offscreenTarget);
        _offscreenWidth = width;
        _offscreenHeight = height;
        _offscreenHasContent = false;
        if (width > 0 && height > 0)
        {
            _offscreenTarget = _gpu.CreateImage((uint)width, (uint)height, _offscreenFormat,
                VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.TransferSrc, $"offscreen target {width}x{height}");
        }
        if (countAsRecreation)
            Statistics.TargetRecreations++;
    }

    private void EnsureBuffer(ref GpuBuffer buffer, ulong size, string name)
    {
        if (buffer.IsNull || buffer.Size < size)
        {
            _gpu.Destroy(ref buffer);
            buffer = _gpu.CreateBuffer(size, VkBufferUsageFlags.TransferDst, hostVisible: true, preferHostCached: true, name);
        }
    }

    private void EnsureOneShotResources()
    {
        if (_oneShotPool.IsNotNull)
            return;
        var api = _gpu.DeviceApi;
        var poolInfo = new VkCommandPoolCreateInfo { flags = VkCommandPoolCreateFlags.Transient, queueFamilyIndex = _gpu.QueueFamily };
        VkCommandPool pool;
        api.vkCreateCommandPool(&poolInfo, null, &pool).Check("vkCreateCommandPool");
        _oneShotPool = pool;
        var allocateInfo = new VkCommandBufferAllocateInfo { commandPool = pool, level = VkCommandBufferLevel.Primary, commandBufferCount = 1 };
        VkCommandBuffer buffer;
        api.vkAllocateCommandBuffers(&allocateInfo, &buffer).Check("vkAllocateCommandBuffers");
        _oneShotBuffer = buffer;
        var fenceInfo = new VkFenceCreateInfo();
        VkFence fence;
        api.vkCreateFence(&fenceInfo, null, &fence).Check("vkCreateFence");
        _oneShotFence = fence;
    }

    private void ReleaseResources()
    {
        if (_gpu.DeviceApi is not null)
        {
            foreach (var slot in _slots)
                slot?.Destroy(_gpu);
            _sprites?.Dispose(); // uses the classic pass's images and sampler
            _sprites = null;
            _text?.Dispose();    // uses the classic pass's palette and sampler
            _text = null;
            _classic?.Dispose();
            _swapchain?.Dispose();
            _swapchain = null;
            _gpu.Destroy(ref _offscreenTarget);
            _gpu.Destroy(ref _oneShotReadback);
            if (_oneShotPool.IsNotNull)
                _gpu.DeviceApi.vkDestroyCommandPool(_oneShotPool);
            if (_oneShotFence.IsNotNull)
                _gpu.DeviceApi.vkDestroyFence(_oneShotFence);
            _oneShotPool = VkCommandPool.Null;
            _oneShotFence = VkFence.Null;
        }
        _completedCaptures.Clear();
        _gpu.Dispose();
    }
}
