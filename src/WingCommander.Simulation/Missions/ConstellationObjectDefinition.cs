using System.Buffers.Binary;

namespace WingCommander.Simulation.Missions;

/// <summary>
/// One of four background planets of a series (CAMP.xxx section 0: 13 series x 4 records of
/// 8 bytes). <see cref="ShapePacket"/> -1 = none, else PLANETS.VGA section <c>ShapePacket + 1</c>;
/// the angles orient the planet around the scene at radius 30000.
/// </summary>
/// <remarks>C: ConstellationObjectDefinition (include/wcdata.h), pConstellationDefinitions.</remarks>
public readonly record struct ConstellationObjectDefinition(short ShapePacket, short Yaw, short Pitch, short Roll)
{
    public const int Size = 8;

    /// <summary>Parses the whole CAMP section 0 table (series-major, four entries per series,
    /// first entry = series 1).</summary>
    public static ConstellationObjectDefinition[] ParseTable(ReadOnlySpan<byte> section)
    {
        var result = new ConstellationObjectDefinition[section.Length / Size];
        for (int i = 0; i < result.Length; i++)
        {
            var r = section.Slice(i * Size, Size);
            result[i] = new ConstellationObjectDefinition(
                BinaryPrimitives.ReadInt16LittleEndian(r),
                BinaryPrimitives.ReadInt16LittleEndian(r[2..]),
                BinaryPrimitives.ReadInt16LittleEndian(r[4..]),
                BinaryPrimitives.ReadInt16LittleEndian(r[6..]));
        }
        return result;
    }
}
