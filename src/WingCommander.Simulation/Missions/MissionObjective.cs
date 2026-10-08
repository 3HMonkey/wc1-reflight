using WingCommander.Core.Numerics;

namespace WingCommander.Simulation.Missions;

/// <summary>
/// Runtime objective built by <c>Build_objective_list</c> (also stored in save games).
/// <see cref="Flags"/>: 1 visited, 2 achieved, 4 sighted.
/// </summary>
/// <remarks>C: MissionObjective (0x1f bytes, include/wcdata.h), aMissionObjectives[16] (0x0059dac0).</remarks>
public struct MissionObjective
{
    public const byte FlagVisited = 1;
    public const byte FlagAchieved = 2;
    public const byte FlagSighted = 4;

    /// <summary>+0x00 nav map X (<c>nav_getxy</c>).</summary>
    public short MapX;

    /// <summary>+0x02 nav map Y.</summary>
    public short MapY;

    /// <summary>+0x04 unknown byte.</summary>
    public byte Field4;

    /// <summary>+0x05 objective type (-1 terminates the list).</summary>
    public int Type;

    /// <summary>+0x09 nav point or mission ship index (truncated to a signed byte).</summary>
    public sbyte Index;

    /// <summary>+0x0A visited/achieved/sighted flags.</summary>
    public byte Flags;

    /// <summary>+0x0B display name: the nav point name or the ship type's display name.</summary>
    public string DisplayName;

    /// <summary>+0x0F mission description (points at the source's description).</summary>
    public string Name;

    /// <summary>+0x13 world position (mobile objectives are relocated at runtime).</summary>
    public FixedVector Position;

    public override readonly string ToString() => $"type {Type} index {Index} flags {Flags}: {DisplayName} / {Name}";
}
