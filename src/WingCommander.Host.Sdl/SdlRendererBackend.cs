using SDL;
using WingCommander.Core.Rendering;
using WingCommander.Core.Video;
using static SDL.SDL3;

namespace WingCommander.Host.Sdl;

/// <summary>
/// Fallback presenter using SDL_Renderer (Direct3D/OpenGL/Metal/software chosen by SDL) for
/// machines without a usable Vulkan driver. Converts the indexed front buffer to ARGB on the CPU
/// only when pixels or palette changed, and letterboxes with <see cref="PresentationLayout"/>.
/// </summary>
public sealed unsafe class SdlRendererBackend : IRenderer
{
    private readonly uint[] _argb = new uint[Framebuffer.PixelCount];
    private SDL_Renderer* _renderer;
    private SDL_Texture* _texture;
    private int _pixelsVersion = -1;
    private int _paletteVersion = -1;
    private ScalingFilter _appliedFilter = (ScalingFilter)(-1);
    private bool _appliedVSync;

    internal SdlRendererBackend(SdlHost host, RendererSettings settings)
    {
        Settings = settings;
        _renderer = SDL_CreateRenderer(host.Window, (byte*)null);
        if (_renderer is null)
            throw new HostException($"SDL_CreateRenderer failed: {SDL_GetError()}");
        _texture = SDL_CreateTexture(_renderer, SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888,
            SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, Framebuffer.Width, Framebuffer.Height);
        if (_texture is null)
            throw new HostException($"SDL_CreateTexture failed: {SDL_GetError()}");
        _appliedVSync = settings.VSync;
        SDL_SetRenderVSync(_renderer, settings.VSync ? 1 : 0);
        Name = $"SDL_Renderer ({SDL_GetRendererName(_renderer)})";
    }

    public string Name { get; }

    public RendererSettings Settings { get; }

    public void SurfaceResized()
    {
        // SDL_Renderer tracks the window size itself.
    }

    public void Render(RenderFrame frame)
    {
        var classic = frame.Classic;
        if (classic.PixelsVersion != _pixelsVersion || classic.Palette.Version != _paletteVersion)
        {
            classic.Pixels.ToArgb(classic.Palette.Argb, _argb);
            fixed (uint* pixels = _argb)
            {
                SDL_UpdateTexture(_texture, null, (IntPtr)pixels, Framebuffer.Width * sizeof(uint));
            }
            _pixelsVersion = classic.PixelsVersion;
            _paletteVersion = classic.Palette.Version;
        }

        if (_appliedFilter != Settings.Filter)
        {
            var mode = Settings.Filter switch
            {
                ScalingFilter.Nearest => SDL_ScaleMode.SDL_SCALEMODE_NEAREST,
                ScalingFilter.Linear => SDL_ScaleMode.SDL_SCALEMODE_LINEAR,
                _ => SDL_ScaleMode.SDL_SCALEMODE_PIXELART,
            };
            SDL_SetTextureScaleMode(_texture, mode);
            _appliedFilter = Settings.Filter;
        }
        if (_appliedVSync != Settings.VSync)
        {
            SDL_SetRenderVSync(_renderer, Settings.VSync ? 1 : 0);
            _appliedVSync = Settings.VSync;
        }

        int w, h;
        SDL_GetRenderOutputSize(_renderer, &w, &h);
        var rect = PresentationLayout.Compute(w, h, Settings.Aspect, Settings.IntegerScaling);
        SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        SDL_RenderClear(_renderer);
        if (!rect.IsEmpty)
        {
            var dst = new SDL_FRect { x = rect.X, y = rect.Y, w = rect.Width, h = rect.Height };
            SDL_RenderTexture(_renderer, _texture, null, &dst);
        }
        SDL_RenderPresent(_renderer);
    }

    public void Dispose()
    {
        if (_texture is not null)
            SDL_DestroyTexture(_texture);
        if (_renderer is not null)
            SDL_DestroyRenderer(_renderer);
        _texture = null;
        _renderer = null;
    }
}
