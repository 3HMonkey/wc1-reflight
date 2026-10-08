# Progress: rooms and menus (WingCommander.Game/Screens/Rooms, Screens/Ui)

Owner: the rooms work stream (2026-10-07). Scope: rec room (bar) with the kill board, barracks
(bunks = save/load, quit, medals), modal prompts and text input, TrainSim menus.

## Status

2026-10-07: everything in scope is ported and wired into `GameFlowScreens.Rooms.cs`
(`RecRoomAsync`, `BarracksScreenAsync`, `RunTrainSimAsync`). Verified headless with 38 tests in
`tests/WingCommander.Game.Tests/Screens/Rooms` (rec room 11, barracks 17, TrainSim 8, GameMain
flows 2) and by looking at PNG snapshots of every screen (written when `WC1_ROOMS_PNG_DIR` is set):

- Rec room: bar with Shotglass, the two seated pilots of the mission and their idle animations,
  drifting stars in the window, region labels at the bottom, cursor frames; kill board; talking
  hands over to the scenes work stream's `PlayConversationAsync` (Paladin's talk shown and back to the
  bar); pan fade-in; empty table when both pilots are dead; every mission of CAMP.000/001/002
  enters and leaves the bar without errors.
- Barracks: room, sleepers per used bunk, status lights, the drop falling into the bucket, the
  flickering ceiling light, label
  strip; "Load a game first." for every door without a campaign; quit Y (GameExitException,
  normal end) / N; save into an empty bunk; Esc cancels; replace with Y/N and the old name
  offered; awaken Y/N; **save -> load -> save is byte-identical** and a crafted slot with garbage
  after every NUL (Secret Missions 1, all fields set) **loads and saves back byte-identical**;
  the developer unlock renames the loaded pilot CHEATER; "Error: Game X not saved." (read-only
  file) and "Error: data may be bad." (bunk cleared behind the barracks' back); medals view (rank insignia, badges,
  stacked stars, summary line with the system name from MODULE section 5).
- TrainSim: forced first session (enemy 2, 4000 points, Get Ready, the placeholder flight, Game
  Over, name and callsign entry with Backspace and Shift, ranking); ranking with the scrolling
  title, enemy selection (portraits in the console corners), low-score and congratulation
  messages, Esc cancels; Victory with fireworks; a provided `ITrainSimFlight` is driven in the
  original order (40 Get Ready frames, 80 Game Over frames).
- GameMain: new game -> TrainSim -> rec room -> barracks -> save -> quit; Continue -> barracks
  -> awaken -> bar.

Build: zero warnings. No reflection, no blocking waits.

## Mapping

| C (address, file) | C# | Status |
| --- | --- | --- |
| `TitleMenuRegion`, `IsPointInRect` (0x435090, mathfp.c), `FindMenuRegionAtPoint` (0x43F7C0, killbrd.c) | `Ui.MenuRegion` | done |
| `InitializeRoomMenu` (0x43F750), `ClearRoomMenuLabel` (0x43F690), `IsRoomMenuLabelEmpty` (0x43F6A0), `DrawRoomMenuLabel` (0x43F6B0), `RefreshRoomMenuLabel` (0x43F6F0), `ClearRoomMenuCursorFrame` (0x43F720), `SelectRoomMenuLabel` (0x43F730), killbrd.c; `UpdateRoomMenuCursor` (0x42A680, hudmsg.c) | `Ui.RoomMenu` | done |
| `InitializeRoomViewports` (0x43F810, killbrd.c) | `Ui.RoomViewports` | done |
| `ModalTextPanel`, `InitializeModalTextPanel` (0x41A9D0), `DrawModalTextPanel` (0x41AAE0), `RestoreModalTextPanel` (0x41AB60), geom.c | `Ui.ModalTextPanel` | done |
| `EraseTextContextBackground` (0x425C30, pilot.cpp), `ResetStringBuilder` (0x403E40, mono.c) | `Ui.ModalTextPanel` (static) | done |
| `ShowModalTextPanel` (0x41AB90), `ReleaseModalTextPanel` (0x41AD10), geom.c; `ShowModalMessage` (0x428F20, hudmsg.c) | `Ui.ModalPrompts.ShowModalTextPanelAsync`, `ReleaseModalTextPanelAsync`, `ShowModalMessageAsync` | done |
| common Y/N body of `ConfirmQuitWingCommander` (0x41BF10), `ConfirmAwakenAfterBadData` (0x41BF60), `ConfirmReplaceFaultyData` (0x41BFE0) | `Ui.ModalPrompts.ConfirmAsync` | done |
| `PromptForTextInput` (0x41B420, barracks.c) | `Ui.ModalPrompts.PromptForTextInputAsync` | done |
| `ReadTextInput` (0x426200), `DrawTextInputCursor` (0x4260E0), `ClearTextInputCharacter` (0x426140), `ClearNextTextInputCharacter` (0x4261D0), pilot.cpp; `EraseLastTextInputCharacter` (0x41DDF0, disk.c) | `Ui.TextInput` (modes `ModeAnyCase`, `ModeUpperCase`, `ModeDigits`) | done |
| `PanToScreen` (0x439430, screens.c), driving its step object | `Ui.ScreenTransitions.PanToScreenAsync` | done |
| `RecRoom` (0x43F940, killbrd.c); `UnionRectBounds` (0x431EA0, screen.c); tables `apRecRoomAnimations`, `aRecRoomCharacterOrigins`, `aRecRoomMenuRegions`, `apszRecRoom*Labels` | `Rooms.RecRoom` | done |
| `ShowChalkBoard` (0x440510, killbrd.c), `asChalkBoardPilotOrder`, `stChalkBoardDate` | `Rooms.ChalkBoard`, `Rooms.RoomsState` | done |
| `init_constellation(0)` / `free_constellation` (0x4243E0 / 0x424490, logic.c), `InitializeConstellationField` (0x42D390), `DrawConstellationField` (0x42D500), music.c | `Rooms.ConstellationField` | done |
| `BarracksScreen` (0x41C170), `InitializeBarracksAnimation` (0x41B070), `FreeBarracksMenuLabel(s)` (0x41B0E0 / 0x41B180), `SetAwakenBarracksMenuLabel` (0x41B110), `SetBunkMenuLabel` (0x41BAD0), `GetBunkInfo` (0x41BB20), `DrawBarracksBunks` (0x41BBD0), `DrawBarracksStaticDetails` (0x41BC90), `AnimateBarracks` (0x41BCE0), `HandleBarracksBunkSelection` (0x41C090), `UpdateBarracksScreen` (0x41C140), barracks.c | `Rooms.Barracks` | done |
| `SaveGame` (0x41B1E0), `LoadGame` (0x41B710), `EnsureSaveGameFile` (0x41B020), barracks.c | `Rooms.Barracks.SaveGame` / `LoadGame` over `Campaign.SaveGameFile` | done |
| `SaveGameWithNamePrompt` (0x41B5C0), `LoadGameFromSlot` (0x41B980), `WarnLoadGameFirst` (0x41B550) | `Rooms.Barracks` | done |
| `ViewMedals` (0x436E30), `DrawMedals` (0x4375C0), screens.c; `InitializeConversationText` (0x427BC0), `RefreshMemoryStatusOverlay` (0x427C30), main.c | `Rooms.MedalsView` (public: `ShowAsync`, `DrawMedalsAsync`) | done |
| abSeriesAuxData part of `LoadMissionData` (0x4059B0, cmpgn.c) | `Rooms.MissionText.LoadSystemName`, `RoomsState.SystemName` | done |
| `RunTrainSim` (0x427080, system.c) | `Rooms.TrainSim.RunAsync` (+ `ITrainSimFlight` hooks) | done |
| `ShowTrainSimHighScores` (0x4268E0), `DisplayTrainSimHighScoreTable` (0x425C60), `AnimateTrainSimTitle` (0x425D00), pilot.cpp | `Rooms.TrainSim` | done |
| `SelectTrainSimMission` (0x426C70), `LoadTrainSimOpponentShape` (0x426C50), pilot.cpp; `UpdateTrainSimMenuCursor` (0x42A610, hudmsg.c); `AlignSpriteFrameToRectCorner` (0x42E1D0, music.c) | `Rooms.TrainSim` | done |
| `UpdateTrainSimHighScores` (0x426820), `EnterPilotNameAndCallsign` (0x426750), `PromptForPilotField` (0x426600), `InitializeTrainSimTextPanel` (0x426660), `ShowTrainSimTextMessage` (0x426700), pilot.cpp | `Rooms.TrainSim` | done |
| `ShowGetReadyScreen` (0x439840), `ShowVictoryScreen` (0x439910), `ShowGameOverScreen` (0x439A80), screens.c; `DrawCenteredScaledIntroText` (0x4037A0), `MeasureScaledIntroTextWidth` (0x403710), mono.c | `Rooms.TrainSimStatusScreens` | done (over `ITrainSimFlight`) |
| `InitializeFireworks` (0x42D270), `TheEndFireWorks` (0x42D2A0), music.c | `Rooms.Fireworks` | done |
| `nArcadeScore`, `nArcadeWave`, `nTrainSimMission`, `nTrainSimActive`, `nArcadeBonusCountdown`, `cCockpitView`, `cCockpitLogicalFile` | `Rooms.TrainSimSession` (`GameFlowScreens.TrainSim`) | done |
| `PlaySfxWaveFileByNumber(n, -1, 0)`, spacetrack / Preload / Release / StopMusicUnlessSuppressed calls | `Rooms.RoomSound` | done |
| `ShouldSuspendCursorForRect` (always 0), `EventManagerHook` (empty), `CheckCursor` (empty), `ShowMemoryStatusDebug` | omitted (no effect) | n/a |
| `PollMenuInputDevices`, `PollJoystickButtonEvents`, `CalibrateJoystickInteractive` (barracks J key) | not installed: the pumps return at once without a joystick; calibration not ported | joystick |

## Deviations

1. **Personalities without art.** CAMP.002 seats personality ids 11 and 12 (and the second id
   may be -1); the original would load missing RECROOM.VGA sections and read outside
   `personalityDeathMission`. Seats with ids outside 0..7 stay empty (no talk). Memory-safety
   guard like the SDL port's.
2. **Missing briefing section.** When BRIEFING.xxx has no section for the mission, talking does
   nothing (the original used stale pointers). Never happens with the campaign data.
3. **Conversation hand-over.** The rec room prepares the screen as the original (scene buffer
   rows 0..127, screen rows 24..151, screen cleared, RECROOM.VGA section 1 as backdrop) and calls
   `PlayConversationAsync(2, script, backdrop)`; `InitializeConversationText` is left to the
   conversation layer.
4. **Modal panel ownership.** The original kept the open panel in the global `pModalTextPanel`;
   `ShowModalTextPanelAsync` returns the panel and `ReleaseModalTextPanelAsync` takes it (all
   original call sites pair them).
5. **File errors.** Writing SAVEGAME.WLD catches I/O errors and shows "Error: Game X not
   saved."; an unreadable file reads as an empty bunk. No floppy prompt (PromptInsertNumberedDisk).
6. **Joystick.** The menu pumps are not installed (`Events.Pump = null`); without a joystick they
   returned immediately in the original. `nMenuPointerSpeed` (joystick only) is not tracked.
7. **TrainSim flight seam.** `ITrainSimFlight` gathers the flight calls of RunTrainSim and of
   the three caption screens; until the flight layer provides one (`GameFlowScreens.TrainSimFlight`),
   `FallbackTrainSimFlight` draws the captions over the simulator console (PCSHIP.V04 section 0)
   with an empty space view (PCSHIP.V04 view geometry, space colour) and pumps events per frame.
8. **Labels are strings compared by reference** (the original compared label pointers): the two
   "Save this campaign  " labels are distinct objects, "Awaken ..." labels are new per refresh.
9. **Interface sound.** The barracks impact (sfx 35) goes straight to `GameAudio.Backend.Play`
   with volume 127, pan 64, tag -1 (what `PlaySfx(35, -1, 0)` does) because the game has no
   shared `SoundEffectManager` yet.

## Original behaviour kept (quirks)

- Window stars are seeded at 0..width/0..height without the viewport offset, so most of the
  three rec-room stars are clipped; only the drifting particles show.
- The second rec-room seat is only considered when the first seat's id is not -1.
- Shotglass's work rectangle is the union of the first idle frame and the window; larger frames
  (pouring) are clipped to it.
- After a bunk click the Y/N questions run with the cursor show count at -1, so the pointer
  stays visible during "Awaken/Replace ... (Y/N)" and "Loading Game...".
- `ReadTextInput` leaves the virtual-key duplicates disabled after Esc or an empty Enter; an
  empty Enter returns without restoring the context's text buffer, viewport and background
  colour, Esc restores buffer and viewport but not the background colour.
- Name entry (mode 0) lower-cases letters unless Shift is held; the default name is copied into
  the record before every attempt, so its tail stays behind the NUL ("ace\0rick") and is saved.
- Esc on the TrainSim ranking cancels the enemy selection at once (the Esc latch stays set).
- `ShowModalMessage` is acknowledged by the next key *release*, so the release of the Enter that
  confirmed a save name closes "Error: Game X not saved." at once (mouse buttons do not count).
- High scores compare signed; scores are shown with a trailing "0"; "YOUR SCORE IS ONLY %ld0".
- The barracks label strip (frame 50) is redrawn only when the label object changes.
- `PanToScreen` leaves palette entries up to 3 short of their values.
- With the virtual-key duplicates enabled (after the title) a key's VK code can act as a scan
  code in the room menus (e.g. 'H' = 0x48 moves the pointer up, '9' = 0x39 activates).

## Requests for integration

1. (Done in integration: `GameMainTests` no longer walks the old placeholders.) A full input
   script through the rooms is in `Rooms/NewCampaignFlowTests`: S at 1000; Space at 5000
   (TrainSim flight placeholder); Enter at 11500 and 12500 (name, callsign); Space at 14000
   (ranking); click (300,100) at 16500 (bar -> barracks); click (300,60) for the hangar or
   (233,55) + 'Y' to quit.
2. **`GameDirectory` is not thread-safe** (`_packets`, `_installTable`): xunit runs test classes in
   parallel over the shared `GameData.Directory`; one run failed with "Operations that change
   non-concurrent collections must have exclusive access" in `CampaignTests`. The room tests use a
   private directory per rig (`RoomScreenRig`). Suggest a lock / `ConcurrentDictionary` in
   `GameDirectory` (and `PacketFile`'s section cache, if any) or private directories in `ScreenRig`.
3. **TrainSim flight**: implement `Rooms.ITrainSimFlight` on the `IFlightLayer` class (picked up
   through `Wc1Game.FlightLayer as ITrainSimFlight`) or assign one to
   `GameFlowScreens.TrainSimFlight`: `BeginSession` (nCannedSceneMode = 0, reset the HUD text
   context), `InitializeMission` (FigureArcadeTime, init_mission(0, m)), `PrepareFlight`
   (forced-session handicap when `campaignStartup`: shields 0, component 2 damaged, hull capacity
   + 1, wave 2, set_up_next_wave, 25 s; InvalidateVduMode 0/1), the caption view
   (`RefreshCockpitStatus`, `DumpBufferToScreen`, `BeginGetReady`/`EndGetReady`/`BeginVictory`/
   `BeginGameOver`) and `EndSession` (free_all_slots, free_cockpit, free_3Space).
   `FlyTrainSimMissionAsync` then only runs `RunSpaceFlight(TrainSim.ArcadeWave)`; the flight adds
   its points to `GameFlowScreens.TrainSim.ArcadeScore` and reads `CockpitView` (4) /
   `CockpitLogicalFile` (21). Key-event duplicates are enabled around the call, as the original.
   Note: the new `Flight/IFlightLayer.FlyTrainSimMissionAsync` is documented as "init_mission(0,
   mission) and the flight", but RunTrainSim calls init_mission *before* "Get Ready" (whose
   caption is drawn over the initialised 3D scene) and applies the forced-session handicap after
   it; with `ITrainSimFlight` on the same class, `FlyTrainSimMissionAsync` is just the flight.
4. **Mission objectives for saves**: `GameFlowScreens.RoomState.MissionObjectives` is what the
   barracks save (aMissionObjectives); the briefing / init_mission (Build_objective_list) should
   keep it current. Loads already update it.
5. **Shared code with the scenes work stream**: `Screens/Scenes/ConstellationField` and
   `Screens/Rooms/ConstellationField` port the same C (created concurrently); worth merging.
   `Rooms.MedalsView.ShowAsync` / `DrawMedalsAsync` are public for `ShowTheEndScreen` (ViewMedals)
   and `AwardCampaignMedal` (DrawMedals).
6. A game-wide `SoundEffectManager` in `Wc1Game` would replace `RoomSound.PlayInterfaceSound`.

## Spec corrections (docs/analysis/gameflow-screens.md, C code wins)

- §5 barracks: the "figure dropping from the top right" is a drop of water falling from the
  ceiling into the bucket (frames 27..29 at x 298, splash frames 37..48 at (298, 139), sfx 35),
  and the "blinking" sprite at (45, 0) is a flickering ceiling light (frames 49 on / 26 off).
- §5 rec room: the three character regions are replaced on every visit by the frame bounds of
  Shotglass and the seated pilots (unseated: (400,400)-(401,401), unreachable).
- §5.3: `ShowModalMessage` waits for a key release (`WaitForKeyAcknowledge(0)`), not a key press;
  the Y/N questions read a virtual-key code (`WaitForStreamInputKey`).
- §2.2 CAMP section 2: CAMP.002 also lists personalities 11 and 12, which have no rec-room art.

## Open questions

1. What the DOS rec room showed for CAMP.002's personalities 11 and 12 (no RECROOM.VGA art).
2. The DOS barracks' quit returned to the title (GameFlow result 6, dead in the Win32 build); the
   port follows Kilrathi Saga (quit ends the game).
3. Whether the Win32/SDL flight pumped events during the Get Ready / Game Over captions (Esc
   latch); the fallback view pumps every frame.

## Next steps

- When the flight layer lands: real `ITrainSimFlight`, remove the reliance of the TrainSim tests
  on the "not ported" placeholder (they press Space where the placeholder waits).
- Joystick menu pumps and calibration together with joystick support.
