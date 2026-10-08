using System.Buffers.Binary;
using System.Text;

namespace WingCommander.Game.Campaign;

/// <summary>
/// One pilot of the campaign roster (0x26 bytes on disk). Index = personality id 0..7 for the
/// wingmen, 8 = the player. Name and callsign keep their raw 14-byte buffers so save games
/// round-trip byte-exactly (the original leaves garbage after the terminating NUL).
/// </summary>
/// <remarks>C: PilotRecord (wcdata.h), aInitialPilotRecords (0x00470108), aPilotRecords (0x005988D0).</remarks>
public sealed class PilotRecord
{
    public const int Size = 0x26;
    public const int TextSize = 14;

    public byte[] NameBytes { get; } = new byte[TextSize];

    public byte[] CallsignBytes { get; } = new byte[TextSize];

    public short Portrait { get; set; }

    /// <summary>0 2nd Lt., 1 1st Lt., 2 Captain, 3 Major, 4 Lt. Col.</summary>
    public short Rank { get; set; }

    public short Missions { get; set; }

    public short Kills { get; set; }

    /// <summary>Last field of the record; purpose unknown (values 0..4, see gameflow analysis §9).</summary>
    public short Personality { get; set; }

    public string Name
    {
        get => ReadCString(NameBytes);
        set => WriteCString(NameBytes, value);
    }

    public string Callsign
    {
        get => ReadCString(CallsignBytes);
        set => WriteCString(CallsignBytes, value);
    }

    public static PilotRecord Create(string name, string callsign, short portrait, short rank, short missions, short kills, short personality)
    {
        var p = new PilotRecord
        {
            Portrait = portrait,
            Rank = rank,
            Missions = missions,
            Kills = kills,
            Personality = personality,
        };
        p.Name = name;
        p.Callsign = callsign;
        return p;
    }

    /// <summary>The nine records the campaign starts with (index 8 = player placeholder).</summary>
    /// <remarks>C: aInitialPilotRecords (0x00470108).</remarks>
    public static PilotRecord[] CreateInitialRoster() =>
    [
        Create("TANAKA", "SPIRIT", 3, 1, 11, 14, 1),
        Create("ST.JOHN", "HUNTER", 4, 2, 25, 32, 4),
        Create("CHEN", "BOSSMAN", 1, 3, 35, 37, 2),
        Create("CASEY", "ICEMAN", 0, 3, 28, 43, 1),
        Create("DEVEREAUX", "ANGEL", 0, 2, 22, 20, 1),
        Create("TAGGART", "PALADIN", 2, 3, 42, 34, 2),
        Create("MARSHALL", "MANIAC", 4, 0, 5, 6, 1),
        Create("KHUMALO", "KNIGHT", 3, 2, 18, 23, 3),
        Create("PELLEY", "GOBLIN", 0, 0, 0, 0, 0),
    ];

    public PilotRecord Clone()
    {
        var p = new PilotRecord
        {
            Portrait = Portrait,
            Rank = Rank,
            Missions = Missions,
            Kills = Kills,
            Personality = Personality,
        };
        NameBytes.CopyTo(p.NameBytes, 0);
        CallsignBytes.CopyTo(p.CallsignBytes, 0);
        return p;
    }

    public static PilotRecord Read(ReadOnlySpan<byte> data)
    {
        var p = new PilotRecord();
        data[..TextSize].CopyTo(p.NameBytes);
        data.Slice(0x0e, TextSize).CopyTo(p.CallsignBytes);
        p.Portrait = BinaryPrimitives.ReadInt16LittleEndian(data[0x1c..]);
        p.Rank = BinaryPrimitives.ReadInt16LittleEndian(data[0x1e..]);
        p.Missions = BinaryPrimitives.ReadInt16LittleEndian(data[0x20..]);
        p.Kills = BinaryPrimitives.ReadInt16LittleEndian(data[0x22..]);
        p.Personality = BinaryPrimitives.ReadInt16LittleEndian(data[0x24..]);
        return p;
    }

    public void Write(Span<byte> data)
    {
        NameBytes.CopyTo(data);
        CallsignBytes.CopyTo(data[0x0e..]);
        BinaryPrimitives.WriteInt16LittleEndian(data[0x1c..], Portrait);
        BinaryPrimitives.WriteInt16LittleEndian(data[0x1e..], Rank);
        BinaryPrimitives.WriteInt16LittleEndian(data[0x20..], Missions);
        BinaryPrimitives.WriteInt16LittleEndian(data[0x22..], Kills);
        BinaryPrimitives.WriteInt16LittleEndian(data[0x24..], Personality);
    }

    internal static string ReadCString(ReadOnlySpan<byte> bytes)
    {
        int length = bytes.IndexOf((byte)0);
        if (length < 0)
            length = bytes.Length;
        return Encoding.Latin1.GetString(bytes[..length]);
    }

    /// <summary>Writes a NUL-terminated string, truncated to fit; bytes after the NUL are kept.</summary>
    internal static void WriteCString(Span<byte> destination, string value)
    {
        int length = Math.Min(value.Length, destination.Length - 1);
        for (int i = 0; i < length; i++)
            destination[i] = (byte)(value[i] <= 0xff ? value[i] : '?');
        destination[length] = 0;
    }
}
