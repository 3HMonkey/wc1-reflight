using WingCommander.Core.Rendering;

namespace WingCommander.Render.Vulkan.Internal;

/// <summary>
/// The renderer-owned <see cref="OverlayDrawList"/> of the key help (ADR-013): laid out with
/// <see cref="KeyHelpLayout.Build"/> for the current target, again only when an input of the
/// layout changed - the overlay's content and visibility (<see cref="KeyHelpOverlay.Version"/>),
/// its font, the glyph images (count and generation), the target size or the picture rectangle.
/// The result is the list a per-frame build would give, without the build's allocations.
/// </summary>
internal sealed class KeyHelpLayoutCache
{
    private readonly OverlayDrawList _list = new();
    private LayoutInputs _inputs;
    private bool _valid;

    /// <summary>The items to draw over everything this frame (empty without a visible key help).</summary>
    public OverlayDrawList Update(KeyHelpOverlay? overlay, int targetWidth, int targetHeight, PresentationRect picture)
    {
        if (overlay is null)
        {
            _list.Clear();
            _valid = false;
            return _list;
        }
        var inputs = new LayoutInputs(overlay, overlay.Version, overlay.Font, targetWidth, targetHeight, picture,
            overlay.Glyphs.Count, overlay.Glyphs.Generation);
        if (!_valid || inputs != _inputs)
        {
            KeyHelpLayout.Build(overlay, targetWidth, targetHeight, picture, _list);
            _inputs = inputs;
            _valid = true;
        }
        return _list;
    }

    /// <summary>Everything <see cref="KeyHelpLayout.Build"/> reads (the overlay by reference).</summary>
    private readonly record struct LayoutInputs(KeyHelpOverlay Overlay, int Version, byte Font, int TargetWidth, int TargetHeight,
        PresentationRect Picture, int GlyphCount, int GlyphGeneration);
}
