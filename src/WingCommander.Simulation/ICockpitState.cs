namespace WingCommander.Simulation;

/// <summary>
/// Cockpit state the simulation reads. The VDU modes are cockpit UI state owned by the Game, but
/// they change gameplay in a few places: the nav pointer occupies an object slot only while the
/// right VDU shows navigation (<c>draw_nav_pointer</c>), and the callers of
/// <c>ShowComponentHitHudMessage</c> test the left VDU's mode.
/// </summary>
public interface ICockpitState
{
    /// <summary>Current mode of VDU <paramref name="vdu"/> (0 left, 1 right): 0 broken, 1 weapons,
    /// 2 damage, 3 target, 4 communication, 5 navigation, 6 transmission, 8 information.</summary>
    /// <remarks>C: get_mode (0x4147E0, cockpt.c).</remarks>
    int GetVduMode(int vdu);

    /// <summary>A HUD message (or a comm transmission) is showing: <c>nMessageTimer &gt; 0</c>. The
    /// player's wingman reports "enemy sighted" only when the message line is free. Default: false.</summary>
    /// <remarks>C: message_showing (0x4149F0, cockpt.c), read by imperial_formation (0x409D60, brains.c).
    /// Added 2026-10-07 with a default implementation.</remarks>
    bool MessageShowing() => false;
}
