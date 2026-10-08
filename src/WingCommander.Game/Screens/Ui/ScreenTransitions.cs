using WingCommander.Graphics.Raster;

namespace WingCommander.Game.Screens.Ui;

/// <summary>Palette transitions with the original's vertical-blank and present points.</summary>
public static class ScreenTransitions
{
    /// <summary>
    /// "Fade in" a picture: every non-black palette entry takes the colour of the destination's
    /// top-left pixel, the source is copied to the destination and presented, then the entries
    /// step back to their colours, one step per vertical blank (entries end up to 3 short of the
    /// original values, as in the original), and the frame is presented again.
    /// </summary>
    /// <remarks>C: PanToScreen (0x439430, screens.c). The palette writes are visible at once, so the
    /// first vertical blank shows the old pixels in the flat colour like the original's
    /// DIBramPalette before the copy; inside the loop <see cref="Video.Display.PaletteChanged"/> is
    /// the DIBramPalette after every step.</remarks>
    public static async Task PanToScreenAsync(Wc1Game game, Viewport source, Viewport destination)
    {
        ArgumentNullException.ThrowIfNull(game);
        var display = game.Display;
        var pan = game.Graphics.BeginPanToScreen(source, destination);
        await display.WaitForVerticalBlankAsync();
        await display.PresentAsync();
        while (pan.Step())
        {
            await display.WaitForVerticalBlankAsync();
            display.PaletteChanged();
        }
        await display.PresentAsync();
    }
}
