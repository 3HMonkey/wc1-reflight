using WingCommander.Core.Rendering;

namespace WingCommander.Core.Tests.Rendering;

public class PresentationLayoutTests
{
    [Fact]
    public void Four_by_three_letterboxes_a_wide_window()
    {
        var r = PresentationLayout.Compute(1920, 1080, AspectMode.FourByThree, integerScaling: false);
        Assert.Equal(new PresentationRect(240, 0, 1440, 1080), r);
    }

    [Fact]
    public void Square_pixels_fill_a_sixteen_by_ten_window()
    {
        var r = PresentationLayout.Compute(1280, 800, AspectMode.SquarePixels, integerScaling: false);
        Assert.Equal(new PresentationRect(0, 0, 1280, 800), r);
    }

    [Fact]
    public void Integer_scaling_uses_whole_multiples()
    {
        var r = PresentationLayout.Compute(1920, 1080, AspectMode.FourByThree, integerScaling: true);
        Assert.Equal(new PresentationRect(320, 60, 1280, 960), r);   // 4x of 320x240
    }

    [Fact]
    public void Mouse_mapping_round_trips_pixel_centres()
    {
        var r = PresentationLayout.Compute(1920, 1080, AspectMode.FourByThree, integerScaling: false);
        for (int y = 0; y < 200; y += 13)
        {
            for (int x = 0; x < 320; x += 17)
            {
                var (px, py) = PresentationLayout.FromFrame(r, x, y);
                Assert.Equal((x, y), PresentationLayout.ToFrame(r, px, py));
            }
        }
        Assert.Equal((0, 0), PresentationLayout.ToFrame(r, 0, 0));          // left bar clamps
        Assert.Equal((319, 199), PresentationLayout.ToFrame(r, 1919, 1079));
    }
}
