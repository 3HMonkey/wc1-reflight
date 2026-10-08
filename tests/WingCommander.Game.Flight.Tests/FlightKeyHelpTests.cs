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
}
