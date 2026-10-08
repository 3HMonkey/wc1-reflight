using WingCommander.Core.Rendering;
using WingCommander.Game.Config;
using WingCommander.Host.Sdl;

namespace WingCommander;

/// <summary>Gives the settings menu the live renderer settings and the window's fullscreen state.</summary>
internal sealed class DisplayControl(SdlHost host, IRenderer renderer) : IDisplayControl
{
    public RendererSettings Renderer => renderer.Settings;

    public bool Fullscreen
    {
        get => host.IsFullscreen;
        set => host.SetFullscreen(value);
    }
}
