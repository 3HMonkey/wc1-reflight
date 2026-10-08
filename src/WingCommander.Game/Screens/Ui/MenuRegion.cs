namespace WingCommander.Game.Screens.Ui;

/// <summary>
/// One hit rectangle of a menu: the cursor frame shown while the pointer is over it and an
/// inclusive rectangle in screen coordinates. A region whose frame is <see cref="EndOfList"/>
/// terminates a region table.
/// </summary>
/// <remarks>C: TitleMenuRegion (include/wcdata.h): <c>short frame, left, top, right, bottom</c>.</remarks>
public struct MenuRegion
{
    /// <summary>Frame value that ends a region table.</summary>
    public const short EndOfList = -1;

    public short Frame;
    public short Left;
    public short Top;
    public short Right;
    public short Bottom;

    public MenuRegion(short frame, short left, short top, short right, short bottom)
    {
        Frame = frame;
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    /// <summary>True when (x, y) lies inside the inclusive rectangle.</summary>
    /// <remarks>C: IsPointInRect (0x435090, mathfp.c).</remarks>
    public readonly bool Contains(short x, short y) => Left <= x && x <= Right && Top <= y && y <= Bottom;

    /// <summary>Stores <c>{left, top, right, bottom}</c> (the layout GetShapeFrameBounds writes through <c>&amp;region.left</c>).</summary>
    public void SetRect(ReadOnlySpan<short> bounds)
    {
        Left = bounds[0];
        Top = bounds[1];
        Right = bounds[2];
        Bottom = bounds[3];
    }

    /// <summary>
    /// Index of the first region containing the point, scanning until the terminating region;
    /// -1 when none does.
    /// </summary>
    /// <remarks>C: FindMenuRegionAtPoint (0x43F7C0, killbrd.c).</remarks>
    public static int FindMenuRegionAtPoint(ReadOnlySpan<MenuRegion> regions, short x, short y)
    {
        for (int index = 0; index < regions.Length && regions[index].Frame != EndOfList; index++)
        {
            if (regions[index].Contains(x, y))
                return index;
        }
        return -1;
    }

    public override readonly string ToString() => $"[{Frame}] ({Left},{Top})-({Right},{Bottom})";
}
