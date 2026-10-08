using System.Diagnostics;
using System.Globalization;
using WingCommander.Core.Imaging;
using WingCommander.Core.Rendering;
using WingCommander.Core.Video;
using WingCommander.Host.Sdl;
using WingCommander.Render.Vulkan;

namespace WingCommander;

/// <summary>
/// Scripted window smoke test (wc1 --window-test): resizes the window, toggles fullscreen,
/// minimises and restores it, switches filter and vsync at fixed frame numbers. On the Vulkan
/// renderer it captures presented frames and checks them against the front buffer: letterbox
/// bars black, source pixel centres exactly the palette colour. Captures are saved as PNG when
/// a directory is given.
/// </summary>
internal sealed class WindowTest(SdlHost host, IRenderer renderer, RenderFrame frame, string? captureDirectory)
{
    /// <summary>Frames the script needs (pass at least this to --frames).</summary>
    public const int RequiredFrames = 270;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<int> _done = [];
    private double _minimizedAt = -1;
    private string? _pendingLabel;
    private byte[]? _expectedPixels;
    private byte[]? _expectedPalette;
    private ushort[]? _textMask;
    private bool _keyHelpVisible;
    private int _spriteBackgroundIndex = -1;

    public int Checks { get; private set; }

    public int Failures { get; private set; }

    public void OnLoop(int frames)
    {
        if (host.IsMinimized)
        {
            if (_minimizedAt >= 0 && _clock.Elapsed.TotalMilliseconds - _minimizedAt > 700)
            {
                Log("restore");
                host.RestoreWindow();
                _minimizedAt = -1;
            }
            return;
        }
        CollectCapture();
        if (!_done.Add(frames))
            return;
        switch (frames)
        {
            case 20:
                Capture("start");
                break;
            case 30:
                Resize(800, 600);
                break;
            case 45:
                Capture("800x600");
                break;
            case 60:
                Resize(1280, 720);
                break;
            case 75:
                Capture("1280x720");
                break;
            case 90:
                Log("fullscreen on");
                host.SetFullscreen(true);
                break;
            case 120:
                Capture("fullscreen");
                break;
            case 150:
                Log("fullscreen off");
                host.SetFullscreen(false);
                break;
            case 165:
                Log("minimise");
                host.MinimizeWindow();
                _minimizedAt = _clock.Elapsed.TotalMilliseconds;
                break;
            case 200:
                Capture("restored");
                break;
            case 215:
                renderer.Settings.Filter = ScalingFilter.Nearest;
                Capture("nearest");
                break;
            case 230:
                renderer.Settings.VSync = false;
                Log("vsync off");
                break;
            case 245:
                renderer.Settings.VSync = true;
                renderer.Settings.Filter = ScalingFilter.SharpBilinear;
                Capture("vsync on");
                break;
            case 260:
                renderer.Settings.Aspect = AspectMode.SquarePixels;
                Capture("square pixels");
                break;
        }
    }

    public void Finish()
    {
        CollectCapture();
        Log($"{Checks} captures checked, {Failures} failure(s)");
        if (renderer is VulkanRenderer vulkan)
            Log($"statistics: {vulkan.Statistics}");
    }

    private void Resize(int width, int height)
    {
        Log($"resize {width}x{height}");
        host.SetWindowSize(width, height);
    }

    private void Capture(string label)
    {
        if (renderer is not VulkanRenderer vulkan)
            return;
        CollectCapture();
        vulkan.RequestCapture();
        _pendingLabel = label;
        // With output-resolution text the renderer shows the text layer's frame and draws glyphs
        // over the pixels of its mask.
        _expectedPixels = (frame.Text?.Pixels ?? frame.Classic.Pixels).Pixels.ToArray();
        _textMask = frame.Text?.Mask.ToArray();
        _keyHelpVisible = frame.KeyHelp is { Visible: true };
        _expectedPalette = frame.Classic.Palette.Rgb.ToArray();
        // With R2 sprites the GPU may draw over every pixel of the space background index.
        _spriteBackgroundIndex = frame.Space is { Sprites.Count: > 0 } space ? space.BackgroundIndex : -1;
    }

    private void CollectCapture()
    {
        if (_pendingLabel is null || renderer is not VulkanRenderer vulkan)
            return;
        CapturedImage? image = vulkan.TakeCapture(waitForGpu: true);
        if (image is null)
            return;
        string label = _pendingLabel;
        _pendingLabel = null;
        Checks++;
        var errors = Check(image, _expectedPixels!, _expectedPalette!);
        if (errors.Count > 0)
            Failures++;
        Log($"capture '{label}' {image.Width}x{image.Height}: {(errors.Count == 0 ? "ok" : string.Join("; ", errors.Take(4)))}");
        if (captureDirectory is not null)
        {
            Directory.CreateDirectory(captureDirectory);
            string name = string.Create(CultureInfo.InvariantCulture, $"window-{Checks:00}-{label.Replace(' ', '-')}.png");
            Png.WriteRgba(Path.Combine(captureDirectory, name), image.Width, image.Height, image.Rgba);
        }
    }

    private List<string> Check(CapturedImage image, byte[] pixels, byte[] palette)
    {
        var errors = new List<string>();
        var rect = PresentationLayout.Compute(image.Width, image.Height, renderer.Settings.Aspect, renderer.Settings.IntegerScaling);
        if (rect.X > 0 && !_keyHelpVisible)
        {
            for (int y = 0; y < image.Height; y += Math.Max(1, image.Height / 8))
            {
                if (image.GetRgb(rect.X / 2, y) != 0)
                    errors.Add($"bar pixel ({rect.X / 2},{y}) not black");
            }
        }
        if (rect.Y > 0)
        {
            for (int x = 0; x < image.Width; x += Math.Max(1, image.Width / 8))
            {
                if (image.GetRgb(x, rect.Y / 2) != 0)
                    errors.Add($"bar pixel ({x},{rect.Y / 2}) not black");
            }
        }
        foreach (int sy in (int[])[3, 50, 100, 150, 196])
        {
            foreach (int sx in (int[])[3, 80, 160, 240, 316])
            {
                var (px, py) = PresentationLayout.FromFrame(rect, sx, sy);
                int index = pixels[sy * Framebuffer.Width + sx];
                if (index == _spriteBackgroundIndex)
                    continue; // owned by the sprite layer
                if (_textMask is not null && _textMask[sy * Framebuffer.Width + sx] != 0)
                    continue; // a glyph drawn at output resolution
                if (_keyHelpVisible)
                    continue; // the key help may cover the picture
                int expected = (palette[index * 3] << 16) | (palette[index * 3 + 1] << 8) | palette[index * 3 + 2];
                int actual = image.GetRgb((int)px, (int)py);
                if (actual != expected)
                    errors.Add(string.Create(CultureInfo.InvariantCulture, $"source ({sx},{sy}) index {index}: {actual:X6} != {expected:X6}"));
            }
        }
        return errors;
    }

    private void Log(string message) =>
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[window-test {_clock.Elapsed.TotalSeconds,6:0.00}s] {message}"));
}
