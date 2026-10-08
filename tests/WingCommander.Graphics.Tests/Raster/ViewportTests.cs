using WingCommander.Core.Video;
using WingCommander.Graphics.Raster;

namespace WingCommander.Graphics.Tests.Raster;

public class ViewportTests
{
    [Fact]
    public void Screen_viewport_wraps_the_framebuffer_without_copying()
    {
        var framebuffer = new Framebuffer();
        var screen = Viewport.InitializeDIBScreenViewport(framebuffer);
        Assert.Same(framebuffer.Pixels, screen.Surface!.Pixels);
        Assert.Equal((0, 0, 319, 199), (screen.Left, screen.Top, screen.Right, screen.Bottom));
        var gfx = new GraphicsContext();
        gfx.DrawViewportPixel(screen, 17, 23, 0x42);
        Assert.Equal(0x42, framebuffer[17, 23]);
    }

    [Fact]
    public void Allocated_viewport_uses_its_rectangle_as_coordinate_origin()
    {
        var vp = Viewport.Allocate(100, 50, 109, 54, clearColour: 7);
        Assert.Equal(10, vp.Surface!.Width);
        Assert.Equal(5, vp.Surface.Height);
        Assert.Equal(100, vp.Surface.OriginX);
        Assert.Equal(50, vp.Surface.OriginY);
        Assert.All(vp.Surface.Pixels, p => Assert.Equal(7, p));
        var gfx = new GraphicsContext();
        gfx.DrawViewportPixel(vp, 101, 52, 9);
        Assert.Equal(9, vp.Surface.Pixels[2 * 10 + 1]);
        Assert.Equal(9, gfx.GetViewportPixel(vp, 101, 52));
        Assert.Equal(-3, gfx.GetViewportPixel(vp, 99, 52));
        Assert.Equal(-3, gfx.GetViewportPixel(vp, 101, 55));
    }

    [Fact]
    public void Unallocated_rectangle_fails_and_allocate_viewport_reports_it()
    {
        var vp = new Viewport(null, 10, 10, 9, 20);
        Assert.False(vp.AllocateViewport(-1));
        Assert.Null(vp.Surface);
        Assert.Throws<InvalidOperationException>(() => new GraphicsContext().DrawViewportPixel(vp, 10, 10, 1));
    }

    [Fact]
    public void Aliases_share_the_surface_and_clip_to_their_own_rectangle()
    {
        var framebuffer = new Framebuffer();
        var gfx = new GraphicsContext(framebuffer, new Palette(), null);
        var screen = gfx.Screen!;
        var vdu = screen.Clone();
        vdu.SetViewportRect(10, 133, 82, 198);
        Assert.Same(screen.Surface, vdu.Surface);
        gfx.ClearViewport(vdu, 5);
        Assert.Equal(5, framebuffer[10, 133]);
        Assert.Equal(5, framebuffer[82, 198]);
        Assert.Equal(0, framebuffer[9, 133]);
        Assert.Equal(0, framebuffer[83, 198]);
        Assert.Equal(0, framebuffer[10, 132]);
        Assert.Equal(0, framebuffer[10, 199]);
        Assert.Equal(73 * 66, framebuffer.Pixels.Count(p => p == 5));
    }

    [Fact]
    public void Clearing_the_screen_object_requests_a_present_but_clearing_an_alias_only_marks_dirty()
    {
        var framebuffer = new Framebuffer();
        var gfx = new GraphicsContext(framebuffer, new Palette(), null);
        var alias = gfx.Screen!.Clone();
        Assert.False(gfx.ClearViewport(alias, 3));
        Assert.True(gfx.ScreenDirty);
        Assert.Equal(3, framebuffer[319, 199]);
        gfx.ScreenDirty = false;
        Assert.True(gfx.ClearViewport(gfx.Screen!, 4));
        Assert.True(gfx.ScreenDirty);
        Assert.False(gfx.ClearViewport(Viewport.Allocate(0, 0, 3, 3), 1));
    }

    [Fact]
    public void Off_screen_drawing_does_not_mark_the_screen_dirty()
    {
        var gfx = new GraphicsContext(new Framebuffer(), new Palette(), null);
        var buffer = Viewport.Allocate(0, 0, 9, 9);
        gfx.DrawViewportLine(buffer, 0, 0, 9, 9, 1);
        Assert.False(gfx.ScreenDirty);
        gfx.GetViewportPixel(gfx.Screen!, 0, 0);
        Assert.True(gfx.ScreenDirty); // even reads mark it, like ValidateViewportBounds
    }

    [Fact]
    public void Copy_viewport_contents_is_top_left_aligned_rect_to_rect()
    {
        var gfx = new GraphicsContext();
        var source = Viewport.Allocate(0, 0, 9, 4);
        for (int i = 0; i < source.Surface!.Pixels.Length; i++)
            source.Surface.Pixels[i] = (byte)i;
        var screen = new Viewport(new IndexedSurface(320, 200), 0, 0, 319, 199);
        var target = screen.Clone();
        target.SetViewportRect(200, 100, 205, 110); // narrower and taller than the source
        gfx.CopyViewportContents(source, target);
        IndexedSurface s = screen.Surface!;
        for (int y = 0; y < 11; y++)
        {
            for (int x = 0; x < 6; x++)
            {
                byte expected = y < 5 ? (byte)(y * 10 + x) : (byte)0;
                Assert.Equal(expected, s[200 + x, 100 + y]);
            }
        }
        Assert.Equal(0, s[206, 100]);
        Assert.Equal(30 - 1, s.Pixels.Count(p => p != 0)); // 6x5 copied, one of them is value 0
    }

    [Fact]
    public void Modal_panel_save_and_restore_round_trips_the_background()
    {
        // InitializeModalTextPanel / RestoreModalTextPanel: allocate a buffer with the panel rect,
        // copy the screen region into it, draw, copy it back.
        var gfx = new GraphicsContext(new Framebuffer(), new Palette(), null);
        var screen = gfx.Screen!;
        var random = new Random(1);
        random.NextBytes(screen.Surface!.Pixels);
        byte[] before = screen.Surface.Pixels.ToArray();
        var panel = screen.Clone();
        panel.SetViewportRect(40, 60, 279, 139);
        var saved = new Viewport(null, panel.Left, panel.Top, panel.Right, panel.Bottom);
        Assert.True(saved.AllocateViewport(0));
        gfx.CopyViewportContents(panel, saved);
        gfx.ClearViewport(panel, 0);
        gfx.DrawViewportBorder(panel, panel.Left, panel.Top, panel.Right, panel.Bottom, 15);
        Assert.NotEqual(before, screen.Surface.Pixels);
        gfx.CopyViewportContents(saved, panel);
        saved.FreeViewport();
        Assert.Equal(before, screen.Surface.Pixels);
    }

    [Fact]
    public void Overlapping_copy_within_one_surface_moving_down_is_safe()
    {
        // Rows are copied bottom-up (asm order) for CopyViewportContents.
        var surface = new IndexedSurface(4, 6);
        for (int y = 0; y < 6; y++)
            surface.GetRow(y).Fill((byte)(y + 1));
        var gfx = new GraphicsContext();
        var from = new Viewport(surface, 0, 0, 3, 3);
        var to = new Viewport(surface, 0, 2, 3, 5);
        gfx.CopyViewportContents(from, to);
        Assert.Equal(new byte[] { 1, 2, 1, 2, 3, 4 }, Enumerable.Range(0, 6).Select(y => surface[0, y]).ToArray());
    }

    [Fact]
    public void Calc_rectangle_area_truncates_to_16_bits()
    {
        Assert.Equal(64000 - 65536, new Viewport(null, 0, 0, 319, 199).CalcRectangleArea());
        Assert.Equal(12, new Viewport(null, 5, 5, 8, 7).CalcRectangleArea());
    }

    [Fact]
    public void Filled_rect_and_border_cover_inclusive_edges()
    {
        var gfx = new GraphicsContext();
        var vp = new Viewport(new IndexedSurface(20, 20), 0, 0, 19, 19);
        gfx.DrawFilledViewportRect(vp, 2, 3, 5, 4, 9);
        Assert.Equal(8, vp.Surface!.Pixels.Count(p => p == 9));
        gfx.DrawViewportBorder(vp, 10, 10, 14, 12, 4);
        Assert.Equal(5 + 5 + 1 + 1, vp.Surface.Pixels.Count(p => p == 4));
        Assert.Equal(4, vp.Surface[10, 11]);
        Assert.Equal(0, vp.Surface[11, 11]);
    }
}
