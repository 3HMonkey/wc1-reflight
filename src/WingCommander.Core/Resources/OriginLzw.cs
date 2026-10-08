using System.Buffers;

namespace WingCommander.Core.Resources;

/// <summary>
/// Origin's LZW variant used by DOS Wing Commander packet sections (compression flag 1).
/// Codes are read LSB-first, 9 bits wide growing to 12; 0x100 = clear, 0x101 = stop,
/// first dictionary code = 0x102. The code width grows when the dictionary size reaches
/// the current width's capacity. A compressed section is prefixed by a little-endian
/// u32 with the uncompressed size (handled by <see cref="PacketFile"/>, not here).
/// </summary>
/// <remarks>C: SdlDecompressOriginLzw (src/sdl/resources.c).</remarks>
public static class OriginLzw
{
    private const int ClearCode = 0x100;
    private const int StopCode = 0x101;
    private const int FirstCode = 0x102;
    private const int MaxCodes = 0x1000;
    private const int MaxWidth = 12;

    /// <summary>
    /// Decompresses <paramref name="source"/> into <paramref name="destination"/>.
    /// Returns the number of bytes written; throws when the stream is malformed or does
    /// not fill the destination exactly.
    /// </summary>
    public static int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        ushort[] prefix = ArrayPool<ushort>.Shared.Rent(MaxCodes);
        byte[] value = ArrayPool<byte>.Shared.Rent(MaxCodes);
        byte[] reverse = ArrayPool<byte>.Shared.Rent(MaxCodes);
        try
        {
            return DecompressCore(source, destination, prefix, value, reverse);
        }
        finally
        {
            ArrayPool<ushort>.Shared.Return(prefix);
            ArrayPool<byte>.Shared.Return(value);
            ArrayPool<byte>.Shared.Return(reverse);
        }
    }

    private static int DecompressCore(ReadOnlySpan<byte> source, Span<byte> destination,
        ushort[] prefix, byte[] value, byte[] reverse)
    {
        var reader = new BitReader(source);
        int outPos = 0;

        int width = 9;
        int code = reader.Read(width);
        if (code < 0)
            throw new GameDataException("LZW: stream is empty.");
        if (code == StopCode)
        {
            if (destination.Length != 0)
                throw new GameDataException("LZW: stream stopped before producing any data.");
            return 0;
        }
        if (code != ClearCode)
            throw new GameDataException("LZW: stream does not start with a clear code.");

        for (;;)
        {
            width = 9;
            int widthThreshold = 1 << width;
            int dictSize = FirstCode;
            int previous = ClearCode;

            for (;;)
            {
                code = reader.Read(width);
                if (code < 0)
                    throw new GameDataException("LZW: unexpected end of compressed data.");
                if (code == StopCode)
                {
                    if (outPos != destination.Length)
                        throw new GameDataException(
                            $"LZW: produced {outPos} bytes, expected {destination.Length}.");
                    return outPos;
                }
                if (code == ClearCode)
                    break;
                if (code > dictSize)
                    throw new GameDataException($"LZW: code {code} exceeds dictionary size {dictSize}.");

                bool special = code == dictSize;
                int decoded = special ? previous : code;
                if (decoded == ClearCode)
                    throw new GameDataException("LZW: KwKwK case without previous code.");

                byte first = WriteCode(prefix, value, reverse, decoded, destination, ref outPos);

                if (previous != ClearCode)
                {
                    if (dictSize >= MaxCodes)
                        throw new GameDataException("LZW: dictionary overflow.");
                    prefix[dictSize] = (ushort)previous;
                    value[dictSize] = first;
                    dictSize++;
                    if (special)
                    {
                        if (outPos >= destination.Length)
                            throw new GameDataException("LZW: output overflow.");
                        destination[outPos++] = first;
                    }
                    if (dictSize == widthThreshold && width < MaxWidth)
                    {
                        width++;
                        widthThreshold <<= 1;
                    }
                }
                previous = code;
            }
        }
    }

    private static byte WriteCode(ushort[] prefix, byte[] value, byte[] reverse, int code,
        Span<byte> destination, ref int outPos)
    {
        int reverseSize = 0;
        while (code >= FirstCode)
        {
            if (code >= MaxCodes || reverseSize >= MaxCodes)
                throw new GameDataException("LZW: corrupt dictionary chain.");
            reverse[reverseSize++] = value[code];
            code = prefix[code];
        }
        if (code > 0xff || outPos >= destination.Length)
            throw new GameDataException("LZW: output overflow.");
        byte first = (byte)code;
        destination[outPos++] = first;
        while (reverseSize != 0)
        {
            if (outPos >= destination.Length)
                throw new GameDataException("LZW: output overflow.");
            destination[outPos++] = reverse[--reverseSize];
        }
        return first;
    }

    private ref struct BitReader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private long _bitPosition;

        /// <summary>Reads <paramref name="width"/> bits LSB-first; returns -1 at end of data.</summary>
        public int Read(int width)
        {
            int result = 0;
            int bitsRead = 0;
            while (bitsRead < width)
            {
                long bytePos = _bitPosition >> 3;
                if (bytePos >= _bytes.Length)
                    return -1;
                int shift = (int)(_bitPosition & 7);
                int available = 8 - shift;
                int needed = width - bitsRead;
                int take = available < needed ? available : needed;
                int mask = (1 << take) - 1;
                result |= ((_bytes[(int)bytePos] >> shift) & mask) << bitsRead;
                _bitPosition += take;
                bitsRead += take;
            }
            return result;
        }
    }
}
