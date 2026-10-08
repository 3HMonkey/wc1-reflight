using System.Buffers.Binary;

namespace WingCommander.Core.Resources;

/// <summary>
/// Compression flag stored in the top byte of each packet directory entry.
/// Only <see cref="Lzw"/> changes how data is read; every other value is stored raw
/// (the Kilrathi Saga MUSIC.MID uses 0xE0, DOS .VGA files use 2 for raw shape tables).
/// </summary>
public enum PacketCompression : byte
{
    None = 0,
    Lzw = 1,
}

/// <summary>Location of one section inside the container file.</summary>
public readonly record struct PacketSectionInfo(int Index, int Offset, int StoredSize, byte Flags)
{
    public PacketCompression Compression => Flags == 1 ? PacketCompression.Lzw : PacketCompression.None;
    public int End => Offset + StoredSize;
}

/// <summary>
/// An Origin "packet" container: a little-endian u32 declared file size, then a directory
/// of u32 entries (top byte = compression flag, low 24 bits = absolute offset). Entry 0's
/// offset equals the directory size, so the section count is directorySize/4 - 1. Section
/// <c>i</c> spans from its offset to the next entry's offset (or the declared file size
/// for the last section). Sections may themselves be packet containers (nested).
/// </summary>
/// <remarks>C: SdlExtractOriginPacketSection / OpenPacketSection / PacketLoad.</remarks>
public sealed class PacketFile
{
    private const int OffsetMask = 0x00ffffff;
    private const int MaxSectionSize = 16 * 1024 * 1024;

    private readonly ReadOnlyMemory<byte> _data;
    private readonly PacketSectionInfo[] _sections;
    private readonly byte[]?[] _decoded;

    private PacketFile(string name, ReadOnlyMemory<byte> data, PacketSectionInfo[] sections)
    {
        Name = name;
        _data = data;
        _sections = sections;
        _decoded = new byte[]?[sections.Length];
    }

    /// <summary>Display name (file name or "parent[index]") used in error messages.</summary>
    public string Name { get; }

    public int SectionCount => _sections.Length;

    /// <summary>The declared size from the first four bytes.</summary>
    public int DeclaredSize => BinaryPrimitives.ReadInt32LittleEndian(_data.Span);

    public PacketSectionInfo GetInfo(int index)
    {
        if ((uint)index >= (uint)_sections.Length)
            throw new ArgumentOutOfRangeException(nameof(index), $"{Name}: section {index} of {_sections.Length}");
        return _sections[index];
    }

    /// <summary>Returns the raw (still compressed, without the size prefix handling) bytes of a section.</summary>
    public ReadOnlyMemory<byte> GetStored(int index)
    {
        var info = GetInfo(index);
        return _data.Slice(info.Offset, info.StoredSize);
    }

    /// <summary>
    /// Returns the decoded content of a section. Results are cached; callers must treat
    /// the memory as read-only.
    /// </summary>
    public ReadOnlyMemory<byte> GetSection(int index)
    {
        var info = GetInfo(index);
        byte[]? cached = _decoded[index];
        if (cached is not null)
            return cached;

        ReadOnlySpan<byte> stored = _data.Span.Slice(info.Offset, info.StoredSize);
        byte[] result;
        if (info.Compression == PacketCompression.Lzw)
        {
            if (stored.Length < 4)
                throw new GameDataException($"{Name}[{index}]: compressed section shorter than its size prefix.");
            int outputSize = BinaryPrimitives.ReadInt32LittleEndian(stored);
            if (outputSize < 0 || outputSize > MaxSectionSize)
                throw new GameDataException($"{Name}[{index}]: implausible uncompressed size {outputSize}.");
            result = new byte[outputSize];
            try
            {
                OriginLzw.Decompress(stored[4..], result);
            }
            catch (GameDataException e)
            {
                throw new GameDataException($"{Name}[{index}]: {e.Message}", e);
            }
        }
        else
        {
            result = stored.ToArray();
        }
        _decoded[index] = result;
        return result;
    }

    /// <summary>Decoded size of a section without caching the content when it is raw.</summary>
    public int GetDecodedSize(int index)
    {
        var info = GetInfo(index);
        if (info.Compression != PacketCompression.Lzw)
            return info.StoredSize;
        ReadOnlySpan<byte> stored = _data.Span.Slice(info.Offset, info.StoredSize);
        return stored.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(stored) : 0;
    }

    /// <summary>True when the decoded section looks like a packet container itself.</summary>
    public bool IsNestedPacket(int index) => LooksLikePacket(GetSection(index).Span);

    /// <summary>Opens a section as a nested packet container.</summary>
    public PacketFile OpenNested(int index)
    {
        var section = GetSection(index);
        return Parse($"{Name}[{index}]", section);
    }

    public static PacketFile Load(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (IOException e)
        {
            throw new GameDataException($"Cannot read '{path}': {e.Message}", e);
        }
        return Parse(Path.GetFileName(path), bytes);
    }

    public static PacketFile Parse(string name, ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (span.Length < 8)
            throw new GameDataException($"{name}: too small to be a packet file ({span.Length} bytes).");

        int declaredSize = BinaryPrimitives.ReadInt32LittleEndian(span);
        uint entry0 = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        int directorySize = (int)(entry0 & OffsetMask);
        if (declaredSize < 8 || declaredSize > span.Length)
            throw new GameDataException($"{name}: declared size {declaredSize} exceeds file size {span.Length}.");
        if (directorySize < 8 || directorySize > declaredSize || (directorySize & 3) != 0)
            throw new GameDataException($"{name}: invalid directory size {directorySize}.");

        int sectionCount = directorySize / 4 - 1;
        var sections = new PacketSectionInfo[sectionCount];
        for (int i = 0; i < sectionCount; i++)
        {
            uint entry = BinaryPrimitives.ReadUInt32LittleEndian(span[(4 + i * 4)..]);
            int offset = (int)(entry & OffsetMask);
            int end;
            if (i + 1 == sectionCount)
            {
                end = declaredSize;
            }
            else
            {
                uint next = BinaryPrimitives.ReadUInt32LittleEndian(span[(4 + (i + 1) * 4)..]);
                end = (int)(next & OffsetMask);
            }
            if (offset < directorySize || end < offset || end > declaredSize)
                throw new GameDataException($"{name}: section {i} has invalid bounds {offset}..{end}.");
            sections[i] = new PacketSectionInfo(i, offset, end - offset, (byte)(entry >> 24));
        }
        return new PacketFile(name, data, sections);
    }

    /// <summary>
    /// Heuristic used to detect nested containers: a plausible declared size and a
    /// directory whose entries are monotonically increasing offsets inside the data.
    /// </summary>
    public static bool LooksLikePacket(ReadOnlySpan<byte> span)
    {
        if (span.Length < 8)
            return false;
        int declaredSize = BinaryPrimitives.ReadInt32LittleEndian(span);
        if (declaredSize < 8 || declaredSize > span.Length)
            return false;
        int directorySize = (int)(BinaryPrimitives.ReadUInt32LittleEndian(span[4..]) & OffsetMask);
        if (directorySize < 8 || directorySize > declaredSize || (directorySize & 3) != 0)
            return false;
        int count = directorySize / 4 - 1;
        int previous = directorySize;
        for (int i = 0; i < count; i++)
        {
            int offset = (int)(BinaryPrimitives.ReadUInt32LittleEndian(span[(4 + i * 4)..]) & OffsetMask);
            if (offset < previous || offset > declaredSize)
                return false;
            previous = offset;
        }
        return true;
    }
}
