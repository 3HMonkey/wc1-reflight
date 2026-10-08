# Wing Commander 1 — Platform Host, Input, Event Manager, Timing, Main Loop

Analysis of the `wc1-re` reverse-engineering (Kilrathi Saga Win32 build plus its SDL2
port) to prepare a C# / .NET 10 port on SDL3. This document is self-contained: it
describes the host contract the game core relies on, the DOS-era event manager, the
scan-code model, the timing model, the main-loop call graph with every blocking point,
and a proposed C# host abstraction.

Reference tree: `reference/wc1-re` (read-only).
Line numbers below refer to that tree as of this analysis.

Files analysed in full:

| File | Role |
| --- | --- |
| `src/winmain.c` | Win32 shell: `WinMain`, window, `MainWindowProc`, `PumpWindowMessages`, joystick wrappers, guarded heap, `WarpMouseTo`, `CheckLauncherAndConfig`. (Also contains unrelated hazard-field gameplay code at 0x401040-0x401CE0.) |
| `src/sysinput.c` | `SetMousePosition`, `PollKeyboardState`, `GetShiftKeyState`, `GetControlKeyState`, `GetKeyboardModifiers`, game clock. |
| `src/eventmgr.c` | DOS-era event manager (`source\eventmgr.c` in the FM Towns build): event pool, queue, cursor sprite, frame-timer wrappers, key-state array. |
| `src/strdos.c` | Event manager lifecycle stubs and `SetEventManagerPump`. |
| `src/sdl/events.c` | SDL event pump, scan-code translation, mouse grab. |
| `src/sdl/input.c` | Win32 shims: `GetAsyncKeyState`, `SetCursorPos`, `Sleep`, `GetTickCount`, critical sections, message box. |
| `src/sdl/timer.c` | `timeSetEvent` / `timeKillEvent` over `SDL_AddTimer`. |
| `src/sdl/thread.c` | `CreateThread`, `CreateEventA`, `WaitForSingleObject`, `SetEvent`, `ResetEvent`, `CloseHandle`. |
| `src/sdl/compat.c` | Case-insensitive path resolution, POSIX `_open` shim, `itoa` family. |
| `src/sdl/registry.c` | Registry shim → `wc1-modern.cfg` in the SDL pref path. |
| `src/sdl/launcher.c` | `main()`: argument parsing, SDL init, window creation, calls `GameMain`. |
| `src/sdl/video.c`, `video_state.c`, `video_internal.h` | Window/renderer/present, 4:3 letterbox math, pointer mapping. |
| `src/sdl/audio.c` | `SdlStartAudio`: SDL audio callback wrapping the `ix` mixer. |
| `src/sdl/joystick.c` | SDL joystick/gamepad → `JOYINFO`, hot-plug, WCAT modes, rumble. |
| `src/mono.c`, `src/debug.cpp` | MONODEBG.VXD console and the Win32 developer overlay console (`PumpMessagesDuringWait` lives on it). |
| `include/wc1sdl.h` | The host API surface the SDL port provides to the game. |

Supporting reads: `src/hudmsg.c` (`RunSpaceFlight`, frame timer callback), `src/dib.c`
(`DIBslamReal`, palette, vblank), `src/screen.c` (`ThrottleFrameAndDrawFps`, joystick
calibration, menu pumps), `src/main.c` (`GameMain`, `player_input`), `src/nav.c`
(`Title_Sequence`, `GameFlow`), `src/disk.c` (`CheckEscaped`, modal waits),
`src/brains.c` (joystick sampling), `src/spc.c` (interactive calibration),
`src/sound.c` / `src/gr.c` (registry), `src/ix/*.cpp` (audio threads), `src/logic.c`
(`EMStartUp`), `docs/SDL2.md`, `README.md`.

---

## 1. Architecture overview

```
+---------------------------------------------------------------+
| Game core (C, DOS-era structure, 320x200 8-bit framebuffer)   |
|   main.c / nav.c / hudmsg.c / screen.c / disk.c / ...         |
|   Talks to the host ONLY through:                             |
|     - eventmgr.c  (QueueInputEvent / PollInputEvent / ...)    |
|     - sysinput.c  (PollKeyboardState, GetShiftKeyState, ...)  |
|     - winmain.c   (PumpWindowMessages, GetJoystickPosition,   |
|                    WarpMouseTo, ShutdownGameWindow)           |
|     - dib.c       (DIBslam/DIBslamReal/DIBupdate, palette,    |
|                    DIBwaitForVerticalBlank)                   |
|     - frame timer (SetFrameTimerPeriodDirect/IsFrameTickElapsed)|
|     - registry (LoadVolumeSettingsFromRegistry, cheater flag) |
|     - ix audio library (own threads)                          |
+---------------------------------------------------------------+
| Win32 names (GetTickCount, GetAsyncKeyState, SetCursorPos,    |
| timeSetEvent, CreateThread, RegOpenKeyExA, ...)  <- #defined  |
| to Sdl* shims in wc1sdl.h                                     |
+---------------------------------------------------------------+
| SDL2 host (src/sdl/*): window, renderer, event pump, timers,  |
| threads, audio device, joystick, cfg file, path resolution    |
+---------------------------------------------------------------+
```

Key architectural facts a porter must internalise:

1. **The game is synchronous and blocking.** There is no "update/render" callback. Every
   screen (title, barracks, briefing, cinematic, space flight, nav map, modal prompt) is a
   function that loops internally until it is done. Host events are consumed only when the
   game *polls*: `PumpWindowMessages()` is called from `PollInputEvent`, `CheckEscaped`,
   `GameFlow`, modal waits, and a few other places. If the game is busy (e.g. loading), no
   events are processed and the OS window is unresponsive.
2. **All input is funnelled into one DOS-era queue** (`eventmgr.c`): a 256-slot pool of
   `InputEvent` records in a doubly-linked FIFO. Mouse, keyboard, and (via a "pump"
   callback) joystick samples all become `InputEvent`s with small integer type codes.
3. **Keyboard is dual-channel:** (a) edge events in the queue carrying **DOS scan codes**;
   (b) a level-state array `abInputKeyState[0x80]` indexed by scan code, plus **polled
   virtual-key state** for arrows/Home/End/PgUp/PgDn/Ins/Del/KP5/comma/period/Shift/Ctrl
   via `GetAsyncKeyState`.
4. **Two timing mechanisms:** a one-shot "frame timer" (`timeSetEvent`, period in 1/60 s)
   used by cinematics/menus for timed waits, and a deadline throttle inside the present
   call (`ThrottleFrameAndDrawFps`) that paces *every* `DIBslamReal` to 16 fps
   (cinematic) or ~20 fps (space flight, adjustable 8–32).
5. **Presentation is a single 320x200 indexed frame + 256x4 palette cache**
   (`pDIBPixelBuffer`, `abDIBPaletteCache` in B,G,R,flags order) converted to ARGB8888 and
   streamed to a texture, letterboxed 4:3.
6. **Threads:** only the `ix` audio library creates threads (mixer thread, streamer thread).
   In the SDL port the mixer thread merely starts the SDL audio device and sleeps; mixing
   happens in the SDL audio callback under a critical section. The game thread never
   blocks on audio except at shutdown.

---

## 2. Complete host interface the game core needs

Everything the game core calls that is *not* pure computation. Grouped by subsystem.
Semantics are those observed in the SDL2 port (which is already a working host); where
the Win32 original differs, it is noted.

### 2.1 Video present

| Function (game-facing) | Semantics |
| --- | --- |
| `DIBinstall(window)` / `DIBreInstall()` / `DIBunInstall()` | Create/recreate/destroy the renderer and 320x200 streaming texture (`SdlInitializeVideo`, `SdlShutdownVideo`). `DIBinstall` zeroes the palette cache and allocates the 320x200 DIB (`DIBmakeDIB`). `DIBreInstall` re-creates and immediately presents. |
| `DIBslam()` | Sets `bDIBSlamPending = 1` (marks whole frame dirty). Cheap. |
| `DIBslamReal()` | **The present call.** If pending: draws the software mouse cursor into the DIB if the cursor's viewport *is* the screen (capture background → draw sprite), presents `pDIBPixelBuffer` with `abDIBPaletteCache` (`SdlPresentIndexedFrame`), restores the cursor background, clears pending. Always (pending or not): `nDIBSlamCount++`, `ServiceSoundSystem()`, then `ThrottleFrameAndDrawFps(0)` (frame pacing — see §5.3). |
| `DIBupdate(l,t,r,b)` | Partial update; clamps to 0..319/0..199; in SDL presents the whole frame. Used by `RefreshMouseCursorDisplay`. |
| `DIBwaitForVerticalBlank()` / `WaitForVerticalBlankThunk()` | In SDL: re-present the last texture (vsync paces it) — `SdlWaitForVerticalBlank`. Called before palette writes and in fades (`screens.c:1417/1431`, `screen.c:773`, `sound.c:540/596`, `winmain.c:27`). |
| `DIBwholePaletteFromTriplets(p)` / `DIBwholePaletteFromWords(p)` / `DIBsetPalette(i,rgb)` / `DIBramPalette()` | Write the 256-entry palette cache (`abDIBPaletteCache[i*4] = B,G,R,4`) and re-present (`DIBramPalette` → `SdlPresentIndexedFrame`). Palette is 8-bit per channel in this build (VGA 6-bit values are already scaled by the loaders). |
| `SdlPresentIndexedFrame(pixels, palette)` | Host primitive: 64000 bytes of indices + 1024-byte B,G,R,x palette → ARGB texture → letterboxed `RenderCopy` → `Present`. Returns 0 on failure (game then calls `DIBerror` → message box + `exit(1)`). |
| `SdlBeginSpaceFrame / SdlCompleteSpaceFrame / SdlCancelSpaceFrame / SdlRecordSpaceSprite / SdlSet/GetThrusterScreenPosition` | Optional "enhanced" GL renderer hooks; no-ops in the indexed backend. `SdlCancelSpaceFrame` also calls `SdlEndJoystickSpaceflight` (stops rumble). Not required for a faithful port. |
| `SdlDrawViewportStatic(viewport, effect, colour)` | Port addition: draws noise on knocked-out VDUs (own xorshift RNG so the game RNG is not consumed). Pure software; can live in the game layer. |
| `DIBpositionWindow()` | Before fatal message boxes; no-op in SDL. |
| `ShutdownGameWindow()` | Terminates the process: `bMainWindowAlive = 0`, destroy streamer, `ServiceAudioStream`, destroy debug console, `DIBunInstall`, close joysticks, destroy window, `SDL_Quit`, **`exit(0)`**. Called from the SDL `QUIT`/window-close event *inside the pump* — i.e. closing the window exits from arbitrary depth in the call stack. |

Screen geometry constants: frame 320x200, display aspect 4:3, letterbox computed by
`SdlCalculateOutputViewport` (video_state.c:54): if `w*3 > h*4` the viewport is
`(h*4/3) x h` centred horizontally, else `w x (w*3/4)` centred vertically.

### 2.2 Timing

| Function | Semantics |
| --- | --- |
| `GetTickCount()` / `timeGetTime()` → `SdlGetTicks` | Milliseconds, 32-bit, monotonic since SDL init. |
| `nTickCount60Hz` (global) | `GetTickCount() * 60 / 1000`, **recomputed only at the end of `PumpWindowMessages()`**. Many consumers (barracks, killboard, nav map blink, double-click detection) read it as "60 Hz ticks now". |
| `GetGameClockTicks()` | `(GetTickCount() - dwGameClockBase) * 60 / 1000` — 60 Hz ticks since a randomised epoch (`InitGameClockEpoch`: `base = now + (rand() & 3600000)`). Used for the in-game clock display (`SplitGameClockTicks` → sec/min/hour/day). |
| `QueryPerformanceCounter(&li)` → `ReadPerformanceCounter` | High-resolution counter; used only for profiling fields in `RunSpaceFlight`. |
| `timeSetEvent(delayMs, resMs, cb, user, flags)` / `timeKillEvent(id)` | Multimedia timer. The game creates **one-shot** timers only (flags 0 = `TIME_ONESHOT`). SDL shim: `SDL_AddTimer`, callback runs on SDL's timer thread, returns 0 to not repeat. Max 16 simultaneous. |
| `SetMultimediaTimerCallback(period60)` (hudmsg.c:1401) | period 0 → kill timer, `bFrameTickPending = 0`. Else kill any pending, `bFrameTickPending = 1`, `timeSetEvent(period*1000/60, ...)` with `FrameTimerCallback` which sets `bFrameTickPending = 0`. |
| `SetFrameTimerPeriodDirect(p)` / `SetFrameTimerPeriod(p)` | Wrappers of the above (eventmgr.c:542/555). |
| `IsFrameTickElapsed()` | `bFrameTickPending == 0`. |
| `WaitForFrameTick()` | **Busy spin** `while (bFrameTickPending)` — no pump, no sleep (eventmgr.c:561). Used by `SetFrameTimerAndWait` (nav.c:868) and `AnimateTrainSimTitle` (pilot.cpp:312). |
| `Sleep(ms)` → `SDL_Delay` | Only `Sleep(0)` is used (yield inside the frame throttle). |
| `ThrottleFrameAndDrawFps(dc)` (screen.c:2109) | Called from every `DIBslamReal`. See §5.3. |
| `SetSpaceFlightFrameTiming()` / `SetCinematicFrameTiming()` (dib.c:25/36) | Select frame interval: `1000/fSpaceFlightFrameRate` (default 20 fps) or `1000/fCinematicFrameRate` (16 fps). Reset deadline to 0. |
| `ReportSpaceFlightMaxFps(±0.5)` (dib.c:11) | Alt+N / Alt+M in the Win32 `WM_SYSKEYDOWN` handler: adjust 8..32 fps, show HUD message. Not wired in the SDL port. |
| `time(0)` | Session start/end timestamps and `srand`. |

### 2.3 Keyboard

| Function | Semantics |
| --- | --- |
| Host → game: `QueueInputEvent(3/4, 0, 0, scanCode, 0, 0, 0)` | Key down (3) / up (4) with a **DOS scan code** (0x01..0x58, see §4). Queued for every physical press/release including auto-repeat (SDL repeat events are not filtered except for the F1 latch and the shortcuts). |
| Host → game: optional duplicate event with the **Windows VK code** | When `bKeyEventQueueEnabled != 0` the host queues a *second* 3/4 event whose `value` is the VK code (`'A'..'Z'`, 0x0d Enter, 0x1b Esc, 0x70.. F-keys, etc.) **before** the scan-code event. Consumers that compare against ASCII letters (e.g. `WaitForKeyExceptXOrF12` compares to `'X'` and `VK_F12`; `'Y'` prompts) depend on this. `bKeyEventQueueEnabled` is set to 1 by `GameFlow` after the barracks/rec-room loop, by `Title_Sequence` on exit, and by `WaitForStreamInputKey`; cleared to 0 during `GameFlow`'s room loop and `Title_Sequence`'s menu. |
| Host → game: `SetInputKeyState(scan, pressed)` | Level state `abInputKeyState[scan] = pressed`. Scan must be < 0x80 or the game prints "keyboard almost messed up" and `exit(1)`. Readers: `ClearInputKeyState`, `ClearInputKeyStatePreservingModifiers` (keeps 0x1d Ctrl and 0x38 Alt), and `SdlIsFullscreenShortcut` (reads `[0x38]`). |
| Host → game: `bEscapePressed = 1` on scan 0x01 down | Latched flag polled by cinematics (`brains.c`, `cmpgn.c`) and `Title_Sequence`; the game clears it. |
| Host → game: `bF1KeyLatch` | 1 while F1 is held *and not auto-repeating*; `GetF1KeyLatch()` read in `hudmsg.c:308` (F1 = front view "hold"). Win32 sets it on `WM_KEYDOWN VK_F1` unless bit 30 (repeat) is set; SDL: `pressed && repeat == 0`. |
| Host → game: `nSystemKeyDown` | VK of the key pressed while Alt is held (Win32 `WM_SYSKEYDOWN` wParam), 0 on `WM_SYSKEYUP`. `GetKeyboardModifiers()` returns it; non-zero ⇒ modifiers bit `0x700` in queued events. SDL: set when `KMOD_ALT` or the Alt scancode itself. |
| Host → game: `dwDebugOverlayKey`, `dwDebugOverlayKeyLatch` | Set to the VK code on **key up** (SDL) / keyboard hook with bit 30 (Win32). `PumpMessagesDuringWait()` waits for `dwDebugOverlayKey != 0`, returns it as `char`, and zeroes it. `TakeDebugStepFlag()` consumes the latch. `ClearDebugPauseFlags()` zeroes both. |
| Game → host: `GetAsyncKeyState(vk)` | Level query; returns 0x8000 if down. Only these VKs are ever queried: `VK_SHIFT`, `VK_CONTROL`, `VK_CLEAR`(KP5), `VK_PRIOR`, `VK_NEXT`, `VK_END`, `VK_HOME`, `VK_LEFT/UP/RIGHT/DOWN`, `VK_INSERT`, `VK_DELETE`, `0xbc` (comma), `0xbe` (period). SDL shim calls `SDL_PumpEvents()` first (side effect: events may be pumped into SDL's queue, not the game's). |
| Game → host: `PollKeyboardState()` (sysinput.c:16) | Combines the above into one DOS scan code with priority: Home 0x47, PgUp 0x49, End 0x4f, PgDn 0x51, Ins/comma 0x52, Del/period 0x53, KP5 0x4c, Up(+Left→0x47, +Right→0x49, else 0x48), Down(+Left→0x4f, +Right→0x51, else 0x50), Left 0x4b, Right 0x4d, else 0. Used by `player_input` every flight frame when the mouse cursor is hidden. |
| `GetShiftKeyState()` / `GetControlKeyState()` | `GetAsyncKeyState(VK_SHIFT / VK_CONTROL)`. **SDL quirk:** Ctrl reports *not pressed* while any arrow/Home/End/PgUp/PgDn/KP1-4,6-9 key is held, so Ctrl+arrow steers instead of changing volume. |

### 2.4 Mouse

| Function | Semantics |
| --- | --- |
| Host → game: `QueueInputEvent(13, x, y, 0, lmb, rmb, 0)` on motion | x,y in **logical 320x200 space, clamped 0..319 / 0..199**. SDL coalesces: if the tail event is already type 13 it is overwritten in place (position, buttons, modifier bits 2/4). Ignored once after `bPointerMovedByKeyboard` is set (the game's own warp echo). |
| Host → game: `QueueInputEvent(2, x, y, 0, lmb, rmb, 0)` on button down; type `1` on button up | `lmb`/`rmb` are the *resulting* button states (SDL: current mask with the event's button added/removed). Middle button is ignored. |
| Host → game: `nHostMouseMessageX/Y`, `bHostPrimaryMouseButton`, `bHostSecondaryMouseButton` | Last mouse message position/buttons (globals). Read by `TranslatePolledInputEvent` and `player_input` when no button event is queued (`buttons = rmb*2 | lmb`). |
| Game → host: `SetMousePosition(x, y)` → `SetCursorPos` → `SdlSetCursorPosition` | Warp the OS pointer to logical (x,y) mapped into the letterboxed window (`SdlMapLogicalToWindow`, rounding half-up). |
| Game → host: `WarpMouseTo(x, y)` (winmain.c:466) | Sets `stHostMouseState.x/y`, `stMouseCursorState.x/y`, then `SetMouseHomePosition` (which warps). Win32 wraps it in `cli/sti` (atomic vs. the DOS interrupt-era event manager). |
| Mouse wheel (SDL port addition) | Wheel up → key events **4 then 3** with scan 0x0d (`=`, speed up); wheel down → 0x0c (`-`, slow down). Release queued *before* press because `player_input` samples one event then consumes the rest. |
| `SdlSetMouseGrab(on)` | Requested only by `RunSpaceFlight` (1 at entry, 0 at exit). Effective grab = requested && window focused && suspend depth == 0 → `SDL_SetWindowMouseGrab`. |
| `SdlSuspendMouseGrab()` / `SdlResumeMouseGrab()` | Depth-counted; wrapped around `WaitForKeyAcknowledge` (pause, modal prompts) so the pointer is free during pauses. |
| Win32 original | `ClipCursor` to 0,0,320,200 (window is a 320x200 `WS_POPUP`), `ShowCursor(FALSE)`; released on minimise. |

### 2.5 Joystick

| Function | Semantics |
| --- | --- |
| `GetJoystickPosition(&x, &y, &buttons, joystick, fallback)` (winmain.c:981) | For device 0/1: on success `x = wXpos`, `y = wYpos`, `buttons = wButtons & 3` (device 0) or `wButtons >> 2` (device 1); returns 0. On failure returns 1 and sets x, y, buttons to `fallback & 0xffff` (SDL: `0xffff` → `-1`). Caller semantics: `x == -1` means "no joystick" during detection (`fallback = 0xffff`). |
| `SdlReadJoystick(dev, JOYINFO*)` | Host primitive: `wXpos/wYpos = axis + 32768` (0..65535), `wZpos = 0`, `wButtons` bits 0..1 (device 1 shifted `<< 2`). Calls `SDL_JoystickUpdate` and hot-plug refresh every call. In WCAT modes only button 0 is reported when the flight pump is active. |
| `GetJoystickDevCaps(joystick, &xMin, &xMax, &yMin, &yMax)` | Axis ranges; SDL reports 0..0xffff. |
| `GetJoystickButtons()` | `(info[1].wButtons << 2) | info[0].wButtons` from the last read. |
| `JoystickEdgeHook` / `GetJoystickButtonEdge` / `StartupHook` | DOS-era no-op stubs. |
| SDL port extras (optional) | `SdlApplyJoystickFlightControls()` (called at the end of `player_input`), `SdlHandleJoystickButtonEvent/HatEvent/DeviceEvent`, rumble queue, WCAT modes, comm-menu D-pad selection (`SdlGetCommunicationMenuSelection` read in `screen.c:1526`). |

### 2.6 Audio callbacks (host side of the `ix` library)

| Function | Semantics |
| --- | --- |
| `SdlStartAudio(mixer, &criticalSection, &tick)` | Open the default device: **22050 Hz, S16 native, 2 channels, 1470 sample frames (≈66.7 ms)**. Callback: `Enter(cs); mixer(stream, bytes); SdlMixOriginFxMusic(stream, frames); (*tick)++; Leave(cs)`. Unpause immediately. Returns FALSE if already open with a different owner is *not* checked — first caller wins (`if (g_wAudioDevice) return TRUE`). |
| `SdlStopAudio()` | Close device, quit audio subsystem. |
| Mixer thread lifecycle (`ix/dsp.cpp:41`, `ix/mixer.cpp:38`) | `ix_dsp_init`: `InitializeCriticalSection(&csMixer)`, `CreateEventA(manualReset)`, `CreateThread(ix_mixer_thread_proc)`. The thread sets priority TIME_CRITICAL, calls `SdlStartAudio(ix_dspv_mix, &csMixer, &dwDspTick)`, then loops `WaitForSingleObject(hMixerWakeEvent, INFINITE); ResetEvent` until `dwDspFlags & 4` clears; then `SdlStopAudio`. Shutdown: clear flag, `SetEvent`, join. |
| Streamer thread (`ix/streamer.cpp`) | Second thread reading `.STR` music files; uses `csStreamer*` critical sections and `WaitForSingleObject(hStreamerWakeEvent, timeout)`. |
| Game-thread hooks | `ServiceSoundSystem()` (every `DIBslamReal`), `ServiceAudioStream()` (shutdown), `UpdateStreamerStoppedFlag()`, `SignalAudioMixerWakeEvent()` (Win32 `WM_SETFOCUS` → `ix_streamer_configure(2,0)`), `SdlServiceOriginFxMusic()` (every `SdlPumpEvents`). |
| DOS-data path | `SdlInitializeOriginFxAudio(standalone)` may open the device itself with `SdlMixDosAdlibMusic`; `bIxAudioEnabled = 0` then. |

### 2.7 Filesystem / paths

| Function | Semantics |
| --- | --- |
| `SdlResolvePath(path, out, size)` | Non-Windows: case-insensitive, `\`→`/`, component-by-component directory lookup. Windows: identity. Used for `WINGCMDR.CFG`, savegames (`cmpgn.c:81`), packet files. |
| `_chdir("gamedat")` / `_chdir("..")` | `GameMain` changes CWD to load `INSTALL.DAT`. The game addresses all data relative to CWD. |
| `_open/_read/_write/_lseek/_close/_filelength/_unlink` | Low-level file I/O (packet files, `j.cal`, savegames). `_open` flags: `0x8000` binary, `0x0100` create, `0x0200` trunc, `0x0400` excl, `0x0008` append; mode `0x180` = rw user. |
| `fopen("WINGCMDR.CFG","rt")` | Read twice: `CheckLauncherAndConfig` (flags b,c,f,k,q and `$#SAGA.EXE`) and `LoadWingCmdrCfgFile` (whitespace-separated tokens become extra argv for `GameMain`). |
| `GetCurrentDirectoryA`, `SetCurrentDirectoryA`, `GetDriveTypeA`, `GetVolumeInformationA` | CD-ROM detection (`PromptInsertCorrectCd`, Win32 only); SDL stubs return failure/0. |
| `CreateFileA("\\\\.\\MONODEBG.VXD")`, `DeviceIoControl` | Developer mono-monitor driver; SDL returns `INVALID_HANDLE_VALUE` so `MonoDebug_install` is a no-op. |
| `SdlUsingDosData()` | Reads byte 7 of `GAMEDAT/MODULE.000`: 1 ⇒ DOS compressed packets, else Kilrathi Saga. Affects audio path and intro. |

### 2.8 Config persistence

| Key (HKLM\Software\Origin Systems\WC: Kilrathi Saga) | Type | Default | Reader / writer |
| --- | --- | --- | --- |
| `MusicVolume` | DWORD 0..20 | 20 (0x14), written back if missing | `LoadVolumeSettingsFromRegistry` (sound.c:198) at `GameMain` start; `SaveVolumeSettingsToRegistry` on every Ctrl+arrow volume change in flight (main.c:565/577/615/658). Applied as `anVolumeLevels[v/2]`. |
| `SFXVolume` | DWORD 0..20 | 20 | same |
| `Cheater` | DWORD | absent = 0 | `ReadCheaterFlagFromRegistry` (gr.c:1035) from `CheckLauncherAndConfig`: non-zero ⇒ `nOriginDevUnlock = 1`, `bPlayerVulnerable = 0`, `bPlayerCollisionResponse = 0`. Never written by the game. |

SDL port stores these as a text file `<SDL_GetPrefPath("Origin Systems","WC Kilrathi Saga")>/wc1-modern.cfg`
with the exact format `MusicVolume=%u\nSFXVolume=%u\nCheater=%u\n` (all three must parse or
none are considered present). **The C# port must persist exactly these three integers.**

Other persisted files (game-layer, not host): `j.cal` (joystick calibration, 7 × int16
little-endian: activeDevice, minX, minY, maxX, maxY, centreX, centreY — written by
`CalibrateJoystickInteractive`, read by `LoadJoystickCalibrationFile`), savegames and
`WINGCMDR.CFG`.

### 2.9 Threading / synchronisation primitives used

| Primitive | SDL mapping | Used by |
| --- | --- | --- |
| `CRITICAL_SECTION` (`Initialize/Enter/Leave/Delete`) | `SDL_mutex` | `csMixer` (audio callback vs. game thread), `csStreamer`, `csStreamerThread`, `csStreamerFileQueue`, per-stream `cs`, DOS AdLib music CS. |
| `CreateEventA(manualReset, initial)` / `SetEvent` / `ResetEvent` / `WaitForSingleObject(h, ms|INFINITE)` | mutex + condvar; `WaitForSingleObject` on a *thread* handle joins it | mixer wake, streamer wake, stream-file completion events. |
| `CreateThread` / `CloseHandle` / `GetCurrentThread` / `SetThreadPriority` | `SDL_CreateThread`, join on close, `SDL_SetThreadPriority(HIGH)` | mixer thread, streamer thread. The debug overlay worker thread exists in Win32 only. |
| `SDL_SpinLock`, atomics | timer table | `timer.c` only. |

### 2.10 Miscellaneous

| Function | Semantics |
| --- | --- |
| `MessageBoxA(hwnd, text, title, type)` | Modal error/notice (`SDL_ShowSimpleMessageBox`); returns `IDCANCEL` for `MB_OKCANCEL` else `IDOK`. Used by `DIBerror`, `ShowNoticeMessageBox`, `ReportHeapGuardCorruption`, CD prompt. All followed by `exit`. |
| `OutputDebugString(s)` / `WriteDebugString` / `SystemDebugPrintf` / `SoundDebugPrintf` / `MonoDebug_print` | Diagnostics; SDL discards unless `SDL_PORT_LEGACY_DEBUG_OUTPUT`. |
| `exit(n)` / `_onexit(AbortToDesktop)` | Many fatal paths call `exit` directly. `AbortToDesktop` logs guarded-heap stats. |
| `AllocateGuardedMemory(size)` / `FreeGuardedAllocation(p)` / `CheckAllGuardedAllocations()` | Debug heap: 0x400 bytes of 0xAB before and after each block, linked list; corruption ⇒ message box + exit. Pure C; in C# use plain arrays/`ArrayPool` and drop the guards (keep the API if the core is transliterated). |
| Single-instance semaphore "Wing Commander 1", memory checks ≥ 8 MB, `waveOutGetNumDevs` | Win32 `WinMain` only; not needed. |
| `CreateDebugOverlayConsole(inst, hwnd, 60, 20)` / `DestroyGlobalDebugOverlayConsole()` | Creates `pDebugOverlay` (`DebugOverlayConsole`, 60x20 text grid). Only its `WaitForKey()` matters (see §11). |

---

## 3. Event manager semantics (eventmgr.c)

### 3.1 Data structures

```c
/* pool record, stride 0x1C, 256 slots (aInputEventPool / aiInputEventSlotUsed) */
typedef struct InputEvent {
    short type;            /* +0x00  event code (see 3.2) */
    short x;               /* +0x02  logical screen x (mouse/joystick) */
    short y;               /* +0x04  logical screen y */
    short value;           /* +0x06  scan code or VK code (key events) */
    unsigned int modifiers;/* +0x08  see 3.3 */
    unsigned int timestamp;/* +0x0C  NEVER WRITTEN in this build - always 0 */
    short primaryButton;   /* +0x10  left button state at queue time */
    short secondaryButton; /* +0x12  right button state */
    InputEvent *next, *previous;
} InputEvent;

/* record handed to the game (packed, 16 bytes) */
typedef struct InputEventState {
    short type;            /* +0x00  set only by PeekInputEvent */
    unsigned int value;    /* +0x02  GetNext: scan/VK/button mask ; Peek: event->modifiers (!) */
    unsigned int timestamp;/* +0x06  set only by PeekInputEvent (always 0) */
    short modifiers;       /* +0x0A  GetNext: event->modifiers ; Peek: 1|2|4 encoding (see 3.5) */
    short x, y;            /* +0x0C/+0x0E */
} InputEventState;

typedef struct MouseCursorState {           /* stMouseCursorState and stHostMouseState */
    volatile short x, y;                    /* logical pointer position */
    unsigned char primaryButton, secondaryButton, reserved;
    unsigned short flags;
    unsigned char *volatile shape;          /* cursor sprite packet (FetchDiskPacket(14,0)) */
    unsigned short frame;                   /* sprite frame = cursor shape (0 arrow, 2 flight crosshair, menu-specific) */
    unsigned int reservedAfterFrame;
    Viewport *volatile viewport;            /* clamp rect + where the cursor is drawn (&stScreen, &stSpaceBuffer, nav sub-rect) */
    unsigned int reservedAfterViewport;
    unsigned char shapeChanged;
} MouseCursorState;                         /* 0x1C bytes packed */

typedef struct InputDeviceSample { int x; int y; unsigned int buttons; } InputDeviceSample;
/* aInputDeviceSamples[2]: raw 0..65535 before calibration, normalised -9..9 after */
```

### 3.2 Event type codes

| Code | Meaning | Queued by (this build) | `GetNextInputEvent` output |
| --- | --- | --- | --- |
| 1 | Mouse button **up** | host (Win32 `WM_xBUTTONUP`, SDL `MOUSEBUTTONUP`), `PollJoystickButtonEvents`/`PollMenuInputDevices` via `QueueInputEventAtCursor(buttons&1 + 1 …)` when a joystick button *releases* | cursor x,y := event; `cursor.primaryButton = 0`; state.x,y |
| 2 | Mouse button **down** (also "joystick button down" via the pumps) | host, pumps, `TranslatePolledInputEvent(2)` | cursor x,y,buttons := event; state.x,y; `state.value = rmb*2 | lmb` |
| 3 | Key **down** | host (scan code; plus VK duplicate when `bKeyEventQueueEnabled`), joystick button→scan mapping, wheel | `state.value = scan/VK`; state.x,y = cursor x,y |
| 4 | Key **up** | host | state.x,y = cursor (**value is lost**; readable only via `PeekInputEvent`/`IsInputEventQueued(4)`) |
| 5 | DOS-era "character" key event | **never queued** in Win32/SDL; consumers treat `3 || 5` identically | state.x = value |
| 6 | Joystick sample (flight) | `TranslatePolledInputEvent(6)` from `get_player_input` when the sample changed or is non-zero | state.x,y = sample x,y (normalised -9..9) |
| 7, 8, 9 | unused | never | passthrough x,y |
| 10 | DOS-era joystick button / "accept" | **never queued**; consumers treat like 2 (`CheckEscaped`, `WaitForInputKey`, `WaitForSceneAdvance`, nav map) | passthrough x,y |
| 13 | Mouse **move** | host, `MoveMenuPointerFromKeyboard` (keyboard-driven pointer), `RunSpaceFlight` exit (`13,160,100`) | cursor x,y := event; state.x,y |

All other codes return 0 from `GetNextInputEvent` *after removing the event*.

### 3.3 Modifier bits (`InputEvent.modifiers`, computed in `QueueInputEvent` at queue time)

| Bit(s) | Set when |
| --- | --- |
| `0x00E0` | Shift down (`GetShiftKeyState() != 0`) — note assignment `modifiers = 0xe0`, not OR; it is the first test so equivalent |
| `0x2000` | Ctrl down (`GetControlKeyState() != 0`) |
| `0x0700` | Alt/system key active (`GetKeyboardModifiers()` i.e. `nSystemKeyDown != 0`) |
| `0x0002` | primaryButton argument non-zero |
| `0x0004` | secondaryButton argument non-zero |

Consumers: `player_input` stores `wCurrentInputModifiers = event.modifiers` and checks
`& 0x2000` (Ctrl) in `HandleSpaceFlightControls` (eject); `& 4` (right button) decides
mouse afterburner mode on type 13 and "lasers while afterburning" on type 2; `PeekInputEvent`
re-encodes buttons as 1/2/4 (below).

These are read on the game thread via `GetAsyncKeyState` **at queue time**, so modifier
state is sampled when the host delivers the event, not when it is consumed.

### 3.4 Queue behaviour

* Fixed pool of 256 records (`AllocateInputEvent` linear scan for a free slot; first call
  initialises the pool). Doubly-linked FIFO `pInputEventHead`..`pInputEventTail`.
* **Overflow policy:** if no slot is free, `QueueInputEvent` calls `ReleaseInputEventQueue()`
  — the *entire* queue is dropped, including the new event.
* `FlushInputEvents()` = drop everything. Called liberally (entry/exit of every screen,
  after every modal accept).
* `RetainInputEventsOfType(t)` = drop everything except type `t` (used by
  `MoveMenuPointerFromKeyboard` to keep key-downs while replacing the pointer position).
* The SDL host coalesces consecutive mouse-move events in place (tail already type 13).
  Win32 queued one per `WM_MOUSEMOVE`.
* `GetNextInputEvent` **clamps the head event's x/y into `stMouseCursorState.viewport`**
  (left/top/right/bottom, inclusive) before dispatch, mutating the record. For key events
  x,y are 0 and get clamped to the viewport's top-left, but the returned state uses cursor
  x,y anyway.
* `timestamp` is never populated. `QueueInputEvent`'s `timestamp` parameter is ignored.

### 3.5 API

| Function | Behaviour |
| --- | --- |
| `QueueInputEvent(type, x, y, value, lmb, rmb, ts)` | Append (see 3.3/3.4). |
| `QueueInputEventAtCursor(type, lmb, rmb)` | `QueueInputEvent(type, cursor.x, cursor.y, 0, lmb, rmb, 0)`. The pumps pass `doubleClick` (0 or 3) as `rmb` so bit 4 of modifiers means "double-click" for joystick-generated button events. |
| `GetNextInputEvent(&state)` | Pop head → `state` per §3.2; returns type or 0 if empty. Does **not** pump. |
| `PollInputEvent(&state, filter)` | `PumpWindowMessages(); return GetNextInputEvent(&state)`. The `filter` argument (always `0xff`) is ignored. **This is the primary blocking-loop primitive.** |
| `PeekInputEvent(&state, type)` | Find first event of `type` without removing: `state.type = type; state.value = event.modifiers; state.timestamp = 0; state.modifiers = (type==1||type==2 ? 1 : 0) | (lmb?2:0) | (rmb?4:0); state.x/y`. Returns 1/0. Used by `CheckEscaped` which then does `escaped = value + 1` (so any match ⇒ non-zero). |
| `IsInputEventQueued(type)` | 1 if any event of that type is queued. |
| `FlushInputEvents()` | Drop all. |
| `RetainInputEventsOfType(type)` | Keep only `type`. |
| `TranslatePolledInputEvent(type, value)` | Re-queue a *synthetic* event from host globals: type 2 → `(nHostMouseMessageX/Y, lmb, rmb)`; type 6 → active joystick sample; type 13 → host mouse position. Called by `player_input` with the type it just popped (so a popped mouse-down is re-queued once; harmless because it is consumed in the same frame's drain loop). |
| `SetEventManagerPump(fn)` | Installs `pEventManagerPump`, invoked at the start of every `PumpWindowMessages`. Values: `0`, `get_player_input` (flight: joystick → type 6), `PollJoystickButtonEvents` (cinematics: joystick buttons → 1/2 at cursor), `PollMenuInputDevices` (menus: joystick axes move the pointer, buttons → 1/2, warps OS pointer). |
| `InitializeEventManager(20, InitializeEventManagerResources, 0)` (`EMStartUp`, logic.c:494) | DOS-era init with **period 20** (in 1/60 s? see §5.6). Loads the cursor shape packet (14,0), sets cursor viewport `&stScreen`, `nInputTickScale = 20`, pump = `PollJoystickButtonEvents`, `nMenuInputRepeatDelay = 6`. |
| `EnterAllocationScope()` / `LeaveAllocationScope()` | Despite the name: increment/decrement `nMouseCursorShowCount` (software cursor visible when > 0). |
| `SetMouseCursorShape(shape, frame)` / `ConfigureEventManagerPointer` | Cursor sprite and frame; restores the previously drawn background first. |
| `SetMouseHomePosition(x,y)` / `ApplyPackedMousePosition(pt)` | Set cursor x,y and warp OS pointer. |
| `RefreshMouseCursorDisplay()` | Capture → draw → `DIBupdate(damage rect)` → restore. Called in the frame throttle spin while the cursor viewport is the screen (so the cursor animates during waits). |

### 3.6 `PumpWindowMessages()` (winmain.c:750) — the only place host events enter

```
if (bWindowMessagePumpActive) return 1;        // re-entrancy guard
bWindowMessagePumpActive = 1;
if (pEventManagerPump) pEventManagerPump();     // joystick sampling → queue
SdlPumpEvents();                                // SDL: SdlServiceOriginFxMusic(); drain SDL_PollEvent
                                                // Win32: PeekMessage/GetMessage loop, minimise handling
nTickCount60Hz = GetTickCount() * 60 / 1000;    // 60 Hz tick snapshot
bWindowMessagePumpActive = 0;
return bMainWindowAlive;                        // 0 only after the window was closed
```

Win32 minimise behaviour: while iconic the loop blocks in `GetMessage` (game frozen),
releases cursor clip, drops priority; on restore it re-installs DirectDraw, re-presents,
re-clips, warps the pointer to 160,100 and calls `init_player_input()`.

SDL event dispatch (`SdlPumpEvents`, events.c:525): `QUIT` → `ShutdownGameWindow()`
(exits process); window close → same; focus gained/lost → re-apply grab; size/display
change → re-apply grab; key down/up → §4; wheel → 0x0d/0x0c; mouse motion/buttons →
map window→logical, clamp, queue; joystick device add/remove, controller/joystick button,
hat → joystick layer.

**Call sites of `PumpWindowMessages`** (direct): `PollInputEvent`, `CheckEscaped`,
`WaitForKeyAcknowledge` (twice), `GameFlow` room loop, `DebugOverlayConsole::WaitForKey`
(SDL), `CreateMainWindow` (Win32 ×3), `Title_Sequence` intro loop (nav.c:1832).
Indirect: everything that calls `PollInputEvent`/`CheckEscaped`/`WaitForInputKey`/
`WaitForSceneAdvance`/`PumpMessagesDuringWait`.

**Not pumped:** `WaitForFrameTick` spin, `ThrottleFrameAndDrawFps` spin, joystick
calibration `WaitForJoystickButtonPress/Release` loops (they only call `SampleJoystickDevice`
→ `SdlReadJoystick` → `SDL_JoystickUpdate`, which does *not* process window events), file
loading, `RunSpaceFlight` frames that do not reach `PollInputEvent` (they always do via
`player_input`).

### 3.7 Software mouse cursor

The cursor is a game sprite, not an OS cursor (OS cursor hidden). `nMouseCursorShowCount`
> 0 and `stMouseCursorState.viewport/shape` non-null ⇒ drawn into the DIB at present
time (`DIBslamReal`) when `viewport->pixels == pDIBPixelBuffer`, with background
save/restore (`abCursorSaveArea`) and a 32x32 damage rectangle. Frames: 0 arrow, 2 in-flight
crosshair (set when the mouse moves in flight), menu-specific frames via
`UpdateTitleMenuCursor` / `UpdateRoomMenuCursor` / `UpdateTrainSimMenuCursor`.

`bMouseCursorVisible` (separate flag) is the flight-mode "mouse is steering" indicator;
`bPointerMovedByKeyboard = 1` tells the host to ignore the *next* motion event (the echo
of a warp).

### 3.8 Input pumps in detail

`get_player_input` (main.c:433, flight): if `nActiveInputDevice != -1` and not re-entered:
`UpdateInputDeviceTransitions(0)` (calibrated read + button edge/double-click bookkeeping);
if the sample is all-zero and unchanged from `stLastPolledFlightInput` → nothing; else queue
type 6 and remember the sample.

`PollJoystickButtonEvents` (screen.c:1063, cinematics/intro): raw read; for each button whose
state changed queue type `(state&1)+1` (2 = pressed, 1 = released) at the cursor with
`doubleClick` 3 in the rmb slot; update `stHostMouseState.primary/secondaryButton`.

`PollMenuInputDevices` (screen.c:1103, menus): calibrated read; axis values × `nMenuPointerSpeed`
(2 default, 1 in barracks) move `stHostMouseState`, clamp 0..319/0..199, `FlushInputEvents()`
on movement then `SetMousePosition` (OS warp → host echoes a type 13); buttons as above;
copies host state into `stMouseCursorState` under `Leave/EnterAllocationScope`.

`UpdateInputDeviceTransitions(raw)` (screen.c:1011): double-click if the same button is
pressed again within `nInputDoubleClickInterval(1) * nInputTickScale(20)` = 20 ticks of
`nTickCount60Hz` (≈333 ms). Sets `asInputButton{1,2}{Changed,DoubleClick}[device]`.

---

## 4. Scan codes and key mapping

### 4.1 `SdlTranslateScanCode` — complete table (SDL scancode → DOS set-1 make code)

| DOS | SDL scancode(s) | Key | | DOS | SDL scancode(s) | Key |
| --- | --- | --- | --- | --- | --- | --- |
| 0x01 | ESCAPE | Esc | | 0x2C | Z | Z |
| 0x02–0x0A | 1 … 9 | 1–9 | | 0x2D | X | X |
| 0x0B | 0 | 0 | | 0x2E | C | C |
| 0x0C | MINUS | - | | 0x2F | V | V |
| 0x0D | EQUALS | = | | 0x30 | B | B |
| 0x0E | BACKSPACE | Backspace | | 0x31 | N | N |
| 0x0F | TAB | Tab | | 0x32 | M | M |
| 0x10 | Q | Q | | 0x33 | COMMA | , |
| 0x11 | W | W | | 0x34 | PERIOD | . |
| 0x12 | E | E | | 0x35 | SLASH | / |
| 0x13 | R | R | | 0x36 | RSHIFT | Right Shift |
| 0x14 | T | T | | 0x37 | KP_MULTIPLY | KP * |
| 0x15 | Y | Y | | 0x38 | LALT, RALT | Alt |
| 0x16 | U | U | | 0x39 | SPACE | Space |
| 0x17 | I | I | | 0x3A | CAPSLOCK | Caps Lock |
| 0x18 | O | O | | 0x3B–0x44 | F1 … F10 | F1–F10 |
| 0x19 | P | P | | 0x45 | NUMLOCKCLEAR | Num Lock |
| 0x1A | LEFTBRACKET | [ | | 0x46 | SCROLLLOCK | Scroll Lock |
| 0x1B | RIGHTBRACKET | ] | | 0x47 | HOME, KP_7 | Home / KP7 |
| 0x1C | RETURN, KP_ENTER | Enter | | 0x48 | UP, KP_8 | Up / KP8 |
| 0x1D | LCTRL, RCTRL | Ctrl | | 0x49 | PAGEUP, KP_9 | PgUp / KP9 |
| 0x1E | A | A | | 0x4A | KP_MINUS | KP - |
| 0x1F | S | S | | 0x4B | LEFT, KP_4 | Left / KP4 |
| 0x20 | D | D | | 0x4C | KP_5 | KP5 |
| 0x21 | F | F | | 0x4D | RIGHT, KP_6 | Right / KP6 |
| 0x22 | G | G | | 0x4E | KP_PLUS | KP + |
| 0x23 | H | H | | 0x4F | END, KP_1 | End / KP1 |
| 0x24 | J | J | | 0x50 | DOWN, KP_2 | Down / KP2 |
| 0x25 | K | K | | 0x51 | PAGEDOWN, KP_3 | PgDn / KP3 |
| 0x26 | L | L | | 0x52 | INSERT, KP_0 | Ins / KP0 |
| 0x27 | SEMICOLON | ; | | 0x53 | DELETE, KP_PERIOD | Del / KP. |
| 0x28 | APOSTROPHE | ' | | 0x56 | NONUSBACKSLASH | ISO extra key |
| 0x29 | GRAVE | ` | | 0x57 | F11 | F11 |
| 0x2A | LSHIFT | Left Shift | | 0x58 | F12 | F12 |
| 0x2B | BACKSLASH | \ | | 0 | anything else | ignored (no event) |

Note: the Win32 build used the hardware scan code from `lParam` bits 16–23 and so received
extended keys (arrows vs. keypad) with the same base codes; the SDL table reproduces that by
mapping both to one code. The game cannot distinguish arrow keys from the numeric keypad.

### 4.2 `SdlTranslateVirtualKey` — the VK duplicate event (`bKeyEventQueueEnabled`)

`a..z` → `'A'..'Z'`; F1..F12 → 0x70..0x7B; Enter/KP Enter → 0x0D; Esc → 0x1B;
Backspace → 0x08; Tab → 0x09; Shift → 0x10; Ctrl → 0x11; Alt → 0x12; PgUp 0x21; PgDn 0x22;
End 0x23; Home 0x24; Left 0x25; Up 0x26; Right 0x27; Down 0x28; Ins 0x2D; Del 0x2E;
comma 0xBC; period 0xBE; any other keycode ≤ 0xFFFF passes through as-is (so digits are
`'0'..'9'`, space 0x20, punctuation as ASCII).

Game-side consumers of VK values: `WaitForKeyExceptXOrF12` (`'X'`, `VK_F12`=0x7B),
Y/N prompts (`'Y'`, and the joystick layer also queues scan 0x15), text entry for pilot
name/callsign (`WaitForStreamInputKey` forces `bKeyEventQueueEnabled = 1`, then
`WaitForInputKey` returns `(signed char)event.value` and rejects 0x1D). Because both
events are queued (VK first, scan second), text entry receives e.g. `'A'` then `0x1E` for
one keypress; the text code accepts printable ASCII and ignores the scan code (verify in the
text-entry port).

### 4.3 Game-level scan codes referenced (for the porter's cross-reference)

| Scan | Key | Used as |
| --- | --- | --- |
| 0x01 | Esc | `bEscapePressed` latch; skip cinematic; back (joystick Back button → 0x1b VK + 0x01) |
| 0x0C / 0x0D | - / = | speed down/up (wheel maps here) |
| 0x0E | Backspace | full stop (`anShipSpeed[0] = 0`); WCAT D-pad down |
| 0x0F / 0x37 | Tab / KP* | afterburner (`your_afterburner`); 0x0F also synthesised from double right-click / joystick button-2 double-click |
| 0x11 | W | weapon select VDU (`SelectCockpitVduMode(0,1)`); WCAT right shoulder |
| 0x22 | G | gun select VDU (`SelectCockpitVduMode(0,1)`); WCAT left shoulder |
| 0x26 | L | toggle target lock mode; WCAT D-pad right |
| 0x2B | \ | full throttle (`accelerate(9000)`); WCAT D-pad up |
| 0x20 | D | damage VDU (`SelectCockpitVduMode(0,2)`) |
| 0x2F | V | Ctrl+V version banner |
| 0x14 | T | target VDU (`SelectCockpitVduMode(1,3)`); WCAT Y button in 4-axis mode |
| 0x15 | Y | yes |
| 0x19 | P | pause banner (`ShowGamePausedBanner`, Ctrl+P variant); gamepad Start |
| 0x1C | Enter | accept; mouse both buttons / joystick both buttons ⇒ 0x1C (missile fire) |
| 0x1D | Ctrl | ignored as a text key |
| 0x1E | A | autopilot (also right trigger/stick in WCAT) |
| 0x1F | S | start/new game in title; Ctrl+S sound toggle in flight |
| 0x12 | E | Ctrl+E eject |
| 0x23 | H | wingman order |
| 0x24 | J | joystick calibration from the title menu |
| 0x2E | C | continue/load in title; comm (D-pad left in WCAT) |
| 0x31 | N | nav map / next objective (left trigger/stick in WCAT) |
| 0x32 | M | title menu option 2; Ctrl+M music toggle |
| 0x33 / 0x34 | , / . | roll left/right (keyboard roll; `PollKeyboardState` maps Ins/comma → 0x52, Del/period → 0x53) |
| 0x39 | Space | fire guns; left mouse button ⇒ 0x39 |
| 0x47–0x53 | keypad/arrows | flight steering via `PollKeyboardState`; menu pointer via `MoveMenuPointerFromKeyboard` (0x4C toggles step 1↔4, step = 2× `nKeyboardPointerStep`, pointer clamped 0..320/0..320 — note the sloppy 320 clamp) |
| 0x3B | F1 | front view hold (`bF1KeyLatch`) |

### 4.4 Port-level shortcuts intercepted by the host (never reach the game)

* Alt+Enter (Win/Linux) / Cmd+Enter (macOS): toggle `SDL_WINDOW_FULLSCREEN_DESKTOP`
  (temporarily releases mouse grab). Detected via keysym *or* scancode, with
  `KMOD_ALT`/`abInputKeyState[0x38]`.
* Cmd+Q (GUI modifier + Q, non-repeat): `ShutdownGameWindow()`.
* Win32 original: Alt+X quit, Alt+N / Alt+M frame-rate ±0.5, `SC_SCREENSAVE`/`SC_MONITORPOWER`
  suppressed.

---

## 5. Timing model

### 5.1 Clocks

| Clock | Source | Resolution | Consumers |
| --- | --- | --- | --- |
| `GetTickCount()` | ms since init | 1 ms (Win32: ~10–16 ms) | frame deadline, `nTickCount60Hz`, game clock, rumble |
| `nTickCount60Hz` | `ms*60/1000` sampled per `PumpWindowMessages` | 1/60 s, but only advances when the game pumps | double-click window (20 ticks), barracks/killboard page timing, nav blink (`/15`), cockpit light blink (`/40 % 3`), `dwLastSecondaryButtonPress` |
| `GetGameClockTicks()` | `(ms - epoch)*60/1000` | 1/60 s | cockpit chronometer |
| QPC | SDL performance counter | ns-ish | profiling only (`liFlight*`) |

### 5.2 One-shot frame timer (menus, cinematics, scripted waits)

`SetFrameTimerPeriodDirect(n)` arms a one-shot of `n * 1000 / 60` ms and sets
`bFrameTickPending = 1`; the timer callback (on the SDL timer thread) clears it.
`IsFrameTickElapsed()` polls; `WaitForFrameTick()` **busy-spins without pumping**.
`period 0` cancels and marks elapsed. Only one timer is outstanding at a time (re-arming kills
the previous). `nFrameTimerId` holds the handle.

Observed call sites and periods (1/60 s units):

| Site | Period | Pattern |
| --- | --- | --- |
| `barracks.c:842/854` | 0 then 2 | per-page animation step: `if (IsFrameTickElapsed()) { step; arm(2) }` inside a `PollInputEvent` loop |
| `brains.c:1864` | `duration` | `while (!IsFrameTickElapsed()) { ... CheckEscaped ... }` cinematic hold |
| `cmpgn.c:570/587/618/1016/1049` | `duration` | scene-script waits |
| `disk.c:474` (`WaitForSceneAdvance`) | `duration` or 0 | `while (!elapsed && !advanced) PollInputEvent…` |
| `hudmsg.c:489/530` | 1 | HUD message cadence |
| `killbrd.c:334/440/484` | 0 / 9 / 1 | killboard animation |
| `logic.c:528` | 0x78 (2 s) | — |
| `logic.c:3025` | `duration/2` | `while (!elapsed && !CheckEscaped())` |
| `nav.c:868` | 20 | `SetFrameTimerAndWait(20)` — **busy spin** ≈ 333 ms |
| `pilot.cpp:262` | 0x2D0 (12 s) | high-score table display with `DIBslam/DIBslamReal` + `CheckEscaped` per iteration |
| `pilot.cpp:301/312` | 3 | title scroll: arm(3) → draw → present → **`WaitForFrameTick()` spin** |
| `screen.c:778/783` | 8 | fade step |
| `screens.c:321/340/1337` | `duration` | image display timers |

Interpretation: `period` is a duration in 60ths of a second, i.e. the DOS PIT tick unit.

### 5.3 Present-time throttle (every frame, both cinematic and flight)

`DIBslamReal()` → `ThrottleFrameAndDrawFps()` (screen.c:2109):

```
while (timeGetTime() < nFrameDeadlineMs) {
    Sleep(0);                                       // yield
    if (cursor viewport is the screen) RefreshMouseCursorDisplay();   // animates cursor during the wait
}
if (bShowFrameRate) { measure fps (TextOutA — stub in SDL) }
nFrameDeadlineMs = timeGetTime() + nFrameIntervalMs;
```

* `nFrameIntervalMs = 62` initially; `SetCinematicFrameTiming()` ⇒ `1000/16 = 62 ms`
  (16 fps) — set at `GameMain` start (main.c:141) and at `RunSpaceFlight` exit (hudmsg.c:912).
* `SetSpaceFlightFrameTiming()` ⇒ `1000/fSpaceFlightFrameRate` (20 fps ⇒ 50 ms) — set at
  `RunSpaceFlight` entry; rate adjustable 8..32 by Alt+N/M (Win32 only).
* Because `DIBslamReal` is called after *every* game-drawn frame (menus, cinematics, flight),
  **the whole game is paced at 16 fps outside flight and 20 fps in flight.** The spin is
  `Sleep(0)`-yielding, not a hard busy loop, but it does not pump events.
* `RefreshMouseCursorDisplay` inside the spin re-presents the frame (via `DIBupdate`) each
  iteration ⇒ many presents per frame while waiting with the cursor on screen. A C# host
  should keep this cheap (or present once and loop on the deadline only).

### 5.4 Space-flight frame structure (`RunSpaceFlight`, hudmsg.c:794)

```
SetEventManagerPump(get_player_input); SdlSetMouseGrab(1); SetSpaceFlightFrameTiming()
while (nArcadeState == 0) {
    HandleSpaceFlightControls()      -> player_input() [PollInputEvent → PumpWindowMessages, PollKeyboardState,
                                        drain queue, SdlApplyJoystickFlightControls], players_flight_dynamics, hotkeys
    Update_3Space()                  -> simulation step (one per loop iteration)
    frameReady = RenderSpaceViewFrame()  -> Draw_3Space_Frame(): nFrameSkipCounter-- ; render only when it hits 0
                                        (nFrameSkip 1..5, Alt+? "FRAMES SKIPPED" message); HUD, cockpit
    update_cockpit()
    if (frameReady) { DIBslam(); DIBslamReal(); }   // present + throttle to 50 ms
}
SetCinematicFrameTiming(); SetEventManagerPump(0); QueueInputEvent(13,160,100,...)
```

Simulation is therefore **frame-locked**: one `Update_3Space` per presented frame at
nominally 20 Hz (the throttle happens only on frames that present; with `nFrameSkip > 1`
the sim runs faster than the present rate). The game logic uses no delta time; all
movement is per-frame integers. Preserving 20 Hz sim ticks is required for fidelity.

### 5.5 Vertical blank

`DIBwaitForVerticalBlank()` originally blocked until VBL start (`DDWAITVB_BLOCKBEGIN`) and
was used to hide palette changes and fade steps. In SDL it re-presents the last frame
(vsync paces it, or `SDL_Delay(1)` if no renderer). Fades call it per step, so fade speed
depends on the host refresh rate — e.g. `screens.c:1417/1431` and `screen.c:773` run
palette fades one step per vblank/present. A C# host should implement it as "present last
frame and wait one vsync (or ~16.7 ms if vsync is unavailable)".

### 5.6 How the DOS original paced (inference, not verified against DOS binaries)

The event manager retains its DOS shape: `InitializeEventManager(period = 20, init, cfg)`,
`nInputTickScale = 20`, and `SetFrameTimerPeriod(period)` in 1/60 s units. On DOS, Origin's
event manager hooked IRQ0 and reprogrammed the 8254 PIT so a tick ISR ran at a fixed
rate (60 Hz is consistent with every unit in this code: `nTickCount60Hz`, `*60/1000`
conversions, the 20-tick double-click window ≈ 1/3 s, and `SplitGameClockTicks` dividing by
60). The ISR also sampled the mouse driver / joystick and decremented a countdown that
`IsFrameTickElapsed` tested — the Win32 build replaces the countdown with a one-shot
`timeSetEvent` and the ISR with `timeSetEvent` + the Win32 message pump. `WaitForFrameTick`'s
bare spin is the DOS idiom (interrupts advance the flag). Frame pacing in DOS flight was
"as fast as possible" with `nFrameSkip` compensating; the Win32 port added the 20 fps cap
(`fSpaceFlightFrameRate`) to keep the integer-per-frame simulation at its design speed.

### 5.7 Recommended C# implementation

**Clock.** One `Stopwatch` started at host init. Expose:
`long Milliseconds` (→ `GetTickCount`), `uint Ticks60 => (uint)(Milliseconds * 60 / 1000)`
(recomputed in `Pump()` exactly as `PumpWindowMessages` does so consumers see identical
granularity), `GameClockTicks` with the randomised epoch. Do not use `Environment.TickCount`
(low resolution, wraps). Preserve 32-bit wrap semantics by masking where the C code used
`unsigned int` subtraction (`(int)(now - then)`).

**Frame timer.** Replace `timeSetEvent` with a *deadline* evaluated on the game thread:
`ArmFrameTimer(period60)` stores `deadlineMs = now + period60 * 1000 / 60` (period 0 ⇒
deadline = 0/elapsed); `IsFrameTickElapsed => now >= deadlineMs`. This is observably
identical (the flag was only ever polled) and removes the timer thread, the 16-timer table,
and a cross-thread `volatile`. `WaitForFrameTick` becomes "pump + sleep until deadline"
(see below) instead of a spin.

**Present throttle.** Keep `ThrottleFrameAndDrawFps` semantics exactly (deadline =
`now + interval` *after* the wait, so late frames do not accumulate credit) but implement
the wait as: pump host events (safe — the C code didn't, but nothing in the game assumes
events cannot arrive here, since `PollInputEvent` is called at unpredictable points anyway),
then `Thread.Sleep(1)`/`SDL_DelayNS` in small steps to the deadline, finally a short
spin for the last ~1 ms if precise 50/62 ms pacing is desired. Optionally allow the host
to run the throttle at a multiple (e.g. 60 fps presentation with 20 Hz sim) only if the
core is later refactored; initially keep 1 present = 1 frame.

**Blocking loops.** Two viable strategies:

1. **Dedicated game thread + cooperative `host.Pump()` (recommended for the first port).**
   Run the transliterated game on its own thread; the main (SDL) thread owns the window and
   the event loop. SDL3 requires `SDL_PollEvent`/`SDL_PumpEvents` on the thread that created
   the window, so the game thread's `Pump()` must *not* call SDL directly. Instead the main
   thread polls SDL every ~1 ms (or in `SDL_WaitEventTimeout`) and pushes translated events
   into a thread-safe queue; `Pump()` on the game thread drains that queue into the DOS-era
   `InputEvent` queue, runs `pEventManagerPump`, refreshes `Ticks60`, and returns
   `windowAlive`. Presents are marshalled the other way: `Present(frame, palette)` copies the
   64000+1024 bytes into a double buffer and signals the main thread, which uploads/renders;
   the game thread continues immediately (the throttle provides pacing). `WaitForVerticalBlank`
   waits for the main thread's "presented" event. Window close: the main thread sets
   `windowAlive = false` and the next `Pump()` on the game thread throws a
   `GameExitException` that unwinds the entire call stack to `GameMain` (the C code called
   `exit(0)` from inside the pump; an exception is the structured equivalent). Keyboard level
   state (`GetAsyncKeyState`) is served from a snapshot array updated by the main thread.
   This preserves the game's control flow unchanged and makes the window responsive even
   during loads.
2. **State-machine rewrite.** Convert every blocking loop (`Title_Sequence`, barracks, 20+
   cinematic/wait loops, `RunSpaceFlight`, nav map, modal prompts, calibration) into
   resumable states driven from a single `Update()` callback. This is the "proper" modern
   design but it touches thousands of lines of transliterated core and risks behaviour drift
   (the loops interleave drawing, timing, audio servicing, and input in idiosyncratic ways).
   Recommended as a *second phase* once the thread-based port is pixel-correct, done one
   screen at a time behind the same host interface.

Rationale: the host interface is small and poll-based, so a thread-plus-queue host costs
~1 extra copy per frame and no changes to the core; it also naturally absorbs the three
"no pump" blocking spots (`WaitForFrameTick`, the throttle spin, calibration button waits)
because the main thread keeps the window alive regardless.

---

## 6. Main loop call graph with blocking points

`[B]` = blocks the game thread in a loop that pumps via `PollInputEvent`/`CheckEscaped`/
`PumpWindowMessages`; `[B!]` = blocks **without** pumping; `[P]` = presents (throttled).

```
main()                                      src/sdl/launcher.c:215   (Win32: WinMain winmain.c:539)
 ├─ parse args / optional Slint launcher GUI [B, native loop]
 ├─ SDL_Init(VIDEO|EVENTS|TIMER|JOYSTICK|GAMECONTROLLER)
 ├─ SDL_CreateWindow("Wing Commander SDL2 port", 960x600, RESIZABLE)
 ├─ DIBinstall(window) → SdlInitializeVideo (renderer + 320x200 ARGB streaming texture)
 ├─ SdlStartEventPump() (bMainWindowAlive = 1)
 ├─ CheckLauncherAndConfig()            cheater flag + WINGCMDR.CFG flags
 ├─ SdlInitializeOriginFxAudio (DOS data / enhanced)
 ├─ MonoDebug_install() (no-op), InitializeAudioSystem(), InitializeAudioStreamer()  → ix threads start
 ├─ srand(time), InitGameClockEpoch(), CreateDebugOverlayConsole(60x20), SDL_ShowCursor(DISABLE)
 ├─ GameMain(argc-1, argv)                 src/main.c:11
 │   ├─ LoadWingCmdrCfgFile, chdir gamedat, LoadInstallDat, chdir ..
 │   ├─ parse game args (Origin dev unlock, -m/-s/-l/-w/-a…)
 │   ├─ SetCinematicFrameTiming(); LoadOriginFxDrivers(); LoadVolumeSettingsFromRegistry(); set volumes
 │   ├─ [dev -l] init_mission → RunSpaceFlight → exit_squadron
 │   ├─ SdlPlayDosStartupIntro()  [B][P] (DOS data: music-synced intro, CheckEscaped per step)
 │   └─ for(;;)
 │       ├─ Title_Sequence()               src/nav.c:1793
 │       │   ├─ (if !bEscapePressed) intro: pump=PollJoystickButtonEvents
 │       │   │     while(state==0){ PumpWindowMessages(); ... 3D intro frames:
 │       │   │        Update_3Space / Draw_3Space_Frame / print_subtitle / DIBslam/DIBslamReal [P]
 │       │   │        CheckEscaped [B] }  (credits, Kilrathi Saga credits)
 │       │   ├─ draw title menu; pump=PollMenuInputDevices; WarpMouseTo(160,100)
 │       │   └─ while(state==0){ UpdateTitleMenuCursor; PollInputEvent [B]; (0x24 → CalibrateJoystickInteractive [B!])
 │       │        DIBslam/DIBslamReal [P] }   → returns 0 new / 1 load / 2,3 (Saga extras)
 │       ├─ StartNewCampaign(…) (0/2/3)
 │       └─ do { GameFlow() } while (result != 0)      src/nav.c:1508
 │            ├─ bKeyEventQueueEnabled = 0
 │            ├─ do { RecRoom() [B][P] (room menus) | RunTrainSim() [B][P] → RunSpaceFlight ;
 │            │       BarracksScreen() [B][P] (save/load/quit → return 0, launch → break);
 │            │       PumpWindowMessages() } while (!launch)
 │            ├─ bKeyEventQueueEnabled = 1
 │            ├─ Briefing() [B][P], PlayScrambleHangarScene() [B][P], scramble() [B][P]
 │            ├─ init_mission(); LaunchPlayerShip()
 │            ├─ RunSpaceFlight(-1)       src/hudmsg.c:794   [B][P] 20 fps loop (see 5.4)
 │            │     inner modal paths: WaitForKeyAcknowledge (pause) [B], nav map [B][P], auto_pilot_sequence [P],
 │            │     comm menus, ShowOnScreenMessage(9999) pause
 │            └─ landing / ejection / stranded / death sequences [B][P]; return 1 to loop, 0 to go back to title
 ├─ SdlSetMouseGrab(0); SDL_ShowCursor(ENABLE); DestroyGlobalDebugOverlayConsole()
 ├─ ix_streamer_destroy(); ServiceAudioStream(); SdlShutdownOriginFxAudio()
 └─ DIBunInstall(); SdlShutdownJoysticks(); SDL_DestroyWindow(); SDL_Quit()
```

Exits from arbitrary depth: `ShutdownGameWindow()` (window close/QUIT/Cmd+Q, from inside
`SdlPumpEvents`) → `exit(0)`; `exit_squadron(msg)`; `DIBerror` → `exit(1)`;
`SetInputKeyState` out-of-range → `exit(1)`; text-wrap fatal in `mathfp.c` → `exit(0)`.
`GameMain` never returns in normal play (the `for(;;)`); quitting is via the barracks
"Quit" which calls `exit_squadron` → `ShutdownGameWindow`/`exit`.

Generic blocking primitives (all `[B]` unless noted):

| Primitive | Loop | Returns on |
| --- | --- | --- |
| `PollInputEvent(&e, 0xff)` | pump once + pop | immediately (0 if nothing) |
| `CheckEscaped()` | pump once, peek 10/2/3 | immediately; flushes queue if hit |
| `WaitForInputKey()` | `PollInputEvent` until key/button | 0x1C on 2/10; scan on 3/5 (ignores 0x1D); if `nEventManagerActive == 0` → `PumpMessagesDuringWait()` |
| `WaitForSceneAdvance(duration)` | frame timer + `PollInputEvent` | timer elapsed or 2/3/5/10 |
| `WaitForKeyAcknowledge(mode)` | `PumpWindowMessages` until type 4 then type 3 (mode≠0) / `PumpMessagesDuringWait` until a key other than P, Down, '-' (mode 0) | key; suspends mouse grab |
| `WaitForKeyExceptXOrF12()` | `PumpMessagesDuringWait` | any VK except 'X'/F12 |
| `WaitForStreamInputKey()` | `WaitForInputKey` with VK duplicates | key |
| `PumpMessagesDuringWait()` → `DebugOverlayConsole::WaitForKey()` | `while (dwDebugOverlayKey == 0 && PumpWindowMessages()) SDL_Delay(1)` | VK of a **key release**; 0x1B if window died |
| `WaitForFrameTick()` | `while (bFrameTickPending)` `[B!]` | timer |
| `WaitForJoystickButtonPress/Release()` | `SampleJoystickDevice` `[B!]` | button edge |
| `ThrottleFrameAndDrawFps` | `Sleep(0)` + cursor refresh `[B!]` | deadline |

`PumpMessagesDuringWait` call sites: `pilot.cpp:18` (`WaitForKeyExceptXOrF12`),
`hudmsg.c:56` (pause), `disk.c:428` (when the event manager is inactive), `music.c:709`,
`pload.c:107`, `sound.c:781/791/824` (fatal data errors: print, wait for a key, exit),
`mathfp.c:269/287` and `eventmgr.c:642` (fatal). It is the "press any key" primitive, and
notably it keys on **release**, so a held key does not satisfy it.

---

## 7. Mouse details

* Logical space 0..319 × 0..199, origin top-left. Host maps window→logical with the
  letterbox rectangle and half-up rounding; clamps. Game → host warps map the other way.
* `stMouseCursorState.viewport` defines (a) the clamp rect applied in `GetNextInputEvent`
  and (b) whether/where the software cursor is drawn. Values: `&stScreen` (default, whole
  screen), `&stSpaceBuffer` (flight), `pointerViewport` sub-rect 32,24–182,159 (nav map),
  conversation/scene buffers (cursor not drawn because `pixels != pDIBPixelBuffer`).
* Flight mouse steering (`player_input` type 13): offset from the space-buffer centre is
  bucketed with `asMouseYawThresholds {10,37,52,57,62,1070}` and
  `asMousePitchThresholds {5,18,27,35,38,1040}` into 0..5, forced to ±8 within 4 px of a
  viewport edge, clamped ±8. Right button held ⇒ afterburner mode: pitch axis → throttle
  (`accelerate(pitch/2)`), yaw → roll. Releasing afterburner warps the pointer back to the
  centre. Left button ⇒ 0x39 (guns), both ⇒ 0x1C (missile), double right click within
  `nInputTickScale` (20) ticks ⇒ 0x0F (afterburner toggle).
* Keyboard pointer: arrows/Home/End/PgUp/PgDn move `stMouseCursorState` by `2*nKeyboardPointerStep`
  (step 4 default, KP5 toggles 1↔4), queue a type 13, set `bPointerMovedByKeyboard`, warp OS pointer.
* Grab rules (SDL port): confined only while `RunSpaceFlight` is active AND the window has focus
  AND no modal wait has suspended it. Win32: always clipped to the 320x200 popup window.
* Mouse buttons are sampled at queue time into both the event and the `bHost*` globals;
  `player_input` reads the globals when no button event is queued to get "held" state.

## 8. Joystick details

* Two logical devices (0,1), each with X,Y axes and 2 buttons (`JOYINFO.wButtons` bits 0–1;
  device 1's bits are stored shifted `<< 2` so `GetJoystickButtons` packs both into 4 bits).
* Detection: `LoadJoystickCalibrationFile(9,9,1,1)` (called on joystick hot-plug in SDL,
  and from the game at init/calibration): `SampleBothJoysticks(fallback 0xffff)`; the first
  device whose X,Y are not −1 becomes `nActiveInputDevice` (−1 = none). Reads `j.cal`;
  if absent uses `GetJoystickDevCaps` to centre = (min+max)/2 and a ±10 window; derives
  per-direction scales `(centre − min)/range` etc. (range 9 ⇒ output −9..+9), dead zones 1.
* `ReadCalibratedJoystick()` (screen.c:2007): sample; if value == `nJoystickFailureValue`
  (−1 by default, `maxX*2` after interactive calibration) ⇒ device lost, `nActiveInputDevice = −1`,
  sample zeroed. Else clamp to min/max, normalise: `x = (raw − centre)/scale` with dead zone,
  negative = left/up.
* `UpdateInputDeviceTransitions` provides button edges and double-click flags (§3.8).
* In flight (`player_input` case 6): button 1 ⇒ guns, both ⇒ missile (or guns while
  afterburning), button 2 held ⇒ roll = x, throttle = −y/2; else yaw = x, pitch = −y;
  button-2 double-click ⇒ 0x0F.
* SDL layer: `SDL_GameController` preferred (axes LEFTX/LEFTY, buttons A/B), raw joystick
  fallback (axes 0/1, buttons 0/1); hot-plug via device events; `SDL_JoystickUpdate` per read.
  Gamepad extras (optional for the port): Back ⇒ Esc, Start ⇒ P (pause) in flight, Y ⇒
  'Y'/0x15 at prompts, D-pad hat as buttons, WCAT modes mapping X/Y/shoulders/triggers/D-pad
  to afterburner/target/weapons/views and extra axes to roll/throttle/rudder, comm-menu D-pad
  navigation, rumble on weapons/damage/collision/afterburner.

## 9. Window / present side of `video.c`

* Window 960x600 resizable, title "Wing Commander SDL2 port", centred; hidden for `--check`.
* Renderer: accelerated + vsync, fallback software; `SDL_HINT_RENDER_SCALE_QUALITY = nearest`.
* Texture: `ARGB8888`, streaming, 320x200. Present: convert indices through the B,G,R,x
  palette to `0xFF000000 | R<<16 | G<<8 | B`, `SDL_UpdateTexture`, clear, `RenderCopy` into
  the 4:3 rect, `RenderPresent`.
* Optional `--ega` dither post-filter and the GL "enhanced" backend are out of scope for a
  faithful port.
* Fullscreen toggle uses desktop fullscreen; the letterbox math handles any size.

## 10. Threads and the audio callback

Game thread (SDL main thread in the C port) + `ix` mixer thread (sleeps on an event;
exists only to own the device lifetime) + `ix` streamer thread (file reads for streamed
music, wakes on event with timeouts) + SDL audio callback thread (runs `ix_dspv_mix`
under `csMixer`; `dwDspTick` incremented per callback, 1470 frames @ 22050 Hz) + SDL timer
thread (`FrameTimerCallback` writes `bFrameTickPending`). Win32 additionally had the debug
overlay spinner thread. Shared state touched cross-thread: `bFrameTickPending` (volatile int),
`dwDspTick`, everything under the `cs*` sections, and the SDL event queue (SDL-internal).

For C#: keep the mixer entirely inside the audio callback (`SDL_AudioStream` callback in SDL3)
with a `lock`; the "mixer thread" and its wake event can be dropped — its only job was to
call `SdlStartAudio`/`SdlStopAudio`. The streamer thread can remain a `Thread` or become
async file reads feeding a ring buffer; the game only interacts through `ix_streamer_*`
calls and `ServiceSoundSystem`.

## 11. Debug consoles

* `mono.c`: `MonoDebug_install/print/remove` talk to `\\.\MONODEBG.VXD` via `DeviceIoControl`
  (version 0x20004, ioctl 1 version, 2 init, 9 print ≤ 0xFA0 bytes). Never available on modern
  hosts; port as a no-op logger.
* `debug.cpp`: `DebugOverlayConsole` — a 60x20 character grid drawn with GDI `TextOutA` over
  the game window (Win32) / nothing (SDL). The port must keep `WaitForKey()` semantics because
  `PumpMessagesDuringWait()` is a game-visible wait primitive: wait until a key is *released*
  (`dwDebugOverlayKey`), return its VK code, or 0x1B if the window closed. Everything else
  (`DebugOverlayPrintf`, scroll, colours) can be a text log.

---

## 12. Proposed C# host abstraction

Design principle: expose exactly the semantics in §2 so the transliterated core compiles
against an interface, with SDL3 behind it. Signatures shown as C# for clarity.

```csharp
public interface IHostVideo
{
    // 320x200 indexed frame; palette = 256 * (B,G,R,flags). Returns false on failure.
    bool Present(ReadOnlySpan<byte> indices, ReadOnlySpan<byte> paletteBgrx);
    // Re-present the last frame and wait one vsync (or ~16.7 ms). DIBwaitForVerticalBlank.
    void WaitForVerticalBlank();
    void Recreate();                      // DIBreInstall
    void SetFullscreen(bool on); bool IsFullscreen { get; }
    bool WindowAlive { get; }             // bMainWindowAlive
}

public interface IHostClock
{
    uint Milliseconds { get; }            // GetTickCount / timeGetTime (32-bit wrap preserved)
    long PerfCounter { get; }             // QueryPerformanceCounter
    void Sleep(int ms);                   // Sleep (only Sleep(0) used)
}

public interface IHostInput
{
    // Called by the game's PumpWindowMessages(): drain host events into the DOS queue.
    // Returns false once the window has been closed (game must unwind/exit).
    bool Pump(IInputSink sink);
    bool IsVirtualKeyDown(int vk);        // GetAsyncKeyState != 0 for the 15 VKs in §2.3
    void WarpPointer(int logicalX, int logicalY);   // SetCursorPos in 320x200 space
    void SetPointerGrab(bool requested);  // SdlSetMouseGrab
    void SuspendPointerGrab(); void ResumePointerGrab();
    bool ReadJoystick(int device, out JoyInfo info);          // SdlReadJoystick
    bool ReadJoystickRange(int device, out JoyRange range);   // 0..0xFFFF
}

// What Pump() delivers; the game-side event manager (eventmgr port) implements this.
public interface IInputSink
{
    void KeyEvent(bool down, int dosScan, int vk, bool repeat);   // queue 3/4 (+VK dup), SetInputKeyState, latches
    void MouseMove(int x, int y, bool lmb, bool rmb);              // coalesced type 13
    void MouseButton(bool down, int x, int y, bool lmb, bool rmb); // type 2 / 1
    void MouseWheel(int delta);                                    // 0x0D / 0x0C press+release
    void JoystickButton(int slot, int button, bool down);          // optional gamepad extras
    void WindowClosed();                                           // → ShutdownGameWindow path
    void FocusChanged(bool focused);
}

public interface IHostAudio
{
    // 22050 Hz, S16, stereo; callback invoked on the audio thread with a frame count.
    bool Start(Action<Span<short>> mix, object mixLock, Action onTick);
    void Stop();
}

public interface IHostFiles
{
    string Resolve(string dosPath);       // case-insensitive, '\\'→'/', relative to data root
    Stream Open(string dosPath, FileAccess access, FileMode mode);
    bool Exists(string dosPath); void Delete(string dosPath);
    string DataRoot { get; set; }         // replaces chdir
    string PrefPath { get; }              // for wc1-modern.cfg
}

public interface IHostConfig
{
    int MusicVolume { get; set; }         // 0..20, default 20
    int SfxVolume { get; set; }           // 0..20, default 20
    bool Cheater { get; }                 // read-only to the game
    void Save();
}

public interface IHostDiagnostics
{
    void MessageBox(string title, string text, bool error);
    void DebugWrite(string text);
    void Fatal(int code);                 // exit()
}
```

Game-side (ported from C, *not* host): `EventManager` (pool, queue, cursor, modifiers),
`FrameTimer` (deadline-based `Arm(period60)/Elapsed`), `FrameThrottle` (interval/deadline),
`KeyboardState` (`abInputKeyState`, `PollKeyboardState`, latches), `JoystickCalibration`
(`j.cal`, normalisation, edges), `TickClock` (`Ticks60`, game clock).

Threading recommendation (from §5.7): SDL3 main thread owns window + `SDL_PollEvent` +
texture upload; game thread runs `GameMain`; `Pump()` drains a `ConcurrentQueue<HostEvent>`;
`Present` hands a frame to the main thread via a double buffer and `ManualResetEventSlim`;
`WindowAlive == false` makes `Pump()` throw `GameExitException` caught in `GameMain`'s caller.

## 13. SDL3 features required

| Need | SDL3 API |
| --- | --- |
| Init | `SDL_Init(SDL_INIT_VIDEO | SDL_INIT_EVENTS | SDL_INIT_AUDIO | SDL_INIT_JOYSTICK | SDL_INIT_GAMEPAD)` (no TIMER subsystem needed with a deadline-based timer) |
| Window | `SDL_CreateWindow(title, 960, 600, SDL_WINDOW_RESIZABLE)`, `SDL_SetWindowFullscreen(window, bool)` (desktop/borderless), `SDL_SetWindowMouseGrab`, `SDL_HideCursor`/`SDL_ShowCursor`, `SDL_WarpMouseInWindow`, `SDL_GetWindowSize`/`SDL_GetWindowSizeInPixels` (HiDPI: use pixel size for the letterbox and `SDL_ConvertEventToRenderCoordinates` or manual mapping) |
| Renderer | `SDL_CreateRenderer`, `SDL_SetRenderVSync(renderer, 1)`, `SDL_CreateTexture(SDL_PIXELFORMAT_ARGB8888, SDL_TEXTUREACCESS_STREAMING, 320, 200)`, `SDL_SetTextureScaleMode(SDL_SCALEMODE_NEAREST)`, `SDL_UpdateTexture` or `SDL_LockTexture`, `SDL_RenderClear`, `SDL_RenderTexture(renderer, tex, null, &dstFRect)`, `SDL_RenderPresent`. Optionally `SDL_SetRenderLogicalPresentation(320, 200, SDL_LOGICAL_PRESENTATION_LETTERBOX)` to get the 4:3 box and coordinate mapping for free — but the game's letterbox is 4:3 *not* 320:200 (1.6), so compute the rect manually as in `SdlCalculateOutputViewport` or use logical size 320x240 with the texture stretched. |
| Events | `SDL_PollEvent` on the main thread: `SDL_EVENT_QUIT`, `SDL_EVENT_WINDOW_CLOSE_REQUESTED`, `_FOCUS_GAINED/_LOST`, `_PIXEL_SIZE_CHANGED`, `SDL_EVENT_KEY_DOWN/UP` (`event.key.scancode`, `.key`, `.mod`, `.repeat`), `SDL_EVENT_MOUSE_MOTION` (float x,y, `state`), `SDL_EVENT_MOUSE_BUTTON_DOWN/UP`, `SDL_EVENT_MOUSE_WHEEL` (float y, `direction`), `SDL_EVENT_JOYSTICK_ADDED/REMOVED`, `SDL_EVENT_GAMEPAD_BUTTON_DOWN/UP`, `SDL_EVENT_JOYSTICK_BUTTON_DOWN/UP`, `SDL_EVENT_JOYSTICK_HAT_MOTION` |
| Keyboard state | `SDL_GetKeyboardState` (snapshot copied for the game thread), `SDL_GetModState` |
| Mouse state | `SDL_GetMouseState` (buttons mask) |
| Gamepad/joystick | `SDL_GetJoysticks`, `SDL_IsGamepad`, `SDL_OpenGamepad`/`SDL_OpenJoystick`, `SDL_GetGamepadAxis(LEFTX/LEFTY)`, `SDL_GetGamepadButton(SOUTH/EAST/...)`, `SDL_GetJoystickAxis/Button/Hat`, `SDL_UpdateJoysticks`, `SDL_RumbleJoystick`/`SDL_RumbleGamepad` (optional) |
| Audio | `SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec{22050, S16, 2}, callback, userdata)` + `SDL_ResumeAudioStreamDevice`; callback receives `additional_amount` and must `SDL_PutAudioStreamData` — allocate a 1470-frame scratch buffer and call the ported mixer; or push from a timer if a pull-callback is undesirable |
| Timing | `SDL_GetTicks` (ms, 64-bit in SDL3 — mask to 32 bits for the game clock), `SDL_GetTicksNS`, `SDL_DelayNS`/`SDL_DelayPrecise`, `SDL_GetPerformanceCounter`; `.NET Stopwatch` is sufficient instead |
| Message box | `SDL_ShowSimpleMessageBox` |
| Paths | `SDL_GetPrefPath("Origin Systems", "WC Kilrathi Saga")`, `SDL_GetBasePath` |
| Threads | not needed from SDL; use `System.Threading` |
| Bindings | SDL3-CS (ppy or flibitijibibo `SDL3#`) — both expose the above; the event union needs `unsafe`/explicit-layout structs |

## 14. Open questions

1. **Text entry with VK duplicates.** `WaitForStreamInputKey` receives both a VK/ASCII event
   and a scan-code event per key. The pilot-name entry code (not analysed here) must be
   checked for how it distinguishes them (likely by range: scan codes < 0x60 vs ASCII ≥ 0x20
   overlap for digits/letters — ASCII `'A'` = 0x41 collides with scan 0x41 = F7). Confirm
   the exact filter before porting, or deliver only the VK event while
   `bKeyEventQueueEnabled` is set for that screen.
2. **Key-up value loss.** `GetNextInputEvent` discards `value` for type 4. Confirm no game
   path relies on key-up identity (only `IsInputEventQueued(4)` is used in
   `WaitForKeyAcknowledge`); if so, the port can keep the same lossy behaviour.
3. **`nTickCount60Hz` granularity.** It advances only in `PumpWindowMessages`. Some loops
   (`barracks.c`, `killbrd.c`) compare it to deltas; if the C# `Pump()` is called more or
   less often than the C code (e.g. inside the throttle), page timings could shift. Keep
   the update exactly in `Pump()` and do not call `Pump()` from the throttle unless
   verified.
4. **Fade/vblank speed.** Palette fades step once per `WaitForVerticalBlank`; on a 144 Hz
   display with vsync they run 2.4× faster than at 60 Hz. Decide whether to clamp the
   vblank wait to ≥ 1/60 s (recommended for fidelity to the Win95-era target).
5. **Spin loops without pump.** `WaitForFrameTick` (2 call sites), calibration button waits,
   and the present throttle do not process window events in C. With the two-thread design
   this is moot for responsiveness, but confirm nothing depends on events *not* being
   consumed during those waits (e.g. `AnimateTrainSimTitle` expects `CheckEscaped` to see
   the Esc *after* the spin — fine either way).
6. **Alt+N/M frame-rate hotkeys and `-f` FPS overlay** are Win32-only; decide whether to
   reinstate (easy: adjust `fSpaceFlightFrameRate`, draw text in the host).
7. **Mouse-motion coalescing** differs between Win32 (one event per message) and SDL (tail
   overwrite). Game behaviour is insensitive (only the last position matters) but the
   256-slot overflow policy (drop everything) is easier to hit with Win32 semantics; keep
   SDL's coalescing.
8. **Minimise/focus-loss handling.** Win32 froze the game in `GetMessage` while iconic and
   re-initialised DirectDraw on restore; SDL only re-applies the grab. Decide whether to
   pause audio/sim on focus loss (modern expectation) — the game has no explicit pause API
   other than `WaitForKeyAcknowledge`.
9. **`PumpMessagesDuringWait` returns on key *release*.** On SDL this is `SdlHandleKeyboardEvent`'s
   `!pressed` branch writing `dwDebugOverlayKey = virtualKey`. Any key, including modifiers
   (Shift release = 0x10). Confirm this is acceptable for "press any key" prompts or filter
   modifiers.
10. **Audio buffer size.** 1470 frames (66.7 ms) at 22050 Hz was chosen to match the original
    DirectSound write cadence (`dwDspTick` ≈ 15 Hz); the `ix` streamer's scheduling may depend
    on that tick rate. Verify before changing the buffer size in SDL3's stream API.
11. **DOS PIT model** in §5.6 is inferred from the surviving units and Origin conventions, not
    from the DOS executable; if DOS-data fidelity matters (intro pacing), verify against
    `data/dos/WC.EXE` disassembly notes in the reference repo (`src/sdl/dos_intro.c` already
    encodes the music-synced timing).
12. **Registry write-back on first run.** `LoadVolumeSettingsFromRegistry` writes defaults if
    a value is missing but only inside an *existing* key; the SDL shim always "opens"
    successfully. The C# config should create the file with defaults on first run (matching
    SDL behaviour).
