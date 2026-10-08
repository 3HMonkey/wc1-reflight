namespace WingCommander.Graphics.Shapes;

/// <summary>
/// Extents of one shape frame relative to its origin (hot spot). All four values are
/// inclusive distances, so <c>Width = Left + Right + 1</c> and <c>Height = Top + Bottom + 1</c>;
/// the pixel range is <c>[-Left, +Right] x [-Top, +Bottom]</c> around the hot spot.
/// </summary>
/// <remarks>C: the four i16 at the start of a frame record (GetShapeFrameExtents 0x4408F0, killbrd.c).</remarks>
public readonly record struct ShapeExtents(short Right, short Left, short Top, short Bottom)
{
    public int Width => Left + Right + 1;

    public int Height => Top + Bottom + 1;
}
