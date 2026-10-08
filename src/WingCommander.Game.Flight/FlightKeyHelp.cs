using WingCommander.Core.Rendering;
using WingCommander.Game.Input;
using static WingCommander.Game.Input.FlightAction;

namespace WingCommander.Game.Flight;

/// <summary>
/// The key reference shown during flight (ADR-013, port addition): the controls of
/// <c>HandleSpaceFlightControls</c>, <c>process_player_input</c> and <c>player_input</c> as the
/// port implements them (docs/analysis/flight-ui.md §2), with the player's keys (ADR-016). Labels
/// are kept short so the reference fits the side margins of a 16:9 window at a readable size.
/// The training simulator skips the first key table (views, comm, autopilot, eject, wingman
/// orders, video, message speed, nav map). Esc opens the pause menu (ADR-015) unless the original
/// behaviour is kept (--ks-literal).
/// </summary>
public static class FlightKeyHelp
{
    public const string Title = "CONTROLS (F10)";

    private static readonly KeyBindings Defaults = new();

    private static readonly KeyHelpSection Mouse = new("MOUSE",
    [
        new("Left", "Guns"),
        new("Both", "Missile"),
        new("Right x2", "Afterburner"),
        new("Right+move", "Roll, throttle"),
    ]);

    /// <summary>Campaign flight with the default keys.</summary>
    public static IReadOnlyList<KeyHelpSection> Campaign { get; } = For(false, true);

    /// <summary>Campaign flight with the original Esc (no pause menu).</summary>
    public static IReadOnlyList<KeyHelpSection> CampaignLiteral { get; } = For(false, false);

    /// <summary>The training simulator with the default keys.</summary>
    public static IReadOnlyList<KeyHelpSection> TrainingSimulator { get; } = For(true, true);

    /// <summary>The training simulator with the original Esc (ends the simulation at once).</summary>
    public static IReadOnlyList<KeyHelpSection> TrainingSimulatorLiteral { get; } = For(true, false);

    /// <summary>
    /// The reference for a flight in the campaign or the simulator, with or without the pause menu
    /// on Esc, showing the keys of <paramref name="bindings"/> (default keys when null).
    /// </summary>
    public static IReadOnlyList<KeyHelpSection> For(bool trainingSimulator, bool escapeOpensMenu, KeyBindings? bindings = null)
    {
        bindings ??= Defaults;
        string Key(FlightAction action) => GameKeys.Name(bindings[action]);
        string Keys(params FlightAction[] actions) => string.Join(' ', actions.Select(Key));
        bool Default(params FlightAction[] actions) => actions.All(a => bindings[a] == KeyBindings.Info(a).DefaultKey);

        var steering = new KeyHelpSection("FLIGHT",
        [
            new(Default(SteerUp, SteerDown, SteerLeft, SteerRight) ? "Arrows" : Keys(SteerUp, SteerDown, SteerLeft, SteerRight), "Steer"),
            new("Shift", "Steer hard"),
            new(Keys(RollLeft, RollRight), "Roll"),
            new(Key(CentreStick), "Centre"),
            new(Default(ThrottleUp, ThrottleDown) ? "+ -" : Keys(ThrottleUp, ThrottleDown), "Throttle"),
            new(Key(FullSpeed), "Full speed"),
            new(Key(Stop), "Stop"),
            new(Key(Afterburner), "Afterburner"),
        ]);
        var weapons = new KeyHelpSection("WEAPONS",
        [
            new(Key(FireGuns), "Guns"),
            new(Key(FireMissile), "Missile"),
            new(Key(SelectGun), "Select gun"),
            new(Key(SelectMissile), "Select missile"),
            new(Key(LockTarget), "Lock target"),
            new(Key(NextTarget), "Target, next"),
        ]);

        if (trainingSimulator)
        {
            var displays = new KeyHelpSection("DISPLAYS", [new(Key(DamageDisplay), "Damage")]);
            var simulator = new KeyHelpSection("SIMULATOR", escapeOpensMenu
                ?
                [
                    new("Esc", "Menu, end"),
                    new(Key(Pause), "Pause"),
                    new("Ctrl+S", "Sound"),
                    new("Ctrl+M", "Music"),
                    new("F10", "Hide help"),
                ]
                :
                [
                    new(Key(Pause), "Pause"),
                    new("Esc", "End"),
                    new("Ctrl+S", "Sound"),
                    new("Ctrl+M", "Music"),
                    new("F10", "Hide help"),
                ]);
            return [steering, weapons, displays, Mouse, simulator];
        }

        var navigation = new KeyHelpSection("NAVIGATION",
        [
            new(Key(Autopilot), "Autopilot"),
            new(Key(NavMap), "Nav, map"),
        ]);
        var campaignDisplays = new KeyHelpSection("DISPLAYS",
        [
            new(Key(DamageDisplay), "Damage"),
            new(Key(Communication), "Comm, 1-9"),
            new(Key(FlightAction.Video), "Video on/off"),
            new(Key(MessageSpeed), "Message speed"),
        ]);
        var wingman = new KeyHelpSection("WINGMAN",
        [
            new(Key(FormUp), "Form up"),
            new(Key(BreakAndAttack), "Break, attack"),
        ]);
        var views = new KeyHelpSection("VIEWS",
        [
            new(Key(CockpitView), "Cockpit"),
            new(Keys(LeftView, RightView, RearView), "Left right rear"),
            new(Key(ChaseView), "Chase"),
            new(Key(BattleView), "Battle"),
            new(Key(TargetView), "Target"),
            new(Key(MissileCamera), "Missile cam"),
            new(Key(NextShipView), "Next ship"),
        ]);
        List<KeyHelpEntry> game = [];
        if (escapeOpensMenu)
            game.Add(new("Esc", "Menu"));
        game.AddRange(
        [
            new(Key(Pause), "Pause"),
            new("Ctrl+E", "Eject"),
            new("Ctrl+S", "Sound"),
            new("Ctrl+M", "Music"),
            new("Alt+X", "Quit"),
            new("F10", "Hide help"),
        ]);
        return [steering, navigation, weapons, campaignDisplays, wingman, views, Mouse, new KeyHelpSection("GAME", game)];
    }
}
