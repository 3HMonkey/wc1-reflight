using WingCommander.Core.Resources;
using WingCommander.Core.Video;
using WingCommander.Game.Input;
using WingCommander.Game.Runtime;

namespace WingCommander;

/// <summary>
/// Temporary screen until the real game screens are wired up: written like a ported screen
/// (an async loop that draws, presents at the 16 fps cinematic rate and polls the event queue)
/// to exercise host, runtime, input and renderer. Shows GAME.PAL as a 16x16 grid, a moving bar
/// and a crosshair at the pointer; Esc quits.
/// </summary>
internal static class HostCheckScreen
{
    public static async Task RunAsync(GameRuntime game, GameDirectory directory)
    {
        LoadGamePalette(directory, game.Display.Palette);
        game.Display.PaletteChanged();
        game.Events.Initialize(PointerBounds.FullScreen);
        game.Timing.SetCinematicFrameTiming();

        var frame = game.Display.Working;
        var state = new InputEventState();
        while (true)
        {
            short type;
            while ((type = game.Events.PollInputEvent(ref state)) != 0)
            {
                if (type == InputEventType.KeyDown && state.Value == 0x01)
                    return;
            }

            frame.Clear(0);
            for (int y = 0; y < 160; y++)
            {
                var row = frame.Row(20 + y);
                for (int x = 0; x < 256; x++)
                    row[32 + x] = (byte)((y / 10) * 16 + x / 16);
            }
            int barX = (int)(game.Timing.Milliseconds / 10 % Framebuffer.Width);
            for (int y = 190; y < 200; y++)
                for (int x = 0; x < 8; x++)
                    frame[(barX + x) % Framebuffer.Width, y] = 15;
            int cx = game.Events.Cursor.X, cy = game.Events.Cursor.Y;
            for (int d = -3; d <= 3; d++)
            {
                frame[Math.Clamp(cx + d, 0, Framebuffer.Width - 1), cy] = 255;
                frame[cx, Math.Clamp(cy + d, 0, Framebuffer.Height - 1)] = 255;
            }

            await game.Display.PresentAsync();
        }
    }

    /// <summary>GAME.PAL is an IFF ILBM whose CMAP holds 256 8-bit RGB triplets (the game reads offset 0x30).</summary>
    private static void LoadGamePalette(GameDirectory directory, Palette palette)
    {
        if (directory.Exists("GAME.PAL"))
        {
            byte[] pal = directory.ReadFile("GAME.PAL");
            int cmap = pal.AsSpan().IndexOf("CMAP"u8);
            if (cmap >= 0 && pal.Length >= cmap + 8 + 768)
            {
                palette.SetRange(0, pal.AsSpan(cmap + 8, 768), 256);
                return;
            }
        }
        for (int i = 0; i < 256; i++)
            palette.SetEntry(i, (byte)i, (byte)i, (byte)i);
    }
}
