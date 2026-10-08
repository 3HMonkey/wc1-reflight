# Progress: platform (Core.Platform, Core.Runtime, Core.Rendering, Host.Sdl)

## Status

2026-10-07: frame-driven model (ADR-009) in place. `SdlHost` owns the main loop
(`Run(IGameApp, IRenderer)`), implements `IHostServices`, creates the window for Vulkan or for
the SDL_Renderer fallback, and exposes the SDL3 Vulkan surface functions for the exe to adapt
to `Render.Vulkan.IVulkanSurfaceSource`. `SdlRendererBackend` is the fallback presenter.
`Core.Runtime.GameScheduler` (virtual clock + coroutines) is tested (deep nesting stays on one
thread, ordering, exceptions). `wc1 --frames 120` runs the host check screen through the full
stack (host loop, runtime, scheduler, event manager, display, SDL_Renderer/Direct3D 11).

## Mapping

| C (file) | C# | Status |
| --- | --- | --- |
| `SdlPresentIndexedFrame`, `SdlCalculateOutputViewport` (sdl/video.c, video_state.c) | `SdlRendererBackend.Render`, `Core.Rendering.PresentationLayout` | done |
| `SdlWaitForVerticalBlank` | `Game.Timing.FrameTiming.WaitForVerticalBlankAsync` (virtual 70 Hz retrace) | done |
| `SdlPumpEvents` OS side, `SdlTranslateScanCode`, `SdlTranslateVirtualKey` | `SdlHost.Translate`, `ScanCodes.FromSdl`, `VirtualKeys.FromSdl` | done |
| Alt+Enter / Cmd+Enter fullscreen, Cmd+Q quit | `SdlHost.HandleHostShortcut` | done |
| `SdlSetCursorPosition`, `SdlMapLogicalToWindow`, mouse mapping | `SdlHost.WarpMouse` / `MapMouse` via `PresentationLayout` (HiDPI aware) | done |
| `SdlSetMouseGrab` (focus-aware) | `SdlHost.SetMouseGrab`; suspend depth in `Game.Input.EventManager` | done |
| `SdlReadJoystick` | `SdlHost.TryReadJoystick` (first two SDL joysticks, axes 0/1, buttons 0/1) | basic |
| `SdlStartAudio`/`SdlStopAudio` | `SdlHost.StartAudio/StopAudio` (SDL3 audio stream, pull callback) | done, verified with the DOS mixer (intro music, 60 fps) |
| `GetTickCount`/`timeGetTime`, `Sleep`, timers | `Core.Runtime.GameScheduler` virtual clock | done |
| `MessageBoxA` | `IHostServices.ShowMessage` | done |
| `SDL_GetPrefPath` config dir | `IHostServices.UserDataDirectory` | done |
| Vulkan presentation | `Render.Vulkan.VulkanRenderer` via `wc1` `Presentation` + `SdlVulkanSurfaceSource` (fallback to SDL_Renderer) | done (R1), validated on screen |
| window control for smoke tests | `SdlHost.LoopHook`, `SetWindowSize`, `SetFullscreen`, `MinimizeWindow`, `RestoreWindow`, `IsMinimized`; `wc1 --window-test` | done |
| Gamepad extras (WCAT modes, rumble, hat/D-pad, controller mappings) | — | todo (optional) |

## Deviations

- The host never translates the mouse wheel into keys; the game's event manager does.
- Vertical blank is a virtual 70 Hz retrace on the game clock, independent of the monitor.
- Quit is an event the runtime turns into "finished" instead of `exit(0)` inside the pump.
- Real-time steps are capped at 100 ms, so stalls pause the game instead of fast-forwarding it.

## Open questions

- Joystick: calibration (`j.cal`), normalisation and the WCAT gamepad layouts are not ported.

## 2026-10-07 night

- Window creation failures (SDL loads Vulkan for SDL_WINDOW_VULKAN windows) now shut SDL down
  and throw `HostException`, which the exe catches to fall back to an SDL_Renderer window.
- Mouse/keyboard path verified through the title menu in tests (headless) and by the user in
  the window; the DOS mixer runs on the SDL audio stream (60 fps with audio).
