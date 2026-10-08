using System.Numerics;

namespace WingCommander.Core.Imaging;

/// <summary>
/// Vectorizes small binary pixel images (font glyphs, ADR-013): traces the pixel boundaries,
/// replaces one-pixel staircase steps by diagonals while keeping real corners and stroke ends
/// square, and evaluates the signed distance to the resulting polygons on a finer grid.
/// </summary>
/// <remarks>
/// <para>Tracing treats the foreground as 8-connected (pixels touching at a corner belong to one
/// shape, so one-pixel diagonal strokes stay connected). Contours run with the foreground on
/// their right in screen coordinates (y down): outer boundaries clockwise, holes counter-clockwise.</para>
/// <para>Smoothing rules on the axis-aligned segments of a contour: a segment of length 1 whose
/// two corners turn in opposite directions is a staircase <b>step</b> and is replaced by its
/// midpoint; a run of length 1..<see cref="AbsorbedRunLength"/> between two steps that go the same
/// way is part of the slope and disappears; any other run keeps its corners, except that the end
/// touching a step moves half a pixel (at most half its length) into the run. One-pixel caps
/// (both corners turn the same way: stroke ends, arms of small crosses), one or two steps next to
/// such a cap (the inner corners of +, # and *), notches, corners between longer runs and
/// isolated pixels stay exactly on the pixel grid.</para>
/// </remarks>
public static class PixelOutline
{
    /// <summary>Longest run between two same-direction steps that is absorbed into the slope.</summary>
    public const int AbsorbedRunLength = 2;

    /// <summary>
    /// Traces the boundaries of the true pixels of <paramref name="mask"/>. Each contour is the
    /// list of its corner points (collinear unit edges merged), closed implicitly.
    /// </summary>
    public static List<List<Point>> Trace(ReadOnlySpan<bool> mask, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (mask.Length < width * height)
            throw new ArgumentException("The mask is smaller than width * height.", nameof(mask));

        // Boundary unit edges, oriented with the foreground on the right; indexed by start vertex.
        int vertexStride = width + 1;
        var outgoing = new Dictionary<int, List<int>>();
        var edges = new List<(Point From, Point To)>();
        bool[] pixels = mask[..(width * height)].ToArray();
        bool Inside(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height && pixels[y * width + x];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!mask[y * width + x])
                    continue;
                if (!Inside(x, y - 1))
                    AddEdge(new Point(x, y), new Point(x + 1, y));
                if (!Inside(x + 1, y))
                    AddEdge(new Point(x + 1, y), new Point(x + 1, y + 1));
                if (!Inside(x, y + 1))
                    AddEdge(new Point(x + 1, y + 1), new Point(x, y + 1));
                if (!Inside(x - 1, y))
                    AddEdge(new Point(x, y + 1), new Point(x, y));
            }
        }

        var used = new bool[edges.Count];
        var contours = new List<List<Point>>();
        for (int start = 0; start < edges.Count; start++)
        {
            if (used[start])
                continue;
            var path = new List<Point>();
            int edge = start;
            while (true)
            {
                used[edge] = true;
                var (from, to) = edges[edge];
                path.Add(from);
                Point direction = to - from;
                int next = -1;
                foreach (int candidate in outgoing[Key(to)])
                {
                    if (used[candidate] && candidate != start)
                        continue;
                    if (next == -1)
                    {
                        next = candidate;
                        continue;
                    }
                    // Saddle (two diagonal foreground pixels): take the left turn, which keeps the
                    // diagonal pixels in one 8-connected shape.
                    Point d = edges[candidate].To - edges[candidate].From;
                    if (Cross(direction, d) < 0)
                        next = candidate;
                }
                if (next == -1 || next == start)
                    break;
                edge = next;
            }
            contours.Add(MergeCollinear(path));
        }
        return contours;

        int Key(Point p) => p.Y * vertexStride + p.X;

        void AddEdge(Point from, Point to)
        {
            int index = edges.Count;
            edges.Add((from, to));
            int key = Key(from);
            if (!outgoing.TryGetValue(key, out var list))
                outgoing[key] = list = [];
            list.Add(index);
        }
    }

    /// <summary>Applies the staircase rules (see the type remarks) to one traced contour.</summary>
    public static List<Vector2> Smooth(IReadOnlyList<Point> corners)
    {
        ArgumentNullException.ThrowIfNull(corners);
        int n = corners.Count;
        var result = new List<Vector2>(n * 2);
        if (n < 4)
        {
            foreach (Point p in corners)
                result.Add(new Vector2(p.X, p.Y));
            return result;
        }

        // Segment i runs from corner i to corner i + 1; turn i is the turn at corner i.
        var length = new int[n];
        var direction = new Point[n];
        var turn = new int[n];
        for (int i = 0; i < n; i++)
        {
            Point d = corners[(i + 1) % n] - corners[i];
            length[i] = Math.Abs(d.X) + Math.Abs(d.Y);
            direction[i] = new Point(Math.Sign(d.X), Math.Sign(d.Y));
        }
        for (int i = 0; i < n; i++)
            turn[i] = Math.Sign(Cross(direction[(i + n - 1) % n], direction[i]));

        var step = new bool[n];
        var cap = new bool[n];
        for (int i = 0; i < n; i++)
        {
            step[i] = length[i] == 1 && turn[i] != turn[(i + 1) % n];
            cap[i] = length[i] == 1 && turn[i] == turn[(i + 1) % n];
        }
        // One or two steps next to a one-pixel cap are the inner corners of thin crosses and
        // stems (+, #, *), not a slope: they stay sharp.
        if (Array.IndexOf(step, false) >= 0)
        {
            int first = Array.IndexOf(step, false);
            for (int k = 1; k <= n; k++)
            {
                int i = (first + k) % n;
                if (!step[i] || step[(i + n - 1) % n])
                    continue;
                int count = 0;
                while (count < n && step[(i + count) % n])
                    count++;
                int before = (i + n - 1) % n, after = (i + count) % n;
                if (count <= 2 && (cap[before] || cap[after]))
                {
                    for (int j = 0; j < count; j++)
                        step[(i + j) % n] = false;
                }
            }
        }
        var absorbed = new bool[n];
        for (int i = 0; i < n; i++)
        {
            int previous = (i + n - 1) % n, next = (i + 1) % n;
            absorbed[i] = !step[i] && length[i] <= AbsorbedRunLength && step[previous] && step[next]
                && direction[previous] == direction[next];
        }

        for (int i = 0; i < n; i++)
        {
            int previous = (i + n - 1) % n, next = (i + 1) % n;
            var start = new Vector2(corners[i].X, corners[i].Y);
            var end = new Vector2(corners[next].X, corners[next].Y);
            if (step[i])
            {
                Add((start + end) * 0.5f);
                continue;
            }
            if (absorbed[i])
                continue;
            var unit = new Vector2(direction[i].X, direction[i].Y);
            // A one-pixel cap (the end of a one-pixel stroke or arm) keeps its corners.
            float inset = cap[i] ? 0f : MathF.Min(0.5f, length[i] * 0.5f);
            Add(step[previous] ? start + unit * inset : start);
            if (step[next])
                Add(end - unit * inset);
        }
        if (result.Count > 1 && result[0] == result[^1])
            result.RemoveAt(result.Count - 1);
        return result;

        void Add(Vector2 point)
        {
            if (result.Count == 0 || result[^1] != point)
                result.Add(point);
        }
    }

    /// <summary>
    /// Signed distance (positive inside, non-zero winding rule) from the polygons, sampled at the centres
    /// of a <paramref name="width"/> x <paramref name="height"/> grid: sample (i, j) lies at
    /// (<paramref name="originX"/> + (i + 0.5) / <paramref name="scale"/>, originY + (j + 0.5) / scale)
    /// in polygon units; distances are in grid cells, clamped to ±<paramref name="limit"/>.
    /// </summary>
    public static void SignedDistance(IReadOnlyList<IReadOnlyList<Vector2>> polygons, int width, int height,
        float scale, float originX, float originY, Span<float> result, float limit)
    {
        ArgumentNullException.ThrowIfNull(polygons);
        int count = width * height;
        if (result.Length < count)
            throw new ArgumentException("The result is smaller than width * height.", nameof(result));
        Span<float> distance = result[..count];
        distance.Fill(limit);

        // Unsigned distance near every edge (grid units), then the sign from a scanline fill.
        foreach (var polygon in polygons)
        {
            int n = polygon.Count;
            for (int k = 0; k < n; k++)
            {
                Vector2 a = (polygon[k] - new Vector2(originX, originY)) * scale;
                Vector2 b = (polygon[(k + 1) % n] - new Vector2(originX, originY)) * scale;
                int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, b.X) - limit));
                int x1 = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(a.X, b.X) + limit));
                int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, b.Y) - limit));
                int y1 = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(a.Y, b.Y) + limit));
                Vector2 ab = b - a;
                float lengthSquared = Vector2.Dot(ab, ab);
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float t = lengthSquared > 0f ? Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f) : 0f;
                        float d = Vector2.Distance(p, a + ab * t);
                        ref float slot = ref distance[y * width + x];
                        if (d < slot)
                            slot = d;
                    }
                }
            }
        }

        // Non-zero winding (the TrueType rule; equal to even-odd for traced pixel outlines).
        var crossings = new List<(float X, int Winding)>();
        for (int y = 0; y < height; y++)
        {
            float sampleY = originY + (y + 0.5f) / scale;
            crossings.Clear();
            foreach (var polygon in polygons)
            {
                int n = polygon.Count;
                for (int k = 0; k < n; k++)
                {
                    Vector2 a = polygon[k], b = polygon[(k + 1) % n];
                    if ((a.Y <= sampleY) == (b.Y <= sampleY))
                        continue;
                    crossings.Add((a.X + (sampleY - a.Y) / (b.Y - a.Y) * (b.X - a.X), b.Y > a.Y ? 1 : -1));
                }
            }
            crossings.Sort((l, r) => l.X.CompareTo(r.X));
            int winding = 0;
            for (int c = 0; c + 1 < crossings.Count; c++)
            {
                winding += crossings[c].Winding;
                if (winding == 0)
                    continue;
                int xStart = Math.Max(0, (int)MathF.Ceiling((crossings[c].X - originX) * scale - 0.5f));
                int xEnd = Math.Min(width - 1, (int)MathF.Ceiling((crossings[c + 1].X - originX) * scale - 0.5f) - 1);
                for (int x = xStart; x <= xEnd; x++)
                    distance[y * width + x] = -distance[y * width + x];
            }
        }
        // Inside samples were negated above; flip so that inside is positive.
        for (int i = 0; i < count; i++)
            distance[i] = -distance[i];
    }

    private static List<Point> MergeCollinear(List<Point> path)
    {
        var corners = new List<Point>(path.Count);
        int n = path.Count;
        for (int i = 0; i < n; i++)
        {
            Point previous = path[(i + n - 1) % n], current = path[i], next = path[(i + 1) % n];
            if (Cross(current - previous, next - current) != 0)
                corners.Add(current);
        }
        return corners;
    }

    private static int Cross(Point a, Point b) => a.X * b.Y - a.Y * b.X;

    /// <summary>A lattice point (pixel corner).</summary>
    public readonly record struct Point(int X, int Y)
    {
        public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
    }
}
