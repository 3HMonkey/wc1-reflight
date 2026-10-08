using WingCommander.Core.Rendering;
using WingCommander.Core.Resources;
using WingCommander.Game.Input;

namespace WingCommander.Game.Config;

/// <summary>
/// Lets the settings menu change the presentation at runtime: the renderer's live settings (read
/// every frame) and the window's fullscreen state. Implemented by the executable; null in headless
/// runs, where the menu leaves the display rows out.
/// </summary>
public interface IDisplayControl
{
    /// <summary>Filter, aspect, integer scaling and vsync of the running renderer.</summary>
    RendererSettings Renderer { get; }

    bool Fullscreen { get; set; }
}

/// <summary>
/// The player's settings (ADR-015), changed in the pause menu and stored in <c>config.json</c>:
/// <code>
/// "audio":     { "musicVolume": 10, "soundVolume": 10 }           0..10
/// "display":   { "fullscreen": false, "filter": "sharp", "aspect": "4:3", "integerScaling": false, "vsync": true }
/// "text":      { "sharp": true, "modernFonts": true }
/// "interface": { "keyHelp": true }
/// "controls":  { "fireGuns": "Space", "steerUp": "Up", ... }          key bindings (ADR-016)
/// </code>
/// Missing values keep their defaults; values of the wrong type are ignored.
/// </summary>
public sealed class UserSettings
{
    /// <summary>Largest volume step shown in the menu (the game's settings are twice this).</summary>
    public const int MaxVolume = 10;

    public int MusicVolume { get; set; } = MaxVolume;

    public int SoundVolume { get; set; } = MaxVolume;

    public bool Fullscreen { get; set; }

    public ScalingFilter Filter { get; set; } = ScalingFilter.SharpBilinear;

    public AspectMode Aspect { get; set; } = AspectMode.FourByThree;

    public bool IntegerScaling { get; set; }

    public bool VSync { get; set; } = true;

    /// <summary>Text at output resolution (when the renderer supports it).</summary>
    public bool SharpText { get; set; } = true;

    /// <summary>The bundled replacement fonts instead of the vectorized originals.</summary>
    public bool ModernFonts { get; set; } = true;

    /// <summary>The flight key help (F10).</summary>
    public bool KeyHelp { get; set; } = true;

    /// <summary>The keys of the flight controls.</summary>
    public KeyBindings Controls { get; private set; } = new();

    /// <summary>Reads the settings from <paramref name="configuration"/> over the values of <paramref name="defaults"/>.</summary>
    public static UserSettings Read(GameConfiguration? configuration, UserSettings? defaults = null)
    {
        var settings = defaults?.Clone() ?? new UserSettings();
        if (configuration is null)
            return settings;
        if (configuration.TryGetInt("audio", "musicVolume", out int music))
            settings.MusicVolume = Math.Clamp(music, 0, MaxVolume);
        if (configuration.TryGetInt("audio", "soundVolume", out int sound))
            settings.SoundVolume = Math.Clamp(sound, 0, MaxVolume);
        if (configuration.TryGetBool("display", "fullscreen", out bool fullscreen))
            settings.Fullscreen = fullscreen;
        if (configuration.TryGetString("display", "filter", out string filter))
            settings.Filter = ParseFilter(filter) ?? settings.Filter;
        if (configuration.TryGetString("display", "aspect", out string aspect))
            settings.Aspect = ParseAspect(aspect) ?? settings.Aspect;
        if (configuration.TryGetBool("display", "integerScaling", out bool integer))
            settings.IntegerScaling = integer;
        if (configuration.TryGetBool("display", "vsync", out bool vsync))
            settings.VSync = vsync;
        if (configuration.TryGetBool("text", "sharp", out bool sharp))
            settings.SharpText = sharp;
        if (configuration.TryGetBool("text", "modernFonts", out bool modern))
            settings.ModernFonts = modern;
        if (configuration.TryGetBool("interface", "keyHelp", out bool keyHelp))
            settings.KeyHelp = keyHelp;
        settings.Controls.Read(id => configuration.TryGetString("controls", id, out string key) ? key : null);
        return settings;
    }

    /// <summary>Stores every setting in <paramref name="configuration"/> (call <see cref="GameConfiguration.Save"/> afterwards).</summary>
    public void Write(GameConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.SetInt("audio", "musicVolume", MusicVolume);
        configuration.SetInt("audio", "soundVolume", SoundVolume);
        configuration.SetBool("display", "fullscreen", Fullscreen);
        configuration.SetString("display", "filter", FilterName(Filter));
        configuration.SetString("display", "aspect", Aspect == AspectMode.SquarePixels ? "square" : "4:3");
        configuration.SetBool("display", "integerScaling", IntegerScaling);
        configuration.SetBool("display", "vsync", VSync);
        configuration.SetBool("text", "sharp", SharpText);
        configuration.SetBool("text", "modernFonts", ModernFonts);
        configuration.SetBool("interface", "keyHelp", KeyHelp);
        Controls.Write((id, key) => configuration.SetString("controls", id, key));
    }

    /// <summary>Copies the display settings into a renderer's settings.</summary>
    public void ApplyTo(RendererSettings renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        renderer.Filter = Filter;
        renderer.Aspect = Aspect;
        renderer.IntegerScaling = IntegerScaling;
        renderer.VSync = VSync;
    }

    /// <summary>Takes over the display settings currently in effect.</summary>
    public void CaptureFrom(IDisplayControl display)
    {
        ArgumentNullException.ThrowIfNull(display);
        Fullscreen = display.Fullscreen;
        Filter = display.Renderer.Filter;
        Aspect = display.Renderer.Aspect;
        IntegerScaling = display.Renderer.IntegerScaling;
        VSync = display.Renderer.VSync;
    }

    public UserSettings Clone()
    {
        var copy = (UserSettings)MemberwiseClone();
        copy.Controls = Controls.Clone();
        return copy;
    }

    public static string FilterName(ScalingFilter filter) => filter switch
    {
        ScalingFilter.Nearest => "nearest",
        ScalingFilter.Linear => "linear",
        _ => "sharp",
    };

    public static ScalingFilter? ParseFilter(string name) => name.ToLowerInvariant() switch
    {
        "nearest" => ScalingFilter.Nearest,
        "linear" or "smooth" => ScalingFilter.Linear,
        "sharp" => ScalingFilter.SharpBilinear,
        _ => null,
    };

    public static AspectMode? ParseAspect(string name) => name.ToLowerInvariant() switch
    {
        "4:3" => AspectMode.FourByThree,
        "square" or "16:10" => AspectMode.SquarePixels,
        _ => null,
    };
}
