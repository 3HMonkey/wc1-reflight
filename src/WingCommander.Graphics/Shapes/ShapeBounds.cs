using WingCommander.Graphics.Raster;

namespace WingCommander.Graphics.Shapes;

/// <summary>Screen bounds of shape frames (plain and rotated/scaled estimates).</summary>
public static class ShapeBounds
{
    /// <summary>
    /// Writes <c>{x - left, y - top, x + right, y + bottom}</c> (inclusive) of a frame drawn with
    /// its hot spot at (x, y) into <paramref name="bounds"/> and returns -1; returns 0 (bounds
    /// untouched) when the frame does not exist.
    /// </summary>
    /// <remarks>C: GetShapeFrameBounds (0x435020, mathfp.c). The original tested
    /// <c>frame*4 &lt; dirSize</c>, accepting frame == frameCount; the port uses the strict test.</remarks>
    public static short GetShapeFrameBounds(Span<short> bounds, int x, int y, ShapeTable? shape, int frame)
    {
        if (shape is null || frame < 0 || !shape.HasFrame(frame))
            return 0;
        ShapeExtents e = shape.GetExtents(frame);
        bounds[2] = unchecked((short)(e.Right + x));
        bounds[0] = unchecked((short)(x - e.Left));
        bounds[1] = unchecked((short)(y - e.Top));
        bounds[3] = unchecked((short)(e.Bottom + y));
        return -1;
    }

    /// <summary>One of the four bounds of <see cref="GetShapeFrameBounds"/> (0 left, 1 top, 2 right, 3 bottom).</summary>
    /// <remarks>C: GetShapeFrameExtent (0x407710, brains.c). Returns 0 for a missing frame (the
    /// original returned uninitialised stack data).</remarks>
    public static short GetShapeFrameExtent(int x, int y, ShapeTable? shape, int frame, int which)
    {
        Span<short> bounds = stackalloc short[4];
        bounds.Clear();
        GetShapeFrameBounds(bounds, x, y, shape, frame);
        return bounds[which & 3];
    }

    /// <summary>
    /// Estimated screen bounds of a frame drawn rotated by <paramref name="angleDegrees"/> and
    /// scaled by <paramref name="scale"/> (8.8) with its hot spot at (x, y), using the absolute
    /// sine/cosine tables. Returns 1 and fills <c>bounds = {left, top, right, bottom}</c> when the
    /// box intersects the viewport rectangle, else 0. With no shape it tests whether (x, y) lies
    /// inside the rectangle. <paramref name="flip"/> is ignored, as in the original. The estimate
    /// combines |cos| and |sin| with the unrotated extents, so it bounds the drawn pixels (within
    /// a pixel) only for 0..90 degrees; for other angles it effectively assumes the frame is
    /// symmetric about its hot spot (original behaviour, reproduced).
    /// </summary>
    /// <remarks>C: GetTransformedShapeBounds (0x442050, gr.c). The angle is reduced modulo 360
    /// (the original indexed the tables unchecked); the frame test is strict (the original
    /// accepted frame == frameCount).</remarks>
    public static int GetTransformedShapeBounds(Viewport viewport, int x, int y, ShapeTable? shape, int frame,
        int angleDegrees, int scale, int flip, Span<short> bounds)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        _ = flip;
        if (shape is null)
            return viewport.ContainsPoint(x, y) ? 1 : 0;
        if (frame < 0 || !shape.HasFrame(frame))
            return 0;
        ShapeExtents e = shape.GetExtents(frame);
        int angle = ((angleDegrees % 360) + 360) % 360;
        int leftExtent = e.Left;
        int topExtent = e.Top;
        int absoluteCosine = (TrigTables.AbsoluteCosine[angle] * scale) >> 8;
        int absoluteSine = (TrigTables.AbsoluteSine[angle] * scale) >> 8;
        if (absoluteCosine == 0)
            absoluteCosine = 1;
        if (absoluteSine == 0)
            absoluteSine = 1;
        int verticalExtent = topExtent + e.Bottom;
        int horizontalExtent = e.Right + leftExtent;
        int transformedHeight = absoluteSine * horizontalExtent + absoluteCosine * verticalExtent;
        if ((transformedHeight & 0xFF) != 0)
            transformedHeight += 0x100;
        transformedHeight >>= 8;
        int transformedWidth = absoluteCosine * horizontalExtent + absoluteSine * verticalExtent;
        if ((transformedWidth & 0xFF) != 0)
            transformedWidth += 0x100;
        transformedWidth >>= 8;
        short top = unchecked((short)(y - (absoluteSine * leftExtent >> 8) - (absoluteCosine * topExtent >> 8)));
        short bottom = unchecked((short)(transformedHeight + top));
        short left = unchecked((short)(((absoluteSine * topExtent >> 8) - (absoluteCosine * leftExtent >> 8) + x)
            - ((absoluteSine * verticalExtent >> 8) + 1)));
        short right = unchecked((short)(transformedWidth + left));
        if (viewport.Left <= right && left <= viewport.Right && viewport.Top <= bottom && top <= viewport.Bottom)
        {
            bounds[0] = left;
            bounds[2] = right;
            bounds[1] = top;
            bounds[3] = bottom;
            return 1;
        }
        return 0;
    }
}
