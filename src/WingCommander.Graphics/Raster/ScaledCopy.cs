namespace WingCommander.Graphics.Raster;

/// <summary>
/// A nearest-neighbour scaled copy (port addition): the source rectangle stretched over the
/// destination rectangle, written only inside the clip. All coordinates are buffer coordinates of
/// the two surfaces (0,0 = first pixel), edges inclusive for the clip. Destination pixel
/// (x, y) takes the source pixel under its centre, <see cref="SourceX"/>/<see cref="SourceY"/>;
/// the blit and the text tracker both use these so they agree on every pixel.
/// </summary>
public readonly record struct ScaledCopy(
    int SourceLeft,
    int SourceTop,
    int SourceWidth,
    int SourceHeight,
    int DestinationLeft,
    int DestinationTop,
    int DestinationWidth,
    int DestinationHeight,
    int ClipLeft,
    int ClipTop,
    int ClipRight,
    int ClipBottom)
{
    /// <summary>Destination pixels per source pixel, horizontally.</summary>
    public float ScaleX => DestinationWidth / (float)SourceWidth;

    /// <summary>Destination pixels per source pixel, vertically.</summary>
    public float ScaleY => DestinationHeight / (float)SourceHeight;

    public bool IsEmpty => ClipRight < ClipLeft || ClipBottom < ClipTop || SourceWidth <= 0 || SourceHeight <= 0;

    /// <summary>The source column under the centre of destination column <paramref name="x"/>.</summary>
    public int SourceX(int x) =>
        SourceLeft + (int)(((long)(x - DestinationLeft) * 2 + 1) * SourceWidth / (2L * DestinationWidth));

    /// <summary>The source row under the centre of destination row <paramref name="y"/>.</summary>
    public int SourceY(int y) =>
        SourceTop + (int)(((long)(y - DestinationTop) * 2 + 1) * SourceHeight / (2L * DestinationHeight));

    /// <summary>The source columns destination column <paramref name="x"/> covers: [start, end), at least one.</summary>
    public (int Start, int End) CoveredColumns(int x) => Covered(x - DestinationLeft, SourceWidth, DestinationWidth, SourceLeft);

    /// <summary>The source rows destination row <paramref name="y"/> covers: [start, end), at least one.</summary>
    public (int Start, int End) CoveredRows(int y) => Covered(y - DestinationTop, SourceHeight, DestinationHeight, SourceTop);

    private static (int Start, int End) Covered(int offset, int sourceLength, int destinationLength, int sourceStart)
    {
        int start = sourceStart + (int)((long)offset * sourceLength / destinationLength);
        int end = sourceStart + (int)(((long)(offset + 1) * sourceLength + destinationLength - 1) / destinationLength);
        return (start, Math.Max(start + 1, end));
    }
}
