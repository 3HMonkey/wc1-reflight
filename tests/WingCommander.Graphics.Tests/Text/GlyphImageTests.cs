using WingCommander.Core.Fonts;
using WingCommander.Core.Rendering;
using WingCommander.Graphics.Text;
using WingCommander.Tests;

namespace WingCommander.Graphics.Tests.Text;

public class GlyphImageTests
{
    private static FontCache Fonts() => FontCache.FromGameDirectory(GameData.Require());

    [DataFact]
    public void VectorizedGlyph_KeepsTheCell_AndCoversTheOriginalStrokes()
    {
        BitmapFont font = Fonts().Get(0);
        GlyphImage image = GlyphImageBuilder.Build(font, (byte)'H');
        Assert.Equal(font.GetWidth((byte)'H'), image.Width);
        Assert.Equal(font.Height, image.Height);
        Assert.Equal(font.GetWidth((byte)'H'), image.Advance);
        Assert.True(image.HasForeground);
        Assert.False(image.Multicolour);
        // Every ink pixel centre of the original is inside the outline, every far background pixel outside.
        ReadOnlySpan<byte> glyph = font.GetGlyph((byte)'H');
        for (int y = 0; y < font.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                byte value = glyph[y * image.Width + x];
                float coverage = GlyphRasterizer.Coverage(image, x + 0.5f, y + 0.5f, texelsPerPixel: 1f);
                if (value == font.InkIndex)
                    Assert.True(coverage > 0.5f, $"ink pixel ({x},{y}) lost");
            }
        }
    }

    [DataFact]
    public void ChalkFont_IsMulticolour()
    {
        BitmapFont font = Fonts().Get(3);
        Assert.True(GlyphImageBuilder.Build(font, (byte)'A').Multicolour);
        Assert.False(GlyphImageBuilder.Build(font, (byte)' ').HasForeground);
    }

    [DataFact]
    public void Replacement_FitsSpaceWingLeaderIntoFont1()
    {
        BitmapFont font = Fonts().Get(1);
        var replacement = new FontReplacement(font, TrueTypeFont.Load(RepositoryAssets.SpaceWingLeader), "SPACE WING LEADER");
        Assert.Equal(6, replacement.Baseline);
        Assert.Equal(6, replacement.CapHeight);
        Assert.InRange(replacement.Condense, 0.6f, 0.9f);

        GlyphImage image = replacement.Build((byte)'H')!;
        Assert.Equal(font.GetWidth((byte)'H'), image.Width);
        Assert.Equal(font.Height, image.Height);
        Assert.Equal(font.InkIndex, image.InkIndex);
        // The left stem of H starts at the original ink and the letter sits on the baseline.
        Assert.True(GlyphRasterizer.Coverage(image, 1.6f, 3f, 1f) > 0.5f);
        Assert.True(GlyphRasterizer.Coverage(image, 3f, 6.5f, 1f) < 0.5f);
        Assert.True(GlyphRasterizer.Coverage(image, 1.6f, 0.2f, 1f) > 0.5f); // cap height reaches row 0
    }

    [DataFact]
    public void Replacement_KeepsTheOriginalForSymbolsAndMissingGlyphs()
    {
        BitmapFont font = Fonts().Get(2);
        var replacement = new FontReplacement(font, TrueTypeFont.Load(RepositoryAssets.SpaceWingLeader), "SPACE WING LEADER");
        Assert.Null(replacement.Build(0xF3)); // gauge symbol
        Assert.Null(replacement.Build((byte)' '));
        Assert.NotNull(replacement.Build((byte)'k'));
    }

    [DataFact]
    public void GlyphImageSource_UsesTheReplacement_AndFallsBack()
    {
        FontCache fonts = Fonts();
        var source = new GlyphImageSource(new GlyphImageCache());
        source.SetReplacement(2, TrueTypeFont.Load(RepositoryAssets.SpaceWingLeader), "SPACE WING LEADER");
        BitmapFont font2 = fonts.Get(2);
        GlyphImage letter = source.Get(font2, (byte)'A');
        GlyphImage symbol = source.Get(font2, 0xF3);
        Assert.Same(letter, source.Get(font2, (byte)'A'));
        Assert.NotEqual(GlyphImageBuilder.Build(font2, (byte)'A').Texels.ToArray(), letter.Texels.ToArray());
        Assert.Equal(GlyphImageBuilder.Build(font2, 0xF3).Texels.ToArray(), symbol.Texels.ToArray());

        int generation = source.Cache.Generation;
        source.SetReplacement(2, null);
        Assert.NotEqual(generation, source.Cache.Generation);
        Assert.Equal(GlyphImageBuilder.Build(font2, (byte)'A').Texels.ToArray(), source.Get(font2, (byte)'A').Texels.ToArray());
    }

    [DataFact]
    public void Replacement_TekturKeepsNearlyItsProportionsInFont0()
    {
        BitmapFont font = Fonts().Get(0);
        var replacement = new FontReplacement(font, TrueTypeFont.Load(RepositoryAssets.Tektur), "Tektur");
        Assert.Equal(9, replacement.Baseline);
        Assert.Equal(9, replacement.CapHeight);
        Assert.InRange(replacement.Condense, 0.85f, 1f);
        Assert.NotNull(replacement.Build((byte)'g'));
    }

    [DataFact]
    public void Replacement_ChawpFillsTheChalkBoardCells()
    {
        BitmapFont font = Fonts().Get(3);
        var replacement = new FontReplacement(font, TrueTypeFont.Load(RepositoryAssets.Chawp), "CHAWP");
        Assert.Equal(10, replacement.Baseline);
        Assert.Equal(10, replacement.CapHeight);
        Assert.Equal(1f, replacement.Condense);
        Assert.True(replacement.Build((byte)'M')!.HasForeground);
        Assert.Null(replacement.Build((byte)'m')); // the chalk font has no lower case
    }
}
