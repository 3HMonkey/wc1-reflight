namespace WingCommander.Game.Input;

/// <summary>
/// Event type codes of the DOS-era event manager. The game compares raw numbers, so these
/// are plain constants. Codes 5 and 10 are never queued by the Win32/SDL builds but the
/// consumers still handle them like 3 and 2.
/// </summary>
public static class InputEventType
{
    public const short None = 0;
    public const short ButtonUp = 1;
    public const short ButtonDown = 2;
    public const short KeyDown = 3;
    public const short KeyUp = 4;
    public const short Character = 5;
    public const short JoystickSample = 6;
    public const short JoystickButton = 10;
    public const short MouseMove = 13;
}

/// <summary>Bits of the queued event's modifier word (computed at queue time).</summary>
public static class InputModifiers
{
    public const uint PrimaryButton = 0x0002;
    public const uint SecondaryButton = 0x0004;
    public const uint Shift = 0x00e0;
    public const uint Alt = 0x0700;
    public const uint Control = 0x2000;
}

/// <summary>
/// The record handed to the game by <see cref="EventManager.GetNextInputEvent"/> and
/// <see cref="EventManager.PeekInputEvent"/>. Like the original, each call only writes the
/// fields its event type defines; callers reuse one instance.
/// </summary>
/// <remarks>C: InputEventState (packed, 16 bytes).</remarks>
public struct InputEventState
{
    public short Type;
    public uint Value;
    public uint Timestamp;
    public short Modifiers;
    public short X;
    public short Y;
}

/// <summary>One sampled joystick position and its button mask (raw 0..65535, or -9..9 after calibration).</summary>
/// <remarks>C: InputDeviceSample.</remarks>
public struct InputDeviceSample
{
    public int X;
    public int Y;
    public uint Buttons;
}

/// <summary>Inclusive rectangle the pointer is clamped to (the cursor's viewport).</summary>
public interface IPointerBounds
{
    short Left { get; }
    short Top { get; }
    short Right { get; }
    short Bottom { get; }
}

/// <summary>Fixed pointer bounds; the default is the whole 320x200 screen.</summary>
public sealed class PointerBounds(short left, short top, short right, short bottom) : IPointerBounds
{
    public static readonly PointerBounds FullScreen = new(0, 0, 319, 199);

    public short Left { get; } = left;
    public short Top { get; } = top;
    public short Right { get; } = right;
    public short Bottom { get; } = bottom;
}

/// <summary>Pointer position/buttons plus the software cursor's shape selection.</summary>
/// <remarks>C: MouseCursorState (stMouseCursorState, stHostMouseState).</remarks>
public sealed class MouseCursorState
{
    public short X;
    public short Y;
    public byte PrimaryButton;
    public byte SecondaryButton;

    /// <summary>Cursor sprite frame (0 arrow, 2 flight crosshair, menu-specific frames).</summary>
    public short Frame;

    /// <summary>Set when the shape or frame changed (the drawn background must be restored).</summary>
    public bool ShapeChanged;

    /// <summary>Clamp rectangle and the viewport the cursor is drawn into.</summary>
    public IPointerBounds Bounds = PointerBounds.FullScreen;
}
