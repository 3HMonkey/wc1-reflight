using System.Buffers.Binary;
using WingCommander.Core.Resources;

namespace WingCommander.Graphics.Shapes;

/// <summary>
/// Bounds-checked cursor over the raw row stream of a shape frame (the bytes after the
/// 8-byte extents header). Layout of one span: <c>u16 rowCode</c> (0 ends the frame),
/// <c>i16 dx</c>, <c>i16 dy</c>; odd rowCode = <c>rowCode &gt;&gt; 1</c> pixels encoded as
/// sub-runs (<c>u8 c</c>: odd = fill <c>c &gt;&gt; 1</c> times with the next byte, even = copy
/// <c>c &gt;&gt; 1</c> literal bytes), even rowCode = <c>rowCode &gt;&gt; 1</c> literal bytes.
/// </summary>
internal ref struct RowStreamReader
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly string _name;

    public RowStreamReader(ReadOnlySpan<byte> rowStream, string name)
    {
        _data = rowStream;
        _name = name;
        Position = 0;
    }

    public int Position { get; private set; }

    public ushort ReadUInt16()
    {
        Require(2);
        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_data[Position..]);
        Position += 2;
        return v;
    }

    public short ReadInt16()
    {
        Require(2);
        short v = BinaryPrimitives.ReadInt16LittleEndian(_data[Position..]);
        Position += 2;
        return v;
    }

    public byte ReadByte()
    {
        Require(1);
        return _data[Position++];
    }

    /// <summary>Returns the next <paramref name="count"/> bytes and advances past them.</summary>
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        Require(count);
        ReadOnlySpan<byte> bytes = _data.Slice(Position, count);
        Position += count;
        return bytes;
    }

    private readonly void Require(int count)
    {
        if (count < 0 || Position + count > _data.Length)
            throw new GameDataException($"{_name}: shape row stream is truncated at offset {Position}.");
    }
}
