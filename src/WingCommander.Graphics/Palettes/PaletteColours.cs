namespace WingCommander.Graphics.Palettes;

/// <summary>Named GAME.PAL indices used by the game in VGA mode.</summary>
/// <remarks>C: the cXxxColour globals (include/globals.h); the EGA remapping in
/// LoadGamePaletteFile is not needed for VGA.</remarks>
public static class PaletteColours
{
    /// <remarks>C: cBlackColour.</remarks>
    public const byte Black = 0;

    /// <remarks>C: cDarkGreyColour.</remarks>
    public const byte DarkGrey = 7;

    /// <remarks>C: cLightGreyColour.</remarks>
    public const byte LightGrey = 0x0B;

    /// <remarks>C: cViewportClearColour.</remarks>
    public const byte ViewportClear = 15;

    /// <remarks>C: cBlueColour.</remarks>
    public const byte Blue = 0x25;

    /// <remarks>C: cDarkBlueColour.</remarks>
    public const byte DarkBlue = 0x27;

    /// <remarks>C: cYellowColour.</remarks>
    public const byte Yellow = 0x47;

    /// <remarks>C: cRedColour.</remarks>
    public const byte Red = 0x50;

    /// <remarks>C: cOrangeColour.</remarks>
    public const byte Orange = 0x85;

    /// <remarks>C: cPrimaryTextColour (also the ink of font 1).</remarks>
    public const byte PrimaryText = 0xA6;

    /// <remarks>C: cDarkGreenColour.</remarks>
    public const byte DarkGreen = 0xAA;

    /// <summary>Space background colour index (runtime colour (0, 0, 32)).</summary>
    /// <remarks>C: cPrimaryViewBufferColour.</remarks>
    public const byte PrimaryViewBuffer = 0xBF;

    /// <summary>The transparent index of shapes and glyphs.</summary>
    public const byte Transparent = 0xFF;
}
