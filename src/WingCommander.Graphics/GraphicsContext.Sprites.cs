using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Graphics;

public sealed partial class GraphicsContext
{
    /// <summary>Flip flag: mirror horizontally.</summary>
    public const int FlipHorizontal = 0x10;

    /// <summary>Flip flag: mirror vertically.</summary>
    public const int FlipVertical = 0x20;

    /// <summary>Unit scale of the sprite entry points (8.8).</summary>
    public const int ScaleOne = 0x100;

    /// <summary>Copies a 256-entry table into <see cref="RasterPaletteTranslation"/>.</summary>
    /// <remarks>C: SetPaletteTranslationTable (asm 0x43AE3F, screens.c).</remarks>
    public void SetPaletteTranslationTable(ReadOnlySpan<byte> translation)
    {
        if (translation.Length < 256)
            throw new ArgumentException("A translation table needs 256 entries.", nameof(translation));
        translation[..256].CopyTo(_rasterPaletteTranslation);
    }

    /// <summary>Maps every index 0..254 to <paramref name="colour"/> (255 stays transparent) for silhouettes.</summary>
    /// <remarks>C: SetSolidColourTranslation (0x440D10, gr.c).</remarks>
    public void SetSolidColourTranslation(byte colour)
    {
        _solidColourTranslation.AsSpan(0, 255).Fill(colour);
        _solidColourTranslation[255] = 0xFF;
        SetPaletteTranslationTable(_solidColourTranslation);
    }

    /// <summary>
    /// Draws a frame with its hot spot at absolute (x, y), rotated by <paramref name="angle"/>
    /// degrees and scaled by <paramref name="scaleX"/>/<paramref name="scaleY"/> (8.8, 0x100 = 1).
    /// <paramref name="flip"/>: 0, <see cref="FlipHorizontal"/>, <see cref="FlipVertical"/> or
    /// both; any other value is a fatal "bad flip". A non-zero <paramref name="blendMode"/>
    /// recolours through <see cref="RasterPaletteTranslation"/>. Invalid shape/frame/viewport
    /// arguments draw nothing.
    /// </summary>
    /// <remarks>C: DrawSpriteTransformed (0x440FE0, gr.c).</remarks>
    public void DrawSpriteTransformed(Viewport viewport, int x, int y, ShapeTable? shape, int frame, int angle,
        int scaleX, int scaleY, int flip, int blendMode)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        if (shape is null || frame < 0 || viewport.Surface is null || frame >= shape.FrameCount)
            return;
        PreparedFrame prepared = shape.GetPreparedFrame(frame);
        RasterClip clip = ClipViewportToScreen(viewport);
        if (flip != 0)
        {
            switch (flip)
            {
                case FlipHorizontal:
                    scaleX = -scaleX;
                    break;
                case FlipVertical:
                    scaleY = -scaleY;
                    break;
                case FlipHorizontal | FlipVertical:
                    scaleX = -scaleX;
                    scaleY = -scaleY;
                    break;
                default:
                    throw new InvalidOperationException($"bad flip 0x{flip:x}");
            }
        }
        ReadOnlySpan<byte> translation = blendMode != 0 ? _rasterPaletteTranslation : ReadOnlySpan<byte>.Empty;
        RleRenderer.RotateRLEImage(clip, prepared, x, y, _transformScratch, unchecked(angle * 10),
            unchecked(scaleX * 256), unchecked(scaleY * 256), translation);
    }

    /// <summary>Unrotated, unscaled draw with the hot spot at absolute (x, y).</summary>
    /// <remarks>C: DrawSpriteDefault (0x441400, gr.c).</remarks>
    public void DrawSpriteDefault(Viewport viewport, int x, int y, ShapeTable? shape, int frame)
    {
        if (shape is not null && frame >= 0)
            DrawSpriteTransformed(viewport, x, y, shape, frame, 0, ScaleOne, ScaleOne, 0, 0);
    }

    /// <summary>Uniformly scaled and rotated draw (every space object).</summary>
    /// <remarks>C: DrawSpriteScaled (0x441FC0, gr.c).</remarks>
    public void DrawSpriteScaled(Viewport viewport, int x, int y, ShapeTable? shape, int frame, int angle, int scale,
        int flip) =>
        DrawSpriteTransformed(viewport, x, y, shape, frame, angle, scale, scale, flip, 0);

    /// <summary>Silhouette: every opaque pixel drawn in <paramref name="colour"/>.</summary>
    /// <remarks>C: DrawSolidColourSprite (0x441A40, gr.c).</remarks>
    public void DrawSolidColourSprite(Viewport viewport, int x, int y, ShapeTable? shape, int frame, byte colour)
    {
        SetSolidColourTranslation(colour);
        DrawSpriteTransformed(viewport, x, y, shape, frame, 0, ScaleOne, ScaleOne, 0, 1);
    }

    /// <summary>Scaled/rotated silhouette.</summary>
    /// <remarks>C: DrawSolidColourSpriteScaled (0x442000, gr.c).</remarks>
    public void DrawSolidColourSpriteScaled(Viewport viewport, int x, int y, ShapeTable? shape, int frame,
        int angle, int scale, int flip, byte colour)
    {
        SetSolidColourTranslation(colour);
        DrawSpriteTransformed(viewport, x, y, shape, frame, angle, scale, scale, flip, 1);
    }

    /// <summary>Saves the pixels under a frame drawn at (x, y) (see <see cref="SpriteBackground"/>).</summary>
    /// <remarks>C: CaptureSpriteBackground (0x441450, gr.c).</remarks>
    public int CaptureSpriteBackground(Viewport viewport, Span<byte> background, int x, int y, ShapeTable? shape,
        int frame) =>
        SpriteBackground.CaptureSpriteBackground(viewport, background, x, y, shape, frame);

    /// <summary>Restores pixels saved by <see cref="CaptureSpriteBackground"/>; marks the screen dirty.</summary>
    /// <remarks>C: RestoreSpriteBackground (0x441740, gr.c).</remarks>
    public int RestoreSpriteBackground(Viewport viewport, ReadOnlySpan<byte> background, int x, int y,
        ShapeTable? shape, int frame)
    {
        int restored = SpriteBackground.RestoreSpriteBackground(viewport, background, x, y, shape, frame);
        if (shape is not null && shape.HasFrame(frame))
            MarkDirtyIfScreen(viewport);
        return restored;
    }

    /// <summary>
    /// Width of one caption line in the shape font (TITLE.VGA section 1): letters 'A'..'z' use
    /// frame <c>c - 'A'</c>, '.' frame 58, ',' frame 59, each advancing by the frame's right extent
    /// + 2; a space advances 6. Stops at '\n' or the end.
    /// </summary>
    /// <remarks>C: GetLineLength (0x403890, mono.c).</remarks>
    public static short GetLineLength(ShapeTable introFont, ReadOnlySpan<byte> text)
    {
        ArgumentNullException.ThrowIfNull(introFont);
        short width = 0;
        foreach (byte c in text)
        {
            if (c == 0 || c == '\n')
                break;
            int frame = SubtitleFrame(c);
            if (frame >= 0)
                width = unchecked((short)(width + ShapeBounds.GetShapeFrameExtent(0, 0, introFont, frame, 2) + 2));
            else if (c == ' ')
                width = unchecked((short)(width + 6));
        }
        return width;
    }

    /// <summary>
    /// Cinematic captions in the shape font: lines 16 px apart, the block vertically centred in
    /// 128 rows, each line horizontally centred in 320 columns. The colour argument of the
    /// original is ignored there too.
    /// </summary>
    /// <remarks>C: print_subtitle (0x403920, mono.c).</remarks>
    public void PrintSubtitle(Viewport viewport, ShapeTable introFont, ReadOnlySpan<byte> text)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(introFont);
        int length = text.IndexOf((byte)0);
        if (length >= 0)
            text = text[..length];
        int lines = 1;
        foreach (byte c in text)
        {
            if (c == '\n')
                lines++;
        }
        short y = (short)((128 - lines * 16) / 2);
        short x = (short)((320 - GetLineLength(introFont, text)) >> 1);
        for (int i = 0; i < text.Length; i++)
        {
            byte c = text[i];
            int frame = SubtitleFrame(c);
            if (frame >= 0)
            {
                DrawSpriteDefault(viewport, x, y, introFont, frame);
                x = unchecked((short)(x + ShapeBounds.GetShapeFrameExtent(0, 0, introFont, frame, 2) + 2));
            }
            else if (c == ' ')
            {
                x = unchecked((short)(x + 6));
            }
            else if (c == '\n')
            {
                y = unchecked((short)(y + 16));
                x = (short)((320 - GetLineLength(introFont, text[(i + 1)..])) >> 1);
            }
        }
        MarkDirtyIfScreen(viewport);
    }

    /// <summary>
    /// Width of a caption line in the shape font scaled by <paramref name="scale"/> (8.8): letters
    /// 'A'..'z' advance by the right edge of their transformed bounds + 1 + scale*2/256, a space by
    /// scale*6/256; stops at '\n' or the end (no '.'/',' frames, unlike the unscaled font). The
    /// bounds are measured on <paramref name="spaceBuffer"/> with the hot spot at (0, 0); when a
    /// letter's box misses the viewport the previous bounds are reused, as in the original.
    /// </summary>
    /// <remarks>C: MeasureScaledIntroTextWidth (0x403710, mono.c) on stSpaceBuffer with pIntroFont.</remarks>
    public static short MeasureScaledIntroTextWidth(Viewport spaceBuffer, ShapeTable introFont, ReadOnlySpan<byte> text, short scale)
    {
        ArgumentNullException.ThrowIfNull(spaceBuffer);
        ArgumentNullException.ThrowIfNull(introFont);
        Span<short> bounds = stackalloc short[4];
        bounds.Clear();
        short width = 0;
        foreach (byte c in text)
        {
            if (c == 0 || c == '\n')
                break;
            if (c is >= (byte)'A' and <= (byte)'z')
            {
                ShapeBounds.GetTransformedShapeBounds(spaceBuffer, 0, 0, introFont, c - 'A', 0, scale, 0, bounds);
                width = unchecked((short)(width + bounds[2] + 1));
                width = unchecked((short)(width + (scale * 2 >> 8)));
            }
            else if (c == ' ')
            {
                width = unchecked((short)(width + (scale * 6 >> 8)));
            }
        }
        return width;
    }

    /// <summary>
    /// Draws one caption line of the shape font scaled by <paramref name="scale"/> (8.8) into
    /// <paramref name="spaceBuffer"/>, centred on <paramref name="centreX"/> with its baseline at
    /// <paramref name="baselineY"/> (the letters' hot spots sit scale*16/512 above it). Used by the
    /// title logo zoom and the attract credits.
    /// </summary>
    /// <remarks>C: DrawCenteredScaledIntroText (0x4037A0, mono.c) on stSpaceBuffer with pIntroFont.</remarks>
    public void DrawCenteredScaledIntroText(Viewport spaceBuffer, ShapeTable introFont, ReadOnlySpan<byte> text,
        short centreX, short baselineY, short scale)
    {
        ArgumentNullException.ThrowIfNull(spaceBuffer);
        ArgumentNullException.ThrowIfNull(introFont);
        Span<short> bounds = stackalloc short[4];
        bounds.Clear();
        int scaled = scale;
        short x = unchecked((short)(centreX - MeasureScaledIntroTextWidth(spaceBuffer, introFont, text, scale) / 2));
        short y = unchecked((short)(baselineY - (scaled * 16 >> 9)));
        foreach (byte c in text)
        {
            if (c == 0 || c == '\n')
                break;
            if (c is >= (byte)'A' and <= (byte)'z')
            {
                DrawSpriteScaled(spaceBuffer, x, y, introFont, c - 'A', 0, scale, 0);
                ShapeBounds.GetTransformedShapeBounds(spaceBuffer, 0, 0, introFont, c - 'A', 0, scale, 0, bounds);
                x = unchecked((short)(x + bounds[2] + 1));
                x = unchecked((short)(x + (scaled * 2 >> 8)));
            }
            else if (c == ' ')
            {
                x = unchecked((short)(x + (scaled * 6 >> 8)));
            }
        }
    }

    private static int SubtitleFrame(byte c) => c switch
    {
        >= (byte)'A' and <= (byte)'z' => c - 'A',
        (byte)'.' => 58,
        (byte)',' => 59,
        _ => -1,
    };
}
