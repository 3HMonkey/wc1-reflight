using WingCommander.Core.Rendering;

namespace WingCommander.Core.Tests.Rendering;

/// <summary>R2b: sprites drawn between two simulation ticks.</summary>
public class SpriteInterpolationTests
{
    private static SpriteInstance Moving() => new(SpriteImageKey.Create(1, 2, 3), 100f, 50f)
    {
        Angle = 10f,
        Scale = 2f,
        HasPrevious = true,
        PreviousX = 80f,
        PreviousY = 60f,
        PreviousAngle = 350f,
        PreviousScale = 1f,
    };

    [Fact]
    public void Position_and_scale_move_linearly_and_the_angle_turns_the_short_way()
    {
        SpriteInstance half = Moving().At(0.5f);
        Assert.Equal(90f, half.X, 4);
        Assert.Equal(55f, half.Y, 4);
        Assert.Equal(1.5f, half.Scale, 4);
        Assert.Equal(360f, half.Angle, 4); // 350 -> 10 through 0, not back through 180
        Assert.Equal(Moving().Image, half.Image);

        SpriteInstance start = Moving().At(0f);
        Assert.Equal(80f, start.X, 4);
        Assert.Equal(350f, start.Angle, 4);
    }

    [Fact]
    public void The_latest_tick_or_no_previous_state_draws_the_sprite_as_recorded()
    {
        SpriteInstance sprite = Moving();
        Assert.Equal(sprite, sprite.At(1f));
        Assert.Equal(sprite, sprite.At(1.5f));
        sprite.HasPrevious = false;
        Assert.Equal(sprite, sprite.At(0.25f));
        Assert.Equal(80f, Moving().At(-1f).X, 4); // clamped to the previous tick
    }

    [Fact]
    public void Stretched_pixels_keep_their_vertical_scale()
    {
        var line = new SpriteInstance(SpriteImageKey.Create(-1, 0, 42), 10f, 20f)
        {
            Scale = 1f,
            ScaleY = 12f,
            HasPrevious = true,
            PreviousX = 10f,
            PreviousY = 18f,
            PreviousScale = 1f,
            PreviousScaleY = 8f,
        };
        Assert.Equal(12f, line.VerticalScale);
        Assert.Equal(10f, line.At(0.5f).VerticalScale, 4);
        Assert.Equal(2f, new SpriteInstance { Scale = 2f }.VerticalScale); // 0 = the uniform scale
    }

    [Fact]
    public void The_display_moves_over_one_tick_after_the_present()
    {
        var view = new SpaceView(new SpriteImageCache()) { PresentedAt = 1000, TickMilliseconds = 50 };
        Assert.Equal(0f, view.InterpolationAt(1000));
        Assert.Equal(0.5f, view.InterpolationAt(1025), 4);
        Assert.Equal(1f, view.InterpolationAt(1050));
        Assert.Equal(1f, view.InterpolationAt(2000));
        Assert.Equal(0f, view.InterpolationAt(900));
        Assert.Equal(1f, new SpaceView(new SpriteImageCache()).InterpolationAt(5)); // no timing: the tick itself
        Assert.Equal(1f, new RenderFrame(new ClassicLayer(new Video.Framebuffer(), new Video.Palette())).Interpolation);
    }
}
