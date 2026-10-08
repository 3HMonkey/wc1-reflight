using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

/// <summary>An <see cref="IShapeBounds"/> without shape data for tests and tools: every object is
/// tested as a point against a fixed viewport (default 320x200), like the original does for an
/// object without a shape.</summary>
public sealed class PointShapeBounds(int left = 0, int top = 0, int right = 319, int bottom = 199) : IShapeBounds
{
    public int GetTransformedShapeBounds(int x, int y, ShapeRef shape, int frame, int angle, int scale, int flip, Span<short> bounds)
    {
        if (x < left || x > right || y < top || y > bottom)
            return 0;
        bounds[0] = unchecked((short)x);
        bounds[1] = unchecked((short)y);
        bounds[2] = unchecked((short)x);
        bounds[3] = unchecked((short)y);
        return 1;
    }
}
