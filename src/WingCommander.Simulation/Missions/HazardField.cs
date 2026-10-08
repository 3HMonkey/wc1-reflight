using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;

namespace WingCommander.Simulation.Missions;

/// <summary>Asteroid or mine field descriptor added from a type 22/23 mission record.</summary>
/// <remarks>C: HazardField (0x16 bytes, include/wcdata.h), aHazardFields[7] (0x0059d870).</remarks>
public struct HazardField
{
    /// <summary>+0x00 <see cref="ObjectType.AsteroidField"/> or <see cref="ObjectType.MineField"/>.</summary>
    public ObjectType Type;

    /// <summary>+0x04 world centre.</summary>
    public FixedVector Center;

    /// <summary>+0x10 radius (record speed + 3000).</summary>
    public short InnerRadius;

    /// <summary>+0x12 same as the inner radius.</summary>
    public short OuterRadius;

    /// <summary>+0x14 density (the record's pilot field).</summary>
    public short Density;
}
