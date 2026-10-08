using WingCommander.Core.Imaging;
using WingCommander.Core.Numerics;
using WingCommander.Core.Platform;
using WingCommander.Core.Resources;
using WingCommander.Core.Video;
using WingCommander.Game.Campaign;
using WingCommander.Game.Runtime;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Screens.Rooms;

/// <summary>
/// A headless game like <see cref="ScreenRig"/>, but over its own <see cref="GameDirectory"/>
/// instance: the shared one of <see cref="GameData"/> caches packets in a plain dictionary, and
/// the room tests open many packets while other test classes run in parallel.
/// </summary>
internal sealed class RoomScreenRig
{
    public RoomScreenRig(Wc1GameOptions? options = null)
    {
        Host = new HeadlessServices
        {
            UserDataDirectory = Path.Combine(Path.GetTempPath(), "wc1-tests", Guid.NewGuid().ToString("N")),
        };
        // An empty save file in the user directory keeps the install copy from being imported.
        System.IO.Directory.CreateDirectory(Host.UserDataDirectory);
        SaveGameFile.CreateEmpty(Path.Combine(Host.UserDataDirectory, SaveGameFile.FileName));
        Runtime = new GameRuntime(Host, new CRandom(1));
        Directory = GameDirectory.Open(GameData.Require().DataPath);
        Game = new Wc1Game(Runtime, Directory, options ?? new Wc1GameOptions { Audio = false });
    }

    public HeadlessServices Host { get; }

    public GameRuntime Runtime { get; }

    public GameDirectory Directory { get; }

    public Wc1Game Game { get; }

    public Framebuffer Front => Game.Display.Front.Pixels;

    /// <summary>Initialises the game and runs <paramref name="screen"/> as the root coroutine.</summary>
    public void Start(Func<Wc1Game, Task> screen) =>
        Runtime.Start(async _ =>
        {
            Game.Initialize();
            await screen(Game);
        });

    public void Key(double atMilliseconds, int scanCode, int virtualKey = 0)
    {
        Runtime.Events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyDown, scanCode, virtualKey, 0, 0, 0, HostModifiers.None, false), atMilliseconds);
        Runtime.Events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyUp, scanCode, virtualKey, 0, 0, 0, HostModifiers.None, false), atMilliseconds + 50);
    }

    public void Click(double atMilliseconds, int x, int y)
    {
        Runtime.Events.EnqueueHostEvent(new HostInputEvent(HostInputKind.MouseMove, 0, 0, x, y, 0, HostModifiers.None, false), atMilliseconds);
        Runtime.Events.EnqueueHostEvent(new HostInputEvent(HostInputKind.MouseButtonDown, 1, 0, x, y, MouseButtons.Left, HostModifiers.None, false), atMilliseconds + 20);
        Runtime.Events.EnqueueHostEvent(new HostInputEvent(HostInputKind.MouseButtonUp, 1, 0, x, y, 0, HostModifiers.None, false), atMilliseconds + 70);
    }

    /// <summary>Writes the displayed frame as PNG.</summary>
    public void SaveFront(string path) =>
        Png.WriteIndexed(path, Framebuffer.Width, Framebuffer.Height, Front.Pixels, Game.Display.Front.Palette.Rgb);
}
