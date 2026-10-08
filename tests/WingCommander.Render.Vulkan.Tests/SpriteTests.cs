using WingCommander.Core.Rendering;
using WingCommander.Core.Video;
using WingCommander.Render.Vulkan.Internal;

namespace WingCommander.Render.Vulkan.Tests;

/// <summary>R2 space sprites at output resolution: footprint, transparency, transforms, occlusion, palette, atlas.</summary>
[Collection("Vulkan")]
public sealed class SpriteTests(OffscreenRendererFixture fixture) : VulkanTestBase(fixture)
{
    // 960x600 square pixels = exactly 3 output pixels per logical pixel.
    private static readonly PresentationRect Square3x = new(0, 0, 960, 600);

    private VulkanRenderer Square() => Fixture.Get(960, 600, ScalingFilter.Nearest, AspectMode.SquarePixels);

    private static CapturedImage Render(VulkanRenderer renderer, SpriteScene scene)
    {
        renderer.Render(scene.Frame);
        return renderer.CaptureLastFrame();
    }

    private static void AssertMatchesReference(SpriteScene scene, CapturedImage image, PresentationRect rect, double maxAmbiguousFraction = 0)
    {
        var (mismatches, ambiguous, first) = SpriteReference.Compare(scene, image, rect);
        Assert.True(mismatches == 0, $"{mismatches} mismatches ({ambiguous} ambiguous); first: {first}");
        Assert.True(ambiguous <= maxAmbiguousFraction * image.Width * image.Height, $"{ambiguous} ambiguous pixels");
    }

    [VulkanTheory]
    [InlineData(0f)]
    [InlineData(0.37f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void InterpolatedSprites_AreDrawnBetweenTheirTicks(float interpolation)
    {
        var renderer = Square();
        var scene = new SpriteScene();
        SpriteImageKey ship = scene.AddImage(1, 21, 13, originX: 10, originY: 6);
        ref SpriteInstance moving = ref scene.Add(ship, 140, 80, angle: 20, scale: 1.5f);
        moving.HasPrevious = true;
        moving.PreviousX = 100;
        moving.PreviousY = 60;
        moving.PreviousAngle = 350;
        moving.PreviousScale = 1;
        scene.Add(ship, 250, 150); // no state of the tick before: always where the tick put it

        // A HUD line: one stretched pixel, moving with its object.
        var pixel = SpriteImageKey.Create(-1, 0, 42);
        scene.Images.Set(pixel, new SpriteImage(1, 1, 0, 0, new byte[] { 42 }));
        ref SpriteInstance line = ref scene.Add(pixel, 60.5f, 120);
        line.Scale = 20;
        line.ScaleY = 1;
        line.HasPrevious = true;
        line.PreviousX = 40.5f;
        line.PreviousY = 110;
        line.PreviousScale = 12;
        line.PreviousScaleY = 1;

        scene.Frame.Interpolation = interpolation;
        CapturedImage image = Render(renderer, scene);
        AssertMatchesReference(scene, image, Square3x, maxAmbiguousFraction: 0.01);

        // The line's middle pixel (it spans x 51..70 at the tick, 35..46 one tick before).
        float x = 40.5f + (60.5f - 40.5f) * interpolation, y = 110 + 10 * interpolation;
        Assert.Equal(TestPatterns.Rgb(scene.Palette, 42), image.GetRgb((int)(3 * (x + 0.5f)), (int)(3 * (y + 0.5f))));
    }

    [VulkanFact]
    public void UnscaledSprites_HaveTheSoftwareFootprint()
    {
        var renderer = Square();
        var scene = new SpriteScene();
        SpriteImageKey ship = scene.AddImage(1, 21, 13, originX: 10, originY: 6);
        SpriteImageKey odd = scene.AddImage(2, 8, 9, originX: -3, originY: 12, seed: 3); // hot spot outside the frame
        scene.Add(ship, 100, 50);
        scene.Add(odd, 200, 120);
        scene.Add(ship, 5, 195);     // partly off screen (bottom left)
        scene.Add(ship, 315, 3);     // partly off screen (top right)
        scene.Add(odd, 105, 52);     // overlaps the first ship: drawn on top (painter order)
        CapturedImage image = Render(renderer, scene);

        AssertMatchesReference(scene, image, Square3x);

        // The rule of the contract, checked directly: frame pixel (i, j) -> screen (X - OriginX + i, Y - OriginY + j).
        Assert.True(scene.Images.TryGet(ship, out SpriteImage? frame));
        for (int j = 0; j < frame.Height; j++)
        {
            for (int i = 0; i < frame.Width; i++)
            {
                int sx = 100 - 10 + i, sy = 50 - 6 + j;
                if (sx >= 108 && sx < 116 && sy >= 40 && sy < 49)
                    continue; // under the 8x9 sprite drawn later at (105, 52) with hot spot (-3, 12)
                byte index = frame.Pixels.Span[j * frame.Width + i];
                int expected = TestPatterns.Rgb(scene.Palette, index == SpriteImage.TransparentIndex ? SpaceView.DefaultBackgroundIndex : index);
                Assert.Equal(expected, image.GetRgb(3 * sx + 1, 3 * sy + 1));
            }
        }
    }

    [VulkanFact]
    public void TransparentIndex_ShowsTheClassicLayer()
    {
        var renderer = Square();
        var scene = new SpriteScene();
        var pixels = new byte[4 * 4];
        Array.Fill(pixels, SpriteImage.TransparentIndex);
        pixels[5] = 77; // one opaque pixel at (1, 1)
        var key = SpriteImageKey.Create(4, 0, 0);
        scene.Images.Set(key, new SpriteImage(4, 4, 0, 0, pixels));
        scene.Add(key, 40, 30);
        CapturedImage image = Render(renderer, scene);

        int background = TestPatterns.Rgb(scene.Palette, SpaceView.DefaultBackgroundIndex);
        Assert.Equal(TestPatterns.Rgb(scene.Palette, 77), image.GetRgb(3 * 41 + 1, 3 * 31 + 1));
        Assert.Equal(background, image.GetRgb(3 * 40 + 1, 3 * 30 + 1));
        Assert.Equal(background, image.GetRgb(3 * 43 + 1, 3 * 33 + 1));
        AssertMatchesReference(scene, image, Square3x);
    }

    [VulkanFact]
    public void Occlusion_OnlyBackgroundPixelsInsideTheWindowAndClipAreReplaced()
    {
        var renderer = Square();
        var scene = new SpriteScene();
        // HUD drawn by the game into the classic layer: a gunsight-like cross of index 42.
        for (int i = 140; i < 180; i++)
        {
            scene.Layer.Pixels[i, 100] = 42;
            scene.Layer.Pixels[160, i - 40] = 42;
        }
        // Cockpit art (index 7) at the bottom; the window mask excludes it and a strip on the left.
        for (int y = 150; y < 200; y++)
            scene.Layer.Pixels.Row(y).Fill(7);
        scene.Layer.MarkPixelsChanged();
        var mask = new SpaceViewMask();
        mask.SetAll(false);
        mask.SetRun(0, 10, 320 * 140); // rows 10..149 like a cockpit view geometry run
        mask.SetRect(new ScreenRect(0, 0, 20, 200), false);
        scene.Space.WindowMask = mask;
        scene.Space.Sprites.Clip = new ScreenRect(0, 10, 300, 180); // space buffer area

        SpriteImageKey big = scene.AddImage(5, 120, 90, originX: 60, originY: 45);
        scene.Add(big, 160, 100);          // across the cross
        scene.Add(big, 30, 150, scale: 1); // across the cockpit edge and the masked strip
        scene.Add(big, 300, 40);           // across the clip edge
        CapturedImage image = Render(renderer, scene);

        AssertMatchesReference(scene, image, Square3x);
        Assert.Equal(TestPatterns.Rgb(scene.Palette, 42), image.GetRgb(3 * 150 + 1, 3 * 100 + 1)); // HUD on top
        Assert.Equal(TestPatterns.Rgb(scene.Palette, 7), image.GetRgb(3 * 30 + 1, 3 * 160 + 1));   // cockpit on top
        Assert.Equal(TestPatterns.Rgb(scene.Palette, SpaceView.DefaultBackgroundIndex), image.GetRgb(3 * 10 + 1, 3 * 120 + 1)); // masked strip
        Assert.Equal(TestPatterns.Rgb(scene.Palette, SpaceView.DefaultBackgroundIndex), image.GetRgb(3 * 305 + 1, 3 * 40 + 1)); // clipped
    }

    [VulkanTheory]
    [InlineData(90f, 1f, SpriteFlip.None)]
    [InlineData(180f, 1f, SpriteFlip.Horizontal)]
    [InlineData(270f, 2f, SpriteFlip.Vertical)]
    [InlineData(-90f, 0.5f, SpriteFlip.Horizontal | SpriteFlip.Vertical)]
    [InlineData(0f, 3f, SpriteFlip.None)]
    [InlineData(0f, 1f, SpriteFlip.Horizontal)]
    public void QuarterTurnsScalesAndFlips_MatchTheReferenceExactly(float angle, float scale, SpriteFlip flip)
    {
        var renderer = Square();
        var scene = new SpriteScene();
        SpriteImageKey key = scene.AddImage(6, 23, 15, originX: 11, originY: 4);
        scene.Add(key, 160, 100, angle, scale, flip);
        scene.Add(key, 61.25f, 40.25f, angle, scale, flip); // sub-pixel position
        CapturedImage image = Render(renderer, scene);

        AssertMatchesReference(scene, image, Square3x, maxAmbiguousFraction: 0.01);
    }

    [VulkanTheory]
    [InlineData(30f, 1f, SpriteFlip.None)]
    [InlineData(45f, 1.5f, SpriteFlip.Horizontal)]
    [InlineData(197.3f, 2.25f, SpriteFlip.Vertical)]
    [InlineData(-12.5f, 0.75f, SpriteFlip.None)]
    public void ArbitraryRotations_MatchTheReferenceAwayFromTexelEdges(float angle, float scale, SpriteFlip flip)
    {
        var renderer = Square();
        var scene = new SpriteScene();
        SpriteImageKey key = scene.AddImage(7, 31, 19, originX: 15, originY: 9, seed: 1);
        scene.Add(key, 160, 100, angle, scale, flip);
        CapturedImage image = Render(renderer, scene);

        AssertMatchesReference(scene, image, Square3x, maxAmbiguousFraction: 0.01);
    }

    [VulkanFact]
    public void FourByThree_StretchesSpritesLikeTheClassicLayer()
    {
        var renderer = Fixture.Get(1280, 960, ScalingFilter.Nearest, AspectMode.FourByThree);
        var scene = new SpriteScene();
        SpriteImageKey key = scene.AddImage(8, 17, 11, originX: 8, originY: 5);
        scene.Add(key, 50, 40);
        scene.Add(key, 250, 160, 90f, 2f);
        CapturedImage image = Render(renderer, scene);

        AssertMatchesReference(scene, image, new PresentationRect(0, 0, 1280, 960), maxAmbiguousFraction: 0.002);
    }

    [VulkanFact]
    public void PaletteChange_RecoloursSpritesWithoutReuploading()
    {
        var renderer = Square();
        var scene = new SpriteScene();
        SpriteImageKey key = scene.AddImage(9, 10, 10, 0, 0);
        scene.Add(key, 100, 100);
        Render(renderer, scene);
        long uploads = renderer.Statistics.SpriteUploads, resets = renderer.Statistics.AtlasResets;

        Assert.True(scene.Images.TryGet(key, out SpriteImage? frame));
        byte index = frame.Pixels.Span[0];
        scene.Palette.SetEntry(index, 0xF0, 0x0D, 0x01); // like the damage flash or a fade
        CapturedImage image = Render(renderer, scene);

        Assert.Equal(0xF00D01, image.GetRgb(3 * 100 + 1, 3 * 100 + 1));
        Assert.Equal(uploads, renderer.Statistics.SpriteUploads);
        Assert.Equal(resets, renderer.Statistics.AtlasResets);
        AssertMatchesReference(scene, image, Square3x);
    }

    [VulkanFact]
    public void Atlas_UploadsEachImageOnce_AndForgetsThemWhenTheCacheIsCleared()
    {
        var renderer = Square();
        var scene = new SpriteScene();
        SpriteImageKey a = scene.AddImage(10, 12, 12, 6, 6);
        SpriteImageKey b = scene.AddImage(11, 30, 7, 0, 0, seed: 2);
        for (int i = 0; i < 6; i++)
            scene.Add(i % 2 == 0 ? a : b, 20 + 40 * i, 60);
        long before = renderer.Statistics.SpriteUploads;
        for (int frame = 0; frame < 4; frame++)
            renderer.Render(scene.Frame);
        Assert.Equal(before + 2, renderer.Statistics.SpriteUploads);

        // New mission: the cache is cleared and refilled (here with a different picture under the same key).
        scene.Images.Clear();
        scene.AddImage(10, 12, 12, 6, 6, seed: 4);
        scene.AddImage(11, 30, 7, 0, 0, seed: 2);
        long resets = renderer.Statistics.AtlasResets;
        CapturedImage image = Render(renderer, scene);
        Assert.Equal(before + 4, renderer.Statistics.SpriteUploads);
        Assert.Equal(resets + 1, renderer.Statistics.AtlasResets);
        AssertMatchesReference(scene, image, Square3x);
    }

    [VulkanFact]
    public void FullAtlas_IsResetAndTheFrameStillDrawsCorrectly()
    {
        var renderer = Square();
        var scene = new SpriteScene();
        // 3 frames x 36 images of 190x190: each frame fits into the 2048x2048 atlas, all of them do not.
        for (int round = 0; round < 3; round++)
        {
            scene.Space.Sprites.Clear();
            for (int i = 0; i < 36; i++)
            {
                SpriteImageKey key = scene.AddImage(100 + round * 36 + i, 190, 190, 95, 95, seed: i);
                scene.Add(key, 20 + (i % 6) * 56, 20 + (i / 6) * 33);
            }
            renderer.Render(scene.Frame);
        }
        CapturedImage image = renderer.CaptureLastFrame();

        Assert.True(renderer.Statistics.AtlasResets >= 1, "the atlas should have been reset");
        AssertMatchesReference(scene, image, Square3x);
    }

    [VulkanFact]
    public void WindowMask_IsUploadedOnlyWhenItChanges()
    {
        var renderer = Square();
        var scene = new SpriteScene();
        SpriteImageKey key = scene.AddImage(12, 50, 50, 25, 25);
        scene.Add(key, 160, 100);
        var mask = new SpaceViewMask();
        mask.SetAll(true);
        scene.Space.WindowMask = mask;
        long uploads = renderer.Statistics.WindowMaskUploads;

        renderer.Render(scene.Frame);
        renderer.Render(scene.Frame);
        Assert.Equal(uploads + 1, renderer.Statistics.WindowMaskUploads);

        mask.SetRect(new ScreenRect(150, 90, 20, 20), false);
        CapturedImage image = Render(renderer, scene);
        Assert.Equal(uploads + 2, renderer.Statistics.WindowMaskUploads);
        AssertMatchesReference(scene, image, Square3x);
        Assert.Equal(TestPatterns.Rgb(scene.Palette, SpaceView.DefaultBackgroundIndex), image.GetRgb(3 * 160 + 1, 3 * 100 + 1));
    }

    [VulkanTheory]
    [InlineData(ScalingFilter.SharpBilinear)]
    [InlineData(ScalingFilter.Linear)]
    public void SmoothFilters_AreExactAtTexelCentresOfOpaqueRegions(ScalingFilter filter)
    {
        var renderer = Fixture.Get(960, 600, filter, AspectMode.SquarePixels);
        var scene = new SpriteScene();
        var pixels = new byte[16 * 16];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = (byte)(20 + (i % 16) + (i / 16) * 3);
        var key = SpriteImageKey.Create(4, 1, 0);
        scene.Images.Set(key, new SpriteImage(16, 16, 0, 0, pixels));
        scene.Add(key, 100, 50);
        CapturedImage image = Render(renderer, scene);

        // Output pixel 3x+1 samples the centre of logical pixel x = the centre of the texel.
        for (int j = 1; j < 15; j++)
        {
            for (int i = 1; i < 15; i++)
            {
                int expected = TestPatterns.Rgb(scene.Palette, pixels[j * 16 + i]);
                Assert.True(TestPatterns.Close(expected, image.GetRgb(3 * (100 + i) + 1, 3 * (50 + j) + 1), 1),
                    $"texel ({i},{j})");
            }
        }
    }

    [VulkanFact]
    public void FramesWithoutSprites_AreExactlyTheClassicLayer()
    {
        var renderer = Square();
        var scene = new SpriteScene();
        TestPatterns.FillSeeded(scene.Layer.Pixels, 3);
        scene.Layer.MarkPixelsChanged();
        long drawn = renderer.Statistics.SpritesDrawn;

        scene.Frame.Space = null;
        CapturedImage classicOnly = Render(renderer, scene);
        scene.Frame.Space = scene.Space; // empty sprite list
        CapturedImage empty = Render(renderer, scene);
        SpriteImageKey key = scene.AddImage(13, 10, 10, 5, 5);
        scene.Add(key, 160, 100);
        scene.Space.BackgroundIndex = 254; // no classic pixel shows it: every sprite pixel is occluded
        CapturedImage occluded = Render(renderer, scene);

        Assert.Equal(classicOnly.Rgba, empty.Rgba);
        Assert.Equal(classicOnly.Rgba, occluded.Rgba);
        Assert.Equal(drawn + 1, renderer.Statistics.SpritesDrawn);
    }

    [VulkanFact]
    public void SpriteFrames_InSteadyState_DoNotAllocate()
    {
        var renderer = Fixture.Get(1280, 960, ScalingFilter.SharpBilinear, AspectMode.FourByThree);
        var scene = new SpriteScene();
        var keys = new SpriteImageKey[8];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = scene.AddImage(200 + i, 20 + 3 * i, 16 + 2 * i, 10, 8, seed: i);

        void Build(int frame)
        {
            scene.Space.Sprites.Clear();
            for (int i = 0; i < 64; i++)
            {
                ref SpriteInstance sprite = ref scene.Space.Sprites.Add();
                sprite.Image = keys[i % keys.Length];
                sprite.X = (i * 37 + frame * 3) % 320;
                sprite.Y = (i * 23 + frame) % 200;
                sprite.Angle = (i * 11 + frame * 7) % 360;
                sprite.Scale = 0.5f + (i % 5) * 0.4f;
                sprite.Flip = (SpriteFlip)((i & 3) << 4);
            }
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

    [Fact]
    public void SpriteTrig_ReproducesTheOriginalQuarterCosineTable()
    {
        // First entries of anRLEQuarterCosine (Graphics TrigTables.QuarterCosine).
        Assert.Equal(new[] { 65536, 65536, 65536, 65535, 65534, 65534, 65532, 65531, 65530, 65528 },
            Enumerable.Range(0, 10).Select(SpriteTrig.QuarterCosine).ToArray());
        Assert.Equal(0, SpriteTrig.QuarterCosine(900));
        Assert.Equal((0f, 1f), SpriteTrig.Get(90));
        Assert.Equal((-1f, 0f), SpriteTrig.Get(180));
        Assert.Equal((0f, -1f), SpriteTrig.Get(-90));
        Assert.Equal((1f, 0f), SpriteTrig.Get(720));
        var (cos30, sin30) = SpriteTrig.Get(30);
        Assert.Equal(SpriteTrig.QuarterCosine(300) / 65536f, cos30);
        Assert.Equal(SpriteTrig.QuarterCosine(600) / 65536f, sin30);
        Assert.Equal(SpriteTrig.Get(12.34f), SpriteTrig.Get(12.3f)); // 0.1 degree resolution
    }
}
