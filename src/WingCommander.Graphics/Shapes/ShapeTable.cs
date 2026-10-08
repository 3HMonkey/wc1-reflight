using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using WingCommander.Core.Resources;

namespace WingCommander.Graphics.Shapes;

/// <summary>
/// A "shape" is a raw graphics section that is itself a packet container whose sections
/// are animation/view frames. Each frame starts with four little-endian i16 extents
/// (right, left, top, bottom) followed at +8 by the RLE row stream (see
/// <see cref="ShapeFrameDecoder"/>). The object replaces the original shape pointer; the
/// prepared ("1.00") draw form of every frame is built lazily and cached here, so it is
/// released together with the shape (the original leaked it).
/// </summary>
/// <remarks>C: GetShapeFrameCount (0x4408D0) = (u16 at +4 &gt;&gt; 2) - 1; frame f at the u32
/// offset at +4+4f; PrepareShapeRLEData (0x440D50) / GetPreparedShapeData (0x4408C0).</remarks>
public sealed class ShapeTable
{
    private readonly PacketFile _packet;
    private readonly PreparedFrame?[] _prepared;

    private ShapeTable(PacketFile packet, ReadOnlyMemory<byte> data)
    {
        _packet = packet;
        Data = data;
        _prepared = new PreparedFrame?[packet.SectionCount];
    }

    /// <summary>The whole section bytes; frame offsets are relative to this.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Number of frames.</summary>
    /// <remarks>C: GetShapeFrameCount (0x4408D0, killbrd.c).</remarks>
    public int FrameCount => _packet.SectionCount;

    public string Name => _packet.Name;

    public static ShapeTable Parse(string name, ReadOnlyMemory<byte> sectionBytes) =>
        new(PacketFile.Parse(name, sectionBytes), sectionBytes);

    /// <summary>
    /// Parses a section as a shape table when it looks like one (a nested packet whose frames
    /// all carry an 8-byte extents header); returns false for raw tables and empty sections.
    /// </summary>
    public static bool TryParse(string name, ReadOnlyMemory<byte> sectionBytes, [NotNullWhen(true)] out ShapeTable? shape)
    {
        shape = null;
        if (!PacketFile.LooksLikePacket(sectionBytes.Span))
            return false;
        ShapeTable candidate;
        try
        {
            candidate = Parse(name, sectionBytes);
        }
        catch (GameDataException)
        {
            return false;
        }
        for (int f = 0; f < candidate.FrameCount; f++)
        {
            if (candidate._packet.GetInfo(f).StoredSize < 10)
                return false;
        }
        shape = candidate;
        return true;
    }

    /// <summary>Opens section <paramref name="section"/> of <paramref name="packet"/> as a shape table.</summary>
    public static ShapeTable FromSection(PacketFile packet, int section)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Parse($"{packet.Name}[{section}]", packet.GetSection(section));
    }

    /// <summary>True when <paramref name="frame"/> is a valid frame index.</summary>
    /// <remarks>C: the <c>frame*4+4 &lt; *(u16*)(shape+4)</c> test used by every helper.</remarks>
    public bool HasFrame(int frame) => (uint)frame < (uint)FrameCount;

    public ShapeExtents GetExtents(int frame)
    {
        ReadOnlySpan<byte> f = GetFrameBytes(frame);
        if (f.Length < 8)
            throw new GameDataException($"{Name}: frame {frame} is shorter than its 8-byte header.");
        return new ShapeExtents(
            BinaryPrimitives.ReadInt16LittleEndian(f),
            BinaryPrimitives.ReadInt16LittleEndian(f[2..]),
            BinaryPrimitives.ReadInt16LittleEndian(f[4..]),
            BinaryPrimitives.ReadInt16LittleEndian(f[6..]));
    }

    /// <summary>Whole frame record including the 8-byte extents header.</summary>
    public ReadOnlyMemory<byte> GetFrame(int frame) => _packet.GetStored(frame);

    /// <summary>The RLE command stream of a frame (bytes after the extents header).</summary>
    public ReadOnlyMemory<byte> GetFramePixels(int frame)
    {
        var whole = GetFrame(frame);
        return whole.Length >= 8 ? whole[8..] : ReadOnlyMemory<byte>.Empty;
    }

    /// <summary>True when the frame has no data (flag 0xFF / zero-length section).</summary>
    public bool IsFrameEmpty(int frame) => _packet.GetInfo(frame).StoredSize == 0;

    /// <summary>
    /// The prepared draw form of a frame, built on first use (decode into a 0xFF-filled bitmap,
    /// re-encode as skip/literal runs). Pixels with value 0xFF inside a span become
    /// transparent, exactly as in the original.
    /// </summary>
    /// <remarks>C: PrepareShapeRLEData (0x440D50, gr.c) + GetPreparedShapeData (0x4408C0, killbrd.c).</remarks>
    public PreparedFrame GetPreparedFrame(int frame)
    {
        if (!HasFrame(frame))
            throw new ArgumentOutOfRangeException(nameof(frame), $"{Name}: frame {frame} of {FrameCount}");
        return _prepared[frame] ??= PreparedFrame.Prepare(this, frame);
    }

    /// <summary>Builds the prepared form of every frame now (the original prepared all frames at once).</summary>
    /// <remarks>C: PrepareShapeRLEData (0x440D50, gr.c).</remarks>
    public void PrepareShapeRLEData()
    {
        for (int f = 0; f < FrameCount; f++)
            GetPreparedFrame(f);
    }

    private ReadOnlySpan<byte> GetFrameBytes(int frame) => _packet.GetStored(frame).Span;
}
