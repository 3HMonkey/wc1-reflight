using System.Buffers.Binary;

namespace WingCommander.Tools;

/// <summary>Minimal RIFF/WAVE writer for 16-bit PCM, plus simple level statistics.</summary>
internal static class Wav
{
    /// <summary>Writes interleaved 16-bit PCM.</summary>
    public static void Write(string path, ReadOnlySpan<short> samples, int sampleRate, int channels)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        int dataSize = samples.Length * 2;
        Span<byte> header = stackalloc byte[44];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], 36 + dataSize);
        "WAVE"u8.CopyTo(header[8..]);
        "fmt "u8.CopyTo(header[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], sampleRate * channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], (short)(channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], dataSize);

        using var file = File.Create(path);
        file.Write(header);
        var buffer = new byte[64 * 1024];
        int position = 0;
        while (position < samples.Length)
        {
            int count = Math.Min(buffer.Length / 2, samples.Length - position);
            for (int i = 0; i < count; i++)
                BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(i * 2), samples[position + i]);
            file.Write(buffer, 0, count * 2);
            position += count;
        }
    }

    /// <summary>Peak, RMS (dBFS) and the number of samples at full scale.</summary>
    public static (int Peak, double RmsDb, int ClippedSamples) Measure(ReadOnlySpan<short> samples)
    {
        int peak = 0;
        double sumSquares = 0;
        int clipped = 0;
        foreach (short s in samples)
        {
            int magnitude = Math.Abs((int)s);
            if (magnitude > peak)
                peak = magnitude;
            if (s == short.MaxValue || s == short.MinValue)
                clipped++;
            sumSquares += (double)s * s;
        }
        double rms = samples.Length == 0 ? 0 : Math.Sqrt(sumSquares / samples.Length);
        double rmsDb = rms <= 0 ? double.NegativeInfinity : 20 * Math.Log10(rms / 32768.0);
        return (peak, rmsDb, clipped);
    }
}
