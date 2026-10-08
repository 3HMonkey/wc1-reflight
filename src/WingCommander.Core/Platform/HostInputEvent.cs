namespace WingCommander.Core.Platform;

public enum HostInputKind : byte
{
    KeyDown,
    KeyUp,
    MouseMove,
    MouseButtonDown,
    MouseButtonUp,
    MouseWheel,
    FocusLost,
    FocusGained,
    Quit,
}

[Flags]
public enum HostModifiers : byte
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
}

/// <summary>Mouse button bit masks used in <see cref="HostInputEvent.Buttons"/>.</summary>
public static class MouseButtons
{
    public const int Left = 1;
    public const int Right = 2;
}

/// <summary>
/// One input event delivered by the host. The host is deliberately dumb: it translates
/// OS events into the vocabulary the original game understands and leaves all game
/// semantics (event queue, coalescing, VK duplicates, latches) to the game's event manager.
/// </summary>
/// <param name="Kind">Event kind.</param>
/// <param name="Code">Keys: IBM PC set-1 scan code (0x01 Esc .. 0x58 F12; arrows and keypad share codes).
/// Mouse buttons: 1 = left, 2 = right.</param>
/// <param name="VirtualKey">Keys: layout-aware Windows virtual-key code ('A'..'Z', '0'..'9', 0x0D Enter,
/// 0x1B Esc, 0x70.. F-keys, punctuation as ASCII, 0xBC comma, 0xBE period); 0 if none.</param>
/// <param name="X">Mouse X in 320x200 frame coordinates (clamped).</param>
/// <param name="Y">Mouse Y in frame coordinates (clamped); wheel: +1 up, -1 down.</param>
/// <param name="Buttons">Mouse button state after the event (<see cref="MouseButtons"/>).</param>
/// <param name="Modifiers">Shift/Control/Alt state when the event happened.</param>
/// <param name="Repeat">Keyboard auto-repeat.</param>
public readonly record struct HostInputEvent(
    HostInputKind Kind,
    int Code,
    int VirtualKey,
    int X,
    int Y,
    int Buttons,
    HostModifiers Modifiers,
    bool Repeat);
