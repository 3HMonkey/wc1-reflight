# Wing Commander 1 — Flight UI Layer (flight loop, controls, cockpit, HUD, space view, flight sequences)

Analysis of the `wc1-re` reconstruction (Kilrathi Saga Win32 build plus its SDL2 port, which also
reads the GOG DOS data) for the port of the **flight UI layer** to C# / .NET 10: everything between
the simulation core (`docs/analysis/simulation.md`, C# `WingCommander.Simulation` with the phase-2
frame API described in `docs/progress/simulation.md`) and the screen during space flight. It covers
the `RunSpaceFlight` loop, player controls, cockpit art and instruments, HUD, VDUs, communication,
the in-flight nav map, the CPU space-view drawing and its composition with the cockpit, the
non-interactive flight sequences (scramble, launch, landing, autopilot, ejection, death, stranded,
training simulator, attract mode) and the complete interface to the simulation. Section 7 is the
C# design for `src/WingCommander.Game/Flight/`.

Verification (2026-10-07): every function named below was read in the reference. Resource
sections and frame counts were checked against the GOG data with `wc1tool dump --nested`,
`export-shape`, `export-view` and `hex` (PCSHIP.V00..V04, COCKPIT.VGA, PILOTANM.VGA, WINGMEN.VGA,
SCRAMBLE.VGA, PLANETS.VGA, OBJECTS.VGA, TITLE.VGA, SHIPTYPE.V00/V09, SHIP.V08). Where the C code and
another analysis disagree, the correction is listed in §8.

Source map (paths relative to `reference/wc1-re/`):

| File | Flight-UI content |
| --- | --- |
| `src/hudmsg.c` | `RunSpaceFlight`, `HandleSpaceFlightControls`, `Draw_3Space_Frame`, `RenderSpaceViewFrame`, `RefreshCockpitStatus`, on-screen messages and pause, training-simulator score panel, weapon selection, `warp`/`unwarp` (space-buffer flash) |
| `src/main.c` | `GameMain` dev switches, `player_input`, `process_player_input`, `players_flight_dynamics`, `fire_players_lasers`, `get_player_input` (joystick pump), space buffer helpers (`dump_buffer_to_screen`, `clear_view_buffer`, `initialize_view_buffer`, `GetScreenUpdateFlag`), palette flashes, `house_keep`, `Update_3Space`, view-8 camera keys |
| `src/cockpt.c` | HUD (`overlay_head_up_display`, `draw_target_box`, HUD text), cockpit lights/bars/readouts, scanner, VDU mode stack and displays, missile-lock state, target selection, comm chatter (`npc_communication`, `vid_transmit`), cockpit damage, pilot hand, `update_cockpit` |
| `src/screen.c` | Comm menu (`BuildCommunication*Menu`, `Chosen_communicate_option`), `real_vid_transmit`, `EndCommMenu`, wingman orders (`request`, phase-3 simulation) |
| `src/logic.c` | Cockpit resources (`LoadSpaceflightResources`, `InitializeCockpitResources`, `free_cockpit`, `init_vdus`), `initialize_cockpit`, palette reset, 3-space init/free, constellation, exhaust and child placement |
| `src/eventmgr.c` | `sort_object_depth`, `draw_sorted_objects_to_buffer`, `intro_drawbackgroundships`, `set_up_screen_viewport`, `TranslatePolledInputEvent` |
| `src/sound.c` | Damage VDU (`show_damage_disp`, `UpdateDamageDisplay`), `LaunchPlayerShip`, `ShowCarrierLaunchSequence` |
| `src/music.c` | Target VDU (`show_target_disp`, `DrawTargetRangeReadout`), scripted view (`parse_view_script`, `update_scripted_view`, `initialize_scripted_view`), `spacetrack`, `changetrack` |
| `src/text.c` | `show_info_disp` (VDU mode 8, unreachable) |
| `src/nav.c` | In-flight computer / nav map (`InflightComputer`, `BuildMap`, `DrawNavLocationReadout`, ...), `GameFlow`, `Title_Sequence`, `DrawTitleLogo` |
| `src/auto.c` | `visit_the_cinema`, `auto_pilot_sequence` |
| `src/cmpgn.c` | `ejection_sequence`, `stranded_sequence` |
| `src/screens.c` | `death_sequence`, training-simulator `ShowGetReadyScreen`, `ShowVictoryScreen`, `ShowGameOverScreen` |
| `src/brains.c` | `scramble`, `landing`, scramble actors, `init_mission`, pilot speech loading (`get_pilot_talk`, `init_personalities`) |
| `src/spc.c` | Camera views (`new_view`, `set_eye_direction_and_position`, `SetFleetOverviewView`), star field, joystick calibration |
| `src/mono.c` | `print_subtitle`, scaled intro text |
| `src/system.c` | `RunTrainSim` (training-simulator session) |
| `src/winmain.c`, `src/sdl/events.c`, `src/sdl/joystick.c` | Win32/SDL key handling (Alt keys, VK duplicates), gamepad flight extras |
| `src/sdl/gl_renderer.c`, `src/sdl/video.c` | SDL "enhanced" GL sprite renderer (the R2 precedent), VDU static noise |
| `src/globals.c`, `include/wcdata.h`, `include/wc1.h` | Cockpit layout tables, bars, lights, comm texts, colours, resource descriptor tables |

---

## 0. Conventions

* **Addresses** are Kilrathi Saga Win32 function starts as in the reference (`/* Function start: */`).
* **Logical files (LF)** are code ids (INSTALL.DAT id − 1) as in `resources.md` and
  `gameflow-screens.md` §7. The cockpit file is `cCockpitLogicalFile = 17 + cCockpitView`
  (PCSHIP.V00..V04); `cCockpitView` is 0 Hornet, 1 Rapier, 2 Scimitar, 3 Raptor, 4 training
  simulator (`InitializeCockpitResources(series == 0 ? 4 : playerShipType)`).
* **Tick** = one iteration of the flight loop = one `Update_3Space` (nominally 20 Hz).
  **Rendered frame** = a tick on which `Draw_3Space_Frame` returned 1 (every tick while
  `nFrameSkip == 1`, the default).
* **Coordinates.** The screen (`stScreen`) is 320x200. The space view is drawn into
  `stSpaceBuffer`, a separate surface whose rectangle is (0,0)-(W-1,H-1) in its own coordinates;
  the view geometry origin (or the cockpitless/full-screen rules, §3.4) maps it onto the screen.
  The VDU, bar, pilot-hand and readout viewports are aliases of `stScreen` with a smaller rectangle.
* **Shapes** `pXxxShape` are packet sections with RLE frames (graphics.md §2.2); "frame n of
  LF:s" means frame n of section s of logical file LF.
* **Colours** (`globals.c` 0x46999C..): `cBlackColour` 0x00, `cViewportClearColour` 0x0F (white),
  `cBlueColour` 0x25, `cDarkBlueColour` 0x27, `cYellowColour` 0x47, `cRedColour` 0x50,
  `cOrangeColour` 0x85, `cPrimaryTextColour` 0xA6, `cDefaultTextColour` 0xA8, `cDarkGreenColour`
  0xAA, `cMagentaColour` 0xB6, `cPrimaryViewBufferColour` **0xBF** (space background),
  `cAsteroidColour` 0xF5, `cBrownColour` 0xFD, `cDarkGreyColour` 0x07, `cLightGreyColour` 0x0B.
* **Random draws** are the shared MSVC LCG (`CRandom`); `malf(c)` = `RandomInRange(0,15) <
  damage[c]²` always draws one number (simulation.md §1.4, §4.3).
* "SDL" means the reference's SDL2 port (`#ifdef SDL_PORT`), "KS" the Win32 Kilrathi Saga code.

---

## 1. The flight loop

### 1.1 From `GameFlow` to the first frame

`GameFlow` (0x40F4B0, nav.c) runs, after the briefing: `PlayScrambleHangarScene` →
`stCampaignState.playerShipType = aMissionShips[nPlayerMissionShipIndex].type` → `scramble()`
(§5.2) → `init_mission(series, mission)` → `LaunchPlayerShip()` (§5.3) → `RunSpaceFlight(-1)`.
`bKeyEventQueueEnabled` is 1 from the briefing on (VK duplicates are queued, §2.1).

`init_mission` (0x40B730, brains.c): `LoadMissionData`; `init_3Space_objects(series)` (0x424A80,
logic.c: `cScreenViewportMode = -1`, all 64 slots removed, `nExternalViewShip = -1`,
`nRenderedSpaceFrame = nSpaceFrame = 0`, `bScriptedView = 0`, `bMissileCameraEnabled = 0`,
`nClosestVisibleObject = nPlayerCollisionObject = -1`, resource slots cleared,
`init_constellation(series)`, `load_common_3Space_objects`); `LoadPacketResourceList(
aMissionResourceDescriptors)` (LF 3 sections 14, 2, 3, 5); wing debris aliased to the metal
sheet; `prepare_mission`; `InitializeCockpitResources(series == 0 ? 4 : playerShipType)` (§3.1).
The developer switch `-l` (with `Origin`) runs `init_mission(series, mission)`, `LaunchPlayerShip`
(only with a cockpit), `RunSpaceFlight(nStartNavPointOverride)` and exits (`GameMain`, main.c).

### 1.2 `RunSpaceFlight(entryNavPoint)` (0x42A190, hudmsg.c) — entry

In this order:

1. `bCockpitlessView = (nTrainSimActive == 0 && bCockpitEnabled == 0)` (`bCockpitEnabled` is
   cleared by the WINGCMDR.CFG/command-line token `c`); `nFrameSkipCounter = 1`; `bInputMode = 1`.
2. SDL: `SdlSetMouseGrab(1)`. Event-manager pump = `get_player_input` (§2.4).
3. Save `stMouseCursorState.viewport`, set it to `&stSpaceBuffer` (mouse events are clamped to
   the space buffer rectangle from now on, §2.3).
4. `init_inflight_music()` (music director: combat music off, in-flight on, initial music pending).
5. `entryNavPoint == -1` → the player's mission record nav point; `set_up_action_sphere(entry)`
   (simulation; destroys/spawns the nav sphere's ships, `clean_up_cockpit`).
6. Cockpitless start only: the space buffer is re-allocated at (0,0)-(nScreenWidth-1,
   nScreenHeight-1), `new_view(0,0)`, re-allocated at 320x200, `initialize_cockpit(
   cScreenViewportMode++)` (the post-increment forces a full re-initialisation, §3.4),
   `SetMousePosition((right-left)/2+1, nViewCenterY)`, mouse flags cleared, buffer allocated,
   `FlushInputEvents`.
7. `copy_frame(0, 62)` (saved player frame); `WarpMouseTo((left+right)/2, (top+bottom)/2)` of the
   space buffer; `FlushInputEvents`; `bMouseAfterburnerControl = bMouseCursorVisible = 0`;
   `nArcadeState = 0`.
8. **Present** (`DIBslam(); DIBslamReal()`, still paced by the cinematic 62 ms interval), then
   `SetSpaceFlightFrameTiming()` (interval `1000/fSpaceFlightFrameRate` = 50 ms, deadline reset to
   0 so the next present is immediate).
9. `FlushInputEvents`, `ClearDebugPauseFlags`, `bMouseCursorVisible = 0`,
   `bPointerMovedByKeyboard = 1` (the host drops the warp echo), `frameReady = 1`.

In normal flight the cockpit picture was already set up by `LaunchPlayerShip` (`force_view(0,0)`,
§5.3); in the training simulator by `ShowGetReadyScreen` (§5.10).

### 1.3 The per-tick loop

```
while (nArcadeState == 0):
    if HandleSpaceFlightControls() == -1:          // §2: input, flight dynamics, key actions (may run modal UI)
        nArcadeState = 5; break
    if nArcadeState == 0:                          // a key may have ejected (2)
        Update_3Space()                            // simulation tick (main.c 0x427C50):
            house_keep()                           //   nav sphere every 32 ticks, hazards every 16; palette fades 185..190 (view 0) / damage alarm release (dead in KS, §3.7)
            house_keep_objects()                   //   lifetimes, deaths, warps, landing check (nArcadeState = 1)
            update_objects_in_space()              //   animate, collide, rotate, think, steer, move, shields, fuel
            set_eye_direction_and_position()       //   camera (may call new_view -> initialize_cockpit, §3.4)
            servicetrack()                         //   music director (consumes randoms)
            nSpaceFrame++
        frameReady = RenderSpaceViewFrame()        // §1.4: draw the space view, HUD, dump to screen
        update_cockpit()                           // §3.7: targeting, repairs, objectives, cockpit instruments, comm chatter, stranded check
    if frameReady:
        frameReady = 0; DIBslam(); DIBslamReal()   // present + ThrottleFrameAndDrawFps (50 ms deadline) + ServiceSoundSystem
    (profiling counters liFlight* — not ported)
```

Notes:

* `HandleSpaceFlightControls` runs `player_input` (which pumps the host via `PollInputEvent`),
  `players_flight_dynamics` (stick input → player rotation rates) and the key dispatch (§2.5).
  Several keys run **modal sub-loops** inside it that present on their own: pause
  (`WaitForKeyAcknowledge`), version banner, joystick calibration, the nav map (`InflightComputer`,
  §3.12) and the autopilot cinematic (§5.6). The simulation does not tick while they run (the
  autopilot cinematic runs its own ticks).
* Mouse buttons and the event drain may call `fire_players_lasers` several times in one tick;
  only the first can fire (refire counter), see §2.3.
* There is no separate HUD/cockpit present: everything drawn in a tick becomes visible with the
  single present at its end. Exception: `initialize_cockpit(4)` presents immediately when it draws
  the letterbox backdrop (§3.4) — e.g. inside `new_view` called from a key or from the camera code.

### 1.4 `RenderSpaceViewFrame` (0x429FC0) and `Draw_3Space_Frame` (0x429DD0)

```
RenderSpaceViewFrame():
    if Draw_3Space_Frame() == 0: return 0        // skipped frame (nFrameSkip > 1)
    check_message()                              // HUD message timer, counts rendered frames (§3.6)
    UpdateArcadeScoreDisplay()                   // training simulator only (§5.10): panel text, score++, time--, time out -> nArcadeState = 4
    RestoreCockpitExplosionIfVisible()           // §3.10 (dead in practice)
    dump_buffer_to_screen()                      // space buffer -> screen through the view geometry (§4.4)
    if nCameraViewMode == 0: RestoreTransientCockpitGraphics()   // erase HUD boxes/text/crosshair in the buffer, cockpit explosion
    if bCockpitlessView == 0 && nTrainSimActive:                 // training simulator wave bonus (§5.10)
        DrawFilledViewportRect(&stSpaceBuffer, 10, 10, right, 0x11, 0xBF)
        if nArcadeBonusCountdown != 0 and --nArcadeBonusCountdown == 0:
            if Vector_magnitude(aShipPosition[0]) > 0x271000 (10000 units): zero_vector(&aShipPosition[0])
            nArcadeScore += nArcadeWaveBonus
            nCurrentWave == -1 ? nArcadeState = 1 : nArcadeWave++
            ClearViewport(&stSpaceBuffer, 0xBF)
    ClearViewport(&stSpaceBuffer, 0xBF)          // the buffer starts each frame at the background colour
    return 1

Draw_3Space_Frame():
    UpdateSpacePaletteFade()                     // EVERY tick: entry 0xBF red flash fades by 4 (§3.9)
    if --nFrameSkipCounter > 0: return 0
    nFrameSkipCounter = nFrameSkip
    nRenderedSpaceFrame++
    transform_objects_to_your_view()             // simulation (first statement: draw_nav_pointer, §3.5)
    update_star_field()                          // simulation (randoms; hazards)
    place_exhaust_on_ships()                     // simulation (randoms, render-rate dependent)
    reposition_fixed_child_objects()             // simulation
    sort_object_depth()                          // simulation (§4.2)
    [SDL] SdlBeginSpaceFrame(geometry, cScreenViewportMode, bCockpitlessView > 0, 0xBF)
    draw_sorted_objects_to_buffer()              // UI: CPU sprites (§4.3)
    if nCameraViewMode == 0: overlay_head_up_display()   // target_locking (simulation, randoms) then the HUD drawing (§3.5)
    return 1
```

The simulation's `PrepareSpaceView()` already implements the simulation half (frame skip counter,
`RenderedSpaceFrame`, projection, stars, exhaust, children, sort, and `TargetLocking` in view 0);
the UI calls `UpdateSpacePaletteFade` before it and draws afterwards (§6).

`RefreshCockpitStatus` (0x42A0C0) is the variant the scripted sequences use instead of the
`Update_3Space` + `RenderSpaceViewFrame` pair: `Update_3Space(); if (nFrameSkipCounter <= 1)
clear_view_buffer(); return Draw_3Space_Frame();` — it clears the buffer **before** drawing and
leaves the dump and the present to the caller (§5.1).

### 1.5 Presents, pacing and frame skip

* Each loop iteration presents at most once; `DIBslamReal` → `ThrottleFrameAndDrawFps` waits for
  the previous frame's deadline and sets `deadline = now + 50 ms` (KS `fSpaceFlightFrameRate`
  20.0, `nFrameIntervalMs = (long)(1000.0/20)`). The game is therefore paced to **20 presented
  frames per second, one simulation tick per presented frame**.
* **Frame skip** (`nFrameSkip` 1..5, Ctrl+`-` / Ctrl+`=`, message "%d FRAMES SKIPPED." with
  `nFrameSkip - 1`): on skipped ticks nothing is drawn or presented, so no throttle wait happens and
  the simulation runs `nFrameSkip` ticks per 50 ms: the whole game speeds up by that factor in the
  KS build. Everything keyed to `nRenderedSpaceFrame` (target VDU refresh `% 8`, cockpit light
  `% 4`, lock-mode malfunction `% 8`, comm portrait `% 2`, ship sparks, hazard slots, the HUD
  message timer, training-simulator time) runs at the presented rate.
* `ReportSpaceFlightMaxFps(±0.5)` (dib.c 0x432050, Win32 Alt+N / Alt+M only) changes
  `fSpaceFlightFrameRate` within 8..32 fps and shows "Space Flight Max FPS : %.1f" (colour 0x50,
  20 frames). This changes the simulation rate as well (one tick per present).
* `UpdateSpacePaletteFade` and `house_keep`'s cockpit-light fades run per tick, not per rendered
  frame (they are outside the frame-skip test).

### 1.6 Exit

After the loop (in order): SDL `SdlSetMouseGrab(0)`, `SdlCancelSpaceFrame()` (drops recorded GL
sprites, stops rumble), DOS data `SdlStopDosSoundEffects()`; `SetCinematicFrameTiming()` (62 ms);
space buffer rectangle (0,0)-(nScreenWidth-1, nScreenHeight-1); `bCockpitlessView = 0`; if
landed (`nArcadeState == 1`): `flag_objective(find_objective(1, -1), 2)` (home-base objective
achieved); `ResetCockpitPaletteEntries()`; restore the cursor viewport; `free_inflight_music()`;
pump 0; `bMouseCursorVisible = 0`; `QueueInputEvent(13, 160, 100, 0, 0, 0, 0)`;
`SetMouseCursorShape(shape, 0)`. Returns `nArcadeState`.

| `nArcadeState` | Set by | `GameFlow` reaction |
| --- | --- | --- |
| 0 | flying | — |
| 1 | `house_keep_objects`: Tiger's Claw landing check (`nPlayerCollisionObject` = the carrier); training simulator: wave bonus of the last wave (§5.10) | `free_cockpit`, `ShowCarrierLaunchSequence(nPlayerCollisionObject)` (the landing approach, §5.4), `nArcadeState = 0`, `nPlayerCollisionObject = -1`, `free_3Space`, `landing(calculate_damage_level())` (§5.5) |
| 2 | Ctrl+E (§2.5) | `ejection_sequence` (§5.7), `check_stranded`, stranded → `stranded_sequence` and back to the title; else ejection bookkeeping (promotion −1, `elapsedDate.year++`, Golden Sun on the first ejection, office visit) |
| 3 | `check_stranded` (end of `update_cockpit`): carrier record state 3 and no enemy within 30000 | `stranded_sequence` (§5.8), `free_3Space`, title |
| 4 | `explode` of the player (simulation); training-simulator time out (§5.10) | `death_sequence` (§5.9), `free_3Space`, `funeral_sequence(1)`, campaign over |
| 5 | `HandleSpaceFlightControls` returned −1: **Esc in the training simulator only** | campaign: `free_cockpit`, `free_all_slots`, `free_3Space`, title (unreachable in a campaign: Esc never returns −1 there); `RunTrainSim` treats every result ≠ 1 as game over |

### 1.7 Mode variables

| Variable | Values |
| --- | --- |
| `nCameraViewMode` (spc.c) | 0 cockpit front, **1 right** (eye forward = player right), **2 left** (eye forward = −player right), 3 rear, 4 chase `cViewObject` (700/500 units, roll matched), 5 fly-by (scripts only), 6 missile camera, 7 target view, 8 capital-ship chase (2000 units, view-8 keys §2.6), 9 death view (rear, full screen), 10 ride object (ejected pilot, pod interior), 11 follow camera, 12 autopilot fly-by, 13 stranded/landing pull-away, 14 fleet overview ("battle view"), 15 scripted. Only 0 draws the HUD and runs the cockpit block of `update_cockpit`. −1 forces the next `new_view` (`force_view`). Full table of eye placements: §3.4. |
| `cScreenViewportMode` | the **view geometry index** last selected by `set_up_screen_viewport`: 0..3 cockpit views (PCSHIP section 6), 4 built-in 320x128 at (0,24) (cinematic letterbox), 5 built-in 320x200 at (0,0); −1 after `init_3Space_objects`/`InflightComputer` (forces re-initialisation) |
| `bCockpitlessView` | 0 cockpit art; 1 cockpitless (320x200 space buffer copied whole, instruments drawn over it); −2 temporarily while the autopilot runs in cockpitless mode (treated as "cockpit" by `set_up_screen_viewport`, no art drawn) |
| `nCannedSceneMode` | 0 normal, 1 cinematic (launch, death, stranded, get-ready), 2 canned-sequence AI (attract mode), 4 autopilot travel |
| `bScriptedView` | the eye follows `pViewScript` (`update_scripted_view` at the start of `set_eye_direction_and_position`) |
| `bIntroSceneResourcesActive` | 1 normally; 0 during the attract mode and `ShowCarrierLaunchSequence`: `initialize_cockpit(4)` then draws no letterbox backdrop and does not present |
| `bInflightComputerActive` | 1 while the nav map runs (blocks `SetHudMessageText`) |
| `bMouseCursorVisible` | 1 = mouse steering (crosshair drawn in the space buffer, keyboard steering polling off) |

---

## 2. Controls

### 2.1 Input pipeline of one tick (`player_input`, 0x4285D0, main.c)

`HandleSpaceFlightControls` (0x429160) starts with `player_input()` and
`players_flight_dynamics()`, then computes `notRepeated = (signed char)bCurrentKey !=
cPreviousKey`, `control = GetControlKeyState()`, calls `GetKeyboardModifiers()` (result unused)
and `HandleFleetOverviewInput()` (§2.6), then dispatches on `bCurrentKey` (§2.5). All controls
therefore act on **one key code per tick** (`bCurrentKey`) plus continuous inputs.

```
player_input():
    cPreviousKey = (signed char)bCurrentKey; save nYaw/nPitch/nRollInput; keyboardRoll = 0
    type = PollInputEvent(&event)                 // pumps the host (PumpWindowMessages: pump get_player_input,
                                                  //   host events, nTickCount60Hz) and POPS the head event
    modifiers = event.modifiers; wCurrentInputModifiers = modifiers
    TranslatePolledInputEvent(type, event.value)  // re-queues a synthetic copy for types 2 (host mouse
                                                  //   position/buttons), 6 (joystick sample), 13 (host mouse
                                                  //   position). Key events (3/4/5) are NOT re-queued:
                                                  //   a key event at the head of the queue is lost (see below)
    flags: bJoystickEventQueued = queued(6), bMouseMoveEventQueued = queued(13),
           bKeyboardEventQueued = queued(5)|queued(3)|queued(4), bMouseButtonEventQueued = queued(2)
    bCurrentKey |= 0x80                           // "no new key": negative as signed char, matches no case
    if bMouseCursorVisible == 0:                  // keyboard steering active
        bCurrentKey = PollKeyboardState()         // held steering keys -> one scan code (host-input-timing §2.3), 0 = none
        if bCurrentKey == 0: nRoll = nPitch = nYawInput = 0; bFlightRollLatch = 0
        else: bMouseAfterburnerControl = 0; process_player_input()       // §2.2
              keyboardRoll = bCurrentKey in {0x33, 0x34, 0x52, 0x53}
    if bMouseButtonEventQueued == 0:              // buttons HELD (host state, no new event)
        buttons = bHostSecondaryMouseButton*2 | bHostPrimaryMouseButton
        if buttons == 0: bAfterburnerButtonLatched = 0
        else:
            if buttons == 3: bCurrentKey = 0x1C                   // both: release weapon
            elif buttons == 1: bCurrentKey = 0x39; fire_players_lasers()
            if (buttons & 2) == 0: cPreviousKey = 0
            if cPreviousKey == 0x0F && buttons == 2: bCurrentKey = 0x0F   // keep afterburning while right held
            if buttons == 1: fire_players_lasers()                // second call, cannot fire again
    while (type = GetNextInputEvent(&event)) != 0:              // drain the rest of the queue
        2 (button down, value = rmb*2|lmb):
            value 1 -> bCurrentKey = 0x39; if (event.modifiers & 4): afterburning ? fire_players_lasers() : bCurrentKey = 0x1C
            value 3 -> bCurrentKey = 0x1C
            value 2 && !bAfterburnerButtonLatched:
                if nTickCount60Hz - dwLastSecondaryButtonPress <= nInputTickScale (20 ticks = 333 ms): bCurrentKey = 0x0F
                bAfterburnerButtonLatched = 1
            cPreviousKey == 0x0F && value 2 -> bCurrentKey = 0x0F
            value 1 -> fire_players_lasers()
            dwLastSecondaryButtonPress = nTickCount60Hz           // for every button-down event
        3, 5 (key down): bMouseAfterburnerControl = 0; wCurrentInputModifiers = event.modifiers;
                         bCurrentKey = value; process_player_input()
        6 (joystick sample): §2.4
        13 (mouse move): §2.3
        1, 4 (button up, key up): ignored
    SDL: SdlApplyJoystickFlightControls()                         // gamepad extras (§2.8)
    stPreviousFlightInput = stLastPolledFlightInput
```

Consequences a port must keep (they follow automatically when `EventManager` is reused, as it
already is in `WingCommander.Game.Input`):

* **The first event of each tick is consumed by `PollInputEvent`.** With `bKeyEventQueueEnabled`
  (always 1 in campaign and training-simulator flight: `GameFlow` sets it before the briefing,
  `RunTrainSim` around `RunSpaceFlight`) every key press queues the VK duplicate *before* the
  scan-code event, so the dropped head event is normally the VK duplicate and the scan code
  survives. VK duplicates that are not at the head reach the drain loop as key codes: VK 'A'..'Z'
  = 0x41..0x5A collide with scan codes (0x41 = F7, 0x4B = Left, ...) but are immediately
  overwritten by the following scan-code event of the same press, because only the **last**
  `bCurrentKey` of the tick is dispatched. (`process_player_input` does run for every drained key
  event, so a VK duplicate equal to a steering scan code steers once.)
* Continuous keyboard fire, throttle and steering depend on the host's **key auto-repeat**: a held
  Space fires only on ticks that receive a repeat event (the polled steering keys are the
  exception). The C# host queues repeats (`HostInputEvent.Repeat`), as SDL did.
* Mouse buttons held without new events act every tick (left = guns, both = release weapon).
* Only the **last** key-type code of the tick reaches `HandleSpaceFlightControls`.

`players_flight_dynamics` (0x4284D0) converts the inputs into the player's rotation rates
(simulation API `PlayersFlightDynamics(pitch, yaw, roll)`): `pitchRate = type.yawRate * nPitchInput
/ 8`, `yawRate = -(type.pitchRate * nYawInput / 8)`, `rollRate = -(type.rollRate * nRollInput / 8)`;
while tumbling (`BLOWING_UP`) the input only counteracts the spin (simulation.md §2.2).

### 2.2 Keyboard steering (`process_player_input`, 0x427F20)

`shift = GetShiftKeyState()`, `control = GetControlKeyState()`. Diagonals expand to two keys:
Home 0x47 → {0x48, 0x4B}, PgUp 0x49 → {0x48, 0x4D}, End 0x4F → {0x50, 0x4B}, PgDn 0x51 → {0x50,
0x4D}; any other code is processed alone. Inputs range −10..10 (normally ±9).

| Scan | Key (DOS set 1; arrows and keypad share codes) | Effect |
| --- | --- | --- |
| 0x33, 0x52 | `,` / Ins / KP0 | roll left: positive roll → 0; else (Shift → −9); > −9 → −1 per call; at −9: `cPreviousKey < 0` → −10, else +1 (so a held key oscillates −9/−8) |
| 0x34, 0x53 | `.` / Del / KP. | roll right (mirror image) |
| 0x48 | Up / KP8 | pitch: negative → 0; Ctrl → **SFX volume +1** (max 20, saved, "SFX VOLUME: %d.") ; else (Shift → 9); below 9 or `cPreviousKey < 0` → +1, else −1 |
| 0x50 | Down / KP2 | pitch: positive → 0; Ctrl → **SFX volume −1**; else mirror of Up |
| 0x4B | Left / KP4 | Ctrl → **music volume −1** and **return** (the second key of a diagonal is skipped); else yaw: positive → 0, Shift → −9, ramp as roll |
| 0x4D | Right / KP6 | Ctrl → **music volume +1** and return; else yaw right |
| 0x4C | KP5 | `WarpMouseTo(space buffer centre)`, roll = pitch = yaw = 0, `init_player_input()` |
| other | — | ignored (`handled--`; the return value is unused) |

Every steering key sets `bMouseCursorVisible = 0` (back to keyboard steering). The Ctrl+direction
volume keys are **dead** in the SDL port and in the C# `EventManager`: `GetControlKeyState`
reports Ctrl released while any direction key is held (host-input-timing §2.3), so Ctrl+arrows
steer. Volume control remains on Ctrl+S / Ctrl+M (toggles, §2.5).

`init_player_input` (0x427DF0): `SetMousePosition((right-left)/2 + 1, nViewCenterY)` of the space
buffer, `ClearDebugPauseFlags`, `bMouseCursorVisible = 0`, `bPointerMovedByKeyboard = 1`.

### 2.3 Mouse flight (event type 13 in `player_input`)

* The event position was clamped by `GetNextInputEvent` to `stMouseCursorState.viewport` =
  `stSpaceBuffer`, i.e. to (0,0)-(W-1,H-1) **in buffer coordinates without the geometry origin**:
  the logical mouse y is used as buffer y, so in the Hornet front view (320x105 at (0,10)) the
  pointer rows 105..199 all clamp to 104. The crosshair is drawn at buffer (x, y) = screen
  (x, y + originY).
* `afterburnerControl = (modifiers & 4) != 0` uses `modifiers` of the **first** event polled this
  tick (not of the move event); it is cleared while the afterburner burns.
* First move: `bMouseCursorVisible = 1`, `stMouseCursorState.frame = 2` (ARROW.VGA flight
  crosshair).
* Offsets: cockpit view `h = x + (left-right)/2 + 1`, `v = y + (top-bottom)/2` (C truncation:
  W = 320 gives `h = x - 158`); cockpitless: `event.y -= 10` (cockpit 0) / `25` (cockpit 1) first,
  then `h` as above and `v = y - nViewCenterY`. `stMouseCursorState.x/y = event.x/y`.
* Buckets: `yaw = count of asMouseYawThresholds {10, 37, 52, 57, 62, 1070} <= |h|`, signed like
  `h`; `pitch = count of asMousePitchThresholds {5, 18, 27, 35, 38, 1040} <= |v|`, signed like
  `v`. Within 4 pixels of an edge: `x - 4 <= left` → yaw −8, `right <= x + 4` → +8, `y - 4 <= top`
  → pitch −8, `bottom <= y + 4` → +8. Clamp to ±8.
* Right button held (`afterburnerControl`): `bMouseAfterburnerControl = 1`, `pitch = -pitch`,
  `nRollInput = yaw`, `nMouseYawInput = yaw`, `nMousePitchInput = pitch`, `accelerate(pitch / 2)`
  (throttle; `malf(0)` random each call); `nYawInput`/`nPitchInput` keep their previous values.
  The first move event after the button is released: roll, yaw, pitch and the mouse inputs 0 and
  `WarpMouseTo(centre)` (yaw/pitch stay 0 until the next move event).
  Otherwise: `nRollInput = 0`, yaw/pitch = buckets.
* Buttons: left = guns (0x39), both = release weapon (0x1C), right double click within 20 ticks of
  the previous button-down = afterburner (0x0F), right held after that = afterburner kept (0x0F).

### 2.4 Joystick flight

* Pump `get_player_input` (0x427E40), installed by `RunSpaceFlight`: when a device is active and
  not re-entered (`bInputPollingGuard`), `UpdateInputDeviceTransitions(0)` (calibrated −9..9 read,
  button edges, double clicks), then queue a type-6 event if the sample is non-zero, or all-zero
  but different from the last queued one.
* Type 6 in `player_input`: `bMouseAfterburnerControl = bMouseCursorVisible = 0`; buttons 1+2 →
  afterburning ? guns : 0x1C; button 1 → `fire_players_lasers()`; button 2 held and not
  afterburning → `nRollInput = x`, `accelerate(-(y / 2))`; else: a pending roll is cleared
  (`stPreviousFlightInput.x = -1`) unless `bFlightRollLatch` or keyboard roll, and if the sample
  changed or is non-zero `nPitchInput = -y`, `nYawInput = x`. Button-2 double click → 0x0F;
  0x0F kept while button 2 is held.
* Ctrl+J: `CalibrateJoystickInteractive` (spc.c 0x4102B0; a series of modal prompts, each waiting for a button, writes `j.cal`).
* The Game's joystick layer (`UpdateInputDeviceTransitions`, calibration, `j.cal`) is not ported
  yet (docs/progress/game.md); the flight controls only need the calibrated sample, the button
  state and the double-click flag.

### 2.5 Key bindings (`HandleSpaceFlightControls`, 0x429160)

Two switches on `(signed char)bCurrentKey`. The **first** runs only outside the training
simulator (`nTrainSimActive == 0`); after it (and after the "restore normal viewport" tail of the
view keys) the **second** always runs, so a code present in both runs both (Ctrl+S). "nr" =
`notRepeated` (code differs from the previous tick's). Ctrl = `GetControlKeyState()` sampled now,
except for eject, which also accepts the queued event's Ctrl bit 0x2000. Random draws: §6.4.

First switch (campaign flight only):

| Scan | Key | Condition | Action |
| --- | --- | --- | --- |
| 0x02..0x0A | 1..9 | nr, right VDU mode 4, view 0, `key - 2 <= nCommMenuChoiceCount` | `Chosen_communicate_option(key - 2)` (§3.11). Off by one: `key - 2 == count` is accepted and reads the cleared command byte (−1) |
| 0x12 | Ctrl+E | nr, Ctrl (live or 0x2000), ejector not destroyed (`acPlayerComponentDamage[7] != 4`) | `RandomInRange(0, damage[7]) == 0` → `nArcadeState = 2`, else `malf_sound` (sfx 0x1F). Intact ejector: 50 % (RandomInRange(0,0) returns 0 or 1); simulation API `TryEject()` |
| 0x1E | A | nr | autopilot: `bMouseCursorVisible = 0`; right VDU to navigation unless already (`SelectCockpitVduMode(1, 5)`, randoms); `auto_pilot_sequence()` (§5.6; cockpitless: buffer resized to the geometry, `bCockpitlessView = -2` around the call, back to 320x200 and 1); `FlushInputEvents`, `ClearDebugPauseFlags`, `bPointerMovedByKeyboard = 1` |
| 0x1F | Ctrl+S | nr | flight sound effects on/off (`ResetSoundStateForScene` if enabled else `ResetSoundStateForFlight`); the second switch then also toggles the SFX volume |
| 0x23 | H | nr, wingman alive and not holding formation | `request(0, wingman, 9)` "Form on my wing." |
| 0x2E | C | nr | message showing → `EndCommMenu()`; else comm menu open (right VDU mode 4) → `CloseCommChoiceMenu()`; else `SelectCockpitVduMode(1, 4)` |
| 0x2F | V | nr, no Ctrl | `bVideoImagesSuppressed ^= 1`; HUD "VIDEO IMAGES SUPRESSED" (sic, red, 20) / "VIDEO IMAGES ENABLED" (0xA6, 20). Changes later random draws (`vid_transmit`, §3.11) |
| 0x30 | B | nr, wingman in formation, enemy within 14000 | `request(0, wingman, 7)` "Break and attack." |
| 0x31 | N | — | nr → `SelectCockpitVduMode(1, 5)` (when already 5: the nav map, §3.12); **always** `init_player_input()` |
| 0x32 | M | nr, no Ctrl | message speed `(speed + 1) % 5`, "MESSAGES SPEED IS NOW %d." (speed + 1) |
| 0x3B | F1 | F1 latch (first press, not a repeat) | geometry 0 active (`cScreenViewportMode == 0`): toggle cockpitless (buffer 320x200 or geometry size, `initialize_cockpit(cScreenViewportMode++)`, mouse to the new centre); otherwise `new_view(0,0)` (back to the cockpit). Then flush, `bPointerMovedByKeyboard = 1` |
| 0x3C | F2 | — | `new_view(2, 0)` **left** view |
| 0x3D | F3 | — | `new_view(1, 0)` **right** view |
| 0x3E | F4 | — | `new_view(3, 0)` rear view |
| 0x3F | F5 | — | `new_view(4, 0)` chase camera on the player; again: toggles near (500) / far (700) |
| 0x40 | F6 | — | `new_view(14, 0)` fleet overview ("battle view", full screen) |
| 0x41 | F7 | a target exists | `new_view(7, 0)` target view |
| 0x42 | F8 | nr | missile camera on/off; HUD "MISSILE CAMERA ON" (red, 20) / "MISSILE CAMERA OFF" (0xA6, 20) |
| 0x43 | F9 | nr | `SelectNextExternalViewObject()` (next slot 0..9 with class ≥ SHIP, wrapping) and `force_view(4, cViewObject)` (a capital ship becomes view 8) |

F2..F7 and F9 set `bMouseCursorVisible = 0`; in cockpitless mode they wrap the call in the buffer
resize dance (buffer at geometry size around `new_view`, then back to 320x200 via the shared
`restore_normal_viewport` tail).

Second switch (always):

| Scan | Key | Condition | Action |
| --- | --- | --- | --- |
| 0x01 | Esc | — | `bEscapePressed = 0`; training simulator → **return −1** (flight ends, state 5); comm menu open → close it; KS: nothing else; **SDL: pause** (`ShowGamePausedBanner(1)`, `SetFrameTimerPeriodDirect(1)`) |
| 0x0C, 0x4A | `-`, KP− | — | Ctrl → `ReportFramesSkipped(-1)`; else `accelerate(-1)` (wheel down queues 0x0C) |
| 0x0D, 0x4E | `=`, KP+ | — | Ctrl → `ReportFramesSkipped(+1)`; else `accelerate(+1)` (wheel up queues 0x0D) |
| 0x0E | Backspace | — | `anShipSpeed[0] = 0` |
| 0x0F, 0x37 | Tab, KP* | — | `your_afterburner()` |
| 0x11 | W | nr | `SelectCockpitVduMode(0, 1)`; already weapons → next release weapon |
| 0x14 | T | nr | `SelectCockpitVduMode(1, 3)`; already target → `cycle_onscreen_targets` |
| 0x19 | P | — | `ShowGamePausedBanner(!Ctrl)`: P → modal "GAME PAUSED" panel + `WaitForKeyAcknowledge(1)`; Ctrl+P → wait without a panel. Then `SetFrameTimerPeriodDirect(1)` |
| 0x1C | Enter | nr, a release weapon selected | mine selected → `drop_player_mine(0)`; else if no missile is tracked (`nExternalViewShip == -1`): `nExternalViewShip = fire_missile(0)`; missile camera on and launched → `new_view(6, missile)`. Simulation API `PlayerReleaseWeapon()` |
| 0x1F | Ctrl+S | nr | `nSfxVolumeSetting` 20 ↔ 0, `SetSoundEffectsVolume(anVolumeLevels[v/2])`, "SFX VOLUME: %d." |
| 0x20 | D | nr | `SelectCockpitVduMode(0, 2)`; already damage → `nDamageDisplayTicks = 0` (next component now) |
| 0x22 | G | nr | `SelectCockpitVduMode(0, 1)`; already weapons → `select_new_gun` (cycles gun types, then "Full Guns") |
| 0x24 | Ctrl+J | — | `CalibrateJoystickInteractive()` |
| 0x26 | L | — | repeated → `init_player_input()`; else toggle `nTargetLockMode` (keep target), sfx 0x19, target VDU showing → `InvalidateVduMode(1)` |
| 0x2B | `\` | — | `accelerate(9000)` (full throttle; clamped to max) |
| 0x2F | Ctrl+V | nr | `ShowVersionBanner`: modal "WING COMMANDER VER. 1.03F-95" + wait |
| 0x32 | Ctrl+M | nr | `nMusicVolumeSetting` 20 ↔ 0, `SetMusicStreamVolume`, "MUSIC VOLUME: %d." |
| 0x39 | Space | — | `fire_players_lasers()` (refire counter −1 and energy > 0; with a target while the right VDU shows navigation it switches to the target display, randoms) |

The return value is 0 except Esc in the training simulator (−1). Key codes not listed are ignored.
There is **no in-flight debug key** in the KS code: `nOriginDevUnlock` (argument `Origin`, the
registry `Cheater` flag, or temporarily inside `visit_the_cinema`) only enables the start-up
switches `-b` (no collision response), `-k` (invulnerable), `-f` (fps text), `-q`, `-l`/`-m`/`-s`/
`-w` (`GameMain`); `As<n>` sets the start nav point.

### 2.6 View-8 camera keys (`HandleFleetOverviewInput`, 0x428D10)

Despite its name it acts only in camera view **8** (capital-ship chase, reached through F5/F9 on a
capital ship). It runs after `player_input`, so steering keys still steer the ship as well.

| Key | Action |
| --- | --- |
| Enter 0x1C | `cViewObject--` (no range or class check; may select an empty slot) and `bCurrentKey = 0x29` (no handler: suppresses the missile) |
| Home 0x47 / End 0x4F | `nCapitalShipViewDistance` −/+ 0x3200 (50 units) |
| Up 0x48 / Down 0x50 | eye `rotate_about_i(−7 / +7)` |
| Left 0x4B / Right 0x4D | eye `rotate_about_j(+7 / −7)` |
| Ins 0x52 | eye basis reset to right (1,0,0), up (0,0,−1), forward (0,1,0) |

Handled keys set `bCurrentKey = 0`; others are restored.

### 2.7 Messages, pause and modal waits

* `ShowOnScreenMessage(flags, duration, format, ...)` (0x428FA0): formats ≤ 51 characters,
  `FlushInputEvents()`. Duration 9999 = modal: `ShowModalTextPanel(font 1, text)` (centred panel,
  presents; implemented by the Rooms work stream's `Screens/Ui`); if the panel cannot be shown the text
  goes to the HUD line and `dump_buffer_to_screen` (no present). Then `WaitForKeyAcknowledge(flags
  ? 1 : 0)` and `ReleaseModalTextPanel` (presents). Duration 0 = `MeasureMessageWidth(text)`
  rendered frames on the HUD line (§3.6), preceded by `SetHudTextColour(1)` (ends a comm message).
* `WaitForKeyAcknowledge(1)` (0x428EA0): SDL suspends the mouse grab, pumps until a key-up event
  is queued, flushes, pumps until a key-down is queued, flushes, resumes the grab. No presents, no
  simulation ticks. (Already ported: `EventManager.WaitForKeyAcknowledgeAsync`.)
* Pause ends with `SetFrameTimerPeriodDirect(1)` (harmless; no flight code polls the timer).
* `MeasureMessageWidth(text)` (0x428E70) = `(min(5, strlen/2) + 5) * (bMessageSpeed + 1)`,
  `bMessageSpeed` default 2, i.e. 15..30 rendered frames.

### 2.8 Host-level keys, SDL extras and differences

| Input | KS (Win32) | SDL port | Port recommendation |
| --- | --- | --- | --- |
| Esc in campaign flight | inert | pause (banner) | SDL behaviour |
| Alt+X | `WM_SYSKEYDOWN 'X'` → `PostQuitMessage`: the game exits | nothing | host shortcut → `GameExitException` (same path as closing the window). It never produces arcade state 5 |
| Alt+N / Alt+M | flight frame rate −/+ 0.5 (8..32) | not wired | optional developer option only (changes the simulation rate) |
| Cmd+Q, Alt+Enter, window close | — | quit / fullscreen | host (already) |
| Mouse wheel | — | up → key 0x0D, down → 0x0C, release queued before press | already in `EventManager` |
| Gamepad (`SdlApplyJoystickFlightControls`, `SdlHandleJoystickButtonEvent`) | — | modes: original (2 buttons as joystick) or 4-button: X = afterburner, Y = target (4-axis), shoulders = gun/weapon VDU (0x22/0x11), triggers/sticks = nav (0x31) / autopilot (0x1E), B = release weapon (0x1C), D-pad = full throttle (0x2B) / stop (0x0E) / comm (0x2E) / lock (0x26), Start = pause (0x19), Back = Esc; comm menu: D-pad up/down highlights a choice (`SdlGetCommunicationMenuSelection`, yellow), right sends `selection + 2`, left = Esc; extra axes: roll, rudder, linear throttle (`celerate` to `max * position`); rumble | optional modern input layer; every action must be expressed as queued key events (release before press, like the SDL port) so the tick logic stays unchanged |

Training-simulator flight (`nTrainSimActive`): the whole first switch is skipped — no views
(F1..F9), nav map, comm, autopilot, eject, H/B orders, V, M; Esc ends the flight (state 5,
counted as game over by `RunTrainSim`).

---

## 3. Cockpit and HUD

### 3.1 Resources (verified against the GOG data)

| Global | LF:section | Frames | Use |
| --- | --- | --- | --- |
| `pTargetLockShape` | 8:0 (COCKPIT.VGA) | 4: 0 gunsight (green), 1 lock marker (red ring), 2 small white cross (scanner nav marker), 3 large white cross (nav pointer in space) | HUD, scanner, nav pointer |
| in-flight computer frame | 8:1 | 1 (320x200 frame, transparent centre) | nav map (§3.12) |
| `pNavMapShape` | 8:2 | 1 | nav map background |
| — | 8:3 | empty section | — |
| `pCockpitIndicatorShape` | 8:4 | 6: shield arcs, even = aft, odd = fore, green/yellow/red | target VDU |
| `pCockpitExplosionShape` | 8:5 | 8 | cockpit explosion (dead in practice, §3.10) |
| `pCinematicViewBackdrop` | 8:6 | 1: 320x200 "WING" / "COMMANDER" letterbox, transparent 320x128 window | `initialize_cockpit(4)` |
| `pRearViewBackdrop` (misnamed) | 8:7 | 1: escape-pod interior ("BEACON ACTIVE", "BATTERY LEVEL", "LIFE SUPPORT") | `initialize_cockpit(7)` = view 10 |
| view templates | 8:8 | raw `ScreenViewportPacket`, 1 geometry: 299x106 at (8,34) | ejection pod view (§5.7) |
| `apCockpitShapes[0..3]` | cockpit:0..3 | 1 each, 320x200 cockpit art front / right / left / rear (V04: only 0) | `initialize_cockpit(0..3)` |
| `pCockpitPilotShape` (misnamed) | cockpit:4 | 4 cockpit damage decals (V04: empty) | `explosion_draw` |
| eject canopy | cockpit:5 | 1 | `ejection_sequence` |
| `pScreenViewportPacket` | cockpit:6 | raw: `u16 bufferSize; i16 offset[(offset[0]-2)/2]`, geometries (graphics.md §2.6) | `set_up_screen_viewport` |
| `pCockpitDamageShape` (misnamed) | cockpit:7 | V00 22, V04 8: light on/off frames and bar frames | lights, bars |
| scramble cockpit | cockpit:8 | 2 (V04: empty) | `scramble`, `landing` |
| `pCockpitWeaponShape` | cockpit:9 | 30: 0 ship outline; 1..20 weapons `type*2 - 47 + disabled` (types 24..33); 21..29 damaged components | weapon and damage VDUs |
| `pPilotHandShape` | 2:3 (PILOTANM.VGA) | 18: 0..16 hand on the stick, 17 sleeve | pilot hand |
| death shape | 2:0 | 8 | `death_sequence` |
| ejection shape | 2:1 | 8 | `ejection_sequence` |
| ejected pilot set | 2:2 | 13 | object type 56 |
| `pConfedCommBackground` | 11:0 (WINGMEN.VGA) | 2 | comm video |
| `apCommPortraitShapes[0..7]` | 11:1..8 | 5 each | faces 0..7 (wingmen) |
| `pKilrathiCommBackground` | 11:9 | 2 | comm video |
| Kilrathi portrait | 11:10 | 5 | faces 8..11 (aces) and 13 (generic Kilrathi) |
| `pCommStaticShape` | 11:11 | **2** | dying speaker |
| pilot speech | LF 13 COMMUNIC.DAT (raw, 12320 bytes) | 14 faces × 11 lines × 80 bytes | `get_pilot_talk` |
| mouse cursor | 14:0 (ARROW.VGA) | 3: 0 arrow (hot spot 1,2), 1 menu cross 11x11, 2 flight crosshair 9x9 | mouse steering |
| `pConstellationShape` | 12:0 (PLANETS.VGA) | 38 (dust frames 0..31, stars 32..37) | stars, dust |
| planets | 12:n+1 | 1 each | constellation objects |
| `pIntroFont` | 9:1 (TITLE.VGA) | 60: 'A'..'z' = 0..57, '.' 58, ',' 59 | subtitles, scaled texts |
| `pTitleShape` | 9:0 | 3 (left, centre, right) | `DrawTitleLogo` |
| `pFireworkShape` | 9:17 | 24 | training-simulator victory |
| `pLaunchDoorShape` | 1:7 (SCRAMBLE.VGA) | 3 (left, centre, right) | launch |
| carrier | 1:8 | 4 | landing approach |
| scramble actors | 1:4 | 17 | landing approach, scramble |

Load/free timeline:

* `LoadSpaceflightResources` (0x421F50, logic.c) once at start-up (`LoadOriginFxDrivers`):
  `aCommon3SpaceResources` (LF 3 effects), `aMissionResourceDescriptors`, `aCockpitResourceDescriptors`
  (8:0, 2:3, 11:0, 11:11, 11:9, 8:5, 8:4, 8:6, 8:7, portraits 11:1..8 and 11:10), then clears the
  portrait pointers 8..11 and 13 (reloaded on demand by `LoadCommPortraitShape`).
* `InitializeCockpitResources(mode)` (0x4245B0) at `init_mission`: returns if the same cockpit is
  loaded, else `free_cockpit`; sets `cCockpitView`, the cockpit LF; `clear_cockpit_damage`,
  `ClearHudGunReadouts`, `reset_cockpit` (lights), `GetScreenUpdateFlag` (frees the space
  buffer); loads `aCockpitPrimaryResources` (cockpit 4, 7, 9, 0..3) and `pScreenViewportPacket`
  (cockpit 6); `stCockpitBar = stScreen`; `init_vdus` (§3.8); readout context (font 2, 0xA6 on
  black, viewport `stScreen`) with readout slots 4, 5, 2, 3 at the layout origins; VDU rectangles;
  pilot hand viewports (`stPilotHand` = screen alias, `stPilotHandComposite` and
  `stPilotHandBackdrop` = off-screen buffers of the hand rectangle's size); loads
  `aCockpitSecondaryResources` (cockpit 7, 9; 8:4; 8:0); allocates save buffers (scanner marker =
  frame 2 of 8:0; largest missile frame of cockpit 9; largest explosion frame; largest of cockpit
  9 frames 0..8); `ResetScannerContacts`; `init_personalities` (speech of every pilot in the
  mission, both generic faces); `nCockpitExplosionFrame = 8`; `bRadioSilence = 0`;
  `bCommVideoEnabled = 1` (memory configuration 2).
* `free_cockpit` (0x4249A0) releases all of it (`FreeCommDisplayResources` included).
* Fonts: font 1 = HUD messages and modal panels; font 2 = VDUs, readouts, nav map labels; font 0 =
  the landing comment.

### 3.2 Cockpit layout tables (`globals.c`)

Cockpit index = `cCockpitView` (0 Hornet, 1 Rapier, 2 Scimitar, 3 Raptor, 4 training simulator).
−99 disables an element. All coordinates are screen pixels.

`stCockpitLayout` (0x46E008):

| Cockpit | Left VDU | Right VDU | Scanner centre (box) | Pilot hand | Readout 4 fore shield | 5 aft shield | 2 set speed | 3 actual speed |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | (10,133)-(82,198) | (236,133)-(309,198) | (159,126) x145..173 y113..139 | (120,152)-(203,199) | (99,130) | (99,140) | (201,105) | (112,105) |
| 1 | (0,99)-(73,165) | (246,97)-(319,165) | (103,132) x89..117 y119..145 | (120,152)-(205,199) | (227,122) | (227,140) | (156,14) | (156,19) |
| 2 | (6,1)-(78,66) | (241,1)-(313,66) | (160,39) x146..174 y26..52 | (120,151)-(213,199) | (38,164) | (38,184) | (197,145) | (105,145) |
| 3 | (11,111)-(84,176) | (235,111)-(308,176) | (159,129) x145..173 y116..142 | (120,152)-(205,199) | (192,126) | (192,136) | (262,102) | (38,102) |
| 4 | (48,126)-(120,191) | (198,126)-(270,191) | (159,130) x145..173 y117..143 | (120,152)-(203,199) | −99 | −99 | (219,115) | (71,115) |

Other tables: `asPilotHandOrigins` = (154,187) for every cockpit; `asPilotHandOffsets[17]`
(sleeve offset per hand frame) = (6,−3) (7,2) (7,9) (7,12) (8,13) (0,−1) (−1,−1) (−4,−1) (−6,−1)
(6,0) (8,0) (10,0) (13,3) (8,−7) (6,−9) (5,−11) (5,−14); `asHudMessageOrigins` = (18,14), (71,5),
(80,29), (49,27), (14,13); `aWeaponDisplayOrigins` = (0,16) for every cockpit (relative to the left
VDU's top-left); `aWeaponDisplayPositions[32]` (per hardpoint, `globals.c` 0x468440);
`aDamageDisplayPositions[9]` = (36,37) (36,28) (36,30) (36,23) (36,19) (36,15) (36,24) (36,16)
(36,22); `abDamageDisplayFrames` = 21..29; `aTargetArmorClipRects` = {12,−20,29,20},
{−11,1,11,20}, {−11,−20,11,0}, {−29,−20,−12,20} (armour index 0..3, relative to the silhouette's
hot spot).

Cockpit lights `aasCockpitLightX/Y`, `aacCockpitLightOff/OnFrame` (frames of cockpit section 7):

| Cockpit | x (lights 0..6) | y | off frames | on frames |
| --- | --- | --- | --- | --- |
| 0 | 101 101 189 137 265 −99 203 | 122 145 121 7 121 −99 147 | 14 14 14 11 13 14 14 | 3 3 3 0 2 3 3 |
| 1 | 198 198 133 145 234 234 −99 | 120 139 114 114 179 188 −99 | 12 12 12 13 12 12 12 | 2 2 2 3 2 2 2 |
| 2 | 76 76 139 257 20 20 −99 | 164 183 22 181 162 178 −99 | 9 9 9 14 9 9 9 | 1 1 1 6 1 1 1 |
| 3 | 194 194 96 137 96 96 −99 | 119 142 136 15 126 116 −99 | 15 15 13 11 13 13 13 | 4 4 2 0 2 2 2 |
| 4 | −99 −99 179 133 −99 −99 −99 | −99 −99 115 114 −99 −99 −99 | 5 ×7 | 1 ×7 |

Light meanings: 0 fore shield below 20 %, 1 aft shield below 20 %, 2 missile on tail, 3 damage
alarm, 4 autopilot available, 5 never set, 6 fuel below 20 %. A light at −99 is drawn off screen.

Bars `aaCockpitBars[cockpit][bar]` = {direction, left, top, right, bottom, length, frameA, frameB};
bar 0 fuel, 1 gun energy, 2 armour front, 3 armour rear, 4 armour index 2, 5 armour index 3,
6 fore shield, 7 aft shield:

| Cockpit | Bars 0..7 |
| --- | --- |
| 0 | {0,215,122,219,152,31,16,5} {2,143,97,175,99,33,12,1} {0,114,127,123,130,4,17,6} {1,114,143,123,146,4,20,9} {2,109,132,112,141,4,18,7} {3,125,132,128,141,4,19,8} {0,109,122,128,125,4,15,4} {1,109,148,128,151,4,21,10} |
| 1 | {2,96,14,134,17,39,10,0} {2,185,14,223,17,39,11,1} {0,213,124,220,127,4,15,5} {1,213,138,220,141,4,18,8} {2,205,129,208,136,4,16,6} {3,225,129,228,136,4,17,7} {0,205,116,228,122,7,14,4} {1,205,143,228,149,7,19,9} |
| 2 | {2,249,165,293,169,45,11,3} {2,131,13,189,17,59,8,0} disabled disabled {2,45,172,51,181,7,12,4} {3,70,172,76,181,7,13,5} {0,49,164,72,170,7,10,2} {1,49,183,72,189,7,15,7} |
| 3 | {0,123,118,126,148,31,14,3} {2,144,100,174,102,31,12,1} {0,207,124,216,127,4,17,6} {1,207,140,216,143,4,20,9} {2,202,129,205,138,4,18,7} {3,218,129,221,138,4,19,8} {0,202,119,221,122,4,16,5} {1,202,145,221,148,4,21,10} |
| 4 | {0,180,124,184,146,22,6,2} {2,143,110,175,112,33,4,0} disabled ×4 {0,134,124,138,146,32,7,3} disabled |

`vdu_polygon(bar, percent)` (0x413DA0):

```
extent = percent * length / 100                     // C int arithmetic
if left == -99: return
A = frameA; B = frameB; clip = stCockpitBar (screen alias)
if direction < 2:                                   // vertical
    if direction == 1: extent = length - extent; swap(A, B)
    clip = (left, top, right, bottom - extent);     if non-empty: DrawSpriteDefault(clip, left, top, cockpit:7, A)
    clip = (left, bottom - extent + 1, right, bottom); if non-empty: DrawSpriteDefault(clip, left, top, cockpit:7, B)
else:                                               // horizontal
    if direction == 3: extent = length - extent; swap(A, B)
    clip = (left, top, right - extent, bottom)      -> frame A
    clip = (right - extent + 1, top, right, bottom) -> frame B
```

Both frames are drawn with their hot spot at (left, top); the clip splits the bar rectangle.

### 3.3 Screen viewports and text contexts

| Name | What | Font, colours |
| --- | --- | --- |
| `stSpaceBuffer` | off-screen space buffer (§4.5) | — |
| `stLeftVdu`, `stRightVdu` | copies of `stScreen` with the VDU rectangles | contexts `stLeftVduTextContext`/`stRightVduTextContext`: font 2, 0xA6 on black |
| `stCockpitBar` | copy of `stScreen`, rectangle set per bar | — |
| `stPilotHand` (+ composite, backdrop) | screen alias + two off-screen buffers of the hand rectangle | — |
| readouts `aCockpitReadouts[6]` | {context, x, y, previousRight}: 0 nav range, 1 target range (right VDU context, origin taken at the cursor when the VDU page is drawn), 2/3 speeds, 4/5 shields (readout context) | readout context font 2, 0xA6 on black, viewport `stScreen` |
| `stHudMessageTextContext` | viewport `&stSpaceBuffer`, alignment 2 (`InitializeGameTextContexts` 0x421D80) | font 1, red, transparent background |
| `stNavLabelTextContext`, `stNavMapTextContext` | nav map (§3.12) | font 2 labels; font 1 texts |

`DrawCockpitReadout(slot, text)` (0x413FB0): skip when x == −99; set the context and cursor,
`DrawFormattedText(text)` (the text is used as a **format string**), then erase from the new
cursor x to the previous right edge (font height rows) with black on the screen, and remember the
new right edge. `update_digital_readouts` (0x414A50): readout 2 = `(short)((anShipSpeed[0] >> 8) *
10)` (set speed ×10), readout 3 = `(short)(MultiplyFixed(Vector_magnitude(velocity), 0xA00) >> 8)`
(actual speed ×10).

### 3.4 Camera views, `initialize_cockpit` and the view geometry

`new_view(view, obj)` (0x4117D0, spc.c; simulation) sets the camera and calls
`initialize_cockpit(mode)` — the simulation's `ISimulationEvents.InitializeCockpitView(mode)`:

| View | `initialize_cockpit` mode | Eye (in `new_view` / per tick in `set_eye_direction_and_position` 0x410AF0) |
| --- | --- | --- |
| 0 front | 0 | player frame, position, velocity |
| 1 right | 1 | right = −player.forward, up = player.up, forward = player.right |
| 2 left | 2 | right = player.forward, up = player.up, forward = −player.right |
| 3 rear | 3 | player frame with right and forward negated |
| 4 chase | 4 | start 1200 behind `cViewObject` (unless scripted); per tick: target point 700 (500 near) behind along the player's forward, velocity = delta/25 (/7 near), forward toward the object, roll matched (rate 4, snap below 5°); a capital ship turns view 4 into 8 |
| 5 fly-by | 4 | 292 units behind the object along its velocity (forward if it stands still), then 400 right and 100 up, looking at it; per tick it only re-aims, and is re-placed (with `generate_stars`) when farther than 2000 |
| 6 missile camera | 4 | starts 600 to the missile's right; follows `nExternalViewShip`; 21 ticks after it is gone → `new_view(0,0)` (with the cockpitless dance) |
| 7 target view | 4 | player + right·300, looking at the target from 600 behind; target lost → `new_view(0,0)` |
| 8 capital chase | 4 | right = object.right, up = −object.forward, forward = object.up; per tick 2000 (`nCapitalShipViewDistance`) behind along the eye's forward; view-8 keys (§2.6) |
| 9 death | 6 (full screen) | like 3 |
| 10 ride object | 7 (pod interior, geometry 0 of the current packet) | copy of `cViewObject` |
| 11 follow | 4 | keeps ≥ 600 units, looks at the object; eye goals reset |
| 12 autopilot | 4 | player + right·500 + forward·2000, drifting −10 right per tick, looking at the player |
| 13 pull-away | 4 | player velocity (and wingman's) zeroed; from the object looking at the carrier, offset (0,−10,−400), velocity −35 along forward, looks at the player |
| 14 fleet overview | `SetFleetOverviewView(1)` → 6 | centre of all ships, distance from their spread (0x410740) |
| 15 scripted | 4 | velocity, rotation and eye goals (`update_scripted_view`) |

`new_view` ends with `set_eye_direction_and_position()` and **`generate_stars()`** (random draws).
Re-selecting the current view does nothing except toggling the chase distance in view 4;
`force_view` sets view −1 first.

`initialize_cockpit(mode)` (0x423E90, logic.c):

```
if message_showing(): EndCommMenu()
if bCockpitlessView == 0 && mode == cScreenViewportMode:        // same picture
    space buffer allocated ? ClearViewport(&stSpaceBuffer, 0xBF) : initialize_view_buffer(); return
GetScreenUpdateFlag()                                            // free the space buffer (ends a comm message)
save stScreen's rectangle, use the full screen
ClearViewport(&stModalSourceViewport, black)                     // whole screen black (no present: not the stScreen object)
cCockpitLogicalFile = cCockpitView + 17; cScreenViewportMode = mode
mode 0: cockpit art cockpit:0 at (0,0) unless cockpitless; ResetCockpitPaletteEntries(); explosion_draw() unless
        cockpitless (re-draws damage decals); reset_cockpit(); InvalidateVduMode(0); InvalidateVduMode(1); update_VDUs();
        clear_head_up_display(); ResetPilotHandAnimation() unless cockpitless; set_up_screen_viewport(0)
mode 1..3: art cockpit:mode unless cockpitless; set_up_screen_viewport(mode)
mode 4: if bIntroSceneResourcesActive == 1: letterbox 8:6 at (0,0) and, if bCockpitlessView < 1, PRESENT NOW
        (DIBslam + DIBslamReal); set_up_screen_viewport(4)
mode 5: set_up_screen_viewport(4)                                 // letterbox geometry without backdrop
mode 6: set_up_screen_viewport(5)                                 // full screen
mode 7: pod interior 8:7 unless cockpitless; set_up_screen_viewport(0)
bViewBufferEnabled = 1; stSpaceBuffer rect = (0,0)-(nScreenWidth-1, nScreenHeight-1); initialize_view_buffer()  // allocated, cleared to 0xBF
restore stScreen's rectangle
```

`set_up_screen_viewport(mode)` (0x436740, eventmgr.c): geometry = built-in record for 4/5, else
`pScreenViewportPacket` geometry `mode`; `cScreenViewportMode = mode`.
Cockpit (and −2): `nScreenWidth/Height` = geometry size, `nViewCenterX/Y` = size / 2, origin =
geometry origin. Cockpitless (1): centre = size / 2 and origin = geometry origin, then cockpit 0:
centre y and origin y +10, cockpit 1: +25, cockpit 2: +50 (3, 4: none); `nScreenWidth = 320`,
`nScreenHeight = 200` (so the projection uses a 160-pixel focal length and the cockpit's aim
point). The simulation needs `ScreenWidth`, `ScreenHeight`, `ViewCenterX/Y` after every call
(projection, `easy2see`, nav pointer).

View geometries (verified, `wc1tool export-view`; graphics.md §2.6):

| File | Geometry 0 (front) | 1 (right) | 2 (left) | 3 (rear) | buffer size field |
| --- | --- | --- | --- | --- | --- |
| PCSHIP.V00 Hornet | 320x105 at (0,10), 105 runs | 313x150 at (0,0), 213 | 320x150 at (0,0), 213 | 317x50 at (0,19), 78 | 48000 |
| PCSHIP.V01 Rapier | 320x70 at (0,24), 117 | 233x96 at (24,28), 106 | 240x96 at (56,28), 106 | 193x68 at (64,28), 162 | 23040 |
| PCSHIP.V02 Scimitar | 320x112 at (0,50), 186 | 298x101 at (0,60), 199 | 304x101 at (16,60), 199 | 313x80 at (0,9), 106 | 35840 |
| PCSHIP.V03 Raptor | 319x87 at (0,14), 117 | 288x98 at (32,16), 111 | 281x98 at (0,16), 111 | 190x66 at (64,25), 77 | 28224 |
| PCSHIP.V04 simulator | 232x81 at (40,29), 91 (only geometry) | — | — | — | 18792 |
| built-in 4 | 320x128 at (0,24), one run (0,24,40960) | | | | |
| built-in 5 | 320x200 at (0,0), one run (0,0,64000) | | | | |
| COCKPIT.VGA 8:8 | 299x106 at (8,34) (escape pod, §5.7) | | | | 31694 |

Cockpitless toggling (F1) and every view change in cockpitless mode wrap `new_view` /
`initialize_cockpit` in a "dance": free the buffer, set its rectangle to (0,0)-(nScreenWidth-1,
nScreenHeight-1), allocate, change the view, free, set 320x200, allocate. In the port this is just
"the space buffer surface has the geometry size in cockpit mode and 320x200 in cockpitless mode".

### 3.5 HUD drawn into the space buffer

`overlay_head_up_display` (0x416AC0, view 0 only, after the objects):

1. `target_locking(acShipTarget[0])` — simulation (`TargetLocking`, randoms, §6.4).
2. A comm message is showing (`message_showing()`) and `nCommSpeakerObject != -1`:
   `cPreviousTargetObject = speaker`; yellow corner brackets around the speaker
   (`draw_target_box(yellow, speaker, solid 0, lockMarker 0, padding 2,
   &stPreviousTargetBracketBounds)`).
3. Target box `draw_target_box(red, acShipTarget[0], solid = nTargetLockMode, lockMarker 1,
   padding 1, &stTargetBracketBounds)`; when locked (`nTargetLockCountdown == 0`)
   `bTargetBracketVisible ^= 1` on even rendered frames and the box is drawn only while visible
   (blinks 2 frames on, 2 off).
4. Gunsight 8:0 frame 0: cockpit → at (`nViewCenterX`, `nViewCenterY`); cockpitless → cockpits
   0/2 centred, 1 at (cx, cy−1), 3 at (cx, cy+14), 4 none.
5. HUD text: `pszPendingHudMessage` → `ShowHudTextLine(text, message colour)` (§3.6).
6. Mouse crosshair (`bMouseCursorVisible == 1`): remember (x, y), `CaptureSpriteBackground` into
   `abMouseCursorBackground`, draw ARROW.VGA frame 2 at the cursor position (buffer coordinates).

`draw_target_box(colour, object, solid, lockMarker, padding, saved)` (0x4164B0):

```
if colour == 0xBF:                                    // erase call
    valid = saved.left != -0x7FFF; bounds = saved
else:
    valid = object != -1 && asObjectScreenX[object] != 0x8001
    if valid:
        c = (screenX + nViewCenterX, screenY + nViewCenterY)
        if GetTransformedShapeBounds(&stSpaceBuffer, c, shape, viewFrame, angle, screenScale, flip, &bounds):
            bounds grown by padding on all sides
        else valid = 0
if valid:
    if colour == red && side[object] == side[0]: colour = blue          // friendly targets in blue
    solid ? DrawViewportBorder(bounds)                                  // lock mode: full rectangle
          : 8 lines: from each corner a horizontal segment of (right-left)/6 + 1 and a vertical one of (bottom-top)/6 + 1
    if lockMarker:
        if colour != 0xBF:
            if nTargetLockCountdown > -1:                               // lock running or acquired
                nTargetLockMarkerAngle += rollRate[0] + pitchRate[0]
                m = c + ((CosFixed(angle) * countdown * 2) >> 8, (SinFixed(angle) * countdown * 2) >> 8)
                DrawSpriteDefault(&stSpaceBuffer, m, 8:0, frame 1); remember m
        elif marker remembered: DrawSolidColourSprite(frame 1 at m, 0xBF); forget
    saved = colour == 0xBF ? {left = -0x7FFF} : bounds
else: saved.left = -0x7FFF
```

The lock marker spirals in: radius `2 * countdown` pixels (lock time 18 ticks for heat seekers,
32 for image recognition, simulation.md §6.4), the start angle is random (`start_lock`), and
it turns with the player's roll and pitch rates; at lock it sits on the target's centre.

Nav pointer: `draw_nav_pointer` (simulation, called first by `transform_objects_to_your_view`)
keeps an effect slot of class PLANET with view frame 3, scale 0x100, distance 0x4A38 (drawn behind
everything nearer than 19000 units) while the right VDU shows navigation (mode 5), not during the
autopilot, in views 0 and 4. The slot's shape is `pTargetLockShape` (8:0, frame 3); the C#
simulation leaves `Shape = None` and the UI draws `NavPointerObject` with that shape. When the
objective leaves the view cone the pointer keeps its last screen position (no reset) — faithful.

`RestoreTransientCockpitGraphics` (0x416CB0), after the dump, view 0: restore the crosshair
background; erase the speaker brackets (`cPreviousTargetObject = -1`) and the target box (colour
0xBF); if the displayed HUD text differs from the pending one, `SetHudTextColour(0)` (erase);
draw the cockpit explosion on the screen (§3.10). The erasures are redundant for the pixels (the
buffer is cleared right afterwards) but **reset state** (saved bounds, marker, displayed text) and
must be kept.

### 3.6 HUD message line

State: `pszPendingHudMessage`, `pszDisplayedHudMessage`, the message colour (`DAT_005a7f00`),
`nMessageTimer` (`message_showing()` = timer > 0).

* `SetHudMessageText(text, colour, duration)` (0x416DE0): ignored while the nav map runs; a
  message showing → `SetHudTextColour(1)` (`EndCommMenu` + erase); set colour, pending text,
  timer.
* `ShowHudTextLine(text, colour)` (0x416460): pending = text; `print_message_text`.
* `check_message` (0x414A20, every rendered frame, any view): timer > 0 → −1; reaching 0 →
  `EndCommMenu()` (timer 0, comm video session ended, pending = null). The message stays visible
  for `duration` **rendered frames**.
* `SetHudTextColour(v)` (0x416480): v ≠ 0 → `EndCommMenu()`; `print_message_text(displayed,
  0xBF)` (erase by redrawing).
* `print_message_text(text, colour)` (0x416260): text copied (≤ 83 characters); viewport = copy
  of the space buffer viewport with rectangle (x0, y0)-(319 − x0, y0 + 60), (x0, y0) =
  `asHudMessageOrigins[cCockpitView]`; context = copy of the HUD context with background 0xFF
  (transparent) and the colour; manual wrapping with `charactersPerLine = (right - left) / 6`
  (`GetRectHeight` returns the width − 1): at every `(position + 1) % charactersPerLine == 0`,
  before the first space a '\n' is inserted after the character; after a space the '\n' is
  written at `output[position - lastSpace]`, **ahead** of the output pointer, where later
  characters overwrite it (`lastSpace` is never reset): visible wrapping then comes from
  `DrawTextString`'s own word wrap at the rectangle edge. Drawn with `DrawTextAt(context, x0, y0,
  wrapped, 2)`; in cockpitless mode y0 += 10 / 25 / 50 for cockpits 0 / 1 / 2 (3: +0; 4: not
  drawn). Finally `pszDisplayedHudMessage = pszPendingHudMessage`.

Messages on this line: on-screen messages (§2.7, red, `MeasureMessageWidth` frames), toggles (V,
F8: 20 frames), the fps message, comm speech (`ShowCentredPrompt`, yellow, §3.11), the training
simulator's "Wave %d complete." / "Mission %d complete." bonus text (§5.10, through
`FormatTextBufferFromStart`).

### 3.7 Cockpit instruments: `update_cockpit` (0x417E70)

Runs every tick after `RenderSpaceViewFrame`, i.e. after the dump: everything it draws goes onto
the screen over the cockpit art (and, in cockpitless mode, over the full-screen space view).

```
check_target()                    // simulation: drop dying target; nRenderedSpaceFrame % 8 == 0 && lock mode: malf(5) -> lock mode off + malf_sound; auto-targeting
repair_internal_damage()          // simulation (randoms)
if cMissionObjectiveCount: update_objective_location(nSpaceFrame % count)       // simulation, phase 3
if nCameraViewMode == 0:
    if !cockpitless: RestoreCockpitExplosionBackground()
    update_lights()               // fuel % -> light 6 + bar 0; gun energy -> bar 1; damage alarm (not simulator):
                                  //   calculate_damage_level() >= 3 && fore+aft shields < 10 -> light 3 blink(2) + sfx 0x20
                                  //   (when no alarm handle or nSpaceFrame % 10 == 0); else, if an alarm handle exists:
                                  //   stop it, handle = 0, light 3 off
    update_missile_warning()      // missile_on_tail(0) -> light 2 blink(1) + spacetrack(3, 1, -1) (not simulator); else light 2 off
    draw_3d_scanner()
    update_digital_readouts()     // readouts 2, 3
    update_VDUs()                 // §3.8
    if !cockpitless: animate_pilot()
    update_bars()                 // bars 2..5 armour (armour[2]/armorRight, armour[3]/armorLeft: swapped divisors, equal for all types),
                                  //   fore/aft shield % -> lights 0/1 blink, bars 6/7, readouts 4/5 (numeric shield)
    draw_cockpit_lights()         // every 4th rendered frame: light 4 = auto_pilot_valid(0); draw lights whose state changed
                                  //   (cockpitless: draw all every tick)
    if !cockpitless: cockpit_explosion()
    npc_communication()           // comm chatter (§3.11, randoms)
fire_computer_graphic_missile()   // weapon VDU launch animation (any view)
check_stranded()                  // simulation: nArcadeState = 3
```

* `SetCockpitLightBlink(light, interval)` (0x414440): interval < 20: 0 → toggle every call, else
  toggle when `nSpaceFrame % interval == 0`; interval ≥ 20 → off. Fuel and shields pass their
  percentage, so they blink below 20 % and faster when lower.
* Damage alarm in KS: `nDamageAlarmSfxHandle` is never assigned a non-zero value
  (`PlaySfxWaveFileByNumber` returns nothing; only `ResetSoundState` and the release branches
  write 0). So `PlaySfxWaveFileByNumber(0x20)` is called on **every tick** (in view 0) while the alarm
  condition holds, and both release branches (`update_lights`' else branch, `house_keep` outside
  view 0) are dead: light 3 keeps its last blink state after the condition clears, until the next
  `reset_cockpit` (cockpit view 0 set-up). DOS presumably kept a driver handle. Audio already ports
  the KS logic literally (`SoundEffectManager.ServiceDamageAlarm(condition, spaceFrame)` returns
  true when the caller must clear light 3; `ReleaseDamageAlarm()` for `house_keep`); see §8.2.
* Scanner (`draw_3d_scanner` 0x415CE0): cockpitless → the radar grid (pixel table
  `aiScannerGridRows`, mirrored into four quadrants, colour 0xAA) every tick;
  `clear_head_up_display` restores last tick's blip pixels and the nav marker background; for
  ships 1..9 with a colour (`get_color` 0x415C00: fighter red 0x50 Kilrathi / blue 0x25 Confed /
  0xA6 neutral; capital orange 0x85 Kilrathi / white 0x0F Tiger's Claw / grey 0x07 other; missile
  targeting the player yellow 0x47; anything else not shown): spherical position of
  `aObjectViewPosition` (eye relative, computed on rendered frames), radius < 0xEA6000 (15000
  units) → `rotational_pos_to_scanner_pos`: x = cx + yaw/4 (|yaw| < 45) else cx + yaw/6, y = cy −
  pitch/3, clamped to the scanner box; the background pixel is saved and the blip drawn (not for
  the current target). Right VDU in navigation mode: the objective's scanner position
  (`set_objective_range(1)`) gets 8:0 frame 2 with saved background. The current target's blip
  blinks: black on even `nSpaceFrame`.
* Pilot hand (`animate_pilot` 0x4173C0, cockpit only, when the hand shape exists):
  `determine_pilot_hand`: yaw = nYawInput/2, pitch = nPitchInput/2; yaw > 0 → frame min(yaw+8, 12),
  yaw < 0 → min(4−yaw, 8), else pitch > 0 → min(pitch+12, 16), pitch < 0 → min(−pitch, 4), else 0.
  On a change `DrawPilotHandFrame` copies the backdrop to the composite, draws the hand frame at
  (154 − hand.left, 187 − hand.top) and the sleeve (frame 17) at that point plus
  `asPilotHandOffsets[frame]`, and copies the composite to the screen. `ResetPilotHandAnimation`
  (cockpit view 0 set-up) grabs the backdrop from the freshly drawn cockpit art.
* Weapon launch animation (`fire_computer_graphic_missile` 0x414D50): the launch (simulation
  callback `PlayerReleaseWeaponLaunched`, C `RemovePlayerReleaseWeapon`) stores the sprite frame
  `type*2 − 47` at the weapon display position of the hardpoint, speed 3; each tick: restore the
  previous background, move up by the speed (mines down) while y is inside (top−10, bottom), speed
  +1, draw (capture + sprite) only in view 0 with the weapon display showing.

### 3.8 VDUs

Mode stack: per VDU four `int` entries `ausVduModeStack` and a depth `acVduModeStackDepth`;
`get_mode(i)` (0x4147E0) = entry at the depth; `set_mode(i, m)` (0x414800) clears the VDU's message
slot if the mode changes, depth 0, entry = m; `push_mode` clears the slot, depth+1, entry = m;
`pop_mode` clears the slot, depth−1. `anVduModeCache[i]` = last drawn mode; `InvalidateVduMode(i)`
sets it to 0 (forces a redraw, except of mode 0). `init_vdus` (0x4244E0): left = mode 1
(weapons), right = 5 (navigation) or 3 (target) in the training simulator.

| Mode | VDU | Page | Entered by |
| --- | --- | --- | --- |
| 0 | both | broken: static every tick (`update_dead_disp` → `malf_noise(vdu, 1, 0xAA, sound 23, 0)`) | `vdu_malf`: computer damage, or a VDU key failing `malf(3)` / `malf(4)` |
| 1 | left | weapons | default; W, G |
| 2 | left | damage report | D |
| 3 | right | target | T; default in the simulator; firing with a target while showing navigation |
| 4 | right | comm menu (pushed) | C |
| 5 | right | navigation | default in campaigns; N; A |
| 6 | right | comm video (pushed) | `real_vid_transmit` |
| 8 | left | info (debug: version, series/mission, kills, scores, objective flags) | **no setter: unreachable** |

`update_VDUs` (0x417B70), view 0, every tick:

```
SetTextContext(left); cockpitless: black rectangles at both VDU bounds (the space view covered them)
if update_vid_disp(0)          // mode != cache -> set_new_vdu(0): mode 0 static, else clear the VDU black; cache = mode
    mode 0: static | 1: show_weapon_disp | 2: show_damage_disp | 8: show_info_disp
else
    mode 0: static | 1: (cockpitless: show_weapon_disp) update_status_text (empty)
    2: (cockpitless: show_damage_disp with bForceDamageDisplayRedraw) UpdateDamageDisplay | 8: show_info_disp
left slot: mode 0 -> text = null, else UpdateMessage(slot 0)
SetTextContext(right)
if update_vid_disp(1)
    0: static | 3: show_target_disp | 4: show_communications_disp | 5: show_navigation_disp | 6: vid_transmit
else
    0: static | 3: (cockpitless: show_target_disp) DrawTargetRangeReadout | 4: (cockpitless: show_communications_disp) talk_equiv
    5: (cockpitless: show_navigation_disp) check_objectives | 6: vid_transmit
right slot: mode 6 or 0 -> text = null, else UpdateMessage(slot 1)
simulator with a pilot hand: CopyTrainSimPilotViewToRightVdu()   // keeps the VDU columns that overlap the hand rectangle
```

`SelectCockpitVduMode(vdu, mode)` (0x417F60): only in view 0; `malf(3) || (mode == 4 &&
malf(4))` → `vdu_malf(vdu, 0x17)` and return; sfx 0x19; mode differs → `vdu_pop_all(vdu)` (pops
pushed pages; a comm video page ends through `EndCommMenu`), invalidate, then mode ≠ 4 → `set_mode`
+ `update_VDUs`, mode 4 → `show_communications_disp` (pushes 4) + `update_VDUs`. Same mode →
1: G (current key 0x22) → `select_new_gun`, else `select_new_release_weapon(-1)`; 2:
`nDamageDisplayTicks = 0`; 3: `cycle_onscreen_targets`; 4: `talk_equiv`; 5: **`InflightComputer`**
(the nav map, §3.12).

`vdu_malf(vdu, sound)` (0x414B20): view 0 → `malf_noise(vdu, 1, 0xAA, sound, 0)`; `set_mode(vdu, 0)`.
`malf_noise(vdu, effect, colour, sound, refresh)` (0x416E20): sound 23 → `PlaySnowStaticSound`
(KS WAV; nothing with DOS audio, audio.md) else sfx; `snow_viewport(VDU, effect, colour)` — KS
draws **nothing**; SDL draws static with its own xorshift32 (seed 0x1F123BB5; per pixel `s = next
>> 16`; skip when `(s & 3) > (effect >= 3 ? 1 : 2)`, else `colour` if `s & 4` else black), never
the game RNG; refresh → `set_new_vdu`.

Pages:

* **Weapons** (`show_weapon_disp` 0x414EA0): `set_new_vdu(0)`; "WEAPON DISPLAY" at (left, top)
  (alignment 2); line (left+2, top+5)-(right−2, top+5) in 0xA6; "\nWeapon: %s" (selected release
  weapon's type name or ""), "\nGun: %s" (gun type name, "Full Guns" for 0x80, "" for none); ship
  outline (cockpit:9 frame 0) at origin = (left, top) + (0,16); every loadout slot at origin +
  `aWeaponDisplayPositions[hardpoint]` with frame `type*2 + disabled − 47` (enabled/disabled look).
* **Damage** (`show_damage_disp` 0x42C800): counts components with damage ≥ 1; `set_new_vdu(0)`;
  "DAMAGE REPORT"; line at top+6; none → "NO INTERNAL\n\nDAMAGE" at top+20; else the outline at
  the weapon origin and slot 0 "%d Unit%c Damaged" (0xA6, permanent). `UpdateDamageDisplay`
  (0x42C970) every tick: count changed → invalidate (full redraw); cockpit mode: countdown
  `nDamageDisplayTicks`; at 0 alternately (phase 1) select the next damaged component (cyclic),
  draw "%s\nDamage: %s" (component name, OK/Light/Moderate/Heavy/Destroyed) at (left+1, top+7),
  capture + draw the component sprite (frames 21..29 at origin + `aDamageDisplayPositions`), line
  from (left+36, top+22) to it in 0xA9, hold 50 ticks; (phase 0) erase all three (background
  restore, text in black, line black), hold 2 ticks. Cockpitless: no erase phase.
  Component names (globals.c 0x46A7C4..): "Ion drive", "Power plant", "Shield gen'r", "Computer
  sys", "InterCom unit", "Target track", "Accel absorbers", "Ejector system", "Repair systems";
  severities "Ok", "Light", "Moderate", "Heavy", "Destroyed".
* **Target** (`show_target_disp` 0x42DB90): "" at (left, top); "   LOCKED TARGET" (red) in lock
  mode else "  AUTO TARGETTING" (0xA8); target dropped if class < SHIP or dying; "\nTarget:" +
  " None" / " callsign" (rating 0..7) / " ace name" (9..12) / " type name"; "\nRange : " (readout
  1 starts here); off screen → stop. Silhouette (the type's `shape`: SHIPTYPE section 1 or SHIP
  section 0x25, 3 frames) at (left+37, top+38): aft shield indicator 8:4 frame `(3 − min(aft*6 /
  maxAft, 3)) * 2`, drawn only when below 6 (aft shield at least 1/6 of maximum);
  four armour quadrants clipped by `aTargetArmorClipRects` with frame 0
  (armour > max/2) or 1; overlay frame 2; fore shield indicator frame + 1. **The image is static
  (no rotation).** `DrawTargetRangeReadout` (0x42DEA0) every tick: dying target → drop + invalidate;
  target changed or `nRenderedSpaceFrame % 8 == 0` → full redraw; readout 1 = "%u m" (≤ 30000),
  "----- m" off screen, "TOO FAR"; lock readout erase when `bTargetLockReadoutDirty`.
* **Navigation** (`show_navigation_disp` 0x415180): "COMP NAVIGATION"; "\n\nDESTINATION\n  %s"
  (`objective_name`: "NONE" past the list, "UNKNOWN" for unsighted '?' names, else the display
  name); "\n\nRANGE\n  " (readout 0 here); "\n\n(N)ew Objective"; readout 0 = "CALCULATING"
  (range ≤ 0) or `range` + " km" where range = objective distance in **units** (`spherical.radius
  >> 8`). `check_objectives` (0x4158A0) every tick: objective lost → `cycle_next_objective` +
  invalidate, else `update_objective_location(current)`; range changed → readout redraw.
* **Comm menu / video**: §3.11.

Message slots (`aHudMessageSlots[2]`): slot 0 = left VDU line at (left, bottom − 6), slot 1 = right
VDU line at (left, bottom − 6). `DrawHudMessageSlot` (0x4140A0, view 0 only): blink phase
`(nTickCount60Hz / 40) % 3 == 0` → black, else the colour (2-second period, one third dark);
`flashCount` decrements on each colour→black edge; 0 → black; −1 = permanent; `DrawTextAt(...,
2)`. `UpdateMessage`: draw, clear when the count is 0 and the text is erased. `ClearHudMessageSlot`
erases. `CockpitMessage` (slot 1) only replaces a different text; `remove_message` clears a
matching text. Uses: slot 1 "MISSILE LOCKED " (red, 2), "Already Near" / "Enemy Near" / "Hazard
Near" (autopilot refused, yellow, 3), "Wait for %s" / "Objective Reached" / "Already Visited"
(yellow, 4), comm "SELECT" / "CHOOSE" (yellow, −1); slot 0 (`ShowComponentHitHudMessage`, only
outside the simulator and with a working left VDU) "%s HIT" (red, 5), "%s FIXD" (red, 8), "Weapon
destroyed" (red, 8), "Fuel tanks hit" (red, 8), "Need Lock" (yellow, 3), "%d Unit%c Damaged".

### 3.9 Palette effects

All in `Graphics.Palettes.FlightPaletteEffects` (already ported):

* Entry **0xBF** (space background): `ResetCockpitPaletteEntries` (0x423E10; cockpit view 0 set-up
  and flight exit) sets (0,0,32) and entries 185..190 black. A hit on the player
  (`TriggerPlayerHitPaletteFlash` 0x427C80, camera views ≤ 3) sets R = 0x30;
  `UpdateSpacePaletteFade` (0x427CD0) at the start of every `Draw_3Space_Frame` call (every tick)
  does R −= 4 and writes the entry while R ≠ 0 (12 ticks to fade).
* Entries **185..190** (cockpit hit-direction lights in the cockpit art): a projectile hitting the
  player sets R = 0x38 of entry 0 rear, 1 front, 2 top, 3 left, 4 bottom, 5 right (|pitch| < 45:
  |yaw| < 45 front, < 136 left/right by sign, else rear; else top/bottom by pitch sign;
  object_collision, spc.c). `house_keep` fades all six per tick in view 0 (`FadeFlightPaletteEntry`:
  R ≠ 0 → R −= 4, G = B = 0; else G = 0) and writes 185..190.
* Whole-palette fades only in sequences (`FadeViewportPaletteToColour` at the end of death,
  ejection, stranded).
* The DOS EGA branches (space buffer filled red) are not used.

### 3.10 Cockpit damage and the cockpit explosion

`place_damage_on_cockpit(damage 0..3)` (0x4178A0; simulation callback, from
`your_internal_damage` with `RandomBelowOrEqual(3)`): only view 0, not the simulator, decal not yet
shown: mark it; if `pCockpitExplosionShape` is null → `explosion_draw()` (load cockpit:4, draw
every marked decal at `aaCockpitDamagePositions[cockpit][decal]` on the screen, free); else start
the explosion at the decal's position (frame 0x7FFF). `cockpit_explosion` (0x4177B0, per tick in
the cockpit): 0x7FFF → 0; sfx 0x1B at frame 0; frame 3 → `DrawPendingCockpitDamage` (decal on the
screen and in the pilot-hand backdrop); capture + draw explosion frames 0..7 (8:5); when no
explosion is active it **frees `pCockpitExplosionShape`**. Because the shape is loaded only at
start-up and `update_cockpit` runs with no active explosion during the launch sequence, the shape
is gone before the first hit: in KS and the SDL port damage decals always appear **instantly**
(`explosion_draw`), the explosion animation is dead code. Decal positions:

| Cockpit | Decals 0..3 |
| --- | --- |
| 0 | (224,5) (132,96) (233,107) (149,161) |
| 1 | (177,6) (153,142) (103,140) (55,183) |
| 2 | (107,25) (211,32) (21,178) (300,178) |
| 3 | (74,10) (294,19) (197,105) (105,134) |

`initialize_cockpit(0)` re-draws marked decals (`explosion_draw`) whenever the front cockpit is
rebuilt; `clear_cockpit_damage` resets them at `InitializeCockpitResources`.

### 3.11 Communication

**Speech data.** `init_personalities` (0x40C2B0) loads, for every mission ship record with pilot
5..12 (wingmen) or > 13 (aces), the 11 lines of its face from COMMUNIC.DAT (`(face*11 + line) *
80`, LF 13) plus both generic faces; `get_face(rating, side)` (0x430BC0): rating −1 → 12 (Confed)
/ 13 (Kilrathi), Kilrathi aces rating − 1 (8..11), wingmen 0..7. Portraits
(`LoadCommPortraitShape` 0x430BF0): faces 0..7 → WINGMEN.VGA section face+1; 8..11 and 13 → section
10; **12 (generic Confed) has none** (text only).

**Queued lines.** `send_message(obj, line)` (cockpt.c 0x417420; simulation, phase 3) stores
`acWingmanMessageState[obj] = line` for rated pilots, the carrier, the escort target or Kilrathi
(radio silence suppresses the wingman). `npc_communication` (0x4174F0, view 0, not canned, not
simulator):

```
for obj in 1..9 while !message_showing():
    if class >= SHIP && pending[obj] != -1: vid_equiv(obj, pending[obj]); pending[obj] = -1   // cleared even if not shown
if RandomBelowOrEqual(5000) > 4998 && nCommSpeakerObject == -1:        // the draw happens every call
    for obj in 1..9: if Kilrathi ship engaging (objective ENGAGE_ENEMY or DESTROY_SHIP)
                     && (rated || RandomBelowOrEqual(100) < 20):
                        pending[obj] = RandomBelowOrEqual(2) + 2; return     // taunt lines 2..4
                     (stop when a speaker appeared)
```

`vid_equiv(obj, line)` (0x417AC0): only when the right VDU is not the comm menu, not simulator,
not canned, view 0 and no message showing → `real_vid_transmit`.

`real_vid_transmit(obj, line)` (0x4316E0): `nCommSpeakerObject = obj`, rating, face; face −1 →
return; with video enabled and images not suppressed: load the portrait and backgrounds, then
`push_mode(1, 6)`, `malf_noise(1, 3, 12, 23, 1)` (static + clear), background (Confed 11:0 or
Kilrathi 11:9) and portrait frame 0 at the right VDU's top-left. Text: "%s: %s" with the callsign
(rating 0..7), the ace name (9..12) or the ship type name, `ExpandCommMessageTokens` (`$C`
callsign, `$N`/`$P` name, `$R` rank with '.' de-duplication), `ShowCentredPrompt(text,
MeasureMessageWidth(unexpanded text))` → yellow HUD line.

`vid_transmit` (0x417910) in right VDU mode 6, every tick: speaker neutral → end the session;
drawn when (cockpitless or `nRenderedSpaceFrame` odd) and a portrait and speech exist and images
are not suppressed: dying speaker (special 9) → static frame `counter / 5` (Confed) or frame 2
(Kilrathi: the section has only 2 frames, so nothing is drawn); else `nCommPortraitFrame == -1` →
`RandomInRange(0, 2)`; `r = RandomInRange(0, 3)`, r < 3 → frame = r; `set_new_vdu(1)`;
background + portrait frame. (Random mouth frames, no lip sync: there are no talking heads in
flight.)

`EndCommMenu` (0x4314C0): timer 0; right VDU mode 6 → `EndCommSessionWithWingman` (static
`malf_noise(1,1,12,23,1)`, `FreeCommDisplayResources` → speaker −1, `pop_mode(1)`); pending HUD
text null. Called when the message timer runs out, by C, by `initialize_cockpit`,
`GetScreenUpdateFlag`, `SetHudTextColour(1)`, the nav map.

**Comm menu** (right VDU mode 4; screen.c):

```
SelectCockpitVduMode(1, 4) -> show_communications_disp():
    menu not open -> HandleCommunicationMenuRequest(): if !message_showing() && CanOpenCommMenu()   // a live target or a wingman
                     -> push_mode(1, 4); pending action 1 (choose recipient); ResetCommMenuChoices(0); RefreshCommunicationMenu()
    menu open -> set_new_vdu(1); heading; "\n%d %s" per choice (1-based); cursor sprite 0x19 of pCommMenuCursorShape
                 (never loaded: draws nothing); SDL highlights the gamepad selection in yellow; reuse mode = 1
RefreshCommunicationMenu() (talk_equiv every tick in mode 4): action 1 -> recipient menu, action 2 -> command menu;
                 any change of a choice resets the reuse mode -> InvalidateVduMode(1) (redraw)
Recipient menu: heading "VID-COM SYSTEM\n\nSend message to?\n\n", slot 1 "SELECT"; wingman dead -> recipient = target;
                no live target or target == wingman -> recipient = wingman; else choices: wingman callsign (1),
                "ENEMY TARGET" (2) for a Kilrathi fighter target, or the target's type name (3) for a Confed target that
                is the Tiger's Claw or a fighter while enemies are within 14000; then "Never mind..." (0)
Command menu (recipient chosen): wingman: 7 if holding formation with enemies within 14000; 9 if the auto-engage
                timer is idle and not in formation, else 8 when the timer runs; 11 under radio silence else 10;
                same side: 12 at the Tiger's Claw before landing clearance; 1 if the target is Kilrathi;
                2 if evaluate_damage(0) < 50 and enemies within 14000; wingman: 3; Kilrathi recipient: 4, 5, 6;
                then 0 if anything was added, else the menu closes.
                Heading "VID-COM SYSTEM\n\nTo: <name>\n" (type name / callsign / ace name), slot 1 "CHOOSE"
Chosen_communicate_option(choice) (keys 1..9, sfx 0x19): action 0 -> close; action 1: command 0 -> close,
                1 -> recipient = wingman, else recipient = target, refresh; action 2 -> close, request(0, recipient, command)
```

Menu texts (`aszCommMenuText`, index = command): 0 "Never mind...", 1 "Attack my target!", 2 "Help
me out here", 3 "Return to base.", 4 "Die furball!", 5 "Slag off!", 6 "Bite it cat face.", 7 "Break
and attack.", 8 "Keep formation!", 9 "Form on my wing.", 10 "Keep radio silence", 11 "Broadcast
freely", 12 "Request Landing". `CloseCommChoiceMenu` (0x430DE0) pops mode 4; called when the menu
is not open it calls `exit_squadron("!stop")` (all callers check first). `request()` is the
simulation's wingman/command logic (phase 3, simulation.md §7.6).

### 3.12 In-flight computer: the nav map (`InflightComputer`, 0x40E480, nav.c)

Entered by N while the right VDU already shows navigation. Modal: **the simulation does not tick**;
it presents every iteration at the flight rate (20 fps).

```
saved nav index; bInflightComputerActive = 1; save stMouseCursorState
message showing -> EndCommMenu(); GetScreenUpdateFlag(); cScreenViewportMode = -1
ClearViewport(&stScreen, black)                   // presents immediately (stScreen object)
in-flight computer frame 8:1 at (0,0)
BriefingMap_LoadShapes(): pNavMapShape = 8:2; scene buffer 260x156 (0,0)-(259,155); LocateMobileObjective for all
ShowConfedNavScan(): stScreen rect (30,22)-(289,177); DrawNavLocationReadout("ConFed Nav Scan", 1):
    scene buffer cleared; texts (font 1) in (155,2)-(259,155): title, "Sector: Vega XR-231.3", "System: <series aux>",
    "* <mission aux> *", "* <player mission type name> *", "\nNotes\n", nav_note(current objective) (name without '?');
    legend: "MISSION FLIGHT PATH" or "HOME BASE" in yellow at y 120 when selected;
    BuildMap(1): map art at (1,1), clip (2,2)-(152,137); hazard fields ("Asteroids" 0xF5 / "Mines" red ellipses + labels);
       objectives (not mobile, or alive and not achieved; not hidden): unvisited -> dot in the style's unvisited colour,
       marker by objective type (aNavMapObjectiveStyles {marker, size, unvisited, marker colour, label}:
       0 nav point square 2 (0xA6, 0xA8), 1 home base triangle 2 (black, white), 2 escort cross 2 (magenta, magenta),
       3 reach ellipse 3 (magenta, 0xA8), 4 destroy ellipse 3 (red, red); labels 0xA8, the current objective yellow),
       labels placed by PlaceNavMapLabel (0x40D2C0: up to 13 rounds of five candidate positions around the point that
       avoid reserved areas and stay inside 150x135, forced in the last round; width = 4 * strlen + 2, height 6);
       player dot (white) + callsign label (light grey 0x0B)
    "Location: %d.%d.%d" at (8,142): each coordinate printed as (short) of the raw 24.8 value (its low 16 bits);
    CopyViewportContents(scene buffer -> stScreen at (30,22)); present
no visible objective -> pump PollJoystickButtonEvents, WaitForInputKey(), SetFrameTimerAndWait(20), pump back
else:
    pointer viewport = screen (32,24)-(182,159) as cursor clamp; pump PollMenuInputDevices; cursor shown (frame 0 arrow);
    mouse warped onto the current objective (map + (30,22))
    loop until done or bEscapePressed:
        selection changed -> sfx 0x19, ShowConfedNavScan()
        stScreen rect (32,24)-(289,177); cursor frame 0
        FormatNavCoordinates(pElapsedCampaignDate)        // game clock hours/minutes written into elapsedDate.day bytes (!)
        player marker blinking (nTickCount60Hz/15: grey 0x07 / white 0x0F) drawn on the screen
        "Standard time HH:MM" at screen (+150, +140), colon blinking every 4 phases
        restore stScreen rect
        event: button (2) or 10 -> done; Enter/Space -> done; N -> cycle_next_objective + mouse onto it;
               other keys -> MoveMenuPointerFromKeyboard
        SelectNavObjectiveAtPoint(cursor): map point within 6 (Manhattan) of an objective or on its label -> set_new_objective
        present
    Esc -> restore the saved objective (set_new_objective)
    free scene buffer, cursor hidden, pumps restored
release map art; sfx 0x19; restore cursor state and warp; force_view(0, 0) (cockpitless: with the buffer dance); bInflightComputerActive = 0
```

Side effect worth porting: `FormatNavCoordinates` writes the game-clock hours into byte 0 and the
minutes into byte 1 of `stCampaignState.elapsedDate.day` (saved with the game; the briefing macro
`$T` prints those two bytes as a time). The map math (`SetScale`, `nav_getxy`,
`ScaleNavMapCoordinates`, `ScaleNavMapMarkerSize`) is already in the simulation; the drawing is
shared with the briefing map (`BriefingMap_DisplayMap`, Scenes work stream) and belongs in one Game class.

---

## 4. The space view on the CPU path

### 4.1 Division of work

The simulation computes, on rendered frames (`PrepareSpaceView`), for every object slot: `Class`,
`Type`, `ScreenX/Y` (relative to the view centre; `0x8001` = not visible), `ScreenScale` (8.8),
`ScreenAngle` (0..359), `Flip` (0x10 mirror x, 0x20 mirror y), `ViewFrame`, `Shape` (`ShapeRef`),
`Distance` (painter key, 0 = not visible), `ViewPosition` (eye-space position, also used by the
scanner), and the draw order `SortedObjects` (−1 terminated). The UI resolves shapes, draws the
sprites into the space buffer, writes `asObjectDrawX/Y` (used by `intro_drawbackgroundships`) and
draws everything else. Projection, culling, view-frame selection, star field, dust, exhaust and
child placement are simulation (simulation.md §2.4, §2.5, §3.6); they consume randoms and decide
gameplay (visibility feeds targeting and `easy2see`), so they never move into the renderer.

Projection facts the UI depends on: focal length `(nScreenWidth & ~1) / 2` (160 for every
320-wide geometry and in cockpitless mode, 116 for the simulator's 232-wide window), origin at
(`nViewCenterX`, `nViewCenterY`) of the space buffer, screen y grows downward with object +y (no
flip), field-of-view cull `z/dist < 0x94` (≈ 54.7° half angle), sprite scale `objScale * (W/2) /
(dist − radius)` clamped to 0x2000, culled below 5. All three (`ScreenWidth`, `ViewCenterX/Y`) are
set by the UI's `set_up_screen_viewport` (§3.4).

### 4.2 Painter order: `sort_object_depth` (0x436460, eventmgr.c; simulation)

```
best = slot with the greatest (ushort)asObjectDistance over ALL 64 slots (strictly greater, so the first of equals);
       invisible slots count (distance 0): with nothing visible this is slot 0
for each output position:
    sorted[k] = best; if best == -1: stop (terminator)
    placed[best] = 1
    best = among unplaced slots with ScreenX != 0x8001 the greatest distance <= the initial maximum (first of equals)
```

So the list is far-to-near, the first entry may be an invisible slot (drawn at `0x8001 + centre`,
i.e. clipped away), equal distances keep slot order.

### 4.3 `draw_sorted_objects_to_buffer` (0x436520, eventmgr.c; UI)

```
for obj in sorted until obj < 0 or aeObjectType[obj] < 0:
    class NULL: skip
    (x, y) = (ScreenX + nViewCenterX, ScreenY + nViewCenterY); asObjectDrawX/Y = (x, y)
    class STAR, DUST (KS: also PLANET):
        shape = obj == nNavPointerObject ? apObjectShape[obj] : pConstellationShape
        [SDL record (angle 0, scale 0x100, flip 0)] else DrawSpriteDefault(&stSpaceBuffer, x, y, shape, ViewFrame)
    any other class, shape != null:
        [SDL record] else DrawSpriteScaled(&stSpaceBuffer, x, y, shape, ViewFrame, ScreenAngle, ScreenScale, Flip)
```

`DrawSpriteScaled` uses the asm-exact rotate/scale mapper for angle ≠ 0 or scale ≠ 0x100 or flip
(graphics.md §5.3, `Graphics.Shapes.RleRenderer`); frames above 0xFA00 pixels draw nothing on
that path.

**Planets.** In KS the PLANET class falls into the constellation branch: a planet is drawn as
constellation frame 0 (a dust dot) — planets are effectively invisible. The SDL port ("WCDX fix")
removes PLANET from that branch, so planets use their own shape (PLANETS.VGA section n+1, frame 0)
with the roll angle from `set_background_objects_rotation` and scale 0xFF, and the nav pointer
(also class PLANET) is drawn by `DrawSpriteScaled` with scale 0x100 and angle 0, i.e. pixel-identical
to the unscaled draw. **The port follows SDL** (the DOS game shows planets).

Sprites by class (shape origins: simulation.md §5.3):

| Object | Shape | Frame | Transform |
| --- | --- | --- | --- |
| stars (slots 42..48) | 12:0 | 32..37 (random at placement) | unscaled |
| dust (34..41) | 12:0 | `((counter + nSpaceFrame) & 3) + (angle & 0x10) + (3 − size) * 4`, size 0..3 from distance | unscaled; bit 0x10 = streak variant |
| planets (constellation slots) | 12:n+1 | 0 | roll angle, scale 0xFF (SDL path) |
| nav pointer | 8:0 | 3 | unscaled (distance 0x4A38) |
| fighters, missiles | type + 22 section 0 (missiles share the heat-seeker set) | `get_right_shape` 0..36 | angle, flip, distance scale |
| capital ships | type + 22 section = view frame (0..36) | 0 | angle, flip, distance scale |
| engine flames (THRUSTERS, re-created every rendered frame) | 3:0 | from the ship's exhaust table (randoms) | parent angle/flip/scale + offset |
| projectiles | 3:6 laser, 3:7 mass driver, 3:8 neutron (turret flak = laser set) | static (type data) | angle 0, distance scale |
| explosions, sparks, jump flash, debris, rocks, asteroids, mines, ejected pilot | 3:1/2/3, 3:9..12, 3:14, 3:4/5, 3:13, 3:16/17, 3:15, 2:2 | animation scripts | scale by distance and script |

There is no separate shield-hit graphic: a projectile that hits becomes a LASER_SPARK at double
scale moving with the victim (simulation); the player additionally gets the palette flashes of §3.9.

### 4.4 Space buffer lifecycle and composition

* `initialize_view_buffer` (0x427A00): if `bViewBufferEnabled` and no pixels: allocate the
  rectangle cleared to 0xBF (fatal "SPACE BUFFER" on failure). `GetScreenUpdateFlag` (0x4279D0):
  a message showing → `EndCommMenu`; free the buffer. `clear_view_buffer` (0x427B00): clear to
  0xBF. Size: geometry width x height in cockpit mode, 320x200 cockpitless (§3.4).
* Per rendered frame: objects → HUD → dump → transient erase → clear (flight loop); scripted
  sequences clear **before** drawing (`RefreshCockpitStatus`).
* `dump_buffer_to_screen` (0x427A40):
  * SDL: `SdlCompleteSpaceFrame()` first.
  * `bCockpitlessView > 0`: `CopyViewportContents(&stSpaceBuffer, &stScreen)` (top-left aligned,
    the whole 320x200).
  * geometry 4: `stScreen` temporarily (0,24)-(319,152), copy (lands on rows 24..151), restore.
  * geometry 5: copy.
  * otherwise `fizzle_fade(&stSpaceBuffer, &stScreen, geometry)`: for each run `(destX, screenY,
    length)` until −1: copy `length` bytes from buffer row `screenY − originY`, column `destX −
    originX` to screen row `screenY`, column `destX` (runs of 320-wide geometries continue into the
    next rows). Ported as `GraphicsContext.FizzleFade`.
  * then `ShowMemoryStatusDebug()` (`-m` switch: memory texts; not ported).
* **White flashes.** `warp`/`unwarp` (0x42AAF0 / 0x42AA10, ships jumping out/in; simulation AI)
  and `death_sequence` frame 7 clear the space buffer to 0x0F. In the flight loop the next draw
  happens on that white buffer (the clear is at the end of `RenderSpaceViewFrame`), giving a
  one-frame hyperspace flash with the objects on white. `bViewportDirty` is written but never read.
* The mouse crosshair, HUD brackets and HUD text live in the buffer (§3.5, §3.6); cockpit art,
  instruments and VDUs live on the screen.

### 4.5 The SDL "enhanced" GL renderer hooks (the precedent R2 replaces)

All behind `#ifdef SDL_PORT`; with the indexed backend they are no-ops.

| Hook | Call sites | Semantics |
| --- | --- | --- |
| `SdlBeginSpaceFrame(geometry, cScreenViewportMode, bCockpitlessView > 0, 0xBF)` | `Draw_3Space_Frame` after the sort; `ShowCarrierLaunchSequence` phase 1 | reset the recorded list and the software-fallback flag; layer offset = geometry origin unless cockpitless or geometry 5; window mask from the geometry runs (whole screen for cockpitless / geometry 5) |
| `SdlRecordSpaceSprite(&stSpaceBuffer, float x, float y, shape, frame, angle, scale, flip)` → 1 = recorded (software draw skipped) | `draw_sorted_objects_to_buffer`; `DrawTitleLogo` (3 parts); `DrawLaunchDoorFrame` (3 parts) | refuses (returns 0) when not recording, after a fallback, null shape, frame out of range, scale 0, invalid flip, capacity, empty frame, frame > 0xFA00 pixels while rotated/scaled/flipped, or atlas failure — **after the first refusal every later sprite of the frame is drawn in software** (painter order). Position: buffer coordinates + layer offset; clip = buffer rectangle + offset |
| sub-pixel position | `draw_sorted_objects_to_buffer` | objects (not NULL/FIXED, not the nav pointer, view z ≠ 0): if re-projecting `aObjectViewPosition` with `DivideFixed/MultiplyFixed` reproduces `ScreenX/Y`, use `center + (W & ~1) * 0.5 * view.x / view.z` in floating point, else the integer position |
| `SdlSetThrusterScreenPosition(obj, fx, fy)` / `SdlGetThrusterScreenPosition` | `reposition_fixed_child_objects` / draw | engine flames: float parent position plus the attachment offset rotated with the 0.1° table (`GetRLETransformTrig`) |
| `SdlCompleteSpaceFrame()` | start of `dump_buffer_to_screen` | the list belongs to the next present |
| `SdlCancelSpaceFrame()` | `RunSpaceFlight` exit | drop the list and the atlas, stop rumble |
| present (`SdlGlRendererPresent`) | every present | sprites are drawn over the indexed frame with sharp-bilinear palette lookup **only where the presented base pixel is 0xBF, inside the window mask and the clip**; the list survives re-presents of the same cursor-free base frame (cursor refreshes) and is dropped when the base changes |

Software drawing that the GL path never records — HUD brackets, gunsight, lock marker, crosshair,
HUD text, subtitles, scaled intro texts, carrier-sequence phases 2/3, ejection/death overlays,
fireworks — is non-0xBF and drawn after the objects, so the occlusion test reproduces the
original painter order. Known limitations: a software pixel of colour 0xBF is transparent to GPU
sprites; on a white warp-flash frame the base is 0x0F, so GPU sprites disappear for that frame
(the software path shows them on white); a paused frame with a modal panel is a new base, so the
GL port drops the sprites during pauses.

---

## 5. Non-interactive flight sequences

### 5.1 Building blocks

* `RefreshCockpitStatus()` (§1.4): one simulation tick, clear, `Draw_3Space_Frame`; no input, no
  `update_cockpit` unless the caller adds it.
* Typical sequence frame: `[PumpWindowMessages();] if (RefreshCockpitStatus()) { overlays into
  the space buffer; dump_buffer_to_screen(); [update_cockpit();] } ... DIBslam(); DIBslamReal();
  if (bEscapePressed == 1) break;` — the present (and the throttle) happens every iteration, also
  on skipped frames. Only `RunSpaceFlight` switches to the 50 ms flight interval and back to 62 ms
  on exit (`SetSpaceFlightFrameTiming`/`SetCinematicFrameTiming` have no other callers besides
  `GameMain`'s start-up), so every sequence outside the flight loop — launch, landing approach,
  landing, ejection, death, stranded, the simulator screens, the attract mode — runs at **16 fps
  with one simulation tick per frame**, while the autopilot cinema and the nav map run inside the
  loop at 20 fps.
* **Escape** is the latch `bEscapePressed`, set when the event manager processes an Esc key-down.
  Loops that never pump (`death_sequence`, `ejection_sequence`, `stranded_sequence`, the
  simulator's get-ready/victory/game-over screens, `visit_the_cinema`) cannot be skipped: the Esc
  is processed only at the next pump.
* `visit_the_cinema(view, obj, frames)` (0x403E50, auto.c): save `nOriginDevUnlock`,
  `bPlayerVulnerable`, `bPlayerCollisionResponse`; set 1, 0, 0 (invulnerable, no collision
  response); `force_view(view, obj)`; `frames` times `Update_3Space(); RenderSpaceViewFrame();
  DIBslam(); DIBslamReal();`; restore.
* Scripted camera: `initialize_scripted_view(script)` (0x42D230) → `bScriptedView = 1`, eye
  velocity 0, `init_ijk(61)`, parse, eye near plane 100; `parse_view_script` (0x42CDB0) commands
  until 14 (wait) or 13 (wait for eye goals): 0 x y z eye position; 1 yaw pitch roll; 2 v
  velocity = forward·v; 3 view `force_view(view, nScriptedViewObject)`; 4/5 pitch goal ∓/± a with
  rate; 6/7 yaw goal; 8 roll goal; 9 yaw pitch roll v add a rotated velocity; 10 copy the object's
  velocity; 11 copy its frame; 12 copy its position; 15 face the object; 16 m object = ship with
  mission index m; −1 end (`bScriptedView = 0`). 14 n waits n ticks; 13 waits while
  `(yawGoal == pitchGoal) != rollGoal` (sic). Simulation code (the C# simulation has
  `InitializeScriptedView`/`ParseViewScript`/`UpdateScriptedView`).

### 5.2 Scramble (`scramble`, 0x408200, brains.c) — 2D

Not the 3D engine: drawn into the 320x128 scene buffer (`InitializeConversationViewport`, screen
rows 24..151) by `DrawScrambleFrame` (0x407E10: frame skip, hangar halves, actors 0/3/4/2, open
canopy, per-ship cockpit parts, damage details, closed canopy overlay, `RefreshMemoryStatusOverlay`
= vblank wait + copy to the screen, present). Resources: cockpit:8 (scramble cockpit), 1:1 hangar
(2 halves), 1:2 canopy, 1:3 ship, 1:4 actors. 10 frames approach, 27 frames canopy opening, 23
frames climbing in (per-ship detail motion), `WaitForSceneAdvance(60)`; sfx 17 / 15 / 16; Esc
skips. Per-ship start positions: Hornet ship (−40,96) detail (−95,71) scale 316; Rapier (−30,80)
(−15,76); Scimitar (−40,86) (4,83); Raptor (−40,80) (−22,67).

### 5.3 Launch (`LaunchPlayerShip`, 0x42BA90, sound.c)

```
doors = {50, 40, 30, 20}; step = 1
spacetrack(changetrack(), 1, 0)                     // mission music (escort 18, strike 17, defend/rendezvous 16, else 15; home 12/13/14)
if !bEscapePressed:
    door shape 1:7; nCannedSceneMode = 1; force_view(0, 0) (cockpit set-up); sfx 20; nFrameSkipCounter = 1
    for frame in 0..24:
        PumpWindowMessages()
        if RefreshCockpitStatus():                  // the simulation ticks: the ship flies out
            for door in 0..3: DrawLaunchDoorFrame(doors[door]); doors[door] -= step
            dump_buffer_to_screen(); update_cockpit()   // instruments come alive
        present; Esc -> break
        if frame % 5 == 0: step++                   // step 1 at frame 0, 2 at frames 1..5, 3 at 6..10, ...
    Esc -> StopMusicUnlessSuppressed(); spacetrack(changetrack(), 1, 0)
else force_view(0, 0)
present; clear_view_buffer(); nCannedSceneMode = 0; ResetSoundState(); bEscapePressed = 0
```

`DrawLaunchDoorFrame(distance)` (0x42B9A0): distance > 10 → scale = 0x1A00 / distance; bounds of
frame 1 at (W/2, H/2) of the space buffer; frame 0 at (bounds.left − 1, H/2), frame 1 at (W/2,
H/2), frame 2 at (bounds.right, H/2), angle 0. It runs before `RunSpaceFlight` (and therefore
before `set_up_action_sphere`), with the player's ship flying for 25 ticks.

### 5.4 Landing approach (`ShowCarrierLaunchSequence(sceneObject)`, 0x42BC00, sound.c)

Called by `GameFlow` after state 1 with the carrier slot, after `free_cockpit` (despite its name it
shows the **landing**). `bIntroSceneResourcesActive = 0`; free ship slots 1..3,
`remove_nav_point_objects`, `ResetSoundState`, music 28; carrier 1:8, actors 1:4; fighter = the
player type's shape set; scripted view `asCarrierLaunchViewData + 2` on the carrier ({12 copy
position, 11 copy frame, 1: yaw 90 pitch 90, 9: −90 0 0 20, 3 15 view 15 (letterbox without
backdrop), −1}).

1. 100 frames: classes of slot 0 and the carrier NULL during the simulation calls;
   `set_eye_direction_and_position`; on rendered frames: `nRenderedSpaceFrame++`,
   `UpdateSpacePaletteFade`, clear, `house_keep_objects`, `update_objects_in_space`,
   `transform_objects_to_your_view`, `update_star_field`, then hand-set screen data (fighter: x 20
   +2/frame, y 64 ± `asCarrierLaunchApproachDeltaX` (frames 0..47), view frame
   `acCarrierLaunchApproachFrames` (36/32/25/18), screen scale `(scale << 4) / (20 + 2·frame)`,
   distance 300 +10/frame; carrier frame 3 at (520 − 2·frame, 64), distance 2000), classes SHIP,
   `sort_object_depth`, (SDL begin), `draw_sorted_objects_to_buffer`, dump. Every frame:
   `nSpaceFrame++`, eye += eye velocity, present. Pumps; Esc skips everything.
2. 35 frames (eye = carrier frame, `alter_yaw(-1)` per frame, actors configured, sfx 18):
   `RefreshCockpitStatus`, carrier halves (frames 0/1), actor 0, fighter (shape set frame 16,
   scale `0x6000 / d`, d 100 → 66), actors 3/4/1 and sprites 16/8, carrier foreground frame 2,
   vblank wait, dump, `PaletteFadeHook` (no-op), present.
3. Up to 50 frames: as 2 with the fighter on `aCarrierLaunchFighterPath` (frames 1..8) and
   `asCarrierLaunchFighterDeltaY` (9..22), sfx 11 at 9, sound flush + sfx 19 at 23.

End: Esc latch cleared, `ResetSoundState`, music stopped/released, `free_ship(0)`, shapes freed,
`bScriptedView = 0`, `bIntroSceneResourcesActive = 1`.

### 5.5 Landing (`landing(damageLevel)`, 0x408650, brains.c) — 2D

Damage level = `calculate_damage_level()` (0..3, simulation). `anLandingDamageDetailCounts` {0, 8,
16, 24} damage details are chosen with `RandomInRange(0, 31)` without repetition (rejection
sampling: **random draws**). Resources cockpit:8, 1:1, 1:3, 1:4, 1:9 details, 1:5 overlay, 1:6
closed canopy; music 29 (0x1D); joystick button pump. 30 frames taxiing (ship up 2/frame), 30
frames canopy opening (canopy offsets from `apLandingCanopyFrames[damage]` from frame 7, overlay
moving; `if (nRenderedSpaceFrame == 29) nFrameSkipCounter = 1` — a typo for the frame counter,
harmless with frame skip 1), then the comment `apszLandingDamageComments[damage]` (font 0, blue, at
(0,160) in the conversation text area) and `WaitForSceneAdvance(300)`. Esc skips.

### 5.6 Autopilot (`auto_pilot_sequence`, 0x404050, auto.c)

```
destination = position of the current flight-path objective
if !auto_pilot_valid(1): return     // "Already Near" (< 8000), "Enemy Near" (Kilrathi < 16000), "Hazard Near" (slot 1, yellow, 3)
leave = the objective is not inside the current nav sphere (+25)
clean_up_cockpit(); ResetSoundState()
ships 0..9: speed 0, velocity 0; living Confed ships: team members without Kilrathi within 10000 travel along
            (special NONE, goals and rates 0; counted for the formation unless the wingman); other Confed ships are
            removed when leaving the sphere
player: point_at(destination), set_speed(60); travelling ships: in formation (auto_position, player's frame and speed)
        when near (< 20000) or the wingman; far ones keep their own destination (modes 2/3)
nCannedSceneMode = 4
visit_the_cinema(12, 0, 120)        // 120 presented frames of view 12 (autopilot camera, §3.4); the ships fly at speed 60
while nCannedSceneMode == 4:        // instant: no frames, the player jumps 400 units (0x19000) per step
    ReleaseStaleNavTarget(); check_hazards()
    stop when < 1000 from the destination, a hazard field is active, a non-travelling ship is within 4000,
         or report_kilrathi_rout(1)
step back once; every traveller to the slowest cruise speed; formation/destinations re-applied
Update_3Space()
force_view(0, 0) (cockpitless: buffer dance, bCockpitlessView restored) and the mouse to the centre
```

`HandleSpaceFlightControls` adds the VDU switch before (`SelectCockpitVduMode(1, 5)` unless
already) and the input flush after. The travel logic is simulation (phase 3); the cinema is a UI
coroutine — the simulation must expose the parts before and after it (§6.3, §7.2).

### 5.7 Ejection (`ejection_sequence`, 0x4046A0, cmpgn.c)

```
free_all_slots(); free_cockpit(); music 31 (0x1F); new_view(9, 0)                 // full-screen geometry 5
rear cockpit art = cockpit:3 (loaded again), seat shape 2:1; sfx 0x21; y = 199, speed 4
10 frames: if RefreshCockpitStatus(): rear art at (0,0) into the space buffer, seat frames
           {0,1,1,3,3}[min(f,4)] (+ {−1,−1,2,−1,4}) at (160, y), frame 5 at (160, y+1); dump
           after frame 1: y -= speed, speed = min(speed + 4, 20); Esc -> break; present
not escaped:
    pScreenViewportPacket = 8:8 (one geometry: 299x106 at (8,34)); ejected-pilot shape set 2:2;
    pilot object (type 56, counter 32000) at the player's position and frame, velocity = player velocity − up·5;
    new_view(10, pilot)                                   // pod interior 8:7, geometry 0 of 8:8
    front art cockpit:0 and the eject canopy cockpit:5; sfx 0x22; y = 40
    10 frames: RefreshCockpitStatus -> front art at (0, y), canopy at (0, y−1) into the buffer, dump; y += speed; Esc
    not escaped: load_all_slots(); eye 600 below the pilot looking up (right = player right, up = −player forward);
                 scripted view {3,11, 14,70, 3,10, 14,80, 3,4, −1} (follow 70 ticks, ride 80, chase);
                 loop: alter_pitch(4, pilot); RefreshCockpitStatus -> dump; frame 10: Explosion(0) (the ship blows up) + sfx 4;
                 until frame > 200 or Esc; present
end: Esc latch 0; bScriptedView = 0; template packet released; FadeViewportPaletteToColour(&stScreen, black);
     clear the screen; present; RestoreGamePalette(); free_all_slots(); music stopped
```

`GameFlow` then runs `check_stranded` (simulation) and, when stranded, `stranded_sequence`.

### 5.8 Stranded (`stranded_sequence`, 0x404BE0, cmpgn.c)

`nCannedSceneMode = 1`; `free_cockpit`; `force_view(13, 0)` (camera pulls away from the player
toward the carrier's position, letterbox); intro font 9:1; 400 frames of `RefreshCockpitStatus` →
from frame 160 `print_subtitle("\nWith your carrier\ndestroyed, you drift\nendlessly through\nthe
void...")`, from 300 "THE END" → dump; present. End: `free_all_slots`, screen rectangle reset,
fade to black, clear, `RestoreGamePalette`.

### 5.9 Death (`death_sequence`, 0x439660, screens.c)

`nCannedSceneMode = 1`; `free_all_slots`; `free_cockpit`; music stopped, music 32; death shape 2:0;
rear cockpit art cockpit:3; sfx 4; `new_view(9, 0)`; 8 frames: frame 7 clears the space buffer to
**white**, frames 0..6 `RefreshCockpitStatus()` (result ignored) + rear art at (0,0); death frame
`f` at (160,199); dump; present. Not escaped: `load_all_slots`; `new_view(4, 0)` (chase camera on
the player's slot); 60 frames `RefreshCockpitStatus` → dump, frame 2 `Explosion(0)`; present. End:
`free_all_slots`, fade to black, clear, `RestoreGamePalette`. `GameFlow` then runs the funeral.

### 5.10 Training simulator

* `RunTrainSim` (0x427080, system.c; Rooms work stream): `cCockpitView = 4`, cockpit LF 21; per mission
  0..3: `FigureArcadeTime` (`nArcadeTimeRemaining = (nArcadeWave + 6) * 400`), `init_mission(0,
  m)`, `ShowGetReadyScreen`; first session of a new campaign (`bCampaignStartupMode`): a rigged
  flight (shields and maximum shields 0, shield generator destroyed, core damage capacity + 1,
  `nCurrentWave = 2`, `set_up_next_wave` (starts a 30-frame wave-bonus countdown), then
  `nArcadeTimeRemaining = 25`: the flight ends after about 55 rendered frames at the latest);
  `InvalidateVduMode(0/1)`; present; `bKeyEventQueueEnabled = 1` around `RunSpaceFlight(
  nArcadeWave)`; result 1 → next mission (victory screen after mission 3), anything else →
  `nArcadeState = 4`, game over.
* `ShowGetReadyScreen` (0x439840): intro font; `nCannedSceneMode = 1`; `force_view(0, 0)`; 40
  frames `RefreshCockpitStatus` → `DrawCenteredScaledIntroText("Get Ready", nViewCenterX,
  nViewCenterY, 0xC800 / distance)` (distance 400 → 100 in steps of 10) → dump; present; clear;
  `nCannedSceneMode = 0`; `ResetSoundState`.
* In flight: `UpdateArcadeScoreDisplay` (0x429EE0, rendered frames, before the dump): HUD context,
  `DrawArcadeScorePanel(10, 10)` = "%X%YScore: %s0 %XTime: %u %X1 UP" at x 10, 140, 200 (the score
  printed with a literal trailing '0'); without a bonus countdown `nArcadeScore++`,
  `nArcadeTimeRemaining--`, below 1 → `nArcadeState = 4`; during the countdown "Wave %d
  complete.\n\nBonus Points: %s0" or "Mission %d complete.\n\nBonus Points: %s0" (format buffer +
  `%P`) at (left, (top+bottom)/2 − 5). Wave end (simulation `set_up_next_wave` →
  `ISimulationEvents.TrainSimWaveCleared`): music cue 21, countdown 60 (30 when more waves follow),
  `GetArcadeBonus` = `(time·(mission+1) + (mission + (wave·5+5)·2)·50)·2`, `FigureArcadeTime`.
  The countdown runs in `RenderSpaceViewFrame` (§1.4).
* `ShowVictoryScreen` (0x439910): fireworks (9:17; each frame `RandomBelowOrEqual(7) == 0` and a
  free slot → spawn at `RandomInRange(0, right)`, `RandomInRange(0, bottom)`, variant
  `RandomInRange(0, 2)`), "Victory" zoom (500 → 100), 80 frames. `ShowGameOverScreen` (0x439A80):
  `cViewObject = Explosion(0)`, chase camera 300 behind the explosion, `generate_stars`, music 22,
  80 frames, "Game Over" after frame 20.
* Differences in flight: cockpit 4 (one geometry, no side views), right VDU starts on the target
  page, the first key switch is disabled (§2.8), Esc ends the flight, no comm chatter
  (`npc_communication`, `send_message`), no damage decals, no component messages, no damage alarm,
  no stranded check, `house_keep` skips nav-sphere and hazard checks, simulator music (track 20).

### 5.11 Attract mode (first part of `Title_Sequence`, 0x40FB70, nav.c)

```
if !bEscapePressed:
    music 23 preloaded; pump PollJoystickButtonEvents; bIntroSceneResourcesActive = 0; init_3Space_objects(0) (no planets);
    nCannedSceneMode = 2 (canned AI); intro font 9:1; intro resources 3:2, 3:5; key state cleared; flush
    loop until escaped (CheckEscaped: key, button or Esc queued):
        PumpWindowMessages(); mission records 32..45 alive; titleDistance = 200; remove_all_hazards()
        bIntroSecondaryScene = 0; set_up_action_sphere(16); title shape 9:0; spacetrack(23, 2, 1)
        initialize_scripted_view(asIntroCameraSequence)    // {0: eye (−1000, 0, −4263); 2 15; 1 0 30 0; 3 15 (letterbox
                                                         //  geometry, no backdrop); 4 30 1 (pitch goal −30); 13; 14 400; −1}
        A, 25 ticks: Update_3Space; rendered -> print_subtitle("In the distant future,\nmankind is locked in a deadly war...");
                     dump; present; intro_drawbackgroundships(); CheckEscaped
        clear_view_buffer()
        B, 110 ticks: Update_3Space; RenderSpaceViewFrame; CheckEscaped; present
        C, 100 ticks: Update_3Space; rendered -> DrawTitleLogo(distance, nViewCenterY − 6); dump; present; clear;
                      distance 200 -> 16 in steps of 4; CheckEscaped
        title shape released; eye velocity = eye forward · 150; set_up_action_sphere(17); bIntroSecondaryScene = 1;
        player rotation 0; start_hazard_field(0) (asteroids)
        each credit card (`nIntroCreditCount`: 11, 20 with "$#SAGA.EXE"): 70 ticks of Update_3Space, rendered ->
            print_subtitle(card), dump, present, clear; CheckEscaped every tick; then 40 ticks RenderSpaceViewFrame + present
        D, 150 ticks: RenderSpaceViewFrame + present
    music stopped, sounds reset, fonts/shapes/intro resources freed, free_all_slots, free_3Space,
    nCannedSceneMode = 0, bScriptedView = 0, bIntroSceneResourcesActive = 1
then the title menu (ported: Screens/TitleSequence)
```

* No `update_cockpit` and no input handling: the attract mode consumes randoms only through the
  simulation (canned AI, stars, exhaust, hazards, explosions).
* `print_subtitle(viewport, colour (ignored), text)` (0x403920): lines 16 pixels apart, the block
  centred vertically in **128** rows (the letterbox buffer), each line centred in 320 using
  `GetLineLength` (glyph right extent + 2, space 6, '.'/',' frames 58/59); ported as
  `GraphicsContext.PrintSubtitle`.
* `DrawTitleLogo(distance, y)` (0x40FA40): distance ≤ 10 → nothing; scale = 0x1000 / distance;
  bounds of frame 1 at (nScreenWidth/2, y); frames 0 / 1 / 2 at (bounds.left − 1, y) / (W/2, y) /
  (bounds.right, y), `DrawSpriteScaled(angle 0, scale)`.
* `intro_drawbackgroundships` (0x436650): erases instead of clearing: every slot with a shape (until
  a negative type) → `DrawSolidColourSpriteScaled(asObjectDrawX/Y, frame, angle, scale, flip,
  0xBF)`; STAR/DUST (KS also PLANET) → `DrawSolidColourSprite(constellation or nav pointer shape,
  0xBF)`. The subtitle drawn into the same buffer survives.
* `DrawCenteredScaledIntroText(text, cx, baselineY, scale)` (0x4037A0, simulator screens): x = cx −
  width/2 (`MeasureScaledIntroTextWidth`), y = baselineY − (scale·16 >> 9); 'A'..'z' →
  `DrawSpriteScaled(intro font, c − 'A', angle 0, scale)`, advance bounds.right + 1 + (scale·2 >>
  8); space advances scale·6 >> 8; stops at '\n'.
* Credits: `apszIntroCredits[20]` (nav.c 0x468A38) holds 11 DOS cards, 8 Saga cards ("Windows 95
  Team" .. "Special Thanks To") and a null terminator. `nIntroCreditCount` starts at 11 and
  `Title_Sequence` adds 9 when `bShowKilrathiSagaCredits` ("$#SAGA.EXE" in WINGCMDR.CFG or on the
  SDL command line) is set — **on every call**, and `GameMain` calls it once per return to the
  title. As reconstructed, the 20th card is the null entry (`print_subtitle(NULL)` would fault) and
  a second attract run reads past the array. The port shows 11 cards, or 19 with the Saga option,
  and computes the count once (see §8.2).
* The canned dogfight (mission records 32..45 at nav points 16/17, command streams
  `asCannedSequence32..`) is simulation data (`IntroMissionData`) and phase-3 AI
  (`update_canned_sequence`).

---

## 6. Interface between the flight UI and the simulation

Simulation API names refer to the C# `SpaceSimulation` (phase-2 state of 2026-10-07; "phase 3"
marks AI/objective code that is not ported yet).

### 6.1 UI → simulation

| Purpose | C | C# | Called from |
| --- | --- | --- | --- |
| mission set-up | `init_mission` (incl. `prepare_mission`) | `InitMission(series, mission)` (raises `InitializeCockpit`) | flight entry (`FlyMissionAsync`), simulator |
| action sphere | `set_up_action_sphere` | `SetUpActionSphere(nav)` | `RunSpaceFlight` entry, attract mode (16, 17) |
| stick input | `players_flight_dynamics` | `PlayersFlightDynamics(pitch, yaw, roll)` | every tick |
| key actions | `accelerate`, `anShipSpeed[0] = 0`, `your_afterburner`, `fire_players_lasers`, key 0x1C body, `nTargetLockMode ^= 1` (+ sfx 0x19), eject roll | `Accelerate(n)`, `ZeroPlayerSpeed()`, `YourAfterburner()`, `FirePlayersLasers()`, `PlayerReleaseWeapon()`, `ToggleTargetLockMode()`, `TryEject()` | §2 |
| weapon / target selection | `select_new_gun`, `select_new_release_weapon(-1)`, `cycle_onscreen_targets` | `SelectNewGun`, `SelectNewReleaseWeapon`, `CycleOnscreenTargets` | VDU keys |
| camera | `new_view`, `force_view`, next/previous external view object, view-8 keys (eye rotations, `nCapitalShipViewDistance`, `cViewObject--`), `bMissileCameraEnabled`, `nFrameSkip` | `NewView`, `ForceView`, `ViewObject`, eye basis, `CapitalShipViewDistance`, `MissileCameraEnabled`, `FrameSkip` | F-keys, §2.6 |
| tick | `Update_3Space` | `Update3Space()` | flight loop, sequences, autopilot |
| view preparation | simulation half of `Draw_3Space_Frame` | `PrepareSpaceView()` (false on skipped frames) | `RenderSpaceViewFrame`, `RefreshCockpitStatus` |
| cockpit simulation | `check_target`, `repair_internal_damage`, `update_objective_location` | `UpdateCockpitSimulation()` | start of `update_cockpit` |
| stranded | `check_stranded` | `CheckStranded()` | end of `update_cockpit`, `GameFlow` after ejection |
| landing damage | `calculate_damage_level` | `CalculateDamageLevel()` | before `landing` |
| objectives / nav | `set_new_objective`, `cycle_next_objective`, `hidden_objective`, `visited`, `achieved`, `sighted`, `objective_name`, `LocateMobileObjective`, `set_objective_range`, `objective_lost` + `update_objective_location` (check_objectives), `find_objective`, `flag_objective`, `SetScale`, `nav_getxy`, `ScaleNavMapCoordinates`, `ScaleNavMapMarkerSize` | same names (phase 1/3) | nav VDU, scanner, nav map, flight exit |
| queries | `missile_on_tail`, `any_enemy`, `kilrathi_near`, `evaluate_damage`, `unactive`, `auto_pilot_valid`, `distance_from_point`, `report_kilrathi_rout` | `MissileOnTail`, `AnyEnemy`, `KilrathiNear`, `EvaluateDamage`, ... | lights, comm menu, keys |
| comm orders | `request(0, ship, command)` | phase 3 | comm menu, H, B |
| autopilot | `auto_pilot_sequence` travel parts | phase 3, split around the cinema (§6.3) | A |
| sequences | `Explosion(0)`, `free_all_slots`, `load_all_slots`, `free_ship`, `remove_nav_point_objects`, `initialize_scripted_view`, `set_eye_direction_and_position`, `house_keep_objects`, `update_objects_in_space`, `transform_objects_to_your_view`, `update_star_field`, `sort_object_depth`, `generate_stars`, `alter_yaw/pitch`, `copy_frame`, `find_vacant_3d_object` + `set_objects_data` (ejected pilot), `start_hazard_field`, `remove_all_hazards`, mission-record states 32..45 | same names | §5 |

### 6.2 Simulation state read by the UI

* Per object (`Objects[i]`): `Class`, `Type`, `ScreenX/Y`, `ScreenScale`, `ScreenAngle`, `Flip`,
  `ViewFrame`, `Shape`, `Distance`, `DrawX/Y` (written by the UI), `ViewPosition` (scanner),
  `Position`/`Velocity` (readouts, nav map, autopilot), `Counter` (dying speaker static), rotation
  rates of slot 0 (lock marker), the eye slot 61 (camera for R2 snapshots).
* Per ship (`Ships[i]`): `Side`, `Rating`, `Target`, `Shield`/`MaxShield`, `Armor`, `WeaponEnergy`,
  `Fuel`, `Weapons` (loadout), `SpecialManeuver`, `Objective`, `MissionType`, `WingmanMessageState`
  (`acWingmanMessageState`, written by `npc_communication`), `CapitalShipViewFrame`.
* Globals: `CameraViewMode`, `ViewObject`, `ArcadeState`, `TargetLockCountdown`, `TargetLockMode`,
  `TargetLockAcquired`, `TargetLockMarkerAngle` (read and advanced by the HUD),
  `TargetLockReadoutDirty`, `SelectedGunType`, `SelectedReleaseWeaponIndex`,
  `PlayerComponentDamage`, `YourWingman`, `AutoEngageTimer`, `RadioSilence`, `LandingAuthorized`,
  `NavPointerObject`, `ExternalViewShip`, `SortedObjects`, `SpaceFrame`, `RenderedSpaceFrame`,
  `CurrentObjective`, `CurrentNavPointIndex`, `CurrentObjectiveRange`, `MissionObjectives`,
  `MissionObjectiveCount`, `FlightPath`, `MissionNavPoints`, `MissionShips`,
  `CarrierMissionShipIndex`, `PlayerMissionShipIndex`, `MissionAuxData`, `SeriesAuxData`,
  `HazardFields`, `ActiveHazardField`, `CurrentWave`, `CannedSceneMode`, `ScriptedView`,
  `TrainSimActive`, `ArcadeScore`, kill counts and objective flags (post-mission).
* The UI writes into the simulation: `ScreenWidth`, `ScreenHeight`, `ViewCenterX/Y` (inside
  `InitializeCockpitView`), `CockpitView`, `CockpitlessView` (−2/0/1), `FrameSkip`,
  `CannedSceneMode` (sequences), `MissileCameraEnabled`, `ArcadeState` (simulator wave end and time
  out), `ArcadeScore` (+bonus), `WingmanMessageState` (cleared), the eye basis (view-8 keys),
  `PlayerVulnerable`/`PlayerCollisionResponse` (cinema), mission-record states (attract mode).

### 6.3 Simulation → UI callbacks

`ISimulationEvents` (src/WingCommander.Simulation/ISimulationEvents.cs), all synchronous at the
original statement position; the Flight layer implements them:

| Callback | UI action |
| --- | --- |
| `PlaySoundEffect(effect, source)`, `ReleaseSoundSource(source)` | `SoundEffectManager` |
| `WeaponSelectionChanged()` | weapons page showing → `InvalidateVduMode(0)` |
| `DestinationChanged()` | `InvalidateVduMode(1)` |
| `ClearHudGunReadouts()` | clear both message slots without drawing |
| `InitializeCockpit(cockpitMode)` | `InitializeCockpitResources` (§3.1) |
| `TrainSimWaveCleared(waveActive)` | music cue 21, bonus countdown 60/30, `GetArcadeBonus`, `FigureArcadeTime` |
| `InitializeCockpitView(mode)` | `initialize_cockpit(mode)` incl. the cockpitless rules and the immediate present of mode 4 (deferred, §7.2); sets `ScreenWidth/Height`, `ViewCenterX/Y` |
| `ServiceTrack(spaceFrame)`, `NewSpaceMusicChanges(attacker, victim)` | `MusicDirector` (consumes randoms) |
| `HouseKeepCockpit(cameraViewMode)` | view 0: fade 185..190; else release the damage alarm and light 3 (dead in KS: no handle is ever stored, §3.7) |
| `AfterburnerExpired()`, `PlayerAfterburnerEngaged(frame)` | afterburner sound |
| `TriggerPlayerHitPaletteFlash()` | `FlightPaletteEffects.TriggerPlayerHitPaletteFlash(cameraViewMode)` |
| `FlashCockpitPaletteEntry(entry)` | R = 0x38 for entry 185 + n |
| `PlaceDamageOnCockpit(damage)` | §3.10 |
| `ShowComponentHitHudMessage(message, component)` | left message slot (§3.8) |
| `VduMalfunction(vdu, sound)` | `vdu_malf` |
| `SelectCockpitVduMode(vdu, mode)` | §3.8 (randoms through `Malf`) |
| `ShowMissileLockedMessage()`, `RemoveMissileLockedMessage()` | right message slot |
| `PlayerReleaseWeaponLaunched(type, hardpoint)` | weapon launch animation |

Query interfaces: `ICockpitState.GetVduMode(vdu)` (VDU modes decide the nav pointer slot and
messages: gameplay-relevant), `IShapeBounds.GetTransformedShapeBounds` (space buffer bounds for
`easy2see`; the UI resolves `ShapeRef` and calls `Graphics.Shapes.ShapeBounds`).

Still missing (needed when phase 3 lands):

* `warp`/`unwarp` (hudmsg.c 0x42AAF0/0x42AA10): `ClearViewport(&stSpaceBuffer, 0x0F)` at the
  statement position → a `SpaceBufferFlash()` callback (white hyperspace flash, §4.4).
* `flag_reached` / `auto_pilot_valid(1)` messages ("Wait for %s", "Objective Reached", "Already
  Visited", "Already Near", "Enemy Near", "Hazard Near") → a cockpit-message callback (right slot).
* `auto_pilot_sequence`: the travel logic is simulation, the 120-frame cinema is a UI coroutine;
  split into `BeginAutopilot()` (returns false when refused) → UI `visit_the_cinema(12, 0, 120)` →
  `AutopilotTravel()` (the instant loop, `nCannedSceneMode` 4 → restored) → `EndAutopilot()`
  (speeds, formation, one `Update3Space`), then the UI's `force_view(0, 0)` and mouse reset.
* `npc_communication`, `vid_equiv`, `real_vid_transmit`: recommended as **UI** code (they read
  `message_showing`, the comm speaker and the VDU mode and draw), writing
  `Ships[].WingmanMessageState`; `send_message` stays simulation (pure state). The randoms they draw
  must stay at the end of `update_cockpit` (§6.4).
* `request(...)` (comm orders) and `can_land`/`cleanup_objectives`: simulation, called by the UI.

### 6.4 Shared `CRandom` draws in tick order

The UI adds or triggers these draws (simulation-internal draws are listed in simulation.md):

| Position in the tick | Function | Draws | Condition |
| --- | --- | --- | --- |
| input | `accelerate` (throttle keys −, =, KP−, KP+, `\`; mouse right-button mode per move event; joystick button 2 per sample) | `malf(0)`: 1 | each call |
| keys | `your_afterburner` (Tab, KP*, mouse/joystick double click) | `malf(0)`: 1 | fuel > 0 |
| keys | `SelectCockpitVduMode` (W, G, D, T, C, N, A, firing with a target while navigation shows) | `malf(3)`: 1, plus `malf(4)` for C when `malf(3)` passed | camera view 0 |
| keys | Ctrl+E | `RandomInRange(0, damage[7])` | ejector not destroyed |
| keys | F1..F7, F9, missile launch with the missile camera on, autopilot end, nav map exit (`new_view` → `generate_stars`) | simulation: per dust slot (8) `RandomInRange(0,1400)`, two `signed_random` (right-to-left argument order), 6 for the velocity, 2 for streak/frame; per star (7) 3 — up to 109 draws | view actually changes |
| keys | comm choice, H, B (`request`) | simulation (phase 3) | — |
| tick | `Update3Space` incl. `ServiceTrack` | simulation, music director | — |
| view | `PrepareSpaceView` (stars, exhaust, hazards, `TargetLocking`: `start_lock`, `malf(5)`) | simulation | rendered frames; lock only in view 0 |
| cockpit | `check_target` | `malf(5)` | lock mode and `nRenderedSpaceFrame % 8 == 0` |
| cockpit | `repair_internal_damage` | simulation | — |
| cockpit, view 0 | `update_missile_warning` → `spacetrack(3, 1, -1)` | music director call (check its draws) | missile on tail, not simulator |
| cockpit, view 0 | `vid_transmit` (right VDU mode 6) | `RandomInRange(0, 2)` once per session + `RandomInRange(0, 3)` | drawn frames: cockpitless or odd `nRenderedSpaceFrame`, portrait loaded, images not suppressed, speaker not dying |
| cockpit, view 0 | `npc_communication` | `RandomBelowOrEqual(5000)` every call; then `RandomBelowOrEqual(100)` per generic engaging Kilrathi, `RandomBelowOrEqual(2)` for the chosen taunt | not canned, not simulator |
| sequences | `landing` damage details | `RandomInRange(0, 31)` with rejection | damage level ≥ 1 |
| sequences | simulator victory fireworks | `RandomBelowOrEqual(7)` per frame, 3 more per spawn | — |
| sequences | `Explosion(0)`, simulation ticks of every 3D sequence | simulation | — |

Consequences: UI state changes the random sequence — the camera view (cockpit block only in view
0), the right VDU mode (comm video), `bVideoImagesSuppressed` (V), cockpitless mode (portrait every
frame), key and mouse input (malfunction tests). A replay must therefore record input at tick
granularity and reproduce the UI exactly.

### 6.5 Render-rate dependent gameplay

Frame skip (§1.5) exists, so these depend on rendered frames rather than ticks: star/dust/exhaust
randoms, hazard slot scheduling (`% 20`), ship damage sparks (every 4th rendered frame), the
missile-lock countdown (`target_locking` per rendered frame), the nav pointer slot, the target
VDU refresh (`% 8`) and lock-mode malfunction check, light 4 (`% 4`), comm portrait frames (`% 2`),
the HUD message timer, the simulator's time, score and wave bonus. A port that renders at display
rate must keep a "rendered space frame" counter that advances exactly when the original would
have drawn (§7.4).

---

## 7. Proposed C# design

### 7.1 Placement

```
src/WingCommander.Game/Flight/
  FlightSession.cs           one mission: owns the SpaceSimulation (shared CRandom, campaign record), the cockpit/HUD/comm
                             state, the space buffer; implements ISimulationEvents, ICockpitState, IShapeBounds and Audio's
                             IFlightMusicState / IFlightSoundWorld
  SpaceFlight.cs             RunSpaceFlightAsync (the flight coroutine), entry/exit (§1.2, §1.6), RenderSpaceViewFrame,
                             RefreshCockpitStatus, UpdateCockpit (UI half of update_cockpit)
  Input/FlightInput.cs       player_input, process_player_input, get_player_input (joystick pump), view-8 keys, mouse buckets
  Input/FlightControls.cs    HandleSpaceFlightControlsAsync (key table §2.5; ValueTask, modal paths awaited)
  View/SpaceBuffer.cs        stSpaceBuffer surface (IndexedSurface + Viewport), size rules, clear, white flash, dump (FizzleFade,
                             letterbox copy, full copy)
  View/ScreenViewport.cs     set_up_screen_viewport: geometry selection (PCSHIP section 6, built-ins 4/5, COCKPIT 8:8),
                             cockpitless offsets; writes ScreenWidth/Height/ViewCenterX/Y into the simulation
  View/SpaceViewRenderer.cs  draw_sorted_objects_to_buffer, intro_drawbackgroundships, ShapeRef -> ShapeTable resolution
                             (incl. capital-ship frames and the nav pointer shape)
  View/SpaceSpriteRecorder.cs R2: records sprites, builds Core.Rendering.SpaceView (mask, clip, background index), binding to presents
  Cockpit/CockpitResources.cs LoadSpaceflightResources / InitializeCockpitResources / free_cockpit as typed resource handles
  Cockpit/CockpitLayout.cs   static readonly tables of §3.2 (+ decal positions, comm texts)
  Cockpit/CockpitPicture.cs  initialize_cockpit (InitializeCockpitView), palette reset, damage decals, cockpit explosion
  Cockpit/Hud.cs             overlay_head_up_display, draw_target_box, gunsight, crosshair, RestoreTransientCockpitGraphics
  Cockpit/HudMessages.cs     HUD message line, check_message, ShowOnScreenMessage (modal variant async)
  Cockpit/Instruments.cs     lights, bars (vdu_polygon), readouts, scanner, pilot hand, weapon launch animation
  Cockpit/Vdus.cs, VduPages.cs  mode stack, update_VDUs, SelectCockpitVduMode, malfunction static, message slots, the pages
  Comm/CommSystem.cs, CommMenu.cs  speech table (COMMUNIC.DAT), portraits, real_vid_transmit, vid_transmit, npc_communication,
                             EndCommMenu, recipient/command menus
  NavMap/NavMapRenderer.cs   BuildMap, markers, label placement, DrawNavLocationReadout (shared with the briefing map)
  NavMap/InflightComputer.cs the modal nav map coroutine
  Sequences/                 LaunchSequence, LandingApproach (ShowCarrierLaunchSequence), ScrambleScene, LandingScene,
                             AutopilotCinema, EjectionSequence, StrandedSequence, DeathSequence, TrainSimScreens (get ready,
                             victory, game over, score panel), AttractSequence (Title_Sequence intro), SequenceHelpers
```

Elsewhere:

* **Graphics** (exists): raster primitives, `RleRenderer` (asm-exact rotation), `ShapeBounds`,
  `FizzleFade`, `FlightPaletteEffects`, `PrintSubtitle`, `ViewGeometry(Set)`, `SnowViewport`,
  `SpriteBackground`, `ShapeFrameDecoder`. Add: `DrawCenteredScaledIntroText` /
  `MeasureScaledIntroTextWidth` (mono.c) next to `PrintSubtitle`; the SDL xorshift static as an
  option of `SnowViewport` (own seed, never `CRandom`). `ModalTextPanel` comes from the Rooms
  work stream (`Screens/Ui`).
* **Simulation**: everything tick-based, including the camera (`NewView`, scripted views), star
  field, projection, sort, target lock/selection, damage, `CheckStranded`; phase 3: AI,
  `request`, the autopilot split and the missing callbacks of §6.3; a
  `FillSnapshot(SpaceViewSnapshot)` at the end of `PrepareSpaceView` for R2b/R3.
* **Core.Rendering** (exists): `SpaceView`, `SpriteDrawList`, `SpriteImageCache`, `SpaceViewMask`,
  `SpaceViewSnapshot`. Add: a renderer capability (`IRenderer.SupportsSpaceView`) that the exe
  forwards to the game (e.g. `GameRuntime.SpaceViewAvailable`), and the binding of a `SpaceView`
  to the presented classic frame (§7.4).
* **Audio** (exists): `MusicDirector` (`InitInflightMusic`, `ServiceTrack`, `SpaceTrack`,
  `ChangeTrack`), `SoundEffectManager`; Flight implements `IFlightMusicState`/`IFlightSoundWorld`.
* **Game.Input** (exists): `EventManager` already reproduces the queue, VK duplicates, the
  dropped head event (`PollInputEvent` + `TranslatePolledInputEvent`), key repeats, wheel,
  `PollKeyboardState`, the SDL Ctrl quirk and `WaitForKeyAcknowledgeAsync`. Missing: the joystick
  layer (`UpdateInputDeviceTransitions`, calibration, `j.cal`, double clicks).
* **Screens**: `GameFlowScreens.Flight.cs` delegates to Flight (§7.7); `TitleSequence` calls
  `AttractSequence`.

### 7.2 Runtime model: one coroutine per flight (ADR-009)

```csharp
public async Task<FlightResult> RunSpaceFlightAsync(short entryNavPoint)
{
    EnterFlight(entryNavPoint);                    // §1.2 steps 1..7, synchronous
    await _display.PresentAsync();                 // step 8, still at the cinematic interval
    _timing.SetSpaceFlightFrameTiming();           // 50 ms
    AfterEntry();                                  // step 9
    bool frameReady = true;
    while (_sim.ArcadeState == 0)
    {
        int control = await _controls.HandleSpaceFlightControlsAsync();  // ValueTask: completes synchronously unless a modal path runs
        if (control == -1) { _sim.ArcadeState = 5; break; }
        if (_sim.ArcadeState == 0)
        {
            _sim.Update3Space();                   // synchronous, callbacks at the original statement positions
            frameReady = RenderSpaceViewFrame();   // CPU drawing (+ R2 recording)
            UpdateCockpit();                       // synchronous
        }
        await _display.SettleDeferredPresentsAsync();   // see "deferred presents"
        if (frameReady) { frameReady = false; await PresentSpaceFrameAsync(); }   // DIBslam + DIBslamReal (+ SpaceView)
    }
    return ExitFlight();                           // §1.6
}
```

* **Never block.** Virtual time advances only in the throttle of a present and in modal waits.
  Simulation code has no awaits; UI drawing is synchronous; one tick is synchronous unless the key
  dispatch enters a modal path.
* **Modal paths** (pause, version banner, joystick calibration, nav map, autopilot cinema, SDL Esc
  pause) are `async` methods awaited from `HandleSpaceFlightControlsAsync` (an `async
  ValueTask<int>`: no allocation when nothing suspends). The statements around them keep their
  order (e.g. autopilot: VDU switch → travel/cinema → flush). `SelectCockpitVduMode` stays
  synchronous for the simulation callback (it can never reach the nav map from there: the nav map
  needs "mode 5 already showing", and the callback only asks for mode 3); the N key uses an async
  variant.
* **Frame skip**: iterations without a present do not await; at most `FrameSkip − 1` (≤ 4) of them
  run back to back, which is the original semantics (faster simulation).
* **Deferred presents.** `initialize_cockpit(4)` (and `ClearViewport(&stScreen)`) present in the
  middle of synchronous code — `InitializeCockpitView` is called by `new_view` inside the
  simulation. The present is split exactly like `DIBslamReal`: `Display.SlamRealNow()` copies the
  working buffer (with the cursor) to the front buffer, counts and services sound immediately, and
  records one owed throttle; `SettleDeferredPresentsAsync()` awaits the owed throttle waits at the
  next await point. Because the original copied first and waited afterwards, and the code between
  the request and the next await consumes no virtual time and reads no clock (the 60 Hz tick only
  changes in pumps), the pixels, the throttle deadlines and the random sequence are identical.
* **Pumping.** Host events enter the game queue only when the game pumps (`PollInputEvent` in
  `player_input`, explicit `PumpWindowMessages` in some sequences, modal waits). Sequences that
  never pump cannot be skipped with Esc — keep it (§5.1).
* **Sequences** are `async Task` methods with the same loop shape (`RefreshCockpitStatus` → draw →
  dump → `[UpdateCockpit]` → settle deferred presents → present).

### 7.3 The classic CPU path

The classic path is the reference and always runs (tests, SDL_Renderer fallback, screenshots):

* `SpaceBuffer` = an `IndexedSurface` of the geometry size (or 320x200 cockpitless) wrapped in a
  `Graphics.Raster.Viewport` with rectangle (0,0)-(W−1,H−1); cleared to 0xBF on allocation, by
  `clear_view_buffer`, at the end of `RenderSpaceViewFrame`; `Flash(0x0F)` for warp/death.
* Screen = `Display.Working` through `GraphicsContext.Screen`; VDU, bar and hand viewports are
  `Viewport.WithRect` aliases; off-screen hand buffers are separate surfaces.
* Every drawing call is the `GraphicsContext` primitive the C code uses, in the same order, so
  the 320x200 frame is bit-identical for the same simulation state (sprites through the asm
  rotation mapper, lines through the DDA, text through `DrawTextString`, palettes through
  `FlightPaletteEffects`).
* Resource handles are resolved once per mission (`ShapeRef` → `ShapeTable` cache in
  `GameResources`); the original's load/free choreography (e.g. `explosion_draw` reloading
  cockpit section 4) is a cache lookup, but its **observable** consequences are kept (the
  cockpit explosion shape being gone after the first cockpit frame, §3.10).
* The UI-side counters that the simulation does not own (`nMessageTimer`, VDU caches, damage VDU
  ticks, lock marker state, readout right edges, scanner saved pixels) are fields of the cockpit
  objects; nothing is static.

### 7.4 The R2 path (sprites at output resolution)

Precedent: the SDL GL renderer (§4.5). Contract: `docs/analysis/rendering.md` §3–4.

* **Enabling.** Only when the active renderer reports space-view support and the user setting is
  on; evaluated at every `Begin` (the SDL_Renderer fallback never records, so the CPU draws
  everything).
* **Recording.** `SpaceSpriteRecorder.Begin(geometry, cScreenViewportMode, cockpitless,
  backgroundIndex)` at the SDL begin points (after `sort_object_depth` in `Draw_3Space_Frame` and in
  the landing approach): clears the back `SpaceView`'s sprite list; clip = (originX, originY,
  originX + W − 1, originY + H − 1), or the whole screen for cockpitless / geometry 5; window mask
  = the geometry's runs (`SpaceViewMask.SetRun`, cached per cockpit file and geometry) or all.
  `TryRecord(shape, frame, x, y, angle, scale, flip, slot)` at the three SDL call sites
  (`draw_sorted_objects_to_buffer`, `DrawTitleLogo`, `DrawLaunchDoorFrame`) adds a
  `SpriteInstance` at buffer coordinates + layer offset with the sub-pixel rule of §4.5 and
  returns true, so the CPU skips the sprite. Refusal rules: as SDL (frame > 0xFA00 pixels when
  transformed draws nothing on both paths; an undecodable frame switches the rest of the frame to
  software to keep painter order).
* **Images.** `SpriteImageCache` keyed by (logical file, section, frame), filled with
  `ShapeFrameDecoder` on first use (index 255 transparent).
* **Binding to presents.** `Complete()` (in `dump_buffer_to_screen`) marks the back view as
  belonging to the next present; `PresentSpaceFrameAsync` publishes front buffer and view
  together (swap back/front `SpaceView`, `RenderFrame.Space = front`). A present without a
  completed view clears `RenderFrame.Space`, except overlay presents inside flight (pause and
  version panels, `ShowOnScreenMessage` 9999) that **retain** the last view (the panel pixels are
  not 0xBF; the SDL port drops the sprites there). `Cancel()` at flight exit and before the nav
  map. Deferred presents (letterbox backdrop) publish without sprites, like SDL.
* **Occlusion** in the renderer: a sprite fragment is visible only where the presented classic
  pixel equals `BackgroundIndex` inside the mask and the clip. That keeps cockpit art, HUD,
  instruments drawn over cockpitless views, subtitles, software-fallback sprites and the cursor on
  top. Improvement over SDL: set `BackgroundIndex` to the clear colour actually used this frame
  (0x0F on warp-flash and death frames), so GPU sprites appear on the white flash like the CPU
  path.
* **R2b / R3.** The simulation fills `SpaceViewSnapshot` per rendered frame; Flight keeps
  Previous/Current, attaches both to the published view and records the virtual time of the
  present; the runtime's `Update` sets `RenderFrame.Interpolation = (now − presentTime) / 50 ms`
  clamped to 0..1. HUD brackets stay at tick positions (≤ 1 tick lag, accepted in rendering.md).
  R3 meshes replace sprites per `ObjectSlot`; nothing in the flight layer changes.
* **Never** in the renderer: projection, culling, view-frame selection, painter order, palette
  effects (they are classic-layer palette writes the renderer reads), HUD geometry.

### 7.5 What must be bit-exact, and where modernisation is safe

Must be exact (gameplay, determinism, or the reference picture):

1. The tick structure and order (input → flight dynamics → keys → `Update3Space` → view → HUD →
   dump → cockpit → present) and the present cadence (20 fps flight, 16 fps sequences, frame
   skip, deferred presents).
2. Input semantics: dropped head event, VK duplicates, last key wins, `notRepeated`, keyboard
   ramps (incl. −10/10 values), mouse buckets/edges/afterburner mode (first event's modifiers),
   joystick handling, key-repeat dependence, Ctrl quirk.
3. Every UI-side random draw with its conditions (§6.4).
4. UI state that feeds gameplay: VDU modes (nav pointer slot, comm gating), message timer
   (comm chatter), lock mode, target cycling, missile camera, frame skip, cockpitless (Raptor gun
   aim; the projection size), camera view (the cockpit block of `update_cockpit`; hazard spawning
   uses view 0 and `cCockpitView`), simulator time/score/bonus in the render path, nav map
   objective selection, the `elapsedDate.day` write, comm menu contents and the off-by-one.
5. The classic frame's pixels: sprite rasterisation and order, buffer composition (runs,
   letterbox, cockpitless), HUD geometry (bounds, brackets, lock spiral, crosshair), HUD text
   layout including `print_message_text`'s wrapping quirk, VDU pages, scanner, bars, lights,
   readouts (format strings and erase), decals, pilot hand, palette values per tick.
6. Timers in rendered frames (messages, damage VDU 50/2, target refresh 8, light 4, portraits)
   and in 60 Hz pump ticks (message-slot blink, nav map blink, double clicks).

Safe to modernise (presentation only, no feedback into the simulation or the random sequence):

* R2/R2b/R3 rendering of the space window, filters, interpolation.
* Planets drawn (SDL/WCDX fix — adopt as default), VDU static noise (SDL xorshift, own RNG),
  optionally the DOS-style animated cockpit explosion (changes no random draw; sfx 0x1B).
* Esc pauses in flight (SDL), Alt+X = quit like closing the window, mouse grab handling, gamepad
  mappings expressed as queued key events, the comm menu highlight.
* Not safe: changing the simulation rate (Alt+N/M, frame skip semantics), synthesising key repeat
  for held keys (changes fire and throttle rates), skipping render-rate dependent work at display
  rate, or drawing the HUD from interpolated positions into the classic layer.

### 7.6 Porting order (milestones, each testable headless)

All tests run on the virtual clock (`GameScheduler`) with real data (`[DataFact]`), a fixed
`CRandom` seed and scripted host events (`ScreenRig`); outputs are front-buffer CRCs/PNGs
(`ScreenRig.SaveFront`, `wc1tool snap`), the simulation state hash and a draw counter on `CRandom`
(add `DrawCount` for tests).

1. **Space view, fixed camera.** `FlightSession` + `SpaceBuffer` (geometry 5) +
   `SpaceViewRenderer` + dump; a test driver ticks `Update3Space`/`PrepareSpaceView` N times on a
   real mission with view 0 and no input. Tests: identical CRCs for the same seed, different for
   another; drawn sprite count = visible objects; draw order = `SortedObjects`; single-sprite
   pixels equal `wc1tool render-shape`.
2. **Cockpit overlay.** `CockpitResources`, `InitializeCockpitView` (modes 0..7), geometry
   selection for all five cockpit files and COCKPIT 8:8, FizzleFade composite, cockpitless
   rules, palette effects, deferred presents. Tests: golden PNGs per cockpit and view; window mask
   equals the run list; flash values per tick; deferred-present timing (virtual timestamps of
   presents).
3. **HUD.** Gunsight, target box (crafted target positions → expected bounds), lock spiral, speaker
   brackets, nav pointer, crosshair, HUD message line and timers, on-screen messages (non-modal).
4. **Flight loop and controls.** `RunSpaceFlightAsync`, `player_input`, `process_player_input`,
   the key table, mouse and joystick, pause/version, frame skip. Tests: one per binding (state
   change and random draw count), ramp sequences, mouse buckets and edges, head-event drop, 20 fps
   cadence and frame-skip speed-up, Esc in the simulator (state 5).
5. **VDUs and instruments.** Mode stack, `update_VDUs`, `SelectCockpitVduMode` with malfunctions,
   the pages, message slots and blink, readouts, bars, lights, scanner, pilot hand, decals,
   launch animation. Tests: crops of the VDUs for crafted states, blink phases, static.
6. **Nav map and communication.** `InflightComputer` (click, N, Esc, presents, `elapsedDate`
   write), `NavMapRenderer` (shared with the briefing), comm menus with `request` (stub until
   phase 3), `real_vid_transmit`/`vid_transmit`/`npc_communication`, `EndCommMenu` interplay.
   Tests: menu contents for crafted situations, request log, portrait timing, random order.
7. **Sequences.** Launch, landing approach, scramble and landing scenes, autopilot cinema (with the
   simulation split), ejection, stranded, death, simulator screens and score panel, attract mode.
   Tests: run to completion with the expected number of presents, final state, Esc behaviour
   (pumping vs non-pumping loops), snapshots at fixed frames, landing randoms.
8. **R2 wiring.** Recorder, publication, capability flag; with the offscreen Vulkan renderer:
   unrotated scenes at integer scale with Nearest equal the classic frame; HUD occlusion; mask per
   geometry; retention on pause, drop on the nav map; then R2b interpolation.

A `wc1tool fly <series> <mission> [--ticks N] [--input ...] [--view v] [--out dir]` command (the
`-l` developer switch: `init_mission` + `LaunchPlayerShip` + `RunSpaceFlight`) makes every milestone
inspectable by eye.

### 7.7 Mapping to `GameFlowScreens.Flight` (`IGameFlowScreens`)

| Member | Implementation |
| --- | --- |
| `ScrambleAsync()` | `ScrambleScene` (§5.2) |
| `FlyMissionAsync(series, mission)` | new `FlightSession`; `InitMission` (→ `InitializeCockpit`); `LaunchSequence`; `RunSpaceFlightAsync(-1)` → `FlightResult` (= `nArcadeState`) |
| `LandingSequenceAsync()` | `free_cockpit`; `LandingApproach(PlayerCollisionObject)`; `ArcadeState = 0`; `PlayerCollisionObject = -1`; `free_3Space`; `LandingScene(CalculateDamageLevel())` — note the C order: the damage level is computed **after** `free_3Space`, which leaves slot 0's type, armour and damage counters intact |
| `EjectionSequenceAsync()` | `EjectionSequence`; `CheckStranded`; stranded → `StrandedSequence`; `free_3Space`; returns stranded |
| `StrandedSequenceAsync()` | `StrandedSequence`; `free_3Space` |
| `DeathSequenceAsync()` | `DeathSequence`; `free_3Space`; `funeral_sequence(1)` (Scenes work stream) |
| `AbortFlight()` | `free_cockpit`, `free_all_slots`, `free_3Space` (state 5; only reachable through a port-added abort) |
| `MissionStatistics`, `ObjectiveAchieved(i)` | read from the session's simulation after the flight |
| `FlyTrainSimMissionAsync(mission)` | `FigureArcadeTime`, `InitMission(0, mission)`, `ShowGetReadyScreen`, the rigged first-session set-up, `RunSpaceFlightAsync(nArcadeWave)` (§5.10); the victory and game-over screens are 3D sequences too and should be Flight methods that `RunTrainSim` (Rooms work stream) calls |
| attract mode | `AttractSequence.RunAsync()` called by `TitleSequence` before the menu |

---

## 8. Corrections to other documents, and open questions

### 8.1 Corrections found while writing this analysis

1. **simulation.md §3.6** lists camera views 1 = left, 2 = right. The code gives **1 = right**
   (eye forward = player right) and **2 = left** (eye forward = −player right); F2 (0x3C) calls
   `new_view(2)`, F3 (0x3D) `new_view(1)`, matching the manual's F2 left / F3 right.
2. **gameflow-screens.md §1.5** attributes `nArcadeState` 5 to "Alt-X abort". In the KS code 5 is
   produced only by Esc in the training simulator; the Win32 Alt+X posts `WM_QUIT` (process exit)
   and the SDL port ignores it. Its §5 table lists the landing approach as "100 frames fly-in +
   35 frames deck": there is a third phase of up to 50 frames (§5.4).
3. **graphics.md §4.2 / §4.4**: `UpdateSpacePaletteFade` runs on every `Draw_3Space_Frame` call,
   i.e. every tick, before the frame-skip test (not "every rendered space frame"); KS draws planets
   through the unscaled constellation branch (the SDL port's scaled path is a fix). graphics.md
   §3.5 refers to a non-existent "§4.7" for the flight flashes (§4.4).
4. **simulation.md §5.3** (logical file table): COCKPIT.VGA (LF 8) section 2 is the nav map art,
   section 7 the escape-pod interior (`pRearViewBackdrop` is a misnomer); PCSHIP section 4 =
   damage decals, 7 = light and bar sprites, 9 = weapon display; PILOTANM.VGA (LF 2) section 3 =
   pilot hand. `fire_weapon`'s extra aim offset applies to the **cockpitless Raptor** (`cCockpitView
   == 3`), not to a "cockpitless rear view" (simulation.md §6.1).
5. The cockpit explosion animation is dead in KS/SDL (§3.10); damage decals appear instantly.

### 8.2 Open questions

1. **Secret Missions 2 Dralthi cockpit.** The player's ship type 10 makes `InitializeCockpitResources(10)`
   use cockpit file 27 and index the five-entry layout tables out of range. PCSHIP.V05 exists in
   the GOG data but no reconstructed code reaches it (resources.md §2.3, question 10). Needs a
   decision: derive a layout for V05 (from the art, or from `SM2.EXE`), or fall back to a known
   cockpit.
2. **Reconstruction fidelity of odd code paths.** The reference reconstructs a debug build and
   reports per-function machine-code similarity (`make report`); not every function is
   byte-exact. Before porting a surprising behaviour literally — `print_message_text`'s forward
   newline write, the comm menu's off-by-one, `CloseCommChoiceMenu`'s `exit_squadron`, the
   `(yawGoal == pitchGoal) != rollGoal` script test — check the disassembly (`code-full`, not in the
   clone) or the function's similarity score.
3. **DOS vs KS.** The port follows KS (ADR-007). Unverified DOS differences in this layer: frame
   pacing (DOS ran uncapped with frame skip), 6-bit palette steps in the fades, possible extra
   developer keys in WC.EXE, and whether the DOS nav pointer also stays at its last position when
   the objective leaves the view (KS: it does).
4. **Defaults to confirm with the user**: Esc pauses in campaign flight (SDL) vs inert (KS);
   planets drawn (SDL) vs dots (KS); VDU static noise (SDL) vs nothing (KS); cockpit explosion
   animation off (KS) vs on (DOS-like); Alt+X quits; Alt+N/M and frame skip as developer options
   only.
5. **Key repeat.** Continuous keyboard fire and throttle follow the host's auto-repeat rate (as on
   DOS/Win32). Decide whether the host should normalise the repeat rate (for example 30 Hz after
   500 ms) so recorded inputs replay identically on every OS.
6. **Ownership at the simulation boundary.** `npc_communication`/`vid_equiv`/`real_vid_transmit`
   (recommended: UI), the autopilot split, and the missing callbacks (warp flash, phase-3 cockpit
   messages) must be agreed with the simulation work stream (§6.3).
7. **`request(0, ship, -1)`.** The comm menu's off-by-one can pass command −1 (and choosing "one
   past the end" in the recipient menu selects the target); phase-3 `request` must accept it like
   the original (its switch ignores unknown commands).
8. **R2 choices.** Per-frame background index for white flash frames and retaining the sprite layer
   during pause overlays (both improvements over the SDL port, §7.4); a "HUD coverage" mask instead
   of the 0xBF test if a HUD pixel ever uses 0xBF (rendering.md §7).
9. **Joystick layer.** Calibration (`j.cal`), `UpdateInputDeviceTransitions` double clicks and the
   menu/flight pumps are not ported in Game yet; flight needs the calibrated −9..9 sample, the
   button state and the button-2 double-click flag.
10. **Interpolation vs HUD.** With R2b the sprites move at display rate while brackets, lock marker
    and crosshair stay at tick positions in the classic layer (≤ 1 tick lag). Acceptable per
    rendering.md; an option to disable interpolation should exist.
11. **Damage alarm handle.** KS never stores a handle in `nDamageAlarmSfxHandle` (§3.7): the alarm
    sound is requested on every tick and light 3 can stay lit after the alarm condition ends.
    Decide between the literal KS behaviour and a working handle (alarm started once, stopped and
    light 3 cleared by `update_lights`/`house_keep`, as the code intends). Either choice changes
    only audio and one cockpit light, not the random sequence.
12. **Saga credit count.** `nIntroCreditCount += 9` on every `Title_Sequence` call reaches the null
    entry and then reads past `apszIntroCredits` (§5.11). Recommended: 11 cards, or 19 with the
    Saga option, computed once; confirm whether the KS binary really adds 9 (disassembly).
