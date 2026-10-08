using WingCommander.Core.Rendering;
using WingCommander.Host.Sdl;
using WingCommander.Render.Vulkan;

namespace WingCommander;

/// <summary>Which renderer wc1 uses.</summary>
internal enum RendererChoice
{
    /// <summary>Vulkan, falling back to SDL_Renderer when Vulkan is unavailable.</summary>
    Auto,

    /// <summary>Vulkan only; fails when unavailable.</summary>
    Vulkan,

    /// <summary>SDL_Renderer only.</summary>
    Sdl,
}

/// <summary>Creates the window and the renderer (ADR-010: Vulkan first, SDL_Renderer as fallback).</summary>
internal static class Presentation
{
    public static (SdlHost Host, IRenderer Renderer) Create(CommandLine options)
    {
        if (options.Renderer != RendererChoice.Sdl)
        {
            SdlHost? host = null;
            try
            {
                // SDL loads Vulkan when it creates a window with SDL_WINDOW_VULKAN, so window creation
                // itself fails (HostException) on systems without a usable Vulkan driver.
                host = new SdlHost(HostOptions(options, WindowGraphicsApi.Vulkan));
                var renderer = VulkanRenderer.Create(new SdlVulkanSurfaceSource(host), options.RendererSettings,
                    new VulkanRendererOptions { Validation = options.VulkanValidation });
                return (host, renderer);
            }
            catch (Exception e) when (e is VulkanRendererException or HostException && options.Renderer == RendererChoice.Auto)
            {
                Console.Error.WriteLine($"Vulkan is not available ({e.Message}); using SDL_Renderer.");
                host?.Dispose();
            }
            catch
            {
                host?.Dispose();
                throw;
            }
        }
        var sdlHost = new SdlHost(HostOptions(options, WindowGraphicsApi.SdlRenderer));
        return (sdlHost, sdlHost.CreateSdlRenderer(options.RendererSettings));
    }

    private static HostOptions HostOptions(CommandLine options, WindowGraphicsApi api) => new()
    {
        Scale = options.Scale,
        Fullscreen = options.Fullscreen,
        GraphicsApi = api,
        MaxFrames = options.MaxFrames,
    };
}
