# Progress: flight UI layer (WingCommander.Game.Flight)

Owner: flight work stream. Specification: `docs/analysis/flight-ui.md` (§7 design, §7.6 milestones).
Owned files: `src/WingCommander.Game.Flight/**`, `tests/WingCommander.Game.Flight.Tests/**`, this file.
Build with a private `--artifacts-path` (see STATUS.md).

## Status (2026-10-08, flight session 2: all milestones done)

| Milestone | State |
| --- | --- |
| M1 flight loop, CPU space view, dump, FlyMissionAsync | **done**, tested |
| M2 cockpit art, view geometries, cockpitless, HUD, instruments, lights, decals, palette | **done**, tested (cockpits, views, decals and explosion, visual checks) |
| M3 controls (key table, keyboard ramps, mouse, views, pause, volumes) | **done**, tested (key bindings, mouse, pause, simulator Esc, determinism) |
| M4 VDUs, comm, nav map | **done**, tested (VDU pages, knocked-out VDU static with the option on/off, comm menu and orders, nav map); message slot blink not tested |
| M5 sequences | **done**, tested: launch, autopilot cinema, scramble, landing approach + hangar landing, ejection, stranded, death, simulator seam (get ready / game over / score panel), canned scenes 0x12 / 0x13, attract mode |
| M6 audio bridges | **done** (IFlightMusicState, IFlightSoundWorld, silent stand-ins without host audio) |
| M7 R2 wiring | **done**, tested headless (sprite recorder, image cache, window mask, publish through `Display.Presented`); needs the host flag from wc1 (Requests) to go live |

Tests: 231 (`tests/WingCommander.Game.Flight.Tests`), all green: determinism (same seed → same
frame hashes and state hash, other seed differs; attract mode too), 20 fps present spacing, every
mission of the three campaigns (88) flown 300 frames without exceptions (every other one with
R2 recording), the five cockpits, the
camera views (F2..F6, F1 back, F1 cockpitless and back), the sequences (exact present counts,
16 fps pacing with the vertical-blank copies, Esc, state afterwards), the canned scenes (drawn
frames, cutscene rows only, the white jump frame), the attract mode until a key and the title
menu after it (`FlightLayerTests`), the simulator flight with the shared arcade score and time. R2
(`SpaceSpriteTests`): nothing recorded without an R2 renderer or with the option off; the recorded
list is exactly the drawn objects in painter order with sub-pixel positions within one pixel of the
CPU position; for the front, rear, chase, fleet-overview and cockpitless views the classic frame
with R2 differs from the CPU frame only by space-colour pixels, and composing the recorded sprites
with the CPU (window mask, clip, background index, integer positions) gives the CPU frame **pixel
for pixel**; images decode with the hot spot as origin and match the CPU draw; re-presents keep
the sprites, a changed frame drops them; a paused frame (modal panel) is identical with and
without R2; attract mode and canned scenes publish one frame per present. Every
frame carries the 3D state of its tick and the tick before (camera, all objects, draw order; each
sprite matches its object's screen position and image). Controls
(`ControlsTests`, keys as host events): throttle keys, Ctrl+-/= inert without and frame skip with
the Origin switch, afterburner, guns, VDU page keys, the toggles (once per press), target lock,
volume keys, pause until a key, Esc pausing a campaign flight only with the option, Esc ending a
simulator flight, nav map open/close with the clock
write, mouse steering, a long key script deterministic. Cockpit (`CockpitTests`): the comm menu
lists the wingman's orders ("To: SPIRIT", radio silence, return to base, never mind) and the
chosen order reaches the simulation; target and damage pages redraw the VDUs; the nav map
replaces the screen and the cockpit returns; a cockpit hit shows the explosion and leaves only
the decal (animated and Kilrathi Saga instant mode); a knocked-out VDU shows fresh static every tick
with the option and a frozen picture without (Kilrathi Saga).

PNGs looked at (scratchpad `flight-out`, written only with `WC1_FLIGHT_PNG_DIR`): Hornet front
view (compared with `reference/wc1-re/screenshots/cockpit-combat.png`: same layout of art, VDUs,
readouts, scanner, hand); Rapier with a planet and the nav pointer; Scimitar (VDUs at the top);
Raptor with a comm transmission (Angel's portrait on the right VDU, yellow HUD line); the SM2
Dralthi on the Hornet cockpit; left/right/rear cockpit art, chase camera letterbox, fleet
overview, cockpitless view with the radar grid and instruments over space. Sequences: scramble for the
Hornet, Rapier, Scimitar, Raptor and the SM2 Dralthi (Hornet hangar; compared with
`reference/wc1-re/screenshots/tigers-claw-hangar.png`), the landing approach over the flight deck,
the deck crew, the hangar with the deck officer's comment, ejection (seat, pod interior "BEACON
ACTIVE", letterbox), stranded text and "THE END", death (pilot, white frame, explosion), the
simulator with score panel, the victory scene (Kilrathi ship, the planet object, the 2D planet),
the escape scene (capital ships, the carrier, the white jump frame), the attract mode (opening
text, dogfight, title logo, "Design by Chris Roberts" over the asteroids; the DOS screenshot
`title-screen.png` shows the WING/COMMANDER bars left on screen by the intro, which the attract
mode never overwrites). Cockpit pages: the comm menu ("VID-COM SYSTEM / To: SPIRIT", three
choices), target ("AUTO TARGETTING, Target: None") and damage ("NO INTERNAL DAMAGE") pages, the
nav map (Confed Nav Scan, Vega XR-2315 / Enyo, flight path, "Standard time 00 00"), the cockpit
explosion flash on the dashboard and the decal after it, the knocked-out right VDU with green
static (option) and frozen (Kilrathi Saga).

## Code layout

`FlightLayer` (public, `IFlightLayer` + `Rooms.ITrainSimFlight`) creates one `FlightSession`
per game (lazily, after `Wc1Game.Initialize`). `FlightSession` is a partial class holding the C
globals of the flight UI, split by area:

| File | C functions |
| --- | --- |
| `FlightSession.cs` | construction, startup switches, text contexts, campaign data (CAMP section 0 planets) |
| `FlightSession.Loop.cs` | RunSpaceFlight, RenderSpaceViewFrame, Draw_3Space_Frame, RefreshCockpitStatus, update_cockpit, Alt+N/M |
| `FlightSession.SpaceView.cs` | GetScreenUpdateFlag, initialize_view_buffer, clear_view_buffer, dump_buffer_to_screen, set_up_screen_viewport, draw_sorted_objects_to_buffer, intro_drawbackgroundships, white flash, cockpitless buffer dance |
| `FlightSession.SpaceSprites.cs` | R2: SdlBeginSpaceFrame / SdlRecordSpaceSprite / SdlCompleteSpaceFrame / SdlCancelSpaceFrame, BuildSpaceViewMask, the presented-frame snapshot (gl_renderer.c), sub-pixel positions, sprite image cache, CPU composition (pause, tests) |
| `FlightSession.CockpitPicture.cs` | InitializeCockpitResources, free_cockpit, initialize_cockpit, ResetCockpitPaletteEntries, cockpit shapes |
| `FlightSession.Instruments.cs` | lights, vdu_polygon bars, readouts, update_digital_readouts, scanner, pilot hand, decals and cockpit explosion, weapon launch animation |
| `FlightSession.Hud.cs` | HUD message line (print_message_text with its wrap quirk), timers, ShowOnScreenMessage (modal panel), pause/version banners, overlay_head_up_display, draw_target_box, RestoreTransientCockpitGraphics |
| `FlightSession.Vdus.cs` | VDU mode stack, update_VDUs, SelectCockpitVduMode, malf_noise + SDL static, message slots, weapons/damage/target/navigation pages |
| `FlightSession.Comm.cs` | COMMUNIC.DAT speech, portraits, real_vid_transmit, vid_transmit, vid_equiv, npc_communication, comm menus, Chosen_communicate_option |
| `FlightSession.NavMap.cs` | InflightComputer and the nav map drawing (BuildMap, labels, markers, legend, time, selection) |
| `FlightSession.Input.cs` | player_input, process_player_input, init_player_input, get_player_input, HandleFleetOverviewInput, SelectNextExternalViewObject |
| `FlightSession.Controls.cs` | HandleSpaceFlightControls (both key tables), view changes, F1 cockpitless toggle |
| `FlightSession.TrainSim.cs` | arcade score panel, wave bonus, FigureArcadeTime, GetArcadeBonus, ITrainSimFlight support |
| `FlightSession.Events.cs` | ISimulationEvents, ICockpitState, IShapeBounds |
| `Sequences/FlightSession.Launch.cs` | LaunchPlayerShip, DrawLaunchDoorFrame, visit_the_cinema, autopilot UI |
| `Sequences/FlightSession.Scramble.cs` | scramble, landing (2D hangar), DrawScrambleFrame, ConfigureScrambleActor, DrawScrambleActor, InitializeConversationViewport, ResetScreenClipToFullHeight, RefreshMemoryStatusOverlay, InitializeConversationText |
| `Sequences/FlightSession.LandingApproach.cs` | ShowCarrierLaunchSequence (the landing approach) |
| `Sequences/FlightSession.AfterFlight.cs` | GameFlow's landing/ejection/stranded/death branches, ejection_sequence, stranded_sequence, death_sequence |
| `Sequences/FlightSession.CannedScenes.cs` | the 3D halves of ShowCampaignVictorySequence and ShowTigerClawEscapeScene (`ICannedSpaceScene`), CreateCannedSceneObject use, the renderer half of init_3Space_objects |
| `Sequences/FlightSession.Attract.cs` | the first half of Title_Sequence, DrawTitleLogo, the credits |
| `Cockpit/CockpitTables.cs` | the layout tables of globals.c |
| `Audio/FlightAudio.cs` | IFlightMusicState, IFlightSoundWorld, silent music/sfx backends when `Wc1Game.Audio` is null |
| `FlightShapes.cs` | ShapeRef → ShapeTable cache (missing/raw/empty sections → null) |

## Deviations

- **SM2 Dralthi (PCSHIP.V05)**: ship types without a layout use the Hornet cockpit (cockpit 0)
  and `Sim.CockpitView = 0` (ADR-012, confirmed in integration).
- **Presentation options (ADR-012, `FlightOptions`, all on by default)**: planets as scaled
  sprites; VDU static noise (SDL xorshift32, own seed) with DOS sound 23; cockpit explosion
  animation (the KS shape free is skipped); Esc pauses campaign flight; credit count once.
- **Developer keys**: Ctrl+-/= (frame skip) and Alt+N/M (flight frame rate) work only with the
  `Origin` switch; without it Ctrl+-/= are inert.
- **Safety guards** where the original reads out of bounds: flag_objective(-1) after a
  simulator landing (no home-base objective) is skipped; objective index -1 in nav_note,
  objective_name and the music state; message slots and nav labels beyond their tables dropped;
  an unallocated space buffer is not dumped (the original exited with "bad viewport").
- **Weapon launch animation**: always animated (the original had no save buffer, hence no
  animation, when the loadout held no missile at cockpit set-up).
- **Silent audio stand-ins**: without host audio the flight runs its own `MusicDirector` and
  `SoundEffectManager` over silent backends so the random sequence and audio state are the
  same with and without sound.
- `SnowViewport`/`PlaySnowStaticSound` of Graphics/Audio are called as in KS and the static is
  added on top when the option is on.
- Flight exit stops sound effects through `SoundEffectManager.StopAllSounds` (also clears the
  afterburner flag; the SDL port's `SdlStopDosSoundEffects` did not).
- `StartupOptions.StartNavPointOverride` defaults to 0 in Game (C: -1); 0 is treated as "no
  override" (see Requests).
- **Uninitialised locals of the victory scene**: `ShowCampaignVictorySequence` reads its spawn
  countdown and planet scale before setting them at the first projectile spawn; the port starts
  the countdown at 8 (as after a spawn), so the first pair uses the computed planet scale.
- **Landing approach scene object** outside 0..63 (no collision object) uses slot 1 (the
  original indexed the object arrays with it).
- **Attract credits, literal mode** (`FixedCreditCount` off): the count grows by 9 per attract
  run with the Saga option and is clamped to the 19 cards (the original read past the table);
  the Game skips the attract call after Esc, so the count grows per run, not per title visit.
- **Renderer half of `init_3Space_objects`** (`cScreenViewportMode = -1`, `bScriptedView = 0`):
  the simulation leaves it to the renderer, so the flight does it in its `InitMission` and
  `Init3SpaceObjects` wrappers (missions, simulator, attract mode, canned scenes) whenever the
  3D space is not active yet.
- **R2 recorder** (follows the SDL port's GL renderer, with these differences): sprites replace
  the colour the buffer was cleared to (0x0F on a hyperspace-flash frame, so objects stay visible on
  the white frame; the reference always used 0xBF); attachments (engine flames, turrets) keep their
  offset to the parent's sub-pixel position (the reference anchored only engine flames, through
  its own placement code); the 2D planet and projectiles of the victory scene are recorded too
  (the reference drew them on the CPU); `CancelSpaceSpriteFrame` keeps the shown sprites until the
  classic frame changes instead of dropping them at once (no frame without objects between the
  flight and the next picture); before a modal panel covers a frame (pause, version banner) the
  CPU composes the shown sprites into it, so the paused picture keeps its objects (the reference
  showed an empty window while paused). Every recorded frame also carries the 3D state of its tick and of
  the tick before (`SpaceView.Current`/`Previous`, `SpaceViewState` from `CaptureSpaceView`, mapping
  of rendering.md §3.2; sprites keyed like the draw list, so the nav pointer and the Kilrathi Saga
  planets match what is drawn). `RenderFrame.Interpolation` is not set (see Requests).

## Original behaviour kept (literal, ADR-012)

- `nCommSpeakerObject` starts at 0, so no Kilrathi taunts start before the first transmission
  of a game ends (RandomBelowOrEqual(5000) is still drawn every tick).
- The comm menu accepts one key past the list (command -1 goes to `Request`).
- The nav map selects labels with the flight-path index although the label table is indexed
  by objective (RE-CHECK).
- `vid_transmit` tests the address of a speech row (always true).
- The damage alarm sound plays every tick (the KS alarm handle is never stored).
- `print_message_text` writes its line break ahead of the output (only visible when the wrap
  falls on a space).
- MissionStatistics are read live when the flow asks (after the landing sequence and
  free_3Space, which reset the wingman like the original).
- **Key queue duplicates** (bKeyEventQueueEnabled, on in every flight): each key first queues its
  Windows virtual-key code, then its scan code; `player_input` runs the steering code for every
  queued key, and several letters' virtual-key codes are steering scan codes (G/H/I/K/L/M/O/P/Q/R/S,
  digits 3/4 roll). So L also centres the stick, H/P nudge the pitch for one tick, and with Ctrl
  the duplicate of M (0x4D, Right) raises the music by one step and that message flushes the
  queue before M's own scan code: **Ctrl+M never toggles the music in flight, and Ctrl+P lowers
  the effects volume instead of pausing** (Ctrl+S still toggles: its duplicate 0x53 only rolls).
  Same in winmain.c and the SDL port (VK first, then the scan code). Tested as such.
- Keys are read with the original's latency: a key pressed right after a present is seen two
  ticks later; the first mouse move after the flight starts is dropped (the pointer warp's echo,
  `PointerMovedByKeyboard`); the pause needs the P release, then any key press.

## Requests (for integration)

1. **StartupOptions.StartNavPointOverride** should default to -1 (C: `nStartNavPointOverride = -1`);
   with the default 0 every campaign mission would start at nav point 0 if copied literally.
2. **bKeyEventQueueEnabled**: GameFlow sets it to 1 before the briefing (nav.c 1551) and
   Title_Sequence clears it (nav.c 2062); `CampaignFlow` does not. Flight sets it in
   `FlyMissionAsync` meanwhile.
3. **Joystick layer** (not ported): flight handles type-6 samples and the button double click
   once they are queued; Ctrl+J (calibration) is inert.
4. **Nav map sharing**: the briefing (`Screens/Scenes/BriefingMap`) and the in-flight map are
   two ports of the same nav.c code; a shared Game class would need the simulation's objective
   state behind an interface. Not urgent.
5. **Simulation, objective -1** (`SpaceSimulation.Objectives.cs`): `SetObjectiveRange`,
   `LocateMobileObjective` and `MobileObjective` index `MissionObjectives[CurrentObjective]`
   directly and throw for objective -1 (simulator missions while the right VDU shows the
   navigation page: `draw_3d_scanner` calls `set_objective_range(1)`). The original reads
   `aMissionObjectives[-1]` (the simulation's `ObjectiveRecord(-1)` layout). Workaround: the
   flight skips `SetObjectiveRange` while the objective is outside the table (the range keeps
   its value; the scanner marker uses position zero).
6. **Simulation, `Init3SpaceObjects`**: could also reset `ScriptedView` (C: `bScriptedView = 0`
   in `init_3Space_objects`); the flight does it meanwhile (harmless if both do).
7. **Game, intro to attract**: the attract mode only writes screen rows 24..151, so rows 0..23
   and 152..199 keep what the intro left (the DOS title screenshot shows the WING/COMMANDER
   bars there). `TitleSequence` calls the attract with whatever the intro left on screen; check
   that the DOS intro port ends on that frame.
8. **wc1 host, R2 on screen**: `Program.cs` should create the layer with
   `new FlightLayer(game, new FlightOptions { RendererSupportsSpaceSprites = renderer is VulkanRenderer { SupportsSpaceSprites: true } })`
   (the flag is false for the SDL_Renderer fallback and headless hosts, which keeps the CPU path),
   and may offer a switch for `FlightOptions.SpriteSpaceView = false` (classic sprites on Vulkan).
   Then an on-screen check in a mission with `--vulkan-validation` (rendering next step 1).
   `RenderFrame.Interpolation` (0..1 since the last 20 Hz tick) belongs to the host loop or the
   runtime, which see the time between ticks; the flight only publishes `Current`/`Previous`.

## Next steps

1. More tests (not blocking): message slot blink, HUD brackets and the lock spiral with an enemy
   in view, nav map selection by click, comm menu with an enemy
   target, ejection by Ctrl+E (its random draw).
2. With the wc1 flag set (Request 8): an on-screen R2 check in a mission (capture vs CPU frame).
