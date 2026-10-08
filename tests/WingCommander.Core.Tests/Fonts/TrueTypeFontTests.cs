using System.Numerics;
using WingCommander.Core.Fonts;
using WingCommander.Tests;

namespace WingCommander.Core.Tests.Fonts;

public sealed class TrueTypeFontTests
{
    [Fact]
    public void SpaceWingLeader_Metrics_MatchTheFontTables()
    {
        var font = TrueTypeFont.Load(RepositoryAssets.SpaceWingLeader);
        Assert.Equal(2048, font.UnitsPerEm);
        Assert.Equal(1575, font.CapHeight);
        Assert.Equal(1260, font.XHeight);
        Assert.Equal(-472, font.Descender);
    }

    [Fact]
    public void SpaceWingLeader_H_HasItsOutlineAndAdvance()
    {
        var font = TrueTypeFont.Load(RepositoryAssets.SpaceWingLeader);
        TrueTypeGlyph? glyph = font.GetGlyphForCodePoint('H');
        Assert.NotNull(glyph);
        Assert.False(glyph.IsEmpty);
        Assert.Equal(2284, glyph.AdvanceWidth);
        // Straight lines only: the polygon corners are the font's points (fontTools: 0 0 1969 1575).
        Assert.Equal(0f, glyph.XMin);
        Assert.Equal(0f, glyph.YMin);
        Assert.Equal(1969f, glyph.XMax);
        Assert.Equal(1575f, glyph.YMax);
    }

    [Fact]
    public void SpaceWingLeader_CoversPrintableAscii_AndHasNoTilde0x7F()
    {
        var font = TrueTypeFont.Load(RepositoryAssets.SpaceWingLeader);
        for (int c = 0x20; c < 0x7F; c++)
            Assert.True(font.TryGetGlyphIndex(c, out _), $"missing 0x{c:X2}");
        Assert.False(font.TryGetGlyphIndex(0x7F, out _));
        Assert.True(font.GetGlyphForCodePoint(' ')!.IsEmpty);
    }

    [Fact]
    public void Chawp_CurvesAreFlattened_InsideTheControlBox()
    {
        var font = TrueTypeFont.Load(RepositoryAssets.Chawp);
        Assert.Equal(1000, font.UnitsPerEm);
        Assert.Equal(850, font.CapHeight);
        TrueTypeGlyph glyph = font.GetGlyphForCodePoint('A')!;
        Assert.Equal(664, glyph.AdvanceWidth);
        // fontTools reports the control box 0 -35 603 831; the flattened curves stay inside it.
        Assert.InRange(glyph.XMin, 0f, 20f);
        Assert.InRange(glyph.XMax, 580f, 603f);
        Assert.InRange(glyph.YMin, -35f, -10f);
        Assert.InRange(glyph.YMax, 800f, 831f);
        Assert.Equal(11, glyph.Contours.Count);
        Assert.All(glyph.Contours, contour => Assert.True(contour.Length >= 3));
    }

    [Fact]
    public void CffFonts_AreRejectedWithAClearMessage()
    {
        byte[] data = new byte[64];
        "OTTO"u8.CopyTo(data);
        var error = Assert.Throws<NotSupportedException>(() => TrueTypeFont.Load(data));
        Assert.Contains("TrueType", error.Message);
    }

    [Fact]
    public void Contours_AreClosedLoopsWithoutDuplicateEndPoint()
    {
        var font = TrueTypeFont.Load(RepositoryAssets.Chawp);
        foreach (char c in "O08")
        {
            foreach (Vector2[] contour in font.GetGlyphForCodePoint(c)!.Contours)
                Assert.NotEqual(contour[0], contour[^1]);
        }
    }
}
