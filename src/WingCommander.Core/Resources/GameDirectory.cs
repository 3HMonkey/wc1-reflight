using System.Collections.Concurrent;

namespace WingCommander.Core.Resources;

/// <summary>
/// Locates and opens the game's data files. Handles the GOG layout (root with GAMEDAT/)
/// and a flat layout (all files in one directory), resolves names case-insensitively so
/// Linux/macOS installs work, and caches opened packet files. Safe to share between threads
/// (tests run in parallel); the game itself uses it from one thread.
/// </summary>
public sealed class GameDirectory
{
    private readonly Dictionary<string, string> _fileIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PacketFile> _packets = new(StringComparer.OrdinalIgnoreCase);
    private InstallTable? _installTable;

    private GameDirectory(string rootPath, string dataPath)
    {
        RootPath = rootPath;
        DataPath = dataPath;
        foreach (string path in Directory.EnumerateFiles(dataPath))
            _fileIndex[Path.GetFileName(path)] = path;
    }

    /// <summary>Directory containing WC.EXE / GAMEDAT (or the data directory itself).</summary>
    public string RootPath { get; }

    /// <summary>Directory containing MODULE.000 etc.</summary>
    public string DataPath { get; }

    /// <summary>True when MODULE.000 uses DOS compressed packets (byte 7 == 1).</summary>
    /// <remarks>C: SdlUsingDosData.</remarks>
    public bool IsDosData
    {
        get
        {
            if (!TryResolve("MODULE.000", out string path))
                return false;
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[8];
            return stream.Read(header) == 8 && header[7] == 1;
        }
    }

    public InstallTable InstallTable => Volatile.Read(ref _installTable) ?? LoadInstallTable();

    private InstallTable LoadInstallTable()
    {
        var table = InstallTable.Parse(ReadFile("INSTALL.DAT"));
        return Interlocked.CompareExchange(ref _installTable, table, null) ?? table;
    }

    /// <summary>
    /// Opens a game directory. Accepts the install root (containing GAMEDAT) or the
    /// GAMEDAT directory itself.
    /// </summary>
    public static GameDirectory Open(string path)
    {
        if (!Directory.Exists(path))
            throw new GameDataException($"Game directory '{path}' does not exist.");

        string? dataDir = FindChildDirectory(path, "GAMEDAT");
        if (dataDir is not null)
            return new GameDirectory(path, dataDir);
        if (FindFile(path, "MODULE.000") is not null)
            return new GameDirectory(Path.GetDirectoryName(path) ?? path, path);
        throw new GameDataException($"'{path}' contains neither a GAMEDAT directory nor MODULE.000.");
    }

    /// <summary>
    /// Finds the game directory from an explicit path, the WC1_GAME_DIR environment variable, the
    /// <c>gameDirectory</c> of the first <c>config.json</c> in the current directory, the
    /// executable's directory or one of their parents (<see cref="GameConfiguration"/>), or the
    /// current directory itself. Returns null when nothing is found.
    /// </summary>
    /// <exception cref="InvalidDataException">A config.json was found but cannot be read.</exception>
    public static GameDirectory? Locate(string? explicitPath = null) =>
        Locate(explicitPath, GameConfiguration.Find(Directory.GetCurrentDirectory(), AppContext.BaseDirectory));

    /// <summary>
    /// Like <see cref="Locate(string?)"/> with an already loaded configuration (null = none).
    /// </summary>
    public static GameDirectory? Locate(string? explicitPath, GameConfiguration? configuration)
    {
        string? configured = configuration?.GameDirectory;
        foreach (string? candidate in new[] { explicitPath, Environment.GetEnvironmentVariable("WC1_GAME_DIR"), configured, Directory.GetCurrentDirectory() })
        {
            if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
                continue;
            try
            {
                return Open(candidate);
            }
            catch (GameDataException)
            {
                // try the next candidate
            }
        }
        return null;
    }

    public bool TryResolve(string fileName, out string fullPath) => _fileIndex.TryGetValue(fileName, out fullPath!);

    public string Resolve(string fileName) =>
        TryResolve(fileName, out string path)
            ? path
            : throw new GameDataException($"Data file '{fileName}' not found in '{DataPath}'.");

    public bool Exists(string fileName) => _fileIndex.ContainsKey(fileName);

    public byte[] ReadFile(string fileName)
    {
        try
        {
            return File.ReadAllBytes(Resolve(fileName));
        }
        catch (IOException e)
        {
            throw new GameDataException($"Cannot read '{fileName}': {e.Message}", e);
        }
    }

    /// <summary>Opens (and caches) a packet container by file name.</summary>
    public PacketFile OpenPacket(string fileName) =>
        _packets.GetOrAdd(fileName, static (name, directory) => PacketFile.Load(directory.Resolve(name)), this);

    /// <summary>Opens a packet container by 0-based logical file index (see <see cref="LogicalFile"/>).</summary>
    public PacketFile OpenPacket(int logicalFile) => OpenPacket(InstallTable[logicalFile].Name);

    /// <summary>Convenience: decoded section of a logical file.</summary>
    /// <remarks>C: FetchDiskPacketRetrying / LoadPacketAllocated.</remarks>
    public ReadOnlyMemory<byte> LoadSection(int logicalFile, int section) => OpenPacket(logicalFile).GetSection(section);

    private static string? FindChildDirectory(string parent, string name)
    {
        foreach (string dir in Directory.EnumerateDirectories(parent))
            if (string.Equals(Path.GetFileName(dir), name, StringComparison.OrdinalIgnoreCase))
                return dir;
        return null;
    }

    private static string? FindFile(string dir, string name)
    {
        foreach (string file in Directory.EnumerateFiles(dir))
            if (string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase))
                return file;
        return null;
    }
}
