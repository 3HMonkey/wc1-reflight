# Porting progress

One file per subsystem, owned by whoever ports that subsystem:

| File | Project | Scope |
| --- | --- | --- |
| `resources.md` | Core | packet container, LZW, install table, game directory (done) |
| `platform.md` | Core, Host.Sdl | IGameApp/IHostServices, headless services, SDL3 host, window control |
| `graphics.md` | Graphics | raster library, shapes, fonts, palettes, fades, cockpit geometry (done) |
| `audio.md` | Audio | OPL2 (ymfm port), OriginFX, mixer, music director, sound effects (done) |
| `rendering.md` | Render.Vulkan (+ Core.Rendering) | Vulkan renderer R1 (done), R2 sprite pass core, roadmap R3/R4 |
| `simulation.md` | Simulation | objects, math, missions, physics, weapons (done), AI (phase 3); "Interface changes for Game" |
| `game.md` | Game | runtime, event manager, timing, display, intro, title, GameMain, flow skeleton |
| `screens-rooms.md` | Game (Screens/Rooms, Screens/Ui) | rec room, kill board, barracks (save/load), modal UI, TrainSim menus (done) |
| `screens-scenes.md` | Game (Screens/Scenes, Scenes) | conversation engine, briefing + nav map, debriefing, office, medals, funerals, MIDGAME, endings (done) |
| `flight.md` | Game.Flight | flight loop, cockpit, HUD, controls, VDUs, nav map, comm, flight sequences, attract mode, R2 wiring |

Each file contains:

1. **Status** — one paragraph: what works, verified how.
2. **Mapping** — table `C function (address, file) -> C# type.member`, with a status column
   (`done`, `partial`, `stub`, `todo`).
3. **Deviations** — every intentional difference from the reference, with the reason.
4. **Open questions / TODO** — things not verified or not yet ported.
