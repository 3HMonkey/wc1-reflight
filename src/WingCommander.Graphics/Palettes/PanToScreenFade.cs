namespace WingCommander.Graphics.Palettes;

/// <summary>
/// The palette half of "pan to screen" (fade a new picture in): at the start every non-black
/// entry is set to one flat colour; each <see cref="Step"/> moves the entries back toward their
/// original colours by 4 and writes them through <see cref="GamePalette.CachePaletteEntry"/>
/// (so the saved mirror follows). Like the original, entries end up to 3 short of their original
/// values. Created by <c>GraphicsContext.BeginPanToScreen</c>, which also copies the picture.
/// </summary>
/// <remarks>C: the transition loop of PanToScreen (0x439430, screens.c):
/// <c>while (StepPaletteTransition(transition, original)) { cache entries; wait vertical blank;
/// DIBramPalette(); }</c>; the vertical-blank waits are the caller's.</remarks>
public sealed class PanToScreenFade
{
    private readonly GamePalette _palette;
    private readonly PaletteTransition _transition = new();
    private readonly byte[] _indices;
    private readonly short[] _transitionColours;
    private readonly short[] _original;

    private PanToScreenFade(GamePalette palette, byte[] indices, short[] transitionColours, short[] original)
    {
        _palette = palette;
        _indices = indices;
        _transitionColours = transitionColours;
        _original = original;
    }

    /// <summary>Number of palette entries being faded.</summary>
    public int ActiveCount => _indices.Length;

    /// <summary>True once <see cref="Step"/> has returned false.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>
    /// Records the active entries and sets all of them to the RGB of palette entry
    /// <paramref name="flatColourIndex"/> (written immediately, saved mirror included).
    /// </summary>
    /// <remarks>C: the set-up part of PanToScreen (0x439430, screens.c).</remarks>
    public static PanToScreenFade Begin(GamePalette palette, int flatColourIndex)
    {
        ArgumentNullException.ThrowIfNull(palette);
        Span<byte> active = stackalloc byte[256];
        int count = palette.CollectActivePaletteIndices(active);
        var indices = active[..count].ToArray();
        var original = new short[count * 3];
        var transition = new short[count * 3];
        var (tr, tg, tb) = palette.GetPaletteEntry(flatColourIndex & 0xFF);
        for (int i = 0; i < count; i++)
        {
            var (r, g, b) = palette.GetPaletteEntry(indices[i]);
            original[i * 3] = r;
            original[i * 3 + 1] = g;
            original[i * 3 + 2] = b;
            palette.CachePaletteEntry(indices[i], tr, tg, tb);
            transition[i * 3] = tr;
            transition[i * 3 + 1] = tg;
            transition[i * 3 + 2] = tb;
        }
        return new PanToScreenFade(palette, indices, transition, original);
    }

    /// <summary>
    /// Applies the next step and returns true, or returns false (changing nothing) when the fade
    /// is complete. Await one vertical blank between calls.
    /// </summary>
    public bool Step()
    {
        if (IsFinished)
            return false;
        if (!_transition.Step(_transitionColours, _original))
        {
            IsFinished = true;
            return false;
        }
        for (int i = 0; i < _indices.Length; i++)
        {
            _palette.CachePaletteEntry(_indices[i], _transitionColours[i * 3], _transitionColours[i * 3 + 1],
                _transitionColours[i * 3 + 2]);
        }
        return true;
    }
}
