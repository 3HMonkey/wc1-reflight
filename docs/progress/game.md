# Progress: game (WingCommander.Game)

## Status

2026-10-07: runtime converted to the frame-driven model (ADR-009): `Runtime.GameRuntime`
(IGameApp: virtual clock, root coroutine, quit/failure handling), `Video.Display` (working
buffer, front buffer, palette semantics), event waits and timing as coroutines on
`Core.Runtime.GameScheduler`. 80 Game tests pass, including whole campaign runs on the
virtual clock. The project temporarily references only Core (see the csproj comment) until
Graphics/Audio/Simulation are integrated.

## Mapping

| C (file) | C# | Status |
| --- | --- | --- |
| `QueueInputEvent`, `AllocateInputEvent`, `ReleaseInputEvent(Queue)`, `RemoveInputEvent` (eventmgr.c) | `EventManager.QueueInputEvent` + private pool (256 records, overflow drops all) | done |
| `QueueInputEventAtCursor`, `TranslatePolledInputEvent` | `EventManager.QueueInputEventAtCursor`, `.TranslatePolledInputEvent` | done |
| `GetNextInputEvent`, `PollInputEvent`, `PeekInputEvent`, `IsInputEventQueued`, `FlushInputEvents`, `RetainInputEventsOfType` | same names on `EventManager` | done |
| `PumpWindowMessages` (winmain.c) + `SdlPumpEvents`, `SdlHandleKeyboardEvent`, `SdlHandleMouseEvent`, `SdlQueueMouseMotion`, `SdlHandleMouseWheelEvent` (sdl/events.c) | `EventManager.PumpWindowMessages` + private dispatch | done |
| `SetInputKeyState`, `ClearInputKeyState`, `ClearInputKeyStatePreservingModifiers` | `EventManager` | done |
| `PollKeyboardState`, `GetShiftKeyState`, `GetControlKeyState` (+SDL Ctrl quirk), `GetKeyboardModifiers` (sysinput.c, sdl/input.c) | `EventManager` (physical key counters per scan code) | done |
| `GetF1KeyLatch`, `bEscapePressed`, `nSystemKeyDown`, `dwDebugOverlayKey(Latch)` | `EventManager.F1KeyLatch`, `.EscapePressed`, `.SystemKeyDown`, `.DebugOverlayKey(Latch)` | done |
| `SetMousePosition`, `SetMouseHomePosition`, `WarpMouseTo`, `ApplyPackedMousePosition` | `EventManager.SetMousePosition`, `.SetMouseHomePosition`, `.WarpMouseTo` | done |
| `EnterAllocationScope`/`LeaveAllocationScope`/`ResetAllocationDepth` (cursor show count) | `EventManager.ShowCursor`/`HideCursor`/`ResetCursorShowCount` | done |
| `SdlSetMouseGrab`/`SdlSuspendMouseGrab`/`SdlResumeMouseGrab` | `EventManager.SetMouseGrab`/`SuspendMouseGrab`/`ResumeMouseGrab` | done |
| `InitializeEventManager`, `InitializeEventManagerResources`, `EMStartUp`, `ShutdownEventManager`, `SetEventManagerPump` | `EventManager.Initialize`, `.Shutdown`, `.Pump` | partial (cursor shape loading waits for Graphics) |
| `CheckEscaped`, `WaitForInputKey`, `WaitForSceneAdvance`, `WaitForStreamInputKey`, `MoveMenuPointerFromKeyboard` (disk.c) | `EventManager` (`CheckEscaped`, `*Async` coroutines) | done |
| `WaitForKeyAcknowledge` (hudmsg.c) | `EventManager.WaitForKeyAcknowledge` | done |
| `PumpMessagesDuringWait`/`DebugOverlayConsole::WaitForKey`, `WaitForKeyExceptXOrF12`, `TakeDebugStepFlag`, `ClearDebugPauseFlags` (pilot.cpp, debug.cpp) | `EventManager` | done |
| `SetMultimediaTimerCallback`, `FrameTimerCallback` (hudmsg.c), `SetFrameTimerPeriod(Direct)`, `SetFrameTimerAndWait`, `WaitForFrameTick`, `IsFrameTickElapsed` (eventmgr.c) | `FrameTiming` (deadline-based one-shot timer) | done |
| `SetSpaceFlightFrameTiming`, `SetCinematicFrameTiming`, `ReportSpaceFlightMaxFps` (dib.c) | `FrameTiming` | done |
| `ThrottleFrameAndDrawFps` (screen.c) | `FrameTiming.ThrottleFrame` | done (fps text overlay not ported) |
| `nTickCount60Hz`, `GetGameClockTicks`, `InitGameClockEpoch` (sysinput.c) | `FrameTiming.Ticks60Hz`, `.GameClockTicks`, `.InitGameClockEpoch` | done |
| `CaptureMouseCursorBackground`, `DrawMouseCursor`, `RestoreMouseCursorBackground`, `RefreshMouseCursorDisplay`, `SetMouseCursorShape` | — | todo (needs Graphics) |
| `DIBslam`, `DIBslamReal`, `DIBupdate`, `DIBramPalette`, `DIBwaitForVerticalBlank` (dib.c) | `Video.Display` (`Slam`, `SlamRealAsync`, `PresentAsync`, `Update`, `PaletteChanged`, `WaitForVerticalBlankAsync`) | done (cursor compositing via `ISoftwareCursor`, implemented once Graphics lands) |
| `UpdateInputDeviceTransitions`, `PollJoystickButtonEvents`, `PollMenuInputDevices`, `GetJoystickPosition`, calibration (screen.c, winmain.c, spc.c, brains.c) | — | todo |
| `LoadVolumeSettingsFromRegistry`, `SaveVolumeSettingsToRegistry`, `ReadCheaterFlagFromRegistry`, sdl/registry.c | `Config.GameSettings` (file `wc1.cfg` in the user data directory, same text format) | done |
| `LoadWingCmdrCfgFile`, `CheckLauncherAndConfig`, GameMain argument loop | `Config.StartupOptions` | done |
| `PilotRecord`, `aInitialPilotRecords`, `CampaignState`, `stInitialCampaignState` | `Campaign.PilotRecord`, `Campaign.CampaignState` | done |
| CAMP.xxx sections (`pConstellationDefinitions`, `pMissionCampaignData`, `pRecRoomRoster`) | `Campaign.CampaignFile` (`SeriesRecord`, `SeriesMission`, `ConstellationObject`) | done |
| `CreateEmptySaveGameFile`, `EnsureSaveGameFile`, `LoadGame`, `SaveGame`, `AnySavedGames` (barracks.c, geom.c) | `Campaign.SaveGameFile`, `SaveGameSlot`, `SavedObjective` (byte-exact round trip) | done |
| `ResetCampaignData`, `CorrectPointers` (killbrd.c) | `CampaignSession.Reset` (pointers are indices) | done |
| `PostMission`, `add_statistics`, `FullMissionScore`, `PlayersMissionScore`, `UpdateSeries`, `MoveNewCampaign` (nav.c) | `CampaignSession` | done |
| `personality_killed` (wingmen), `wing_status` | `CampaignSession.WingmanKilled`, `.WingStatus` | done (enemy aces: flight) |
| `StartNewCampaign`, `GameFlow` (nav.c) | `Flow.CampaignFlow` + `IGameFlowScreens` (screens/sequences behind an interface) | done (victory and loss paths verified on CAMP.000) |
| `LoadGameFromSlot` UI, `SaveGameWithNamePrompt` UI | — | todo (screens) |
| `LoadBriefingData`, `BriefingPacketHeader`, funeral/office/medal section headers (cmpgn.c) | `Scenes.BriefingFile`, `MissionConversations` | done |
| `ConversationSceneRecord` stream, text block | `Scenes.ConversationRecord`, `Scenes.ConversationScript` | done |
| `ParseTests`, `int_value` (screens.c) | `ConversationScript.EvaluateTests` + `ISceneConditions` | done (all scripts of BRIEFING.000-002 swept) |
| `ParseMouthAnimation`, `ParseFaceAnimation`, `asMouthFramesByPhoneme` (cmpgn.c) | `Scenes.AnimationScripts` | done |
| `AddPCName`, `apszMedalNames`, `apszPilotRankNames`, `asConversationTextColours` | `Scenes.TextMacros` | done |
| `LoadSceneAnimationResources` (data part), `FindSceneAnimationCommand`, `SceneAnimationGoalReached`, `UpdateSceneAnimationObject` (logic.c) | `Scenes.SceneAnimation` (+ `ISceneAnimationRenderer`) | done (all MIDGAME V00-V05 scenes run to completion) |
| `SceneDirector`, `LongTalk`, `CloseTalk`, `LoadFace`, shot handlers, `PlaySceneAnimation` loop | — | todo (needs Graphics/Display) |

## Deviations

- All waits are coroutines on the virtual clock (ADR-009). Polling loops yield 1 virtual ms
  when no event was consumed (the original spun); `WaitForFrameTick` and the throttle await
  their deadline instead of spinning (nothing could change during the spin).
- Host events carry an arrival time and enter the original queue only when the game pumps.
- `GetAsyncKeyState` is emulated from physical key-down counters per scan code updated by
  the pump, so arrows and the numeric keypad both count for `PollKeyboardState` (DOS
  behaviour; the SDL port only looked at the cursor block for the polled state).
- Window close ends the runtime (the root coroutine is abandoned) instead of `exit(0)` inside the pump.

## 2026-10-07 evening: start-up and DOS intro

- `Wc1Game` = composition root and GameMain start-up (see docs/STATUS.md for the exact steps).
- `Screens/DosIntro` ports `sdl/dos_intro.c` 1:1 (actor table, stage logic, music sync through
  `IIntroMusic`), drawing through `GraphicsContext` into a 320x128 viewport copied to rows 24..151.
- `Video/SoftwareCursor`, `Resources/GameResources`, `Audio/GameAudio` added; Display's slam flag
  is the GraphicsContext's ScreenDirty.
- Not yet ported: Title_Sequence (menu and attract), GameMain loop, text contexts
  (InitializeGameTextContexts), constellation/campaign packet preload, direction view frames.

## 2026-10-07 night: title menu, GameMain loop, screen skeleton

- `Screens/TitleSequence` (menu part of Title_Sequence; attract part pending the flight engine),
  6 tests (new game by S and click, hidden continue, continue with a synthetic save, misses).
- `Wc1Game.RunAsync` = GameMain loop (title, StartNewCampaign, CampaignFlow), `Session`, `Flow`,
  `Screens` (`GameFlowScreens` partials: Rooms and Scenes owned by two porting work streams, Flight by
  integration), `SaveGamePath` (user data dir, import from GAMEDAT), `DefaultText`
  (InitializeGameTextContexts), `ShowNotPortedAsync` placeholder.
- Test rig `tests/WingCommander.Game.Tests/Screens/ScreenRig.cs` (headless game, scripted keys and
  clicks, PNG snapshots via `SaveFront`).
- Known original quirk kept: UpdateTitleMenuCursor reads stHostMouseState, which the mouse does
  not update (only WarpMouseTo, MoveMenuPointerFromKeyboard and the joystick menu pump).

## Deviations (integration, 2026-10-08)

- New Secret Missions campaigns use their own CAMP constellation definitions (section 0); the
  original kept CAMP.000's from start-up until a saved game was loaded (gameflow-screens.md §10).
- `CampaignSession.SavedCampaignDate` starts at {20, 340} like the C global (was zero).
- Key repeat is generated by the game (ADR-012); Alt+X throws GameExitException in the pump.
- `Display.SlamRealNow` / `SettleDeferredPresentsAsync` for presents inside synchronous code.
