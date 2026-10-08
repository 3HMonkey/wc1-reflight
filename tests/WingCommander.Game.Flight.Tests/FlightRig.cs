using WingCommander.Core.Imaging;
using WingCommander.Core.Numerics;
using WingCommander.Core.Platform;
using WingCommander.Core.Video;
using WingCommander.Game.Runtime;
using WingCommander.Game.Video;
using WingCommander.Tests;

namespace WingCommander.Game.Flight.Tests;

/// <summary>A headless game with the flight layer over the GOG data (virtual clock, private user directory).</summary>
internal sealed class FlightRig
{
    public FlightRig(int seed = 1, FlightOptions? options = null)
    {
        Host = new HeadlessServices
        {
            UserDataDirectory = Path.Combine(Path.GetTempPath(), "wc1-tests", Guid.NewGuid().ToString("N")),
        };
        Runtime = new GameRuntime(Host, new CRandom((uint)seed));
        Game = new Wc1Game(Runtime, GameData.Require(), new Wc1GameOptions { Audio = false, SkipIntro = true });
        Layer = new FlightLayer(Game, options);
        Game.FlightLayer = Layer;
    }

    public HeadlessServices Host { get; }

    public GameRuntime Runtime { get; }

    public Wc1Game Game { get; }

    public FlightLayer Layer { get; }

    internal FlightSession Session => Layer.Session;

    public Framebuffer Front => Game.Display.Front.Pixels;

    /// <summary>Initialises the game and runs <paramref name="body"/> as the root coroutine.</summary>
    public void Start(Func<FlightRig, Task> body) =>
        Runtime.Start(async _ =>
        {
            Game.Initialize();
            await body(this);
        });

    /// <summary>Runs until the root coroutine ends (or the virtual time limit); rethrows its failure.</summary>
    public void Run(double limitMilliseconds = 600_000)
    {
        Runtime.RunHeadless(limitMilliseconds);
        if (Runtime.Failure is { } failure)
            throw new InvalidOperationException("The game coroutine failed.", failure);
    }

    /// <summary>Ends the flight after <paramref name="frames"/> presented space frames (arcade state 5).</summary>
    public void StopAfter(int frames, Action<FlightSession, int>? onFrame = null)
    {
        int count = 0;
        Session.FramePresented = session =>
        {
            count++;
            onFrame?.Invoke(session, count);
            if (count >= frames)
                session.Sim.ArcadeState = 5;
        };
    }

    /// <summary>Scripts a key press (down at <paramref name="atMilliseconds"/>, up 50 ms later).</summary>
    public void Key(double atMilliseconds, int scanCode, int virtualKey = 0, double holdMilliseconds = 50,
        HostModifiers modifiers = HostModifiers.None)
    {
        Runtime.Events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyDown, scanCode, virtualKey, 0, 0, 0, modifiers, false),
            atMilliseconds);
        Runtime.Events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyUp, scanCode, virtualKey, 0, 0, 0, modifiers, false),
            atMilliseconds + holdMilliseconds);
    }

    /// <summary>
    /// Calls <paramref name="onPresent"/> after every present with the number of presents (DIBslamReal
    /// calls) made before it; chained to <see cref="Display.Presented"/>.
    /// </summary>
    public void OnPresent(Action<int> onPresent)
    {
        var display = Game.Display;
        var previous = display.Presented;
        display.Presented = () =>
        {
            previous?.Invoke();
            onPresent(display.SlamCount);
        };
    }

    /// <summary>Pixels of <paramref name="colour"/> (or, with null, not black) in screen rows <paramref name="top"/>..<paramref name="bottom"/>.</summary>
    public int CountPixels(int top, int bottom, byte? colour = null)
    {
        int count = 0;
        var pixels = Front.Pixels;
        for (int i = top * Framebuffer.Width; i < (bottom + 1) * Framebuffer.Width; i++)
        {
            if (colour is { } c ? pixels[i] == c : pixels[i] != 0)
                count++;
        }
        return count;
    }

    /// <summary>FNV-1a of the displayed frame.</summary>
    public static ulong HashFrame(Framebuffer frame)
    {
        ulong hash = 14695981039346656037ul;
        foreach (byte b in frame.Pixels)
        {
            hash ^= b;
            hash *= 1099511628211ul;
        }
        return hash;
    }

    /// <summary>The PNG output directory (WC1_FLIGHT_PNG_DIR), or null when frames are not written.</summary>
    public static string? PngDirectory => Environment.GetEnvironmentVariable("WC1_FLIGHT_PNG_DIR");

    /// <summary>Writes the displayed frame as PNG when <see cref="PngDirectory"/> is set (for looking at it while porting).</summary>
    public void SavePng(string name)
    {
        if (PngDirectory is not { Length: > 0 } directory)
            return;
        Directory.CreateDirectory(directory);
        Png.WriteIndexed(Path.Combine(directory, name + ".png"), Framebuffer.Width, Framebuffer.Height, Front.Pixels,
            Game.Display.Front.Palette.Rgb);
    }
}
