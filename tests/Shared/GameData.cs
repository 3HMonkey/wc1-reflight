using WingCommander.Core.Resources;

namespace WingCommander.Tests;

/// <summary>
/// Locates the game data for data-driven tests: WC1_GAME_DIR, else the repository's config.json
/// (found from the test binaries upwards); tests that need the data are skipped without it.
/// </summary>
public static class GameData
{
    private static readonly Lazy<GameDirectory?> Instance = new(() => GameDirectory.Locate());

    public static GameDirectory? Directory => Instance.Value;

    public static bool IsAvailable => Directory is not null;

    public static GameDirectory Require() => Directory ?? throw new InvalidOperationException("Game data not available.");
}

/// <summary>A fact that is skipped when the game data directory is not available.</summary>
public sealed class DataFactAttribute : FactAttribute
{
    public DataFactAttribute()
    {
        if (!GameData.IsAvailable)
            Skip = "Game data not found (set WC1_GAME_DIR).";
    }
}

/// <summary>A theory that is skipped when the game data directory is not available.</summary>
public sealed class DataTheoryAttribute : TheoryAttribute
{
    public DataTheoryAttribute()
    {
        if (!GameData.IsAvailable)
            Skip = "Game data not found (set WC1_GAME_DIR).";
    }
}
