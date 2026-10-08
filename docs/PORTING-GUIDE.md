# Porting guide: C reference -> C#

Read `ARCHITECTURE.md` first. This file is the rulebook for anyone porting a C unit.

## Where to look in the reference

- `reference/wc1-re/src/*.c` game core; `src/sdl/*` SDL2 host + DOS-data support (OriginFX,
  DOS intro, LZW); `src/ix/*` Kilrathi Saga audio library (not needed for DOS data);
  `third_party/ymfm` OPL emulator.
- `include/wc1.h`, `include/wcdata.h` (types, record layouts), `include/globals.h` (all
  globals with addresses), `include/wc1funcs.h` (prototypes), `src/map` (functions by address),
  `docs/ORDER.md` (which address range lives in which file).
- `#ifdef SDL_PORT` blocks are the SDL port's fixes (64-bit safety, DOS data support,
  guarded out-of-range reads). Prefer the SDL_PORT behaviour unless it is purely
  platform glue.
- Function names ending in `Fn<addr>`, `Hook`, `Thunk` or starting with `DoLocal`/`Get/SetTbl`
  are operational labels, not original names. Give them a descriptive name in C#.

## Subsystem specs

`docs/analysis/*.md` are the specifications. When the C code and the spec disagree, the C
code wins; fix the spec and note it.

## Type mapping

| C | C# |
| --- | --- |
| `short` game state | `short` (keep it where values are stored, compared with wrap-around or saved); locals and loop counters may be `int`. Reproduce truncation with `unchecked((short)...)`. |
| `unsigned short` / `unsigned char` / `signed char` | `ushort` / `byte` / `sbyte` |
| `int` / `long` (32-bit in MSVC) | `int` |
| `FixedVector` | `Core.Numerics.FixedVector` (mutable struct, 24.8 fixed) |
| `MultiplyFixed`/`DivideFixed`/trig | `Core.Numerics.FixedMath` (FPU-exact, never `>> 8` shortcuts) |
| `rand()` and wrappers | `Core.Numerics.CRandom` (one shared instance) |
| `unsigned char *` packet/shape pointer | `ReadOnlyMemory<byte>` / parsed class (`PacketFile`, `ShapeTable`, ...) |
| parallel arrays `aXxx[64]` | fields of a struct per slot (array of structs), slot index is the identity |
| enums with `-1` sentinels | C# enums with an explicit `None = -1` member |
| `#pragma pack` disk records | explicit parsers with `BinaryPrimitives`, never struct overlays |
| `exit()` on fatal errors | throw `GameDataException` (data) or `InvalidOperationException` (logic) |
| `exit_squadron`/window close | `GameExitException` |

## Naming and documentation

- C# PascalCase of the original name when the original name is real (`house_keep` ->
  `HouseKeep`, `set_objects_data` -> `SetObjectsData`); descriptive names for operational labels.
- Every ported method gets `/// <remarks>C: original_name (0xADDRESS, file.c)</remarks>` so
  anyone can grep the reference.
- Globals lose Hungarian prefixes in C#: `nTickCount60Hz` -> `Ticks60Hz`, `aeObjectClass` ->
  `Class` field of the object struct.
- One public type per file; files grouped in folders by subsystem.

## Behaviour rules

1. Gameplay math is integer/fixed point exactly as the reference, including FPU rounding
   (`FixedMath`). No floating point in simulation except where the C code uses it.
2. Every random number goes through the shared `CRandom` in the original call order.
3. Rendering is pixel-exact; the asm-derived algorithms in `analysis/graphics.md` win over
   the reference's portable C where they differ (documented there).
4. Keep the original sequential structure, but as **async coroutines** (ADR-009). Where the C
   code pumped, slept, presented or waited for a timer, the C# code awaits:
   `await display.PresentAsync()` (DIBslam + DIBslamReal), `await events.WaitForInputKeyAsync()`,
   `await timing.WaitForVerticalBlankAsync()`, `await timing.WaitForFrameTickAsync()`, or for an
   explicit polling loop `await scheduler.Delay(1)` when nothing was consumed. Never block,
   never `Thread.Sleep`, never await anything except scheduler awaitables and coroutines.
   Simulation code (Simulation project) is tick-based and has no awaits at all.
5. Known original bugs: reproduce when they are gameplay-visible, document in the spec;
   fix memory-safety bugs (out-of-range reads) the way the SDL port's guards do.
6. Never port Win32/DirectDraw/registry/CD-ROM/mono-debug logic; the host contract covers it.

## Tests and verification

- Each library has a test project; data-driven tests use `[DataFact]`/`[DataTheory]`
  (`tests/Shared/GameData.cs`). Prefer whole-data sweeps (decode every section of every
  file) plus a few exact golden values verified by hand from the C code or the data.
- `wc1tool` gets a command for every data format (dump, export to PNG/WAV) so results can
  be inspected. Exported files go to a scratch or user-chosen directory, never the repo.
- Build/test without fighting parallel workers: `dotnet test tests/<Project> --artifacts-path <private dir>`.

## Documentation duty

A port is done when: code builds without warnings, tests pass, and the subsystem's
progress file `docs/progress/<subsystem>.md` lists what was ported (C function -> C# member),
what is missing, deviations and open questions. `docs/STATUS.md` is the session journal
and is only edited by the coordinating session.
