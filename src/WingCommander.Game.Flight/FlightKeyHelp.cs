using WingCommander.Core.Rendering;

namespace WingCommander.Game.Flight;

/// <summary>
/// The key reference shown during flight (ADR-013, port addition): the controls of
/// <c>HandleSpaceFlightControls</c>, <c>process_player_input</c> and <c>player_input</c> as the
/// port implements them (docs/analysis/flight-ui.md §2). Labels are kept short so the reference
/// fits the side margins of a 16:9 window at a readable size. The training simulator skips the
/// first key table (views, comm, autopilot, eject, wingman orders, video, message speed, nav map).
/// </summary>
public static class FlightKeyHelp
{
    public const string Title = "CONTROLS (F10)";

    private static readonly KeyHelpSection Steering = new("FLIGHT",
    [
        new("Arrows", "Steer"),
        new("Shift", "Steer hard"),
        new("Ins Del", "Roll"),
        new("Num 5", "Centre"),
        new("+ -", "Throttle"),
        new("\\", "Full speed"),
        new("Bksp", "Stop"),
        new("Tab", "Afterburner"),
    ]);

    private static readonly KeyHelpSection Navigation = new("NAVIGATION",
    [
        new("A", "Autopilot"),
        new("N", "Nav, map"),
    ]);

    private static readonly KeyHelpSection Weapons = new("WEAPONS",
    [
        new("Space", "Guns"),
        new("Enter", "Missile"),
        new("G", "Select gun"),
        new("W", "Select missile"),
        new("L", "Lock target"),
        new("T", "Target, next"),
    ]);

    private static readonly KeyHelpSection Displays = new("DISPLAYS",
    [
        new("D", "Damage"),
        new("C", "Comm, 1-9"),
        new("V", "Video on/off"),
        new("M", "Message speed"),
    ]);

    private static readonly KeyHelpSection TrainingDisplays = new("DISPLAYS",
    [
        new("D", "Damage"),
    ]);

    private static readonly KeyHelpSection Wingman = new("WINGMAN",
    [
        new("H", "Form up"),
        new("B", "Break, attack"),
    ]);

    private static readonly KeyHelpSection Views = new("VIEWS",
    [
        new("F1", "Cockpit"),
        new("F2 F3 F4", "Left right rear"),
        new("F5", "Chase"),
        new("F6", "Battle"),
        new("F7", "Target"),
        new("F8", "Missile cam"),
        new("F9", "Next ship"),
    ]);

    private static readonly KeyHelpSection Mouse = new("MOUSE",
    [
        new("Left", "Guns"),
        new("Both", "Missile"),
        new("Right x2", "Afterburner"),
        new("Right+move", "Roll, throttle"),
    ]);

    private static readonly KeyHelpSection Game = new("GAME",
    [
        new("P", "Pause"),
        new("Ctrl+E", "Eject"),
        new("Ctrl+S", "Sound"),
        new("Ctrl+M", "Music"),
        new("Alt+X", "Quit"),
        new("F10", "Hide help"),
    ]);

    private static readonly KeyHelpSection TrainingGame = new("SIMULATOR",
    [
        new("P", "Pause"),
        new("Esc", "End"),
        new("Ctrl+S", "Sound"),
        new("Ctrl+M", "Music"),
        new("F10", "Hide help"),
    ]);

    /// <summary>Campaign flight.</summary>
    public static IReadOnlyList<KeyHelpSection> Campaign { get; } =
        [Steering, Navigation, Weapons, Displays, Wingman, Views, Mouse, Game];

    /// <summary>The training simulator.</summary>
    public static IReadOnlyList<KeyHelpSection> TrainingSimulator { get; } =
        [Steering, Weapons, TrainingDisplays, Mouse, TrainingGame];
}
