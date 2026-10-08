using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;

namespace WingCommander.Graphics;

public sealed partial class GraphicsContext
{
    /// <summary>
    /// Starts fading every non-black palette entry to the colour of entry
    /// <paramref name="colourIndex"/>; drive it with <see cref="FadeToColour.Step"/>, awaiting a
    /// vertical blank between steps, and present the screen afterwards. The viewport argument is
    /// ignored, as in the original.
    /// </summary>
    /// <remarks>C: FadeViewportPaletteToColour (0x42A700, hudmsg.c), split into a step object
    /// (ADR-009: the library never waits or presents).</remarks>
    public FadeToColour BeginFadeViewportPaletteToColour(Viewport? viewport, int colourIndex)
    {
        _ = viewport;
        return FadeToColour.Begin(Palette, colourIndex);
    }

    /// <summary>
    /// "Fade in" a new picture: every non-black entry is set to the colour of the destination's
    /// current top-left pixel, then the source is copied to the destination. The caller then
    /// awaits a vertical blank, presents, runs <see cref="PanToScreenFade.Step"/> with a vertical
    /// blank between steps, and presents again:
    /// <code>
    /// var pan = gfx.BeginPanToScreen(sceneBuffer, screen);
    /// await scheduler.VerticalBlank();
    /// await PresentScreen();
    /// while (pan.Step()) await scheduler.VerticalBlank();
    /// await PresentScreen();
    /// </code>
    /// </summary>
    /// <remarks>C: PanToScreen (0x439430, screens.c), split into set-up + step object.</remarks>
    public PanToScreenFade BeginPanToScreen(Viewport source, Viewport destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        int flatColour = GetViewportPixel(destination, destination.Left, destination.Top);
        PanToScreenFade fade = PanToScreenFade.Begin(Palette, flatColour);
        CopyViewportContents(source, destination);
        return fade;
    }

    /// <summary>
    /// Startup palette: GAME.PAL as the whole palette, the cockpit light entries reset
    /// (185..190 black, 191 = (0, 0, 32)) and the result saved as the game palette.
    /// </summary>
    /// <remarks>C: LoadGamePaletteFile (0x4219C0, logic.c), VGA branch.</remarks>
    public void LoadGamePaletteFile(ReadOnlySpan<byte> gamePalFile)
    {
        Palette.LoadPaletteTripletsFile(gamePalFile);
        FlightPalette.ResetCockpitPaletteEntries(Palette);
        Palette.SaveGamePalette();
    }
}
