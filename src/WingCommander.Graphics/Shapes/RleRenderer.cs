using WingCommander.Graphics.Raster;

namespace WingCommander.Graphics.Shapes;

/// <summary>
/// Draws prepared shape frames: the unrotated clipped path and the scaled/rotated scanline
/// mapper of the original raster library. Coordinates are absolute; the hot spot lands on
/// (x, y). Pixels whose value is 0xFF are transparent; an optional 256-entry translation table
/// recolours every drawn pixel (the "blend"/solid-colour mode).
/// </summary>
public static class RleRenderer
{
    /// <summary>Size of the rotate/scale decode scratch (the largest frame area, 320 x 200).</summary>
    /// <remarks>C: abShapeTransformScratch[0xFA00].</remarks>
    public const int TransformScratchSize = 0xFA00;

    /// <summary>
    /// Draws a prepared frame unrotated and unscaled, clipped to the clip rectangle. Returns
    /// -2 for an empty clip, -4 for a degenerate frame, -3 when wholly outside, else 0.
    /// </summary>
    /// <remarks>C: DrawRLEImage (asm 0x43A974) and DrawRLEImageColor (translation not empty),
    /// including their *Unclipped fast paths (screens.c).</remarks>
    public static int DrawRLEImage(in RasterClip clip, PreparedFrame frame, int x, int y,
        ReadOnlySpan<byte> translation = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (clip.IsEmpty)
            return -2;
        if (frame.Right < frame.Left || frame.Bottom < frame.Top)
            return -4;
        int frameLeft = x + frame.Left, frameTop = y + frame.Top;
        int frameRight = x + frame.Right, frameBottom = y + frame.Bottom;
        if (frameRight < clip.Left || frameLeft > clip.Right || frameBottom < clip.Top || frameTop > clip.Bottom)
            return -3;

        IndexedSurface surface = clip.Surface;
        byte[] pixels = surface.Pixels;
        byte[] ops = frame.OpsArray;
        int[] rowStarts = frame.RowStartsArray;
        int cl = clip.Left, cr = clip.Right;
        bool translate = !translation.IsEmpty;
        int firstRow = Math.Max(0, clip.Top - frameTop);
        int lastRow = Math.Min(frame.Height - 1, clip.Bottom - frameTop);
        for (int row = firstRow; row <= lastRow; row++)
        {
            int p = rowStarts[row];
            int dx = frameLeft;
            int rowBase = surface.IndexOf(0, frameTop + row);
            while (true)
            {
                byte op = ops[p++];
                if (op == 0)
                    break;
                if (op == 1)
                {
                    dx += ops[p++];
                    continue;
                }
                int count = op >> 1;
                int start = Math.Max(dx, cl);
                int end = Math.Min(dx + count - 1, cr);
                if ((op & 1) != 0)
                {
                    if (start <= end)
                    {
                        if (translate)
                        {
                            for (int c = start; c <= end; c++)
                                pixels[rowBase + c] = translation[ops[p + c - dx]];
                        }
                        else
                        {
                            ops.AsSpan(p + start - dx, end - start + 1).CopyTo(pixels.AsSpan(rowBase + start));
                        }
                    }
                    p += count;
                }
                else
                {
                    byte value = ops[p++];
                    if (translate)
                        value = translation[value];
                    if (start <= end)
                        pixels.AsSpan(rowBase + start, end - start + 1).Fill(value);
                }
                dx += count;
            }
        }
        return 0;
    }

    /// <summary>
    /// Scaled and/or rotated draw (ships in space). <paramref name="angleTenths"/> is the angle
    /// in tenths of a degree (positive = clockwise on screen), <paramref name="scaleX"/> /
    /// <paramref name="scaleY"/> are 16.16 (negative mirrors). At angle 0 and scale 1.0 this is
    /// <see cref="DrawRLEImage"/>. Otherwise the frame is decoded into <paramref name="scratch"/>,
    /// its four corners are transformed with the original rounding (scale floored, each rotation
    /// product rounded separately) and the quad is scan-converted with 16.16 edge and span
    /// interpolation, sampling the scratch at <c>floor()</c> of the interpolated source position.
    /// Frames larger than 64000 pixels are not drawn (-4), like the SDL port.
    /// </summary>
    /// <remarks>C: RotateRLEImage (asm 0x43B469, screens_rotate_rle_image.inc) with
    /// TransformRLEPoint (0x43E3B1) and GetRLETransformTrig (0x43E2D3). The reference's portable C
    /// is a different (inverse mapping) algorithm and is not followed.</remarks>
    public static int RotateRLEImage(in RasterClip clip, PreparedFrame frame, int x, int y, Span<byte> scratch,
        int angleTenths, int scaleX, int scaleY, ReadOnlySpan<byte> translation = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (scaleX == 0x10000 && scaleY == 0x10000 && angleTenths == 0)
            return DrawRLEImage(clip, frame, x, y, translation);

        int width = frame.Width, height = frame.Height;
        if (width <= 0 || height <= 0)
            return -4;
        int area = width * height;
        if (area > TransformScratchSize || area > scratch.Length)
            return -4;
        Span<byte> bitmap = scratch[..area];
        bitmap.Fill(0xFF);
        frame.DecodeTo(bitmap, translation);
        if (clip.IsEmpty)
            return -2;

        int cl = clip.Left, ct = clip.Top, cr = clip.Right, cb = clip.Bottom;
        GetRLETransformTrig(angleTenths, out int cosine, out int sine);
        int originX = -frame.Left, originY = -frame.Top;

        // Vertex ring: source corners (0,0), (W-1,0), (W-1,H-1), (0,H-1).
        Span<int> sourceX = stackalloc int[4];
        Span<int> sourceY = stackalloc int[4];
        sourceX[0] = 0;
        sourceX[1] = sourceX[2] = width - 1;
        sourceX[3] = 0;
        sourceY[0] = sourceY[1] = 0;
        sourceY[2] = sourceY[3] = height - 1;
        Span<int> vertexX = stackalloc int[4];
        Span<int> vertexY = stackalloc int[4];
        int outside = 0xF, minY = 0x7FFF, maxY = -0x8000, topVertex = -1;
        for (int k = 0; k < 4; k++)
        {
            TransformCore(sourceX[k], sourceY[k], originX, originY, cosine, sine, scaleX, scaleY,
                out int tx, out int ty);
            int vx = unchecked(tx + (x - originX));
            int vy = unchecked(ty + (y - originY));
            vertexX[k] = vx;
            vertexY[k] = vy;
            int code = (vx - cl < 0 ? 8 : 0) | (cr - vx < 0 ? 4 : 0) | (vy - ct < 0 ? 2 : 0) | (cb - vy < 0 ? 1 : 0);
            if (vy <= minY)
            {
                minY = vy;
                topVertex = k;
            }
            if (vy >= maxY)
                maxY = vy;
            outside &= code;
        }
        if (outside != 0 || topVertex < 0 || maxY == minY)
            return 0;

        // First edge of each chain: skip edges entirely above the clip and horizontal ones.
        var left = new Edge();
        var right = new Edge();
        if (!left.Start(topVertex, -1, ct, vertexX, vertexY, sourceX, sourceY)
            || !right.Start(topVertex, +1, ct, vertexX, vertexY, sourceX, sourceY))
            return 0;

        int currentY = minY;
        int rows = (maxY > cb ? cb : maxY) - currentY;
        if (ct > currentY)
        {
            rows -= ct - currentY;
            currentY = ct;
            left.Advance(ct - vertexY[left.From]);
            right.Advance(ct - vertexY[right.From]);
        }

        IndexedSurface surface = clip.Surface;
        byte[] pixels = surface.Pixels;
        int stepX = 0, stepY = 0;
        while (true)
        {
            int lx = left.DestX, rx = right.DestX;
            int lsx = left.SourceX, rsx = right.SourceX;
            int lsy = left.SourceY, rsy = right.SourceY;
            if (rx <= lx)
            {
                (lx, rx) = (rx, lx);
                (lsx, rsx) = (rsx, lsx);
                (lsy, rsy) = (rsy, lsy);
            }
            int xa = lx >> 16;
            int xb = rx >> 16;
            if (xa <= cr && xb >= cl)
            {
                int span = xb - xa;
                int srcX = lsx, srcY = lsy;
                if (span != 0)
                {
                    stepX = RowStep(rsx - lsx, span);
                    stepY = RowStep(rsy - lsy, span);
                    if (cl > xa)
                    {
                        int n = cl - xa;
                        xa = cl;
                        srcX = unchecked(srcX + MultiplyFixed(stepX, n));
                        srcY = unchecked(srcY + MultiplyFixed(stepY, n));
                    }
                    if (xb > cr)
                        xb = cr;
                }
                long accX = srcX, accY = srcY;
                int rowBase = surface.IndexOf(0, currentY);
                for (int px = xa; px <= xb; px++)
                {
                    int sx = (int)(accX >> 16), sy = (int)(accY >> 16);
                    if ((uint)sx < (uint)width && (uint)sy < (uint)height)
                    {
                        byte value = bitmap[sy * width + sx];
                        if (value != 0xFF)
                            pixels[rowBase + px] = value;
                    }
                    accX += stepX;
                    accY += stepY;
                }
            }

            rows--;
            if (rows < 0)
                break;
            currentY++;
            if (rows == 0)
            {
                // Last row: both chains advance one step without switching edges.
                left.Step();
                right.Step();
                continue;
            }
            if (--left.Dy == 0)
                left.Next(-1, vertexX, vertexY, sourceX, sourceY);
            else
                left.Step();
            if (--right.Dy == 0)
                right.Next(+1, vertexX, vertexY, sourceX, sourceY);
            else
                right.Step();
        }
        return 0;
    }

    /// <summary>
    /// cos/sin (16.16) of an angle in tenths of a degree from the quarter cosine table. The angle
    /// is reduced to [0, 3600] by adding/subtracting full turns.
    /// </summary>
    /// <remarks>C: GetRLETransformTrig (asm 0x43E2D3, screens.c).</remarks>
    public static void GetRLETransformTrig(int angleTenths, out int cosine, out int sine)
    {
        int a = angleTenths;
        if (a < 0)
        {
            a %= 3600;
            if (a < 0)
                a += 3600;
        }
        else if (a > 3600)
        {
            a = (a - 1) % 3600 + 1;
        }
        ReadOnlySpan<int> table = TrigTables.QuarterCosine;
        if (a <= 1800)
        {
            if (a <= 900)
            {
                cosine = table[a];
                sine = table[900 - a];
            }
            else
            {
                int b = 1800 - a;
                cosine = -table[b];
                sine = table[900 - b];
            }
        }
        else
        {
            int b = 3600 - a;
            if (b <= 900)
            {
                cosine = table[b];
                sine = -table[900 - b];
            }
            else
            {
                b = 1800 - b;
                cosine = -table[b];
                sine = -table[900 - b];
            }
        }
    }

    /// <summary>
    /// Scales and rotates <c>point - origin</c> and adds the origin back:
    /// <c>e = floor(d * scale / 65536)</c> per axis, then
    /// <c>x' = round(ex*cos) - round(ey*sin)</c>, <c>y' = round(ey*cos) + round(ex*sin)</c>
    /// with each 16.16 product rounded separately.
    /// </summary>
    /// <remarks>C: TransformRLEPoint (asm 0x43E3B1, screens.c).</remarks>
    public static void TransformRLEPoint(int pointX, int pointY, int originX, int originY, int angleTenths,
        int scaleX, int scaleY, out int resultX, out int resultY)
    {
        GetRLETransformTrig(angleTenths, out int cosine, out int sine);
        TransformCore(pointX, pointY, originX, originY, cosine, sine, scaleX, scaleY, out resultX, out resultY);
    }

    private static void TransformCore(int pointX, int pointY, int originX, int originY, int cosine, int sine,
        int scaleX, int scaleY, out int resultX, out int resultY)
    {
        unchecked
        {
            int ex = (int)(((long)((pointX - originX) << 16) * scaleX + 0x8000) >> 32);
            int ey = (int)(((long)((pointY - originY) << 16) * scaleY + 0x8000) >> 32);
            int exCos = (int)(((long)ex * cosine + 0x8000) >> 16);
            int exSin = (int)(((long)ex * sine + 0x8000) >> 16);
            int eyCos = (int)(((long)ey * cosine + 0x8000) >> 16);
            int eySin = (int)(((long)ey * sine + 0x8000) >> 16);
            resultX = exCos - eySin + originX;
            resultY = eyCos + exSin + originY;
        }
    }

    /// <summary>Per-row edge step: <c>((short)d &lt;&lt; 32) / (dy &lt;&lt; 16)</c>, truncated (the asm idiv).</summary>
    private static int EdgeStep(int delta, int dy)
    {
        long numerator = (long)unchecked((short)delta) << 32;
        int denominator = unchecked(dy << 16);
        return denominator == 0 ? 0 : unchecked((int)(numerator / denominator));
    }

    /// <summary>Per-pixel span step: <c>(diff &lt;&lt; 16) / (span &lt;&lt; 16)</c>, truncated.</summary>
    private static int RowStep(int difference, int span)
    {
        long numerator = (long)difference << 16;
        int denominator = unchecked(span << 16);
        return denominator == 0 ? 0 : unchecked((int)(numerator / denominator));
    }

    /// <summary><c>(step * (n &lt;&lt; 16)) &gt;&gt; 16</c> as the asm imul/shrd pair.</summary>
    private static int MultiplyFixed(int step, int count) =>
        unchecked((int)(((long)step * (count << 16)) >> 16));

    /// <summary>State of one edge chain of the scan converter.</summary>
    private struct Edge
    {
        public int From;
        public int To;
        public int Dy;
        public int DestX;
        public int SourceX;
        public int SourceY;
        private int _stepDestX;
        private int _stepSourceX;
        private int _stepSourceY;

        /// <summary>Chooses the first edge from the top vertex (direction -1 = left chain, +1 = right chain).</summary>
        public bool Start(int top, int direction, int clipTop, ReadOnlySpan<int> vx, ReadOnlySpan<int> vy,
            ReadOnlySpan<int> sx, ReadOnlySpan<int> sy)
        {
            From = top;
            for (int guard = 0; guard < 8; guard++)
            {
                To = (From + direction + 4) & 3;
                int fromY = vy[From], toY = vy[To];
                if (!((fromY < clipTop && toY <= clipTop) || toY == fromY))
                {
                    Load(toY - fromY, vx, sx, sy);
                    return true;
                }
                From = To;
            }
            return false;
        }

        /// <summary>Switches to the next edge when the current one is exhausted (a zero dy counts as 1).</summary>
        public void Next(int direction, ReadOnlySpan<int> vx, ReadOnlySpan<int> vy, ReadOnlySpan<int> sx,
            ReadOnlySpan<int> sy)
        {
            From = To;
            To = (From + direction + 4) & 3;
            int dy = vy[To] - vy[From];
            Load(dy == 0 ? 1 : dy, vx, sx, sy);
        }

        /// <summary>Moves the accumulators <paramref name="rows"/> rows down (exact products).</summary>
        public void Advance(int rows)
        {
            Dy -= rows;
            DestX = unchecked(DestX + MultiplyFixed(_stepDestX, rows));
            SourceX = unchecked(SourceX + MultiplyFixed(_stepSourceX, rows));
            SourceY = unchecked(SourceY + MultiplyFixed(_stepSourceY, rows));
        }

        public void Step()
        {
            unchecked
            {
                DestX += _stepDestX;
                SourceX += _stepSourceX;
                SourceY += _stepSourceY;
            }
        }

        private void Load(int dy, ReadOnlySpan<int> vx, ReadOnlySpan<int> sx, ReadOnlySpan<int> sy)
        {
            Dy = dy;
            _stepDestX = EdgeStep(vx[To] - vx[From], dy);
            _stepSourceX = EdgeStep(sx[To] - sx[From], dy);
            _stepSourceY = EdgeStep(sy[To] - sy[From], dy);
            unchecked
            {
                DestX = (vx[From] << 16) + 0x8000;
                SourceX = (sx[From] << 16) + 0x8000;
                SourceY = (sy[From] << 16) + 0x8000;
            }
        }
    }
}
