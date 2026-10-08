namespace WingCommander.Simulation.Geometry;

/// <summary>Polar form: 24.8 radius plus integer-degree yaw and pitch.</summary>
/// <remarks>C: SphericalVector (include/wcdata.h).</remarks>
public struct SphericalVector
{
    /// <summary>Length, 24.8 fixed.</summary>
    public int Radius;

    /// <summary>Degrees, negative to the left (x &lt; 0).</summary>
    public short Yaw;

    /// <summary>Degrees, <c>ArcCos(y / r) - 90</c> (negative = up).</summary>
    public short Pitch;

    public override readonly string ToString() => $"r {Radius / 256.0:0.###} yaw {Yaw} pitch {Pitch}";
}
