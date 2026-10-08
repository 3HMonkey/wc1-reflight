using WingCommander.Core.Rendering;

namespace WingCommander.Core.Platform;

/// <summary>
/// What the host drives (ADR-009). The host owns the main loop: it forwards OS events to
/// <see cref="HandleEvent"/>, calls <see cref="Update"/> with the real elapsed time, then renders
/// <see cref="Frame"/> with its renderer. Implementations must never block.
/// </summary>
public interface IGameApp
{
    /// <summary>Window title.</summary>
    string Title { get; }

    void HandleEvent(in HostInputEvent e);

    /// <summary>Advances the game's virtual clock by real elapsed time and runs everything that falls due.</summary>
    void Update(TimeSpan elapsed);

    /// <summary>The picture to show now (the game's front buffer).</summary>
    RenderFrame Frame { get; }

    /// <summary>True once the game ended (quit, window closed, fatal error).</summary>
    bool IsFinished { get; }
}

/// <summary>
/// Passive services the game may use while it runs (pointer, joystick, audio device, messages).
/// Called from the game thread only, except that the host calls <see cref="IAudioSource.Render"/>
/// on its audio thread.
/// </summary>
public interface IHostServices
{
    /// <summary>Moves the OS pointer to a frame coordinate (the game's SetCursorPos).</summary>
    void WarpMouse(int x, int y);

    /// <summary>Confines the pointer to the window while focused (requested during spaceflight).</summary>
    void SetMouseGrab(bool enabled);

    /// <summary>Reads joystick <paramref name="device"/> (0 or 1); axes 0..65535, buttons bits 0..1.</summary>
    bool TryReadJoystick(int device, out JoystickState state);

    /// <summary>Opens the audio device and starts pulling samples from <paramref name="source"/>.</summary>
    void StartAudio(IAudioSource source);

    void StopAudio();

    /// <summary>Modal message box (fatal errors, notices).</summary>
    void ShowMessage(string title, string text, bool isError);

    /// <summary>Per-user writable directory for settings (volume, calibration).</summary>
    string UserDataDirectory { get; }
}

/// <summary>Raw joystick sample: axes 0..65535 (centre about 32768), button bits 0..1.</summary>
public readonly record struct JoystickState(int X, int Y, int Buttons);

/// <summary>
/// Pulls interleaved stereo 16-bit PCM. <see cref="Render"/> runs on the audio thread and
/// must not block or allocate.
/// </summary>
public interface IAudioSource
{
    int SampleRate { get; }

    void Render(Span<short> interleavedStereo);
}

/// <summary>
/// Thrown inside game coroutines to end the game (the original's exit_squadron/exit paths).
/// The runtime treats it as a normal end, not as a crash.
/// </summary>
public sealed class GameExitException : Exception
{
    public GameExitException()
        : base("The game was asked to exit.")
    {
    }

    public GameExitException(string message)
        : base(message)
    {
    }

    public GameExitException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
