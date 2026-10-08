# Wing Commander 1 — .NET 10 Port: Documentation Index

This folder documents the project. Every finding, decision and work-in-progress note lives
here so that anyone can build a full understanding from these files alone.

**Where to start:** read this file, then `STATUS.md`, then `ARCHITECTURE.md`, `DECISIONS.md`
and `PORTING-GUIDE.md`. Dive into `analysis/*.md` only for
the subsystem you are working on, and check `progress/*.md` for what is already ported.

## Project goal

Port *Wing Commander 1* (1990, Origin Systems) to **C# / .NET 10**, cross-platform
(Windows, Linux, macOS) and **NativeAOT**-compilable, using the DOS game data from the
GOG release. The source of truth for behaviour is the C reverse-engineering project
[neuromancer/wc1-re](https://github.com/neuromancer/wc1-re) (Kilrathi Saga Win32 build
reconstructed in C, plus an SDL2 port that reads DOS data). We restructure it into a
layered application architecture and optimise where it is safe, while keeping gameplay,
rendering and data compatibility faithful.

## Files

| File | Purpose |
| --- | --- |
| `STATUS.md` | Project journal: done, learned, next. **Updated at the end of every work block.** |
| `ARCHITECTURE.md` | Projects, dependency rules, host model, state ownership, AOT constraints. |
| `DECISIONS.md` | Architecture decision records (libraries, host model, licences, naming). |
| `PORTING-GUIDE.md` | Rules for translating C units to C#: types, naming, behaviour, tests, docs duty. |
| `BUILD.md` | Build/test/publish commands, prerequisites, known toolchain issues. |
| `DATA-FILES.md` | Inventory and verified structure of the GOG DOS data files. |
| `analysis/resources.md` | Packet container, LZW, INSTALL.DAT logical files, CFG, memory model. |
| `analysis/graphics.md` | Viewports, RLE shapes, rotation rasteriser, lines, fonts/text, palettes and fades. |
| `analysis/host-input-timing.md` | Host interface, event manager, scan codes, timing, call graph with blocking points. |
| `analysis/audio.md` | OriginFX/AdLib music and SFX, timbres, OPL2 model, mixer, game-facing audio API. |
| `analysis/simulation.md` | Fixed point, RNG, objects, ships, weapons, AI, mission loading, frame loop. |
| `analysis/gameflow-screens.md` | Game state machine, campaign, save games, briefing scene engine, screens. |
| `analysis/flight-ui.md` | Flight UI layer: flight loop, controls and key bindings, cockpit/HUD/VDUs, comm, nav map, CPU space view and R2 hooks, flight sequences, simulation interface, C# design. |
| `progress/*.md` | Per-subsystem porting progress: C function -> C# member, gaps, deviations. |

## Locations

| What | Where |
| --- | --- |
| C reference (git clone, ignored by git) | `reference/wc1-re` — re-clone with `git clone --depth 1 https://github.com/neuromancer/wc1-re reference/wc1-re` |
| GOG game install (DOS version) | your copy of the GOG release (data in `GAMEDAT/`), set in `config.json` (see `config.example.json`) |
| .NET SDK | 10.0.400 (`global.json`) |

## Working rules

1. Never commit game data or files derived from it. Tests locate the data via
   `WC1_GAME_DIR` or the repository's `config.json` and skip when absent.
2. Never modify `reference/wc1-re`; it is read-only input.
3. Every ported layer is verified against real data (tests, `wc1tool` exports) before it is
   marked done.
4. Keep `docs/` current: a feature is not finished until its docs are.
