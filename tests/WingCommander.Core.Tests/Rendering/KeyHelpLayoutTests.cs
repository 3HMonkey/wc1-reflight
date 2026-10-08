using WingCommander.Core.Rendering;

namespace WingCommander.Core.Tests.Rendering;

public sealed class KeyHelpLayoutTests
{
    /// <summary>A font of 6x8 cells (advance 6) whose glyphs all have foreground, space excepted.</summary>
    private static KeyHelpOverlay Overlay()
    {
        var cache = new GlyphImageCache();
        for (int c = 32; c < 127; c++)
        {
            var texels = new byte[(6 * 8 + 8) * (8 * 8 + 8) * GlyphImage.BytesPerTexel];
            cache.Set(new GlyphKey(1, (byte)c), new GlyphImage(6, 8, 6, 15, hasForeground: c != ' ', multicolour: false, texels));
        }
        return new KeyHelpOverlay(cache)
        {
            Title = "CONTROLS",
            Sections =
            [
                new KeyHelpSection("FLIGHT", [new("Arrows", "Steer"), new("Tab", "Afterburner")]),
                new KeyHelpSection("WEAPONS", [new("Space", "Guns"), new("Enter", "Missile")]),
            ],
            Visible = true,
        };
    }

    [Fact]
    public void WideTarget_UsesTheMargins_AndLeavesThePictureFree()
    {
        var list = new OverlayDrawList();
        var picture = PresentationLayout.Compute(1920, 1080, AspectMode.FourByThree, false);
        Assert.Equal(KeyHelpPlacement.Margins, KeyHelpLayout.Build(Overlay(), 1920, 1080, picture, list));
        Assert.True(list.Count > 0);
        foreach (OverlayItem item in list.Items)
        {
            bool left = item.X + item.Width <= picture.X;
            bool right = item.X >= picture.X + picture.Width;
            Assert.True(left || right, $"item at {item.X} overlaps the picture");
        }
    }

    [Fact]
    public void FourByThreeTarget_DrawsAPanelOverThePicture()
    {
        var list = new OverlayDrawList();
        var picture = PresentationLayout.Compute(1280, 960, AspectMode.FourByThree, false);
        Assert.Equal(KeyHelpPlacement.Panel, KeyHelpLayout.Build(Overlay(), 1280, 960, picture, list));
        Assert.Equal(OverlayItemKind.Rectangle, list.Items[0].Kind);
        Assert.Equal(KeyHelpLayout.PanelColour, list.Items[0].Colour);
    }

    [Fact]
    public void HiddenOverlay_DrawsNothing()
    {
        var overlay = Overlay();
        overlay.Visible = false;
        var list = new OverlayDrawList();
        list.AddRectangle(0, 0, 1, 1, 0);
        Assert.Equal(KeyHelpPlacement.None, KeyHelpLayout.Build(overlay, 1920, 1080,
            PresentationLayout.Compute(1920, 1080, AspectMode.FourByThree, false), list));
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void ChangingTheFont_ChangesTheVersion()
    {
        var overlay = Overlay();
        int version = overlay.Version;
        overlay.Font = 2;
        Assert.NotEqual(version, overlay.Version);
    }
}
