namespace WingCommander.Graphics.Palettes;

/// <summary>
/// Palette flashes during space flight: entry 191 (the space background) turns red when the
/// player is hit and fades back by 4 per rendered frame; entries 185..190 are the cockpit
/// damage-direction lights that the cockpit artwork uses, fading by 4 per housekeeping call.
/// </summary>
/// <remarks>C: asDamageFlashColour[3] and aPaletteFadeEntries[6][3] with the helpers of
/// logic.c and main.c (VGA paths only; the EGA branches of UpdateSpacePaletteFade are dropped).</remarks>
public sealed class FlightPaletteEffects
{
    /// <summary>First palette index of the cockpit direction lights (0xB9).</summary>
    public const int FirstCockpitFlashEntry = 185;

    /// <summary>Number of cockpit direction lights (185..190).</summary>
    public const int CockpitFlashEntryCount = 6;

    /// <summary>Red value set by a hit (front 1, left/right 3/5, rear 0, above/below 2/4 in spc.c).</summary>
    public const short CockpitFlashRed = 0x38;

    /// <summary>Red value of the space background flash.</summary>
    public const short PlayerHitRed = 0x30;

    private readonly short[] _damageFlash = new short[3];
    private readonly short[] _fadeEntries = new short[CockpitFlashEntryCount * 3];

    /// <summary>The space background colour (R, G, B) set into entry 191.</summary>
    /// <remarks>C: asDamageFlashColour[3].</remarks>
    public Span<short> DamageFlashColour => _damageFlash;

    /// <summary>One cockpit light colour (R, G, B); index 0..5 = palette entry 185..190.</summary>
    /// <remarks>C: aPaletteFadeEntries[index].</remarks>
    public Span<short> GetFadeEntry(int index) => _fadeEntries.AsSpan(index * 3, 3);

    /// <summary>Entries 185..190 black, entry 191 (0, 0, 32).</summary>
    /// <remarks>C: ResetCockpitPaletteEntries (0x423E10, logic.c).</remarks>
    public void ResetCockpitPaletteEntries(GamePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        for (int i = 0; i < CockpitFlashEntryCount; i++)
        {
            Span<short> entry = GetFadeEntry(i);
            entry.Clear();
            palette.SetPaletteEntry(FirstCockpitFlashEntry + i, entry);
        }
        _damageFlash[0] = 0;
        _damageFlash[1] = 0;
        _damageFlash[2] = 32;
        palette.SetPaletteEntry(PaletteColours.PrimaryViewBuffer, _damageFlash);
    }

    /// <summary>Starts the red space flash (only in camera modes up to 3).</summary>
    /// <remarks>C: TriggerPlayerHitPaletteFlash (0x427C80, main.c).</remarks>
    public void TriggerPlayerHitPaletteFlash(int cameraViewMode)
    {
        if (cameraViewMode <= 3)
            _damageFlash[0] = PlayerHitRed;
    }

    /// <summary>Called once per rendered space frame: R -= 4 and re-set entry 191 while R != 0.</summary>
    /// <remarks>C: UpdateSpacePaletteFade (0x427CD0, main.c), VGA branch.</remarks>
    public void UpdateSpacePaletteFade(GamePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (_damageFlash[0] == 0)
            return;
        _damageFlash[0] = unchecked((short)(_damageFlash[0] - 4));
        palette.SetPaletteEntry(PaletteColours.PrimaryViewBuffer, _damageFlash);
    }

    /// <summary>Lights one cockpit direction entry (0..5) red.</summary>
    /// <remarks>C: <c>aPaletteFadeEntries[n][0] = 0x38</c> in spc.c.</remarks>
    public void FlashCockpitEntry(int index) => GetFadeEntry(index)[0] = CockpitFlashRed;

    /// <summary>R -= 4 with G = B = 0 while R != 0; otherwise only G is cleared (B is left alone).</summary>
    /// <remarks>C: FadeFlightPaletteEntry (0x427CA0, main.c).</remarks>
    public static void FadeFlightPaletteEntry(Span<short> entry)
    {
        if (entry[0] != 0)
        {
            entry[0] = unchecked((short)(entry[0] - 4));
            entry[1] = 0;
            entry[2] = 0;
            return;
        }
        entry[1] = 0;
    }

    /// <summary>Fades all six cockpit lights one step and writes them to entries 185..190.</summary>
    /// <remarks>C: the camera-mode-0 part of house_keep (0x427D40, main.c).</remarks>
    public void FadeCockpitFlashEntries(GamePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        for (int i = 0; i < CockpitFlashEntryCount; i++)
        {
            Span<short> entry = GetFadeEntry(i);
            FadeFlightPaletteEntry(entry);
            palette.SetPaletteEntry(FirstCockpitFlashEntry + i, entry);
        }
    }
}
