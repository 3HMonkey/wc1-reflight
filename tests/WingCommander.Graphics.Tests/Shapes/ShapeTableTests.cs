using WingCommander.Graphics.Shapes;
using WingCommander.Core.Resources;

namespace WingCommander.Graphics.Tests.Shapes;

using WingCommander.Tests;

public class ShapeTableTests
{
    [DataFact]
    public void Arrow_vga_has_three_frames_with_known_extents()
    {
        // Verified against the raw bytes in docs/analysis/resources.md §1.3.
        var packet = GameData.Require().OpenPacket("ARROW.VGA");
        Assert.Equal(1, packet.SectionCount);
        Assert.True(packet.IsNestedPacket(0));
        var shape = ShapeTable.FromSection(packet, 0);
        Assert.Equal(3, shape.FrameCount);
        var e = shape.GetExtents(0);
        Assert.Equal(new ShapeExtents(8, 1, 2, 11), e);
        Assert.Equal(10, e.Width);
        Assert.Equal(14, e.Height);
    }

    [DataFact]
    public void Title_vga_sections_are_shape_tables()
    {
        var packet = GameData.Require().OpenPacket(LogicalFile.TitleVga);
        Assert.Equal(18, packet.SectionCount);
        Assert.Equal(3, ShapeTable.FromSection(packet, 0).FrameCount);
        Assert.Equal(60, ShapeTable.FromSection(packet, 1).FrameCount);
        for (int s = 0; s < packet.SectionCount; s++)
        {
            var shape = ShapeTable.FromSection(packet, s);
            for (int f = 0; f < shape.FrameCount; f++)
            {
                if (shape.IsFrameEmpty(f))
                    continue;
                var e = shape.GetExtents(f);
                Assert.InRange(e.Width, 1, 320);
                Assert.InRange(e.Height, 1, 200);
            }
        }
    }
}
