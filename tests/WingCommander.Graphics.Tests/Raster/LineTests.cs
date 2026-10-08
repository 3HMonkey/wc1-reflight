using WingCommander.Graphics.Raster;

namespace WingCommander.Graphics.Tests.Raster;

public class LineTests
{
    private static HashSet<(int, int)> Draw(int x1, int y1, int x2, int y2, int cl = -100, int ct = -100, int cr = 200, int cb = 200)
    {
        var surface = new IndexedSurface(400, 400, -150, -150);
        var clip = new RasterClip(surface, cl, ct, cr, cb);
        RasterPrimitives.DrawClippedLine(clip, x1, y1, x2, y2, 1);
        return Pixels(surface);
    }

    private static HashSet<(int, int)> Pixels(IndexedSurface surface)
    {
        var set = new HashSet<(int, int)>();
        for (int y = surface.Top; y <= surface.Bottom; y++)
            for (int x = surface.Left; x <= surface.Right; x++)
                if (surface[x, y] != 0)
                    set.Add((x, y));
        return set;
    }

    [Fact]
    public void Shallow_line_rounds_half_up_from_the_start_point()
    {
        Assert.Equal(new HashSet<(int, int)> { (0, 0), (1, 0), (2, 1), (3, 1), (4, 2), (5, 2) }, Draw(0, 0, 5, 2));
        // slope exactly 1/2: the half step rounds up
        Assert.Equal(new HashSet<(int, int)> { (0, 0), (1, 1), (2, 1), (3, 2), (4, 2) }, Draw(0, 0, 4, 2));
    }

    [Fact]
    public void Line_pixels_depend_on_the_direction_like_the_original()
    {
        Assert.Equal(new HashSet<(int, int)> { (4, 2), (3, 1), (2, 1), (1, 0), (0, 0) }, Draw(4, 2, 0, 0));
        Assert.NotEqual(Draw(0, 0, 4, 2), Draw(4, 2, 0, 0));
    }

    [Fact]
    public void Steep_line_steps_x_on_carry()
    {
        Assert.Equal(new HashSet<(int, int)> { (0, 0), (0, 1), (1, 2), (1, 3), (2, 4), (2, 5) }, Draw(0, 0, 2, 5));
        Assert.Equal(new HashSet<(int, int)> { (0, 0), (0, -1), (-1, -2), (-1, -3), (-2, -4), (-2, -5) }, Draw(0, 0, -2, -5));
    }

    [Fact]
    public void Axis_aligned_and_diagonal_lines()
    {
        Assert.Equal(new HashSet<(int, int)> { (3, 1), (4, 1), (5, 1) }, Draw(5, 1, 3, 1));
        Assert.Equal(new HashSet<(int, int)> { (2, 7), (2, 8), (2, 9) }, Draw(2, 9, 2, 7));
        Assert.Equal(new HashSet<(int, int)> { (0, 0), (1, -1), (2, -2) }, Draw(0, 0, 2, -2));
        Assert.Equal(new HashSet<(int, int)> { (7, 7) }, Draw(7, 7, 7, 7));
    }

    [Fact]
    public void Axis_aligned_lines_are_clamped_to_the_clip()
    {
        Assert.Equal(new HashSet<(int, int)> { (10, 5), (11, 5), (12, 5) }, Draw(0, 5, 50, 5, 10, 0, 12, 20));
        Assert.Empty(Draw(0, 30, 50, 30, 10, 0, 12, 20));
    }

    [Fact]
    public void Rejected_lines_return_2_and_clipped_lines_return_1()
    {
        var clip = new RasterClip(new IndexedSurface(50, 50), 10, 10, 20, 20);
        Assert.Equal(2, RasterPrimitives.DrawClippedLine(clip, 0, 0, 5, 3, 1));
        Assert.Equal(1, RasterPrimitives.DrawClippedLine(clip, 0, 12, 30, 18, 1));
        Assert.Equal(0, RasterPrimitives.DrawClippedLine(clip, 11, 12, 19, 18, 1));
    }

    [Fact]
    public void Clipped_lines_draw_exactly_the_unclipped_pixels_inside_the_clip()
    {
        // The asm clips by computing the first/last DDA pixel on each clip edge from the original
        // start point, so clipping never changes which pixels are drawn.
        var random = new Random(1234);
        const int Cl = 10, Ct = 5, Cr = 60, Cb = 40;
        var full = new IndexedSurface(200, 180, -65, -65);
        var part = new IndexedSurface(200, 180, -65, -65);
        var smallClip = new RasterClip(part, Cl, Ct, Cr, Cb);
        for (int i = 0; i < 5000; i++)
        {
            int x1 = random.Next(-60, 130), y1 = random.Next(-60, 110);
            int x2 = random.Next(-60, 130), y2 = random.Next(-60, 110);
            full.Clear(0);
            part.Clear(0);
            RasterPrimitives.DrawClippedLine(RasterClip.ForSurface(full), x1, y1, x2, y2, 1);
            RasterPrimitives.DrawClippedLine(smallClip, x1, y1, x2, y2, 1);
            for (int y = full.Top; y <= full.Bottom; y++)
            {
                int row = (y - full.Top) * full.Width;
                for (int x = full.Left; x <= full.Right; x++)
                {
                    bool inside = x >= Cl && x <= Cr && y >= Ct && y <= Cb;
                    byte expected = inside ? full.Pixels[row + x - full.Left] : (byte)0;
                    if (part.Pixels[row + x - full.Left] != expected)
                        Assert.Fail($"line ({x1},{y1})-({x2},{y2}) differs at ({x},{y})");
                }
            }
        }
    }

    [Fact]
    public void Viewport_line_uses_absolute_coordinates()
    {
        var gfx = new GraphicsContext();
        var vp = Viewport.Allocate(100, 100, 109, 109);
        gfx.DrawViewportLine(vp, 100, 100, 109, 100, 3);
        Assert.Equal(10, vp.Surface!.Pixels.Take(10).Count(p => p == 3));
    }
}
