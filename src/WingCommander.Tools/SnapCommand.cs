using System.Globalization;
using WingCommander.Core.Imaging;
using WingCommander.Core.Numerics;
using WingCommander.Core.Platform;
using WingCommander.Core.Rendering;
using WingCommander.Game;
using WingCommander.Game.Config;
using WingCommander.Game.Flight;
using WingCommander.Game.Runtime;

namespace WingCommander.Tools;

internal static partial class Commands
{
    private const StringSplitOptions SplitClean = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;

    /// <summary>
    /// Runs the game headless on the virtual clock (seeded rand, optional scripted input) and
    /// writes the displayed frame as PNG at each requested time. Deterministic, so it doubles as
    /// a regression tool for screens.
    /// </summary>
    private static int SnapCommand(ToolOptions o)
    {
        var directory = RequireGameDirectory(o);
        string outDir = o.Option("out") ?? Path.Combine(Environment.CurrentDirectory, "snap");
        System.IO.Directory.CreateDirectory(outDir);
        double[] times = (o.Option("at") ?? "1000").Split(",", SplitClean)
            .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).Order().ToArray();

        (int Width, int Height)? hd = null;
        if (o.Option("hd") is { } size)
        {
            string[] parts = size.Split('x', SplitClean);
            hd = (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
        }

        var host = new HeadlessServices { UserDataDirectory = Path.Combine(Path.GetTempPath(), "wc1tool-snap") };
        var runtime = new GameRuntime(host, new CRandom(1));
        var game = new Wc1Game(runtime, directory, new Wc1GameOptions
        {
            Audio = o.Has("audio"),
            SkipIntro = o.Has("skip-intro"),
            Arguments = (o.Option("args") ?? "").Split(" ", SplitClean),
            HighResolutionText = hd is not null,
            ReplacementFonts = o.Has("original-fonts") ? new Dictionary<int, ReplacementFont>() : BundledFonts.Load(),
        });
        // The flight layer draws every sprite on the CPU here (no R2 renderer), so the PNGs show the classic frame.
        game.FlightLayer = new FlightLayer(game);
        foreach (var (at, e) in ParseInputScript(o.Option("input")))
            runtime.Events.EnqueueHostEvent(e, at);
        game.Start();

        foreach (double at in times)
        {
            bool finished = runtime.RunHeadless(at);
            if (runtime.Failure is { } failure)
            {
                Console.Error.WriteLine(failure);
                return 1;
            }
            var layer = runtime.Frame.Classic;
            string path = Path.Combine(outDir, string.Create(CultureInfo.InvariantCulture, $"snap_{at:000000}.png"));
            Png.WriteIndexed(path, 320, 200, layer.Pixels.Pixels, layer.Palette.Rgb);
            if (hd is var (width, height))
            {
                // The output-resolution text and key help, rendered like the Vulkan passes (4:3).
                var rgba = new byte[width * height * 4];
                ReferenceCompositor.Render(runtime.Frame, width, height, AspectMode.FourByThree, rgba);
                Png.WriteRgba(Path.ChangeExtension(path, null) + "_hd.png", width, height, rgba);
            }
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{runtime.Scheduler.Now,9:0} ms  presents {game.Display.SlamCount,5}  {(finished ? "finished" : "running ")}  {path}"));
            if (finished)
                break;
        }
        return 0;
    }

    /// <summary>
    /// "ms key scan [vk]; ms click x y; ms move x y" (scan codes and keys in hex with 0x or
    /// decimal; coordinates in 320x200 frame space). Keys are released 50 ms later.
    /// </summary>
    private static IEnumerable<(double At, HostInputEvent Event)> ParseInputScript(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
            yield break;
        foreach (string entry in script.Split(";", SplitClean))
        {
            string[] p = entry.Split(" ", SplitClean);
            double at = double.Parse(p[0], CultureInfo.InvariantCulture);
            switch (p[1].ToLowerInvariant())
            {
                case "key":
                {
                    int scan = ParseNumber(p[2]);
                    int vk = p.Length > 3 ? ParseNumber(p[3]) : 0;
                    yield return (at, new HostInputEvent(HostInputKind.KeyDown, scan, vk, 0, 0, 0, HostModifiers.None, false));
                    yield return (at + 50, new HostInputEvent(HostInputKind.KeyUp, scan, vk, 0, 0, 0, HostModifiers.None, false));
                    break;
                }
                case "move":
                {
                    int x = ParseNumber(p[2]), y = ParseNumber(p[3]);
                    yield return (at, new HostInputEvent(HostInputKind.MouseMove, 0, 0, x, y, 0, HostModifiers.None, false));
                    break;
                }
                case "click":
                {
                    int x = ParseNumber(p[2]), y = ParseNumber(p[3]);
                    yield return (at, new HostInputEvent(HostInputKind.MouseMove, 0, 0, x, y, 0, HostModifiers.None, false));
                    yield return (at + 10, new HostInputEvent(HostInputKind.MouseButtonDown, 1, 0, x, y, MouseButtons.Left, HostModifiers.None, false));
                    yield return (at + 60, new HostInputEvent(HostInputKind.MouseButtonUp, 1, 0, x, y, 0, HostModifiers.None, false));
                    break;
                }
                default:
                    throw new FormatException($"unknown input action '{p[1]}' in '{entry}'");
            }
        }
    }

    private static int ParseNumber(string s) =>
        s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : int.Parse(s, CultureInfo.InvariantCulture);
}
