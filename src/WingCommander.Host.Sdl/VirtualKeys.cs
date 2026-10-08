using SDL;

namespace WingCommander.Host.Sdl;

/// <summary>
/// Layout-aware Windows virtual-key codes for the game's "VK duplicate" key events (text
/// entry, Y/N prompts). Letters come from the SDL keycode so non-QWERTY layouts type the
/// right characters.
/// </summary>
/// <remarks>C: SdlTranslateVirtualKey (src/sdl/input.c); see docs/analysis/host-input-timing.md §4.2.</remarks>
public static class VirtualKeys
{
    public static int FromSdl(SDL_Scancode scancode, SDL_Keycode key)
    {
        switch (scancode)
        {
            case SDL_Scancode.SDL_SCANCODE_RETURN:
            case SDL_Scancode.SDL_SCANCODE_KP_ENTER:
                return 0x0d;
            case SDL_Scancode.SDL_SCANCODE_ESCAPE: return 0x1b;
            case SDL_Scancode.SDL_SCANCODE_BACKSPACE: return 0x08;
            case SDL_Scancode.SDL_SCANCODE_TAB: return 0x09;
            case SDL_Scancode.SDL_SCANCODE_LSHIFT:
            case SDL_Scancode.SDL_SCANCODE_RSHIFT:
                return 0x10;
            case SDL_Scancode.SDL_SCANCODE_LCTRL:
            case SDL_Scancode.SDL_SCANCODE_RCTRL:
                return 0x11;
            case SDL_Scancode.SDL_SCANCODE_LALT:
            case SDL_Scancode.SDL_SCANCODE_RALT:
                return 0x12;
            case SDL_Scancode.SDL_SCANCODE_PAGEUP: return 0x21;
            case SDL_Scancode.SDL_SCANCODE_PAGEDOWN: return 0x22;
            case SDL_Scancode.SDL_SCANCODE_END: return 0x23;
            case SDL_Scancode.SDL_SCANCODE_HOME: return 0x24;
            case SDL_Scancode.SDL_SCANCODE_LEFT: return 0x25;
            case SDL_Scancode.SDL_SCANCODE_UP: return 0x26;
            case SDL_Scancode.SDL_SCANCODE_RIGHT: return 0x27;
            case SDL_Scancode.SDL_SCANCODE_DOWN: return 0x28;
            case SDL_Scancode.SDL_SCANCODE_INSERT: return 0x2d;
            case SDL_Scancode.SDL_SCANCODE_DELETE: return 0x2e;
            case SDL_Scancode.SDL_SCANCODE_COMMA: return 0xbc;
            case SDL_Scancode.SDL_SCANCODE_PERIOD: return 0xbe;
        }
        if (scancode >= SDL_Scancode.SDL_SCANCODE_F1 && scancode <= SDL_Scancode.SDL_SCANCODE_F12)
            return 0x70 + (scancode - SDL_Scancode.SDL_SCANCODE_F1);

        uint k = (uint)key;
        if (k >= 'a' && k <= 'z')
            return (int)(k - 'a' + 'A');
        return k <= 0xffff ? (int)k : 0;
    }
}
