using WingCommander.Core.Video;

namespace WingCommander.Graphics.Palettes;

/// <summary>
/// The game's palette state on top of the live <see cref="Palette"/>: the "saved game palette"
/// mirror that every single-entry write also updates (but whole-palette writes do not) and the
/// whole-palette scratch used by <see cref="FadeToColour"/>. Components are 8-bit (Kilrathi Saga
/// behaviour: GAME.PAL CMAP values are used unshifted). Every write is visible immediately; the
/// vertical-blank waits that the original performed before whole-palette uploads belong to the
/// caller (ADR-009: the Game awaits them on its scheduler).
/// </summary>
/// <remarks>C: abDIBPaletteCache (live), awPaletteRgbWords (saved mirror), abPaletteTriplets
/// (scratch) and the DIB palette functions of dib.c.</remarks>
public sealed class GamePalette
{
    private readonly byte[] _saved = new byte[Palette.EntryCount * 3];
    private readonly byte[] _triplets = new byte[Palette.EntryCount * 3];

    public GamePalette(Palette live)
    {
        ArgumentNullException.ThrowIfNull(live);
        Live = live;
    }

    /// <summary>The palette that is presented.</summary>
    public Palette Live { get; }

    /// <summary>The "saved game palette" (R,G,B per entry).</summary>
    /// <remarks>C: awPaletteRgbWords[0x300] (low bytes).</remarks>
    public ReadOnlySpan<byte> Saved => _saved;

    /// <summary>
    /// Whole-palette scratch of <see cref="FadeToColour"/>: a fade writes only the active
    /// entries, the rest keeps values from earlier fades and is uploaded as well (original
    /// behaviour).
    /// </summary>
    /// <remarks>C: abPaletteTriplets[256][3].</remarks>
    public Span<byte> Triplets => _triplets;

    /// <summary>
    /// Sets one entry when it differs from the live value (each component's low byte is stored,
    /// the comparison uses the full short like the original) and mirrors it into
    /// <see cref="Saved"/>.
    /// </summary>
    /// <remarks>C: SetPaletteEntry (0x4413E0, gr.c) -> DIBsetPalette (0x432F10, dib.c).</remarks>
    public void SetPaletteEntry(int index, short red, short green, short blue)
    {
        var (r, g, b) = Live.GetEntry(index);
        if (r == red && g == green && b == blue)
            return;
        CachePaletteEntry(index, red, green, blue);
    }

    /// <summary>Span overload of <see cref="SetPaletteEntry(int, short, short, short)"/> (R, G, B).</summary>
    /// <remarks>C: SetPaletteEntry (0x4413E0, gr.c).</remarks>
    public void SetPaletteEntry(int index, ReadOnlySpan<short> rgb) => SetPaletteEntry(index, rgb[0], rgb[1], rgb[2]);

    /// <summary>Reads one live entry.</summary>
    /// <remarks>C: GetPaletteEntry (0x4413C0, gr.c) -> GetPaletteEntryAsWords (0x433020, dib.c).</remarks>
    public (byte R, byte G, byte B) GetPaletteEntry(int index) => Live.GetEntry(index);

    /// <summary>Writes one entry unconditionally into the live palette and the saved mirror.</summary>
    /// <remarks>C: CachePaletteEntryFromWords (0x432E30, dib.c).</remarks>
    public void CachePaletteEntry(int index, short red, short green, short blue)
    {
        byte nr = unchecked((byte)red), ng = unchecked((byte)green), nb = unchecked((byte)blue);
        Live.SetEntry(index, nr, ng, nb);
        _saved[index * 3] = nr;
        _saved[index * 3 + 1] = ng;
        _saved[index * 3 + 2] = nb;
    }

    /// <summary>
    /// Replaces all 256 live entries; the saved mirror is not touched. The original waited for
    /// the vertical blank before the upload; callers that need that timing await it first.
    /// </summary>
    /// <remarks>C: SetWholePaletteFromTriplets (0x434FD0, mathfp.c) -> DIBwholePaletteFromTriplets (0x433060, dib.c).</remarks>
    public void SetWholePaletteFromTriplets(ReadOnlySpan<byte> rgb)
    {
        if (rgb.Length < Palette.EntryCount * 3)
            throw new ArgumentException("A whole palette needs 768 bytes.", nameof(rgb));
        Live.SetRange(0, rgb, Palette.EntryCount);
    }

    /// <summary>Copies the live palette into the saved mirror.</summary>
    /// <remarks>C: SaveGamePalette (0x401000, winmain.c).</remarks>
    public void SaveGamePalette() => Live.Rgb.CopyTo(_saved);

    /// <summary>
    /// Reloads the saved mirror into the live palette. The original waited for two vertical
    /// blanks first (WaitForVerticalBlankThunk plus the wait inside the whole-palette upload);
    /// callers await them.
    /// </summary>
    /// <remarks>C: RestoreGamePalette (0x401020, winmain.c) -> DIBwholePaletteFromWords (0x433120, dib.c).</remarks>
    public void RestoreGamePalette() => Live.SetRange(0, _saved, Palette.EntryCount);

    /// <summary>
    /// Sets <c>active[i] = 1</c> for every live entry that is not black. The viewport argument
    /// of the original is ignored there too (the DOS version scanned pixels).
    /// </summary>
    /// <remarks>C: MarkActivePaletteEntries (0x441370, gr.c).</remarks>
    public void MarkActivePaletteEntries(Span<byte> active)
    {
        ReadOnlySpan<byte> rgb = Live.Rgb;
        for (int i = 0; i < Palette.EntryCount && i < active.Length; i++)
        {
            if (rgb[i * 3] != 0 || rgb[i * 3 + 1] != 0 || rgb[i * 3 + 2] != 0)
                active[i] = 1;
        }
    }

    /// <summary>Lists the indices of all non-black entries (ascending); returns the count.</summary>
    /// <remarks>C: CollectActivePaletteIndices (0x418140, geom.c).</remarks>
    public int CollectActivePaletteIndices(Span<byte> indices)
    {
        Span<byte> active = stackalloc byte[Palette.EntryCount];
        active.Clear();
        MarkActivePaletteEntries(active);
        int count = 0;
        int capacity = Math.Min(indices.Length, Palette.EntryCount);
        for (int i = 0; i < capacity; i++)
        {
            if (active[i] != 0)
                indices[count++] = (byte)i;
        }
        return count;
    }

    /// <summary>Loads GAME.PAL (768 RGB bytes at file offset 0x30) as the whole palette.</summary>
    /// <remarks>C: LoadPaletteTripletsFile (0x404610, cmpgn.c).</remarks>
    public void LoadPaletteTripletsFile(ReadOnlySpan<byte> gamePalFile)
    {
        Span<byte> rgb = stackalloc byte[Palette.EntryCount * 3];
        GamePaletteFile.ReadTriplets(gamePalFile, rgb);
        SetWholePaletteFromTriplets(rgb);
    }
}
