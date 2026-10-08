namespace WingCommander.Game.Config;

/// <summary>A TrueType font that replaces a FONTS.FNT font in the output-resolution text (ADR-013).</summary>
public sealed record ReplacementFont(string Name, ReadOnlyMemory<byte> Data)
{
    /// <summary>A font file from disk (named after the file).</summary>
    public static ReplacementFont FromFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new ReplacementFont(Path.GetFileNameWithoutExtension(path), File.ReadAllBytes(path));
    }
}

/// <summary>
/// The replacement fonts compiled into the game (embedded resources from <c>assets/fonts</c>; their
/// licences are in THIRD-PARTY-NOTICES.txt next to the executable and credited in README.md):
/// Tektur SemiBold (SIL OFL 1.1) for font 0, SPACE WING LEADER (CC BY 3.0) for fonts 1 and 2,
/// CHAWP (SIL OFL 1.1) for font 3.
/// </summary>
public static class BundledFonts
{
    public const string Tektur = "fonts/Tektur-SBold.ttf";

    public const string SpaceWingLeader = "fonts/space-wing-leader.ttf";

    public const string Chawp = "fonts/chawp.ttf";

    /// <summary>Font index to replacement font.</summary>
    public static IReadOnlyDictionary<int, ReplacementFont> Load()
    {
        ReplacementFont spaceWingLeader = Resource(SpaceWingLeader, "SPACE WING LEADER");
        return new Dictionary<int, ReplacementFont>
        {
            [0] = Resource(Tektur, "Tektur SemiBold"),
            [1] = spaceWingLeader,
            [2] = spaceWingLeader,
            [3] = Resource(Chawp, "CHAWP"),
        };
    }

    private static ReplacementFont Resource(string name, string displayName)
    {
        using Stream stream = typeof(BundledFonts).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The bundled font '{name}' is missing from the assembly.");
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return new ReplacementFont(displayName, data);
    }
}
