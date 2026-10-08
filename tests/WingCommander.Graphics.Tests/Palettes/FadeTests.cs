using WingCommander.Core.Video;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;

namespace WingCommander.Graphics.Tests.Palettes;

public class FadeTests
{
    [Fact]
    public void Transition_moves_by_four_and_finishes_after_max_delta_over_four_steps()
    {
        var transition = new PaletteTransition();
        short[] current = [0];
        short[] target = [12];
        var values = new List<short>();
        while (transition.Step(current, target))
            values.Add(current[0]);
        Assert.Equal(new short[] { 4, 8, 12 }, values);
        Assert.False(transition.IsActive);
    }

    [Fact]
    public void Transition_spreads_smaller_deltas_and_stops_short_of_the_target()
    {
        // deltas 12 (+4) and 6 (-4): the second component moves on steps 2 only and ends 2 short.
        var transition = new PaletteTransition();
        short[] current = [0, 10];
        short[] target = [12, 4];
        var steps = new List<(short, short)>();
        while (transition.Step(current, target))
            steps.Add((current[0], current[1]));
        Assert.Equal(new[] { ((short)4, (short)10), ((short)8, (short)6), ((short)12, (short)6) }, steps);
    }

    [Fact]
    public void Transition_with_zero_delta_finishes_immediately_and_reinitialises()
    {
        var transition = new PaletteTransition();
        short[] current = [7, 7];
        Assert.False(transition.Step(current, [7, 7]));
        Assert.Equal(new short[] { 7, 7 }, current);
        Assert.True(transition.Step(current, [11, 7]));
        Assert.Equal(new short[] { 11, 7 }, current);
    }

    [Fact]
    public void Fade_to_colour_steps_every_active_entry_and_leaves_the_saved_palette_alone()
    {
        var palette = new GamePalette(new Palette());
        palette.CachePaletteEntry(1, 40, 20, 0);
        palette.CachePaletteEntry(2, 0, 0, 255);
        palette.SaveGamePalette();
        FadeToColour fade = FadeToColour.Begin(palette, 0);
        Assert.Equal(2, fade.ActiveCount);
        int steps = 0;
        var blue = new List<byte>();
        while (fade.Step())
        {
            steps++;
            blue.Add(palette.GetPaletteEntry(2).B);
        }
        Assert.Equal(255 / 4, steps);
        Assert.True(fade.IsFinished);
        Assert.False(fade.Step());
        Assert.Equal(3, palette.GetPaletteEntry(2).B);       // 255 - 63*4: up to 3 short of black
        Assert.Equal(((byte)0, (byte)0, (byte)0), palette.GetPaletteEntry(1));
        Assert.Equal(251, blue[0]);
        Assert.Equal(new byte[] { 40, 20, 0 }, palette.Saved.Slice(3, 3).ToArray());
        palette.RestoreGamePalette();
        Assert.Equal(((byte)40, (byte)20, (byte)0), palette.GetPaletteEntry(1));
    }

    [Fact]
    public void Fade_uploads_stale_scratch_values_for_inactive_entries()
    {
        // abPaletteTriplets keeps the last faded values of entries that were active earlier.
        var palette = new GamePalette(new Palette());
        palette.CachePaletteEntry(9, 100, 0, 0);
        FadeToColour first = FadeToColour.Begin(palette, 9 + 1); // fade entry 9 towards black (entry 10)
        while (first.Step())
        {
        }
        Assert.Equal(0, palette.GetPaletteEntry(9).R);
        palette.CachePaletteEntry(9, 0, 0, 0);
        palette.CachePaletteEntry(20, 8, 8, 8);
        palette.Triplets[9 * 3] = 77; // simulate a stale value from an earlier fade
        FadeToColour second = FadeToColour.Begin(palette, 0);
        Assert.True(second.Step());
        Assert.Equal(77, palette.GetPaletteEntry(9).R);
    }

    [Fact]
    public void Pan_to_screen_flattens_the_palette_copies_the_picture_and_fades_back()
    {
        var gfx = new GraphicsContext(new Framebuffer(), new Palette(), null);
        gfx.Palette.CachePaletteEntry(1, 200, 100, 40);
        gfx.Palette.CachePaletteEntry(2, 4, 8, 12);
        var scene = Viewport.Allocate(0, 0, 319, 199, 2);
        PanToScreenFade pan = gfx.BeginPanToScreen(scene, gfx.Screen!);
        // destination top-left pixel is 0 (black): both active entries are flattened to black
        Assert.Equal(((byte)0, (byte)0, (byte)0), gfx.Palette.GetPaletteEntry(1));
        Assert.Equal(((byte)0, (byte)0, (byte)0), gfx.Palette.GetPaletteEntry(2));
        Assert.Equal(2, gfx.Screen!.Surface![100, 100]); // picture copied
        Assert.True(gfx.ScreenDirty);
        int steps = 0;
        while (pan.Step())
            steps++;
        Assert.Equal(200 / 4, steps);
        Assert.Equal(((byte)200, (byte)100, (byte)40), gfx.Palette.GetPaletteEntry(1));
        Assert.Equal(((byte)4, (byte)8, (byte)12), gfx.Palette.GetPaletteEntry(2));
        // the saved mirror follows every cached write
        Assert.Equal(new byte[] { 200, 100, 40 }, gfx.Palette.Saved.Slice(3, 3).ToArray());
    }

    [Fact]
    public void Pan_to_screen_ends_up_to_three_short_of_odd_targets()
    {
        var gfx = new GraphicsContext(new Framebuffer(), new Palette(), null);
        gfx.Palette.CachePaletteEntry(1, 203, 0, 0);
        PanToScreenFade pan = gfx.BeginPanToScreen(Viewport.Allocate(0, 0, 9, 9, 1), gfx.Screen!);
        while (pan.Step())
        {
        }
        Assert.Equal(200, gfx.Palette.GetPaletteEntry(1).R);
    }
}
