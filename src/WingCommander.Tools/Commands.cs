using WingCommander.Core.Resources;

namespace WingCommander.Tools;

/// <summary>
/// Command registry of wc1tool. Each subsystem registers its commands in its own file by
/// implementing one of the partial Register* methods, so parallel work never edits the
/// same file. Use <see cref="Register"/> with a name, a one-line usage and a handler.
/// </summary>
internal static partial class Commands
{
    private static readonly SortedDictionary<string, (string Usage, Func<ToolOptions, int> Run)> Registry =
        new(StringComparer.OrdinalIgnoreCase);

    static partial void RegisterResourceCommands();
    static partial void RegisterGraphicsCommands();
    static partial void RegisterAudioCommands();
    static partial void RegisterSimulationCommands();
    static partial void RegisterGameCommands();

    public static void Register(string name, string usage, Func<ToolOptions, int> run) =>
        Registry[name] = (usage, run);

    public static int Run(string[] args)
    {
        RegisterAll();
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }
        var options = ToolOptions.Parse(args);
        if (!Registry.TryGetValue(options.Command, out var command))
        {
            Console.Error.WriteLine($"unknown command '{options.Command}' (try 'wc1tool help')");
            return 1;
        }
        return command.Run(options);
    }

    private static void RegisterAll()
    {
        if (Registry.Count != 0)
            return;
        RegisterResourceCommands();
        RegisterGraphicsCommands();
        RegisterAudioCommands();
        RegisterSimulationCommands();
        RegisterGameCommands();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("wc1tool - Wing Commander 1 data inspection tool");
        Console.WriteLine();
        Console.WriteLine("usage: wc1tool <command> [arguments] [--game <dir>] [--out <path>]");
        Console.WriteLine();
        Console.WriteLine("commands:");
        foreach (var (name, (usage, _)) in Registry)
            Console.WriteLine($"  {name,-14} {usage}");
        Console.WriteLine();
        Console.WriteLine("The game directory is taken from --game, the WC1_GAME_DIR environment variable or");
        Console.WriteLine("the current directory. File arguments may be names inside GAMEDAT or paths.");
    }

    /// <summary>Opens a packet by GAMEDAT name, or by path when the argument is an existing file.</summary>
    public static PacketFile OpenPacket(ToolOptions o, string file) =>
        File.Exists(file) ? PacketFile.Load(file) : RequireGameDirectory(o).OpenPacket(file);

    public static GameDirectory RequireGameDirectory(ToolOptions o) =>
        GameDirectory.Locate(o.Option("game"))
        ?? throw new GameDataException("game directory not found; set it in config.json, use --game <dir> or set WC1_GAME_DIR");

    /// <summary>Resolves "3/1/0" (nested section path) to the decoded bytes of the innermost section.</summary>
    public static ReadOnlyMemory<byte> ResolveSectionPath(PacketFile packet, string path)
    {
        string[] parts = path.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            int index = int.Parse(parts[i]);
            if (i == parts.Length - 1)
                return packet.GetSection(index);
            packet = packet.OpenNested(index);
        }
        throw new GameDataException("empty section path");
    }
}

/// <summary>Parsed command line: command, positional arguments, --name value options and --flags.</summary>
internal sealed class ToolOptions
{
    private static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "out", "length", "frame", "section", "seconds", "rate", "palette", "scale", "count", "series", "campaign", "part",
        "at", "input", "args", "smooth", "colour", "ttf", "hd", "gpu", "filter",
    };

    private ToolOptions(string command, List<string> positionals, Dictionary<string, string> named, HashSet<string> flags)
    {
        Command = command;
        Positionals = positionals;
        Named = named;
        Flags = flags;
    }

    public string Command { get; }

    public IReadOnlyList<string> Positionals { get; }

    private Dictionary<string, string> Named { get; }

    private HashSet<string> Flags { get; }

    public static ToolOptions Parse(string[] args)
    {
        var positional = new List<string>();
        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < args.Length; i++)
        {
            string a = args[i];
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                string name = a[2..];
                if (ValueOptions.Contains(name) && i + 1 < args.Length)
                    named[name] = args[++i];
                else
                    flags.Add(name);
            }
            else
            {
                positional.Add(a);
            }
        }
        return new ToolOptions(args.Length > 0 ? args[0] : "", positional, named, flags);
    }

    public string Positional(int i) =>
        i < Positionals.Count ? Positionals[i] : throw new GameDataException($"missing argument #{i + 1}");

    public string? PositionalOrNull(int i) => i < Positionals.Count ? Positionals[i] : null;

    public string? Option(string name) => Named.GetValueOrDefault(name);

    public int IntOption(string name, int fallback) =>
        Named.TryGetValue(name, out var v) && int.TryParse(v, out int n) ? n : fallback;

    public bool Has(string flag) => Flags.Contains(flag);
}
