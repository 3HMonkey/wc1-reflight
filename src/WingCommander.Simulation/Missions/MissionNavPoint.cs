using WingCommander.Core.Numerics;

namespace WingCommander.Simulation.Missions;

/// <summary>
/// Runtime nav point record. Entries 0..15 are loaded per mission from MODULE section 1;
/// entries 16..19 are the built-in intro scenes. <see cref="Type"/>: 0 unused/terminator,
/// 1 active, 2..5 follow-up waves (stored in the records after the active one), -1 consumed.
/// </summary>
/// <remarks>C: MissionNavPoint (0x51 bytes, include/wcdata.h), aMissionNavPoints[20] (0x0046c2f0).</remarks>
public struct MissionNavPoint
{
    /// <summary>Name (char[30], text up to the first NUL).</summary>
    public string Name;

    /// <summary>+0x1E nav type.</summary>
    public sbyte Type;

    /// <summary>+0x1F world position, 24.8 fixed.</summary>
    public FixedVector Position;

    /// <summary>+0x2B sphere radius in units. Unsigned on disk, a signed short at runtime (the
    /// intro navs' 50000 is negative, so <c>FindNearestNavPoint</c> never matches them).</summary>
    public short ProximityRadius;

    /// <summary>+0x2D triggers applied when the sphere is entered.</summary>
    public NavTriggers Triggers;

    /// <summary>+0x35 object types whose shapes the sphere needs.</summary>
    public NavPreloadTypes PreloadObjectTypes;

    /// <summary>+0x3D mission ships spawned in this sphere.</summary>
    public NavShipList MissionShips;

    public override readonly string ToString() => $"{Name} (type {Type}) at {Position}";
}
