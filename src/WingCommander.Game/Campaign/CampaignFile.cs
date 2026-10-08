using System.Buffers.Binary;
using WingCommander.Core.Resources;

namespace WingCommander.Game.Campaign;

/// <summary>A planet placed around a series' constellation (PLANETS.VGA section, orientation).</summary>
/// <remarks>C: ConstellationObjectDefinition.</remarks>
public readonly record struct ConstellationObject(short ShapePacket, short Yaw, short Pitch, short Roll);

/// <summary>Scoring row of one mission inside a series record (20 bytes).</summary>
public sealed class SeriesMission
{
    /// <summary>Medal awarded when earned (see <see cref="Medal"/>).</summary>
    public short MedalIndex { get; init; }

    /// <summary>Required medal score (9000+/2000 effectively mean "never").</summary>
    public short MedalThreshold { get; init; }

    /// <summary>Points per mission objective, indexed like the mission's objective list.</summary>
    public sbyte[] ObjectiveScores { get; init; } = new sbyte[16];
}

/// <summary>One series (system) of the campaign tree (90 bytes).</summary>
public sealed class SeriesRecord
{
    /// <summary>Series number 1..13.</summary>
    public int Series { get; init; }

    /// <summary>Wingman portrait in the debriefing long shot.</summary>
    public short DebriefPersonality { get; init; }

    public sbyte MissionCount { get; init; }

    /// <summary>The series is failed when its score stays below this.</summary>
    public short ScoreThreshold { get; init; }

    /// <summary>-1 none, 0..7 MIDGAME.V0n cutscene, 0x40 victory ending, 0x41 Tiger's Claw escape ending.</summary>
    public sbyte PostSeriesSequence { get; init; }

    public sbyte WinNextSeries { get; init; }

    public sbyte WinShipType { get; init; }

    public sbyte LoseNextSeries { get; init; }

    public sbyte LoseShipType { get; init; }

    public SeriesMission[] Missions { get; init; } = [];
}

/// <summary>
/// CAMP.000/.001/.002: constellations (section 0, 13 x 4 x 8 bytes), the series tree with
/// scoring (section 1, 13 x 90 bytes) and the rec-room roster (section 2, 13 x 4 x 2 personality
/// ids). The raw series table is kept because the original indexes it with arithmetic that
/// can read the neighbouring record.
/// </summary>
/// <remarks>C: pConstellationDefinitions, pMissionCampaignData, pRecRoomRoster; asCampaignPilotFiles = {58, 61, 74}.</remarks>
public sealed class CampaignFile
{
    public const int SeriesCount = 13;
    public const int SeriesRecordSize = 0x5a;
    public const int MissionBlockSize = 0x14;

    private static readonly int[] LogicalFiles = [LogicalFile.Camp000, LogicalFile.Camp001, LogicalFile.Camp002];

    private CampaignFile(byte[] seriesTable, ConstellationObject[,] constellations, sbyte[] roster)
    {
        SeriesTable = seriesTable;
        Constellations = constellations;
        RecRoomRoster = roster;
        var series = new SeriesRecord[SeriesCount];
        for (int s = 1; s <= SeriesCount; s++)
            series[s - 1] = ParseSeries(s);
        Series = series;
    }

    /// <summary>Raw section 1 (1170 bytes): record of series s at (s - 1) * 90.</summary>
    public byte[] SeriesTable { get; }

    /// <summary>[series - 1, object 0..3].</summary>
    public ConstellationObject[,] Constellations { get; }

    /// <summary>Two personality ids per (series, mission); -1 = nobody. Index (mission + series * 4) * 2 - 8.</summary>
    public sbyte[] RecRoomRoster { get; }

    public IReadOnlyList<SeriesRecord> Series { get; }

    public SeriesRecord GetSeries(int series) => Series[series - 1];

    /// <summary>Logical file of CAMP.&lt;campaign&gt;.</summary>
    public static int LogicalFileFor(int campaignIndex) => LogicalFiles[campaignIndex];

    public static CampaignFile Load(GameDirectory directory, int campaignIndex)
    {
        var packet = directory.OpenPacket(LogicalFileFor(campaignIndex));
        return Parse(packet);
    }

    public static CampaignFile Parse(PacketFile packet)
    {
        if (packet.SectionCount < 3)
            throw new GameDataException($"{packet.Name}: expected 3 sections, found {packet.SectionCount}.");
        var s0 = packet.GetSection(0).Span;
        var s1 = packet.GetSection(1).Span;
        var s2 = packet.GetSection(2).Span;
        if (s0.Length < SeriesCount * 32 || s1.Length < SeriesCount * SeriesRecordSize || s2.Length < SeriesCount * 8)
            throw new GameDataException($"{packet.Name}: sections are too short.");

        var constellations = new ConstellationObject[SeriesCount, 4];
        for (int s = 0; s < SeriesCount; s++)
        {
            for (int o = 0; o < 4; o++)
            {
                var r = s0[(s * 32 + o * 8)..];
                constellations[s, o] = new ConstellationObject(
                    BinaryPrimitives.ReadInt16LittleEndian(r),
                    BinaryPrimitives.ReadInt16LittleEndian(r[2..]),
                    BinaryPrimitives.ReadInt16LittleEndian(r[4..]),
                    BinaryPrimitives.ReadInt16LittleEndian(r[6..]));
            }
        }
        var roster = new sbyte[SeriesCount * 8];
        for (int i = 0; i < roster.Length; i++)
            roster[i] = unchecked((sbyte)s2[i]);
        return new CampaignFile(s1[..(SeriesCount * SeriesRecordSize)].ToArray(), constellations, roster);
    }

    /// <summary>Byte at <c>series * 0x5a + offset - 0x5a</c> of the raw table, or null when outside it.</summary>
    public sbyte? RawSeriesByte(int series, int offset)
    {
        int index = series * SeriesRecordSize + offset - SeriesRecordSize;
        return (uint)index < (uint)SeriesTable.Length ? unchecked((sbyte)SeriesTable[index]) : null;
    }

    /// <summary>The two personality ids sitting in the bar for (series, mission).</summary>
    public (sbyte First, sbyte Second) GetRecRoomPilots(int series, int mission)
    {
        int index = (mission + series * 4) * 2 - 8;
        return (RecRoomRoster[index], RecRoomRoster[index + 1]);
    }

    private SeriesRecord ParseSeries(int series)
    {
        ReadOnlySpan<byte> r = SeriesTable.AsSpan((series - 1) * SeriesRecordSize, SeriesRecordSize);
        var missions = new SeriesMission[4];
        for (int m = 0; m < 4; m++)
        {
            var block = r[(10 + m * MissionBlockSize)..];
            var scores = new sbyte[16];
            for (int i = 0; i < 16; i++)
                scores[i] = unchecked((sbyte)block[4 + i]);
            missions[m] = new SeriesMission
            {
                MedalIndex = BinaryPrimitives.ReadInt16LittleEndian(block),
                MedalThreshold = BinaryPrimitives.ReadInt16LittleEndian(block[2..]),
                ObjectiveScores = scores,
            };
        }
        return new SeriesRecord
        {
            Series = series,
            DebriefPersonality = BinaryPrimitives.ReadInt16LittleEndian(r),
            MissionCount = unchecked((sbyte)r[2]),
            ScoreThreshold = BinaryPrimitives.ReadInt16LittleEndian(r[3..]),
            PostSeriesSequence = unchecked((sbyte)r[5]),
            WinNextSeries = unchecked((sbyte)r[6]),
            WinShipType = unchecked((sbyte)r[7]),
            LoseNextSeries = unchecked((sbyte)r[8]),
            LoseShipType = unchecked((sbyte)r[9]),
            Missions = missions,
        };
    }
}
