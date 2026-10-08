namespace WingCommander.Game.Flight.Cockpit;

/// <summary>An inclusive screen rectangle (C: ShortRect).</summary>
internal readonly record struct ScreenRect(short Left, short Top, short Right, short Bottom);

/// <summary>A screen point (C: ShortPoint).</summary>
internal readonly record struct ScreenPoint(short X, short Y);

/// <summary>One instrument bar (C: CockpitBarDefinition): direction 0 vertical (filled from the
/// bottom), 1 vertical reversed, 2 horizontal, 3 horizontal reversed; left -99 = not present.</summary>
internal readonly record struct CockpitBar(short Direction, short Left, short Top, short Right, short Bottom,
    short Length, short FilledFrame, short EmptyFrame);

/// <summary>The 3D scanner of a cockpit (C: CockpitScannerGeometry).</summary>
internal readonly record struct ScannerGeometry(short CenterX, short CenterY, short MinimumX, short MinimumY,
    short MaximumX, short MaximumY);

/// <summary>
/// The compiled-in cockpit layout tables, indexed by cockpit (0 Hornet, 1 Rapier, 2 Scimitar,
/// 3 Raptor, 4 training simulator). All coordinates are screen pixels; -99 disables an element.
/// </summary>
/// <remarks>C: globals.c stCockpitLayout (0x0046E008), aasCockpitLightX/Y (0x0046DCA8/0x0046DCF0),
/// aacCockpitLightOffFrame/OnFrame (0x0046DD38/0x0046DD60), aaCockpitBars (0x0046DD88),
/// asPilotHandOrigins (0x0046E120), asPilotHandOffsets (0x00469018), asHudMessageOrigins
/// (0x004691E0), aaCockpitDamagePositions (0x00469228), aWeaponDisplayPositions (0x00468440),
/// aWeaponDisplayOrigins (0x004684C0), aDamageDisplayPositions (0x0046A750), aTargetArmorClipRects
/// (0x0046A928), asMouseYawThresholds/asMousePitchThresholds (0x0046A030/0x0046A040),
/// aiScannerGridRows (0x00469098).</remarks>
internal static class CockpitTables
{
    /// <summary>Cockpits in the tables (cCockpitView 0..4).</summary>
    public const int CockpitCount = 5;

    /// <summary>The training simulator cockpit (PCSHIP.V04).</summary>
    public const int TrainingSimulator = 4;

    /// <summary>Marks a disabled table element.</summary>
    public const short Disabled = -99;

    /// <summary>Readout slots: 0 nav range, 1 target range, 2 set speed, 3 actual speed, 4 fore shield, 5 aft shield.</summary>
    public const int ReadoutCount = 6;

    /// <summary>Origins of readouts 4 (fore shield), 5 (aft shield), 2 (set speed), 3 (actual speed), per cockpit.</summary>
    /// <remarks>C: stCockpitLayout.readoutOrigins[4][6] (the sixth entry is padding).</remarks>
    public static readonly ScreenPoint[][] ReadoutOrigins =
    [
        [new(99, 130), new(227, 122), new(38, 164), new(192, 126), new(-99, -99)],
        [new(99, 140), new(227, 140), new(38, 184), new(192, 136), new(-99, -99)],
        [new(201, 105), new(156, 14), new(197, 145), new(262, 102), new(219, 115)],
        [new(112, 105), new(156, 19), new(105, 145), new(38, 102), new(71, 115)],
    ];

    /// <remarks>C: stCockpitLayout.leftVduBounds.</remarks>
    public static readonly ScreenRect[] LeftVduBounds =
    [
        new(10, 133, 82, 198), new(0, 99, 73, 165), new(6, 1, 78, 66), new(11, 111, 84, 176), new(48, 126, 120, 191),
    ];

    /// <remarks>C: stCockpitLayout.rightVduBounds.</remarks>
    public static readonly ScreenRect[] RightVduBounds =
    [
        new(236, 133, 309, 198), new(246, 97, 319, 165), new(241, 1, 313, 66), new(235, 111, 308, 176),
        new(198, 126, 270, 191),
    ];

    /// <remarks>C: stCockpitLayout.scanner.</remarks>
    public static readonly ScannerGeometry[] Scanners =
    [
        new(159, 126, 145, 113, 173, 139),
        new(103, 132, 89, 119, 117, 145),
        new(160, 39, 146, 26, 174, 52),
        new(159, 129, 145, 116, 173, 142),
        new(159, 130, 145, 117, 173, 143),
    ];

    /// <remarks>C: stCockpitLayout.pilotHandBounds.</remarks>
    public static readonly ScreenRect[] PilotHandBounds =
    [
        new(120, 152, 203, 199), new(120, 152, 205, 199), new(120, 151, 213, 199), new(120, 152, 205, 199),
        new(120, 152, 203, 199),
    ];

    /// <summary>Screen position the hand frames are drawn at (the same for every cockpit).</summary>
    /// <remarks>C: asPilotHandOrigins.</remarks>
    public static readonly ScreenPoint[] PilotHandOrigins =
    [
        new(154, 187), new(154, 187), new(154, 187), new(154, 187), new(154, 187),
    ];

    /// <summary>Offset of the sleeve (frame 17) for each hand frame 0..16.</summary>
    /// <remarks>C: asPilotHandOffsets[34].</remarks>
    public static readonly ScreenPoint[] PilotHandOffsets =
    [
        new(6, -3), new(7, 2), new(7, 9), new(7, 12), new(8, 13), new(0, -1), new(-1, -1), new(-4, -1), new(-6, -1),
        new(6, 0), new(8, 0), new(10, 0), new(13, 3), new(8, -7), new(6, -9), new(5, -11), new(5, -14),
    ];

    /// <summary>Top-left of the HUD message line in the space view.</summary>
    /// <remarks>C: asHudMessageOrigins[10].</remarks>
    public static readonly ScreenPoint[] HudMessageOrigins =
    [
        new(18, 14), new(71, 5), new(80, 29), new(49, 27), new(14, 13),
    ];

    /// <summary>Positions of the four cockpit damage decals.</summary>
    /// <remarks>C: aaCockpitDamagePositions[5][4].</remarks>
    public static readonly ScreenPoint[][] DamagePositions =
    [
        [new(224, 5), new(132, 96), new(233, 107), new(149, 161)],
        [new(177, 6), new(153, 142), new(103, 140), new(55, 183)],
        [new(107, 25), new(211, 32), new(21, 178), new(300, 178)],
        [new(74, 10), new(294, 19), new(197, 105), new(105, 134)],
        [new(0, 0), new(0, 0), new(0, 0), new(0, 0)],
    ];

    /// <summary>Cockpit light positions (lights 0..6).</summary>
    /// <remarks>C: aasCockpitLightX / aasCockpitLightY.</remarks>
    public static readonly short[][] LightX =
    [
        [101, 101, 189, 137, 265, -99, 203],
        [198, 198, 133, 145, 234, 234, -99],
        [76, 76, 139, 257, 20, 20, -99],
        [194, 194, 96, 137, 96, 96, -99],
        [-99, -99, 179, 133, -99, -99, -99],
    ];

    public static readonly short[][] LightY =
    [
        [122, 145, 121, 7, 121, -99, 147],
        [120, 139, 114, 114, 179, 188, -99],
        [164, 183, 22, 181, 162, 178, -99],
        [119, 142, 136, 15, 126, 116, -99],
        [-99, -99, 115, 114, -99, -99, -99],
    ];

    /// <summary>Frames of cockpit section 7 for lights that are off / on.</summary>
    /// <remarks>C: aacCockpitLightOffFrame / aacCockpitLightOnFrame.</remarks>
    public static readonly sbyte[][] LightOffFrame =
    [
        [14, 14, 14, 11, 13, 14, 14],
        [12, 12, 12, 13, 12, 12, 12],
        [9, 9, 9, 14, 9, 9, 9],
        [15, 15, 13, 11, 13, 13, 13],
        [5, 5, 5, 5, 5, 5, 5],
    ];

    public static readonly sbyte[][] LightOnFrame =
    [
        [3, 3, 3, 0, 2, 3, 3],
        [2, 2, 2, 3, 2, 2, 2],
        [1, 1, 1, 6, 1, 1, 1],
        [4, 4, 2, 0, 2, 2, 2],
        [1, 1, 1, 1, 1, 1, 1],
    ];

    /// <summary>Bars 0 fuel, 1 gun energy, 2 front armour, 3 rear armour, 4 armour index 2, 5 armour index 3,
    /// 6 fore shield, 7 aft shield.</summary>
    /// <remarks>C: aaCockpitBars[5][8].</remarks>
    public static readonly CockpitBar[][] Bars =
    [
        [
            new(0, 215, 122, 219, 152, 31, 16, 5), new(2, 143, 97, 175, 99, 33, 12, 1),
            new(0, 114, 127, 123, 130, 4, 17, 6), new(1, 114, 143, 123, 146, 4, 20, 9),
            new(2, 109, 132, 112, 141, 4, 18, 7), new(3, 125, 132, 128, 141, 4, 19, 8),
            new(0, 109, 122, 128, 125, 4, 15, 4), new(1, 109, 148, 128, 151, 4, 21, 10),
        ],
        [
            new(2, 96, 14, 134, 17, 39, 10, 0), new(2, 185, 14, 223, 17, 39, 11, 1),
            new(0, 213, 124, 220, 127, 4, 15, 5), new(1, 213, 138, 220, 141, 4, 18, 8),
            new(2, 205, 129, 208, 136, 4, 16, 6), new(3, 225, 129, 228, 136, 4, 17, 7),
            new(0, 205, 116, 228, 122, 7, 14, 4), new(1, 205, 143, 228, 149, 7, 19, 9),
        ],
        [
            new(2, 249, 165, 293, 169, 45, 11, 3), new(2, 131, 13, 189, 17, 59, 8, 0),
            new(0, -99, -99, -99, -99, 0, 0, 0), new(1, -99, -99, -99, -99, 0, 0, 0),
            new(2, 45, 172, 51, 181, 7, 12, 4), new(3, 70, 172, 76, 181, 7, 13, 5),
            new(0, 49, 164, 72, 170, 7, 10, 2), new(1, 49, 183, 72, 189, 7, 15, 7),
        ],
        [
            new(0, 123, 118, 126, 148, 31, 14, 3), new(2, 144, 100, 174, 102, 31, 12, 1),
            new(0, 207, 124, 216, 127, 4, 17, 6), new(1, 207, 140, 216, 143, 4, 20, 9),
            new(2, 202, 129, 205, 138, 4, 18, 7), new(3, 218, 129, 221, 138, 4, 19, 8),
            new(0, 202, 119, 221, 122, 4, 16, 5), new(1, 202, 145, 221, 148, 4, 21, 10),
        ],
        [
            new(0, 180, 124, 184, 146, 22, 6, 2), new(2, 143, 110, 175, 112, 33, 4, 0),
            new(0, -99, -99, 0, 0, 0, 0, 0), new(0, -99, -99, 0, 0, 0, 0, 0),
            new(0, -99, -99, 0, 0, 0, 0, 0), new(0, -99, -99, 0, 0, 0, 0, 0),
            new(0, 134, 124, 138, 146, 32, 7, 3), new(0, -99, -99, 0, 0, 0, 0, 0),
        ],
    ];

    /// <summary>Weapon VDU positions per hardpoint, relative to the weapon display origin.</summary>
    /// <remarks>C: aWeaponDisplayPositions[32].</remarks>
    public static readonly ScreenPoint[] WeaponDisplayPositions =
    [
        new(64, 28), new(8, 28), new(45, 25), new(27, 25), new(36, 23), new(20, 32), new(27, 24), new(31, 31),
        new(36, 22), new(41, 31), new(51, 32), new(45, 24), new(8, 32), new(18, 32), new(25, 11), new(26, 32),
        new(36, 28), new(46, 32), new(47, 11), new(54, 32), new(64, 32), new(12, 35), new(17, 37), new(26, 16),
        new(28, 31), new(37, 41), new(37, 29), new(45, 31), new(47, 16), new(56, 37), new(61, 35), new(0, 0),
    ];

    /// <summary>Weapon display origin relative to the left VDU's top-left (the same for every cockpit).</summary>
    /// <remarks>C: aWeaponDisplayOrigins[5].</remarks>
    public static readonly ScreenPoint WeaponDisplayOrigin = new(0, 16);

    /// <summary>Damage VDU component sprite positions (relative to the weapon display origin).</summary>
    /// <remarks>C: aDamageDisplayPositions[9].</remarks>
    public static readonly ScreenPoint[] DamageDisplayPositions =
    [
        new(36, 37), new(36, 28), new(36, 30), new(36, 23), new(36, 19), new(36, 15), new(36, 24), new(36, 16),
        new(36, 22),
    ];

    /// <summary>Clip rectangles of the four armour quadrants of the target silhouette (relative to its hot spot).</summary>
    /// <remarks>C: aTargetArmorClipRects[4].</remarks>
    public static readonly ScreenRect[] TargetArmorClipRects =
    [
        new(12, -20, 29, 20), new(-11, 1, 11, 20), new(-11, -20, 11, 0), new(-29, -20, -12, 20),
    ];

    /// <summary>Mouse steering buckets (horizontal offsets).</summary>
    /// <remarks>C: asMouseYawThresholds[6].</remarks>
    public static readonly short[] MouseYawThresholds = [10, 37, 52, 57, 62, 1070];

    /// <summary>Mouse steering buckets (vertical offsets).</summary>
    /// <remarks>C: asMousePitchThresholds[6].</remarks>
    public static readonly short[] MousePitchThresholds = [5, 18, 27, 35, 38, 1040];

    /// <summary>
    /// The cockpitless radar grid: rows of y offsets terminated by -1 (one row per x offset),
    /// the table by -2; mirrored into the four quadrants around the scanner centre.
    /// </summary>
    /// <remarks>C: aiScannerGridRows[79].</remarks>
    public static readonly int[] ScannerGridRows =
    [
        5, 13, 16, -1,
        5, 13, 16, -1,
        5, 13, 16, -1,
        4, 13, 16, -1,
        4, 12, 16, -1,
        2, 3, 4, 12, 15, -1,
        0, 1, 5, 12, 15, -1,
        6, 11, 15, -1,
        7, 11, 14, -1,
        8, 10, 14, -1,
        9, 13, -1,
        8, 13, -1,
        6, 7, 12, -1,
        4, 5, 11, -1,
        0, 1, 2, 3, 10, -1,
        9, -1,
        7, 8, -1,
        4, 5, 6, -1,
        0, 1, 2, 3, -1,
        -2,
    ];

    /// <summary>Internal component names (damage VDU and component messages).</summary>
    /// <remarks>C: apszComponentNames[9].</remarks>
    public static readonly string[] ComponentNames =
    [
        "Ion drive", "Power plant", "Shield gen'r", "Computer sys", "InterCom unit", "Target track",
        "Accel absorbers", "Ejector system", "Repair systems",
    ];

    /// <summary>Damage severities 0..4.</summary>
    /// <remarks>C: apszDamageSeverityNames[5].</remarks>
    public static readonly string[] DamageSeverityNames = ["Ok", "Light", "Moderate", "Heavy", "Destroyed"];

    /// <summary>Communication menu texts (index = command).</summary>
    /// <remarks>C: aszCommMenuText / apszCommMenuText[13].</remarks>
    public static readonly string[] CommMenuText =
    [
        "Never mind...", "Attack my target!", "Help me out here", "Return to base.", "Die furball!", "Slag off!",
        "Bite it cat face.", "Break and attack.", "Keep formation!", "Form on my wing.", "Keep radio silence",
        "Broadcast freely", "Request Landing",
    ];

    /// <summary>Kilrathi ace names (pilot ratings 9..12).</summary>
    /// <remarks>C: aszKilrathiAceNames / apszKilrathiAceNames[4].</remarks>
    public static readonly string[] KilrathiAceNames = ["Bhurak", "Dakhath", "Khajja", "Bakhtosh"];
}
