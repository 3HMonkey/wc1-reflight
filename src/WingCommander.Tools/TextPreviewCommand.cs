using System.Globalization;
using WingCommander.Core.Fonts;
using WingCommander.Core.Imaging;
using WingCommander.Core.Rendering;
using WingCommander.Graphics;
using WingCommander.Graphics.Text;

namespace WingCommander.Tools;

internal static partial class Commands
{
    /// <summary>
    /// Renders a text (or the printable glyphs) of one font twice at the given magnification: the
    /// classic pixels (nearest) and the output-resolution glyphs of ADR-013, evaluated on the CPU
    /// with the renderer's sampling rules.
    /// </summary>
    private static int HdTextPreview(ToolOptions o)
    {
        int fontIndex = int.Parse(o.Positional(0), CultureInfo.InvariantCulture);
        string text = o.Positionals.Count > 1
            ? o.Positional(1)
            : " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLM\nNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";
        text = text.Replace("\\n", "\n", StringComparison.Ordinal);
        float scaleX = float.Parse(o.Option("scale") ?? "6", CultureInfo.InvariantCulture);
        float scaleY = o.Has("aspect") ? scaleX * 1.2f : scaleX;
        float smoothing = float.Parse(o.Option("smooth") ?? "0",
            CultureInfo.InvariantCulture);
        GraphicsContext graphics = CreateGraphics(o, withFonts: true);
        BitmapFont font = graphics.Fonts!.Get(fontIndex);
        byte colour = o.Option("colour") is { } c ? byte.Parse(c, CultureInfo.InvariantCulture) : font.InkIndex;
        FontReplacement? replacement = o.Option("ttf") is { } ttf
            ? new FontReplacement(font, TrueTypeFont.Load(ttf), Path.GetFileNameWithoutExtension(ttf))
            : null;
        if (replacement is not null)
        {
            Console.WriteLine($"replacement {replacement.Name}: baseline {replacement.Baseline}, cap height {replacement.CapHeight}, " +
                $"scale {replacement.Scale:0.#####} px/unit, condense {replacement.Condense:0.###}");
        }
        ReadOnlySpan<byte> palette = graphics.Palette.Live.Rgb;

        string[] lines = text.Split('\n');
        int sourceWidth = 0;
        foreach (string line in lines)
        {
            int w = 0;
            foreach (char ch in line)
                w += font.GetWidth((byte)ch);
            sourceWidth = Math.Max(sourceWidth, w);
        }
        int sourceHeight = lines.Length * font.Height;
        const int margin = 8;
        int blockWidth = (int)MathF.Ceiling(sourceWidth * scaleX);
        int blockHeight = (int)MathF.Ceiling(sourceHeight * scaleY);
        int width = blockWidth + 2 * margin;
        int height = blockHeight * 2 + 3 * margin;
        var rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            rgba[i * 4] = 0x18;
            rgba[i * 4 + 1] = 0x1C;
            rgba[i * 4 + 2] = 0x28;
            rgba[i * 4 + 3] = 0xFF;
        }

        var images = new Dictionary<byte, GlyphImage>();
        int cursorY = 0;
        foreach (string line in lines)
        {
            int cursorX = 0;
            foreach (char ch in line)
            {
                byte code = (byte)ch;
                int glyphWidth = font.GetWidth(code);
                ReadOnlySpan<byte> glyph = font.GetGlyph(code);
                if (!images.TryGetValue(code, out GlyphImage? image))
                    images[code] = image = replacement?.Build(code) ?? GlyphImageBuilder.Build(font, code, smoothing);

                // Classic: nearest-neighbour blocks of the translated glyph pixels.
                for (int y = 0; y < font.Height && glyph.Length >= glyphWidth * font.Height; y++)
                {
                    for (int x = 0; x < glyphWidth; x++)
                    {
                        byte value = glyph[y * glyphWidth + x];
                        if (value == 0xFF || value == font.BackgroundIndex)
                            continue;
                        if (value == font.InkIndex)
                            value = colour;
                        int x0 = margin + (int)MathF.Round((cursorX + x) * scaleX);
                        int x1 = margin + (int)MathF.Round((cursorX + x + 1) * scaleX);
                        int y0 = margin + (int)MathF.Round((cursorY + y) * scaleY);
                        int y1 = margin + (int)MathF.Round((cursorY + y + 1) * scaleY);
                        for (int py = y0; py < y1; py++)
                            for (int px = x0; px < x1; px++)
                                Blend(rgba, width, px, py, palette, value, 1f);
                    }
                }

                // Output resolution: coverage from the distance field, colour from the nearest texel.
                if (image.HasForeground)
                {
                    int top = 2 * margin + blockHeight;
                    int px0 = margin + (int)MathF.Floor(cursorX * scaleX);
                    int px1 = margin + (int)MathF.Ceiling((cursorX + glyphWidth) * scaleX);
                    int py0 = top + (int)MathF.Floor(cursorY * scaleY);
                    int py1 = top + (int)MathF.Ceiling((cursorY + font.Height) * scaleY);
                    float texelsPerPixel = ReferenceCompositor.TexelsPerPixel(scaleX, scaleY);
                    for (int py = py0; py < py1; py++)
                    {
                        for (int px = px0; px < px1; px++)
                        {
                            float cellX = (px + 0.5f - margin) / scaleX - cursorX;
                            float cellY = (py + 0.5f - top) / scaleY - cursorY;
                            if (cellX < 0 || cellY < 0 || cellX >= glyphWidth || cellY >= font.Height)
                                continue;
                            float coverage = GlyphRasterizer.Coverage(image, cellX, cellY, texelsPerPixel);
                            if (coverage <= 0f)
                                continue;
                            var rgb = GlyphRasterizer.SampleColour(image, cellX, cellY, colour, palette);
                            BlendRgb(rgba, width, px, py, rgb, coverage);
                        }
                    }
                }
                cursorX += glyphWidth;
            }
            cursorY += font.Height;
        }

        string file = o.Option("out") ?? $"hdtext{fontIndex}.png";
        Png.WriteRgba(file, width, height, rgba);
        Console.WriteLine($"font {fontIndex}: {lines.Length} line(s), scale {scaleX}x{scaleY}, smoothing {smoothing} -> {file}");
        return 0;
    }

    /// <summary>Prints the cell width and the ink extents of every printable character of a font.</summary>
    private static int FontMetrics(ToolOptions o)
    {
        int fontIndex = int.Parse(o.Positional(0), CultureInfo.InvariantCulture);
        GraphicsContext graphics = CreateGraphics(o, withFonts: true);
        BitmapFont font = graphics.Fonts!.Get(fontIndex);
        Console.WriteLine($"font {fontIndex}: height {font.Height}, ink {font.InkIndex}, background {font.BackgroundIndex}");
        Console.WriteLine("char width  inkLeft inkRight inkTop inkBottom");
        for (int c = 32; c < 256; c++)
        {
            int width = font.GetWidth((byte)c);
            ReadOnlySpan<byte> glyph = font.GetGlyph((byte)c);
            if (width == 0 || glyph.Length < width * font.Height)
                continue;
            int left = int.MaxValue, right = -1, top = int.MaxValue, bottom = -1;
            for (int y = 0; y < font.Height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte v = glyph[y * width + x];
                    if (v == 0xFF || v == font.BackgroundIndex)
                        continue;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
            string label = c < 127 ? $"'{(char)c}'" : $"0x{c:X2}";
            Console.WriteLine(right < 0
                ? $"{label,-5} {width,5}  (no ink)"
                : $"{label,-5} {width,5}  {left,7} {right,8} {top,6} {bottom,9}");
        }
        return 0;
    }

    private static void BlendRgb(byte[] rgba, int width, int x, int y, System.Numerics.Vector3 rgb, float alpha)
    {
        int o = (y * width + x) * 4;
        if ((uint)o >= (uint)rgba.Length)
            return;
        rgba[o] = (byte)MathF.Round(rgba[o] + (rgb.X - rgba[o]) * alpha);
        rgba[o + 1] = (byte)MathF.Round(rgba[o + 1] + (rgb.Y - rgba[o + 1]) * alpha);
        rgba[o + 2] = (byte)MathF.Round(rgba[o + 2] + (rgb.Z - rgba[o + 2]) * alpha);
    }

    private static void Blend(byte[] rgba, int width, int x, int y, ReadOnlySpan<byte> palette, byte index, float alpha)
    {
        int o = (y * width + x) * 4;
        if ((uint)o >= (uint)rgba.Length)
            return;
        for (int k = 0; k < 3; k++)
            rgba[o + k] = (byte)MathF.Round(rgba[o + k] + (palette[index * 3 + k] - rgba[o + k]) * alpha);
    }
}
