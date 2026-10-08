using System.Buffers.Binary;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Graphics.Tests.TestSupport;

/// <summary>Builds shape tables in the on-disk format for synthetic tests.</summary>
internal static class TestShapes
{
    /// <summary>
    /// Encodes a bitmap (0xFF = transparent) as a raw frame record: extents header, one literal
    /// span (even rowCode) per horizontal run of opaque pixels, terminator.
    /// </summary>
    public static byte[] Frame(int width, int height, int leftExtent, int topExtent, byte[] pixels, bool useSubRuns = false)
    {
        var bytes = new List<byte>();
        AddInt16(bytes, (short)(width - leftExtent - 1));
        AddInt16(bytes, (short)leftExtent);
        AddInt16(bytes, (short)topExtent);
        AddInt16(bytes, (short)(height - topExtent - 1));
        for (int y = 0; y < height; y++)
        {
            int x = 0;
            while (x < width)
            {
                if (pixels[y * width + x] == 0xFF)
                {
                    x++;
                    continue;
                }
                int start = x;
                while (x < width && pixels[y * width + x] != 0xFF)
                    x++;
                int n = x - start;
                if (useSubRuns)
                {
                    AddUInt16(bytes, (ushort)((n << 1) | 1));
                    AddInt16(bytes, (short)(start - leftExtent));
                    AddInt16(bytes, (short)(y - topExtent));
                    // alternate fill (when a pixel repeats) and literal sub-runs
                    int i = start;
                    while (i < x)
                    {
                        int repeat = 1;
                        while (i + repeat < x && pixels[y * width + i + repeat] == pixels[y * width + i] && repeat < 127)
                            repeat++;
                        if (repeat >= 2)
                        {
                            bytes.Add((byte)((repeat << 1) | 1));
                            bytes.Add(pixels[y * width + i]);
                            i += repeat;
                        }
                        else
                        {
                            bytes.Add(1 << 1);
                            bytes.Add(pixels[y * width + i]);
                            i++;
                        }
                    }
                }
                else
                {
                    AddUInt16(bytes, (ushort)(n << 1));
                    AddInt16(bytes, (short)(start - leftExtent));
                    AddInt16(bytes, (short)(y - topExtent));
                    for (int i = start; i < x; i++)
                        bytes.Add(pixels[y * width + i]);
                }
            }
        }
        AddUInt16(bytes, 0);
        return [.. bytes];
    }

    /// <summary>Wraps frame records in a nested packet (the shape table container).</summary>
    public static ShapeTable Table(params byte[][] frames)
    {
        int directory = 4 + 4 * frames.Length;
        int size = directory + frames.Sum(f => f.Length);
        var data = new byte[size];
        BinaryPrimitives.WriteInt32LittleEndian(data, size);
        int offset = directory;
        for (int i = 0; i < frames.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4 + 4 * i), offset);
            frames[i].CopyTo(data, offset);
            offset += frames[i].Length;
        }
        return ShapeTable.Parse("test", data);
    }

    private static void AddInt16(List<byte> bytes, short value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
    }

    private static void AddUInt16(List<byte> bytes, ushort value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
    }
}
