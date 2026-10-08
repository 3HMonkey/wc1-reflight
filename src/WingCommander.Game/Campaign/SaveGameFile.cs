using System.Buffers.Binary;
using WingCommander.Core.Numerics;

namespace WingCommander.Game.Campaign;

/// <summary>
/// One mission objective as stored in a save game (0x19 bytes). The two name fields are
/// truncated pointers in the original and are meaningless on disk; the runtime objectives
/// are rebuilt from the mission data before use.
/// </summary>
/// <remarks>C: SaveGameDiskObjective.</remarks>
public struct SavedObjective
{
    public const int Size = 0x19;

    public short MapX;
    public short MapY;
    public byte Field4;
    public short Type;
    public sbyte Index;

    /// <summary>1 visited, 2 achieved, 4 sighted.</summary>
    public byte Flags;

    public short DisplayNameRaw;
    public short NameRaw;
    public FixedVector Position;

    public static SavedObjective Read(ReadOnlySpan<byte> d) => new()
    {
        MapX = BinaryPrimitives.ReadInt16LittleEndian(d),
        MapY = BinaryPrimitives.ReadInt16LittleEndian(d[2..]),
        Field4 = d[4],
        Type = BinaryPrimitives.ReadInt16LittleEndian(d[5..]),
        Index = unchecked((sbyte)d[7]),
        Flags = d[8],
        DisplayNameRaw = BinaryPrimitives.ReadInt16LittleEndian(d[9..]),
        NameRaw = BinaryPrimitives.ReadInt16LittleEndian(d[0x0b..]),
        Position = new FixedVector(
            BinaryPrimitives.ReadInt32LittleEndian(d[0x0d..]),
            BinaryPrimitives.ReadInt32LittleEndian(d[0x11..]),
            BinaryPrimitives.ReadInt32LittleEndian(d[0x15..])),
    };

    public readonly void Write(Span<byte> d)
    {
        BinaryPrimitives.WriteInt16LittleEndian(d, MapX);
        BinaryPrimitives.WriteInt16LittleEndian(d[2..], MapY);
        d[4] = Field4;
        BinaryPrimitives.WriteInt16LittleEndian(d[5..], Type);
        d[7] = unchecked((byte)Index);
        d[8] = Flags;
        BinaryPrimitives.WriteInt16LittleEndian(d[9..], DisplayNameRaw);
        BinaryPrimitives.WriteInt16LittleEndian(d[0x0b..], NameRaw);
        BinaryPrimitives.WriteInt32LittleEndian(d[0x0d..], Position.X);
        BinaryPrimitives.WriteInt32LittleEndian(d[0x11..], Position.Y);
        BinaryPrimitives.WriteInt32LittleEndian(d[0x15..], Position.Z);
    }
}

/// <summary>One bunk of SAVEGAME.WLD (0x33C bytes).</summary>
/// <remarks>C: SaveGameDiskRecord / SaveGameRecord.</remarks>
public sealed class SaveGameSlot
{
    public const int Size = 0x33c;
    public const int DescriptionSize = 17;

    /// <summary>Raw description buffer (16 characters + NUL; garbage after the NUL is preserved).</summary>
    public byte[] DescriptionBytes { get; } = new byte[DescriptionSize];

    public bool Occupied { get; set; }

    public PilotRecord[] Pilots { get; } = new PilotRecord[9];

    public CampaignState Campaign { get; set; } = CampaignState.CreateInitial();

    public SavedObjective[] Objectives { get; } = new SavedObjective[16];

    public string Description
    {
        get => PilotRecord.ReadCString(DescriptionBytes);
        set => PilotRecord.WriteCString(DescriptionBytes, value);
    }

    public static SaveGameSlot Read(ReadOnlySpan<byte> d)
    {
        var slot = new SaveGameSlot();
        d[..DescriptionSize].CopyTo(slot.DescriptionBytes);
        slot.Occupied = d[0x11] != 0;
        for (int i = 0; i < 9; i++)
            slot.Pilots[i] = PilotRecord.Read(d.Slice(0x12 + i * PilotRecord.Size, PilotRecord.Size));

        var c = d.Slice(0x168, 0x44);
        var s = new CampaignState
        {
            CurrentPilotRaw = BinaryPrimitives.ReadInt16LittleEndian(c),
            PlayerShipType = BinaryPrimitives.ReadInt16LittleEndian(c[2..]),
            CurrentMission = unchecked((sbyte)c[0x15]),
            CurrentSeries = unchecked((sbyte)c[0x16]),
            SeriesHistoryCount = unchecked((sbyte)c[0x17]),
            CurrentDate = new CampaignDate(BinaryPrimitives.ReadInt16LittleEndian(c[0x34..]), BinaryPrimitives.ReadInt16LittleEndian(c[0x36..])),
            ElapsedDate = new CampaignDate(BinaryPrimitives.ReadInt16LittleEndian(c[0x38..]), BinaryPrimitives.ReadInt16LittleEndian(c[0x3a..])),
            PromotionScore = BinaryPrimitives.ReadInt16LittleEndian(c[0x3c..]),
            MissionScore = BinaryPrimitives.ReadInt16LittleEndian(c[0x3e..]),
            SeriesScore = BinaryPrimitives.ReadInt16LittleEndian(c[0x40..]),
            CampaignIndex = BinaryPrimitives.ReadInt16LittleEndian(c[0x42..]),
        };
        c.Slice(4, 5).CopyTo(s.Medals);
        c.Slice(9, 12).CopyTo(s.Badges);
        for (int i = 0; i < 8; i++)
            s.SeriesHistory[i] = unchecked((sbyte)c[0x18 + i]);
        for (int i = 0; i < 8; i++)
            s.PersonalityDeathMission[i] = BinaryPrimitives.ReadInt16LittleEndian(c[(0x20 + i * 2)..]);
        c.Slice(0x30, 4).CopyTo(s.AceFlags);
        slot.Campaign = s;

        for (int i = 0; i < 16; i++)
            slot.Objectives[i] = SavedObjective.Read(d.Slice(0x1ac + i * SavedObjective.Size, SavedObjective.Size));
        return slot;
    }

    public void Write(Span<byte> d)
    {
        d[..Size].Clear();
        DescriptionBytes.CopyTo(d);
        d[0x11] = (byte)(Occupied ? 1 : 0);
        for (int i = 0; i < 9; i++)
            (Pilots[i] ?? new PilotRecord()).Write(d.Slice(0x12 + i * PilotRecord.Size, PilotRecord.Size));

        var c = d.Slice(0x168, 0x44);
        var s = Campaign;
        BinaryPrimitives.WriteInt16LittleEndian(c, s.CurrentPilotRaw);
        BinaryPrimitives.WriteInt16LittleEndian(c[2..], unchecked((short)s.PlayerShipType));
        s.Medals.CopyTo(c[4..]);
        s.Badges.CopyTo(c[9..]);
        c[0x15] = unchecked((byte)s.CurrentMission);
        c[0x16] = unchecked((byte)s.CurrentSeries);
        c[0x17] = unchecked((byte)s.SeriesHistoryCount);
        for (int i = 0; i < 8; i++)
            c[0x18 + i] = unchecked((byte)s.SeriesHistory[i]);
        for (int i = 0; i < 8; i++)
            BinaryPrimitives.WriteInt16LittleEndian(c[(0x20 + i * 2)..], unchecked((short)s.PersonalityDeathMission[i]));
        s.AceFlags.CopyTo(c[0x30..]);
        BinaryPrimitives.WriteInt16LittleEndian(c[0x34..], s.CurrentDate.Day);
        BinaryPrimitives.WriteInt16LittleEndian(c[0x36..], s.CurrentDate.Year);
        BinaryPrimitives.WriteInt16LittleEndian(c[0x38..], s.ElapsedDate.Day);
        BinaryPrimitives.WriteInt16LittleEndian(c[0x3a..], s.ElapsedDate.Year);
        BinaryPrimitives.WriteInt16LittleEndian(c[0x3c..], s.PromotionScore);
        BinaryPrimitives.WriteInt16LittleEndian(c[0x3e..], s.MissionScore);
        BinaryPrimitives.WriteInt16LittleEndian(c[0x40..], s.SeriesScore);
        BinaryPrimitives.WriteInt16LittleEndian(c[0x42..], s.CampaignIndex);

        for (int i = 0; i < 16; i++)
            Objectives[i].Write(d.Slice(0x1ac + i * SavedObjective.Size, SavedObjective.Size));
    }

    /// <summary>An unused bunk named "game n" (the original left the rest uninitialised; the port writes zeros).</summary>
    /// <remarks>C: CreateEmptySaveGameFile (0x41ADA0).</remarks>
    public static SaveGameSlot CreateEmpty(int slotIndex)
    {
        var slot = new SaveGameSlot { Occupied = false, Campaign = new CampaignState { CurrentSeries = 0 } };
        for (int i = 0; i < 9; i++)
            slot.Pilots[i] = new PilotRecord();
        slot.Description = $"game {slotIndex + 1}";
        return slot;
    }
}

/// <summary>
/// SAVEGAME.WLD: eight bunks of <see cref="SaveGameSlot.Size"/> bytes (6624 bytes). A file with
/// the wrong length is recreated empty, like the original.
/// </summary>
/// <remarks>C: EnsureSaveGameFile, CreateEmptySaveGameFile, LoadGame, SaveGame (barracks.c).</remarks>
public static class SaveGameFile
{
    public const int SlotCount = 8;
    public const int FileSize = SlotCount * SaveGameSlot.Size;
    public const string FileName = "SAVEGAME.WLD";

    /// <remarks>C: EnsureSaveGameFile (0x41B020).</remarks>
    public static void Ensure(string path)
    {
        if (File.Exists(path) && new FileInfo(path).Length == FileSize)
            return;
        CreateEmpty(path);
    }

    public static void CreateEmpty(string path)
    {
        var bytes = new byte[FileSize];
        for (int i = 0; i < SlotCount; i++)
            SaveGameSlot.CreateEmpty(i).Write(bytes.AsSpan(i * SaveGameSlot.Size, SaveGameSlot.Size));
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Reads a slot; returns null when the file is missing/short or the slot is unoccupied.</summary>
    /// <remarks>C: LoadGame (0x41B710).</remarks>
    public static SaveGameSlot? Load(string path, int slot)
    {
        var record = ReadSlot(path, slot);
        return record is { Occupied: true } ? record : null;
    }

    /// <summary>Reads a slot regardless of its occupied flag (bunk labels need the description).</summary>
    public static SaveGameSlot? ReadSlot(string path, int slot)
    {
        if ((uint)slot >= SlotCount || !File.Exists(path))
            return null;
        byte[] bytes = File.ReadAllBytes(path);
        int offset = slot * SaveGameSlot.Size;
        if (bytes.Length < offset + SaveGameSlot.Size)
            return null;
        return SaveGameSlot.Read(bytes.AsSpan(offset, SaveGameSlot.Size));
    }

    /// <summary>Writes one slot in place (the file is created first if needed).</summary>
    /// <remarks>C: SaveGame (barracks.c).</remarks>
    public static void Save(string path, int slot, SaveGameSlot record)
    {
        if ((uint)slot >= SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot));
        Ensure(path);
        var buffer = new byte[SaveGameSlot.Size];
        record.Write(buffer);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
        stream.Position = slot * SaveGameSlot.Size;
        stream.Write(buffer);
    }

    /// <summary>True when any slot holds a game; also reports whether one belongs to a Secret Missions campaign.</summary>
    /// <remarks>C: AnySavedGames (0x41AD50).</remarks>
    public static bool AnySavedGames(string path, out bool secretMissionsSave)
    {
        secretMissionsSave = false;
        bool found = false;
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = Load(path, i);
            if (slot is null)
                continue;
            found = true;
            if (slot.Campaign.CampaignIndex > 0)
                secretMissionsSave = true;
        }
        return found;
    }
}
