using System.Runtime.CompilerServices;
using WingCommander.Graphics.Text;

namespace WingCommander.Graphics.Raster;

/// <summary>
/// The raster library primitives the game uses (fill, blit, pixel, clipped line, ellipses),
/// reproduced from the original hand-written assembly. All coordinates are absolute; the
/// original's clip-relative coordinates only add a constant offset, which every algorithm here
/// is invariant to. Return codes follow the original (0 ok, -2 empty clip, -3 nothing drawn,
/// line: 2 rejected / 1 clipped) although the game never inspects them.
/// </summary>
/// <remarks>C: screens.c naked asm bodies (screens_*.inc).</remarks>
public static class RasterPrimitives
{
    /// <summary>Fills the whole clip rectangle.</summary>
    /// <remarks>C: FillRasterClip (screens.c).</remarks>
    public static int FillRasterClip(in RasterClip clip, byte colour)
    {
        if (clip.IsEmpty)
            return -2;
        IndexedSurface s = clip.Surface;
        int width = clip.Right - clip.Left + 1;
        for (int y = clip.Top; y <= clip.Bottom; y++)
            s.Pixels.AsSpan(s.IndexOf(clip.Left, y), width).Fill(colour);
        return 0;
    }

    /// <summary>
    /// Copies the overlap of the source clip and the destination clip, where source point
    /// (<paramref name="sourceX"/>, <paramref name="sourceY"/>) corresponds to destination point
    /// (<paramref name="destinationX"/>, <paramref name="destinationY"/>). Like the asm, only the
    /// difference of the two points matters: the whole intersection is copied, also above or left
    /// of the given points. Rows are processed bottom-up when the copy's clip-relative top in the
    /// source is not below the one in the destination (top-down otherwise), exactly as the asm;
    /// this only matters for overlapping copies within one surface. Within a row the copy has
    /// memmove semantics.
    /// </summary>
    /// <remarks>C: BlitRasterClip (screens.c) with colour 0xFFFFFFFF (copy). The fill variant
    /// (colour &lt;= 0xFF) is never used by the game.</remarks>
    public static int BlitRasterClip(in RasterClip source, int sourceX, int sourceY,
        in RasterClip destination, int destinationX, int destinationY, IRasterCopyObserver? observer = null)
    {
        if (source.IsEmpty)
            return -2;
        if (destination.IsEmpty)
            return -2;
        int offsetX = sourceX - destinationX;
        int offsetY = sourceY - destinationY;
        int left = Math.Max(source.Left, destination.Left + offsetX);
        int top = Math.Max(source.Top, destination.Top + offsetY);
        int right = Math.Min(source.Right, destination.Right + offsetX);
        int bottom = Math.Min(source.Bottom, destination.Bottom + offsetY);
        if (right < left || bottom < top)
            return -3;

        int width = right - left + 1;
        int height = bottom - top + 1;
        IndexedSurface src = source.Surface, dst = destination.Surface;
        int dstLeft = left - offsetX, dstTop = top - offsetY;
        bool bottomUp = top - source.OriginY <= dstTop - destination.OriginY;
        for (int i = 0; i < height; i++)
        {
            int row = bottomUp ? height - 1 - i : i;
            int sourceIndex = src.IndexOf(left, top + row), destinationIndex = dst.IndexOf(dstLeft, dstTop + row);
            observer?.OnCopy(src, sourceIndex, dst, destinationIndex, width);
            src.Pixels.AsSpan(sourceIndex, width).CopyTo(dst.Pixels.AsSpan(destinationIndex, width));
        }
        return 0;
    }

    /// <summary>Sets one pixel; returns the previous value or -2/-3 when clipped.</summary>
    /// <remarks>C: SetRasterClipPixel (screens.c).</remarks>
    public static int SetRasterClipPixel(in RasterClip clip, int x, int y, byte colour)
    {
        if (clip.IsEmpty)
            return -2;
        if (!clip.Contains(x, y))
            return -3;
        byte[] pixels = clip.Surface.Pixels;
        int index = clip.Surface.IndexOf(x, y);
        byte previous = pixels[index];
        pixels[index] = colour;
        return previous;
    }

    /// <summary>Reads one pixel; returns -2/-3 when clipped.</summary>
    /// <remarks>C: ReadRasterClipPixel (screens.c).</remarks>
    public static int ReadRasterClipPixel(in RasterClip clip, int x, int y)
    {
        if (clip.IsEmpty)
            return -2;
        if (!clip.Contains(x, y))
            return -3;
        return clip.Surface.Pixels[clip.Surface.IndexOf(x, y)];
    }

    /// <summary>
    /// Draws a clipped line (mode 0: store <paramref name="colour"/>) with the original 0.32
    /// fixed-point DDA and Cohen-Sutherland clipping. The minor coordinate of pixel k is
    /// <c>round-half-up(k * minor / major)</c> measured from the original start point; clipped
    /// endpoints land exactly on the pixels the unclipped DDA would draw. Returns 2 when the line
    /// is rejected, 1 when it was clipped, 0 otherwise (the vertical/horizontal paths of the asm
    /// return an uninitialised flag; this port returns 0 for them).
    /// </summary>
    /// <remarks>C: DrawClippedLine (asm 0x439E39, screens_draw_clipped_line.inc). Modes 1
    /// (translation table) and 2+ (per-pixel callback) are never used by the game.</remarks>
    public static int DrawClippedLine(in RasterClip clip, int x1, int y1, int x2, int y2, byte colour)
    {
        if (clip.IsEmpty)
            return -2;
        int cl = clip.Left, ct = clip.Top, cr = clip.Right, cb = clip.Bottom;
        int dx = x2 - x1, dy = y2 - y1;
        bool negX = dx < 0, negY = dy < 0;
        int adx = Math.Abs(dx), ady = Math.Abs(dy);

        if (dx == 0)
        {
            if (x1 < cl || x1 > cr || Math.Max(y1, y2) < ct || Math.Min(y1, y2) > cb)
                return 2;
            int ya = Math.Clamp(y1, ct, cb), yb = Math.Clamp(y2, ct, cb);
            DrawStraight(clip, x1, ya, 0, negY ? -1 : 1, Math.Abs(yb - ya) + 1, colour);
            return 0;
        }
        if (dy == 0)
        {
            if (y1 < ct || y1 > cb || Math.Max(x1, x2) < cl || Math.Min(x1, x2) > cr)
                return 2;
            int xa = Math.Clamp(x1, cl, cr), xb = Math.Clamp(x2, cl, cr);
            DrawStraight(clip, xa, y1, negX ? -1 : 1, 0, Math.Abs(xb - xa) + 1, colour);
            return 0;
        }

        bool diff = negX != negY;
        bool shallow = adx >= ady;
        uint slope = adx == ady
            ? 0xFFFFFFFFu
            : (uint)(((ulong)(uint)Math.Min(adx, ady) << 32) / (uint)Math.Max(adx, ady));

        int p1x = x1, p1y = y1, p2x = x2, p2y = y2;
        int seen = 0;
        for (int pass = 0; ; pass++)
        {
            int c1 = Outcode(p1x, p1y, cl, ct, cr, cb);
            int c2 = Outcode(p2x, p2y, cl, ct, cr, cb);
            seen |= c1 | c2;
            if ((c1 | c2) == 0)
                break;
            if ((c1 & c2) != 0 || pass > 16)
                return 2;
            bool ok;
            if (shallow)
            {
                if ((c1 & 8) != 0) { p1x = cl; ok = Major(cl - x1, slope, y1, diff, out p1y); }
                else if ((c1 & 4) != 0) { p1x = cr; ok = Major(x1 - cr, slope, y1, !diff, out p1y); }
                else if ((c1 & 2) != 0) { p1y = ct; ok = MinorFirst(ct - y1, slope, x1, diff, out p1x); }
                else if ((c1 & 1) != 0) { p1y = cb; ok = MinorFirst(y1 - cb, slope, x1, !diff, out p1x); }
                else if ((c2 & 8) != 0) { p2x = cl; ok = Major(x1 - cl, slope, y1, !diff, out p2y); }
                else if ((c2 & 4) != 0) { p2x = cr; ok = Major(cr - x1, slope, y1, diff, out p2y); }
                else if ((c2 & 2) != 0) { p2y = ct; ok = MinorLast(y1 - ct, slope, x1, !diff, out p2x); }
                else { p2y = cb; ok = MinorLast(cb - y1, slope, x1, diff, out p2x); }
            }
            else
            {
                if ((c1 & 8) != 0) { p1x = cl; ok = MinorFirst(cl - x1, slope, y1, diff, out p1y); }
                else if ((c1 & 4) != 0) { p1x = cr; ok = MinorFirst(x1 - cr, slope, y1, !diff, out p1y); }
                else if ((c1 & 2) != 0) { p1y = ct; ok = Major(ct - y1, slope, x1, diff, out p1x); }
                else if ((c1 & 1) != 0) { p1y = cb; ok = Major(y1 - cb, slope, x1, !diff, out p1x); }
                else if ((c2 & 8) != 0) { p2x = cl; ok = MinorLast(x1 - cl, slope, y1, !diff, out p2y); }
                else if ((c2 & 4) != 0) { p2x = cr; ok = MinorLast(cr - x1, slope, y1, diff, out p2y); }
                else if ((c2 & 2) != 0) { p2y = ct; ok = Major(y1 - ct, slope, x1, !diff, out p2x); }
                else { p2y = cb; ok = Major(cb - y1, slope, x1, diff, out p2x); }
            }
            if (!ok)
                return 2; // the asm would raise a divide overflow; cannot happen for sane lines
        }

        IndexedSurface s = clip.Surface;
        byte[] pixels = s.Pixels;
        int stepX = negX ? -1 : 1, stepY = negY ? -1 : 1;
        if (adx == ady)
        {
            DrawStraight(clip, p1x, p1y, stepX, stepY, Math.Abs(p2x - p1x) + 1, colour);
        }
        else if (shallow)
        {
            int count = Math.Abs(p2x - p1x) + 1;
            uint acc = unchecked((uint)((ulong)(uint)Math.Abs(p1x - x1) * slope) + 0x80000000u);
            int x = p1x, y = p1y;
            for (int i = 0; i < count; i++)
            {
                if (clip.Contains(x, y))
                    pixels[s.IndexOf(x, y)] = colour;
                x += stepX;
                uint old = acc;
                acc = unchecked(acc + slope);
                if (acc < old)
                    y += stepY;
            }
        }
        else
        {
            int count = Math.Abs(p2y - p1y) + 1;
            uint acc = unchecked((uint)((ulong)(uint)Math.Abs(p1y - y1) * slope) + 0x80000000u);
            int x = p1x, y = p1y;
            for (int i = 0; i < count; i++)
            {
                if (clip.Contains(x, y))
                    pixels[s.IndexOf(x, y)] = colour;
                y += stepY;
                uint old = acc;
                acc = unchecked(acc + slope);
                if (acc < old)
                    x += stepX;
            }
        }
        return seen != 0 ? 1 : 0;
    }

    /// <summary>
    /// Ellipse outline with horizontal radius <paramref name="radiusX"/> and vertical radius
    /// <paramref name="radiusY"/> (midpoint algorithm, 32-bit wrapping arithmetic as the asm).
    /// A zero radius degenerates to <see cref="DrawClippedLine"/>.
    /// </summary>
    /// <remarks>C: DrawRasterEllipse (asm 0x43CE80, screens_draw_raster_ellipse.inc).</remarks>
    public static int DrawRasterEllipse(in RasterClip clip, int x, int y, int radiusX, int radiusY, byte colour)
    {
        if (radiusX == 0 || radiusY == 0)
            return DrawClippedLine(clip, x - radiusX, y - radiusY, x + radiusX, y + radiusY, colour);
        if (clip.IsEmpty)
            return -2;
        var walker = new EllipseWalker(radiusX, radiusY);
        while (walker.InRegionOne)
        {
            Plot4(clip, x, y, walker.X, walker.Y, colour);
            walker.StepRegionOne();
        }
        walker.EnterRegionTwo();
        do
        {
            Plot4(clip, x, y, walker.X, walker.Y, colour);
        }
        while (walker.StepRegionTwo());
        return 0;
    }

    /// <summary>
    /// Filled ellipse: the same midpoint recurrence as <see cref="DrawRasterEllipse"/>, filling
    /// rows <c>y + py</c> and <c>y - py</c> between <c>x - px</c> and <c>x + px</c> at every step.
    /// </summary>
    /// <remarks>C: FillRasterEllipse (asm 0x43D1C1, screens.c). Ported from the asm body in
    /// screens.c, not from the portable C (which uses a different extent formula).</remarks>
    public static int FillRasterEllipse(in RasterClip clip, int x, int y, int radiusX, int radiusY, byte colour)
    {
        if (radiusX == 0 || radiusY == 0)
            return DrawClippedLine(clip, x - radiusX, y - radiusY, x + radiusX, y + radiusY, colour);
        if (clip.IsEmpty)
            return -2;
        var walker = new EllipseWalker(radiusX, radiusY);
        while (walker.InRegionOne)
        {
            FillPair(clip, x, y, walker.X, walker.Y, colour);
            walker.StepRegionOne();
        }
        walker.EnterRegionTwo();
        do
        {
            FillPair(clip, x, y, walker.X, walker.Y, colour);
        }
        while (walker.StepRegionTwo());
        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Outcode(int x, int y, int cl, int ct, int cr, int cb) =>
        (x < cl ? 8 : 0) | (x > cr ? 4 : 0) | (y < ct ? 2 : 0) | (y > cb ? 1 : 0);

    /// <summary>Clip along the major axis by a distance d: minor offset = (d * S + 2^31) >> 32.</summary>
    private static bool Major(int distance, uint slope, int origin, bool negate, out int result)
    {
        uint v = (uint)((((ulong)(uint)distance * slope) + 0x80000000UL) >> 32);
        result = unchecked(origin + (negate ? -(int)v : (int)v));
        return true;
    }

    /// <summary>Clip the start point along the minor axis: first DDA step whose minor offset reaches d.</summary>
    private static bool MinorFirst(int distance, uint slope, int origin, bool negate, out int result)
    {
        uint high = unchecked((uint)(distance - 1));
        if (high >= slope)
        {
            result = origin;
            return false;
        }
        ulong dividend = ((ulong)high << 32) | 0x80000000UL;
        ulong quotient = dividend / slope;
        uint steps = unchecked((uint)quotient + (dividend % slope != 0 ? 1u : 0u));
        result = unchecked(origin + (negate ? -(int)steps : (int)steps));
        return true;
    }

    /// <summary>Clip the end point along the minor axis: last DDA step whose minor offset stays within d.</summary>
    private static bool MinorLast(int distance, uint slope, int origin, bool negate, out int result)
    {
        uint high = unchecked((uint)distance);
        if (high >= slope)
        {
            result = origin;
            return false;
        }
        ulong dividend = ((ulong)high << 32) | 0x80000000UL;
        ulong quotient = dividend / slope;
        uint steps = unchecked((uint)quotient - (dividend % slope == 0 ? 1u : 0u));
        result = unchecked(origin + (negate ? -(int)steps : (int)steps));
        return true;
    }

    private static void DrawStraight(in RasterClip clip, int x, int y, int stepX, int stepY, int count, byte colour)
    {
        IndexedSurface s = clip.Surface;
        byte[] pixels = s.Pixels;
        for (int i = 0; i < count; i++)
        {
            if (clip.Contains(x, y))
                pixels[s.IndexOf(x, y)] = colour;
            x += stepX;
            y += stepY;
        }
    }

    private static void Plot4(in RasterClip clip, int cx, int cy, int px, int py, byte colour)
    {
        IndexedSurface s = clip.Surface;
        byte[] pixels = s.Pixels;
        if (clip.Contains(cx + px, cy + py))
            pixels[s.IndexOf(cx + px, cy + py)] = colour;
        if (clip.Contains(cx + px, cy - py))
            pixels[s.IndexOf(cx + px, cy - py)] = colour;
        if (clip.Contains(cx - px, cy + py))
            pixels[s.IndexOf(cx - px, cy + py)] = colour;
        if (clip.Contains(cx - px, cy - py))
            pixels[s.IndexOf(cx - px, cy - py)] = colour;
    }

    private static void FillPair(in RasterClip clip, int cx, int cy, int px, int py, byte colour)
    {
        int right = cx + px;
        if (right < clip.Left)
            return;
        if (right > clip.Right)
            right = clip.Right;
        int left = cx - px;
        if (left > clip.Right)
            return;
        if (left < clip.Left)
            left = clip.Left;
        int count = right - left + 1;
        if (count <= 0)
            return;
        IndexedSurface s = clip.Surface;
        int lower = cy + py;
        if (lower < clip.Top)
            return;
        if (lower <= clip.Bottom)
            s.Pixels.AsSpan(s.IndexOf(left, lower), count).Fill(colour);
        int upper = cy - py;
        if (upper < clip.Top || upper > clip.Bottom)
            return;
        s.Pixels.AsSpan(s.IndexOf(left, upper), count).Fill(colour);
    }

    /// <summary>The asm midpoint recurrence shared by the outline and the filled ellipse.</summary>
    private struct EllipseWalker
    {
        private readonly int _ry2, _twoRy2, _rx2, _twoRx2;
        private int _dX, _dY, _d, _remaining;

        public EllipseWalker(int radiusX, int radiusY)
        {
            unchecked
            {
                _ry2 = radiusY * radiusY;
                _twoRy2 = _ry2 << 1;
                _rx2 = radiusX * radiusX;
                _twoRx2 = _rx2 << 1;
                _dX = 0;
                _dY = _twoRx2 * radiusY;
                _d = (int)((uint)_rx2 >> 2) + _ry2 - _rx2 * radiusY;
            }
            _remaining = radiusY;
            X = 0;
            Y = radiusY;
        }

        public int X { get; private set; }

        public int Y { get; private set; }

        public readonly bool InRegionOne => unchecked(_dX - _dY) < 0;

        public void StepRegionOne()
        {
            unchecked
            {
                if (_d >= 0)
                {
                    Y--;
                    _remaining--;
                    _dY -= _twoRx2;
                    _d -= _dY;
                }
                X++;
                _dX += _twoRy2;
                _d += _dX + _ry2;
            }
        }

        public void EnterRegionTwo()
        {
            unchecked
            {
                int e = _rx2 - _ry2;
                _d += ((e >> 1) + e - _dX - _dY) >> 1;
            }
        }

        /// <summary>Advances one row; returns false when the bottom row has been plotted.</summary>
        public bool StepRegionTwo()
        {
            unchecked
            {
                if (_d < 0)
                {
                    X++;
                    _dX += _twoRy2;
                    _d += _dX;
                }
                Y--;
                _dY -= _twoRx2;
                _d -= _dY - _rx2;
                _remaining--;
            }
            return _remaining >= 0;
        }
    }
}
