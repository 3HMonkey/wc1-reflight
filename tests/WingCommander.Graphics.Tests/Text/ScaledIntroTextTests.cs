using WingCommander.Core.Resources;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Tests;

namespace WingCommander.Graphics.Tests.Text;

public class ScaledIntroTextTests
{
    private static ShapeTable IntroFont() => ShapeTable.FromSection(GameData.Require().OpenPacket(LogicalFile.TitleVga), 1);

    [DataFact]
    public void The_width_adds_each_letter_advance_and_the_scaled_space()
    {
        var font = IntroFont();
        var space = Viewport.Allocate(0, 0, 319, 127, 0);
        Span<short> b = stackalloc short[4];
        int expected = 0;
        foreach (char c in "AB C")
        {
            if (c == ' ')
            {
                expected += 6;
                continue;
            }
            ShapeBounds.GetTransformedShapeBounds(space, 0, 0, font, c - 'A', 0, 0x100, 0, b);
            expected += b[2] + 1 + 2;
        }
        Assert.Equal(expected, GraphicsContext.MeasureScaledIntroTextWidth(space, font, "AB C"u8, 0x100));
        Assert.Equal(GraphicsContext.MeasureScaledIntroTextWidth(space, font, "AB"u8, 0x100),
            GraphicsContext.MeasureScaledIntroTextWidth(space, font, "AB\nC"u8, 0x100));
    }

    [DataFact]
    public void The_drawn_line_is_centred_on_the_given_column()
    {
        var font = IntroFont();
        var gfx = new GraphicsContext();
        var space = Viewport.Allocate(0, 0, 319, 127, 0);
        gfx.DrawCenteredScaledIntroText(space, font, "WING COMMANDER"u8, 160, 80, 0x180);
        int left = int.MaxValue, right = int.MinValue;
        for (int y = 0; y <= 127; y++)
        {
            for (int x = 0; x <= 319; x++)
            {
                if (gfx.GetViewportPixel(space, x, y) > 0)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                }
            }
        }
        Assert.True(right > left, "nothing was drawn");
        Assert.InRange((left + right) / 2, 155, 165);
    }
}
