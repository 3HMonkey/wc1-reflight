using System.Numerics;

namespace WingCommander.Core.Rendering;

/// <summary>
/// CPU evaluation of <see cref="GlyphImage"/>s, the reference for the renderers' glyph shaders
/// (same sampling rules) and the preview in wc1tool: the distance field is sampled bilinearly
/// (texel centres at +0.5, clamped at the field border) and turned into coverage with a linear
/// ramp one output pixel wide. Single-colour glyphs take the text colour; multicolour glyphs
/// blend the palette colours of the four nearest source pixels (ink = text colour).
/// </summary>
public static class GlyphRasterizer
{
    /// <summary>Signed distance in field texels at a field position (texel units, (0,0) = field corner).</summary>
    public static float SampleDistance(GlyphImage image, float fieldX, float fieldY)
    {
        ArgumentNullException.ThrowIfNull(image);
        ReadOnlySpan<byte> texels = image.Texels.Span;
        int width = image.FieldWidth, height = image.FieldHeight;
        float x = fieldX - 0.5f, y = fieldY - 0.5f;
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        float d00 = Tap(texels, width, height, x0, y0, 0);
        float d10 = Tap(texels, width, height, x0 + 1, y0, 0);
        float d01 = Tap(texels, width, height, x0, y0 + 1, 0);
        float d11 = Tap(texels, width, height, x0 + 1, y0 + 1, 0);
        float top = d00 + (d10 - d00) * fx;
        float bottom = d01 + (d11 - d01) * fx;
        return GlyphImage.DecodeDistance(top + (bottom - top) * fy);
    }

    /// <summary>
    /// Foreground coverage 0..1 at a point of the glyph cell (source pixels, (0,0) = cell corner);
    /// <paramref name="texelsPerPixel"/> = field texels covered by one output pixel (anti-aliasing width).
    /// </summary>
    public static float Coverage(GlyphImage image, float cellX, float cellY, float texelsPerPixel)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!image.HasForeground)
            return 0f;
        float d = SampleDistance(image,
            GlyphImage.FieldPadding + cellX * GlyphImage.FieldScale,
            GlyphImage.FieldPadding + cellY * GlyphImage.FieldScale);
        return Math.Clamp(d / Math.Max(texelsPerPixel, 1e-3f) + 0.5f, 0f, 1f);
    }

    /// <summary>Palette index of source pixel (x, y), -1 &lt;= x &lt;= Width (clamped to the field).</summary>
    public static byte SourceColour(GlyphImage image, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(image);
        const int half = GlyphImage.FieldScale / 2;
        return Tap(image.Texels.Span, image.FieldWidth, image.FieldHeight,
            GlyphImage.FieldPadding + x * GlyphImage.FieldScale + half,
            GlyphImage.FieldPadding + y * GlyphImage.FieldScale + half, 1);
    }

    /// <summary>
    /// Colour (0..255 per channel) at a point of the glyph cell: the text colour for single-colour
    /// glyphs, otherwise the bilinear blend of the four nearest source pixels' palette colours.
    /// </summary>
    public static Vector3 SampleColour(GlyphImage image, float cellX, float cellY, byte textColour, ReadOnlySpan<byte> paletteRgb)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!image.Multicolour)
            return PaletteColour(paletteRgb, textColour);
        float x = cellX - 0.5f, y = cellY - 0.5f;
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        Vector3 c00 = Resolve(image, x0, y0, textColour, paletteRgb);
        Vector3 c10 = Resolve(image, x0 + 1, y0, textColour, paletteRgb);
        Vector3 c01 = Resolve(image, x0, y0 + 1, textColour, paletteRgb);
        Vector3 c11 = Resolve(image, x0 + 1, y0 + 1, textColour, paletteRgb);
        return Vector3.Lerp(Vector3.Lerp(c00, c10, fx), Vector3.Lerp(c01, c11, fx), fy);
    }

    private static Vector3 Resolve(GlyphImage image, int x, int y, byte textColour, ReadOnlySpan<byte> paletteRgb)
    {
        byte index = SourceColour(image, x, y);
        return PaletteColour(paletteRgb, index == image.InkIndex ? textColour : index);
    }

    private static Vector3 PaletteColour(ReadOnlySpan<byte> paletteRgb, byte index) =>
        new(paletteRgb[index * 3], paletteRgb[index * 3 + 1], paletteRgb[index * 3 + 2]);

    private static byte Tap(ReadOnlySpan<byte> texels, int width, int height, int x, int y, int channel)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        return texels[(y * width + x) * GlyphImage.BytesPerTexel + channel];
    }
}
