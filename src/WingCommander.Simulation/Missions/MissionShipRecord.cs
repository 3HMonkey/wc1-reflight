using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;

namespace WingCommander.Simulation.Missions;

/// <summary>
/// Runtime mission ship record. Records 0..31 are loaded per mission from MODULE section 3;
/// 32..45 are the built-in intro (attract mode) dogfight records whose behaviour slot holds a
/// canned command stream instead of a pilot. <see cref="Position"/> is relative to the nav
/// point the ship spawns at. Asteroid/mine fields are records of type 22/23 where
/// <c>Speed + 3000</c> is the radius and <see cref="Pilot"/> the density.
/// </summary>
/// <remarks>C: MissionShipRecord (0x36 bytes, include/wcdata.h), aMissionShips[48] (0x0046c948).</remarks>
public struct MissionShipRecord
{
    /// <summary>+0x00 ship type.</summary>
    public ObjectType Type;

    /// <summary>+0x04 allegiance.</summary>
    public Side Side;

    /// <summary>+0x08 leader flag/index from the disk record (not used by the simulation).</summary>
    public sbyte Leader;

    /// <summary>+0x09 unknown byte (disk offset 5).</summary>
    public sbyte Field9;

    /// <summary>+0x0A initial AI mission ("order").</summary>
    public ShipMissionType MissionType;

    /// <summary>+0x0E nav point the position is relative to (rewritten by <c>init_ship</c>).</summary>
    public sbyte NavPoint;

    /// <summary>+0x0F position relative to the nav point, 24.8 fixed.</summary>
    public FixedVector Position;

    /// <summary>+0x1B orientation angle; note <c>Set_up_ship_info</c> applies it as YAW (<c>alter_yaw(-pitch)</c>).</summary>
    public short Pitch;

    /// <summary>+0x1D orientation angle; applied as PITCH (<c>alter_pitch(-yaw)</c>).</summary>
    public short Yaw;

    /// <summary>+0x1F roll angle.</summary>
    public short Roll;

    /// <summary>+0x21 slot in the formation shape.</summary>
    public sbyte FormationSpot;

    /// <summary>+0x22 initial speed in units/frame (hazard fields: radius - 3000).</summary>
    public short Speed;

    /// <summary>+0x24 "AI level 0-4" disk field (not used by the simulation).</summary>
    public int Rating;

    /// <summary>+0x28 pilot / personality (see <see cref="Data.Rating"/>); hazard fields: density.</summary>
    public int Pilot;

    /// <summary>+0x28 for the intro records 32..45: the canned command stream (the union's other arm).</summary>
    public short[]? CannedSequence;

    /// <summary>+0x2C unknown (disk offset 34).</summary>
    public short Field2C;

    /// <summary>+0x2E unknown (disk offset 36, a short on disk).</summary>
    public int Field2E;

    /// <summary>+0x32 0 alive/unspawned, 1 arrived home, 2 left (warped out), 3 destroyed.</summary>
    public sbyte State;

    /// <summary>+0x33 mission record of the wing leader (-1 none).</summary>
    public sbyte LeaderMissionIndex;

    /// <summary>+0x34 formation shape 0..4 (-1 none).</summary>
    public sbyte FormationIndex;

    /// <summary>+0x35 mission record of the escort/strike/defend target (GOTO_WARP: nav index).</summary>
    public sbyte TargetMissionIndex;

    public override readonly string ToString() => $"{Type} {Side} {MissionType} pilot {Pilot} at {Position}";
}
