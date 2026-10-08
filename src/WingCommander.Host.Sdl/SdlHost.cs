using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;
using WingCommander.Core.Platform;
using WingCommander.Core.Rendering;
using static SDL.SDL3;

namespace WingCommander.Host.Sdl;

/// <summary>Which graphics API the window is created for.</summary>
public enum WindowGraphicsApi
{
    /// <summary>Window with SDL_WINDOW_VULKAN; the Vulkan renderer draws into it.</summary>
    Vulkan,

    /// <summary>Plain window for the SDL_Renderer fallback presenter.</summary>
    SdlRenderer,
}

public sealed class HostOptions
{
    /// <summary>Initial window size as a multiple of the 320x240 (4:3) presentation.</summary>
    public int Scale { get; init; } = 3;

    public bool Fullscreen { get; init; }

    public string Title { get; init; } = "Wing Commander";

    public WindowGraphicsApi GraphicsApi { get; init; } = WindowGraphicsApi.Vulkan;

    /// <summary>Stop after this many rendered frames (0 = until the game ends). Used by smoke tests.</summary>
    public int MaxFrames { get; init; }

    /// <summary>Frame rate cap when vsync is off.</summary>
    public int MaxFramesPerSecond { get; init; } = 500;
}

/// <summary>
/// SDL3 host (ADR-009): owns the window and the main loop, translates OS input into
/// <see cref="HostInputEvent"/>s, drives an <see cref="IGameApp"/> and an <see cref="IRenderer"/>,
/// and provides <see cref="IHostServices"/> (pointer, joystick, audio device, message boxes).
/// Everything except the audio callback runs on the thread that created the host.
/// </summary>
public sealed unsafe class SdlHost : IHostServices, IDisposable
{
    private const int AudioChunkFrames = 1024;

    private readonly HostOptions _options;
    private readonly short[] _audioBuffer = new short[AudioChunkFrames * 2];
    private readonly SDL_Joystick*[] _joysticks = new SDL_Joystick*[2];

    private SDL_Window* _window;
    private SDL_AudioStream* _audioStream;
    private GCHandle _selfHandle;
    private volatile IAudioSource? _audioSource;
    private IRenderer? _renderer;
    private bool _grabRequested;
    private bool _focused = true;
    private bool _disposed;
    private string? _userDataDirectory;

    public SdlHost(HostOptions? options = null)
    {
        _options = options ?? new HostOptions();
        SDL_SetAppMetadata("Wing Commander", "0.1", "com.mysticforgestudios.wc1");
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_AUDIO | SDL_InitFlags.SDL_INIT_JOYSTICK))
            throw new HostException($"SDL_Init failed: {SDL_GetError()}");

        var flags = SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY;
        if (_options.GraphicsApi == WindowGraphicsApi.Vulkan)
            flags |= SDL_WindowFlags.SDL_WINDOW_VULKAN;
        if (_options.Fullscreen)
            flags |= SDL_WindowFlags.SDL_WINDOW_FULLSCREEN;
        _window = SDL_CreateWindow(_options.Title, 320 * _options.Scale, 240 * _options.Scale, flags);
        if (_window is null)
        {
            string error = SDL_GetError() ?? "unknown error";
            SDL_Quit(); // nothing else was created yet
            throw new HostException($"SDL_CreateWindow failed: {error}");
        }

        // The game draws its own software cursor into the frame.
        SDL_HideCursor();
        _selfHandle = GCHandle.Alloc(this);
    }

    /// <summary>The SDL window (for renderer back ends living in this assembly).</summary>
    internal SDL_Window* Window => _window;

    public HostOptions Options => _options;

    public string UserDataDirectory
    {
        get
        {
            if (_userDataDirectory is null)
            {
                string? path = SDL_GetPrefPath("Origin Systems", "Wing Commander");
                _userDataDirectory = string.IsNullOrEmpty(path)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WingCommander")
                    : path;
                Directory.CreateDirectory(_userDataDirectory);
            }
            return _userDataDirectory;
        }
    }

    /// <summary>Drawable size in physical pixels (HiDPI aware).</summary>
    public (int Width, int Height) DrawableSize
    {
        get
        {
            int w, h;
            SDL_GetWindowSizeInPixels(_window, &w, &h);
            return (w, h);
        }
    }

    // ------------------------------------------------------------------ renderers

    /// <summary>Creates the SDL_Renderer fallback presenter for this window.</summary>
    public IRenderer CreateSdlRenderer(RendererSettings settings) => new SdlRendererBackend(this, settings);

    /// <summary>Instance extensions SDL needs for a Vulkan surface on this platform.</summary>
    public IReadOnlyList<string> VulkanInstanceExtensions
    {
        get
        {
            uint count;
            byte** names = SDL_Vulkan_GetInstanceExtensions(&count);
            if (names is null)
                throw new HostException($"SDL_Vulkan_GetInstanceExtensions failed: {SDL_GetError()}");
            var list = new string[count];
            for (int i = 0; i < count; i++)
                list[i] = Marshal.PtrToStringUTF8((nint)names[i]) ?? "";
            return list;
        }
    }

    /// <summary>The Vulkan loader's vkGetInstanceProcAddr as loaded by SDL (0 if unavailable).</summary>
    public nint VulkanGetInstanceProcAddr => SDL_Vulkan_GetVkGetInstanceProcAddr();

    /// <summary>Creates a VkSurfaceKHR for the window; returns the surface handle.</summary>
    public ulong CreateVulkanSurface(nint instance)
    {
        VkSurfaceKHR_T* surface;
        if (!SDL_Vulkan_CreateSurface(_window, (VkInstance_T*)instance, null, &surface))
            throw new HostException($"SDL_Vulkan_CreateSurface failed: {SDL_GetError()}");
        return (ulong)(nint)surface;
    }

    public void DestroyVulkanSurface(nint instance, ulong surface) =>
        SDL_Vulkan_DestroySurface((VkInstance_T*)instance, (VkSurfaceKHR_T*)(nint)surface, null);

    // ------------------------------------------------------------------ window control (smoke tests)

    /// <summary>
    /// Called by <see cref="Run"/> right before each frame is rendered (and once per idle
    /// iteration while minimised) with the number of frames rendered so far. Smoke tests use it
    /// to resize, toggle fullscreen and minimise the window at known points.
    /// </summary>
    public Action<int>? LoopHook { get; set; }

    public bool IsMinimized => (SDL_GetWindowFlags(_window) & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != 0;

    public void SetWindowSize(int width, int height)
    {
        SDL_SetWindowSize(_window, width, height);
        SDL_SyncWindow(_window);
    }

    public void SetFullscreen(bool fullscreen)
    {
        SDL_SetWindowFullscreen(_window, fullscreen);
        SDL_SyncWindow(_window);
    }

    public void MinimizeWindow() => SDL_MinimizeWindow(_window);

    public void RestoreWindow() => SDL_RestoreWindow(_window);

    // ------------------------------------------------------------------ main loop

    /// <summary>Runs the main loop until the game ends (or MaxFrames frames were shown).</summary>
    public void Run(IGameApp app, IRenderer renderer)
    {
        _renderer = renderer;
        SDL_SetWindowTitle(_window, app.Title);
        var clock = Stopwatch.StartNew();
        TimeSpan last = TimeSpan.Zero;
        int frames = 0;
        SDL_Event ev;

        while (!app.IsFinished)
        {
            while (SDL_PollEvent(&ev))
            {
                if (Translate(in ev, out var e))
                    app.HandleEvent(in e);
            }

            TimeSpan now = clock.Elapsed;
            app.Update(now - last);
            last = now;

            var (w, h) = DrawableSize;
            if (w <= 0 || h <= 0 || (SDL_GetWindowFlags(_window) & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != 0)
            {
                LoopHook?.Invoke(frames);
                SDL_DelayNS(10_000_000);
                continue;
            }

            LoopHook?.Invoke(frames);
            renderer.Render(app.Frame);
            frames++;
            if (_options.MaxFrames > 0 && frames >= _options.MaxFrames)
                break;

            if (!renderer.Settings.VSync && _options.MaxFramesPerSecond > 0)
            {
                double minimumFrame = 1000.0 / _options.MaxFramesPerSecond;
                double spent = (clock.Elapsed - now).TotalMilliseconds;
                if (spent < minimumFrame)
                    SDL_DelayNS((ulong)((minimumFrame - spent) * 1_000_000));
            }
        }
    }

    // ------------------------------------------------------------------ services

    public void WarpMouse(int x, int y)
    {
        var rect = CurrentLayout();
        if (rect.IsEmpty)
            return;
        var (px, py) = PresentationLayout.FromFrame(rect, x, y);
        float density = SDL_GetWindowPixelDensity(_window);
        if (density <= 0)
            density = 1;
        SDL_WarpMouseInWindow(_window, px / density, py / density);
    }

    public void SetMouseGrab(bool enabled)
    {
        _grabRequested = enabled;
        ApplyMouseGrab();
    }

    public bool TryReadJoystick(int device, out JoystickState state)
    {
        state = default;
        if ((uint)device >= (uint)_joysticks.Length || _joysticks[device] is null)
            return false;
        SDL_UpdateJoysticks();
        var joystick = _joysticks[device];
        int x = SDL_GetJoystickAxis(joystick, 0) + 32768;
        int y = SDL_GetJoystickAxis(joystick, 1) + 32768;
        int buttons = (SDL_GetJoystickButton(joystick, 0) ? 1 : 0) | (SDL_GetJoystickButton(joystick, 1) ? 2 : 0);
        state = new JoystickState(x, y, buttons);
        return true;
    }

    public void StartAudio(IAudioSource source)
    {
        StopAudio();
        _audioSource = source;
        var spec = new SDL_AudioSpec
        {
            format = SDL_AudioFormat.SDL_AUDIO_S16LE,
            channels = 2,
            freq = source.SampleRate,
        };
        _audioStream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, &AudioCallback, GCHandle.ToIntPtr(_selfHandle));
        if (_audioStream is null)
        {
            _audioSource = null;
            Console.Error.WriteLine($"Cannot open audio device: {SDL_GetError()}");
            return;
        }
        SDL_ResumeAudioStreamDevice(_audioStream);
    }

    public void StopAudio()
    {
        if (_audioStream is not null)
        {
            SDL_DestroyAudioStream(_audioStream);
            _audioStream = null;
        }
        _audioSource = null;
    }

    public void ShowMessage(string title, string text, bool isError)
    {
        var flags = isError ? SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR : SDL_MessageBoxFlags.SDL_MESSAGEBOX_INFORMATION;
        SDL_ShowSimpleMessageBox(flags, title, text, _window);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void AudioCallback(IntPtr userdata, SDL_AudioStream* stream, int additionalAmount, int totalAmount)
    {
        try
        {
            if (GCHandle.FromIntPtr(userdata).Target is SdlHost host)
                host.FillAudio(stream, additionalAmount);
        }
        catch
        {
            // Never let an exception cross into native code.
        }
    }

    private void FillAudio(SDL_AudioStream* stream, int bytesNeeded)
    {
        var source = _audioSource;
        int framesNeeded = bytesNeeded / 4;
        while (framesNeeded > 0)
        {
            int frames = Math.Min(framesNeeded, AudioChunkFrames);
            var span = _audioBuffer.AsSpan(0, frames * 2);
            if (source is null)
                span.Clear();
            else
                source.Render(span);
            fixed (short* p = _audioBuffer)
            {
                SDL_PutAudioStreamData(stream, (IntPtr)p, frames * 4);
            }
            framesNeeded -= frames;
        }
    }

    // ------------------------------------------------------------------ events

    private PresentationRect CurrentLayout()
    {
        var (w, h) = DrawableSize;
        var settings = _renderer?.Settings ?? new RendererSettings();
        return PresentationLayout.Compute(w, h, settings.Aspect, settings.IntegerScaling);
    }

    private (int X, int Y) MapMouse(float windowX, float windowY)
    {
        float density = SDL_GetWindowPixelDensity(_window);
        if (density <= 0)
            density = 1;
        return PresentationLayout.ToFrame(CurrentLayout(), windowX * density, windowY * density);
    }

    private bool Translate(in SDL_Event ev, out HostInputEvent result)
    {
        switch ((SDL_EventType)ev.type)
        {
            case SDL_EventType.SDL_EVENT_QUIT:
            case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                result = new HostInputEvent(HostInputKind.Quit, 0, 0, 0, 0, 0, HostModifiers.None, false);
                return true;

            case SDL_EventType.SDL_EVENT_KEY_DOWN:
            case SDL_EventType.SDL_EVENT_KEY_UP:
            {
                bool down = (SDL_EventType)ev.type == SDL_EventType.SDL_EVENT_KEY_DOWN;
                if (down && !ev.key.repeat && HandleHostShortcut(ev.key.scancode, ev.key.mod, out bool quit))
                {
                    result = quit ? new HostInputEvent(HostInputKind.Quit, 0, 0, 0, 0, 0, HostModifiers.None, false) : default;
                    return quit;
                }
                int code = ScanCodes.FromSdl(ev.key.scancode);
                int vk = VirtualKeys.FromSdl(ev.key.scancode, ev.key.key);
                if (code == 0 && vk == 0)
                    break;
                result = new HostInputEvent(down ? HostInputKind.KeyDown : HostInputKind.KeyUp,
                    code, vk, 0, 0, 0, ToModifiers(ev.key.mod), ev.key.repeat);
                return true;
            }

            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
            {
                var (x, y) = MapMouse(ev.motion.x, ev.motion.y);
                result = new HostInputEvent(HostInputKind.MouseMove, 0, 0, x, y, ToButtons(ev.motion.state), CurrentModifiers(), false);
                return true;
            }

            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
            {
                int button = ev.button.button == SDL_BUTTON_LEFT ? MouseButtons.Left
                    : ev.button.button == SDL_BUTTON_RIGHT ? MouseButtons.Right
                    : 0;
                if (button == 0)
                    break; // the game ignores the middle button
                var (x, y) = MapMouse(ev.button.x, ev.button.y);
                bool down = (SDL_EventType)ev.type == SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN;
                float fx, fy;
                int buttons = ToButtons(SDL_GetMouseState(&fx, &fy));
                buttons = down ? buttons | button : buttons & ~button;
                result = new HostInputEvent(down ? HostInputKind.MouseButtonDown : HostInputKind.MouseButtonUp,
                    button, 0, x, y, buttons, CurrentModifiers(), false);
                return true;
            }

            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
            {
                float wheel = ev.wheel.y;
                if (wheel == 0)
                    break;
                result = new HostInputEvent(HostInputKind.MouseWheel, 0, 0, 0, wheel > 0 ? 1 : -1, 0, CurrentModifiers(), false);
                return true;
            }

            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
                _focused = false;
                ApplyMouseGrab();
                result = new HostInputEvent(HostInputKind.FocusLost, 0, 0, 0, 0, 0, HostModifiers.None, false);
                return true;

            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED:
                _focused = true;
                ApplyMouseGrab();
                result = new HostInputEvent(HostInputKind.FocusGained, 0, 0, 0, 0, 0, HostModifiers.None, false);
                return true;

            case SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
            case SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
                _renderer?.SurfaceResized();
                break;

            case SDL_EventType.SDL_EVENT_JOYSTICK_ADDED:
                OpenJoystick(ev.jdevice.which);
                break;

            case SDL_EventType.SDL_EVENT_JOYSTICK_REMOVED:
                CloseJoystick(ev.jdevice.which);
                break;
        }
        result = default;
        return false;
    }

    /// <summary>Alt+Enter / Cmd+Enter toggles fullscreen; Cmd+Q quits (macOS). They never reach the game.</summary>
    private bool HandleHostShortcut(SDL_Scancode scancode, SDL_Keymod mod, out bool quit)
    {
        quit = false;
        bool alt = (mod & SDL_Keymod.SDL_KMOD_ALT) != 0;
        bool gui = (mod & SDL_Keymod.SDL_KMOD_GUI) != 0;
        if ((alt || gui) && (scancode == SDL_Scancode.SDL_SCANCODE_RETURN || scancode == SDL_Scancode.SDL_SCANCODE_KP_ENTER))
        {
            var flags = SDL_GetWindowFlags(_window);
            SDL_SetWindowFullscreen(_window, (flags & SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) == 0);
            return true;
        }
        if (gui && scancode == SDL_Scancode.SDL_SCANCODE_Q)
        {
            quit = true;
            return true;
        }
        return false;
    }

    private void ApplyMouseGrab() => SDL_SetWindowMouseGrab(_window, _grabRequested && _focused);

    private void OpenJoystick(SDL_JoystickID id)
    {
        for (int i = 0; i < _joysticks.Length; i++)
        {
            if (_joysticks[i] is not null && SDL_GetJoystickID(_joysticks[i]) == id)
                return;
        }
        for (int i = 0; i < _joysticks.Length; i++)
        {
            if (_joysticks[i] is null)
            {
                _joysticks[i] = SDL_OpenJoystick(id);
                return;
            }
        }
    }

    private void CloseJoystick(SDL_JoystickID id)
    {
        for (int i = 0; i < _joysticks.Length; i++)
        {
            if (_joysticks[i] is not null && SDL_GetJoystickID(_joysticks[i]) == id)
            {
                SDL_CloseJoystick(_joysticks[i]);
                _joysticks[i] = null;
            }
        }
    }

    private static int ToButtons(SDL_MouseButtonFlags flags)
    {
        int b = 0;
        if ((flags & SDL_MouseButtonFlags.SDL_BUTTON_LMASK) != 0) b |= MouseButtons.Left;
        if ((flags & SDL_MouseButtonFlags.SDL_BUTTON_RMASK) != 0) b |= MouseButtons.Right;
        return b;
    }

    private static HostModifiers ToModifiers(SDL_Keymod mod)
    {
        var m = HostModifiers.None;
        if ((mod & SDL_Keymod.SDL_KMOD_SHIFT) != 0) m |= HostModifiers.Shift;
        if ((mod & SDL_Keymod.SDL_KMOD_CTRL) != 0) m |= HostModifiers.Control;
        if ((mod & SDL_Keymod.SDL_KMOD_ALT) != 0) m |= HostModifiers.Alt;
        return m;
    }

    private static HostModifiers CurrentModifiers() => ToModifiers(SDL_GetModState());

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopAudio();
        for (int i = 0; i < _joysticks.Length; i++)
        {
            if (_joysticks[i] is not null)
            {
                SDL_CloseJoystick(_joysticks[i]);
                _joysticks[i] = null;
            }
        }
        if (_window is not null)
            SDL_DestroyWindow(_window);
        _window = null;
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
        SDL_Quit();
    }
}

public sealed class HostException(string message) : Exception(message);
