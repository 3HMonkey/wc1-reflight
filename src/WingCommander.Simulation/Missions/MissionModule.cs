using System.Buffers.Binary;
using System.Text;
using WingCommander.Core.Numerics;
using WingCommander.Core.Resources;
using WingCommander.Simulation.Data;

namespace WingCommander.Simulation.Missions;

/// <summary>
/// Parser for a mission data file (MODULE.000/.001/.002, logical files 15/52/72). Six packet
/// sections hold fixed-stride tables for 64 mission slots (<c>mission + series * 4</c>):
/// 0 header (0x18), 1 nav points (16 x 77 bytes), 2 objectives (16 x 64), 3 ships (32 x 42),
/// 4 mission name (0x28), 5 series/system name (0x28 per series).
/// </summary>
/// <remarks>
/// C: LoadMissionData (0x4059B0, cmpgn.c) with the packed disk records MissionHeaderDisk,
/// MissionNavPointDisk, MissionObjectiveDisk, MissionShipDisk. Verified against the GOG DOS
/// data: the DOS records use exactly this layout with 32-bit 24.8 fixed coordinates (the
/// "3-byte coordinate" community description reads the integer bytes of the same values).
/// </remarks>
public sealed class MissionModule
{
    public const int MissionSlots = 64;
    public const int MissionsPerSeries = 4;
    public const int SeriesSlots = 16;
    public const int HeaderSize = 0x18;
    public const int NavRecordSize = 77;
    public const int NavPointsPerMission = 16;
    public const int NavStride = 0x4d0;
    public const int ObjectiveRecordSize = 64;
    public const int ObjectivesPerMission = 16;
    public const int ObjectiveStride = 0x400;
    public const int ShipRecordSize = 42;
    public const int ShipsPerMission = 32;
    public const int ShipStride = 0x540;
    public const int AuxSize = 0x28;

    private readonly byte[] _header;
    private readonly byte[] _navPoints;
    private readonly byte[] _objectives;
    private readonly byte[] _ships;
    private readonly byte[] _missionAux;
    private readonly byte[] _seriesAux;

    private MissionModule(string name, byte[][] sections)
    {
        Name = name;
        _header = sections[0];
        _navPoints = sections[1];
        _objectives = sections[2];
        _ships = sections[3];
        _missionAux = sections[4];
        _seriesAux = sections[5];
        Require(_header, MissionSlots * HeaderSize, 0);
        Require(_navPoints, MissionSlots * NavStride, 1);
        Require(_objectives, MissionSlots * ObjectiveStride, 2);
        Require(_ships, MissionSlots * ShipStride, 3);
        Require(_missionAux, MissionSlots * AuxSize, 4);
        Require(_seriesAux, SeriesSlots * AuxSize, 5);
    }

    /// <summary>File name used in messages.</summary>
    public string Name { get; }

    /// <summary>Logical file of the mission data for a campaign data set (0 original, 1 SM1, 2 SM2).</summary>
    /// <remarks>C: asMissionDataFiles = {15, 52, 72} (globals.c).</remarks>
    public static int LogicalFileForCampaign(int campaignDataSet) => campaignDataSet switch
    {
        0 => LogicalFile.Module000,
        1 => LogicalFile.Module001,
        2 => LogicalFile.Module002,
        _ => throw new ArgumentOutOfRangeException(nameof(campaignDataSet)),
    };

    /// <summary><c>mission + series * 4</c>.</summary>
    public static int GetMissionIndex(int series, int mission) => mission + series * MissionsPerSeries;

    /// <summary>Parses an opened MODULE packet file.</summary>
    public static MissionModule Parse(PacketFile packet)
    {
        if (packet.SectionCount < 6)
            throw new GameDataException($"{packet.Name}: expected 6 sections, found {packet.SectionCount}.");
        var sections = new byte[6][];
        for (int i = 0; i < 6; i++)
            sections[i] = packet.GetSection(i).ToArray();
        return new MissionModule(packet.Name, sections);
    }

    /// <summary>Loads the module of a logical file through the resource provider.</summary>
    public static MissionModule Load(ISimulationResources resources, int logicalFile)
    {
        var sections = new byte[6][];
        for (int i = 0; i < 6; i++)
            sections[i] = resources.LoadSection(logicalFile, i).ToArray();
        return new MissionModule($"logical file {logicalFile}", sections);
    }

    /// <summary>
    /// True when the slot holds a loadable mission: index 0..63 and a player ship index in
    /// 0..31 (the SDL port's guard; an unused slot has player ship -1).
    /// </summary>
    public bool HasMission(int series, int mission)
    {
        int index = GetMissionIndex(series, mission);
        if (index < 0 || index >= MissionSlots)
            return false;
        short player = BinaryPrimitives.ReadInt16LittleEndian(_header.AsSpan(index * HeaderSize + 4, 2));
        return player >= 0 && player < ShipsPerMission;
    }

    /// <summary>
    /// Parses one mission. Returns null when <see cref="HasMission"/> is false (the SDL port's
    /// <c>LoadMissionData</c> returns 1 in that case).
    /// </summary>
    public MissionData? GetMission(int series, int mission)
    {
        if (!HasMission(series, mission))
            return null;
        int index = GetMissionIndex(series, mission);
        var header = ParseHeader(_header.AsSpan(index * HeaderSize, HeaderSize));

        var navs = new MissionNavPoint[NavPointsPerMission];
        for (int i = 0; i < navs.Length; i++)
            navs[i] = ParseNavPoint(_navPoints.AsSpan(index * NavStride + i * NavRecordSize, NavRecordSize));

        var objectives = new MissionObjectiveSource[ObjectivesPerMission];
        for (int i = 0; i < objectives.Length; i++)
            objectives[i] = ParseObjective(_objectives.AsSpan(index * ObjectiveStride + i * ObjectiveRecordSize, ObjectiveRecordSize));

        var ships = new MissionShipRecord[ShipsPerMission];
        for (int i = 0; i < ships.Length; i++)
            ships[i] = ParseShip(_ships.AsSpan(index * ShipStride + i * ShipRecordSize, ShipRecordSize));

        byte[] missionAux = _missionAux.AsSpan(index * AuxSize, AuxSize).ToArray();
        byte[] seriesAux = GetSeriesAux(series).ToArray();
        return new MissionData(series, mission, header, navs, objectives, ships, missionAux, seriesAux);
    }

    /// <summary>Text of the mission name record (section 4), e.g. "Alpha Wing".</summary>
    public string GetMissionName(int series, int mission)
    {
        int index = GetMissionIndex(series, mission);
        return index is >= 0 and < MissionSlots ? DecodeText(_missionAux.AsSpan(index * AuxSize, AuxSize)) : "";
    }

    /// <summary>Text of the series record (section 5), e.g. "Enyo" for series 1.</summary>
    public string GetSeriesName(int series) => DecodeText(GetSeriesAux(series));

    private ReadOnlySpan<byte> GetSeriesAux(int series) =>
        series is >= 0 and < SeriesSlots ? _seriesAux.AsSpan(series * AuxSize, AuxSize) : ReadOnlySpan<byte>.Empty;

    /// <summary>Parses the 0x18-byte header record.</summary>
    /// <remarks>C: MissionHeaderDisk.</remarks>
    public static MissionHeader ParseHeader(ReadOnlySpan<byte> r)
    {
        var initial = new short[8];
        for (int i = 0; i < 8; i++)
            initial[i] = I16(r, 6 + i * 2);
        return new MissionHeader(I16(r, 0), I16(r, 2), I16(r, 4), initial, I16(r, 0x16));
    }

    /// <summary>Parses one 77-byte nav point record into the runtime form.</summary>
    /// <remarks>C: MissionNavPointDisk: name[30], sbyte type @30, FixedVector @31, ushort radius @43,
    /// sbyte triggers[8] @45, short preload[2] @53, short ships[10] @57.</remarks>
    public static MissionNavPoint ParseNavPoint(ReadOnlySpan<byte> r)
    {
        var nav = new MissionNavPoint
        {
            Name = DecodeText(r[..30]),
            Type = (sbyte)r[30],
            Position = new FixedVector(I32(r, 31), I32(r, 35), I32(r, 39)),
            ProximityRadius = I16(r, 43),
        };
        for (int i = 0; i < 8; i++)
            nav.Triggers[i] = (sbyte)r[45 + i];
        for (int i = 0; i < 2; i++)
            nav.PreloadObjectTypes[i] = (ObjectType)I16(r, 53 + i * 2);
        for (int i = 0; i < 10; i++)
            nav.MissionShips[i] = I16(r, 57 + i * 2);
        return nav;
    }

    /// <summary>Parses one 64-byte objective record.</summary>
    /// <remarks>C: MissionObjectiveDisk: short type, short index, char description[60].</remarks>
    public static MissionObjectiveSource ParseObjective(ReadOnlySpan<byte> r) => new()
    {
        Type = I16(r, 0),
        Index = I16(r, 2),
        Description = DecodeText(r.Slice(4, 60)),
    };

    /// <summary>Parses one 42-byte ship record into the runtime form.</summary>
    /// <remarks>C: MissionShipDisk: short type @0, short side @2, sbyte leader @4, sbyte field_5 @5,
    /// short missionType @6, sbyte navPoint @8, FixedVector @9, short pitch/yaw/roll @21, sbyte
    /// formationSpot @27, short speed @28, short rating @30, short pilot @32, short field_2c @34,
    /// short field_2e @36, sbyte state @38, leaderMissionIndex @39, formationIndex @40,
    /// targetMissionIndex @41.</remarks>
    public static MissionShipRecord ParseShip(ReadOnlySpan<byte> r) => new()
    {
        Type = (ObjectType)I16(r, 0),
        Side = (Side)I16(r, 2),
        Leader = (sbyte)r[4],
        Field9 = (sbyte)r[5],
        MissionType = (ShipMissionType)I16(r, 6),
        NavPoint = (sbyte)r[8],
        Position = new FixedVector(I32(r, 9), I32(r, 13), I32(r, 17)),
        Pitch = I16(r, 21),
        Yaw = I16(r, 23),
        Roll = I16(r, 25),
        FormationSpot = (sbyte)r[27],
        Speed = I16(r, 28),
        Rating = I16(r, 30),
        Pilot = I16(r, 32),
        Field2C = I16(r, 34),
        Field2E = I16(r, 36),
        State = (sbyte)r[38],
        LeaderMissionIndex = (sbyte)r[39],
        FormationIndex = (sbyte)r[40],
        TargetMissionIndex = (sbyte)r[41],
    };

    /// <summary>Decodes a fixed-size char field up to the first NUL (Latin-1, byte = char).</summary>
    public static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        int length = bytes.IndexOf((byte)0);
        if (length < 0)
            length = bytes.Length;
        return Encoding.Latin1.GetString(bytes[..length]);
    }

    private static short I16(ReadOnlySpan<byte> r, int offset) => BinaryPrimitives.ReadInt16LittleEndian(r.Slice(offset, 2));

    private static int I32(ReadOnlySpan<byte> r, int offset) => BinaryPrimitives.ReadInt32LittleEndian(r.Slice(offset, 4));

    private void Require(byte[] section, int size, int index)
    {
        if (section.Length < size)
            throw new GameDataException($"{Name}: section {index} has {section.Length} bytes, expected at least {size}.");
    }
}
