using WingCommander.Audio.Director;

namespace WingCommander.Audio.Tests.Director;

public class AudioVolumeSettingsTests
{
    [Fact]
    public void Defaults_AreFullVolume()
    {
        var settings = new AudioVolumeSettings();
        Assert.Equal(20, settings.SfxVolume);
        Assert.Equal(20, settings.MusicVolume);
        Assert.Equal(64000, settings.SfxLevel);
        Assert.Equal(64000, settings.MusicLevel);
    }

    [Fact]
    public void Steps_ClampAndReportHalfTheSetting()
    {
        var settings = new AudioVolumeSettings();
        Assert.Equal(10, settings.StepSfxVolume(1));
        Assert.Equal(9, settings.StepSfxVolume(-1));
        Assert.Equal(19, settings.SfxVolume);
        for (int i = 0; i < 30; i++)
            settings.StepMusicVolume(-1);
        Assert.Equal(0, settings.MusicVolume);
        Assert.Equal(0, settings.MusicLevel);
        settings.MusicVolume = 3;
        Assert.Equal(40000, settings.MusicLevel);
    }

    [Fact]
    public void Toggles_SwitchBetweenZeroAndTwenty()
    {
        var settings = new AudioVolumeSettings { SfxVolume = 7 };
        Assert.Equal(0, settings.ToggleSfxVolume());
        Assert.Equal(10, settings.ToggleSfxVolume());
        Assert.Equal(0, settings.ToggleMusicVolume());
        Assert.Equal(10, settings.ToggleMusicVolume());
    }

    [Fact]
    public void Text_RoundTrips_AndMissingValuesDefault()
    {
        var settings = new AudioVolumeSettings { SfxVolume = 4, MusicVolume = 11 };
        var copy = new AudioVolumeSettings();
        Assert.True(copy.ApplyText(settings.ToText()));
        Assert.Equal(4, copy.SfxVolume);
        Assert.Equal(11, copy.MusicVolume);

        Assert.False(copy.ApplyText("MusicVolume=5"));
        Assert.Equal(5, copy.MusicVolume);
        Assert.Equal(20, copy.SfxVolume);

        Assert.True(copy.ApplyText("sfxvolume = 99\r\nMUSICVOLUME=-4\r\n"));
        Assert.Equal(20, copy.SfxVolume);
        Assert.Equal(0, copy.MusicVolume);
    }

    [Fact]
    public void LoadAndSave_UseASmallTextFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "wc1-audio-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, AudioVolumeSettings.DefaultFileName);
        try
        {
            var settings = new AudioVolumeSettings { SfxVolume = 2, MusicVolume = 2 };
            settings.Load(path);
            Assert.Equal(20, settings.SfxVolume);
            Assert.True(File.Exists(path));

            settings.SfxVolume = 6;
            settings.MusicVolume = 12;
            settings.Save(path);
            var loaded = new AudioVolumeSettings();
            loaded.Load(path);
            Assert.Equal(6, loaded.SfxVolume);
            Assert.Equal(12, loaded.MusicVolume);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
