namespace WingCommander.Render.Vulkan;

/// <summary>Running counters of a <see cref="VulkanRenderer"/> (diagnostics and tests).</summary>
public sealed class VulkanRendererStatistics
{
    /// <summary>Frames submitted to the GPU.</summary>
    public long FramesRendered { get; internal set; }

    /// <summary>Render calls that drew nothing (minimised window, zero-sized target, swapchain not ready).</summary>
    public long FramesSkipped { get; internal set; }

    /// <summary>
    /// Uploads of the 320x200 index image (only when <c>ClassicLayer.PixelsVersion</c> changed, or
    /// <c>TextLayer.Version</c> while the frame carries text, or the frame switched between the two).
    /// </summary>
    public long PixelUploads { get; internal set; }

    /// <summary>Uploads of the 256-entry palette (only when <c>Palette.Version</c> changed).</summary>
    public long PaletteUploads { get; internal set; }

    /// <summary>Space sprites drawn (R2), summed over all frames.</summary>
    public long SpritesDrawn { get; internal set; }

    /// <summary>Sprite images uploaded into the atlas (each image once until the atlas is reset).</summary>
    public long SpriteUploads { get; internal set; }

    /// <summary>Sprite atlas resets (image cache generation changed, or the atlas was full).</summary>
    public long AtlasResets { get; internal set; }

    /// <summary>Uploads of the cockpit window mask (only when the mask object or its version changed).</summary>
    public long WindowMaskUploads { get; internal set; }

    /// <summary>Glyphs of the game text drawn at output resolution (ADR-013), summed over all frames.</summary>
    public long GlyphsDrawn { get; internal set; }

    /// <summary>Glyph images uploaded into the glyph atlas (each image once until the atlas is reset).</summary>
    public long GlyphUploads { get; internal set; }

    /// <summary>Glyph atlas resets (a glyph cache was replaced or changed generation, or the atlas was full).</summary>
    public long TextAtlasResets { get; internal set; }

    /// <summary>Uploads of the 320x200 text mask (only when the text layer object or its version changed).</summary>
    public long TextMaskUploads { get; internal set; }

    /// <summary>Key help overlay items (rectangles and glyphs) drawn, summed over all frames.</summary>
    public long OverlayItemsDrawn { get; internal set; }

    /// <summary>Swapchain or offscreen target re-creations after the first creation.</summary>
    public long TargetRecreations { get; internal set; }

    /// <summary>Captures read back to the CPU.</summary>
    public long CapturesCompleted { get; internal set; }

    /// <summary>Validation layer errors reported through the debug messenger (0 when validation is off).</summary>
    public long ValidationErrors { get; internal set; }

    /// <summary>Validation layer warnings reported through the debug messenger.</summary>
    public long ValidationWarnings { get; internal set; }

    public override string ToString() =>
        $"frames {FramesRendered}, skipped {FramesSkipped}, pixel uploads {PixelUploads}, palette uploads {PaletteUploads}, " +
        $"sprites drawn {SpritesDrawn}, sprite uploads {SpriteUploads}, atlas resets {AtlasResets}, " +
        $"glyphs drawn {GlyphsDrawn}, glyph uploads {GlyphUploads}, text atlas resets {TextAtlasResets}, " +
        $"text mask uploads {TextMaskUploads}, overlay items {OverlayItemsDrawn}, " +
        $"target recreations {TargetRecreations}, captures {CapturesCompleted}, validation errors {ValidationErrors}, warnings {ValidationWarnings}";
}
