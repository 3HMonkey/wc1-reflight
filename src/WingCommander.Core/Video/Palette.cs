namespace WingCommander.Core.Video;

/// <summary>
/// The 256-entry presentation palette with 8-bit components. The game manipulates
/// individual entries at runtime (damage flash, fades, cockpit lights), so the table is
/// mutable; <see cref="Version"/> changes on every write so hosts can skip re-uploads.
/// VGA data uses 6-bit components; <see cref="SetEntry6"/> expands them the way the VGA
/// DAC displays them (63 maps to 255).
/// </summary>
public sealed class Palette
{
    public const int EntryCount = 256;

    private readonly byte[] _rgb = new byte[EntryCount * 3];
    private readonly uint[] _argb = new uint[EntryCount];
    private bool _dirty = true;

    /// <summary>Raw 8-bit RGB triplets, 768 bytes.</summary>
    public ReadOnlySpan<byte> Rgb => _rgb;

    /// <summary>Packed 0xAARRGGBB (alpha 0xFF).</summary>
    public ReadOnlySpan<uint> Argb
    {
        get
        {
            if (_dirty)
                Rebuild();
            return _argb;
        }
    }

    /// <summary>Changes whenever an entry is written.</summary>
    public int Version { get; private set; }

    /// <summary>Expands a 6-bit VGA DAC component to 8 bits (0..63 to 0..255).</summary>
    public static byte Expand6(int v6)
    {
        int v = v6 & 0x3f;
        return (byte)((v << 2) | (v >> 4));
    }

    public void SetEntry(int index, byte r, byte g, byte b)
    {
        int o = index * 3;
        _rgb[o] = r;
        _rgb[o + 1] = g;
        _rgb[o + 2] = b;
        Touch();
    }

    public void SetEntry6(int index, int r6, int g6, int b6) =>
        SetEntry(index, Expand6(r6), Expand6(g6), Expand6(b6));

    public (byte R, byte G, byte B) GetEntry(int index)
    {
        int o = index * 3;
        return (_rgb[o], _rgb[o + 1], _rgb[o + 2]);
    }

    /// <summary>Loads <paramref name="count"/> 8-bit RGB triplets starting at <paramref name="firstIndex"/>.</summary>
    public void SetRange(int firstIndex, ReadOnlySpan<byte> rgbTriplets, int count)
    {
        rgbTriplets[..(count * 3)].CopyTo(_rgb.AsSpan(firstIndex * 3, count * 3));
        Touch();
    }

    /// <summary>Loads <paramref name="count"/> 6-bit RGB triplets starting at <paramref name="firstIndex"/>.</summary>
    public void SetRange6(int firstIndex, ReadOnlySpan<byte> rgb6Triplets, int count)
    {
        for (int i = 0; i < count * 3; i++)
            _rgb[firstIndex * 3 + i] = Expand6(rgb6Triplets[i]);
        Touch();
    }

    public void Fill(byte r, byte g, byte b)
    {
        for (int i = 0; i < EntryCount; i++)
        {
            int o = i * 3;
            _rgb[o] = r;
            _rgb[o + 1] = g;
            _rgb[o + 2] = b;
        }
        Touch();
    }

    public void CopyFrom(Palette other)
    {
        other._rgb.CopyTo(_rgb.AsSpan());
        Touch();
    }

    private void Touch()
    {
        _dirty = true;
        Version++;
    }

    private void Rebuild()
    {
        for (int i = 0; i < EntryCount; i++)
        {
            int o = i * 3;
            _argb[i] = 0xff000000u | ((uint)_rgb[o] << 16) | ((uint)_rgb[o + 1] << 8) | _rgb[o + 2];
        }
        _dirty = false;
    }
}
