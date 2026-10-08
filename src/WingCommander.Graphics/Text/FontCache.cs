using WingCommander.Core.Resources;

namespace WingCommander.Graphics.Text;

/// <summary>
/// The four text fonts of FONTS.FNT (logical file 0), loaded on demand. Font 1 (the VDU /
/// cockpit font, loaded with flag 0x10 in the original) is never released.
/// </summary>
/// <remarks>C: apTextFonts[4] with the loading part of InitializeTextContextFromFont
/// (0x41D510, disk.c) and ReleaseTextFont (0x41D590, disk.c). AllocateFontWorkspace /
/// FreeFontWorkspace allocate an unused scratch bitmap and are dropped.</remarks>
public sealed class FontCache
{
    /// <summary>Number of fonts in FONTS.FNT.</summary>
    public const int FontCount = 4;

    private readonly Func<int, ReadOnlyMemory<byte>> _loadSection;
    private readonly BitmapFont?[] _fonts = new BitmapFont?[FontCount];

    /// <param name="loadSection">Returns the decoded FONTS.FNT section for a font index.</param>
    public FontCache(Func<int, ReadOnlyMemory<byte>> loadSection)
    {
        ArgumentNullException.ThrowIfNull(loadSection);
        _loadSection = loadSection;
    }

    /// <summary>Fonts loaded from the game's FONTS.FNT through the install table.</summary>
    public static FontCache FromGameDirectory(GameDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        return new FontCache(index => directory.LoadSection(LogicalFile.Fonts, index));
    }

    /// <summary>True when the font is currently loaded.</summary>
    public bool IsLoaded(int fontIndex) => (uint)fontIndex < FontCount && _fonts[fontIndex] is not null;

    /// <summary>Returns the font, loading it first when needed.</summary>
    public BitmapFont Get(int fontIndex)
    {
        if ((uint)fontIndex >= FontCount)
            throw new ArgumentOutOfRangeException(nameof(fontIndex), fontIndex, "FONTS.FNT has four fonts.");
        return _fonts[fontIndex] ??= BitmapFont.Parse($"FONTS.FNT[{fontIndex}]", _loadSection(fontIndex), fontIndex);
    }

    /// <summary>Drops a loaded font (font 1 stays resident).</summary>
    /// <remarks>C: ReleaseTextFont (0x41D590, disk.c).</remarks>
    public void ReleaseTextFont(int fontIndex)
    {
        if (fontIndex == 1 || (uint)fontIndex >= FontCount)
            return;
        _fonts[fontIndex] = null;
    }
}
