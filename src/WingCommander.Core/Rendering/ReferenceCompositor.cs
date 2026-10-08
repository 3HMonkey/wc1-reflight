using System.Numerics;
using WingCommander.Core.Video;

namespace WingCommander.Core.Rendering;

/// <summary>
/// CPU rendition of a <see cref="RenderFrame"/> at output resolution: the classic picture
/// (nearest), the text layer and the key help overlay, following the same rules as the Vulkan
/// passes (ADR-013). Used by wc1tool for high-resolution snapshots and by tests as the
/// reference; the space view (R2) is not drawn (snapshots use the CPU sprites of the classic frame).
/// </summary>
public static class ReferenceCompositor
{
    /// <summary>Renders <paramref name="frame"/> into <paramref name="rgba"/> (width * height * 4 bytes).</summary>
    public static void Render(RenderFrame frame, int width, int height, AspectMode aspect, Span<byte> rgba,
        OverlayDrawList? overlay = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgba.Length < width * height * 4)
            throw new ArgumentException("The target is smaller than width * height * 4 bytes.", nameof(rgba));
        for (int i = 0; i < width * height; i++)
        {
            rgba[i * 4] = 0;
            rgba[i * 4 + 1] = 0;
            rgba[i * 4 + 2] = 0;
            rgba[i * 4 + 3] = 255;
        }

        PresentationRect rect = PresentationLayout.Compute(width, height, aspect, integerScaling: false);
        ReadOnlySpan<byte> palette = frame.Classic.Palette.Rgb;
        TextLayer? text = frame.Text;
        byte[] indices = text?.Pixels.Pixels ?? frame.Classic.Pixels.Pixels;
        float toLogicalX = Framebuffer.Width / (float)rect.Width;
        float toLogicalY = Framebuffer.Height / (float)rect.Height;
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            int sy = Math.Min(Framebuffer.Height - 1, (int)((y + 0.5f - rect.Y) * toLogicalY));
            for (int x = rect.X; x < rect.X + rect.Width; x++)
            {
                int sx = Math.Min(Framebuffer.Width - 1, (int)((x + 0.5f - rect.X) * toLogicalX));
                int index = indices[sy * Framebuffer.Width + sx];
                int o = (y * width + x) * 4;
                rgba[o] = palette[index * 3];
                rgba[o + 1] = palette[index * 3 + 1];
                rgba[o + 2] = palette[index * 3 + 2];
            }
        }

        if (text is not null)
            DrawText(text, rect, width, palette, rgba);

        if (frame.KeyHelp is { } help)
        {
            overlay ??= new OverlayDrawList();
            KeyHelpLayout.Build(help, width, height, rect, overlay);
            DrawOverlay(overlay, width, height, rgba);
        }
    }

    private static void DrawText(TextLayer text, PresentationRect rect, int width, ReadOnlySpan<byte> palette, Span<byte> rgba)
    {
        float scaleX = rect.Width / (float)Framebuffer.Width;
        float scaleY = rect.Height / (float)Framebuffer.Height;
        float texelsPerPixel = TexelsPerPixel(scaleX, scaleY);
        ReadOnlySpan<GlyphInstance> instances = text.Instances;
        for (int i = 0; i < instances.Length; i++)
        {
            GlyphInstance instance = instances[i];
            if (!text.Glyphs.TryGet(instance.Glyph, out GlyphImage? image) || !image.HasForeground)
                continue;
            int x0 = Math.Max(rect.X, (int)MathF.Floor(rect.X + instance.X * scaleX));
            int x1 = Math.Min(rect.X + rect.Width, (int)MathF.Ceiling(rect.X + (instance.X + image.Width) * scaleX));
            int y0 = Math.Max(rect.Y, (int)MathF.Floor(rect.Y + instance.Y * scaleY));
            int y1 = Math.Min(rect.Y + rect.Height, (int)MathF.Ceiling(rect.Y + (instance.Y + image.Height) * scaleY));
            for (int y = y0; y < y1; y++)
            {
                float ly = (y + 0.5f - rect.Y) / scaleY;
                int cy = (int)ly;
                if ((uint)cy >= Framebuffer.Height || ly < instance.Y || ly >= instance.Y + image.Height)
                    continue;
                for (int x = x0; x < x1; x++)
                {
                    float lx = (x + 0.5f - rect.X) / scaleX;
                    int cx = (int)lx;
                    if ((uint)cx >= Framebuffer.Width || lx < instance.X || lx >= instance.X + image.Width)
                        continue;
                    int mask = text.Mask[cy * Framebuffer.Width + cx];
                    if (mask == 0 || i < mask - 1)
                        continue;
                    float coverage = GlyphRasterizer.Coverage(image, lx - instance.X, ly - instance.Y, texelsPerPixel);
                    if (coverage <= 0f)
                        continue;
                    Vector3 colour = GlyphRasterizer.SampleColour(image, lx - instance.X, ly - instance.Y, instance.Colour, palette);
                    Blend(rgba, (y * width + x) * 4, colour, coverage);
                }
            }
        }
    }

    private static void DrawOverlay(OverlayDrawList list, int width, int height, Span<byte> rgba)
    {
        foreach (ref readonly OverlayItem item in list.Items)
        {
            var (r, g, b, a) = OverlayColour.Unpack(item.Colour);
            var colour = new Vector3(r, g, b);
            float alpha = a / 255f;
            int x0 = Math.Max(0, (int)MathF.Floor(item.X)), x1 = Math.Min(width, (int)MathF.Ceiling(item.X + item.Width));
            int y0 = Math.Max(0, (int)MathF.Floor(item.Y)), y1 = Math.Min(height, (int)MathF.Ceiling(item.Y + item.Height));
            if (item.Kind == OverlayItemKind.Rectangle)
            {
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                        Blend(rgba, (y * width + x) * 4, colour, alpha);
                continue;
            }
            if (list.Glyphs is null || !list.Glyphs.TryGet(item.Glyph, out GlyphImage? image) || !image.HasForeground)
                continue;
            float scaleX = item.Width / image.Width, scaleY = item.Height / image.Height;
            float texelsPerPixel = TexelsPerPixel(scaleX, scaleY);
            for (int y = y0; y < y1; y++)
            {
                float cy = (y + 0.5f - item.Y) / scaleY;
                if (cy < 0 || cy >= image.Height)
                    continue;
                for (int x = x0; x < x1; x++)
                {
                    float cx = (x + 0.5f - item.X) / scaleX;
                    if (cx < 0 || cx >= image.Width)
                        continue;
                    float coverage = GlyphRasterizer.Coverage(image, cx, cy, texelsPerPixel);
                    if (coverage > 0f)
                        Blend(rgba, (y * width + x) * 4, colour, coverage * alpha);
                }
            }
        }
    }

    /// <summary>
    /// Field texels per output pixel for anti-aliasing: the mean of the horizontal and vertical
    /// rates, like the shaders' average of fwidth over x and y.
    /// </summary>
    public static float TexelsPerPixel(float outputPixelsPerSourcePixelX, float outputPixelsPerSourcePixelY) =>
        GlyphImage.FieldScale * 0.5f * (1f / outputPixelsPerSourcePixelX + 1f / outputPixelsPerSourcePixelY);

    private static void Blend(Span<byte> rgba, int offset, Vector3 colour, float alpha)
    {
        rgba[offset] = (byte)MathF.Round(rgba[offset] + (colour.X - rgba[offset]) * alpha);
        rgba[offset + 1] = (byte)MathF.Round(rgba[offset + 1] + (colour.Y - rgba[offset + 1]) * alpha);
        rgba[offset + 2] = (byte)MathF.Round(rgba[offset + 2] + (colour.Z - rgba[offset + 2]) * alpha);
    }
}
