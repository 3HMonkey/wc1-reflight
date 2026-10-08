using System.Globalization;

namespace WingCommander.Game.Config;

/// <summary>
/// The three persisted values the Kilrathi Saga build kept in the registry
/// (HKLM\Software\Origin Systems\WC: Kilrathi Saga): music and SFX volume (0..20,
/// default 20) and the developer "Cheater" flag. Stored like the SDL port's
/// <c>wc1-modern.cfg</c>: <c>MusicVolume=%u\nSFXVolume=%u\nCheater=%u\n</c>. If the file does
/// not parse completely, none of the values count as present.
/// </summary>
/// <remarks>C: LoadVolumeSettingsFromRegistry / SaveVolumeSettingsToRegistry (sound.c),
/// ReadCheaterFlagFromRegistry (gr.c), registry emulation (sdl/registry.c).</remarks>
public sealed class GameSettings
{
    public const string FileName = "wc1.cfg";
    public const int DefaultVolume = 0x14;

    private readonly string? _path;
    private bool _present;

    private GameSettings(string? path)
    {
        _path = path;
    }

    /// <remarks>C: nMusicVolumeSetting (0..20; the mixer uses anVolumeLevels[v / 2]).</remarks>
    public int MusicVolume { get; set; }

    /// <remarks>C: nSfxVolumeSetting.</remarks>
    public int SfxVolume { get; set; }

    /// <summary>Developer flag: unlocks Origin switches, invulnerability, no collision response.</summary>
    public bool Cheater { get; set; }

    /// <summary>
    /// Port setting: the flight key help (F10, ADR-013) is shown. Stored as an optional fourth
    /// line <c>KeyHelp=0</c> only when it is off, so the original three-line format stays valid.
    /// </summary>
    public bool KeyHelp { get; set; } = true;

    /// <summary>
    /// Loads the settings file in <paramref name="directory"/>; missing volumes default to 20
    /// and are written back immediately, exactly like the registry code.
    /// </summary>
    public static GameSettings Load(string? directory)
    {
        var settings = new GameSettings(directory is null ? null : Path.Combine(directory, FileName));
        if (settings._path is not null && File.Exists(settings._path))
        {
            try
            {
                settings.Parse(File.ReadAllText(settings._path));
            }
            catch (IOException)
            {
                // treated like a missing file
            }
        }
        if (!settings._present)
        {
            settings.MusicVolume = DefaultVolume;
            settings.SfxVolume = DefaultVolume;
            settings.Cheater = false;
            settings.Save();
        }
        return settings;
    }

    /// <summary>Parses the three lines; all of them must be present and numeric.</summary>
    public static GameSettings FromText(string text)
    {
        var settings = new GameSettings(null);
        settings.Parse(text);
        if (!settings._present)
        {
            settings.MusicVolume = DefaultVolume;
            settings.SfxVolume = DefaultVolume;
        }
        return settings;
    }

    public string ToText() => string.Create(CultureInfo.InvariantCulture,
        $"MusicVolume={(uint)MusicVolume}\nSFXVolume={(uint)SfxVolume}\nCheater={(Cheater ? 1u : 0u)}\n{(KeyHelp ? "" : "KeyHelp=0\n")}");

    /// <summary>Writes the file (the original wrote on every Ctrl+arrow volume change).</summary>
    public void Save()
    {
        if (_path is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, ToText());
        }
        catch (IOException)
        {
            // Settings are best effort; the game keeps running with the in-memory values.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void Parse(string text)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length < 3)
            return;
        if (TryValue(lines[0], "MusicVolume=", out uint music) &&
            TryValue(lines[1], "SFXVolume=", out uint sfx) &&
            TryValue(lines[2], "Cheater=", out uint cheater))
        {
            MusicVolume = (int)music;
            SfxVolume = (int)sfx;
            Cheater = cheater != 0;
            _present = true;
            if (lines.Length > 3 && TryValue(lines[3], "KeyHelp=", out uint keyHelp))
                KeyHelp = keyHelp != 0;
        }
    }

    private static bool TryValue(string line, string prefix, out uint value)
    {
        value = 0;
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        return uint.TryParse(line.AsSpan(prefix.Length).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
