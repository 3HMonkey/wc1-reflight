namespace WingCommander.Graphics.Palettes;

/// <summary>
/// Step object for "fade the picture to one colour": every non-black palette entry moves toward
/// the colour of one entry in steps of 4. Each <see cref="Step"/> advances the transition, writes
/// the active entries into <see cref="GamePalette.Triplets"/> and uploads that whole scratch
/// palette (inactive entries get whatever earlier fades left there, as in the original). The
/// palette stays faded afterwards; callers clear the screen and restore the game palette.
/// </summary>
/// <remarks>
/// C: FadeViewportPaletteToColour (0x42A700, hudmsg.c). The original looped
/// <c>while (StepPaletteTransition(...)) { write; wait vertical blank; upload; }</c> and then
/// presented (<c>DIBslam(); DIBslamReal()</c>). With ADR-009 the Game drives it:
/// <code>
/// var fade = gfx.BeginFadeViewportPaletteToColour(screen, PaletteColours.Black);
/// while (fade.Step()) await scheduler.VerticalBlank();
/// await PresentScreen();
/// </code>
/// </remarks>
public sealed class FadeToColour
{
    private readonly GamePalette _palette;
    private readonly PaletteTransition _transition = new();
    private readonly byte[] _indices;
    private readonly short[] _current;
    private readonly short[] _target;

    private FadeToColour(GamePalette palette, byte[] indices, short[] current, short[] target)
    {
        _palette = palette;
        _indices = indices;
        _current = current;
        _target = target;
    }

    /// <summary>Number of palette entries being faded (the non-black ones at the start).</summary>
    public int ActiveCount => _indices.Length;

    /// <summary>True once <see cref="Step"/> has returned false.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>Collects the active entries and their colours; nothing is written yet.</summary>
    /// <remarks>C: the set-up part of FadeViewportPaletteToColour (0x42A700, hudmsg.c).</remarks>
    public static FadeToColour Begin(GamePalette palette, int colourIndex)
    {
        ArgumentNullException.ThrowIfNull(palette);
        Span<byte> active = stackalloc byte[256];
        int count = palette.CollectActivePaletteIndices(active);
        var indices = active[..count].ToArray();
        var current = new short[count * 3];
        var target = new short[count * 3];
        var (tr, tg, tb) = palette.GetPaletteEntry(colourIndex & 0xFF);
        for (int i = 0; i < count; i++)
        {
            var (r, g, b) = palette.GetPaletteEntry(indices[i]);
            current[i * 3] = r;
            current[i * 3 + 1] = g;
            current[i * 3 + 2] = b;
            target[i * 3] = tr;
            target[i * 3 + 1] = tg;
            target[i * 3 + 2] = tb;
        }
        return new FadeToColour(palette, indices, current, target);
    }

    /// <summary>
    /// Applies the next step and returns true, or returns false (changing nothing) when the fade
    /// is complete. Await one vertical blank between calls.
    /// </summary>
    /// <remarks>C: one iteration of the fade loop in FadeViewportPaletteToColour (0x42A700).</remarks>
    public bool Step()
    {
        if (IsFinished)
            return false;
        if (!_transition.Step(_current, _target))
        {
            IsFinished = true;
            return false;
        }
        Span<byte> triplets = _palette.Triplets;
        for (int i = 0; i < _indices.Length; i++)
        {
            int o = _indices[i] * 3;
            triplets[o] = unchecked((byte)_current[i * 3]);
            triplets[o + 1] = unchecked((byte)_current[i * 3 + 1]);
            triplets[o + 2] = unchecked((byte)_current[i * 3 + 2]);
        }
        _palette.SetWholePaletteFromTriplets(triplets);
        return true;
    }
}
