using System.Text;

namespace WingCommander.Core.Resources;

/// <summary>
/// One 16-byte record of INSTALL.DAT: 13-byte zero-padded 8.3 file name, floppy disk
/// number, file class, and the 1-based logical file id (0xFF = not addressable by id).
/// </summary>
/// <remarks>C: DiskFileRecord (include/wcdata.h).</remarks>
public readonly record struct DiskFileRecord(string Name, sbyte DiskNumber, byte FileClass, byte LogicalFileId)
{
    public const int Size = 16;

    public static DiskFileRecord Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size)
            throw new GameDataException("INSTALL.DAT record truncated.");
        int nameLength = bytes[..13].IndexOf((byte)0);
        if (nameLength < 0)
            nameLength = 13;
        string name = Encoding.ASCII.GetString(bytes[..nameLength]);
        return new DiskFileRecord(name, (sbyte)bytes[13], bytes[14], bytes[15]);
    }
}

/// <summary>
/// The runtime lookup table built from INSTALL.DAT: index = logical file id - 1. The game
/// code addresses data files by that 0-based index (e.g. 0 = FONTS.FNT, 9 = TITLE.VGA,
/// 15 = MODULE.000, 58 = CAMP.000). The DOS INSTALL.DAT only lists the base game and
/// Secret Missions 1; the Secret Missions 2 files are appended at indices 72..75 exactly
/// as the reference port does.
/// </summary>
/// <remarks>C: LoadInstallDat, SdlCompleteDosInstallTable, pDiskFileRecords.</remarks>
public sealed class InstallTable
{
    /// <summary>Number of runtime slots (0..76); slot 76 is left empty like the reference.</summary>
    public const int SlotCount = 77;

    private readonly DiskFileRecord?[] _byIndex;

    private InstallTable(DiskFileRecord?[] byIndex, IReadOnlyList<DiskFileRecord> records)
    {
        _byIndex = byIndex;
        Records = records;
    }

    /// <summary>All records in file order (including DISK.xxx markers and executables).</summary>
    public IReadOnlyList<DiskFileRecord> Records { get; }

    /// <summary>Returns the record for a 0-based logical file index.</summary>
    public DiskFileRecord this[int logicalFile] =>
        (uint)logicalFile < (uint)_byIndex.Length && _byIndex[logicalFile] is { } r
            ? r
            : throw new GameDataException($"No data file is registered for logical file {logicalFile}.");

    public bool TryGet(int logicalFile, out DiskFileRecord record)
    {
        if ((uint)logicalFile < (uint)_byIndex.Length && _byIndex[logicalFile] is { } r)
        {
            record = r;
            return true;
        }
        record = default;
        return false;
    }

    public static InstallTable Parse(ReadOnlySpan<byte> installDat, bool addSecretMissions2 = true)
    {
        var records = new List<DiskFileRecord>(installDat.Length / DiskFileRecord.Size);
        var byIndex = new DiskFileRecord?[SlotCount];
        for (int offset = 0; offset + DiskFileRecord.Size <= installDat.Length; offset += DiskFileRecord.Size)
        {
            var record = DiskFileRecord.Parse(installDat.Slice(offset, DiskFileRecord.Size));
            if (record.Name.Length == 0)
                continue;
            records.Add(record);
            if (record.LogicalFileId != 0xff && record.LogicalFileId >= 1 && record.LogicalFileId <= SlotCount)
                byIndex[record.LogicalFileId - 1] = record;
        }

        if (addSecretMissions2)
        {
            // C: SdlCompleteDosInstallTable — the SM2 files added at records[72..75].
            byIndex[72] = new DiskFileRecord("MODULE.002", 1, 7, 73);
            byIndex[73] = new DiskFileRecord("BRIEFING.002", 1, 2, 74);
            byIndex[74] = new DiskFileRecord("CAMP.002", 1, 2, 75);
            byIndex[75] = new DiskFileRecord("TITLE1.VGA", 1, 2, 76);
        }
        return new InstallTable(byIndex, records);
    }
}

/// <summary>Well-known 0-based logical file indices (INSTALL.DAT logical id - 1).</summary>
public static class LogicalFile
{
    public const int Fonts = 0;          // FONTS.FNT
    public const int ScrambleVga = 1;    // SCRAMBLE.VGA
    public const int PilotAnimVga = 2;   // PILOTANM.VGA
    public const int ObjectsVga = 3;     // OBJECTS.VGA
    public const int BriefingVga = 4;    // BRIEFING.VGA
    public const int RecRoomVga = 5;     // RECROOM.VGA
    public const int TalkingVga = 6;     // TALKING.VGA
    public const int MusicMid = 7;       // MUSIC.MID
    public const int CockpitVga = 8;     // COCKPIT.VGA
    public const int TitleVga = 9;       // TITLE.VGA
    public const int Briefing000 = 10;   // BRIEFING.000
    public const int WingmenVga = 11;    // WINGMEN.VGA
    public const int PlanetsVga = 12;    // PLANETS.VGA
    public const int CommunicDat = 13;   // COMMUNIC.DAT
    public const int ArrowVga = 14;      // ARROW.VGA
    public const int Module000 = 15;     // MODULE.000
    public const int SaveGameWld = 16;   // SAVEGAME.WLD
    public const int PcShipV00 = 17;     // PCSHIP.V00 .. V04 = 17..21
    public const int ShipTypeV00 = 22;   // SHIPTYPE.V00 .. (ids 0x17..) see INSTALL.DAT
    public const int ShipV04 = 26;       // SHIP.V04 (id 0x1b)
    public const int Module001 = 52;     // MODULE.001 (id 0x35)
    public const int Camp000 = 58;       // CAMP.000 (id 0x3b)
    public const int WingLdrTim = 59;    // WINGLDR.TIM (id 0x3c)
    public const int IntroDat = 60;      // INTRO.DAT (id 0x3d)
    public const int Camp001 = 61;       // CAMP.001 (id 0x3e)
    public const int Briefing001 = 62;   // BRIEFING.001 (id 0x3f)
    public const int MidgameV00 = 63;    // MIDGAME.V00 (id 0x40) .. V05 = 68
    public const int SeriesVga = 71;     // SERIES.VGA (id 0x48)
    public const int Module002 = 72;     // MODULE.002 (added)
    public const int Briefing002 = 73;   // BRIEFING.002 (added)
    public const int Camp002 = 74;       // CAMP.002 (added)
    public const int Title1Vga = 75;     // TITLE1.VGA (added)
}
