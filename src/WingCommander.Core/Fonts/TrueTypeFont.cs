using System.Buffers.Binary;
using System.Numerics;

namespace WingCommander.Core.Fonts;

/// <summary>
/// One glyph outline of a <see cref="TrueTypeFont"/> in font units (y up): closed contours with
/// quadratic curves flattened to line segments, the advance and the bounding box.
/// </summary>
public sealed class TrueTypeGlyph
{
    public TrueTypeGlyph(int advanceWidth, int leftSideBearing, IReadOnlyList<Vector2[]> contours)
    {
        ArgumentNullException.ThrowIfNull(contours);
        AdvanceWidth = advanceWidth;
        LeftSideBearing = leftSideBearing;
        Contours = contours;
        float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
        foreach (var contour in contours)
        {
            foreach (Vector2 p in contour)
            {
                xMin = MathF.Min(xMin, p.X);
                yMin = MathF.Min(yMin, p.Y);
                xMax = MathF.Max(xMax, p.X);
                yMax = MathF.Max(yMax, p.Y);
            }
        }
        if (xMin > xMax)
            xMin = yMin = xMax = yMax = 0f;
        XMin = xMin;
        YMin = yMin;
        XMax = xMax;
        YMax = yMax;
    }

    public int AdvanceWidth { get; }

    public int LeftSideBearing { get; }

    /// <summary>Closed polygons (the last point connects to the first), font units, y up.</summary>
    public IReadOnlyList<Vector2[]> Contours { get; }

    public float XMin { get; }

    public float YMin { get; }

    public float XMax { get; }

    public float YMax { get; }

    public bool IsEmpty => Contours.Count == 0;
}

/// <summary>
/// Minimal reader for TrueType fonts (glyf outlines): character map (formats 4 and 12), glyph
/// outlines including composite glyphs, horizontal advances and the vertical metrics of the
/// OS/2 table. Enough to turn a replacement font into distance fields (ADR-013); hinting,
/// kerning and CFF outlines (.otf with 'OTTO') are not supported.
/// </summary>
public sealed class TrueTypeFont
{
    private const int MaximumCurveSegments = 8;

    private readonly byte[] _data;
    private readonly Dictionary<uint, (int Offset, int Length)> _tables = [];
    private readonly int _numberOfHMetrics;
    private readonly bool _longLocations;
    private readonly int _cmapOffset;
    private readonly int _cmapFormat;
    private readonly Dictionary<int, TrueTypeGlyph> _glyphs = [];

    private TrueTypeFont(byte[] data)
    {
        _data = data;
        ReadOnlySpan<byte> span = data;
        if (data.Length < 12)
            throw new InvalidDataException("The file is too short for a font.");
        uint version = BinaryPrimitives.ReadUInt32BigEndian(span);
        if (version == Tag("OTTO"))
            throw new NotSupportedException("Fonts with CFF outlines (OpenType 'OTTO') are not supported; use a TrueType (glyf) font.");
        if (version != 0x00010000 && version != Tag("true"))
            throw new InvalidDataException($"Not a TrueType font (version 0x{version:X8}).");
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(span[4..]);
        for (int i = 0; i < tableCount; i++)
        {
            int record = 12 + i * 16;
            if (record + 16 > data.Length)
                throw new InvalidDataException("The table directory is truncated.");
            uint tag = BinaryPrimitives.ReadUInt32BigEndian(span[record..]);
            int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(span[(record + 8)..]));
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(span[(record + 12)..]));
            if (offset < 0 || length < 0 || offset + length > data.Length)
                throw new InvalidDataException("A font table lies outside the file.");
            _tables[tag] = (offset, length);
        }

        int head = Table("head", 54);
        UnitsPerEm = U16(head + 18);
        _longLocations = I16(head + 50) != 0;
        GlyphCount = U16(Table("maxp", 6) + 4);
        int hhea = Table("hhea", 36);
        Ascender = I16(hhea + 4);
        Descender = I16(hhea + 6);
        _numberOfHMetrics = U16(hhea + 34);
        Table("hmtx", 4 * _numberOfHMetrics);
        Table("loca", 2);
        Table("glyf", 0);

        CapHeight = Ascender;
        XHeight = Ascender / 2;
        if (_tables.TryGetValue(Tag("OS/2"), out var os2) && os2.Length >= 78)
        {
            int table = os2.Offset;
            int os2Version = U16(table);
            Ascender = I16(table + 68);
            Descender = I16(table + 70);
            CapHeight = Ascender;
            if (os2Version >= 2 && os2.Length >= 90)
            {
                XHeight = I16(table + 86);
                CapHeight = I16(table + 88);
            }
        }

        (_cmapOffset, _cmapFormat) = SelectCharacterMap();
    }

    /// <summary>Design units per em.</summary>
    public int UnitsPerEm { get; }

    public int GlyphCount { get; }

    /// <summary>Typographic ascender (OS/2, else hhea), font units above the baseline.</summary>
    public int Ascender { get; }

    /// <summary>Typographic descender (negative), font units.</summary>
    public int Descender { get; }

    /// <summary>Height of capital letters (OS/2 version 2+, else the ascender).</summary>
    public int CapHeight { get; }

    /// <summary>Height of lower-case x (OS/2 version 2+, else half the ascender).</summary>
    public int XHeight { get; }

    public static TrueTypeFont Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new TrueTypeFont(File.ReadAllBytes(path));
    }

    public static TrueTypeFont Load(ReadOnlySpan<byte> data) => new(data.ToArray());

    /// <summary>The glyph index of a Unicode code point; false when the font has none (index 0 = .notdef).</summary>
    public bool TryGetGlyphIndex(int codePoint, out int glyphIndex)
    {
        glyphIndex = _cmapFormat switch
        {
            4 => LookUpFormat4(codePoint),
            12 => LookUpFormat12(codePoint),
            _ => 0,
        };
        return glyphIndex > 0 && glyphIndex < GlyphCount;
    }

    /// <summary>The glyph for a Unicode code point, or null when the font does not have it.</summary>
    public TrueTypeGlyph? GetGlyphForCodePoint(int codePoint) =>
        TryGetGlyphIndex(codePoint, out int index) ? GetGlyph(index) : null;

    /// <summary>Outline and metrics of a glyph index (cached).</summary>
    public TrueTypeGlyph GetGlyph(int glyphIndex)
    {
        if ((uint)glyphIndex >= (uint)GlyphCount)
            throw new ArgumentOutOfRangeException(nameof(glyphIndex));
        if (!_glyphs.TryGetValue(glyphIndex, out var glyph))
        {
            var contours = new List<Vector2[]>();
            ReadOutline(glyphIndex, Matrix3x2.Identity, contours, depth: 0);
            (int advance, int bearing) = HorizontalMetrics(glyphIndex);
            _glyphs[glyphIndex] = glyph = new TrueTypeGlyph(advance, bearing, contours);
        }
        return glyph;
    }

    // ---- tables ------------------------------------------------------------------------------

    private static uint Tag(string tag) =>
        (uint)(tag[0] << 24 | tag[1] << 16 | tag[2] << 8 | tag[3]);

    private int Table(string tag, int minimumLength)
    {
        if (!_tables.TryGetValue(Tag(tag), out var table))
            throw new InvalidDataException($"The font has no '{tag}' table.");
        if (table.Length < minimumLength)
            throw new InvalidDataException($"The '{tag}' table is truncated.");
        return table.Offset;
    }

    private int U16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(offset, 2));

    private int I16(int offset) => BinaryPrimitives.ReadInt16BigEndian(_data.AsSpan(offset, 2));

    private uint U32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(offset, 4));

    private (int Advance, int Bearing) HorizontalMetrics(int glyphIndex)
    {
        int hmtx = _tables[Tag("hmtx")].Offset;
        if (glyphIndex < _numberOfHMetrics)
            return (U16(hmtx + glyphIndex * 4), I16(hmtx + glyphIndex * 4 + 2));
        int advance = U16(hmtx + (_numberOfHMetrics - 1) * 4);
        int bearingOffset = hmtx + _numberOfHMetrics * 4 + (glyphIndex - _numberOfHMetrics) * 2;
        int bearing = bearingOffset + 2 <= _tables[Tag("hmtx")].Offset + _tables[Tag("hmtx")].Length ? I16(bearingOffset) : 0;
        return (advance, bearing);
    }

    // ---- character map ----------------------------------------------------------------------

    private (int Offset, int Format) SelectCharacterMap()
    {
        int cmap = Table("cmap", 4);
        int count = U16(cmap + 2);
        int best = -1, bestFormat = 0, bestRank = int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            int record = cmap + 4 + i * 8;
            int platform = U16(record);
            int encoding = U16(record + 2);
            int offset = cmap + (int)U32(record + 4);
            if (offset + 4 > _data.Length)
                continue;
            int format = U16(offset);
            int rank = (platform, encoding, format) switch
            {
                (3, 10, 12) => 0,
                (0, _, 12) => 1,
                (3, 1, 4) => 2,
                (0, _, 4) => 3,
                _ => int.MaxValue,
            };
            if (rank < bestRank)
            {
                bestRank = rank;
                best = offset;
                bestFormat = format;
            }
        }
        if (best < 0)
            throw new InvalidDataException("The font has no Unicode character map (format 4 or 12).");
        return (best, bestFormat);
    }

    private int LookUpFormat4(int codePoint)
    {
        if (codePoint is < 0 or > 0xFFFF)
            return 0;
        int table = _cmapOffset;
        int segments = U16(table + 6) / 2;
        int endCodes = table + 14;
        int startCodes = endCodes + segments * 2 + 2;
        int deltas = startCodes + segments * 2;
        int rangeOffsets = deltas + segments * 2;
        for (int i = 0; i < segments; i++)
        {
            int end = U16(endCodes + i * 2);
            if (end < codePoint)
                continue;
            int start = U16(startCodes + i * 2);
            if (start > codePoint)
                return 0;
            int delta = I16(deltas + i * 2);
            int rangeOffsetPosition = rangeOffsets + i * 2;
            int rangeOffset = U16(rangeOffsetPosition);
            if (rangeOffset == 0)
                return (codePoint + delta) & 0xFFFF;
            int glyphPosition = rangeOffsetPosition + rangeOffset + (codePoint - start) * 2;
            if (glyphPosition + 2 > _data.Length)
                return 0;
            int glyph = U16(glyphPosition);
            return glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
        }
        return 0;
    }

    private int LookUpFormat12(int codePoint)
    {
        int table = _cmapOffset;
        uint groups = U32(table + 12);
        for (uint i = 0; i < groups; i++)
        {
            int group = table + 16 + (int)i * 12;
            uint start = U32(group), end = U32(group + 4);
            if ((uint)codePoint >= start && (uint)codePoint <= end)
                return (int)(U32(group + 8) + ((uint)codePoint - start));
        }
        return 0;
    }

    // ---- outlines --------------------------------------------------------------------------

    private (int Offset, int Length) GlyphLocation(int glyphIndex)
    {
        int loca = _tables[Tag("loca")].Offset;
        int glyf = _tables[Tag("glyf")].Offset;
        int start, end;
        if (_longLocations)
        {
            start = (int)U32(loca + glyphIndex * 4);
            end = (int)U32(loca + glyphIndex * 4 + 4);
        }
        else
        {
            start = U16(loca + glyphIndex * 2) * 2;
            end = U16(loca + glyphIndex * 2 + 2) * 2;
        }
        if (end < start || glyf + end > _data.Length)
            throw new InvalidDataException($"Glyph {glyphIndex} lies outside the 'glyf' table.");
        return (glyf + start, end - start);
    }

    private void ReadOutline(int glyphIndex, Matrix3x2 transform, List<Vector2[]> contours, int depth)
    {
        if (depth > 8)
            throw new InvalidDataException("Composite glyphs nest too deeply.");
        (int offset, int length) = GlyphLocation(glyphIndex);
        if (length == 0)
            return;
        int contourCount = I16(offset);
        if (contourCount >= 0)
            ReadSimpleOutline(offset, contourCount, transform, contours);
        else
            ReadCompositeOutline(offset, transform, contours, depth);
    }

    private void ReadSimpleOutline(int offset, int contourCount, Matrix3x2 transform, List<Vector2[]> contours)
    {
        if (contourCount == 0)
            return;
        int position = offset + 10;
        var ends = new int[contourCount];
        for (int i = 0; i < contourCount; i++)
            ends[i] = U16(position + i * 2);
        position += contourCount * 2;
        int pointCount = ends[^1] + 1;
        int instructionLength = U16(position);
        position += 2 + instructionLength;

        var flags = new byte[pointCount];
        for (int i = 0; i < pointCount;)
        {
            byte flag = _data[position++];
            flags[i++] = flag;
            if ((flag & 0x08) != 0)
            {
                int repeat = _data[position++];
                for (int r = 0; r < repeat && i < pointCount; r++)
                    flags[i++] = flag;
            }
        }
        var xs = new int[pointCount];
        var ys = new int[pointCount];
        int value = 0;
        for (int i = 0; i < pointCount; i++)
        {
            byte flag = flags[i];
            if ((flag & 0x02) != 0)
            {
                int delta = _data[position++];
                value += (flag & 0x10) != 0 ? delta : -delta;
            }
            else if ((flag & 0x10) == 0)
            {
                value += I16(position);
                position += 2;
            }
            xs[i] = value;
        }
        value = 0;
        for (int i = 0; i < pointCount; i++)
        {
            byte flag = flags[i];
            if ((flag & 0x04) != 0)
            {
                int delta = _data[position++];
                value += (flag & 0x20) != 0 ? delta : -delta;
            }
            else if ((flag & 0x20) == 0)
            {
                value += I16(position);
                position += 2;
            }
            ys[i] = value;
        }

        int first = 0;
        foreach (int end in ends)
        {
            if (end >= first)
            {
                Vector2[] polygon = FlattenContour(xs, ys, flags, first, end, transform);
                if (polygon.Length >= 3)
                    contours.Add(polygon);
            }
            first = end + 1;
        }
    }

    /// <summary>Turns one TrueType contour (on/off-curve points, implied midpoints) into a polygon.</summary>
    private Vector2[] FlattenContour(int[] xs, int[] ys, byte[] flags, int first, int last, Matrix3x2 transform)
    {
        float tolerance = UnitsPerEm / 64f; // control-polygon length per segment
        int count = last - first + 1;
        var points = new List<(Vector2 Point, bool OnCurve)>(count * 2);
        for (int i = 0; i < count; i++)
        {
            int index = first + i;
            var point = new Vector2(xs[index], ys[index]);
            bool on = (flags[index] & 0x01) != 0;
            if (i > 0 && !on && !points[^1].OnCurve)
                points.Add(((points[^1].Point + point) * 0.5f, true));
            points.Add((point, on));
        }
        if (points.Count == 0)
            return [];
        if (!points[0].OnCurve && !points[^1].OnCurve)
            points.Add(((points[0].Point + points[^1].Point) * 0.5f, true));
        // Rotate so that the contour starts on an on-curve point.
        int start = points.FindIndex(p => p.OnCurve);
        if (start < 0)
            return [];
        var result = new List<Vector2>(points.Count * 2);
        int n = points.Count;
        Vector2 current = points[start].Point;
        result.Add(current);
        for (int k = 1; k <= n; k++)
        {
            var (point, on) = points[(start + k) % n];
            if (on)
            {
                if (k < n)
                    result.Add(point);
                current = point;
                continue;
            }
            var (next, _) = points[(start + k + 1) % n];
            // Short curves (chalk and brush fonts have thousands) need fewer segments.
            float span = Vector2.Distance(current, point) + Vector2.Distance(point, next);
            int segments = Math.Clamp((int)MathF.Ceiling(span / tolerance), 1, MaximumCurveSegments);
            for (int s = 1; s <= segments; s++)
            {
                float t = s / (float)segments;
                float u = 1 - t;
                Vector2 curvePoint = u * u * current + 2 * u * t * point + t * t * next;
                if (!(k + 1 >= n && s == segments))
                    result.Add(curvePoint);
            }
            current = next;
            k++;
        }
        var output = new Vector2[result.Count];
        for (int i = 0; i < output.Length; i++)
            output[i] = Vector2.Transform(result[i], transform);
        return output;
    }

    private void ReadCompositeOutline(int offset, Matrix3x2 transform, List<Vector2[]> contours, int depth)
    {
        int position = offset + 10;
        while (true)
        {
            int flags = U16(position);
            int component = U16(position + 2);
            position += 4;
            float dx, dy;
            if ((flags & 0x0001) != 0)
            {
                dx = I16(position);
                dy = I16(position + 2);
                position += 4;
            }
            else
            {
                dx = (sbyte)_data[position];
                dy = (sbyte)_data[position + 1];
                position += 2;
            }
            if ((flags & 0x0002) == 0)
                dx = dy = 0; // point matching (rare) is not supported
            float a = 1, b = 0, c = 0, d = 1;
            if ((flags & 0x0008) != 0)
            {
                a = d = F2Dot14(position);
                position += 2;
            }
            else if ((flags & 0x0040) != 0)
            {
                a = F2Dot14(position);
                d = F2Dot14(position + 2);
                position += 4;
            }
            else if ((flags & 0x0080) != 0)
            {
                a = F2Dot14(position);
                b = F2Dot14(position + 2);
                c = F2Dot14(position + 4);
                d = F2Dot14(position + 6);
                position += 8;
            }
            var local = new Matrix3x2(a, b, c, d, dx, dy);
            if (component < GlyphCount)
                ReadOutline(component, local * transform, contours, depth + 1);
            if ((flags & 0x0020) == 0)
                break;
        }
    }

    private float F2Dot14(int offset) => I16(offset) / 16384f;
}
