namespace WingCommander.Simulation.Data;

/// <summary>
/// Static geometry tables: hardpoint (child) offsets, formation slot positions and the
/// direction-view sprite frame/flip tables.
/// </summary>
/// <remarks>C: aChildOffsets (0x004682f0), aaFormationPositions (0x00465ed8),
/// acDirectionShapeFrame (0x0046db28), acDirectionShapeFlip (0x0046dbe8) in globals.c.</remarks>
public static class GeometryTables
{
    /// <summary>Number of direction views per sprite table (DIRECTION_VIEW_COUNT).</summary>
    public const int DirectionViewCount = 62;

    /// <summary>Number of direction sprite tables (ships/capitals, missiles/turret, Kilrathi base).</summary>
    public const int DirectionShapeTableCount = 3;

    /// <summary>
    /// Hardpoint offsets in object-local integer units {x right, y up, z forward}: 0..32 fighter
    /// gun/missile points, 33..40 carrier/base turret ring, 41..46 Jalthi guns, 47..53 capital
    /// turrets, 54 tanker turret, 55 tanker mine rack.
    /// </summary>
    /// <remarks>C: aChildOffsets[56] (0x004682f0).</remarks>
    public static ReadOnlySpan<ShortVector> ChildOffsets => ChildOffsetTable;

    /// <summary>
    /// Formation slot positions in units: five formation shapes of eight slots each, index
    /// <c>formation * 8 + spot</c>. Use <see cref="FormationPosition"/>.
    /// </summary>
    /// <remarks>C: aaFormationPositions[5][8] (0x00465ed8).</remarks>
    public static ReadOnlySpan<ShortVector> FormationPositions => FormationTable;

    /// <summary>Sprite frame per direction view; 3 tables of 62 entries (ships/capitals, missiles and
    /// the turret, Kilrathi base).</summary>
    /// <remarks>C: acDirectionShapeFrame[186] (0x0046db28).</remarks>
    public static ReadOnlySpan<sbyte> DirectionShapeFrame =>
    [
        0, 1, 2, 3, 4, 5, 6, 7, 6, 5, 4, 3, 2,
        14, 13, 12, 11, 10, 9, 8, 9, 10, 11, 12, 13,
        15, 16, 17, 18, 19, 20, 21, 20, 19, 18, 17, 16,
        28, 27, 26, 25, 24, 23, 22, 23, 24, 25, 26, 27,
        29, 30, 31, 32, 33, 34, 35, 34, 33, 32, 31, 30,
        36,
        0, 1, 2, 3, 4, 5, 6, 7, 6, 5, 4, 3, 2,
        14, 13, 12, 11, 10, 9, 8, 9, 10, 11, 12, 13,
        15, 16, 17, 18, 19, 20, 21, 20, 19, 18, 17, 16,
        14, 13, 12, 11, 10, 9, 8, 9, 10, 11, 12, 13,
        1, 2, 3, 4, 5, 6, 7, 6, 5, 4, 3, 2, 0,
        0, 1, 2, 3, 1, 3, 2, 1, 2, 3, 1, 3, 2,
        4, 5, 6, 4, 6, 5, 4, 5, 6, 4, 6, 5,
        7, 8, 9, 7, 9, 8, 7, 8, 9, 7, 9, 8,
        10, 11, 12, 10, 12, 11, 10, 11, 12, 10, 12, 11,
        13, 14, 15, 13, 15, 14, 13, 14, 15, 13, 15, 14, 16,
    ];

    /// <summary>Sprite mirror flags per direction view (0 none, 1 mirror X, 2 mirror Y, 3 both;
    /// shifted left by 4 into <c>asObjectFlip</c>).</summary>
    /// <remarks>C: acDirectionShapeFlip[186] (0x0046dbe8).</remarks>
    public static ReadOnlySpan<sbyte> DirectionShapeFlip =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1,
        0,
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1,
        2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3,
        2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 2,
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 0,
    ];

    /// <summary>Slot offset of <paramref name="spot"/> in formation shape <paramref name="formation"/>.</summary>
    public static ShortVector FormationPosition(int formation, int spot) => FormationTable[formation * 8 + spot];

    private static readonly ShortVector[] ChildOffsetTable =
    [
        new(120, 10, 20), new(-120, 10, 20), new(75, 45, -30),
        new(-75, 45, -30), new(0, 50, 10), new(-100, 10, -40),
        new(-90, 0, 30), new(-30, -40, -30), new(0, 0, 0),
        new(30, -40, -30), new(100, 10, -40), new(90, 0, 30),
        new(-140, 10, 30), new(-100, 10, 0), new(-75, 0, -40),
        new(-30, 10, -20), new(0, 10, 10), new(30, 10, -20),
        new(75, 0, -40), new(100, 10, 0), new(140, 10, 30),
        new(-120, -10, 0), new(-100, 10, -20), new(-90, 0, 40),
        new(-30, 20, -20), new(0, 10, -80), new(0, 10, 10),
        new(30, 20, -20), new(90, 0, 40), new(100, 10, -20),
        new(120, -10, 0), new(0, 10, 10), new(0, 0, -60),
        new(0, 0, 500), new(-200, 0, 250), new(200, 0, 250),
        new(-300, 0, 0), new(300, 0, 0), new(-200, 0, -250),
        new(200, 0, -250), new(0, 0, -500), new(-130, 40, 20),
        new(-110, 20, 20), new(-90, 0, 20), new(130, 40, 20),
        new(110, 20, 20), new(90, 0, 20), new(0, 0, 400),
        new(-50, -20, 350), new(50, 20, 350), new(-150, 20, 0),
        new(150, -20, 0), new(-75, -20, -350), new(75, 20, -350),
        new(0, 100, 350), new(0, 0, -300),
    ];

    private static readonly ShortVector[] FormationTable =
    [
        new(0, 0, 0), new(-750, 0, 0), new(750, 0, 0), new(0, 0, -750),
        new(0, 0, 750), new(-750, 0, -750), new(750, 0, -750), new(0, 0, -1500),

        new(0, 0, 0), new(750, 0, 0), new(-750, -100, -250), new(1500, -100, -250),
        new(-1500, -200, -500), new(-2250, -300, -750), new(2250, -200, -500), new(3000, -300, -750),

        new(0, 0, 0), new(750, 0, -500), new(-750, 0, -500), new(0, 0, -1000),
        new(-1500, 0, -1000), new(-750, 0, -1500), new(1500, 0, -1000), new(750, 0, -1500),

        new(0, 0, 0), new(750, 0, -250), new(0, 325, -500), new(750, -325, -750),
        new(0, 325, -500), new(750, -325, -750), new(0, 0, -1000), new(750, 0, -1250),

        new(0, 0, 0), new(0, 0, -750), new(-750, 0, -500), new(-750, 0, -1250),
        new(750, 0, -500), new(750, 0, -1250), new(0, 500, -500), new(0, 500, -1250),
    ];
}
