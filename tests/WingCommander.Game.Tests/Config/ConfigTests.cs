using WingCommander.Game.Config;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Config;

public class ConfigTests
{
    [Fact]
    public void Settings_round_trip_in_the_sdl_port_format()
    {
        var settings = GameSettings.FromText("MusicVolume=7\nSFXVolume=12\nCheater=1\n");
        Assert.Equal(7, settings.MusicVolume);
        Assert.Equal(12, settings.SfxVolume);
        Assert.True(settings.Cheater);
        Assert.Equal("MusicVolume=7\nSFXVolume=12\nCheater=1\n", settings.ToText());
    }

    [Fact]
    public void Incomplete_settings_fall_back_to_defaults()
    {
        var settings = GameSettings.FromText("MusicVolume=7\nSFXVolume=12\n");
        Assert.Equal(20, settings.MusicVolume);
        Assert.Equal(20, settings.SfxVolume);
        Assert.False(settings.Cheater);
    }

    [Fact]
    public void Missing_settings_file_is_created_with_defaults()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wc1-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = GameSettings.Load(dir);
            Assert.Equal(20, settings.MusicVolume);
            Assert.True(File.Exists(Path.Combine(dir, GameSettings.FileName)));
            settings.MusicVolume = 5;
            settings.Save();
            Assert.Equal(5, GameSettings.Load(dir).MusicVolume);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Last_argument_is_dropped_like_the_original()
    {
        var combined = StartupOptions.CombineArguments(["v", "a904", "z"], []);
        Assert.Equal(["v", "a904"], combined);
    }

    [Fact]
    public void Gog_config_selects_adlib_and_vga()
    {
        var options = new StartupOptions();
        options.ApplyArguments(StartupOptions.CombineArguments(["v", "a904", "z"], []));
        Assert.Equal(2, options.MusicPlaybackMode);
        Assert.Equal(904, options.ArcadeStartupParameter);
        Assert.Equal(0, options.GraphicsVariant);
    }

    [Fact]
    public void Dev_switches_need_the_origin_unlock()
    {
        var locked = new StartupOptions();
        locked.ApplyArguments(["-k", "s3", "m2", "l"]);
        Assert.True(locked.PlayerVulnerable);
        Assert.Equal(1, locked.Series);
        Assert.False(locked.LaunchMission);

        var unlocked = new StartupOptions();
        unlocked.ApplyArguments(["Origin", "-k", "s3", "m2", "l"]);
        Assert.False(unlocked.PlayerVulnerable);
        Assert.Equal(3, unlocked.Series);
        Assert.Equal(2, unlocked.Mission);
        Assert.True(unlocked.LaunchMission);
        Assert.True(unlocked.CampaignActive);
    }

    [Fact]
    public void Launcher_config_flags_apply_without_unlock()
    {
        var options = new StartupOptions();
        options.ApplyLauncherConfig(cheater: false, ["-c", "f", "$#SAGA.EXE"]);
        Assert.False(options.CockpitEnabled);
        Assert.True(options.ShowFrameRate);
        Assert.True(options.ShowKilrathiSagaCredits);
    }

    [Fact]
    public void Atoi_matches_c_semantics()
    {
        Assert.Equal(904, StartupOptions.Atoi("a904", 1));
        Assert.Equal(-12, StartupOptions.Atoi("as-12x", 2));
        Assert.Equal(0, StartupOptions.Atoi("a", 1));
        Assert.Equal(7, StartupOptions.Atoi("m 7", 1));
    }

    [DataFact]
    public void Reads_the_gog_config_file()
    {
        // The GOG file ends with CR LF NUL: C reads the NUL as a fourth, empty token.
        var tokens = StartupOptions.ReadConfigTokens(GameData.Require());
        Assert.Equal(["v", "a904", "z", ""], tokens);
        var options = new StartupOptions();
        options.ApplyArguments(StartupOptions.CombineArguments(tokens, []));
        Assert.True(options.UnknownZFlag);
        Assert.Equal(2, options.MusicPlaybackMode);
    }
}
