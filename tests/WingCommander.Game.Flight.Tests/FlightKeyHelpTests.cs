namespace WingCommander.Game.Flight.Tests;

public class FlightKeyHelpTests
{
    private static string? Action(bool trainingSimulator, bool escapeOpensMenu, string key) =>
        FlightKeyHelp.For(trainingSimulator, escapeOpensMenu)
            .SelectMany(section => section.Entries)
            .Where(entry => entry.Keys == key)
            .Select(entry => (string?)entry.Action)
            .FirstOrDefault();

    [Theory]
    [InlineData(false, true, "Menu")]
    [InlineData(false, false, null)]
    [InlineData(true, true, "Menu, end")]
    [InlineData(true, false, "End")]
    public void Esc_is_listed_as_the_flight_handles_it(bool trainingSimulator, bool escapeOpensMenu, string? expected) =>
        Assert.Equal(expected, Action(trainingSimulator, escapeOpensMenu, "Esc"));

    private static string? KeysOf(IReadOnlyList<Core.Rendering.KeyHelpSection> help, string action) =>
        help.SelectMany(section => section.Entries)
            .Where(entry => entry.Action == action)
            .Select(entry => (string?)entry.Keys)
            .FirstOrDefault();

    [Fact]
    public void The_help_shows_the_players_keys()
    {
        var defaults = FlightKeyHelp.For(false, true);
        Assert.Equal("Arrows", KeysOf(defaults, "Steer"));
        Assert.Equal("Space", KeysOf(defaults, "Guns"));
        Assert.Equal("+ -", KeysOf(defaults, "Throttle"));
        Assert.Equal("F2 F3 F4", KeysOf(defaults, "Left right rear"));

        var bindings = new Input.KeyBindings();
        bindings.Bind(Input.FlightAction.SteerUp, 0x11);   // W (select missile takes Up)
        bindings.Bind(Input.FlightAction.SteerDown, 0x1f); // S
        bindings.Bind(Input.FlightAction.SteerLeft, 0x1e); // A (autopilot takes Left)
        bindings.Bind(Input.FlightAction.SteerRight, 0x20); // D (damage display takes Right)
        bindings.Bind(Input.FlightAction.FireGuns, 0x21);  // F
        var help = FlightKeyHelp.For(false, true, bindings);
        Assert.Equal("W S A D", KeysOf(help, "Steer"));
        Assert.Equal("F", KeysOf(help, "Guns"));
        Assert.Equal("Up", KeysOf(help, "Select missile"));
        Assert.Equal("Left", KeysOf(help, "Autopilot"));
        Assert.Equal("Right", KeysOf(help, "Damage"));
        Assert.Equal("F", KeysOf(FlightKeyHelp.For(true, true, bindings), "Guns"));
    }
}
