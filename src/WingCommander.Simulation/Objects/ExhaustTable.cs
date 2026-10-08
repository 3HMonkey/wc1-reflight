using System.Buffers.Binary;

namespace WingCommander.Simulation.Objects;

/// <summary>
/// The engine flame table of a ship or missile type (section 2 of its ship file, loaded by
/// <c>load_ship</c> into <c>aObjectTypeData[type].animation</c>): one little-endian short per
/// sprite view frame giving the byte offset of a list of 6-short records
/// <c>{frame, scale, distance, angle, x, y}</c> terminated by a record whose first short is -1;
/// an offset of -1 means no flames for that view.
/// </summary>
/// <remarks>C: the packet walked by place_exhaust_on_ships (0x421430, logic.c). Reads outside the
/// section return -1 (the original read past the packet), so every walk terminates.</remarks>
public sealed class ExhaustTable
{
    /// <summary>Size of one flame record in bytes.</summary>
    public const int RecordSize = 12;

    private readonly byte[] _data;

    public ExhaustTable(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
    }

    /// <summary>Size of the section in bytes.</summary>
    public int Length => _data.Length;

    /// <summary>The short at <paramref name="byteOffset"/>, or -1 outside the section.</summary>
    public short ReadShort(int byteOffset)
    {
        if (byteOffset < 0 || byteOffset > _data.Length - 2)
            return -1;
        return BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(byteOffset, 2));
    }

    /// <summary>Byte offset of the flame list of view frame <paramref name="viewFrame"/>, or -1.</summary>
    public short GetListOffset(int viewFrame) => ReadShort(viewFrame * 2);
}
