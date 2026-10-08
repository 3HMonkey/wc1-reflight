using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

/// <summary>
/// Sprite bounds query of the space view. The simulation decides with it whether an object is
/// really visible on screen (<c>easy2see</c>: invisible asteroids and mines are removed instead of
/// colliding), so the result is gameplay-relevant. Implemented by the Game with
/// <c>WingCommander.Graphics.ShapeBounds.GetTransformedShapeBounds</c> on the space view buffer.
/// </summary>
public interface IShapeBounds
{
    /// <summary>
    /// Fills <paramref name="bounds"/> (left, top, right, bottom) with the box of frame
    /// <paramref name="frame"/> of <paramref name="shape"/> drawn rotated by <paramref name="angle"/>
    /// degrees, scaled by <paramref name="scale"/> (8.8) with its hot spot at screen position
    /// (<paramref name="x"/>, <paramref name="y"/>) and returns non-zero when the box intersects the
    /// space viewport. A shape that is not loaded (<see cref="ShapeRef.None"/> or a missing
    /// section) is tested as a point.
    /// </summary>
    /// <remarks>C: GetTransformedShapeBounds(&amp;stSpaceBuffer, x, y, shape, frame, angle, scale, flip, bounds)
    /// (0x442050, gr.c).</remarks>
    int GetTransformedShapeBounds(int x, int y, ShapeRef shape, int frame, int angle, int scale, int flip, Span<short> bounds);
}
