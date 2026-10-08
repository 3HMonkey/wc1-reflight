namespace WingCommander.Game.Input;

/// <summary>A flight control the player can put on another key (ADR-016).</summary>
public enum FlightAction : byte
{
    SteerUp,
    SteerDown,
    SteerLeft,
    SteerRight,
    RollLeft,
    RollRight,
    CentreStick,
    ThrottleUp,
    ThrottleDown,
    FullSpeed,
    Stop,
    Afterburner,
    Autopilot,
    NavMap,
    FireGuns,
    FireMissile,
    SelectGun,
    SelectMissile,
    LockTarget,
    NextTarget,
    DamageDisplay,
    Communication,
    Video,
    MessageSpeed,
    FormUp,
    BreakAndAttack,
    CockpitView,
    LeftView,
    RightView,
    RearView,
    ChaseView,
    BattleView,
    TargetView,
    MissileCamera,
    NextShipView,
    Pause,
}

/// <summary>
/// One flight control: its identifier in <c>config.json</c>, its name in the menus, the key help
/// section it belongs to and the scan codes the game reads for it (the first one is the default key).
/// </summary>
public sealed record FlightActionInfo(FlightAction Action, string Id, string Name, string Section, byte[] OriginalKeys)
{
    public int DefaultKey => OriginalKeys[0];
}

/// <summary>
/// The player's keys for the flight controls (ADR-016): one key per control, stored in the
/// <c>controls</c> section of <c>config.json</c>. Binding a key that another control uses swaps
/// the two keys, so every control always has exactly one key. In flight
/// <see cref="KeyTranslation"/> turns the bound keys into the original ones; controls that keep
/// their default key keep all their original keys (for example the comma for rolling left).
/// </summary>
public sealed class KeyBindings
{
    /// <summary>Every bindable control, in the order of the menu and the key help.</summary>
    public static IReadOnlyList<FlightActionInfo> Actions { get; } =
    [
        new(FlightAction.SteerUp, "steerUp", "Steer up", "FLIGHT", [0x48]),
        new(FlightAction.SteerDown, "steerDown", "Steer down", "FLIGHT", [0x50]),
        new(FlightAction.SteerLeft, "steerLeft", "Steer left", "FLIGHT", [0x4b]),
        new(FlightAction.SteerRight, "steerRight", "Steer right", "FLIGHT", [0x4d]),
        new(FlightAction.RollLeft, "rollLeft", "Roll left", "FLIGHT", [0x52, 0x33]),
        new(FlightAction.RollRight, "rollRight", "Roll right", "FLIGHT", [0x53, 0x34]),
        new(FlightAction.CentreStick, "centreStick", "Centre stick", "FLIGHT", [0x4c]),
        new(FlightAction.ThrottleUp, "throttleUp", "Throttle up", "FLIGHT", [0x0d, 0x4e]),
        new(FlightAction.ThrottleDown, "throttleDown", "Throttle down", "FLIGHT", [0x0c, 0x4a]),
        new(FlightAction.FullSpeed, "fullSpeed", "Full speed", "FLIGHT", [0x2b]),
        new(FlightAction.Stop, "stop", "Stop", "FLIGHT", [0x0e]),
        new(FlightAction.Afterburner, "afterburner", "Afterburner", "FLIGHT", [0x0f, 0x37]),
        new(FlightAction.Autopilot, "autopilot", "Autopilot", "NAVIGATION", [0x1e]),
        new(FlightAction.NavMap, "navMap", "Nav map", "NAVIGATION", [0x31]),
        new(FlightAction.FireGuns, "fireGuns", "Fire guns", "WEAPONS", [0x39]),
        new(FlightAction.FireMissile, "fireMissile", "Fire missile", "WEAPONS", [0x1c]),
        new(FlightAction.SelectGun, "selectGun", "Select gun", "WEAPONS", [0x22]),
        new(FlightAction.SelectMissile, "selectMissile", "Select missile", "WEAPONS", [0x11]),
        new(FlightAction.LockTarget, "lockTarget", "Lock target", "WEAPONS", [0x26]),
        new(FlightAction.NextTarget, "nextTarget", "Next target", "WEAPONS", [0x14]),
        new(FlightAction.DamageDisplay, "damageDisplay", "Damage display", "DISPLAYS", [0x20]),
        new(FlightAction.Communication, "communication", "Communication", "DISPLAYS", [0x2e]),
        new(FlightAction.Video, "video", "Video on/off", "DISPLAYS", [0x2f]),
        new(FlightAction.MessageSpeed, "messageSpeed", "Message speed", "DISPLAYS", [0x32]),
        new(FlightAction.FormUp, "formUp", "Form up", "WINGMAN", [0x23]),
        new(FlightAction.BreakAndAttack, "breakAndAttack", "Break and attack", "WINGMAN", [0x30]),
        new(FlightAction.CockpitView, "cockpitView", "Cockpit view", "VIEWS", [0x3b]),
        new(FlightAction.LeftView, "leftView", "Left view", "VIEWS", [0x3c]),
        new(FlightAction.RightView, "rightView", "Right view", "VIEWS", [0x3d]),
        new(FlightAction.RearView, "rearView", "Rear view", "VIEWS", [0x3e]),
        new(FlightAction.ChaseView, "chaseView", "Chase view", "VIEWS", [0x3f]),
        new(FlightAction.BattleView, "battleView", "Battle view", "VIEWS", [0x40]),
        new(FlightAction.TargetView, "targetView", "Target view", "VIEWS", [0x41]),
        new(FlightAction.MissileCamera, "missileCamera", "Missile camera", "VIEWS", [0x42]),
        new(FlightAction.NextShipView, "nextShipView", "Next ship view", "VIEWS", [0x43]),
        new(FlightAction.Pause, "pause", "Pause", "GAME", [0x19]),
    ];

    private readonly byte[] _keys = new byte[Actions.Count];

    public KeyBindings() => Reset();

    /// <summary>Changes with every change of a key (the key help is rebuilt then).</summary>
    public int Version { get; private set; }

    /// <summary>The key (scan code) of <paramref name="action"/>.</summary>
    public int this[FlightAction action] => _keys[(int)action];

    /// <summary>True when every control has its default key (the game then reads the original keys untouched).</summary>
    public bool IsDefault
    {
        get
        {
            foreach (var info in Actions)
            {
                if (_keys[(int)info.Action] != info.DefaultKey)
                    return false;
            }
            return true;
        }
    }

    public static FlightActionInfo Info(FlightAction action) => Actions[(int)action];

    /// <summary>
    /// Keys that cannot be bound: Esc (pause menu), 1-9 (communication choices), F10 (key help),
    /// the modifiers (Shift steers hard, Ctrl and Alt make the fixed combinations) and the locks.
    /// </summary>
    public static bool CanBind(int scanCode) =>
        GameKeys.IsKnown(scanCode)
        && scanCode is not (0x01 or >= 0x02 and <= 0x0a or 0x1d or 0x2a or 0x36 or 0x38 or 0x3a or 0x44 or 0x45 or 0x46);

    /// <summary>The control that has <paramref name="scanCode"/>, or null.</summary>
    public FlightAction? ActionOf(int scanCode)
    {
        for (int i = 0; i < _keys.Length; i++)
        {
            if (_keys[i] == scanCode)
                return (FlightAction)i;
        }
        return null;
    }

    /// <summary>
    /// Puts <paramref name="action"/> on <paramref name="scanCode"/>. A control that had the key
    /// takes the old key of <paramref name="action"/> and is returned; null when the key was free.
    /// </summary>
    /// <exception cref="ArgumentException">The key cannot be bound (<see cref="CanBind"/>).</exception>
    public FlightAction? Bind(FlightAction action, int scanCode)
    {
        if (!CanBind(scanCode))
            throw new ArgumentException($"The key {GameKeys.Name(scanCode)} cannot be bound.", nameof(scanCode));
        int old = _keys[(int)action];
        if (old == scanCode)
            return null;
        FlightAction? other = ActionOf(scanCode);
        if (other is { } swapped)
            _keys[(int)swapped] = (byte)old;
        _keys[(int)action] = (byte)scanCode;
        Version++;
        return other;
    }

    /// <summary>Gives every control its default key.</summary>
    public void Reset()
    {
        foreach (var info in Actions)
            _keys[(int)info.Action] = (byte)info.DefaultKey;
        Version++;
    }

    public KeyBindings Clone()
    {
        var copy = new KeyBindings();
        _keys.CopyTo(copy._keys, 0);
        return copy;
    }

    /// <summary>
    /// Reads the <c>controls</c> entries ("fireGuns": "Space"); unknown controls, unknown keys and
    /// keys that cannot be bound are ignored, a key named twice ends up on the later control.
    /// </summary>
    public void Read(Func<string, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        foreach (var info in Actions)
        {
            if (GameKeys.TryParse(lookup(info.Id), out int scanCode) && CanBind(scanCode))
                Bind(info.Action, scanCode);
        }
    }

    /// <summary>Writes every control with its key identifier.</summary>
    public void Write(Action<string, string> store)
    {
        ArgumentNullException.ThrowIfNull(store);
        foreach (var info in Actions)
            store(info.Id, GameKeys.Id(_keys[(int)info.Action]));
    }
}

/// <summary>
/// What the bindings do to the keys in flight: a bound key becomes the original key of its
/// control (scan code and virtual-key code, so it behaves exactly like that key), the original
/// keys of controls that moved to another key do nothing, every other key is left alone.
/// </summary>
public sealed class KeyTranslation
{
    /// <summary><see cref="Translate"/>: the key is not changed.</summary>
    public const int Unchanged = -1;

    /// <summary><see cref="Translate"/>: the key does nothing.</summary>
    public const int Blocked = -2;

    private readonly short[] _target = new short[GameKeys.Count];

    public KeyTranslation(KeyBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        Array.Fill(_target, (short)Unchanged);
        foreach (var info in KeyBindings.Actions)
        {
            if (bindings[info.Action] == info.DefaultKey)
                continue;
            foreach (byte key in info.OriginalKeys)
                _target[key] = Blocked;
        }
        foreach (var info in KeyBindings.Actions)
        {
            int key = bindings[info.Action];
            if (key != info.DefaultKey)
                _target[key] = (short)info.DefaultKey;
        }
    }

    /// <summary>The scan code the game receives for <paramref name="scanCode"/>, <see cref="Unchanged"/> or <see cref="Blocked"/>.</summary>
    public int Translate(int scanCode) => (uint)scanCode < GameKeys.Count ? _target[scanCode] : Unchanged;
}
