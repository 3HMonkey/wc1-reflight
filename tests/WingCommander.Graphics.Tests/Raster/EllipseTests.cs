using WingCommander.Graphics.Raster;

namespace WingCommander.Graphics.Tests.Raster;

public class EllipseTests
{
    private static HashSet<(int, int)> Pixels(IndexedSurface surface, int cx, int cy)
    {
        var set = new HashSet<(int, int)>();
        for (int y = surface.Top; y <= surface.Bottom; y++)
            for (int x = surface.Left; x <= surface.Right; x++)
                if (surface[x, y] != 0)
                    set.Add((x - cx, y - cy));
        return set;
    }

    [Fact]
    public void Small_outline_matches_the_hand_computed_asm_recurrence()
    {
        // rx = 2, ry = 1: region one plots (0,±1), (±1,±1); region two plots (±2,0).
        var surface = new IndexedSurface(30, 30);
        RasterPrimitives.DrawRasterEllipse(RasterClip.ForSurface(surface), 10, 10, 2, 1, 1);
        Assert.Equal(new HashSet<(int, int)> { (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1), (2, 0), (-2, 0) },
            Pixels(surface, 10, 10));
    }

    [Fact]
    public void Small_filled_ellipse_matches_the_hand_computed_asm_recurrence()
    {
        var surface = new IndexedSurface(30, 30);
        RasterPrimitives.FillRasterEllipse(RasterClip.ForSurface(surface), 10, 10, 2, 1, 1);
        var expected = new HashSet<(int, int)>();
        for (int x = -1; x <= 1; x++)
        {
            expected.Add((x, 1));
            expected.Add((x, -1));
        }
        for (int x = -2; x <= 2; x++)
            expected.Add((x, 0));
        Assert.Equal(expected, Pixels(surface, 10, 10));
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(12, 4)]
    [InlineData(3, 9)]
    [InlineData(40, 25)]
    public void Outline_is_symmetric_closed_and_inside_the_bounding_box(int rx, int ry)
    {
        var surface = new IndexedSurface(120, 120, -60, -60);
        RasterPrimitives.DrawRasterEllipse(RasterClip.ForSurface(surface), 0, 0, rx, ry, 1);
        HashSet<(int, int)> set = Pixels(surface, 0, 0);
        Assert.All(set, p => Assert.Contains((-p.Item1, p.Item2), set));
        Assert.All(set, p => Assert.Contains((p.Item1, -p.Item2), set));
        Assert.All(set, p => Assert.True(Math.Abs(p.Item1) <= rx && Math.Abs(p.Item2) <= ry));
        Assert.Contains((rx, 0), set);
        Assert.Contains((0, ry), set);
        // every row between -ry and ry has a pixel (no gaps in the outline)
        for (int y = -ry; y <= ry; y++)
            Assert.Contains(set, p => p.Item2 == y);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(12, 4)]
    [InlineData(3, 9)]
    public void Filled_ellipse_covers_the_outline(int rx, int ry)
    {
        var outline = new IndexedSurface(60, 60, -30, -30);
        var filled = new IndexedSurface(60, 60, -30, -30);
        RasterPrimitives.DrawRasterEllipse(RasterClip.ForSurface(outline), 0, 0, rx, ry, 1);
        RasterPrimitives.FillRasterEllipse(RasterClip.ForSurface(filled), 0, 0, rx, ry, 1);
        HashSet<(int, int)> o = Pixels(outline, 0, 0), f = Pixels(filled, 0, 0);
        Assert.True(o.IsSubsetOf(f));
    }

    [Fact]
    public void Zero_radius_degenerates_to_a_line()
    {
        var surface = new IndexedSurface(30, 30);
        RasterPrimitives.DrawRasterEllipse(RasterClip.ForSurface(surface), 10, 10, 0, 3, 1);
        Assert.Equal(new HashSet<(int, int)> { (0, -3), (0, -2), (0, -1), (0, 0), (0, 1), (0, 2), (0, 3) }, Pixels(surface, 10, 10));
    }

    [Fact]
    public void Clipped_ellipse_is_the_unclipped_one_inside_the_clip()
    {
        var full = new IndexedSurface(80, 80);
        var part = new IndexedSurface(80, 80);
        RasterPrimitives.DrawRasterEllipse(RasterClip.ForSurface(full), 40, 40, 30, 20, 1);
        RasterPrimitives.DrawRasterEllipse(new RasterClip(part, 20, 25, 70, 45), 40, 40, 30, 20, 1);
        for (int y = 0; y < 80; y++)
            for (int x = 0; x < 80; x++)
                Assert.Equal(x >= 20 && x <= 70 && y >= 25 && y <= 45 ? full[x, y] : 0, part[x, y]);
    }

    [Fact]
    public void Viewport_ellipse_centre_is_relative_to_the_viewport_corner()
    {
        // DrawViewportEllipse does not subtract the viewport origin (unlike the other wrappers)
        // and takes the vertical radius first.
        var gfx = new GraphicsContext();
        var screen = new Viewport(new IndexedSurface(100, 100), 0, 0, 99, 99);
        var panel = screen.Clone();
        panel.SetViewportRect(20, 30, 99, 99);
        gfx.DrawViewportEllipse(panel, 10, 10, 1, 2, 1);
        Assert.Equal(new HashSet<(int, int)> { (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1), (2, 0), (-2, 0) },
            Pixels(screen.Surface!, 30, 40));
    }
}
