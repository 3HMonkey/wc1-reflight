# Architecture

Last updated 2026-10-07 (frame-driven runtime, Vulkan renderer wired in, game composition; ADR-009 to ADR-011).
Decisions and their rationale are in `DECISIONS.md`.

## Solution layout

```
WingCommander.slnx
Directory.Build.props        net10.0, nullable, AOT/trim analyzers for every non-test project
global.json                  SDK 10.0.400 (rollForward latestFeature)
src/
  WingCommander.Core/        shared foundation, no platform code, no unsafe
    Resources/               PacketFile (Origin packet container), OriginLzw, InstallTable/LogicalFile, GameDirectory
    Runtime/                 GameScheduler: deterministic virtual clock + coroutine scheduler
    Platform/                IGameApp (what the host drives), IHostServices (what the game may call),
                             HostInputEvent, IAudioSource, JoystickState, GameExitException, HeadlessServices
    Rendering/               IRenderer, RendererSettings, RenderFrame/ClassicLayer, PresentationLayout (letterbox math)
    Video/                   Framebuffer (320x200 indexed), Palette (256 x 8-bit RGB)
    Numerics/                FixedMath (24.8, FPU-exact like the KS build), FixedVector, CRandom (MSVC rand LCG)
  WingCommander.Graphics/    raster library: viewports, RLE shapes (draw/scale/rotate), lines, fonts/text,
                             palette loading and fades, cockpit view geometry
  WingCommander.Audio/       OPL2 emulation (ymfm port), OriginFX timbres/sequencer/synth/SFX, mixer,
                             music director and sound-effect manager (game-facing audio API)
  WingCommander.Simulation/  space objects, ship systems, weapons, collisions, AI, mission loading
  WingCommander.Game/        Wc1Game (composition root, GameMain), runtime (GameRuntime), input (EventManager),
                             timing, video (Display: working/front buffer + GraphicsContext, SoftwareCursor),
                             resources (packet/shape cache), audio wrapper (GameAudio), campaign, scenes, flow,
                             screens (DosIntro, TitleSequence, GameFlowScreens partials), IFlightLayer seam.
  WingCommander.Game.Flight/ space flight layer on the Simulation library: flight loop, cockpit, HUD,
                             controls, flight sequences, attract scenes (implements Game.Flow.IFlightLayer)
  WingCommander.Render.Vulkan/  Vulkan renderer (Vortice.Vulkan), cross-platform incl. MoltenVK
  WingCommander.Host.Sdl/    SDL3 window, main loop, input translation, audio device, SDL_Renderer fallback presenter
  WingCommander/             executable `wc1`: argument parsing and composition, PublishAot
  WingCommander.Tools/       executable `wc1tool`: data inspection/export
tests/
  Directory.Build.props      xunit packages + tests/Shared/*.cs linked into every test project
  Shared/GameData.cs         locates the GOG data (WC1_GAME_DIR), [DataFact]/[DataTheory] skip without data
  WingCommander.<Layer>.Tests/   one test project per library
docs/                        project documentation (start at docs/README.md)
reference/wc1-re/            C reference, read-only, gitignored
```

Dependency rules (enforced by project references):

```
Core <- Graphics, Audio <- Game <- Game.Flight -> Simulation -> Core
Game, Game.Flight <- wc1 (exe) -> Host.Sdl -> Core
                             \--> Render.Vulkan -> Core
Tools -> Core, Graphics, Audio, Simulation, Game      Tests -> their own library
```

Graphics, Audio and Simulation never reference each other. Host.Sdl and Render.Vulkan never
reference each other: the exe adapts the host's Vulkan surface functions to the renderer's
`IVulkanSurfaceSource` (`src/WingCommander/SdlVulkanSurfaceSource.cs`). Game references Core,
Graphics and Audio, never Simulation: the flight layer lives in `Game.Flight` and plugs into
`Wc1Game.FlightLayer` (`IFlightLayer`), so Game builds and tests without the simulation and a
half-edited Simulation never breaks the screens.

## Game composition

`Wc1Game` (Game project) is the composition root the exe creates over a `GameRuntime`:
resources, display + graphics, event manager + software cursor, audio, settings and startup
switches, campaign session and flow, screens. `Wc1Game.RunAsync` is GameMain:

```
Initialize()   settings (wc1.cfg), WINGCMDR.CFG + switches, cinematic timing, audio (DOS mixer -> host),
               event manager + ARROW.VGA cursor, frame timer, GAME.PAL, fonts, default text context
DosIntro       Origin FX intro (music-synchronised when audio is on)
loop:          TitleSequence -> StartNewCampaign (new game) -> CampaignFlow.RunAsync until it returns false
```

Screens are async coroutines over the game services. `GameFlowScreens` implements the flow's
`IGameFlowScreens` and is split by area into partial files (Rooms, Scenes, Flight) so the areas
are ported independently; unported members show a "not ported yet" screen and lead back to
the title. Save games and settings live in the host's user data directory; an existing
`GAMEDAT\SAVEGAME.WLD` is imported on first use (ADR-011).

## Runtime model (ADR-009)

The host owns the main loop; the game never blocks and never presents by itself.

```
SdlHost.Run(app, renderer):                       GameRuntime (IGameApp)
  loop:                                             Scheduler   virtual clock, coroutine queue
    SDL_PollEvent -> app.HandleEvent(e)  -------->  Events.EnqueueHostEvent(e, now)   (Quit ends the game)
    app.Update(real elapsed, max 100 ms) -------->  Scheduler.Advance(ms): resumes every coroutine
                                                      continuation that falls due, in due-time order
    renderer.Render(app.Frame)  <---------------    Display.Front (ClassicLayer: front buffer + palette)
    (vsync, or a frame cap)

Game code (ported screens) stays sequential, written as async coroutines:
    while (...) {
        events.PollInputEvent(ref e)            // pumps: due host events enter the original queue
        draw into Display.Working
        await display.PresentAsync()            // DIBslam + DIBslamReal: copy to front buffer,
    }                                           //   then wait for the 16/20 fps frame deadline
    await events.WaitForInputKeyAsync()         // polling waits yield 1 ms of virtual time per poll
    await timing.WaitForVerticalBlankAsync()    // palette fades step on a 70 Hz virtual retrace
```

- **Determinism:** virtual time only moves in `Scheduler.Advance`. On the SDL host it follows
  real time (capped per step so stalls pause the game instead of fast-forwarding it); in tests
  it advances by scripted steps and host events are scheduled at virtual timestamps. A whole
  campaign can be run headless in milliseconds.
- **Threading:** one game/render thread; only the audio device calls `IAudioSource.Render` on
  its own thread. The scheduler's `SynchronizationContext` keeps every continuation on the game
  thread; game code must only await scheduler awaitables and its own coroutines.
- **Presentation semantics:** pixels become visible on present (front-buffer copy, with the
  software cursor composited); the front buffer shares the live palette, so palette writes
  (fades) affect the displayed pixels immediately, as on VGA/DirectDraw. `Display.PaletteChanged`
  additionally shows the current working pixels (the SDL port's DIBramPalette). The slam flag is
  `GraphicsContext.ScreenDirty`: every raster call on the screen sets it, a present clears it.
- **Exit:** window close / Cmd+Q ends the runtime; `GameExitException` inside a coroutine ends the
  game normally; any other exception is reported as a failure by the exe.

## Rendering (ADR-010)

- `RenderFrame` carries the classic layer (320x200 indexed front buffer + live palette with
  version counters, so renderers upload only on change) and an interpolation factor; the
  roadmap adds an optional space view (R2: sprites at output resolution, R3: glTF meshes with
  PBR, R4: ray-traced effects on capable GPUs) composited under the cockpit/HUD pixels.
- `PresentationLayout` computes the 4:3 (or square-pixel) letterbox for every renderer and
  for the host's mouse mapping, so the pointer always lands on the displayed pixel.
- Renderers: **Vulkan** (`Render.Vulkan`, the default; SDL3 creates the surface; MoltenVK on
  macOS) and the **SDL_Renderer fallback** (`SdlRendererBackend`, CPU palette conversion).
  `src/WingCommander/Presentation.cs` creates the window for Vulkan and falls back to a plain
  window with SDL_Renderer when SDL cannot create a Vulkan window or the renderer throws
  `VulkanRendererException` (`--renderer auto|vulkan|sdl`). Validation layers: `--vulkan-validation`
  or `WC1_VULKAN_VALIDATION=1`; Debug builds enable them automatically when installed.
- R2 is live: during flight the Game.Flight layer records the space objects into
  `RenderFrame.Space` (SpaceView: sprite list, sprite image cache, cockpit window mask, background
  index) and publishes it with each present; the Vulkan sprite pass draws them at output
  resolution where the classic frame shows the space background inside the window mask. Without
  an R2 renderer (SDL_Renderer, headless) or with `--classic-space` the CPU draws them.
  R2b (ADR-019): each sprite also carries its state of the tick before, and the renderer draws it
  between the two ticks (`RenderFrame.Interpolation`, set by `GameRuntime` after every host
  update), so flight motion is smooth at any display rate.
- Text at output resolution (ADR-013): `Graphics.Text.TextLayerTracker` follows every glyph the
  game draws (and the raster copies that move it) and publishes `RenderFrame.Text` with each
  present; the Vulkan text pass draws the glyphs from distance fields (vectorized originals or
  bundled replacement fonts) on top of the classic and sprite layers, and the flight's key help
  (`RenderFrame.KeyHelp`) last. `Core.Rendering.ReferenceCompositor` renders the same on the CPU.
- `wc1 --window-test [--capture-dir dir]` resizes, toggles fullscreen, minimises/restores and
  switches filter/vsync/aspect while the game runs, captures swapchain frames and checks them
  against the front buffer (smoke test of the on-screen path).

## State ownership

The C code keeps all state in ~2000 globals. The port groups them by subsystem into
instance classes; nothing is static except immutable tables.

| C globals (examples) | C# owner |
| --- | --- |
| `pDiskFileRecords`, packet cache | `Core.Resources.GameDirectory` |
| `pDIBPixelBuffer`, `abDIBPaletteCache` | `Game.Video.Display` (working buffer, palette, front buffer) |
| `stScreen`, raster/text/palette globals, `bDIBSlamPending` | `Graphics.GraphicsContext`, owned by `Display` (`ScreenDirty` = slam flag) |
| `stMouseCursorState.shape/frame/viewport`, cursor save area | `Game.Video.SoftwareCursor` (+ `Graphics.Cursor.MouseCursorCompositor`) |
| `stDefaultTextContext`, packet handles | `Wc1Game.DefaultText`, `Game.Resources.GameResources` |
| event queue, cursor, key state, joystick samples | `Game.Input.EventManager` |
| `nTickCount60Hz`, frame timer, throttle | `Game.Timing.FrameTiming` on `Core.Runtime.GameScheduler` |
| `rand()` seed | one `Core.Numerics.CRandom` shared by all subsystems (consumption order matters) |
| per-object arrays `ae*/as*/a*[64]` | `Simulation` world (array of structs, slot index = identity) |
| `stCampaignState`, pilots, saves | `Game.Campaign.CampaignSession` |
| music mode, volumes, active sounds | `Audio` |

Cross-subsystem calls that the C code makes directly (e.g. AI code showing a HUD message
or starting a sound) become small interfaces owned by the caller's project, implemented in Game.

## NativeAOT constraints

- No reflection, `dynamic`, runtime code generation or `Marshal.PtrToStructure` anywhere.
- Binary data is parsed with `BinaryPrimitives` over spans.
- Unsafe code only in `Host.Sdl` and `Render.Vulkan`; native callbacks use `[UnmanagedCallersOnly]` and never throw.
- Shaders are precompiled SPIR-V; no runtime shader compiler.
- Trim/AOT analyzer warnings must be fixed, not suppressed.
- Verified: `dotnet publish src/WingCommander -c Release -r win-x64` gives a 2.3 MB `wc1.exe`
  (game, audio, Vulkan renderer) plus `SDL3.dll` and `THIRD-PARTY-NOTICES.txt`, with no ILC warnings;
  the published binary passes `--window-test --vulkan-validation` (see `BUILD.md` for the
  Windows linker PATH workaround).
