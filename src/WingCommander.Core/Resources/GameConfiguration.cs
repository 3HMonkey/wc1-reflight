using System.Text.Json;
using System.Text.Json.Nodes;

namespace WingCommander.Core.Resources;

/// <summary>
/// The port's local configuration file <c>config.json</c> (kept out of version control): where the
/// game data is, plus the player's settings in sections (<c>audio</c>, <c>display</c>,
/// <c>text</c>, <c>interface</c>). Example (see <c>config.example.json</c>):
/// <code>{ "gameDirectory": "C:\\GOG Games\\Wing Commander", "audio": { "musicVolume": 10 } }</code>
/// A relative game path is relative to the file. The file is looked up in a start directory and
/// its parents, so the repository root works for <c>dotnet run</c>, the tools and the tests, and a
/// published build finds the file next to the executable. Saving keeps every property the port
/// does not know (comments are not preserved).
/// </summary>
public sealed class GameConfiguration
{
    public const string FileName = "config.json";

    private static readonly JsonDocumentOptions DocumentOptions =
        new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    private readonly JsonObject _root;

    private GameConfiguration(string path, JsonObject root, bool exists)
    {
        Path = path;
        _root = root;
        Exists = exists;
    }

    /// <summary>The file the configuration was read from (or will be written to).</summary>
    public string Path { get; }

    /// <summary>False for a configuration created in memory that was never saved.</summary>
    public bool Exists { get; private set; }

    /// <summary>The game data directory (install folder or its GAMEDAT folder), absolute; null when not set.</summary>
    public string? GameDirectory
    {
        get
        {
            if (!TryGetString(null, "gameDirectory", out string value) || string.IsNullOrWhiteSpace(value))
                return null;
            return System.IO.Path.GetFullPath(value, System.IO.Path.GetDirectoryName(Path)!);
        }
    }

    /// <summary>Reads a configuration file.</summary>
    /// <exception cref="InvalidDataException">The file is not valid JSON or has a wrong value type.</exception>
    public static GameConfiguration Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string fullPath = System.IO.Path.GetFullPath(path);
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(File.ReadAllBytes(fullPath), documentOptions: DocumentOptions);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{fullPath}: {e.Message}", e);
        }
        if (node is not JsonObject root)
            throw new InvalidDataException($"{fullPath}: the configuration must be a JSON object.");
        var configuration = new GameConfiguration(fullPath, root, exists: true);
        if (root["gameDirectory"] is { } directory && directory.GetValueKind() != JsonValueKind.String)
            throw new InvalidDataException($"{fullPath}: \"gameDirectory\" must be a string.");
        return configuration;
    }

    /// <summary>An empty configuration that will be written to <paramref name="path"/> on <see cref="Save()"/>.</summary>
    public static GameConfiguration CreateAt(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new GameConfiguration(System.IO.Path.GetFullPath(path), new JsonObject(), exists: false);
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
        var configuration = CreateAt(path);
        configuration.SetString(null, "gameDirectory", gameDirectory);
        configuration.Save();
    }

    /// <summary>Writes the configuration back to <see cref="Path"/> (indented JSON).</summary>
    public void Save()
    {
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        using (var stream = File.Create(Path))
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            _root.WriteTo(writer);
        File.AppendAllText(Path, "\n");
        Exists = true;
    }

    public bool TryGetString(string? section, string key, out string value)
    {
        value = "";
        if (Node(section, key) is not JsonValue node || node.GetValueKind() != JsonValueKind.String)
            return false;
        value = node.GetValue<string>();
        return true;
    }

    public bool TryGetInt(string? section, string key, out int value)
    {
        value = 0;
        return Node(section, key) is JsonValue node && node.GetValueKind() == JsonValueKind.Number && node.TryGetValue(out value);
    }

    public bool TryGetBool(string? section, string key, out bool value)
    {
        value = false;
        if (Node(section, key) is not JsonValue node)
            return false;
        switch (node.GetValueKind())
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                return true;
            default:
                return false;
        }
    }

    public void SetString(string? section, string key, string value) => Section(section)[key] = JsonValue.Create(value);

    public void SetInt(string? section, string key, int value) => Section(section)[key] = JsonValue.Create(value);

    public void SetBool(string? section, string key, bool value) => Section(section)[key] = JsonValue.Create(value);

    private JsonNode? Node(string? section, string key)
    {
        if (section is null)
            return _root[key];
        return _root[section] is JsonObject container ? container[key] : null;
    }

    private JsonObject Section(string? section)
    {
        if (section is null)
            return _root;
        if (_root[section] is JsonObject container)
            return container;
        var created = new JsonObject();
        _root[section] = created;
        return created;
    }
}
