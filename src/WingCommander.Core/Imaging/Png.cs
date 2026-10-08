using System.Buffers.Binary;
using System.IO.Compression;

namespace WingCommander.Core.Imaging;

/// <summary>
/// Minimal PNG encoder (8-bit indexed with optional transparent index, or 8-bit RGBA),
/// using <see cref="ZLibStream"/> for the IDAT payload and filter type 0 on every row.
/// </summary>
public static class Png
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Writes an indexed image; <paramref name="rgbPalette"/> holds 256 R,G,B triplets.</summary>
    public static void WriteIndexed(string path, int width, int height, ReadOnlySpan<byte> pixels,
        ReadOnlySpan<byte> rgbPalette, int transparentIndex = -1)
    {
        if (pixels.Length < width * height)
            throw new ArgumentException("Pixel buffer too small.", nameof(pixels));
        if (rgbPalette.Length < 768)
            throw new ArgumentException("Palette needs 768 bytes.", nameof(rgbPalette));
        using var file = CreateFile(path);
        file.Write(Signature);
        WriteHeader(file, width, height, colourType: 3);
        WriteChunk(file, "PLTE", rgbPalette[..768]);
        if (transparentIndex is >= 0 and < 256)
        {
            Span<byte> alpha = stackalloc byte[256];
            alpha.Fill(0xFF);
            alpha[transparentIndex] = 0;
            WriteChunk(file, "tRNS", alpha);
        }
        WriteImageData(file, pixels, width, height, bytesPerPixel: 1);
        WriteChunk(file, "IEND", ReadOnlySpan<byte>.Empty);
    }

    /// <summary>Writes a 32-bit RGBA image (4 bytes per pixel, row-major).</summary>
    public static void WriteRgba(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length < width * height * 4)
            throw new ArgumentException("Pixel buffer too small.", nameof(rgba));
        using var file = CreateFile(path);
        file.Write(Signature);
        WriteHeader(file, width, height, colourType: 6);
        WriteImageData(file, rgba, width, height, bytesPerPixel: 4);
        WriteChunk(file, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static FileStream CreateFile(string path)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        return File.Create(path);
    }

    private static void WriteHeader(Stream file, int width, int height, byte colourType)
    {
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;          // bit depth
        header[9] = colourType; // 3 = indexed, 6 = RGBA
        header[10] = 0;         // deflate
        header[11] = 0;         // adaptive filtering
        header[12] = 0;         // no interlace
        WriteChunk(file, "IHDR", header);
    }

    private static void WriteImageData(Stream file, ReadOnlySpan<byte> pixels, int width, int height, int bytesPerPixel)
    {
        int rowBytes = width * bytesPerPixel;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(pixels.Slice(y * rowBytes, rowBytes));
            }
        }
        WriteChunk(file, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
    }

    private static void WriteChunk(Stream file, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
        for (int i = 0; i < 4; i++)
            header[4 + i] = (byte)type[i];
        file.Write(header);
        file.Write(data);
        uint crc = UpdateCrc(0xFFFFFFFFu, header[4..]);
        crc = UpdateCrc(crc, data) ^ 0xFFFFFFFFu;
        Span<byte> trailer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(trailer, crc);
        file.Write(trailer);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
