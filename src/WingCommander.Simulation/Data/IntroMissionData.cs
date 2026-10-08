using WingCommander.Core.Numerics;
using WingCommander.Simulation.Missions;

namespace WingCommander.Simulation.Data;

/// <summary>
/// The built-in attract-mode (intro) mission data compiled into the executable: nav points
/// 16..19 and mission ship records 32..45 with their canned command streams. Command stream
/// format (<c>advance_canned_sequence</c>): <c>0 n</c> wait n frames, <c>1 yaw pitch roll speed</c>
/// set goals and speed, <c>2</c> explode, <c>3</c> fire guns, <c>4</c> afterburner; -1 ends.
/// </summary>
/// <remarks>C: aMissionNavPoints[16..19] (0x0046c2f0), aMissionShips[32..45] (0x0046c948),
/// asCannedSequence32..45 (0x0046c0b8-0x0046c2e8) in globals.c.</remarks>
public static class IntroMissionData
{
    /// <summary>First built-in nav point.</summary>
    public const int FirstNavPoint = 16;

    /// <summary>Number of built-in nav points (16..19).</summary>
    public const int NavPointCount = 4;

    /// <summary>First built-in mission ship record.</summary>
    public const int FirstShipRecord = 32;

    /// <summary>Number of built-in mission ship records (32..45).</summary>
    public const int ShipRecordCount = 14;

    internal static readonly short[] CannedSequence32 = [0, 20, 1, 0, 0, 180, 40, 1, 0, 0, 180, 40, 2, 0, 400, -1];
    internal static readonly short[] CannedSequence33 = [0, 20, 0, 20, 1, 0, -15, -60, 50, 0, 400, -1];
    internal static readonly short[] CannedSequence34 = [0, 20, 0, 20, 1, 0, 15, 60, 50, 0, 400, -1];

    /// <remarks>Declared as <c>short[42]</c> with 41 initialisers: the trailing 0 is kept.</remarks>
    internal static readonly short[] CannedSequence35 =
    [
        0, 17, 0, 6, 3, 0, 6, 3, 0, 6, 3, 0, 6, 3,
        0, 6, 3, 0, 6, 3, 0, 6, 3,
        1, 0, 0, 180, 40, 1, 0, 0, 180, 40,
        1, 0, -30, 60, 40, 0, 400, -1, 0,
    ];

    internal static readonly short[] CannedSequence37 = [0, 100, 1, 0, 30, 0, 50, 0, 20, 2, 0, 400, -1];
    internal static readonly short[] CannedSequence38 = [0, 110, 1, 0, 30, 0, 50, 0, 10, 2, 0, 400, -1];
    internal static readonly short[] CannedSequence39 = [0, 10, 2, 0, 400, -1];

    internal static readonly short[] CannedSequence40 =
    [
        0, 40,
        3, 1, 0, 0, 90, 60, 3, 1, 0, 0, 90, 60,
        3, 1, 0, 0, 90, 60, 3, 1, 0, 0, 90, 60,
        3, 1, 0, 0, 90, 60, 3, 1, 0, 0, 90, 60, -1,
    ];

    internal static readonly short[] CannedSequence41 = [0, 400, -1];
    internal static readonly short[] CannedSequence42 = [0, 120, 1, 0, 0, 180, 50, 3, 1, 0, 0, 180, 50, 3, -1];

    internal static readonly short[] CannedSequence43 =
    [
        0, 120, 1, 0, 0, 180, 50, 3, 1, 0, 0, 180, 50,
        3, 1, 0, 0, 180, 50, 3, -1,
    ];

    internal static readonly short[] CannedSequence44 =
    [
        0, 5, 3, 0, 5, 3, 0, 5, 3, 0, 5, 3,
        1, 0, 0, 180, 50, 1, 0, 0, 180, 50,
        1, 0, 0, 180, 50, 1, 0, 0, 180, 50, -1,
    ];

    internal static readonly short[] CannedSequence45 = [0, 20, 2, -1];

    /// <summary>Initial value of built-in nav point <paramref name="index"/> (16..19).</summary>
    public static MissionNavPoint CreateNavPoint(int index) => index switch
    {
        16 => Nav(ObjectType.Dralthi, ObjectType.Hornet, 32, 33, 34, 35),
        17 => Nav(ObjectType.AsteroidField, ObjectType.None, 36),
        18 => Nav(ObjectType.Gratha, ObjectType.Rapier, 37, 38, 39, 40, 41),
        19 => Nav(ObjectType.Krant, ObjectType.Scimitar, 42, 43, 44, 45),
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    /// <summary>Initial value of built-in mission ship record <paramref name="index"/> (32..45).</summary>
    public static MissionShipRecord CreateShipRecord(int index) => index switch
    {
        32 => Ship(ObjectType.Dralthi, Side.Kilrathi, 16, new(0, 0, 0), -155, 0, 0, 40, CannedSequence32),
        33 => Ship(ObjectType.Dralthi, Side.Kilrathi, 16, new(-154521, -25600, -232012), -155, 0, 0, 40, CannedSequence33),
        34 => Ship(ObjectType.Dralthi, Side.Kilrathi, 16, new(-61849, 25600, -232012), -155, 0, 0, 40, CannedSequence34),
        35 => Ship(ObjectType.Hornet, Side.Imperial, 16, new(324582, 0, 696038), -155, 0, 0, 40, CannedSequence35),
        36 => Ship(ObjectType.AsteroidField, Side.Imperial, 17, new(0, 0, 0), 0, 0, 0, 30000, null),
        37 => Ship(ObjectType.Gratha, Side.Kilrathi, 18, new(102400, -153600, -332800), 0, 0, -30, 80, CannedSequence37),
        38 => Ship(ObjectType.Gratha, Side.Kilrathi, 18, new(0, 256000, -332800), 0, 0, 90, 62, CannedSequence38),
        39 => Ship(ObjectType.Gratha, Side.Kilrathi, 18, new(0, 256000, -153600), 0, 0, -30, 80, CannedSequence39),
        40 => Ship(ObjectType.Rapier, Side.Imperial, 18, new(0, 256000, -819200), 0, 0, 30, 60, CannedSequence40),
        41 => Ship(ObjectType.Rapier, Side.Imperial, 18, new(102400, -153600, -819200), 0, 0, 0, 80, CannedSequence41),
        42 => Ship(ObjectType.Krant, Side.Kilrathi, 19, new(-51200, 76800, -1382400), 0, 0, 0, 50, CannedSequence42),
        43 => Ship(ObjectType.Krant, Side.Kilrathi, 19, new(153600, 0, -1280000), 0, 0, 60, 50, CannedSequence43),
        44 => Ship(ObjectType.Krant, Side.Kilrathi, 19, new(-249856, 0, 1139200), 155, 0, 0, 50, CannedSequence44),
        45 => Ship(ObjectType.Scimitar, Side.Imperial, 19, new(-76800, 0, 768000), 155, 0, 0, 50, CannedSequence45),
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    /// <summary><c>{ "", 1, {0,0,0}, 50000, {{0,0}x4}, {preload}, {ships, -1...} }</c>; the radius
    /// is stored in a signed short.</summary>
    private static MissionNavPoint Nav(ObjectType preload0, ObjectType preload1, params ReadOnlySpan<short> ships)
    {
        var nav = new MissionNavPoint
        {
            Name = "",
            Type = 1,
            Position = default,
            ProximityRadius = unchecked((short)50000),
        };
        nav.PreloadObjectTypes[0] = preload0;
        nav.PreloadObjectTypes[1] = preload1;
        for (int i = 0; i < 10; i++)
            nav.MissionShips[i] = i < ships.Length ? ships[i] : (short)-1;
        return nav;
    }

    /// <summary><c>{ type, side, -1, -1, CANNED_SEQUENCE, nav, pos, pitch, yaw, roll, 0, speed, 3,
    /// {sequence}, 0, 0, 0, -1, 0, 0 }</c>.</summary>
    private static MissionShipRecord Ship(
        ObjectType type, Side side, sbyte navPoint, FixedVector position, short pitch, short yaw, short roll,
        short speed, short[]? sequence) => new()
        {
            Type = type,
            Side = side,
            Leader = -1,
            Field9 = -1,
            MissionType = ShipMissionType.CannedSequence,
            NavPoint = navPoint,
            Position = position,
            Pitch = pitch,
            Yaw = yaw,
            Roll = roll,
            FormationSpot = 0,
            Speed = speed,
            Rating = 3,
            Pilot = 0,
            CannedSequence = sequence,
            Field2C = 0,
            Field2E = 0,
            State = 0,
            LeaderMissionIndex = -1,
            FormationIndex = 0,
            TargetMissionIndex = 0,
        };
}
