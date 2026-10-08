using System.Numerics;
using Vortice.Vulkan;
using WingCommander.Core.Rendering;
using WingCommander.Core.Video;

namespace WingCommander.Render.Vulkan.Tests;

/// <summary>
/// Output-resolution text (ADR-013): the text layer's pixels replace the classic pixels, glyphs
/// match the CPU reference (<see cref="GlyphRasterizer"/>) at several output scales, the mask rule
/// and painter order, multicolour glyphs, palette changes, the glyph atlas, frames in flight, the
/// key help overlay in target pixels, sprites under text, sRGB targets and allocations.
/// </summary>
[Collection("Vulkan")]
public sealed class TextTests(OffscreenRendererFixture fixture) : VulkanTestBase(fixture)
{
    /// <summary>Per channel, 0..255: blending is quantized after every draw, hardware filtering has finite precision.</summary>
    private const int Tolerance = 3;

    // 960x600 square pixels = exactly 3 output pixels per logical pixel.
    private static readonly PresentationRect Square3x = new(0, 0, 960, 600);

    private VulkanRenderer Square() => Fixture.Get(960, 600, ScalingFilter.Nearest, AspectMode.SquarePixels);

    private static CapturedImage Render(VulkanRenderer renderer, RenderFrame frame)
    {
        renderer.Render(frame);
        return renderer.CaptureLastFrame();
    }

    private static int At3x(CapturedImage image, int x, int y) => image.GetRgb(3 * x + 1, 3 * y + 1);

    private static void AssertMatchesReference(TextScene scene, CapturedImage image, PresentationRect rect, int minimumTextPixels,
        bool linear = false, double maxAmbiguousFraction = 0.1)
    {
        var (mismatches, ambiguous, changed, first) = TextReference.Compare(image,
            (x, y) => TextReference.GameText(scene.Text, scene.Palette, rect, x, y, linear), Tolerance,
            (x, y) => TextReference.Classic(scene.Text.Pixels, scene.Palette, rect, x, y));
        Assert.True(mismatches == 0, $"{mismatches} mismatches ({ambiguous} ambiguous, {changed} text pixels); first: {first}");
        Assert.True(ambiguous <= maxAmbiguousFraction * image.Width * image.Height, $"{ambiguous} ambiguous pixels");
        Assert.True(changed >= minimumTextPixels, $"only {changed} output pixels show text");
    }

    /// <summary>Letters, a space, glyphs partly off screen and an overlapping pair; mask 1 over every cell.</summary>
    private static TextScene LetterScene()
    {
        var scene = new TextScene();
        GlyphKey a = scene.Set('A', TextScene.LetterA);
        GlyphKey g = scene.Set('g', TextScene.LetterG);
        GlyphKey slash = scene.Set('/', TextScene.Slash);
        GlyphKey space = scene.Set(' ', SyntheticGlyphs.Build(["....", "....", "....", "....", "....", "....", "....", "...."]));
        int x = 40;
        foreach (GlyphKey key in new[] { a, g, slash, space, a, slash, g })
        {
            scene.Add(x, 60, key, (byte)(30 + x));
            x += 6;
        }
        scene.Add(-2, 10, a, 200);   // partly off screen (left)
        scene.Add(317, 194, g, 201); // partly off screen (bottom right)
        scene.Add(100, 100, a, 77);  // overlapping pair: the later one is on top
        scene.Add(102, 101, slash, 78);
        scene.AllowAllCells();
        scene.Publish();
        return scene;
    }

    private static KeyHelpOverlay KeyHelp(GlyphImageCache glyphs)
    {
        for (int c = 32; c < 127; c++)
            glyphs.Set(new GlyphKey(1, (byte)c), SyntheticGlyphs.FontGlyph((byte)c));
        return new KeyHelpOverlay(glyphs)
        {
            Title = "Flight keys",
            Sections =
            [
                new KeyHelpSection("Flight", [new("A", "Afterburner"), new("Tab", "Throttle up"), new("C", "Communicate")]),
                new KeyHelpSection("Weapons", [new("Space", "Fire guns"), new("Enter", "Fire missile"), new("W", "Select weapon")]),
            ],
            Visible = true,
        };
    }

    [VulkanFact]
    public void ClassicPixels_ComeFromTheTextLayerWhileItIsSet_AndSwitchingReuploads()
    {
        var renderer = Square();
        var scene = new TextScene(); // no glyphs: only the pixel source matters here
        scene.Publish();
        int ClassicColour(int x, int y) => TestPatterns.Rgb(scene.Palette, scene.Layer.Pixels[x, y]);
        int TextColour(int x, int y) => TestPatterns.Rgb(scene.Palette, scene.Text.Pixels[x, y]);
        long uploads = renderer.Statistics.PixelUploads;

        CapturedImage withText = Render(renderer, scene.Frame);
        Assert.Equal(0, TestPatterns.CompareAtPixelCentres(withText, Square3x, TextColour).Mismatches);
        scene.Frame.Text = null;
        CapturedImage classic = Render(renderer, scene.Frame);
        Assert.Equal(0, TestPatterns.CompareAtPixelCentres(classic, Square3x, ClassicColour).Mismatches);
        scene.Frame.Text = scene.Text; // same text version as two frames ago: still a new upload
        CapturedImage again = Render(renderer, scene.Frame);
        Assert.Equal(withText.Rgba, again.Rgba);
        Assert.Equal(uploads + 3, renderer.Statistics.PixelUploads);

        renderer.Render(scene.Frame);
        scene.Layer.MarkPixelsChanged(); // the classic layer is not shown while text is set
        renderer.Render(scene.Frame);
        Assert.Equal(uploads + 3, renderer.Statistics.PixelUploads);

        scene.Text.Pixels[5, 7] = 99;
        scene.Publish();
        CapturedImage changed = Render(renderer, scene.Frame);
        Assert.Equal(uploads + 4, renderer.Statistics.PixelUploads);
        Assert.Equal(TestPatterns.Rgb(scene.Palette, 99), At3x(changed, 5, 7));
    }

    [VulkanTheory]
    [InlineData(960, 600, AspectMode.SquarePixels)]  // 3x
    [InlineData(1280, 960, AspectMode.FourByThree)]  // 4 x 4.8: output pixels per source pixel differ per axis
    [InlineData(1000, 625, AspectMode.SquarePixels)] // 3.125x: some pixel centres lie on cell edges
    [InlineData(640, 480, AspectMode.FourByThree)]   // 2 x 2.4: wide anti-aliasing ramp
    [InlineData(1600, 1200, AspectMode.FourByThree)] // 5 x 6
    [InlineData(1366, 768, AspectMode.FourByThree)]  // pillarbox
    public void Glyphs_MatchTheCpuReference(int width, int height, AspectMode aspect)
    {
        var renderer = Fixture.Get(width, height, ScalingFilter.Nearest, aspect);
        TextScene scene = LetterScene();
        long drawn = renderer.Statistics.GlyphsDrawn;
        CapturedImage image = Render(renderer, scene.Frame);

        var rect = PresentationLayout.Compute(width, height, aspect, false);
        AssertMatchesReference(scene, image, rect, minimumTextPixels: 600);
        Assert.Equal(drawn + 10, renderer.Statistics.GlyphsDrawn); // 11 instances, the space has no foreground
    }

    [VulkanTheory]
    [InlineData(960, 600, AspectMode.SquarePixels)]
    [InlineData(1600, 1200, AspectMode.FourByThree)]
    public void ScaledGlyphs_MatchTheCpuReference(int width, int height, AspectMode aspect)
    {
        // Text the game copied smaller or larger (the briefing board): fractional cells and scales.
        var renderer = Fixture.Get(width, height, ScalingFilter.Nearest, aspect);
        var scene = new TextScene();
        GlyphKey a = scene.Set('A', TextScene.LetterA);
        GlyphKey g = scene.Set('g', TextScene.LetterG);
        float x = 30.4f;
        foreach (GlyphKey key in new[] { a, g, a, g })
        {
            scene.Text.Add(new GlyphInstance(x, 50.3f, key, 90, 154f / 260, 88f / 156));
            x += 5 * 154f / 260;
        }
        scene.Text.Add(new GlyphInstance(120.5f, 80.25f, a, 91, 1.5f, 1.25f));
        scene.Text.Add(new GlyphInstance(200f, 120f, g, 92, 0.5f, 0.5f));
        scene.AllowAllCells();
        scene.Publish();
        CapturedImage image = Render(renderer, scene.Frame);

        var rect = PresentationLayout.Compute(width, height, aspect, false);
        AssertMatchesReference(scene, image, rect, minimumTextPixels: 300);
    }

    [VulkanFact]
    public void Mask_DecidesWhichInstancesMayDrawWhere_InPainterOrder()
    {
        var renderer = Square();
        var scene = new TextScene();
        GlyphKey block = scene.Set('B', TextScene.Block); // 4x6, solid
        scene.Add(100, 50, block, 20);                    // instance 0
        scene.Add(102, 50, block, 40);                    // instance 1, overlaps columns 102..103
        scene.Add(200, 100, block, 60);                   // instance 2
        scene.SetMask(new ScreenRect(100, 50, 6, 3), 1);  // everyone may draw: instance 1 over instance 0
        scene.SetMask(new ScreenRect(100, 53, 6, 3), 2);  // drawn over after instance 0: only instances >= 1
        scene.SetMask(new ScreenRect(200, 100, 2, 6), 0); // covered by later drawing: no glyph
        scene.SetMask(new ScreenRect(202, 100, 2, 3), 3); // threshold = index 2 + 1: drawn
        scene.SetMask(new ScreenRect(202, 103, 2, 3), 4); // threshold above index 2: not drawn
        scene.Publish();
        CapturedImage image = Render(renderer, scene.Frame);

        AssertMatchesReference(scene, image, Square3x, minimumTextPixels: 200);
        int Colour(int index) => TestPatterns.Rgb(scene.Palette, index);
        int Background(int x, int y) => TestPatterns.Rgb(scene.Palette, scene.Text.Pixels[x, y]);
        Assert.Equal(Colour(20), At3x(image, 101, 51));
        Assert.Equal(Colour(40), At3x(image, 102, 51));
        Assert.Equal(Background(101, 54), At3x(image, 101, 54));
        Assert.Equal(Colour(40), At3x(image, 103, 54));
        Assert.Equal(Background(201, 102), At3x(image, 201, 102));
        Assert.Equal(Colour(60), At3x(image, 202, 101));
        Assert.Equal(Background(203, 104), At3x(image, 203, 104));

        // Mask 0 everywhere: nothing is drawn, the frame is exactly the text layer's pixels.
        Array.Clear(scene.Text.Mask);
        scene.Publish();
        CapturedImage none = Render(renderer, scene.Frame);
        Assert.Equal(0, TestPatterns.CompareAtPixelCentres(none, Square3x, Background).Mismatches);
        Assert.Equal(0, TestPatterns.NonPaletteColours(none, Square3x, scene.Palette));
    }

    [VulkanTheory]
    [InlineData(960, 600, AspectMode.SquarePixels)]
    [InlineData(1280, 960, AspectMode.FourByThree)]
    public void MulticolourGlyphs_BlendTheirSourceColoursBilinearly(int width, int height, AspectMode aspect)
    {
        var renderer = Fixture.Get(width, height, ScalingFilter.Nearest, aspect);
        var scene = new TextScene();
        GlyphKey chalk = scene.Set('C', TextScene.Chalk, TextScene.ChalkColours);
        Assert.True(scene.Glyphs.TryGet(chalk, out GlyphImage? image) && image.Multicolour);
        for (int i = 0; i < 6; i++)
            scene.Add(30 + i * 7, 40 + i * 3, chalk, (byte)(50 + i));
        scene.AllowAllCells();
        scene.Publish();
        CapturedImage captured = Render(renderer, scene.Frame);

        var rect = PresentationLayout.Compute(width, height, aspect, false);
        AssertMatchesReference(scene, captured, rect, minimumTextPixels: 600);
        if (aspect == AspectMode.SquarePixels)
        {
            Assert.Equal(TestPatterns.Rgb(scene.Palette, 50), At3x(captured, 30, 40));  // ink: the text colour
            Assert.Equal(TestPatterns.Rgb(scene.Palette, 100), At3x(captured, 32, 40)); // fixed colour 'a'
            Assert.Equal(TestPatterns.Rgb(scene.Palette, 150), At3x(captured, 33, 40)); // fixed colour 'b'
        }
    }

    [VulkanFact]
    public void PaletteChange_RecoloursTheText_WithoutUploadingGlyphsOrMask()
    {
        var renderer = Square();
        var scene = new TextScene();
        GlyphKey block = scene.Set('B', TextScene.Block);
        GlyphKey a = scene.Set('A', TextScene.LetterA);
        scene.Add(50, 50, block, 123);
        scene.Add(60, 50, a, 123);
        scene.AllowAllCells();
        scene.Publish();
        Render(renderer, scene.Frame);
        long glyphs = renderer.Statistics.GlyphUploads;
        long masks = renderer.Statistics.TextMaskUploads;
        long pixels = renderer.Statistics.PixelUploads;

        scene.Palette.SetEntry(123, 0xF0, 0x0D, 0x01); // a fade or flash: no new text version
        CapturedImage image = Render(renderer, scene.Frame);

        Assert.Equal(0xF00D01, At3x(image, 51, 52));
        Assert.Equal(glyphs, renderer.Statistics.GlyphUploads);
        Assert.Equal(masks, renderer.Statistics.TextMaskUploads);
        Assert.Equal(pixels, renderer.Statistics.PixelUploads);
        AssertMatchesReference(scene, image, Square3x, minimumTextPixels: 200);
    }

    [VulkanFact]
    public void TextMask_IsUploadedOnlyForANewTextVersion()
    {
        var renderer = Square();
        TextScene scene = LetterScene();
        long masks = renderer.Statistics.TextMaskUploads;
        renderer.Render(scene.Frame);
        renderer.Render(scene.Frame);
        Assert.Equal(masks + 1, renderer.Statistics.TextMaskUploads);

        scene.SetMask(new ScreenRect(40, 60, 12, 8), 0); // the first two letters are covered now
        scene.Publish();
        CapturedImage image = Render(renderer, scene.Frame);
        Assert.Equal(masks + 2, renderer.Statistics.TextMaskUploads);
        AssertMatchesReference(scene, image, Square3x, minimumTextPixels: 400);
    }

    [VulkanFact]
    public void ReplacedGlyph_ResetsTheAtlas_AndTheNewImageIsDrawn()
    {
        var renderer = Square();
        var scene = new TextScene();
        GlyphKey a = scene.Set('A', TextScene.LetterA);
        GlyphKey g = scene.Set('g', TextScene.LetterG);
        scene.Add(40, 40, a, 90);
        scene.Add(46, 40, g, 91);
        scene.Add(52, 40, a, 92);
        scene.AllowAllCells();
        scene.Publish();
        long uploads = renderer.Statistics.GlyphUploads;
        for (int i = 0; i < 3; i++)
            renderer.Render(scene.Frame);
        Assert.Equal(uploads + 2, renderer.Statistics.GlyphUploads); // each image once

        long resets = renderer.Statistics.TextAtlasResets;
        scene.Set('A', TextScene.Slash); // replaced: the cache generation changes
        CapturedImage image = Render(renderer, scene.Frame);

        Assert.Equal(resets + 1, renderer.Statistics.TextAtlasResets);
        Assert.Equal(uploads + 4, renderer.Statistics.GlyphUploads); // both images of this frame again
        AssertMatchesReference(scene, image, Square3x, minimumTextPixels: 200);
    }

    [VulkanFact]
    public void FullAtlas_IsReset_AndEveryFrameStillDrawsCorrectly()
    {
        var renderer = Square();
        var scene = new TextScene();
        long resets = renderer.Statistics.TextAtlasResets;
        // 3 frames x 40 different 30x30 glyphs (248x248 field texels, 64 fit into the atlas):
        // every frame fits, all of them together do not.
        for (int round = 0; round < 3; round++)
        {
            scene.Text.Clear();
            Array.Clear(scene.Text.Mask);
            for (int i = 0; i < 40; i++)
            {
                GlyphKey key = scene.Set((char)(round * 40 + i), SyntheticGlyphs.Ring(30, 30, round * 40 + i), font: 2);
                scene.Add(i % 8 * 40 + 2, i / 8 * 40 + 2, key, (byte)(i * 5 + round));
            }
            scene.AllowAllCells();
            scene.Publish();
            renderer.Render(scene.Frame);
        }
        CapturedImage image = renderer.CaptureLastFrame();

        Assert.True(renderer.Statistics.TextAtlasResets >= resets + 2, "the atlas should have been reset when it was full");
        AssertMatchesReference(scene, image, Square3x, minimumTextPixels: 100_000);
    }

    [VulkanFact]
    public void FrameWithMoreGlyphsThanTheAtlasHolds_DrawsWhatFits_AndWarnsOnce()
    {
        var warnings = new List<string>();
        using var renderer = VulkanRenderer.CreateOffscreen(640, 400,
            new RendererSettings { Filter = ScalingFilter.Nearest, Aspect = AspectMode.SquarePixels },
            new VulkanRendererOptions
            {
                Log = ValidationLog.Combine((level, message) =>
                {
                    if (level == VulkanLogLevel.Warning && message.Contains("glyph atlas", StringComparison.Ordinal))
                        warnings.Add(message);
                }),
                UnlimitedValidationMessages = true,
            });
        var scene = new TextScene();
        for (int i = 0; i < 80; i++)
        {
            GlyphKey key = scene.Set((char)i, SyntheticGlyphs.Ring(30, 30, i), font: 3);
            scene.Add(i % 10 * 32, i / 10 * 25, key, (byte)i);
        }
        scene.AllowAllCells();
        scene.Publish();

        renderer.Render(scene.Frame);
        renderer.Render(scene.Frame);
        renderer.WaitIdle();

        Assert.Single(warnings);
        Assert.Equal(2 * 64, renderer.Statistics.GlyphsDrawn); // 8 x 8 fields of 248 texels per frame
    }

    [VulkanTheory]
    [InlineData(1920, 1080, KeyHelpPlacement.Margins)]
    [InlineData(1280, 960, KeyHelpPlacement.Panel)]
    public void KeyHelp_IsDrawnInTargetPixels_AsLaidOut(int width, int height, KeyHelpPlacement placement)
    {
        var renderer = Fixture.Get(width, height, ScalingFilter.Nearest, AspectMode.FourByThree);
        var palette = TestPatterns.DistinctPalette();
        Framebuffer pixels = TestPatterns.Pattern();
        RenderFrame frame = TestPatterns.Frame(pixels, palette);
        frame.KeyHelp = KeyHelp(new GlyphImageCache());
        var rect = PresentationLayout.Compute(width, height, AspectMode.FourByThree, false);
        var items = new OverlayDrawList();
        Assert.Equal(placement, KeyHelpLayout.Build(frame.KeyHelp, width, height, rect, items));
        long drawn = renderer.Statistics.OverlayItemsDrawn;

        CapturedImage image = Render(renderer, frame);

        Assert.Equal(drawn + items.Count, renderer.Statistics.OverlayItemsDrawn);
        Vector3? Classic(int x, int y) => TextReference.Classic(pixels, palette, rect, x, y);
        var (mismatches, ambiguous, changed, first) = TextReference.Compare(image,
            (x, y) => Classic(x, y) is { } below ? TextReference.Overlay(items, x, y, below) : null, Tolerance, Classic);
        Assert.True(mismatches == 0, $"{mismatches} mismatches ({ambiguous} ambiguous, {changed} overlay pixels); first: {first}");
        Assert.True(ambiguous <= image.Width * image.Height / 10, $"{ambiguous} ambiguous pixels"); // 4.5x: every 9th column samples a pixel edge
        Assert.True(changed >= 2000, $"only {changed} output pixels show the overlay");

        if (placement == KeyHelpPlacement.Panel)
        {
            // Straight alpha: inside the panel, away from the text, the picture is darkened by 184/255.
            OverlayItem panel = items.Items[0];
            Assert.Equal(OverlayItemKind.Rectangle, panel.Kind);
            int px = (int)panel.X + 1, py = (int)panel.Y + 1;
            Vector3 expected = TextReference.Blend(Classic(px, py)!.Value, Vector3.Zero, 184 / 255f, linear: false);
            Assert.True(TestPatterns.Close(TextReference.Rgb(expected), image.GetRgb(px, py), 1));
        }
        else
        {
            int outside = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < rect.X; x++)
                    outside += image.GetRgb(x, y) != 0 ? 1 : 0;
            }
            Assert.True(outside > 500, $"only {outside} pixels of the left margin show the key help");
        }
    }

    [VulkanFact]
    public void HiddenOrAbsentKeyHelp_LeavesTheFrameUntouched()
    {
        var renderer = Fixture.Get(1280, 960, ScalingFilter.Nearest, AspectMode.FourByThree);
        RenderFrame frame = TestPatterns.Frame(TestPatterns.Pattern(), TestPatterns.DistinctPalette());
        CapturedImage plain = Render(renderer, frame);
        KeyHelpOverlay keyHelp = KeyHelp(new GlyphImageCache());
        keyHelp.Visible = false;
        frame.KeyHelp = keyHelp;
        long drawn = renderer.Statistics.OverlayItemsDrawn;

        CapturedImage hidden = Render(renderer, frame);
        Assert.Equal(plain.Rgba, hidden.Rgba);
        Assert.Equal(drawn, renderer.Statistics.OverlayItemsDrawn);

        keyHelp.Visible = true;
        CapturedImage shown = Render(renderer, frame);
        Assert.NotEqual(plain.Rgba, shown.Rgba);
        Assert.True(renderer.Statistics.OverlayItemsDrawn > drawn);

        frame.KeyHelp = null;
        CapturedImage absent = Render(renderer, frame);
        Assert.Equal(plain.Rgba, absent.Rgba);
    }

    [VulkanFact]
    public void TextFramesInFlight_EveryCaptureShowsItsOwnText()
    {
        // 640x400 = 2x. Every frame moves the glyphs, changes the text layer's pixels and mask; the
        // CPU runs up to two frames ahead and captures are collected late.
        var renderer = Fixture.Get(640, 400, ScalingFilter.Nearest, AspectMode.SquarePixels);
        static void Build(TextScene scene, int f)
        {
            GlyphKey a = new(1, (byte)'A'), g = new(1, (byte)'g');
            scene.Text.Clear();
            Array.Clear(scene.Text.Mask);
            TestPatterns.FillSeeded(scene.Text.Pixels, f);
            for (int i = 0; i < 6; i++)
                scene.Add(10 + f * 3 + i * 6, 20 + (f * 7 + i * 13) % 150, i % 2 == 0 ? a : g, (byte)(f + i * 40));
            scene.AllowAllCells();
            scene.Publish();
        }
        TextScene Scene()
        {
            var scene = new TextScene();
            scene.Set('A', TextScene.LetterA);
            scene.Set('g', TextScene.LetterG);
            return scene;
        }
        TextScene live = Scene(), check = Scene();
        const int frames = 30;
        long firstFrame = renderer.Statistics.FramesRendered;
        var captures = new List<CapturedImage>();
        for (int f = 0; f < frames; f++)
        {
            Build(live, f);
            if (f % 3 != 1)
                renderer.RequestCapture();
            renderer.Render(live.Frame);
            if (renderer.TakeCapture(waitForGpu: false) is { } early)
                captures.Add(early);
        }
        while (renderer.TakeCapture() is { } late)
            captures.Add(late);

        Assert.Equal(frames - frames / 3, captures.Count);
        var rect = new PresentationRect(0, 0, 640, 400);
        foreach (CapturedImage image in captures)
        {
            int f = (int)(image.FrameNumber - firstFrame);
            Build(check, f);
            var (mismatches, _, changed, first) = TextReference.Compare(image,
                (x, y) => TextReference.GameText(check.Text, check.Palette, rect, x, y), Tolerance,
                (x, y) => TextReference.Classic(check.Text.Pixels, check.Palette, rect, x, y));
            Assert.True(mismatches == 0, $"frame {f}: {mismatches} mismatches; first: {first}");
            Assert.True(changed > 300, $"frame {f}: only {changed} text pixels");
        }
    }

    [VulkanFact]
    public void Resize_BetweenTextFrames_KeepsTextAndKeyHelpCorrect()
    {
        var renderer = Fixture.Get(640, 400, ScalingFilter.Nearest, AspectMode.FourByThree);
        TextScene scene = LetterScene();
        scene.Frame.KeyHelp = KeyHelp(new GlyphImageCache());
        for (int i = 0; i < 12; i++)
        {
            renderer.ResizeOffscreen(640 + i * 80, 400 + i * 50);
            renderer.Render(scene.Frame);
            renderer.Render(scene.Frame);
        }
        CapturedImage image = renderer.CaptureLastFrame();

        Assert.Equal((1520, 950), (image.Width, image.Height));
        var rect = PresentationLayout.Compute(1520, 950, AspectMode.FourByThree, false);
        var items = new OverlayDrawList();
        Assert.NotEqual(KeyHelpPlacement.None, KeyHelpLayout.Build(scene.Frame.KeyHelp, 1520, 950, rect, items));
        var (mismatches, ambiguous, changed, first) = TextReference.Compare(image,
            (x, y) => TextReference.GameText(scene.Text, scene.Palette, rect, x, y) is { } below ? TextReference.Overlay(items, x, y, below) : null,
            Tolerance, (x, y) => TextReference.Classic(scene.Text.Pixels, scene.Palette, rect, x, y));
        Assert.True(mismatches == 0, $"{mismatches} mismatches ({ambiguous} ambiguous); first: {first}");
        Assert.True(changed > 2000, $"only {changed} changed pixels");
    }

    [VulkanFact]
    public void SpritesShowBehindTheText_AndTheGlyphsStayOnTop()
    {
        var renderer = Square();
        // The game drew a letter (index 42) over space; the text layer holds the frame without it.
        var sprites = new SpriteScene();
        SpriteImageKey ship = sprites.AddImage(1, 40, 30, originX: 20, originY: 15);
        sprites.Add(ship, 100, 60);
        var text = new TextLayer(new GlyphImageCache());
        var key = new GlyphKey(1, (byte)'A');
        text.Glyphs.Set(key, SyntheticGlyphs.Build(TextScene.LetterA));
        text.Add(new GlyphInstance(95, 55, key, 42));
        sprites.Layer.Pixels.CopyTo(text.Pixels);
        for (int y = 0; y < TextScene.LetterA.Length; y++)
        {
            for (int x = 0; x < TextScene.LetterA[y].Length; x++)
            {
                if (TextScene.LetterA[y][x] == '#')
                    sprites.Layer.Pixels[95 + x, 55 + y] = 42;
                text.Mask[(55 + y) * Framebuffer.Width + 95 + x] = 1;
            }
        }
        sprites.Layer.MarkPixelsChanged();
        text.Publish();

        CapturedImage classic = Render(renderer, sprites.Frame); // without text: index 42 occludes the sprite
        Assert.Equal(TestPatterns.Rgb(sprites.Palette, 42), At3x(classic, 95, 56));
        sprites.Frame.Text = text;
        CapturedImage image = Render(renderer, sprites.Frame);

        // Reference: the sprites over the text-free pixels (what the classic index image holds), then the glyph.
        var reference = new SpriteScene();
        reference.AddImage(1, 40, 30, originX: 20, originY: 15);
        reference.Add(ship, 100, 60);
        Vector3? Below(int x, int y) => SpriteReference.Expected(reference, Square3x, x, y) is { } rgb
            ? new Vector3(rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255)
            : null;
        var (mismatches, ambiguous, changed, first) = TextReference.Compare(image,
            (x, y) => TextReference.GameText(text, sprites.Palette, Square3x, x, y, below: Below), Tolerance, Below);
        Assert.True(mismatches == 0, $"{mismatches} mismatches ({ambiguous} ambiguous); first: {first}");
        Assert.True(changed > 100, $"only {changed} glyph pixels");
        Assert.Equal(TestPatterns.Rgb(sprites.Palette, 42), At3x(image, 95, 56)); // glyph on top of the sprite
        Assert.Equal(Below(3 * 97 + 1, 3 * 57 + 1)!.Value, ToVector(At3x(image, 97, 57))); // inside the 'A': the sprite
        Assert.NotEqual(TestPatterns.Rgb(sprites.Palette, SpaceView.DefaultBackgroundIndex), At3x(image, 97, 57));
    }

    [VulkanFact]
    public void SrgbTarget_BlendsTextAndKeyHelpInLinearLight()
    {
        using var renderer = VulkanRenderer.CreateOffscreen(960, 600,
            new RendererSettings { Filter = ScalingFilter.Nearest, Aspect = AspectMode.SquarePixels },
            new VulkanRendererOptions { OffscreenFormat = VkFormat.R8G8B8A8Srgb, Log = ValidationLog.Sink, UnlimitedValidationMessages = true });
        TextScene scene = LetterScene();
        scene.Frame.KeyHelp = KeyHelp(new GlyphImageCache());
        CapturedImage image = Render(renderer, scene.Frame);

        var items = new OverlayDrawList();
        Assert.Equal(KeyHelpPlacement.Panel, KeyHelpLayout.Build(scene.Frame.KeyHelp, 960, 600, Square3x, items));
        var (mismatches, ambiguous, changed, first) = TextReference.Compare(image,
            (x, y) => TextReference.GameText(scene.Text, scene.Palette, Square3x, x, y, linear: true) is { } below
                ? TextReference.Overlay(items, x, y, below, linear: true)
                : null,
            Tolerance, (x, y) => TextReference.Classic(scene.Text.Pixels, scene.Palette, Square3x, x, y));
        Assert.True(mismatches == 0, $"{mismatches} mismatches ({ambiguous} ambiguous); first: {first}");
        Assert.True(changed > 2000, $"only {changed} changed pixels");
    }

    [VulkanFact]
    public void Vulkan12Path_DrawsTheSameText()
    {
        using var renderer = VulkanRenderer.CreateOffscreen(1280, 960,
            new RendererSettings { Filter = ScalingFilter.Nearest, Aspect = AspectMode.FourByThree },
            new VulkanRendererOptions { MaxApiVersion = new Version(1, 2), Log = ValidationLog.Sink, UnlimitedValidationMessages = true });
        Assert.False(renderer.Capabilities.UsesVulkan13Core);
        TextScene scene = LetterScene();
        CapturedImage image = Render(renderer, scene.Frame);

        AssertMatchesReference(scene, image, new PresentationRect(0, 0, 1280, 960), minimumTextPixels: 600);
    }

    [VulkanFact]
    public void TextFrames_InSteadyState_DoNotAllocate()
    {
        var renderer = Fixture.Get(1280, 960, ScalingFilter.SharpBilinear, AspectMode.FourByThree);
        var scene = new TextScene();
        var keys = new[]
        {
            scene.Set('A', SyntheticGlyphs.Build(TextScene.LetterA), font: 0),
            scene.Set('g', SyntheticGlyphs.Build(TextScene.LetterG), font: 0),
            scene.Set('C', SyntheticGlyphs.Build(TextScene.Chalk, TextScene.ChalkColours), font: 0),
        };
        scene.Frame.KeyHelp = KeyHelp(new GlyphImageCache()); // a second glyph cache in the same frames

        void Build(int frame)
        {
            scene.Text.Clear();
            Array.Clear(scene.Text.Mask);
            for (int i = 0; i < 120; i++)
                scene.Add((i * 37 + frame) % 300, (i * 23 + frame * 3) % 190, keys[i % keys.Length], (byte)(i + frame));
            scene.AllowAllCells();
            scene.Text.Pixels[frame % 320, 0] = (byte)frame;
            scene.Publish(); // pixels, mask and instances change every frame
        }

        for (int frame = 0; frame < 10; frame++)
        {
            Build(frame);
            renderer.Render(scene.Frame);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 10; frame < 210; frame++)
        {
            Build(frame);
            scene.Palette.SetEntry(frame & 255, (byte)frame, 0, 0);
            renderer.Render(scene.Frame);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        renderer.WaitIdle();

        Assert.Equal(0, allocated);
    }

    private static Vector3 ToVector(int rgb) => new(rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255);
}
