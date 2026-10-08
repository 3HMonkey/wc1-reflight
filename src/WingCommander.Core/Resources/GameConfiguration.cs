using System.Text.Json;

namespace WingCommander.Core.Resources;

/// <summary>
/// The port's local configuration file <c>config.json</c> (kept out of version control): where the
/// game data is. Example (see <c>config.example.json</c>):
/// <code>{ "gameDirectory": "C:\\GOG Games\\Wing Commander" }</code>
/// A relative path is relative to the file. The file is looked up in a start directory and its
/// parents, so the repository root works for <c>dotnet run</c>, the tools and the tests, and a
/// published build finds the file next to the executable.
/// </summary>
public sealed class GameConfiguration
{
    public const string FileName = "config.json";

    private GameConfiguration(string path, string? gameDirectory)
    {
        Path = path;
        GameDirectory = gameDirectory;
    }

    /// <summary>The file the configuration was read from.</summary>
    public string Path { get; }

    /// <summary>The game data directory (install folder or its GAMEDAT folder), absolute; null when not set.</summary>
    public string? GameDirectory { get; }

    /// <summary>Reads a configuration file.</summary>
    /// <exception cref="InvalidDataException">The file is not valid JSON or has a wrong value type.</exception>
    public static GameConfiguration Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string fullPath = System.IO.Path.GetFullPath(path);
        string? gameDirectory = null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(fullPath),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{fullPath}: the configuration must be a JSON object.");
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!property.NameEquals("gameDirectory"))
                    continue;
                if (property.Value.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException($"{fullPath}: \"gameDirectory\" must be a string.");
                gameDirectory = property.Value.GetString();
            }
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{fullPath}: {e.Message}", e);
        }
        if (!string.IsNullOrWhiteSpace(gameDirectory))
            gameDirectory = System.IO.Path.GetFullPath(gameDirectory, System.IO.Path.GetDirectoryName(fullPath)!);
        else
            gameDirectory = null;
        return new GameConfiguration(fullPath, gameDirectory);
    }

    /// <summary>
    /// The first <see cref="FileName"/> found in one of <paramref name="startDirectories"/> or their
    /// parents (searched in the given order), or null.
    /// </summary>
    public static GameConfiguration? Find(params string?[] startDirectories)
    {
        ArgumentNullException.ThrowIfNull(startDirectories);
        foreach (string? start in startDirectories)
        {
            if (string.IsNullOrEmpty(start))
                continue;
            for (var directory = new DirectoryInfo(System.IO.Path.GetFullPath(start)); directory is not null; directory = directory.Parent)
            {
                string candidate = System.IO.Path.Combine(directory.FullName, FileName);
                if (File.Exists(candidate))
                    return Load(candidate);
            }
        }
        return null;
    }

    /// <summary>Writes a configuration file with the game directory.</summary>
    public static void Save(string path, string gameDirectory)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(gameDirectory);
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("gameDirectory", gameDirectory);
        writer.WriteEndObject();
    }
}
