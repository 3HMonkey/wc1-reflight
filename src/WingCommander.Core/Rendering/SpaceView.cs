namespace WingCommander.Core.Rendering;

/// <summary>
/// The space window of one displayed frame for renderers that draw it at output resolution
/// (ADR-010 R2+): screen-space sprites in painter order, the decoded images they use, the cockpit
/// window mask, and the 3D snapshots for interpolation and meshes. Sprites only replace classic
/// pixels that show <see cref="BackgroundIndex"/> inside <see cref="WindowMask"/>, so cockpit art,
/// HUD and cursor (all in the classic layer) stay on top. Renderers without R2 ignore it.
/// </summary>
public sealed class SpaceView
{
    /// <summary>The space buffer's clear colour (cPrimaryViewBufferColour).</summary>
    public const byte DefaultBackgroundIndex = 0xBF;

    public SpaceView(SpriteImageCache images)
    {
        ArgumentNullException.ThrowIfNull(images);
        Images = images;
    }

    /// <summary>Screen-space sprites of this frame, farthest first.</summary>
    public SpriteDrawList Sprites { get; } = new();

    /// <summary>Decoded frames the sprites refer to (long-lived, shared across frames).</summary>
    public SpriteImageCache Images { get; set; }

    /// <summary>Cockpit window; null = the whole screen is space.</summary>
    public SpaceViewMask? WindowMask { get; set; }

    /// <summary>Classic palette index that sprites may replace (0xBF).</summary>
    public byte BackgroundIndex { get; set; } = DefaultBackgroundIndex;

    /// <summary>3D state of the latest 20 Hz tick (interpolation, meshes); optional for R2.</summary>
    public SpaceViewState? Current { get; set; }

    /// <summary>3D state of the tick before <see cref="Current"/>.</summary>
    public SpaceViewState? Previous { get; set; }
}
