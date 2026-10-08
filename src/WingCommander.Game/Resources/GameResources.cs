using WingCommander.Core.Resources;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Game.Resources;

/// <summary>
/// Packet and shape cache over the game directory, addressed by code logical file number
/// (<see cref="LogicalFile"/>). Replaces the original's handle-based loader: packets are read
/// once and kept, shapes keep their prepared frames, so "release" is unnecessary.
/// </summary>
/// <remarks>C: FetchDiskPacketRetrying (0x41D9A0), LoadPacketAllocated, ReleasePacketHandle (disk.c).</remarks>
public sealed class GameResources(GameDirectory directory)
{
    private readonly Dictionary<int, PacketFile> _packets = [];
    private readonly Dictionary<(int File, int Section), ShapeTable> _shapes = [];

    public GameDirectory Directory { get; } = directory;

    /// <summary>The packet of a logical file (cached).</summary>
    public PacketFile GetPacket(int logicalFile)
    {
        if (!_packets.TryGetValue(logicalFile, out var packet))
            _packets[logicalFile] = packet = Directory.OpenPacket(logicalFile);
        return packet;
    }

    /// <summary>The decoded bytes of a section.</summary>
    public ReadOnlyMemory<byte> GetSection(int logicalFile, int section) => GetPacket(logicalFile).GetSection(section);

    /// <summary>A section opened as a shape table (cached with its prepared frames).</summary>
    public ShapeTable GetShape(int logicalFile, int section)
    {
        if (!_shapes.TryGetValue((logicalFile, section), out var shape))
            _shapes[(logicalFile, section)] = shape = ShapeTable.FromSection(GetPacket(logicalFile), section);
        return shape;
    }
}
