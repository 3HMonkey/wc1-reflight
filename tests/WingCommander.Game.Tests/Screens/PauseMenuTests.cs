using WingCommander.Core.Platform;
using WingCommander.Core.Rendering;
using WingCommander.Core.Resources;
using WingCommander.Game.Config;
using WingCommander.Game.Screens.Ui;
using WingCommander.Graphics.Palettes;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Screens;

public sealed class PauseMenuTests
{
    private const int Esc = 0x01, Enter = 0x1c, Up = 0x48, Down = 0x50, Left = 0x4b, Right = 0x4d;

    private sealed class FakeDisplay : IDisplayControl
    {
        public RendererSettings Renderer { get; } = new();

        public bool Fullscreen { get; set; }
    }

    private static (ScreenRig Rig, Func<PauseMenuChoice?> Choice) Open(PauseMenuContext context, ScreenRig? rig = null)
    {
        rig ??= new ScreenRig();
        PauseMenuChoice? choice = null;
        rig.Start(async game =>
        {
            await game.Display.ClearViewportAsync(game.Graphics.Screen!, PaletteColours.DarkGrey);
            choice = await game.ShowPauseMenuAsync(context);
            await game.Display.PresentAsync();
        });
        return (rig, () => choice);
    }

    [DataFact]
    public void Esc_Resumes_AndThePictureComesBack()
    {
        var (rig, choice) = Open(PauseMenuContext.Menu);
        rig.Runtime.RunHeadless(300);
        Assert.Contains(rig.Front.Pixels, p => p == PaletteColours.Blue); // the panel frame
        rig.Key(400, Esc);
        rig.Runtime.RunHeadless(1500);
        Assert.Equal(PauseMenuChoice.Resume, choice());
        Assert.All(rig.Front.Pixels, p => Assert.Equal(PaletteColours.DarkGrey, p));
    }

    [DataFact]
    public void TrainingSimulator_OffersEndSimulation()
    {
        var (rig, choice) = Open(PauseMenuContext.TrainingSimulator);
        rig.Key(300, Down);
        rig.Key(500, Down);
        rig.Key(700, Enter);
        rig.Runtime.RunHeadless(2000);
        Assert.Equal(PauseMenuChoice.EndSimulation, choice());
    }

    [DataFact]
    public void MouseClickOnResume_Resumes()
    {
        var (rig, choice) = Open(PauseMenuContext.Menu);
        rig.Click(300, 160, 80); // first item row (top 52 + 26)
        rig.Runtime.RunHeadless(1500);
        Assert.Equal(PauseMenuChoice.Resume, choice());
    }

    [DataFact]
    public void Settings_ChangeTheVolume_AndSaveConfigJson()
    {
        string file = Path.Combine(Path.GetTempPath(), "wc1-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var configuration = GameConfiguration.CreateAt(file);
            var rig = new ScreenRig(options: new Wc1GameOptions { Audio = false, Configuration = configuration });
            var (_, choice) = Open(PauseMenuContext.Menu, rig);
            rig.Key(300, Down);   // Settings
            rig.Key(500, Enter);
            rig.Key(800, Left);   // music 10 -> 9
            rig.Key(1000, Left);  // -> 8
            rig.Key(1200, Down);  // sound
            rig.Key(1400, Left);  // sound 10 -> 9
            rig.Key(1600, Esc);   // back to the pause menu (saves)
            rig.Key(1900, Esc);   // resume
            rig.Runtime.RunHeadless(3000);
            Assert.Equal(PauseMenuChoice.Resume, choice());
            Assert.Equal(16, rig.Game.Volumes.MusicVolume);
            Assert.Equal(18, rig.Game.Volumes.SfxVolume);
            var saved = GameConfiguration.Load(file);
            Assert.True(saved.TryGetInt("audio", "musicVolume", out int music));
            Assert.Equal(8, music);
            Assert.True(saved.TryGetInt("audio", "soundVolume", out int sound));
            Assert.Equal(9, sound);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [DataFact]
    public void Settings_ChangeTheDisplay_ThroughTheControl()
    {
        var display = new FakeDisplay();
        var rig = new ScreenRig(options: new Wc1GameOptions { Audio = false, Display = display });
        var (_, choice) = Open(PauseMenuContext.Menu, rig);
        rig.Key(300, Down);   // Settings
        rig.Key(500, Enter);
        rig.Key(800, Down);   // sound
        rig.Key(1000, Down);  // fullscreen
        rig.Key(1200, Enter); // on
        rig.Key(1400, Down);  // filter
        rig.Key(1600, Right); // sharp -> nearest
        rig.Key(1800, Down);  // aspect
        rig.Key(2000, Enter); // square pixels
        rig.Key(2200, Esc);
        rig.Key(2500, Esc);
        rig.Runtime.RunHeadless(3500);
        Assert.Equal(PauseMenuChoice.Resume, choice());
        Assert.True(display.Fullscreen);
        Assert.Equal(ScalingFilter.Nearest, display.Renderer.Filter);
        Assert.Equal(AspectMode.SquarePixels, display.Renderer.Aspect);
        Assert.True(rig.Game.Preferences.Fullscreen);
        Assert.Equal(ScalingFilter.Nearest, rig.Game.Preferences.Filter);
    }

    [DataFact]
    public void QuitGame_AsksFirst_ThenEndsTheGame()
    {
        var (rig, _) = Open(PauseMenuContext.Menu);
        rig.Key(300, Up);     // wraps to "Quit game"
        rig.Key(500, Enter);
        rig.Key(1000, 0x15, 'Y'); // Y
        bool finished = rig.Runtime.RunHeadless(3000);
        Assert.True(finished);
        Assert.Null(rig.Runtime.Failure); // GameExitException is a normal end
    }

    [Fact]
    public void UserSettings_RoundTripThroughTheConfiguration()
    {
        string file = Path.Combine(Path.GetTempPath(), "wc1-user-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, "{ \"gameDirectory\": \"/games/wc1\", \"custom\": { \"keep\": 1 } }");
            var configuration = GameConfiguration.Load(file);
            var settings = UserSettings.Read(configuration);
            Assert.Equal(UserSettings.MaxVolume, settings.MusicVolume);
            settings.MusicVolume = 3;
            settings.Filter = ScalingFilter.Linear;
            settings.Aspect = AspectMode.SquarePixels;
            settings.KeyHelp = false;
            settings.Write(configuration);
            configuration.Save();
            var reloaded = GameConfiguration.Load(file);
            var read = UserSettings.Read(reloaded);
            Assert.Equal(3, read.MusicVolume);
            Assert.Equal(ScalingFilter.Linear, read.Filter);
            Assert.Equal(AspectMode.SquarePixels, read.Aspect);
            Assert.False(read.KeyHelp);
            Assert.True(reloaded.TryGetInt("custom", "keep", out int keep) && keep == 1);
            Assert.Equal(Path.GetFullPath("/games/wc1", Path.GetDirectoryName(file)!), reloaded.GameDirectory);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [DataFact]
    public void SharpTextAndFonts_SwitchAtRuntime()
    {
        var rig = new ScreenRig(options: new Wc1GameOptions
        {
            Audio = false,
            HighResolutionText = true,
            ReplacementFonts = BundledFonts.Load(),
        });
        rig.Start(async game =>
        {
            game.Graphics.DrawTextAt(game.DefaultText, 20, 30, "HELLO", 0);
            await game.Display.PresentAsync();
        });
        rig.Runtime.RunHeadless(300);
        var game = rig.Game;
        Assert.NotNull(rig.Runtime.Frame.Text);
        int generation = game.Glyphs!.Cache.Generation;
        game.SetModernFonts(false);
        Assert.NotEqual(generation, game.Glyphs.Cache.Generation); // cached glyphs rebuilt
        game.SetSharpText(false);
        Assert.Null(rig.Runtime.Frame.Text);
        game.SetSharpText(true);
        Assert.Same(game.Display.Text, rig.Runtime.Frame.Text);
    }

    [Fact]
    public void ExampleConfiguration_Parses()
    {
        string example = RepositoryAssets.InRepository("config.example.json");
        var configuration = GameConfiguration.Load(example);
        var settings = UserSettings.Read(configuration);
        Assert.NotNull(configuration.GameDirectory);
        Assert.Equal(UserSettings.MaxVolume, settings.MusicVolume);
        Assert.Equal(ScalingFilter.SharpBilinear, settings.Filter);
        Assert.True(settings.SharpText && settings.ModernFonts && settings.KeyHelp);
    }
}
