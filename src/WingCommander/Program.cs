using WingCommander;
using WingCommander.Core.Rendering;
using WingCommander.Core.Resources;
using WingCommander.Game;
using WingCommander.Game.Config;
using WingCommander.Game.Flight;
using WingCommander.Game.Runtime;
using WingCommander.Host.Sdl;
using WingCommander.Render.Vulkan;

var options = CommandLine.Parse(args);
if (options is null)
    return 0;

// --game, WC1_GAME_DIR, config.json (current or executable directory and their parents), the current directory.
GameDirectory? directory;
try
{
    directory = GameDirectory.Locate(options.GameDirectory);
}
catch (InvalidDataException e)
{
    Console.Error.WriteLine($"config.json: {e.Message}");
    return 2;
}
if (directory is null)
{
    Console.Error.WriteLine("Game data not found. Put the path into config.json (see config.example.json), " +
        "or use --game <dir> or WC1_GAME_DIR.");
    return 2;
}
Console.WriteLine($"Game data: {directory.DataPath} ({(directory.IsDosData ? "DOS" : "Kilrathi Saga")})");

try
{
    var (createdHost, createdRenderer) = Presentation.Create(options);
    using SdlHost host = createdHost;
    using IRenderer renderer = createdRenderer;
    Console.WriteLine($"Renderer: {renderer.Name}");

    var runtime = new GameRuntime(host);
    if (options.HostCheck)
    {
        runtime.Start(game => HostCheckScreen.RunAsync(game, directory));
    }
    else
    {
        bool highResolutionText = !options.ClassicText && renderer is VulkanRenderer { SupportsText: true };
        var game = new Wc1Game(runtime, directory, new Wc1GameOptions
        {
            Arguments = options.GameArguments,
            Audio = !options.NoAudio,
            SkipIntro = options.SkipIntro,
            HighResolutionText = highResolutionText,
            ReplacementFonts = options.OriginalFonts ? new Dictionary<int, ReplacementFont>() : BundledFonts.Load(),
        });
        bool literal = options.KsLiteral;
        game.FlightLayer = new FlightLayer(game, new FlightOptions
        {
            RendererSupportsSpaceSprites = renderer is VulkanRenderer { SupportsSpaceSprites: true },
            SpriteSpaceView = !options.ClassicSpace,
            DrawPlanets = !literal,
            VduStaticNoise = !literal,
            CockpitExplosionAnimation = !literal,
            EscapePausesFlight = !literal,
            FixedCreditCount = !literal,
        });
        game.Start();
    }
    WindowTest? windowTest = null;
    if (options.WindowTest)
    {
        windowTest = new WindowTest(host, renderer, runtime.Frame, options.CaptureDirectory);
        host.LoopHook = windowTest.OnLoop;
    }
    host.Run(runtime, renderer);
    windowTest?.Finish();
    if (runtime.Failure is { } failure)
    {
        Console.Error.WriteLine(failure);
        return 4;
    }
    if (windowTest is { Failures: > 0 })
        return 5;
}
catch (HostException e)
{
    Console.Error.WriteLine($"host error: {e.Message}");
    return 3;
}
return 0;

namespace WingCommander
{
    internal sealed class CommandLine
    {
        public string? GameDirectory { get; private set; }
        public int MaxFrames { get; private set; }
        public bool Fullscreen { get; private set; }
        public int Scale { get; private set; } = 3;
        public bool NoAudio { get; private set; }
        public bool SkipIntro { get; private set; }
        public bool HostCheck { get; private set; }
        public bool ClassicSpace { get; private set; }
        public bool ClassicText { get; private set; }
        public bool OriginalFonts { get; private set; }
        public bool KsLiteral { get; private set; }
        public RendererChoice Renderer { get; private set; }
        public bool WindowTest { get; private set; }
        public string? CaptureDirectory { get; private set; }
#if DEBUG
        public ValidationMode VulkanValidation { get; private set; } = ValidationMode.Auto;
#else
        public ValidationMode VulkanValidation { get; private set; } = ValidationMode.Disabled;
#endif
        public List<string> GameArguments { get; } = [];
        public RendererSettings RendererSettings { get; } = new();

        public static CommandLine? Parse(string[] args)
        {
            var o = new CommandLine();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--game" when i + 1 < args.Length:
                        o.GameDirectory = args[++i];
                        break;
                    case "--frames" when i + 1 < args.Length:
                        o.MaxFrames = int.Parse(args[++i]);
                        break;
                    case "--scale" when i + 1 < args.Length:
                        o.Scale = int.Parse(args[++i]);
                        break;
                    case "--fullscreen":
                        o.Fullscreen = true;
                        break;
                    case "--filter" when i + 1 < args.Length:
                        o.RendererSettings.Filter = args[++i].ToLowerInvariant() switch
                        {
                            "nearest" => ScalingFilter.Nearest,
                            "linear" => ScalingFilter.Linear,
                            _ => ScalingFilter.SharpBilinear,
                        };
                        break;
                    case "--square-pixels":
                        o.RendererSettings.Aspect = AspectMode.SquarePixels;
                        break;
                    case "--integer":
                        o.RendererSettings.IntegerScaling = true;
                        break;
                    case "--no-vsync":
                        o.RendererSettings.VSync = false;
                        break;
                    case "--no-audio":
                        o.NoAudio = true;
                        break;
                    case "--skip-intro":
                        o.SkipIntro = true;
                        break;
                    case "--renderer" when i + 1 < args.Length:
                        o.Renderer = args[++i].ToLowerInvariant() switch
                        {
                            "vulkan" => RendererChoice.Vulkan,
                            "sdl" => RendererChoice.Sdl,
                            _ => RendererChoice.Auto,
                        };
                        break;
                    case "--vulkan-validation":
                        o.VulkanValidation = ValidationMode.Required;
                        break;
                    case "--window-test":
                        o.WindowTest = true;
                        break;
                    case "--capture-dir" when i + 1 < args.Length:
                        o.CaptureDirectory = args[++i];
                        break;
                    case "--classic-space":
                        o.ClassicSpace = true;
                        break;
                    case "--classic-text":
                        o.ClassicText = true;
                        break;
                    case "--original-fonts":
                        o.OriginalFonts = true;
                        break;
                    case "--ks-literal":
                        o.KsLiteral = true;
                        break;
                    case "--host-check":
                        o.HostCheck = true;
                        break;
                    case "--":
                        o.GameArguments.AddRange(args[(i + 1)..]);
                        return o;
                    case "-h" or "--help":
                        Console.WriteLine("""
                            wc1 - Wing Commander (.NET port)

                            usage: wc1 [--game <dir>] [--scale N] [--fullscreen] [--filter nearest|sharp|linear]
                                       [--square-pixels] [--integer] [--no-vsync] [--frames N]
                                       [--renderer auto|vulkan|sdl] [--vulkan-validation]
                                       [--no-audio] [--skip-intro] [--classic-space] [--classic-text] [--original-fonts]
                                       [--ks-literal] [--host-check]
                                       [--window-test [--capture-dir <dir>]]
                                       [-- <original switches>]

                            The game directory (GOG install root or its GAMEDAT folder) is taken from --game,
                            the WC1_GAME_DIR environment variable, config.json ("gameDirectory", see
                            config.example.json; searched in the current and the executable's directory and
                            their parents) or the current directory.
                            Arguments after -- are the original game's switches (for example: p = no music).
                            --classic-space draws space objects into the 320x200 frame like the original instead of
                            as sprites at output resolution (Vulkan). --classic-text keeps the text in the 320x200 frame
                            instead of drawing it at output resolution; --original-fonts uses the vectorized original
                            fonts instead of the bundled replacement fonts. F10 shows or hides the key help in flight.
                            --ks-literal switches off the visual fixes
                            (planets, VDU static, cockpit explosion, Esc pause, credit count) for Kilrathi Saga behaviour.
                            Alt+Enter toggles fullscreen.
                            """);
                        return null;
                }
            }
            if (o.WindowTest && o.MaxFrames < WingCommander.WindowTest.RequiredFrames)
                o.MaxFrames = WingCommander.WindowTest.RequiredFrames + 10;
            return o;
        }
    }
}
