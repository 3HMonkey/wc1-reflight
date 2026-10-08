namespace WingCommander.Core.Platform;

/// <summary>
/// <see cref="IHostServices"/> for tests and tools: records pointer warps, grab requests, audio
/// and messages instead of touching an OS window. Combine with <c>GameScheduler.Advance</c> and
/// scripted input to run game code deterministically without a display.
/// </summary>
public sealed class HeadlessServices : IHostServices
{
    public bool MouseGrabbed { get; private set; }

    public (int X, int Y)? LastWarp { get; private set; }

    public IAudioSource? AudioSource { get; private set; }

    public List<string> Messages { get; } = [];

    public string UserDataDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "wc1-headless");

    public void WarpMouse(int x, int y) => LastWarp = (x, y);

    public void SetMouseGrab(bool enabled) => MouseGrabbed = enabled;

    public bool TryReadJoystick(int device, out JoystickState state)
    {
        state = default;
        return false;
    }

    public void StartAudio(IAudioSource source) => AudioSource = source;

    public void StopAudio() => AudioSource = null;

    public void ShowMessage(string title, string text, bool isError) => Messages.Add($"{title}: {text}");
}
