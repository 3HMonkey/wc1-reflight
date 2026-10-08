using System.Runtime.CompilerServices;
using WingCommander.Core.Rendering;
using WingCommander.Render.Vulkan.Internal;

namespace WingCommander.Render.Vulkan.Tests;

/// <summary>The CPU side of the text pass (no GPU): record layouts, the key help layout cache, the test glyphs.</summary>
public sealed class TextContractTests
{
    [Fact]
    public void InstanceAndPushConstantRecords_HaveTheShaderLayout()
    {
        Assert.Equal(48, Unsafe.SizeOf<TextInstanceData>());   // text.vert: 3 x 16-byte attributes
        Assert.Equal(32, Unsafe.SizeOf<TextPushConstants>());  // TextParams: vec2 x 3, uint x 2
    }

    [Fact]
    public void KeyHelpLayoutCache_GivesTheLayout_AndRebuildsOnlyWhenAnInputChanges()
    {
        var glyphs = new GlyphImageCache();
        for (int c = 32; c < 127; c++)
            glyphs.Set(new GlyphKey(1, (byte)c), SyntheticGlyphs.FontGlyph((byte)c));
        var overlay = new KeyHelpOverlay(glyphs)
        {
            Title = "Keys",
            Sections =
            [
                new KeyHelpSection("Flight", [new("A", "Afterburner"), new("Tab", "Throttle")]),
                new KeyHelpSection("Weapons", [new("Space", "Fire guns")]),
            ],
            Visible = true,
        };
        var cache = new KeyHelpLayoutCache();
        PresentationRect wide = PresentationLayout.Compute(1920, 1080, AspectMode.FourByThree, false);
        PresentationRect full = PresentationLayout.Compute(1280, 960, AspectMode.FourByThree, false);

        OverlayDrawList list = cache.Update(overlay, 1920, 1080, wide);
        Assert.Equal(Layout(overlay, 1920, 1080, wide), list.Items.ToArray());
        Assert.Same(glyphs, list.Glyphs);

        cache.Update(overlay, 1920, 1080, wide);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            cache.Update(overlay, 1920, 1080, wide);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); // a rebuild would allocate

        Assert.Equal(Layout(overlay, 1280, 960, full), cache.Update(overlay, 1280, 960, full).Items.ToArray());
        overlay.Visible = false;
        Assert.Equal(0, cache.Update(overlay, 1280, 960, full).Count);
        overlay.Visible = true;
        overlay.Sections = [new KeyHelpSection("Flight", [new("A", "Afterburner")])];
        Assert.Equal(Layout(overlay, 1280, 960, full), cache.Update(overlay, 1280, 960, full).Items.ToArray());
        overlay.Font = 2; // no glyphs in that font: nothing to draw (the font does not change the version)
        Assert.Equal(Layout(overlay, 1280, 960, full), cache.Update(overlay, 1280, 960, full).Items.ToArray());
        Assert.Equal(0, cache.Update(null, 1280, 960, full).Count);
    }

    [Fact]
    public void SyntheticGlyphs_FollowTheGlyphImageEncoding()
    {
        GlyphImage block = SyntheticGlyphs.Build(TextScene.Block);
        Assert.Equal((4, 6, 40, 56), (block.Width, block.Height, block.FieldWidth, block.FieldHeight));
        Assert.True(block.HasForeground);
        Assert.False(block.Multicolour);
        Assert.Equal(1f, GlyphRasterizer.Coverage(block, 2f, 3f, 2f));
        Assert.Equal(0f, GlyphRasterizer.Coverage(block, -0.9f, 3f, 2f));
        Assert.InRange(GlyphRasterizer.Coverage(block, 0f, 3f, 2f), 0.45f, 0.55f); // on the outline

        GlyphImage chalk = SyntheticGlyphs.Build(TextScene.Chalk, TextScene.ChalkColours);
        Assert.True(chalk.Multicolour);
        Assert.Equal(SyntheticGlyphs.Ink, GlyphRasterizer.SourceColour(chalk, 0, 0));
        Assert.Equal(100, GlyphRasterizer.SourceColour(chalk, 2, 0));
        Assert.Equal(150, GlyphRasterizer.SourceColour(chalk, 3, 0));

        Assert.False(SyntheticGlyphs.FontGlyph((byte)' ').HasForeground);
        Assert.True(SyntheticGlyphs.Ring(30, 30, 7).HasForeground);
    }

    private static OverlayItem[] Layout(KeyHelpOverlay overlay, int width, int height, PresentationRect picture)
    {
        var list = new OverlayDrawList();
        KeyHelpLayout.Build(overlay, width, height, picture, list);
        return list.Items.ToArray();
    }
}
