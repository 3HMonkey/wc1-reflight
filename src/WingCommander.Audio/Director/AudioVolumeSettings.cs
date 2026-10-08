using System.Globalization;

namespace WingCommander.Audio.Director;

/// <summary>
/// The two volume settings (0..20, displayed as setting / 2) and their persistence. The
/// original stores them as DWORDs <c>MusicVolume</c>/<c>SFXVolume</c> under
/// <c>HKLM\Software\Origin Systems\WC: Kilrathi Saga</c>; the port uses a small text file with
/// the same value names in the host's user data directory.
/// </summary>
/// <remarks>C: nSfxVolumeSetting, nMusicVolumeSetting, anVolumeLevels (globals.c),
/// LoadVolumeSettingsFromRegistry, SaveVolumeSettingsToRegistry (sound.c).</remarks>
public sealed class AudioVolumeSettings
{
    /// <summary>Largest setting.</summary>
    public const int MaxSetting = 20;

    /// <summary>Default setting (0x14).</summary>
    public const int DefaultSetting = 20;

    /// <summary>Suggested file name inside <c>IGameHost.UserDataDirectory</c>.</summary>
    public const string DefaultFileName = "volume.cfg";

    private int _sfxVolume = DefaultSetting;
    private int _musicVolume = DefaultSetting;

    /// <summary>Volume levels (0..64000) indexed by setting / 2.</summary>
    /// <remarks>C: anVolumeLevels (0x469fc8).</remarks>
    public static ReadOnlySpan<int> VolumeLevels => VolumeLevelTable;

    private static readonly int[] VolumeLevelTable = [0, 40000, 50000, 55000, 60000, 61000, 61500, 62000, 63000, 63500, 64000];

    /// <summary>Sound-effect volume setting 0..20.</summary>
    /// <remarks>C: nSfxVolumeSetting.</remarks>
    public int SfxVolume
    {
        get => _sfxVolume;
        set => _sfxVolume = Math.Clamp(value, 0, MaxSetting);
    }

    /// <summary>Music volume setting 0..20.</summary>
    /// <remarks>C: nMusicVolumeSetting.</remarks>
    public int MusicVolume
    {
        get => _musicVolume;
        set => _musicVolume = Math.Clamp(value, 0, MaxSetting);
    }

    /// <summary>The level the game passes to SetSoundEffectsVolume: anVolumeLevels[setting / 2].</summary>
    public int SfxLevel => VolumeLevels[_sfxVolume / 2];

    /// <summary>The level the game passes to SetMusicStreamVolume.</summary>
    public int MusicLevel => VolumeLevels[_musicVolume / 2];

    /// <summary>
    /// Ctrl+Up/Down (SFX) or Ctrl+Left/Right (music) step: clamps to 0..20 and returns the
    /// value the game displays ("SFX VOLUME: n.").
    /// </summary>
    /// <remarks>C: the volume hotkeys of main.c (cases 0x48/0x50/0x4b/0x4d).</remarks>
    public int StepSfxVolume(int delta)
    {
        SfxVolume = _sfxVolume + delta;
        return _sfxVolume / 2;
    }

    /// <inheritdoc cref="StepSfxVolume"/>
    public int StepMusicVolume(int delta)
    {
        MusicVolume = _musicVolume + delta;
        return _musicVolume / 2;
    }

    /// <summary>Ctrl+S: toggles the effects between 0 and 20; returns the displayed value.</summary>
    /// <remarks>C: hudmsg.c case 0x1f.</remarks>
    public int ToggleSfxVolume()
    {
        _sfxVolume = _sfxVolume == 0 ? MaxSetting : 0;
        return _sfxVolume / 2;
    }

    /// <summary>Ctrl+M: toggles the music between 0 and 20; returns the displayed value.</summary>
    /// <remarks>C: hudmsg.c case 0x32.</remarks>
    public int ToggleMusicVolume()
    {
        _musicVolume = _musicVolume == 0 ? MaxSetting : 0;
        return _musicVolume / 2;
    }

    /// <summary>
    /// Applies the stored text (<c>MusicVolume=n</c> / <c>SFXVolume=n</c> lines). Values that
    /// are missing get the default 20, like the registry version; returns false when at least
    /// one value was missing (the caller should then write the settings back).
    /// </summary>
    /// <remarks>C: LoadVolumeSettingsFromRegistry (value part).</remarks>
    public bool ApplyText(string? text)
    {
        bool haveMusic = false;
        bool haveSfx = false;
        foreach (string rawLine in (text ?? "").Split('\n'))
        {
            string line = rawLine.Trim();
            int equals = line.IndexOf('=');
            if (equals <= 0)
                continue;
            string name = line[..equals].Trim();
            if (!int.TryParse(line[(equals + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                continue;
            if (name.Equals("MusicVolume", StringComparison.OrdinalIgnoreCase))
            {
                MusicVolume = value;
                haveMusic = true;
            }
            else if (name.Equals("SFXVolume", StringComparison.OrdinalIgnoreCase))
            {
                SfxVolume = value;
                haveSfx = true;
            }
        }
        if (!haveMusic)
            MusicVolume = DefaultSetting;
        if (!haveSfx)
            SfxVolume = DefaultSetting;
        return haveMusic && haveSfx;
    }

    /// <summary>The persisted form of both settings.</summary>
    /// <remarks>C: SaveVolumeSettingsToRegistry (value part).</remarks>
    public string ToText() =>
        string.Create(CultureInfo.InvariantCulture, $"MusicVolume={_musicVolume}\nSFXVolume={_sfxVolume}\n");

    /// <summary>
    /// Loads the settings from a file (startup only); missing values get the default 20 and
    /// the file is rewritten. An unreadable file keeps the current values.
    /// </summary>
    /// <remarks>C: LoadVolumeSettingsFromRegistry (0x42B870, sound.c).</remarks>
    public void Load(string path)
    {
        string? text;
        try
        {
            text = File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        if (!ApplyText(text))
            Save(path);
    }

    /// <summary>
    /// Writes both settings (a few bytes; called on volume hotkeys, never per tick). I/O errors
    /// are ignored like the registry calls.
    /// </summary>
    /// <remarks>C: SaveVolumeSettingsToRegistry (0x42B930, sound.c).</remarks>
    public void Save(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, ToText());
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
