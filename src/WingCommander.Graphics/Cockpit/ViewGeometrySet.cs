using System.Buffers.Binary;
using WingCommander.Core.Resources;

namespace WingCommander.Graphics.Cockpit;

/// <summary>
/// Section 6 of a PCSHIP.Vnn file: <c>u16 bufferSize</c> (the largest space buffer, e.g.
/// 0xBB80 = 320x150; not a count) followed by <c>i16 offset[n]</c> to the view geometries of
/// cockpit views 0..3. PCSHIP.V04 has a single geometry (its first offset is 4); the number of
/// offsets is therefore <c>(offset[0] - 2) / 2</c>, at most 4.
/// </summary>
/// <remarks>C: ScreenViewportPacket (include/wcdata.h), selected by set_up_screen_viewport
/// (0x436740, eventmgr.c) through <c>geometryOffsets[mode]</c>.</remarks>
public sealed class ViewGeometrySet
{
    /// <summary>Section index of the view geometries inside PCSHIP.Vnn.</summary>
    public const int PcShipSection = 6;

    private readonly ViewGeometry[] _views;

    private ViewGeometrySet(ushort bufferSize, ViewGeometry[] views)
    {
        BufferSize = bufferSize;
        _views = views;
    }

    /// <summary>Size in bytes of the largest space buffer of this cockpit.</summary>
    public ushort BufferSize { get; }

    /// <summary>Number of view geometries present.</summary>
    public int Count => _views.Length;

    /// <summary>Geometry of cockpit view <paramref name="view"/> (0..Count-1).</summary>
    public ViewGeometry this[int view] => _views[view];

    /// <summary>Parses a PCSHIP section 6.</summary>
    public static ViewGeometrySet Parse(string name, ReadOnlySpan<byte> section)
    {
        if (section.Length < 4)
            throw new GameDataException($"{name}: view geometry section is too short.");
        ushort bufferSize = BinaryPrimitives.ReadUInt16LittleEndian(section);
        int first = BinaryPrimitives.ReadInt16LittleEndian(section[2..]);
        int count = Math.Clamp((first - 2) / 2, 1, 4);
        var views = new ViewGeometry[count];
        for (int view = 0; view < count; view++)
        {
            int offset = BinaryPrimitives.ReadInt16LittleEndian(section[(2 + view * 2)..]);
            views[view] = ViewGeometry.Parse(section, offset, $"{name} view {view}");
        }
        return new ViewGeometrySet(bufferSize, views);
    }
}
