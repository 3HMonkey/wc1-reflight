using System.Buffers.Binary;
using System.Text;
using WingCommander.Core.Resources;

namespace WingCommander.Game.Scenes;

/// <summary>One nav point of a mission (MODULE section 1, 77-byte disk record).</summary>
/// <remarks>C: MissionNavPointDisk / MissionNavPoint (cmpgn.c, wcdata.h).</remarks>
public sealed class BriefingNavPoint
{
    /// <summary>Display name ("Nav 1", "Tiger's Claw", ...).</summary>
    public string Name { get; init; } = "";

    /// <summary>0 ends the nav point list.</summary>
    public sbyte Type { get; init; }

    /// <summary>World position (24.8 fixed point).</summary>
    public int X { get; init; }

    public int Y { get; init; }

    public int Z { get; init; }

    /// <summary>Mission ship records placed at this nav point (-1 = empty slot).</summary>
    public short[] MissionShips { get; init; } = new short[10];
}

/// <summary>One objective source record (MODULE section 2, 64 bytes).</summary>
/// <remarks>C: MissionObjectiveDisk / MissionObjectiveSource.</remarks>
public readonly record struct BriefingObjectiveSource(short Type, short Index, string Description);

/// <summary>The fields of a mission ship record (MODULE section 3, 42 bytes) the scenes use.</summary>
/// <remarks>C: MissionShipDisk / MissionShipRecord.</remarks>
public sealed class BriefingMissionShip
{
    /// <summary>Object type (0 Hornet, 1 Rapier, 2 Scimitar, 3 Raptor, ..., 22 asteroid field, 23 mine field).</summary>
    public short Type { get; init; }

    public short Side { get; init; }

    /// <summary>Ship mission type (index into the nav map's mission type names).</summary>
    public short MissionType { get; init; }

    /// <summary>Nav point the position is relative to.</summary>
    public sbyte NavPoint { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public int Z { get; init; }

    /// <summary>Speed; hazard fields keep their marker size here.</summary>
    public short Speed { get; init; }

    /// <summary>0 = present at mission start.</summary>
    public sbyte State { get; init; }
}

/// <summary>A runtime objective built from a source record (aMissionObjectives).</summary>
/// <remarks>C: MissionObjective (wcdata.h), built by Build_objective_list (0x40CED0, brains.c).</remarks>
public sealed class BriefingObjective
{
    /// <summary>0 nav point, 1..4 mission ship kinds, -1 terminator.</summary>
    public int Type { get; set; }

    /// <summary>Nav point or mission ship index.</summary>
    public sbyte Index { get; set; }

    /// <summary>The objective description (a leading '.' hides it, a leading '?' means "unknown until sighted").</summary>
    public string Name { get; set; } = "";

    /// <summary>Nav point name or ship type name.</summary>
    public string DisplayName { get; set; } = "";

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    /// <summary>Nav map coordinates (world / 100 &gt;&gt; 8, then scaled by the map).</summary>
    public short MapX { get; set; }

    public short MapY { get; set; }

    /// <summary>1 visited, 2 achieved, 4 sighted.</summary>
    public byte Flags { get; set; }
}

/// <summary>
/// The parts of MODULE.000/.001/.002 the conversation scenes need for one mission: header, nav
/// points, objective sources, mission ships, the mission name (section 4, shown on the briefing
/// map) and the system name of the series (section 5, the <c>$S</c> macro), plus the runtime
/// objective list and flight path that Build_objective_list derives from them.
/// </summary>
/// <remarks>
/// C: LoadMissionData (0x4059B0, cmpgn.c) and Build_objective_list (0x40CED0, brains.c) with the
/// objective helpers of cockpt.c (mobile_objective, hidden_objective, objective_name,
/// set_new_objective, nav_note). The Game project does not reference the Simulation project yet,
/// so this is a lean local reader (same record layouts as Simulation's MissionModule); it should be
/// replaced by the simulation's mission state once the flight layer is integrated (see
/// docs/progress/screens-scenes.md, Requests). The world is assumed empty while the briefing runs:
/// no mission ship is spawned (find_ship_index = -1) and the player object sits at the origin.
/// </remarks>
public sealed class MissionBriefingData
{
    public const int NavPointCount = 16;
    public const int ObjectiveCount = 16;
    public const int ShipCount = 32;
    public const int AuxSize = 0x28;

    private const int HeaderSize = 0x18;
    private const int NavRecordSize = 77;
    private const int NavStride = NavRecordSize * NavPointCount;
    private const int ObjectiveRecordSize = 64;
    private const int ObjectiveStride = ObjectiveRecordSize * ObjectiveCount;
    private const int ShipRecordSize = 42;
    private const int ShipStride = ShipRecordSize * ShipCount;

    private static readonly int[] LogicalFiles = [LogicalFile.Module000, LogicalFile.Module001, LogicalFile.Module002];

    /// <summary>Ship type display names for object types 0..21 (the remaining types never are objectives).</summary>
    /// <remarks>C: aObjectTypeData[type].displayName -> aszObjectTypeDisplayNames (0x004684D4).</remarks>
    private static readonly string[] ShipTypeDisplayNames =
    [
        "Hornet", "Rapier", "Scimitar", "Raptor", "Venture", "Dilligent", "Drayman", "Exeter", "Tiger's Claw",
        "Salthi", "Dralthi", "Krant", "Gratha", "Jalthi", "Spikeri", "Dorkir", "Lumbari", "Ralari", "Fralthi",
        "Snakeir", "Sivar", "Star post",
    ];

    private MissionBriefingData()
    {
    }

    public int Series { get; private init; }

    public int Mission { get; private init; }

    /// <summary>mission + series * 4.</summary>
    public int MissionIndex => Mission + Series * 4;

    /// <remarks>C: nMissionEntryNavPoint.</remarks>
    public short EntryNavPoint { get; private init; }

    /// <remarks>C: nHomeMissionShipIndex.</remarks>
    public short HomeMissionShip { get; private init; }

    /// <summary>Mission ship record of the player (its type is the ship the player flies).</summary>
    /// <remarks>C: nPlayerMissionShipIndex.</remarks>
    public short PlayerMissionShip { get; private init; }

    public IReadOnlyList<BriefingNavPoint> NavPoints { get; private init; } = [];

    public IReadOnlyList<BriefingObjectiveSource> ObjectiveSources { get; private init; } = [];

    public IReadOnlyList<BriefingMissionShip> Ships { get; private init; } = [];

    /// <summary>Mission name (abMissionAuxData), e.g. "Alpha Wing".</summary>
    public string MissionName { get; private init; } = "";

    /// <summary>System name of the series (abSeriesAuxData), the <c>$S</c> macro.</summary>
    public string SystemName { get; private init; } = "";

    /// <summary>Runtime objectives (aMissionObjectives); entry <see cref="ObjectiveListCount"/> is the -1 terminator.</summary>
    public BriefingObjective[] Objectives { get; } = CreateObjectives();

    /// <remarks>C: cMissionObjectiveCount.</remarks>
    public int ObjectiveListCount { get; private set; }

    /// <summary>Objective indices in flight order, -1 terminated.</summary>
    /// <remarks>C: abFlightPath.</remarks>
    public sbyte[] FlightPath { get; } = new sbyte[ObjectiveCount + 1];

    /// <remarks>C: cCurrentNavPointIndex.</remarks>
    public sbyte CurrentNavPointIndex { get; set; }

    /// <summary>The objective the nav map highlights (the briefing's map shots set it from the record's talker).</summary>
    /// <remarks>C: cCurrentObjective.</remarks>
    public sbyte CurrentObjective { get; set; }

    /// <summary>Type of the player's ship in this mission (aMissionShips[nPlayerMissionShipIndex].type).</summary>
    public int PlayerShipType =>
        (uint)PlayerMissionShip < (uint)Ships.Count ? Ships[PlayerMissionShip].Type : 0;

    /// <summary>Mission type of the player's mission ship (the nav map's "* Patrol *" line).</summary>
    public int PlayerMissionType =>
        (uint)PlayerMissionShip < (uint)Ships.Count ? Ships[PlayerMissionShip].MissionType : 0;

    /// <summary>Logical file of MODULE.&lt;campaign&gt; (asMissionDataFiles = {15, 52, 72}).</summary>
    public static int LogicalFileFor(int campaignIndex) => LogicalFiles[Math.Clamp(campaignIndex, 0, 2)];

    /// <summary>
    /// Loads mission <paramref name="mission"/> of <paramref name="series"/> and builds its objective
    /// list. Returns null when the slot is outside the table or holds no player ship (the SDL
    /// port's guard: an empty header marks its player ship as -1).
    /// </summary>
    /// <remarks>C: LoadMissionData (0x4059B0) + Build_objective_list (0x40CED0).</remarks>
    public static MissionBriefingData? Load(GameDirectory directory, int campaignIndex, int series, int mission)
    {
        ArgumentNullException.ThrowIfNull(directory);
        return Parse(directory.OpenPacket(LogicalFileFor(campaignIndex)), series, mission);
    }

    /// <inheritdoc cref="Load"/>
    public static MissionBriefingData? Parse(PacketFile packet, int series, int mission)
    {
        ArgumentNullException.ThrowIfNull(packet);
        int index = mission + series * 4;
        if (index < 0 || index >= 64 || packet.SectionCount < 6)
            return null;
        var header = packet.GetSection(0).Span;
        if (header.Length < (index + 1) * HeaderSize)
            return null;
        var h = header[(index * HeaderSize)..];
        short playerShip = BinaryPrimitives.ReadInt16LittleEndian(h[4..]);
        if (playerShip < 0 || playerShip >= ShipCount)
            return null;

        var navSection = packet.GetSection(1).Span;
        var objectiveSection = packet.GetSection(2).Span;
        var shipSection = packet.GetSection(3).Span;
        var missionAux = packet.GetSection(4).Span;
        var seriesAux = packet.GetSection(5).Span;
        if (navSection.Length < (index + 1) * NavStride || objectiveSection.Length < (index + 1) * ObjectiveStride ||
            shipSection.Length < (index + 1) * ShipStride)
            throw new GameDataException($"{packet.Name}: mission tables are too short for mission slot {index}.");

        var navPoints = new BriefingNavPoint[NavPointCount];
        for (int i = 0; i < NavPointCount; i++)
        {
            var r = navSection.Slice(index * NavStride + i * NavRecordSize, NavRecordSize);
            var ships = new short[10];
            for (int s = 0; s < 10; s++)
                ships[s] = BinaryPrimitives.ReadInt16LittleEndian(r[(57 + s * 2)..]);
            navPoints[i] = new BriefingNavPoint
            {
                Name = ReadCString(r[..30]),
                Type = unchecked((sbyte)r[30]),
                X = BinaryPrimitives.ReadInt32LittleEndian(r[31..]),
                Y = BinaryPrimitives.ReadInt32LittleEndian(r[35..]),
                Z = BinaryPrimitives.ReadInt32LittleEndian(r[39..]),
                MissionShips = ships,
            };
        }

        var sources = new BriefingObjectiveSource[ObjectiveCount];
        for (int i = 0; i < ObjectiveCount; i++)
        {
            var r = objectiveSection.Slice(index * ObjectiveStride + i * ObjectiveRecordSize, ObjectiveRecordSize);
            sources[i] = new BriefingObjectiveSource(
                BinaryPrimitives.ReadInt16LittleEndian(r),
                BinaryPrimitives.ReadInt16LittleEndian(r[2..]),
                ReadCString(r.Slice(4, 60)));
        }

        var shipRecords = new BriefingMissionShip[ShipCount];
        for (int i = 0; i < ShipCount; i++)
        {
            var r = shipSection.Slice(index * ShipStride + i * ShipRecordSize, ShipRecordSize);
            shipRecords[i] = new BriefingMissionShip
            {
                Type = BinaryPrimitives.ReadInt16LittleEndian(r),
                Side = BinaryPrimitives.ReadInt16LittleEndian(r[2..]),
                MissionType = BinaryPrimitives.ReadInt16LittleEndian(r[6..]),
                NavPoint = unchecked((sbyte)r[8]),
                X = BinaryPrimitives.ReadInt32LittleEndian(r[9..]),
                Y = BinaryPrimitives.ReadInt32LittleEndian(r[13..]),
                Z = BinaryPrimitives.ReadInt32LittleEndian(r[17..]),
                Speed = BinaryPrimitives.ReadInt16LittleEndian(r[28..]),
                State = unchecked((sbyte)r[38]),
            };
        }

        var data = new MissionBriefingData
        {
            Series = series,
            Mission = mission,
            EntryNavPoint = BinaryPrimitives.ReadInt16LittleEndian(h),
            HomeMissionShip = BinaryPrimitives.ReadInt16LittleEndian(h[2..]),
            PlayerMissionShip = playerShip,
            NavPoints = navPoints,
            ObjectiveSources = sources,
            Ships = shipRecords,
            MissionName = (index + 1) * AuxSize <= missionAux.Length ? ReadCString(missionAux.Slice(index * AuxSize, AuxSize)) : "",
            SystemName = (series + 1) * AuxSize <= seriesAux.Length && series >= 0
                ? ReadCString(seriesAux.Slice(series * AuxSize, AuxSize))
                : "",
        };
        data.BuildObjectiveList();
        return data;
    }

    /// <summary>
    /// Converts the objective sources into runtime objectives up to the -1 terminator (at most 16):
    /// nav points (type 0) and mission ships (types 1..4, placed at their nav point) join the flight
    /// path. Then the first visible flight path entry becomes the current destination.
    /// </summary>
    /// <remarks>C: Build_objective_list (0x40CED0, brains.c). Guards like the simulation's port: at
    /// most 16 sources, and the destination search gives up instead of looping forever when every
    /// objective is hidden.</remarks>
    public void BuildObjectiveList()
    {
        int flightPathCount = 0;
        ObjectiveListCount = 0;
        int x = 0, y = 0, z = 0;
        string displayName = "";
        for (int source = 0; source < ObjectiveCount && ObjectiveSources[source].Type != -1; source++)
        {
            var src = ObjectiveSources[source];
            int type = src.Type;
            var objective = Objectives[ObjectiveListCount];
            objective.Flags = 0;
            if (type == 0)
            {
                var nav = NavPoints[Math.Clamp((int)src.Index, 0, NavPointCount - 1)];
                (x, y, z) = (nav.X, nav.Y, nav.Z);
                displayName = nav.Name;
                FlightPath[flightPathCount++] = (sbyte)ObjectiveListCount;
            }
            else if (type >= 1 && type <= 4)
            {
                var ship = Ships[Math.Clamp((int)src.Index, 0, ShipCount - 1)];
                displayName = ship.Type >= 0 && ship.Type < ShipTypeDisplayNames.Length ? ShipTypeDisplayNames[ship.Type] : "";
                (x, y, z) = SpherePoint(ship);
                FlightPath[flightPathCount++] = (sbyte)ObjectiveListCount;
            }
            objective.Type = type;
            objective.Index = unchecked((sbyte)src.Index);
            objective.Name = src.Description;
            (objective.X, objective.Y, objective.Z) = (x, y, z);
            (objective.MapX, objective.MapY) = NavGetXY(x, z);
            objective.DisplayName = displayName;
            ObjectiveListCount++;
        }
        FlightPath[flightPathCount] = -1;
        Objectives[ObjectiveListCount].Type = -1;
        CurrentNavPointIndex = 0;
        CurrentObjective = 0;
        if (ObjectiveListCount != 0)
        {
            for (int attempt = 0; attempt < 3 * (ObjectiveCount + 1) && !SetNewObjective(CurrentNavPointIndex); attempt++)
                CurrentNavPointIndex++;
        }
    }

    /// <summary>Position of a mission ship: its nav point plus its offset.</summary>
    /// <remarks>C: set_sphere_point (0x40B670, brains.c).</remarks>
    public (int X, int Y, int Z) SpherePoint(BriefingMissionShip ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        var nav = NavPoints[Math.Clamp((int)ship.NavPoint, 0, NavPointCount - 1)];
        return (unchecked(nav.X + ship.X), unchecked(nav.Y + ship.Y), unchecked(nav.Z + ship.Z));
    }

    /// <summary>World X/Z to unscaled nav map coordinates: <c>(v / 100) &gt;&gt; 8</c>.</summary>
    /// <remarks>C: nav_getxy (0x40CC30, brains.c) with nNavMapCoordinateScaling = 0.</remarks>
    public static (short X, short Y) NavGetXY(int worldX, int worldZ) =>
        (unchecked((short)((worldX / 100) >> 8)), unchecked((short)((worldZ / 100) >> 8)));

    /// <remarks>C: mobile_objective (0x415A30, cockpt.c).</remarks>
    public bool MobileObjective(int objective)
    {
        int type = Objectives[objective].Type;
        return type is 1 or 2 or 3 or 4;
    }

    /// <remarks>C: visited (0x415070, cockpt.c).</remarks>
    public bool Visited(int objective) => (Objectives[objective].Flags & 1) == 1;

    /// <remarks>C: achieved (0x415090, cockpt.c).</remarks>
    public bool Achieved(int objective) => (Objectives[objective].Flags & 2) == 2;

    /// <remarks>C: sighted (0x415050, cockpt.c).</remarks>
    public bool Sighted(int objective) => (Objectives[objective].Flags & 4) == 4;

    /// <summary>
    /// Names starting with '.' and mission ships that are not present at the start (state != 0) are
    /// hidden. (The WARP_ARRIVE test of the original needs a spawned ship, which never exists here.)
    /// </summary>
    /// <remarks>C: hidden_objective (0x4151F0, cockpt.c).</remarks>
    public bool HiddenObjective(int objective)
    {
        if (objective < 0 || objective >= Objectives.Length)
            return true;
        var o = Objectives[objective];
        bool nameHidden = o.DisplayName.StartsWith('.') || o.Name.StartsWith('.');
        return nameHidden || (MobileObjective(objective) && Ship(o.Index).State != 0);
    }

    /// <summary>The ship type name, "UNKNOWN" for unsighted '?' objectives, "NONE" past the list.</summary>
    /// <remarks>C: objective_name (0x415140, cockpt.c).</remarks>
    public string ObjectiveName(int objective)
    {
        if (objective >= ObjectiveListCount)
            return "NONE";
        if (Objectives[objective].Name.StartsWith('?') && !Sighted(objective))
            return "UNKNOWN";
        return Objectives[objective].DisplayName;
    }

    /// <summary>The objective's description with a leading '?' removed.</summary>
    /// <remarks>C: nav_note (0x40DF50, nav.c).</remarks>
    public string NavNote(int objective)
    {
        string note = (uint)objective < (uint)Objectives.Length ? Objectives[objective].Name : "";
        return note.StartsWith('?') ? note[1..] : note;
    }

    /// <summary>
    /// Makes flight-path entry <paramref name="pathIndex"/> the destination (negative = the last
    /// entry, out of range = the first). Returns false when that objective is hidden.
    /// </summary>
    /// <remarks>C: set_new_objective (0x4152C0, cockpt.c), without the player ship's objective update.</remarks>
    public bool SetNewObjective(int pathIndex)
    {
        if (pathIndex < 0)
        {
            pathIndex = -1;
            do
            {
                pathIndex++;
            }
            while (FlightPath[pathIndex] != -1 && pathIndex < ObjectiveListCount);
            pathIndex--;
        }
        if (pathIndex > ObjectiveListCount)
            pathIndex = 0;
        if (pathIndex < 0 || FlightPath[pathIndex] == -1)
            pathIndex = 0;
        CurrentNavPointIndex = unchecked((sbyte)pathIndex);
        if (HiddenObjective(FlightPath[pathIndex]))
            return false;
        CurrentObjective = FlightPath[CurrentNavPointIndex];
        return true;
    }

    /// <summary>The mission ship record at <paramref name="index"/> (clamped like a C array read would not be).</summary>
    public BriefingMissionShip Ship(int index) => Ships[Math.Clamp(index, 0, ShipCount - 1)];

    private static BriefingObjective[] CreateObjectives()
    {
        var objectives = new BriefingObjective[ObjectiveCount + 1];
        for (int i = 0; i < objectives.Length; i++)
            objectives[i] = new BriefingObjective { Type = -1 };
        return objectives;
    }

    private static string ReadCString(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
    }
}
