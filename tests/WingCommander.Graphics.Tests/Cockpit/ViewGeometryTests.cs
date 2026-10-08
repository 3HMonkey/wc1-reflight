using WingCommander.Core.Resources;
using WingCommander.Core.Video;
using WingCommander.Graphics.Cockpit;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Tests;

namespace WingCommander.Graphics.Tests.Cockpit;

public class ViewGeometryTests
{
    [DataFact]
    public void Pcship_v00_has_the_documented_four_views()
    {
        PacketFile packet = GameData.Require().OpenPacket("PCSHIP.V00");
        var set = ViewGeometrySet.Parse(packet.Name, packet.GetSection(ViewGeometrySet.PcShipSection).Span);
        Assert.Equal(0xBB80, set.BufferSize);
        Assert.Equal(4, set.Count);
        var expected = new (int W, int H, int X, int Y, int Runs)[]
        {
            (320, 105, 0, 10, 105), (313, 150, 0, 0, 213), (320, 150, 0, 0, 213), (317, 50, 0, 19, 78),
        };
        for (int v = 0; v < 4; v++)
        {
            ViewGeometry g = set[v];
            Assert.Equal(expected[v], (g.Width, g.Height, g.OriginX, g.OriginY, g.Runs.Length));
            Assert.True(g.Width * g.Height <= set.BufferSize);
        }
        Assert.Equal(new ViewRun(71, 10, 57), set[0].Runs[0]);
        Assert.Equal(new ViewRun(0, 19, 11840), set[0].Runs.ToArray().First(r => r.Length > 320)); // spans 37 rows
    }

    [DataFact]
    public void Every_pcship_file_parses_and_v04_has_a_single_view()
    {
        GameDirectory data = GameData.Require();
        foreach (string path in Directory.EnumerateFiles(data.DataPath, "PCSHIP.V*"))
        {
            PacketFile packet = data.OpenPacket(Path.GetFileName(path));
            var set = ViewGeometrySet.Parse(packet.Name, packet.GetSection(ViewGeometrySet.PcShipSection).Span);
            Assert.InRange(set.Count, 1, 4);
            for (int v = 0; v < set.Count; v++)
            {
                foreach (ViewRun run in set[v].Runs)
                {
                    Assert.InRange(run.ScreenY, 0, 199);
                    Assert.True(run.ScreenY * 320 + run.DestinationX + run.Length <= 64000, $"{packet.Name} view {v}");
                }
            }
            if (packet.Name.Equals("PCSHIP.V04", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal(1, set.Count);
                Assert.Equal((232, 81, 40, 29, 91), (set[0].Width, set[0].Height, set[0].OriginX, set[0].OriginY, set[0].Runs.Length));
            }
            else
            {
                Assert.Equal(4, set.Count);
            }
        }
    }

    [Fact]
    public void Fizzle_fade_copies_runs_linearly_from_the_space_buffer()
    {
        var framebuffer = new Framebuffer();
        var gfx = new GraphicsContext(framebuffer, new Palette(), null);
        var space = Viewport.Allocate(0, 0, 319, 9);
        for (int i = 0; i < space.Surface!.Pixels.Length; i++)
            space.Surface.Pixels[i] = (byte)(i % 251 + 1);
        // origin (0, 50): buffer row r shows at screen row 50 + r. One run continues into the next row.
        var geometry = new ViewGeometry(320, 10, 0, 50, [new ViewRun(10, 51, 5), new ViewRun(318, 53, 4)]);
        gfx.FizzleFade(space, gfx.Screen!, geometry);
        Assert.True(gfx.ScreenDirty);
        for (int x = 10; x < 15; x++)
            Assert.Equal(space.Surface[x, 1], framebuffer[x, 51]);
        Assert.Equal(space.Surface[318, 3], framebuffer[318, 53]);
        Assert.Equal(space.Surface[319, 3], framebuffer[319, 53]);
        Assert.Equal(space.Surface[0, 4], framebuffer[0, 54]);   // wrapped to the next row in both buffers
        Assert.Equal(space.Surface[1, 4], framebuffer[1, 54]);
        Assert.Equal(9, framebuffer.Pixels.Count(p => p != 0));
    }

    [Fact]
    public void Built_in_modes_cover_their_whole_buffer()
    {
        Assert.Equal(320 * 128, ViewGeometry.Mode4.Runs[0].Length);
        Assert.Equal(24, ViewGeometry.Mode4.OriginY);
        Assert.Equal(64000, ViewGeometry.Mode5.Runs[0].Length);
        var framebuffer = new Framebuffer();
        var gfx = new GraphicsContext(framebuffer, new Palette(), null);
        var space = Viewport.Allocate(0, 0, 319, 127, 9);
        gfx.FizzleFade(space, gfx.Screen!, ViewGeometry.Mode4);
        Assert.Equal(320 * 128, framebuffer.Pixels.Count(p => p == 9));
        Assert.Equal(9, framebuffer[0, 24]);
        Assert.Equal(9, framebuffer[319, 151]);
        Assert.Equal(0, framebuffer[0, 23]);
        Assert.Equal(0, framebuffer[0, 152]);
    }

    [DataFact]
    public void Cockpit_view_mask_only_touches_the_window()
    {
        // Draw the cockpit art, then composite a uniform space buffer through view 0: every
        // changed pixel must have been a pixel the runs cover, and the art elsewhere survives.
        PacketFile packet = GameData.Require().OpenPacket("PCSHIP.V00");
        var set = ViewGeometrySet.Parse(packet.Name, packet.GetSection(ViewGeometrySet.PcShipSection).Span);
        var framebuffer = new Framebuffer();
        var gfx = new GraphicsContext(framebuffer, new Palette(), null);
        var cockpit = ShapeTable.FromSection(packet, 0);
        gfx.DrawSpriteDefault(gfx.Screen!, 0, 0, cockpit, 0);
        byte[] art = framebuffer.Pixels.ToArray();
        ViewGeometry view = set[0];
        var space = Viewport.Allocate(0, 0, view.Width - 1, view.Height - 1, 0xEE);
        gfx.FizzleFade(space, gfx.Screen!, view);
        var covered = new bool[64000];
        foreach (ViewRun run in view.Runs)
            for (int i = 0; i < run.Length; i++)
                covered[run.ScreenY * 320 + run.DestinationX + i] = true;
        for (int i = 0; i < 64000; i++)
            Assert.Equal(covered[i] ? 0xEE : art[i], framebuffer.Pixels[i]);
        Assert.True(covered.Count(c => c) > 20000);
    }
}
