using WingCommander.Core.Rendering;
using WingCommander.Core.Video;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Tests.TestSupport;
using WingCommander.Graphics.Text;

namespace WingCommander.Graphics.Tests.Text;

public class TextLayerTrackerTests
{
    private const byte Ink = 15;
    private const byte Paper = 5;

    /// <summary>'A' = 3x3 plus, 'B' = 2x3 block, 'C' = 3x3 with background corners (bg index 0).</summary>
    private static BitmapFont Font() => TestFonts.Build(3, Ink, 0, new Dictionary<byte, byte[]>
    {
        [(byte)'A'] = [0xFF, Ink, 0xFF, Ink, Ink, Ink, 0xFF, Ink, 0xFF],
        [(byte)'B'] = [Ink, Ink, Ink, Ink, Ink, Ink],
        [(byte)'C'] = [0, Ink, 0, Ink, Ink, Ink, 0, Ink, 0],
        [(byte)' '] = [0, 0, 0, 0, 0, 0, 0, 0, 0],
    }, c => c == 'B' ? 2 : 3, index: 1);

    private sealed class Rig
    {
        public Rig()
        {
            Frame = new Framebuffer();
            Array.Fill(Frame.Pixels, Paper);
            Gfx = new GraphicsContext(Frame, new Palette(), fonts: null);
            Tracker = new TextLayerTracker(Gfx.ScreenSurface!, new GlyphImageSource(new GlyphImageCache()));
            Gfx.TextTracker = Tracker;
            Layer = new TextLayer(Tracker.Glyphs.Cache);
            Context = new TextContext { Viewport = Gfx.Screen, Font = Font(), Colour = 7, BackgroundColour = 0xFF };
        }

        public Framebuffer Frame { get; }

        public GraphicsContext Gfx { get; }

        public TextLayerTracker Tracker { get; }

        public TextLayer Layer { get; }

        public TextContext Context { get; }

        public void Draw(char character, int x, int y, byte colour = 7, byte background = 0xFF)
        {
            Context.Colour = colour;
            Context.BackgroundColour = background;
            Context.CursorX = (short)x;
            BitmapFont font = Context.Font!;
            Gfx.DrawFontGlyph((byte)character, Context, font.Height, font.GetWidth((byte)character), y);
        }

        public void Publish() => Tracker.Publish(Frame.Pixels, Layer);

        public ushort Mask(int x, int y) => Layer.Mask[y * 320 + x];

        public byte Pixel(int x, int y) => Layer.Pixels.Pixels[y * 320 + x];
    }

    [Fact]
    public void TransparentGlyph_IsListed_AndItsInkLeavesThePixels()
    {
        var rig = new Rig();
        rig.Draw('A', 10, 20);
        rig.Publish();
        Assert.Equal(1, rig.Layer.Count);
        Assert.Equal(new GlyphInstance(10, 20, new GlyphKey(1, (byte)'A'), 7), rig.Layer.Instances[0]);
        Assert.Equal(7, rig.Frame.Pixels[21 * 320 + 11]);     // the game's frame keeps the glyph
        Assert.Equal(Paper, rig.Pixel(11, 21));                 // the text layer's frame does not
        for (int y = 20; y < 23; y++)
            for (int x = 10; x < 13; x++)
                Assert.Equal(1, rig.Mask(x, y));
        Assert.Equal(0, rig.Mask(13, 21));
        Assert.Equal(0, rig.Mask(9, 21));
    }

    [Fact]
    public void OpaqueBackground_StaysInThePixels()
    {
        var rig = new Rig();
        rig.Draw('C', 10, 20, colour: 7, background: 2);
        rig.Publish();
        Assert.Equal(2, rig.Pixel(10, 20)); // background corner
        Assert.Equal(2, rig.Pixel(11, 21)); // ink replaced by the background colour
        Assert.Equal(1, rig.Mask(11, 21));
    }

    [Fact]
    public void GlyphWithChangedInk_IsShownByTheClassicFrame()
    {
        var rig = new Rig();
        rig.Draw('A', 10, 20);
        rig.Frame.Pixels[21 * 320 + 11] = 9; // something drawn over the centre
        rig.Publish();
        Assert.Equal(0, rig.Layer.Count);
        Assert.Equal(0, rig.Mask(11, 21));
        Assert.Equal(9, rig.Pixel(11, 21));
        Assert.Equal(0, rig.Mask(10, 21)); // the remaining ink stays classic, no fragment is redrawn
        Assert.Equal(7, rig.Pixel(10, 21));
    }

    [Fact]
    public void ClearedText_LeavesNoFragments()
    {
        // A subtitle line on an opaque background, then the panel is cleared to that background:
        // the old background pixels are unchanged, but the glyph is gone.
        var rig = new Rig();
        rig.Draw('C', 10, 20, colour: 7, background: 2);
        for (int y = 20; y < 23; y++)
            for (int x = 10; x < 13; x++)
                rig.Frame.Pixels[y * 320 + x] = 2;
        rig.Publish();
        Assert.Equal(0, rig.Layer.Count);
        for (int y = 20; y < 23; y++)
            for (int x = 10; x < 13; x++)
                Assert.Equal(0, rig.Mask(x, y));
    }

    [Fact]
    public void NewOpaqueTextOverOldText_OldRemainderStaysWhereTheClassicFrameShowsIt()
    {
        var rig = new Rig();
        rig.Draw('C', 10, 20, colour: 7, background: 2); // index 0
        rig.Draw('B', 11, 20, colour: 8, background: 2); // index 1, covers columns 11..12
        rig.Publish();
        Assert.Equal(2, rig.Layer.Count);
        Assert.Equal(1, rig.Mask(10, 21)); // the old glyph's left column is still on screen
        Assert.Equal(2, rig.Mask(11, 21));
    }

    [Fact]
    public void FullyOverwrittenGlyph_IsForgotten()
    {
        var rig = new Rig();
        rig.Draw('B', 10, 20);
        for (int y = 20; y < 23; y++)
            for (int x = 10; x < 12; x++)
                rig.Frame.Pixels[y * 320 + x] = 3;
        rig.Publish();
        Assert.Equal(0, rig.Layer.Count);
        Assert.Equal(0, rig.Tracker.LiveGlyphs);
        Assert.Equal(3, rig.Pixel(10, 20));
    }

    [Fact]
    public void ShadowUnderTransparentText_BothDrawWhereTheyOverlap()
    {
        var rig = new Rig();
        rig.Draw('A', 11, 21, colour: 0); // shadow, index 0
        rig.Draw('A', 10, 20, colour: 7); // text, index 1
        rig.Publish();
        Assert.Equal(2, rig.Layer.Count);
        // (12, 22) is the shadow's centre and the text's transparent corner: both layers apply.
        Assert.Equal(1, rig.Mask(12, 22));
        Assert.Equal(Paper, rig.Pixel(12, 22));
        // (11, 21): text ink over shadow corner (transparent): both.
        Assert.Equal(1, rig.Mask(11, 21));
        // (10, 20): only the text's cell.
        Assert.Equal(2, rig.Mask(10, 20));
    }

    [Fact]
    public void OpaqueGlyphOverOlderText_HidesIt()
    {
        var rig = new Rig();
        rig.Draw('A', 10, 20);                          // index 0
        rig.Draw('C', 10, 20, colour: 8, background: 2); // index 1, opaque
        rig.Publish();
        Assert.Equal(2, rig.Mask(11, 21)); // only the newer glyph
        Assert.Equal(2, rig.Pixel(11, 21));
    }

    [Fact]
    public void SameGlyphRedrawn_IsListedOnce()
    {
        var rig = new Rig();
        for (int i = 0; i < 6; i++)
        {
            rig.Draw('A', 10, 20);
            rig.Publish();
        }
        Assert.Equal(1, rig.Layer.Count);
        Assert.Equal(1, rig.Tracker.LiveGlyphs);
        Assert.Equal(Paper, rig.Pixel(11, 21));
    }

    [Fact]
    public void GlyphCrossingTheScreenEdge_IsNotFollowed()
    {
        var rig = new Rig();
        rig.Draw('A', 318, 20);
        rig.Draw('A', 10, 198);
        rig.Publish();
        Assert.Equal(0, rig.Layer.Count);
    }

    [Fact]
    public void BackgroundOnlyGlyph_IsOrdinaryDrawing()
    {
        var rig = new Rig();
        rig.Draw('A', 10, 20);
        rig.Draw(' ', 10, 20, colour: 7, background: 4); // erases the A with an opaque space
        rig.Publish();
        Assert.Equal(0, rig.Layer.Count);
        Assert.Equal(4, rig.Pixel(11, 21));
    }

    [Fact]
    public void Disabled_PassesTheFrameThrough()
    {
        var rig = new Rig();
        rig.Tracker.Enabled = false;
        rig.Draw('A', 10, 20);
        rig.Publish();
        Assert.Equal(0, rig.Layer.Count);
        Assert.Equal(7, rig.Pixel(11, 21));
        Assert.Equal(0, rig.Mask(11, 21));
    }

    [Fact]
    public void Publish_ChangesTheVersion_AndUsesTheGlyphImages()
    {
        var rig = new Rig();
        int version = rig.Layer.Version;
        rig.Draw('A', 10, 20);
        rig.Publish();
        Assert.NotEqual(version, rig.Layer.Version);
        Assert.True(rig.Layer.Glyphs.TryGet(new GlyphKey(1, (byte)'A'), out GlyphImage? image));
        Assert.True(image.HasForeground);
    }

    [Fact]
    public void TextDrawnOffscreen_FollowsTheCopyToTheScreen()
    {
        var rig = new Rig();
        var buffer = new IndexedSurface(100, 40);
        buffer.Clear(Paper);
        var bufferViewport = new Viewport(buffer, 0, 0, 99, 39);
        rig.Context.Viewport = bufferViewport;
        rig.Draw('A', 4, 5);
        rig.Context.Viewport = rig.Gfx.Screen;
        rig.Gfx.CopyViewportContents(bufferViewport, new Viewport(rig.Gfx.ScreenSurface, 50, 60, 149, 99));
        rig.Publish();
        Assert.Equal(1, rig.Layer.Count);
        Assert.Equal(new GlyphInstance(54, 65, new GlyphKey(1, (byte)'A'), 7), rig.Layer.Instances[0]);
        Assert.Equal(1, rig.Mask(55, 66));
        Assert.Equal(Paper, rig.Pixel(55, 66));
        Assert.Equal(7, rig.Frame.Pixels[66 * 320 + 55]);
    }

    [Fact]
    public void SavedAndRestoredBackground_KeepsTheText()
    {
        var rig = new Rig();
        rig.Draw('A', 10, 20);
        var saved = new Viewport(new IndexedSurface(30, 10), 0, 0, 29, 9);
        var area = new Viewport(rig.Gfx.ScreenSurface, 0, 15, 29, 24);
        rig.Gfx.CopyViewportContents(area, saved);                         // save
        rig.Gfx.DrawFilledViewportRect(rig.Gfx.Screen!, 0, 15, 29, 24, 3);  // a panel covers it
        rig.Publish();
        Assert.Equal(0, rig.Layer.Count);
        rig.Gfx.CopyViewportContents(saved, area);                         // restore
        rig.Publish();
        Assert.Equal(1, rig.Layer.Count);
        Assert.Equal(new GlyphInstance(10, 20, new GlyphKey(1, (byte)'A'), 7), rig.Layer.Instances[0]);
        Assert.Equal(1, rig.Mask(11, 21));
    }

    [Fact]
    public void PartiallyCopiedGlyph_StaysClassic()
    {
        var rig = new Rig();
        var buffer = new IndexedSurface(20, 10);
        buffer.Clear(Paper);
        var bufferViewport = new Viewport(buffer, 0, 0, 19, 9);
        rig.Context.Viewport = bufferViewport;
        rig.Draw('B', 0, 0);
        // Only the left column of the 2-wide glyph is copied.
        rig.Gfx.CopyViewportContents(new Viewport(buffer, 0, 0, 0, 9), new Viewport(rig.Gfx.ScreenSurface, 100, 100, 100, 109));
        rig.Publish();
        // Half a glyph is not drawn at output resolution; the copied column stays classic.
        Assert.Equal(0, rig.Layer.Count);
        Assert.Equal(0, rig.Mask(100, 101));
        Assert.Equal(7, rig.Pixel(100, 101));
    }
}
