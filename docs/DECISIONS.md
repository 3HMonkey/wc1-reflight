# Architecture Decision Records

## ADR-001: Target .NET 10, NativeAOT, cross-platform
Accepted 2026-10-07. All non-test projects set `IsAotCompatible`, trim/AOT analyzers on,
`InvariantGlobalization`. No reflection-based serialization, no `dynamic`, no runtime
code generation. `wc1.csproj` publishes with `PublishAot=true`. Verified working on win-x64.

## ADR-002: SDL3 via `ppy.SDL3-CS`
Accepted 2026-10-07. Chosen over Silk.NET (heavier, generic) and hand-written P/Invoke.
`ppy.SDL3-CS` (osu! team, package version 2026.1002.1) is a thin LibraryImport-based
binding that bundles SDL3 native binaries for win-x64/x86/arm64, osx-x64/arm64,
linux-x64/arm64/arm. Namespace `SDL`, static class `SDL.SDL3`, pointer-based API (needs
unsafe). All SDL usage is confined to `WingCommander.Host.Sdl`.

## ADR-003: Layered projects
Accepted 2026-10-07 (revised the same day from a single Core project). Core / Graphics /
Audio / Simulation / Game / Host.Sdl / wc1 / wc1tool, each library with its own test
project. Rationale: project references enforce the dependency rules, each subsystem can be
built and tested in isolation (also by parallel workers), and the composition root stays
small. Graphics, Audio and Simulation must not reference each other.

## ADR-004: Keep the original blocking control flow; ScummVM-style host
**Superseded 2026-10-07 by ADR-009** (the user wants a modern Vulkan renderer, later real 3D
models and ray tracing; a blocking game that owns the loop cannot drive that well).
Accepted 2026-10-07. Supersedes the first draft ("convert every screen to an Update/Draw
state machine"). The host analysis (`analysis/host-input-timing.md` §5-6) shows the game
consists of 20+ nested blocking loops that interleave drawing, presenting with a frame
throttle, palette fades on vertical blank, audio servicing and event polling, often
several calls deep (text drawing presents, the 3D intro runs the full simulation, the nav
map is modal inside flight). Rewriting these as state machines or async coroutines would
touch thousands of lines and risk subtle timing drift.

Decision: the game keeps the original structure and runs on the main thread; it calls a
passive `IGameHost` (present, poll event, sleep, clock, vertical blank, mouse warp/grab,
joystick, audio device). This is the proven ScummVM `OSystem` model. Window close raises a
quit flag; the game's event pump throws `GameExitException` (replacing `exit(0)`).

Rejected alternatives:
- Frame-driven state machines everywhere: high porting cost and drift risk (may still be
  applied per screen later where it pays off, e.g. for a modern options overlay).
- Game on a worker thread with a message-pump bridge: adds cross-thread hand-over of frames,
  palettes and events for no benefit once the host is single-threaded.
- C# `async` coroutines: viral through the whole call graph.

## ADR-005: OPL2 emulation = C# port of ymfm (YM3812)
Accepted 2026-10-07. DOS music and sound effects need a Yamaha OPL2. ymfm (Aaron Giles,
BSD-3-Clause) is what the reference SDL port uses, so a port gives bit-identical output
for regression tests, and the licence is compatible with static NativeAOT linking.
Nuked-OPL3 C# ports (LGPL-2.1) and DOSBox dbopl (GPL) were rejected for licence reasons.
The emulator sits behind an `IOplChip` interface. Source: `reference/wc1-re/third_party/ymfm`.

## ADR-006: Tests use real game data when available
Accepted 2026-10-07. `WC1_GAME_DIR` env var or the repository's `config.json` (since 2026-10-08).
`[DataFact]`/`[DataTheory]` skip when the data is missing so CI without data still passes.
Never commit game data or files derived from it (exported PNG/WAV stay outside the repo).
Exception (user decision 2026-10-08): the README screenshots in `docs/images/`, rendered by
`scripts/readme-screenshots.py`; they show game art (copyright Electronic Arts), as noted in
the README. No other exports, sprites or sounds.

## ADR-007: Gameplay numbers follow the Kilrathi Saga reconstruction
Accepted 2026-10-07. The reference reconstructs the 1996 Win32 Kilrathi Saga executable.
Ship/weapon statistics (`aObjectTypeData`), AI tables and the FPU-based fixed-point math
come from it, even though we run on DOS data. The DOS WC.EXE may differ in details (ship
stats, frame pacing, 6-bit palette handling); extracting DOS values is a possible later
task. Pacing: 20 Hz frame-locked simulation in flight, 16 fps cinematic (KS behaviour).

## ADR-008: Naming and namespaces
Accepted 2026-10-07. Namespaces follow folders (`WingCommander.<Project>.<Folder>`).
Never name a namespace like a BCL type (`...Core.Math` hid `System.Math`; it is now
`WingCommander.Core.Numerics`). Original developer function names are kept in PascalCase
with the C name and address in an XML `<remarks>` (see `PORTING-GUIDE.md`).

## ADR-009: Host-owned loop, deterministic scheduler, game code as coroutines
Accepted 2026-10-07, supersedes ADR-004. Goals that drive it: a modern renderer running at
display rate (60-240 Hz) with interpolation, later real 3D ship models and ray tracing,
overlay menus, replays, save states.

- The **host owns the main loop**: poll OS events, `app.Update(elapsed)`, `renderer.Render(frame)`.
  The game never blocks the thread and never presents by itself.
- The game runs on a **deterministic virtual clock** (`GameScheduler`): continuations are
  resumed in due-time order; virtual time advances by real elapsed time on the SDL host and
  by scripted steps in tests. Every timing rule of the original (16/20 fps present throttle,
  1/60 s frame timers, 70 Hz vertical blank for fades) becomes a deadline on this clock.
- Original blocking loops are kept **sequential** but written as C# `async` coroutines
  (`await` where the original pumped/slept/presented). The scheduler installs its own
  `SynchronizationContext`, so every continuation runs on the game thread in FIFO order;
  game code must never await anything but scheduler awaitables and its own coroutines.
- The game "presents" by copying its working 320x200 buffer (plus software cursor) into a
  **front buffer**; the renderer shows the front buffer at display rate. Palette writes are
  visible immediately (as in the original), pixel writes only after a present.
- Simulation and flight stay **fixed-step at 20 Hz** (all constants are per frame); a later
  3D view interpolates between the last two simulation snapshots.
- Input: host events are queued with their arrival time and enter the original event queue
  only when the game pumps, preserving the original semantics.

## ADR-010: Vulkan renderer, cross-platform
Accepted 2026-10-07 (user requirement: "modern Vulkan renderer, cross-platform"; long term:
real 3D models and ray tracing).

- Bindings: **Vortice.Vulkan** (MIT, .NET 9/10, function-pointer based, used by Stride).
  Silk.NET.Vulkan 2.x was the alternative; Vortice is leaner and targets current .NET.
- Window and surface: **SDL3** (`SDL_WINDOW_VULKAN`, `SDL_Vulkan_GetInstanceExtensions`,
  `SDL_Vulkan_CreateSurface`); the renderer only sees a small surface-source interface.
- Platforms: Windows and Linux use the system Vulkan loader; macOS uses **MoltenVK**
  (portability enumeration/subset, Vulkan 1.2 feature level; no ray tracing).
- Baseline: Vulkan 1.2 with dynamic rendering and synchronization2 (core in 1.3, extensions
  on 1.2/MoltenVK). Optional features (acceleration structures, ray query, ray tracing
  pipeline) are detected at start-up and only enable extra effects.
- Shaders: GLSL sources in the repo, compiled to SPIR-V without requiring an installed Vulkan
  SDK (NuGet shaderc in a build/dev tool); SPIR-V is embedded, no runtime compiler.
- Render passes are layered so the roadmap fits without rewrites:
  R1 classic layer (indexed 320x200 + palette lookup in a shader, 4:3 letterbox, filters);
  R2 space view at output resolution (sprites as quads/billboards, stars, effects) with the
  cockpit/HUD composited from the classic layer; R3 glTF ship meshes with PBR, HDR/bloom;
  R4 optional ray-traced shadows/reflections on capable GPUs.
- The SDL_Renderer presenter remains as a fallback when Vulkan is unavailable.
- Development: validation layers come with the LunarG Vulkan SDK (installed on the dev
  machine on 2026-10-07: 1.4.363.0). `wc1` enables them automatically when present in Debug
  builds (Release: off); `--vulkan-validation` / `WC1_VULKAN_VALIDATION=1` make them mandatory.
- Status 2026-10-07: R1 is the default renderer of `wc1`; offscreen tests, on-screen window
  test and NativeAOT publish all pass with validation (0 errors, 0 warnings).
- Status 2026-10-08: R2 is live: the flight layer records space sprites (Core.Rendering
  SpaceView) and the Vulkan sprite pass draws them at output resolution under the classic
  cockpit/HUD; `--classic-space` keeps the CPU path. R2b (interpolation), R3 and R4 are next.

## ADR-011: Saves and settings live in the user data directory
Accepted 2026-10-07.

- `SAVEGAME.WLD` and `wc1.cfg` (volumes, cheat flag) are read and written in the host's user
  data directory (`SDL_GetPrefPath("Origin Systems", "Wing Commander")`), never in the install,
  which may be read-only (Program Files, package managers, read-only media).
- On first use an existing `GAMEDAT\SAVEGAME.WLD` is copied there, so DOS saves carry over;
  the file format stays byte-identical to the original (8 bunks of 0x33C bytes).
- The install directory is only read (`GameDirectory`).

## ADR-012: Original bugs, defaults and input normalisation
Accepted 2026-10-07 (open questions of `analysis/flight-ui.md` §8.2).

- **Literal by default:** everything that affects gameplay, timing or the shared `CRandom`
  sequence follows the Kilrathi Saga build (ADR-007), including its bugs (for example the comm
  menu off-by-one that can pass command -1, timers that count rendered frames, the damage alarm
  handle that is never stored). Surprising reconstructed paths are ported literally and marked
  with a `RE-CHECK` comment until they can be compared with the disassembly.
- **Fixed by default, with a compatibility switch:** visual or audio-only defects of the KS build
  that the DOS game or the SDL port shows as intended: planets drawn as scaled sprites (KS: a
  dust dot), VDU static noise with its sound, the cockpit explosion animation, Esc pauses during
  campaign flight (SDL port), the Saga credit count computed once (KS grows it on every title).
- **Keys:** Alt+X quits the game (Win32 behaviour, `GameExitException`); frame skip and Alt+N/M
  stay developer options behind the `Origin` switch.
- **Key repeat is normalised:** the game ignores the operating system's auto-repeat and generates
  repeats itself on the virtual clock (500 ms delay, then 30 per second, the Windows default),
  so continuous fire and throttle behave the same on every OS and recorded input replays exactly.
- **Secret Missions 2 Dralthi cockpit (PCSHIP.V05):** no reconstructed code uses it; until a
  layout is derived from the art, the player's Dralthi uses a known cockpit layout (documented
  deviation).
- **Ownership at the simulation boundary:** NPC communication text and video
  (`npc_communication`, `vid_equiv`, `real_vid_transmit`) is UI (Game.Flight), called back by the
  simulation at the original statement positions; the autopilot computation is simulation, its
  cinematic is UI; every missing presentation effect (warp flash, phase-3 cockpit messages) is
  an `ISimulationEvents` member.

## ADR-015: Pause menu and settings in config.json
Accepted 2026-10-08 (user request: "a pause menu with settings on Esc").

- **Esc opens the pause menu** where the original had no use for it: campaign flight (instead of
  the "GAME PAUSED" banner of ADR-012), the training simulator (adds "End simulation", the
  original's Esc), the rec room, the barracks and the title menu. Cutscenes, briefings and
  conversations keep Esc for skipping. `--ks-literal` keeps the original flight behaviour.
- The menu (`Screens/Ui/PauseMenu`, `SettingsMenu`, `MenuSession`) is a game screen drawn into
  the 320x200 frame with font 0, so it works with every renderer and in headless tests, and the
  output-resolution text shows it in the replacement font. It saves and restores the picture,
  the cursor and the input state; the flight stands still while it is open.
- **Settings**: music and sound volume, fullscreen, picture filter, aspect ratio, vsync, sharp
  text, fonts, key help. They apply at once (the renderer reads its settings every frame; the
  window through `IDisplayControl`, implemented by wc1) and are saved to `config.json` in the
  sections `audio`, `display`, `text`, `interface` (`UserSettings`, `GameConfiguration` keeps
  unknown properties). Command-line options override the saved display settings for one run.
  wc1.cfg keeps the original volume format and is still written; config.json wins when both
  exist. Without a config.json, wc1 creates one next to the executable on the first save.
- Planned next: key bindings in the `controls` section.

## ADR-014: Licence GPL-3.0
Accepted 2026-10-08. The port translates the reconstructed C source of neuromancer/wc1-re
(GPL-3.0): function structure, names, tables and constants. It is a derivative work and is
therefore licensed under the GPL-3.0 (`LICENSE`). Bundled third-party parts keep their
GPL-compatible licences (`THIRD-PARTY-NOTICES.txt`): ymfm BSD-3-Clause, Tektur and CHAWP OFL 1.1,
SPACE WING LEADER CC BY 3.0; NuGet packages SDL3 (zlib) and Vortice.Vulkan (MIT). Game data is not
part of the project.

## ADR-013: Text at output resolution, replacement fonts, key help, config.json
Accepted 2026-10-08 (user requests: "sharpen all lettering", "show the key bindings in the
cockpit", replacement fonts, game path in `config.json`).

- **The game keeps drawing all text into the 320x200 frame**; the port follows every glyph
  instead of changing the game code. `GraphicsContext.DrawFontGlyph` reports each glyph and raster
  copies (`CopyViewportContents`, `FizzleFade`) report the runs they move to the
  `TextLayerTracker` (Graphics). Per pixel it keeps a stack of the glyph cells drawn over it with
  the value each one covered; any other change is detected by value. A glyph is drawn at output
  resolution only while **all** its foreground pixels are intact (text drawn over it does not
  count): erased or overdrawn text falls back to the classic pixels, so no fragments survive.
- At every present the tracker publishes `RenderFrame.Text` (`TextLayer`): the frame without the
  drawn glyphs' foreground, a per-pixel mask (`instance index >= mask - 1` may draw) and the glyph
  instances. The cursor is composited afterwards and masked out. Renderers without text support
  ignore the layer.
- **Glyph images** (`GlyphImage`): signed distance field at 8 texels per source pixel plus
  per-texel palette indices. Original glyphs are vectorized (`PixelOutline`: one-pixel staircases
  become diagonals, caps and real corners stay square); a TrueType replacement
  (`TrueTypeFont` reader, `FontReplacement`) is fitted into the original cell with the original
  cap height, baseline and a font-wide horizontal factor, so the game's layout is unchanged.
  Bundled: Tektur SemiBold (font 0), SPACE WING LEADER (fonts 1 and 2), CHAWP (font 3); sources
  and licences in `assets/fonts/`, compiled into WingCommander.Game as embedded resources, all
  licences in `THIRD-PARTY-NOTICES.txt` next to the executable; `--original-fonts` turns them
  off, `--classic-text` the layer. `build.bat` publishes the trimmed native build (3 files).
- **Key help** (`KeyHelpOverlay`, `KeyHelpLayout`): the flight publishes its key reference
  (`FlightKeyHelp`) while flying; renderers lay it out in the side margins when they are wide
  enough, otherwise as a translucent panel. F10 toggles it (port hotkey, consumed before the
  original input; saved as an optional fourth line `KeyHelp=0` in `wc1.cfg`).
- **Vulkan**: the classic pass uploads `TextLayer.Pixels`; a text pass (RG8 glyph atlas, R16 mask)
  draws the glyphs after the sprites and the overlay last. `ReferenceCompositor` is the CPU
  reference (wc1tool `snap --hd`, tests).
- **config.json** (repository root or next to the executable, not committed; template
  `config.example.json`) holds `gameDirectory`; it will also hold the settings of the planned
  pause menu (volumes, key bindings, display).
