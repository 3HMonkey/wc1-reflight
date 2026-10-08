using System.Buffers.Binary;
using WingCommander.Core.Resources;

namespace WingCommander.Graphics.Cockpit;

/// <summary>One copy run of a cockpit view mask: <see cref="Length"/> bytes starting at screen
/// (<see cref="DestinationX"/>, <see cref="ScreenY"/>); a run may continue into following rows.</summary>
public readonly record struct ViewRun(short DestinationX, short ScreenY, ushort Length);

/// <summary>
/// Geometry of one space view: the size of the off-screen space buffer, where its top-left
/// lands on the screen, and the run list of screen pixels not covered by the cockpit art.
/// Record layout: <c>i16 width, height, originX, originY</c>, then runs of
/// <c>(i16 destX, i16 screenY, u16 length)</c> terminated by <c>destX == -1</c>.
/// </summary>
/// <remarks>C: ScreenViewportGeometry (include/wcdata.h); PCSHIP section 6 records and the two
/// built-ins aScreenViewportGeometry[4]/[5] (0x0046dab8).</remarks>
public sealed class ViewGeometry
{
    private readonly ViewRun[] _runs;

    public ViewGeometry(short width, short height, short originX, short originY, ViewRun[] runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        Width = width;
        Height = height;
        OriginX = originX;
        OriginY = originY;
        _runs = runs;
    }

    /// <summary>Space buffer width.</summary>
    public short Width { get; }

    /// <summary>Space buffer height.</summary>
    public short Height { get; }

    /// <summary>Screen x of space buffer column 0.</summary>
    public short OriginX { get; }

    /// <summary>Screen y of space buffer row 0.</summary>
    public short OriginY { get; }

    public ReadOnlySpan<ViewRun> Runs => _runs;

    /// <summary>View mode 4: a 320x128 buffer shown at screen rows 24..151 (one run).</summary>
    /// <remarks>C: aScreenViewportGeometry[4] = {320, 128, 0, 24, (0, 24, 40960), -1}.</remarks>
    public static ViewGeometry Mode4 { get; } = new(320, 128, 0, 24, [new ViewRun(0, 24, 40960)]);

    /// <summary>View mode 5: a full-screen 320x200 buffer (one run).</summary>
    /// <remarks>C: aScreenViewportGeometry[5] = {320, 200, 0, 0, (0, 0, 64000), -1}.</remarks>
    public static ViewGeometry Mode5 { get; } = new(320, 200, 0, 0, [new ViewRun(0, 0, 64000)]);

    /// <summary>Parses one record at <paramref name="offset"/>.</summary>
    public static ViewGeometry Parse(ReadOnlySpan<byte> data, int offset, string name)
    {
        if (offset < 0 || offset + 10 > data.Length)
            throw new GameDataException($"{name}: view geometry offset {offset} is outside the section.");
        short width = BinaryPrimitives.ReadInt16LittleEndian(data[offset..]);
        short height = BinaryPrimitives.ReadInt16LittleEndian(data[(offset + 2)..]);
        short originX = BinaryPrimitives.ReadInt16LittleEndian(data[(offset + 4)..]);
        short originY = BinaryPrimitives.ReadInt16LittleEndian(data[(offset + 6)..]);
        int p = offset + 8;
        int count = 0;
        while (true)
        {
            if (p + 2 > data.Length)
                throw new GameDataException($"{name}: view geometry run list at {offset} is not terminated.");
            if (BinaryPrimitives.ReadInt16LittleEndian(data[p..]) == -1)
                break;
            if (p + 6 > data.Length)
                throw new GameDataException($"{name}: view geometry run list at {offset} is truncated.");
            p += 6;
            count++;
        }
        var runs = new ViewRun[count];
        p = offset + 8;
        for (int i = 0; i < count; i++, p += 6)
        {
            runs[i] = new ViewRun(
                BinaryPrimitives.ReadInt16LittleEndian(data[p..]),
                BinaryPrimitives.ReadInt16LittleEndian(data[(p + 2)..]),
                BinaryPrimitives.ReadUInt16LittleEndian(data[(p + 4)..]));
        }
        return new ViewGeometry(width, height, originX, originY, runs);
    }
}
