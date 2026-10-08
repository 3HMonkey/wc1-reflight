using WingCommander.Core.Resources;

namespace WingCommander.Core.Tests.Resources;

public sealed class GameConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wc1-config-" + Guid.NewGuid().ToString("N"));

    public GameConfigurationTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string relativePath, string json)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Load_ReadsTheGameDirectory_WithCommentsAndTrailingCommas()
    {
        string path = Write("config.json", "{\n  // where the game is\n  \"gameDirectory\": \"/games/wc1\",\n}");
        var configuration = GameConfiguration.Load(path);
        Assert.Equal(Path.GetFullPath("/games/wc1", _root), configuration.GameDirectory);
        Assert.Equal(path, configuration.Path);
    }

    [Fact]
    public void RelativeGameDirectory_IsRelativeToTheFile()
    {
        string path = Write(Path.Combine("repo", "config.json"), "{ \"gameDirectory\": \"data/wc1\" }");
        Assert.Equal(Path.Combine(_root, "repo", "data", "wc1"), GameConfiguration.Load(path).GameDirectory);
    }

    [Fact]
    public void Find_SearchesTheParents_FirstStartDirectoryWins()
    {
        Write(Path.Combine("repo", "config.json"), "{ \"gameDirectory\": \"a\" }");
        Write(Path.Combine("other", "config.json"), "{ \"gameDirectory\": \"b\" }");
        string deep = Path.Combine(_root, "repo", "src", "bin", "Debug");
        Directory.CreateDirectory(deep);
        var found = GameConfiguration.Find(null, deep, Path.Combine(_root, "other"));
        Assert.NotNull(found);
        Assert.Equal(Path.Combine(_root, "repo", "a"), found.GameDirectory);
    }

    [Fact]
    public void MissingValue_IsNull_AndWrongTypes_AreReported()
    {
        Assert.Null(GameConfiguration.Load(Write("empty.json", "{ }")).GameDirectory);
        Assert.Throws<InvalidDataException>(() => GameConfiguration.Load(Write("number.json", "{ \"gameDirectory\": 5 }")));
        Assert.Throws<InvalidDataException>(() => GameConfiguration.Load(Write("broken.json", "{ \"gameDirectory\": ")));
    }

    [Fact]
    public void Save_RoundTrips()
    {
        string path = Path.Combine(_root, "saved.json");
        string directory = Path.Combine(_root, "Wing Commander");
        GameConfiguration.Save(path, directory);
        Assert.Equal(directory, GameConfiguration.Load(path).GameDirectory);
    }
}
