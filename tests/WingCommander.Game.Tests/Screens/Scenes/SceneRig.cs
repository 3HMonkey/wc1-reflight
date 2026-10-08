using WingCommander.Core.Imaging;
using WingCommander.Core.Video;
using WingCommander.Game.Screens.Scenes;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Screens.Scenes;

/// <summary>
/// A headless game prepared for the cutscenes: a campaign (CAMP data loaded for the chosen data
/// set) and the scene director. PNG snapshots are written only when the environment variable
/// WC1_SCENE_PNG_DIR names a directory (never into the repository).
/// </summary>
internal sealed class SceneRig
{
    /// <summary>
    /// The scene tests run one at a time: the shared <see cref="GameData"/> directory caches its packets
    /// in a plain dictionary.
    /// </summary>
    public const string Collection = "Scenes";

    public SceneRig(int campaign = 0, bool audio = false)
    {
        Screen = new ScreenRig(audio, copySaves: false);
        Game.Session.LoadCampaignData(GameData.Require(), campaign);
        Game.Session.State.CampaignIndex = (short)campaign;
        Game.Session.CampaignActive = true;
    }

    public ScreenRig Screen { get; }

    public Wc1Game Game => Screen.Game;

    public SceneDirector Director => Game.Screens.Director;

    /// <summary>Directory for looking at frames while porting (null: no snapshots).</summary>
    public static string? SnapshotDirectory => Environment.GetEnvironmentVariable("WC1_SCENE_PNG_DIR");

    /// <summary>Runs <paramref name="scene"/> as the root coroutine (after Wc1Game.Initialize).</summary>
    public void Start(Func<SceneDirector, Task> scene) => Screen.Start(game => scene(game.Screens.Director));

    /// <summary>Runs <paramref name="scene"/> on a director that reads <paramref name="outcome"/> as the flown mission's results.</summary>
    public SceneDirector Start(FakeOutcome outcome, Func<SceneDirector, Task> scene)
    {
        var director = new SceneDirector(Game, outcome);
        Screen.Start(_ => scene(director));
        return director;
    }

    /// <summary>Advances to <paramref name="milliseconds"/>; true when the scene has ended.</summary>
    public bool RunUntil(double milliseconds) => Screen.Runtime.RunHeadless(milliseconds);

    /// <summary>Runs until <paramref name="limitMilliseconds"/>, writing a snapshot every <paramref name="everyMilliseconds"/>.</summary>
    public bool RunWithSnapshots(string name, double limitMilliseconds, double everyMilliseconds, int zoom = 2)
    {
        if (SnapshotDirectory is null)
            return RunUntil(limitMilliseconds);
        double t = 0;
        int index = 0;
        while (t < limitMilliseconds)
        {
            t += everyMilliseconds;
            bool done = RunUntil(t);
            Snapshot($"{name}-{index++:000}-{(int)t:000000}ms", zoom);
            if (done)
                return true;
        }
        return false;
    }

    /// <summary>Writes one snapshot (when snapshots are enabled), optionally enlarged for inspection.</summary>
    public void Snapshot(string name, int zoom = 1)
    {
        if (SnapshotDirectory is not { } directory)
            return;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name + ".png");
        if (zoom <= 1)
        {
            Screen.SaveFront(path);
            return;
        }
        var front = Screen.Front;
        int width = Framebuffer.Width * zoom, height = Framebuffer.Height * zoom;
        var pixels = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            var row = front.Row(y / zoom);
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = row[x / zoom];
        }
        Png.WriteIndexed(path, width, height, pixels, Game.Display.Front.Palette.Rgb);
    }

    /// <summary>Presses Space every <paramref name="intervalMilliseconds"/> from <paramref name="start"/> to <paramref name="end"/>.</summary>
    public void PressSpaceEvery(double start, double end, double intervalMilliseconds)
    {
        for (double t = start; t < end; t += intervalMilliseconds)
            Screen.Key(t, 0x39, 0x20);
    }
}
