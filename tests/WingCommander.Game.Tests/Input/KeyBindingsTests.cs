using WingCommander.Core.Resources;
using WingCommander.Game.Config;
using WingCommander.Game.Input;

namespace WingCommander.Game.Tests.Input;

public class KeyBindingsTests
{
    private const int F = 0x21, Q = 0x10, Space = 0x39, Tab = 0x0f;

    [Fact]
    public void Defaults_are_the_original_keys_and_can_all_be_bound()
    {
        var bindings = new KeyBindings();
        Assert.True(bindings.IsDefault);
        Assert.Equal(Enum.GetValues<FlightAction>().Length, KeyBindings.Actions.Count);
        Assert.Equal(KeyBindings.Actions.Count, KeyBindings.Actions.Select(a => a.Id).Distinct().Count());
        Assert.Equal(KeyBindings.Actions.Count, KeyBindings.Actions.Select(a => a.DefaultKey).Distinct().Count());
        for (int i = 0; i < KeyBindings.Actions.Count; i++)
        {
            var info = KeyBindings.Actions[i];
            Assert.Equal((FlightAction)i, info.Action);
            Assert.Equal(info.DefaultKey, bindings[info.Action]);
            Assert.True(KeyBindings.CanBind(info.DefaultKey), info.Name);
        }
        Assert.Equal(Space, bindings[FlightAction.FireGuns]);
    }

    [Fact]
    public void A_key_another_control_has_is_swapped()
    {
        var bindings = new KeyBindings();
        int version = bindings.Version;
        Assert.Equal(FlightAction.Afterburner, bindings.Bind(FlightAction.FireGuns, Tab));
        Assert.Equal(Tab, bindings[FlightAction.FireGuns]);
        Assert.Equal(Space, bindings[FlightAction.Afterburner]);
        Assert.NotEqual(version, bindings.Version);
        Assert.Null(bindings.Bind(FlightAction.Pause, F)); // a free key
        Assert.False(bindings.IsDefault);
        bindings.Reset();
        Assert.True(bindings.IsDefault);
    }

    [Theory]
    [InlineData(0x01, false)] // Esc: pause menu
    [InlineData(0x02, false)] // 1..9: communication choices
    [InlineData(0x0a, false)]
    [InlineData(0x0b, true)]  // 0
    [InlineData(0x1d, false)] // Ctrl
    [InlineData(0x2a, false)] // Shift
    [InlineData(0x38, false)] // Alt
    [InlineData(0x44, false)] // F10: key help
    [InlineData(0x57, true)]  // F11
    [InlineData(0x21, true)]  // F
    [InlineData(0x60, false)] // no key
    public void Reserved_keys_cannot_be_bound(int scanCode, bool bindable)
    {
        Assert.Equal(bindable, KeyBindings.CanBind(scanCode));
        if (!bindable)
            Assert.Throws<ArgumentException>(() => new KeyBindings().Bind(FlightAction.FireGuns, scanCode));
    }

    [Fact]
    public void The_translation_moves_bound_keys_and_silences_the_old_ones()
    {
        var bindings = new KeyBindings();
        Assert.All(Enumerable.Range(0, GameKeys.Count), k => Assert.Equal(KeyTranslation.Unchanged, new KeyTranslation(bindings).Translate(k)));

        bindings.Bind(FlightAction.FireGuns, F);
        bindings.Bind(FlightAction.RollLeft, Q);
        var translation = new KeyTranslation(bindings);
        Assert.Equal(Space, translation.Translate(F));
        Assert.Equal(KeyTranslation.Blocked, translation.Translate(Space));
        Assert.Equal(0x52, translation.Translate(Q));                           // Q rolls left like Ins
        Assert.Equal(KeyTranslation.Blocked, translation.Translate(0x52));      // Ins
        Assert.Equal(KeyTranslation.Blocked, translation.Translate(0x33));      // and the comma
        Assert.Equal(KeyTranslation.Unchanged, translation.Translate(0x53));    // roll right keeps Del
        Assert.Equal(KeyTranslation.Unchanged, translation.Translate(0x34));    // and the period
        Assert.Equal(KeyTranslation.Unchanged, translation.Translate(0x12));    // E (Ctrl+E ejects)
        Assert.Equal(KeyTranslation.Unchanged, translation.Translate(200));
    }

    [Fact]
    public void Swapped_keys_translate_both_ways()
    {
        var bindings = new KeyBindings();
        bindings.Bind(FlightAction.FireGuns, Tab);
        var translation = new KeyTranslation(bindings);
        Assert.Equal(Space, translation.Translate(Tab));
        Assert.Equal(Tab, translation.Translate(Space));
        Assert.Equal(KeyTranslation.Blocked, translation.Translate(0x37)); // Num *, the afterburner's second key
    }

    [Theory]
    [InlineData("Space", 0x39)]
    [InlineData("space", 0x39)]
    [InlineData("Num 5", 0x4c)]
    [InlineData("num5", 0x4c)]
    [InlineData("\\", 0x2b)]
    [InlineData("Backslash", 0x2b)]
    [InlineData("F12", 0x58)]
    [InlineData("q", 0x10)]
    [InlineData("PgDn", 0x51)]
    public void Keys_are_found_by_identifier_or_name(string text, int scanCode)
    {
        Assert.True(GameKeys.TryParse(text, out int parsed));
        Assert.Equal(scanCode, parsed);
        Assert.True(GameKeys.TryParse(GameKeys.Id(scanCode), out int byId));
        Assert.Equal(scanCode, byId);
    }

    [Fact]
    public void Unknown_key_names_are_rejected() => Assert.False(GameKeys.TryParse("Joystick", out _));

    [Fact]
    public void Bindings_round_trip_through_config_json()
    {
        string file = Path.Combine(Path.GetTempPath(), "wc1-keys-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, """
                { "controls": { "fireGuns": "F", "afterburner": "Space", "pause": "Escape", "lockTarget": "Joystick", "nonsense": "A" } }
                """);
            var settings = UserSettings.Read(GameConfiguration.Load(file));
            Assert.Equal(F, settings.Controls[FlightAction.FireGuns]);
            Assert.Equal(Space, settings.Controls[FlightAction.Afterburner]);
            Assert.Equal(0x19, settings.Controls[FlightAction.Pause]);        // Esc cannot be bound
            Assert.Equal(0x26, settings.Controls[FlightAction.LockTarget]);   // unknown key

            var configuration = GameConfiguration.Load(file);
            settings.Write(configuration);
            configuration.Save();
            var reloaded = UserSettings.Read(GameConfiguration.Load(file));
            foreach (var info in KeyBindings.Actions)
                Assert.Equal(settings.Controls[info.Action], reloaded.Controls[info.Action]);
            Assert.True(GameConfiguration.Load(file).TryGetString("controls", "fireGuns", out string guns));
            Assert.Equal("F", guns);

            var copy = settings.Clone();
            copy.Controls.Reset();
            Assert.Equal(F, settings.Controls[FlightAction.FireGuns]); // the clone has its own bindings
        }
        finally
        {
            File.Delete(file);
        }
    }
}
