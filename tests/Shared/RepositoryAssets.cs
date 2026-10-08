namespace WingCommander.Tests;

/// <summary>Files under the repository's <c>assets/</c> folder (bundled fonts), found from the test binaries.</summary>
public static class RepositoryAssets
{
    private static readonly Lazy<string?> Root = new(() =>
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "WingCommander.slnx")))
                return directory.FullName;
        }
        return null;
    });

    /// <summary>Tektur SemiBold (font 0).</summary>
    public static string Tektur => Locate("fonts", "tektur", "Tektur-SBold.ttf");

    /// <summary>SPACE WING LEADER (fonts 1 and 2).</summary>
    public static string SpaceWingLeader => Locate("fonts", "space-wing-leader", "space-wing-leader.ttf");

    /// <summary>CHAWP (font 3, the chalk board).</summary>
    public static string Chawp => Locate("fonts", "chawp", "chawp.ttf");

    /// <summary>A file relative to the repository root.</summary>
    public static string InRepository(params string[] parts) =>
        System.IO.Path.Combine([Root.Value ?? throw new InvalidOperationException("Repository root not found."), .. parts]);

    public static string Locate(params string[] parts) =>
        System.IO.Path.Combine([Root.Value ?? throw new InvalidOperationException("Repository root not found."), "assets", .. parts]);
}
