# Progress: conversation engine and non-flight cutscenes (WingCommander.Game, Screens/Scenes)

Owner: scenes work stream (2026-10-07). Files: `src/WingCommander.Game/Screens/GameFlowScreens.Scenes.cs`,
`src/WingCommander.Game/Screens/Scenes/*`, `src/WingCommander.Game/Scenes/*` (data layer additions
`MissionBriefingData`, `CampaignSceneContext`), `tests/WingCommander.Game.Tests/Screens/Scenes/*`,
`tests/WingCommander.Game.Tests/Scenes/MissionBriefingDataTests.cs`.

## Status

Done (2026-10-07). The conversation engine (SceneDirector with every shot handler) and every member
of `GameFlowScreens.Scenes.cs` are ported from the reference and run on the virtual clock:
briefing (room, podium, talking heads, Dismissed, nav map, stand-up), scramble hangar walk,
debriefing, Colonel's office, medal ceremony, wingman and player funerals, MIDGAME "Meanwhile..."
scenes (V00..V08, both variants), The End (with ViewMedals and fireworks). The two endings that
are canned 3D scenes (campaign victory, Tiger's Claw escape) have documented placeholders for the
3D part; their 2D parts are ported. Zero warnings; Game tests: 213 pass (84 of them are the scene
tests listed below).

Verified how:

- Tests (`tests/WingCommander.Game.Tests/Screens/Scenes`, collection "Scenes"):
  - whole campaigns through `CampaignFlow` with scripted rooms/flight and the real scenes: the Vega
    victory path (18 briefings, offices, medals 0/1/2/4, three wingman funerals, MIDGAME 0..3,
    victory, The End with fireworks) and the loss path (MIDGAME lost variants, escape, The End);
  - every briefing + scramble + debriefing of BRIEFING.000/001/002 (40 + 18 + 18 missions, 2500
    records) with varied campaign states (objectives, kills, dead pilots, ejection, promotion,
    ship change, ace flags, mission score) and Space every 150 ms: no exception, every scene ends;
  - office and medal ceremonies (all five medals) and wingman/player funerals (every series value)
    for the three campaigns;
  - all 16 MIDGAME scene/variant combinations play to the end (and end black); keys fast-forward
    every animation; a missing variant (V04 won) is skipped;
  - branch golden values traced by hand through BRIEFING.000: debriefing 1.0 for "nothing
    achieved", "all achieved with kills" and "Spirit died this mission"; medal ceremony for Bronze
    Star, Golden Sun, Terran Medal of Valor; office promotion; second Golden Sun refused;
  - briefing 1.0: exact record/handler sequence (24 records), Space skips record by record,
    Esc ends the whole scene, the nav map labels (Nav 1..3, Tiger's Claw, Asteroids x2), macros
    ($S $T $D $C $R $N $W), music 25 -> 26 (and the skip stopping it), layout released at the end;
  - rec-room talks through `PlayConversationAsync` (all three talks of mission 1.0) restore the
    screen clip and buffer; funeral with music ends with the funeral march (101 s);
    scramble = 72 frames, Esc skips; escape scene = 260 frames; The End = medals + 320 frames.
  - `MissionBriefingDataTests`: every mission with a briefing has MODULE data; objective lists and
    flight paths consistent; scene context (ace status, time bytes, ejections, names, medals).
- Looked at (PNG snapshots via `WC1_SCENE_PNG_DIR`, written to the session scratchpad only):
  briefing establishing shot, long shot, podium with mouth, Colonel/Spirit/player heads, Dismissed,
  nav map (3x), "Squadron dismissed" stand-up; debriefing pan, officer close look, Colonel head;
  office door, window with stars, Colonel with window; medal hangar doors, citation, medal
  close-up, salute overlay, applause; wingman funeral deck, helmet overlay, "Fire!", drift; player
  funeral "THE END"; scramble walk and legs; ViewMedals; The End with fireworks; victory
  placeholder and flag-raising animation; escape placeholder; MIDGAME V00, V04, V05, V08; rec-room
  talks (Paladin; Shotglass with stars through the window). Everything is placed and animated as
  expected; the podium shot matches the DOS screenshot in the reference repository
  (`reference/wc1-re/screenshots/mission-briefing.png`): same picture, layout and subtitle.

## Architecture

- `Screens/Scenes/SceneDirector` (partial class, one file per area) holds the original's
  conversation globals and implements every shot handler and the cutscenes built on them.
  `GameFlowScreens.Director` creates it on first use and keeps it for the game (like the globals).
  - `SceneDirector.cs`: the record loop (`RunAsync` = SceneDirector), `PlayConversationAsync`
    (entry point for the rec room), `LoadMissionData`, TalkerInit/FreeTalker, script parsing,
    `PlayedRecords` (diagnostics: index, shot, handler, start time of every record played).
  - `.TalkingHeads.cs`: LoadFace, CloseTalk, LongTalk.
  - `.Briefing.cs`: Briefing + LoadBriefingRoom, EstablishingShot, DrawBriefingLongShot,
    ReturnToBriefingLongShot, Dismissed, DrawPodiumShot, DrawBriefingCharacter, CloseLook, UpdateMap.
  - `.Debriefing.cs`: DeBriefing, Office, DrawDebriefingLongShot, DebriefingEstablishingShot.
  - `.Medals.cs`: AwardCampaignMedal, ViewMedals, DrawMedals, DrawMedalChest, DrawMedalLongShot,
    MedalEstablish, PinMedal.
  - `.Funeral.cs`: funeral_sequence, funeral_player, funeral_wingman, DrawFuneralLongShot.
  - `.Meanwhile.cs`: ShowMeanwhileTransition, Load/ReleaseSceneAnimationResources,
    PlaySceneAnimation, the ISceneAnimationRenderer over the scene buffer, `OpenMidgame`.
  - `.Scramble.cs`: PlayScrambleHangarScene, AnimateScrambleWalk.
  - `.Endings.cs`: ShowCampaignVictorySequence, ShowTigerClawEscapeScene, ShowTheEndScreen,
    InitializeFireworks/TheEndFireWorks.
- `Screens/Scenes/ConversationStage`: stSceneBuffer (320x128 at screen rows 24..151), the subtitle
  strip (rows 152..199) and its text context, InitializeConversationViewport/Text,
  ResetScreenClipToFullHeight, RefreshMemoryStatusOverlay (vertical blank + copy), PanToScreen,
  FadeViewportPaletteToColour, RestoreGamePalette, music requests and a non-positional
  SoundEffectManager for cutscene sounds.
- `Screens/Scenes/ConstellationField`: the 2D star field (init_constellation(0),
  InitializeConstellationField, DrawConstellationField).
- `Screens/Scenes/BriefingMap`: the briefing nav map (nav.c and the brains.c helpers).
- Data layer additions (`Scenes/`): `MissionBriefingData` (lean MODULE.xxx reader + Build_objective_list
  and the objective helpers), `CampaignSceneContext` (ISceneConditions + ITextMacroContext over
  CampaignSession), `IMissionOutcome` (kills and objective flags reported by the flight layer).

Mapping between screen code and the timing primitives: DIBslam+DIBslamReal = `Display.PresentAsync`
(16 fps throttle), RefreshMemoryStatusOverlay = vertical-blank wait + copy, CheckEscaped /
WaitForSceneAdvance / SetFrameTimerPeriodDirect / IsFrameTickElapsed = EventManager/FrameTiming
members, busy loops yield 1 ms per poll.

## Mapping

| C (address, file) | C# | Status |
| --- | --- | --- |
| SceneDirector (0x438C00, screens.c) | `SceneDirector.RunAsync` | done |
| TalkerInit (0x438B90) / FreeTalker (0x438BC0) | `SceneDirector.TalkerInit` / `FreeTalker` | done |
| LoadFace (0x4050B0), LongTalk (0x405290), CloseTalk (0x4054B0), cmpgn.c | `LoadFaceAsync`, `LongTalkAsync`, `CloseTalkAsync` | done |
| CloseLook (0x405DE0), UpdateMap (0x405CC0), cmpgn.c | `CloseLookAsync`, `UpdateMapAsync` | done (CloseLook's shot-0 branch is dead code: shot 0 goes to EstablishingShot) |
| Briefing (0x405660), DeBriefing (0x4056F0), Office (0x405840), cmpgn.c | `BriefingAsync`, `DebriefingAsync`, `OfficeAsync` | done |
| LoadMissionData (0x4059B0, cmpgn.c), Build_objective_list (0x40CED0, brains.c) | `MissionBriefingData.Load` / `BuildObjectiveList`, `SceneDirector.LoadMissionData` | done (local reader, see Requests) |
| LoadBriefingRoom (0x436D00), EstablishingShot (0x437770), DrawBriefingLongShot (0x4378D0), ReturnToBriefingLongShot (0x437980), Dismissed (0x437B80), DrawPodiumShot (0x439070), DrawBriefingCharacter (0x439150), screens.c | same names in `SceneDirector.Briefing.cs` | done |
| DrawDebriefingLongShot (0x437DC0), DebriefingEstablishingShot (0x437F20), screens.c | `SceneDirector.Debriefing.cs` | done |
| ViewMedals (0x436E30), AwardCampaignMedal (0x436F50), DrawMedalChest (0x4370D0), DrawMedalLongShot (0x437250), MedalEstablish (0x4373E0), PinMedal (0x4374B0), DrawMedals (0x4375C0), screens.c | `SceneDirector.Medals.cs` (`ViewMedalsAsync` is public) | done |
| DrawFuneralLongShot (0x439220, screens.c) | `DrawFuneralLongShotAsync` (the office's still and window shots 9, 12..15) | done |
| funeral_player (0x408B90), funeral_wingman (0x408D50), funeral_sequence (0x408DE0), brains.c | `FuneralPlayerFrameAsync`, `FuneralWingmanAsync`, `FuneralSequenceAsync(bool)` | done |
| AnimateScrambleWalk (0x4078D0), PlayScrambleHangarScene (0x4079C0), brains.c | `SceneDirector.Scramble.cs` | done |
| ShowMeanwhileTransition (0x425770, pilot.cpp), LoadSceneAnimationResources (0x424D00), ReleaseSceneAnimationResources (0x424DA0), PlaySceneAnimation (0x425500), logic.c | `SceneDirector.Meanwhile.cs` | done |
| ShowCampaignVictorySequence (0x42FC00), ShowTigerClawEscapeScene (0x430150), screen.c | `SceneDirector.Endings.cs` | 2D parts done; canned 3D scenes are placeholders |
| ShowTheEndScreen (0x4304F0, screen.c), InitializeFireworks (0x42D270), TheEndFireWorks (0x42D2A0), music.c | `TheEndScreenAsync`, `TheEndFireworks` | done |
| InitializeConversationViewport (0x427B20), ResetScreenClipToFullHeight (0x427BA0), InitializeConversationText (0x427BC0), RefreshMemoryStatusOverlay (0x427C30), main.c | `ConversationStage` | done |
| PanToScreen (0x439430, screens.c), FadeViewportPaletteToColour (0x42A700, hudmsg.c), RestoreGamePalette (0x401020, winmain.c) | `ConversationStage.PanToScreenAsync`, `FadeToColourAsync`, `RestoreGamePaletteAsync` | done |
| init_constellation(0) (0x4243E0) / free_constellation (0x424490), logic.c; InitializeConstellationField (0x42D390), DrawConstellationField (0x42D500), music.c | `ConstellationField` | done (scene 0 = star sprites only; the 3D planets of other scenes belong to flight) |
| BriefingMap_DisplayMap (0x40E210), BriefingMap_LoadShapes (0x40E190), DrawNavLocationReadout (0x40DF70), BuildMap (0x40DA00), NavMapPointInsideReservedArea .. DrawNavMapLabels (0x40D090..0x40D540), DrawNavRectangleMarker .. DrawNavCrossMarker (0x40D5A0..0x40D830), SetScreenClipRect (0x40D8C0), DrawNavHazardMarker (0x40D8F0), nav.c; SetScale (0x40CD30), CheckPoint, IncludeNavMapWorldPoint, nav_getxy (0x40CC30), ScaleNavMapCoordinates (0x40CBE0), ScaleNavMapMarkerSize (0x40CBC0), DrawNavTextLine, brains.c | `BriefingMap` | done for the briefing (showFlightData = 0; the in-flight nav scan belongs to flight) |
| objective_name (0x415140), hidden_objective (0x4151F0), mobile_objective (0x415A30), visited/achieved/sighted, set_new_objective (0x4152C0), cockpt.c; nav_note (0x40DF50, nav.c); set_sphere_point (0x40B670, brains.c) | `MissionBriefingData` | done (empty world: no spawned ships) |
| no_objectives_achieved (0x438090), ace_status (0x422010), wing_status (0x4380D0) | `CampaignSceneContext` | done |
| AddPCName (0x404E10), ParseTests (0x438160), ParseMouth/FaceAnimation | `TextMacros`, `ConversationScript`, `AnimationScripts` (existing data layer) + `CampaignSceneContext` | done |

## Deviations

- Every blocking call is an await on the virtual clock (ADR-009); busy loops yield 1 ms per poll.
- The conversation context's string builder is 512 bytes (szDefaultTextBuffer had 200; the longest
  subtitle in the data is 134 characters, so this changes nothing); the nav map contexts have their
  own builders (the original shared szDefaultTextBuffer; no visible effect).
- The briefing map assumes an empty world (no spawned ship, player at the origin); the original
  read stale flight state (aShipPosition[0] of the previous flight or of the attract mode).
- Guards against out-of-range reads: label and reserved-area tables, objective/ship/nav indices,
  text colour index, debriefing personality; the MIDGAME fast-forward is bounded (100000 steps);
  invalid scene-animation flip values are drawn unflipped (the original exited with "bad flip").
- MIDGAME.V06..V08 (Secret Missions 2) are opened by file name: the DOS INSTALL.DAT does not list
  them (logical 69/70 unregistered, 71 is SERIES.VGA). Sequence 8 thus plays MIDGAME.V08 (the
  original's 8-entry table had no entry for it: analysis open question 2 answered). A variant
  whose sections are empty (V04 won, V06 lost) is skipped instead of reading garbage.
- Campaign victory and Tiger's Claw escape: the canned 3D scenes (action spheres 0x12/0x13,
  scripted camera, planet and projectile sprites placed by the 3D eye, hyperspace flash object)
  are placeholders: black space view with the original captions, frame counts (victory: two
  presents per frame like the original), the white jump flash frame and Esc skip. The flag-raising
  animation (TITLE.VGA section 5) is ported.
- The End: firework positions use the cinematic view bounds 319 x 127 (the original read
  stSpaceBuffer's rectangle); the Saga firework sound stub is not played; the flight input pump the
  original installed is not ported.
- The joystick pump SceneDirector installed (PollJoystickButtonEvents) is not ported (no joystick
  support in the event manager yet); the pump is cleared at the end like the original.
- `PlayConversationAsync` (rec room entry) works with or without a caller-allocated scene buffer
  and restores the screen clip it found (the caller still prepares the screen like the original).

## Kept original behaviour worth knowing

- After the first briefing map, the screen viewport keeps the map's top edge (row 4): the rest of
  the briefing is drawn 20 rows higher, with a black band at rows 132..151. UpdateMap restores its
  saved copies of stScreen/stSceneBuffer *before* calling BriefingMap_DisplayMap in the reference,
  so the restore has no effect. Open question: verify against the Kilrathi Saga binary (restoring
  after the call would explain UpdateMap's two consecutive ClearViewport(&stScreen) calls, but would
  have restored a freed scene-buffer pointer).
- Pacing is the Kilrathi Saga one: every DIBslamReal waits for the 16 fps deadline and a script tick
  lasts `ticks * 2 + 1` loop iterations. LongTalk presents once per iteration, CloseLook twice
  (podium) or three times (debriefing officer), so a one-tick phoneme lasts about 190/370/560 ms and
  speech looks slower than in the DOS original (briefing 1.0 runs 178 s without skipping).
- LongTalk/CloseLook skip the first entry of every mouth and face script; an 'R' at the start of a
  face script never loops; an 'R' in the middle restarts at the first entry (which then repeats).
  When the mouth script ends and no face script is running, the record's duration is not held.
- Esc ends the whole scene, any other key or button only the current record (the scramble and the
  funeral ceremony react to Esc only; The End cannot be skipped).
- Skipping "Squadron dismissed" stops the music (ReturnToBriefingLongShot).
- Rec-room talks show `$S` from the mission data last loaded (the original's abSeriesAuxData);
  in a fresh game nothing was loaded yet and it is empty.
- SM2 scripts use shots 31/32 (no TALKING.VGA head of that number): like the original they keep the
  previous shot's handler and head.

## Corrections to docs/analysis/gameflow-screens.md (for integration; the C code wins)

- §4.4/§5: the debriefing plays music 33 when the player scored *more than* 70 % (`> 70`), not ≥.
- §4.4: shots 9 and 12..15 (DrawFuneralLongShot) are the Colonel's office: 9 = the office door
  still, 12..15 = the window view (frame shot - 8 over frame 3, desk frame 8) with the star field.
  Shot 16 (DrawMedalChest) is the hangar deck doors sliding open, not a chest.
- §4.8: `aBriefingPortraitScale` is passed as the rotation angle (degrees), not as a scale.
- §4.5: script entries last `ticks * 2 + 1` loop iterations; the first entry of every mouth and
  face script is skipped; face scripts in the data start with 'R', which therefore never loops.
- §4.10: the Kilrathi Saga briefing map is a 260x156 picture at screen row 4 that replaces the
  room (the DOS game drew the map inside the room's screen).
- §9 question 2: MIDGAME.V06..V08 exist (Secret Missions 2 Firekka scenes); V08 is a valid file.
- §9 question 4: LoadOriginFxDrivers loads CAMP.000 sections 0 and 1 at start-up
  (`pConstellationDefinitions = LoadPacketAllocated(0x3a, 0)`), so a fresh game has them.
- §2.6: stSavedCampaignDate starts as {day 20, year 340} in the reference (the `$E` macro before
  the first medal); CampaignSession initialises it to zero.
- Secret Missions 2 lets the player fly a captured Dralthi (mission ship type 10).

## Open questions

1. The briefing map's lasting top edge (above). If the Kilrathi Saga binary turns out to restore
   the screen viewport after the map, the fix is one line in `SceneDirector.UpdateMapAsync`
   (set `Stage.Screen.Top` back to 24 after `Map.DisplayAsync`).
2. Whether the DOS game ran the cutscenes faster than the Saga's 16 fps (the mouth scripts suggest
   about 30 fps); `FrameTiming.CinematicFrameRate` is the single knob.
3. `IMissionOutcome.Sighted` is false until the flight layer reports it (affects the '?' objective
   names after a flight and tests 27/28 in debriefings).

## Requests (for integration / other work streams)

1. Game should reference Simulation so the scenes can use `MissionModule`/`SpaceSimulation`
   objectives instead of the local `MissionBriefingData` reader (same record layouts), and the two
   endings can run their canned 3D scenes.
2. Flight partial (`GameFlowScreens.Flight.cs`): use `Director.Mission?.PlayerShipType` for
   `BriefedPlayerShipType` (it is the briefed mission's player ship; SM2 has a Dralthi mission);
   expose objective "sighted" flags (wire them into `FlightOutcome.Sighted` in
   GameFlowScreens.Scenes.cs); `DeathSequenceAsync` should call
   `Director.FuneralSequenceAsync(playerFuneral: true)` after the death animation.
3. Duplicates to consolidate: `Screens/Rooms/ConstellationField` and `Screens/Rooms/MedalsView`
   (rooms work stream) port the same C functions as `Screens/Scenes/ConstellationField` and
   `SceneDirector.ViewMedalsAsync`/`DrawMedalsAsync`. The rec room already calls
   `PlayConversationAsync(2, ...)` correctly.
4. A shared `SoundEffectManager` in `Wc1Game`/`GameAudio` (the stage creates its own for
   non-positional cutscene sounds).
5. `GameDirectory`'s packet cache is a plain `Dictionary`; tests run in parallel and share one
   `GameData.Directory`. Make the cache thread-safe (the scene tests run in one xunit collection to
   limit the exposure).

## Next steps (if continued)

1. Port the canned 3D parts of the endings once the flight renderer exists (screen.c 0x42FC00 and
   0x430150; the 2D overlay code is in the reference next to the 3D calls).
2. Replace `MissionBriefingData` with the simulation's mission state when Game references Simulation.
3. Joystick pump for the conversation (PollJoystickButtonEvents) when the event manager has joysticks.
