# Building, testing, publishing

## Prerequisites

- .NET SDK 10.0.400 or newer feature band (`global.json`, rollForward latestFeature).
- NativeAOT publish on Windows needs the MSVC linker: Visual Studio (any edition, also
  Insiders/Preview) with the "Desktop development with C++" workload.
- Linux NativeAOT needs `clang` and `zlib1g-dev`; macOS needs Xcode command line tools.
- SDL3 native libraries come from the `ppy.SDL3-CS` NuGet package (`runtimes/<rid>/native`);
  nothing to install.

## Commands

```bash
dotnet build                                   # all projects
dotnet test                                    # xunit; data tests need WC1_GAME_DIR
dotnet run --project src/WingCommander          # game path from config.json (see config.example.json)
dotnet run --project src/WingCommander.Tools -- dump TITLE.VGA --nested
dotnet publish src/WingCommander -c Release -r win-x64 -o artifacts/publish/win-x64
build.bat [win-x64|win-arm64]                  # Windows release build into publish\<rid>
```

`build.bat` publishes the native, self-contained, trimmed executable without debug symbols
(`DebugType=none`, `UseSystemResourceKeys=true`) and puts the VS installer directory on PATH (see
below). Output: `wc1.exe` (about 4 MB; the replacement fonts are embedded resources of
WingCommander.Game), `SDL3.dll` (no static library is shipped by ppy.SDL3-CS) and
`THIRD-PARTY-NOTICES.txt` (ymfm, Tektur, SPACE WING LEADER and CHAWP licences in full).

Build outputs of the executable project land in `src/WingCommander/bin/<cfg>/net10.0/win-x64/`
because the project sets a RuntimeIdentifier implicitly through `PublishAot`/`SelfContained`.

Verified 2026-10-07: NativeAOT `wc1.exe` = 2.3 MB (game, DOS audio, Vulkan renderer; + `SDL3.dll`
3.0 MB and `THIRD-PARTY-NOTICES.txt`), no ILC warnings; the published binary passes
`wc1 --window-test --vulkan-validation`.

## wc1 options

```
wc1 [--game <dir>] [--scale N] [--fullscreen] [--filter nearest|sharp|linear] [--square-pixels]
    [--integer] [--no-vsync] [--renderer auto|vulkan|sdl] [--vulkan-validation]
    [--no-audio] [--skip-intro] [--classic-space] [--classic-text] [--original-fonts]
    [--ks-literal] [--frames N] [--host-check]
    [--window-test [--capture-dir <dir>]] [-- <original switches, e.g. p = no music>]
```

- With the Vulkan renderer, text is drawn at output resolution with the bundled replacement fonts
  (ADR-013); `--classic-text` keeps the pixel text, `--original-fonts` the vectorized originals.
  F10 toggles the key help in flight.
- With the Vulkan renderer, space objects are drawn as sprites at output resolution (R2);
  `--classic-space` draws them into the 320x200 frame like the original. `--ks-literal` turns off
  the visual fixes of ADR-012 (planets, VDU static, cockpit explosion, Esc pause, credit count).

- The game directory comes from `--game`, `WC1_GAME_DIR`, `config.json` (`"gameDirectory"`;
  searched in the current directory, the executable's directory and their parents; copy
  `config.example.json`), then the current directory.
- `--renderer auto` (default) uses Vulkan and falls back to SDL_Renderer; `vulkan` fails instead.
  Force the fallback for testing with `VK_LOADER_DRIVERS_SELECT=*none*`.
- `--window-test` runs a scripted smoke test of the window and renderer (resize, fullscreen,
  minimise, filters, vsync, aspect) and checks captured frames; exit code 5 on mismatches.
- `--host-check` shows the old host test pattern instead of the game.
- Saves and settings live in the user data directory (Windows:
  `%APPDATA%\Origin Systems\Wing Commander\`), see ADR-011.

`wc1tool snap --at ms,... [--input "ms key 0x39 [vk]; ms click x y"] [--audio] [--skip-intro]
[--hd WxH] [--original-fonts]` runs the game headless on the virtual clock and saves PNGs of the
displayed frame; with `--hd` also the output-resolution text and key help (CPU reference).
`wc1tool hd-text <font> ["text"] [--ttf font.ttf]` compares classic and output-resolution glyphs;
`wc1tool font-metrics <font>` prints cell widths and ink extents.


## Known issue: NativeAOT link fails with "vswhere.exe ... nicht gefunden"

Symptom (German Windows):

```
error MSB3073: Der Befehl ""Der Befehl "vswhere.exe" ist entweder falsch geschrieben oder;konnte nicht gefunden werden.;C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\...\link.exe" @"...link.rsp"" wurde mit dem Code 123 beendet.
```

Cause: the ILCompiler package runs `findvcvarsall.bat`, which calls the VS 18 `vcvarsall.bat`.
That script invokes `vswhere.exe` without a path; when
`C:\Program Files (x86)\Microsoft Visual Studio\Installer` is not on `PATH`, cmd prints an
error to stderr, MSBuild captures it together with stdout, and the garbage becomes part of the
linker path. Running inside a VS developer prompt does not help because the ILCompiler still
calls the script.

Fix: put the installer directory on `PATH` for the publish (or permanently in the user PATH):

```powershell
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;" + $env:PATH
dotnet publish src\WingCommander -c Release -r win-x64 -o artifacts\publish\win-x64
```

## Vulkan renderer and shaders

- No Vulkan SDK is needed to build or run (it only adds validation layers). GLSL sources live in `src/WingCommander.Render.Vulkan/Shaders/`; the
  SPIR-V in `Shaders/Compiled/*.spv` is **checked in** and embedded into the renderer assembly, so
  builds and the shipped game never run a shader compiler.
- After editing a shader, regenerate with shaderc from NuGet (Vortice.ShaderCompiler):
  `dotnet run --project tools/WingCommander.ShaderBuild` (`-- --check` only verifies; exit code 1
  when a `.spv` is stale or orphaned). The test `ShaderTests.CheckedInSpirv_IsNotStale` fails when
  the GLSL and the checked-in SPIR-V disagree (skipped where shaderc has no native binary).
- Renderer tests run offscreen on the local GPU and are skipped without a Vulkan device:
  `dotnet test tests/WingCommander.Render.Vulkan.Tests --artifacts-path <private dir>`.
- Validation: installed `VK_LAYER_KHRONOS_validation` is enabled automatically by the renderer
  (`ValidationMode.Auto`; `wc1` passes Disabled in Release builds); `WC1_VULKAN_VALIDATION=0`
  disables, `=1` requires it. With validation on, the renderer also enables synchronization
  validation (VK_EXT_layer_settings `validate_sync`; `WC1_VULKAN_VALIDATION_SYNC=0` turns it off).
  With the SDK installed every GPU test runs under validation and fails on any validation message
  (`VulkanTestBase`). `WC1_VULKAN_DEVICE=<name part|index>` picks a GPU.
