# Wing Commander 1 — Game Flow, Campaign and Non-Flight Screens

> Corrections found while porting are collected in §10 at the end; they override the text above.


Analysis of the `wc1-re` reconstruction (Kilrathi Saga Win32 build + SDL2 port with DOS data
support) for porting the "between missions" layer to C#/.NET 10. All offsets, constants and
behaviours below were read from the C sources listed in §0.2 and verified where possible
against the GOG DOS data in `<GOG install>/GAMEDAT` (decoded with a small
Python LZW/packet reader, see §2.1).

Sibling analyses cover flight (`nav.c` nav map drawing is only summarised here in §4.10).

---

## 0. Scope, sources, conventions

### 0.1 What this layer is

Everything the player sees that is not the 3D cockpit simulation:

| Area | Original functions (file) |
| --- | --- |
| Process entry, arg parsing, main loop | `GameMain` (`main.c`) |
| Attract/title + main menu | `Title_Sequence`, `DrawTitleLogo`, `UpdateTitleMenuCursor` (`nav.c`) |
| Campaign bookkeeping | `StartNewCampaign`, `GameFlow`, `PostMission`, `UpdateSeries`, `MoveNewCampaign`, `FullMissionScore`, `PlayersMissionScore` (`nav.c`), `ResetCampaignData`, `CorrectPointers` (`killbrd.c`) |
| Rec room (bar) + chalkboard (kill board) | `RecRoom`, `ShowChalkBoard`, room-menu helpers (`killbrd.c`) |
| Barracks (save/load, medals, quit, launch) | `BarracksScreen` and `SaveGame`/`LoadGame` family (`barracks.c`) |
| Mission/briefing packet loading | `LoadMissionData`, `LoadBriefingData`, `Briefing`, `DeBriefing`, `Office` (`cmpgn.c`) |
| Conversation scene director | `SceneDirector`, `ParseTests`, `CloseLook`, `EstablishingShot`, `Dismissed`, medal shots, funeral shots (`screens.c`, `cmpgn.c`) |
| Talking heads | `LoadFace`, `LongTalk`, `CloseTalk`, `ParseMouthAnimation`, `ParseFaceAnimation`, `AddPCName` (`cmpgn.c`) |
| Scene-animation bytecode (MIDGAME.Vxx) | `LoadSceneAnimationResources`, `UpdateSceneAnimationObject`, `FindSceneAnimationCommand`, `PlaySceneAnimation` (`logic.c`), `ShowMeanwhileTransition` (`pilot.cpp`) |
| Scramble / launch / landing | `PlayScrambleHangarScene`, `scramble`, `landing`, `DrawScrambleFrame` (`brains.c`), `LaunchPlayerShip`, `ShowCarrierLaunchSequence` (`sound.c`) |
| Death / ejection / funeral / stranded | `death_sequence` (`screens.c`), `ejection_sequence`, `stranded_sequence` (`cmpgn.c`), `funeral_sequence` (`brains.c`) |
| Endings | `ShowCampaignVictorySequence`, `ShowTigerClawEscapeScene`, `ShowTheEndScreen` (`screen.c`), `TheEndFireWorks` (`music.c`) |
| Medals | `AwardCampaignMedal`, `ViewMedals`, `DrawMedals` (`screens.c`) |
| TrainSim (arcade) menus & pilot name entry | `RunTrainSim` (`system.c`), `pilot.cpp` |
| DOS startup intro (SDL restoration) | `SdlPlayDosStartupIntro` (`sdl/dos_intro.c`) |
| Win32 debug console (not game logic) | `DebugOverlayConsole` (`debug.cpp`) |

### 0.2 Files read

`src/main.c`, `src/nav.c` (1281–2121 fully; 1–1000 for the map), `src/cmpgn.c`, `src/killbrd.c`,
`src/barracks.c`, `src/screens.c` (1–1660; the rest is raster code), `src/screen.c` (595–1215),
`src/brains.c` (1029–2260, 3437–3510, 4399–4600), `src/logic.c` (708–760, 1936–1970, 2578–3035),
`src/sound.c` (246–560, 680–850), `src/music.c` (170–270, 1125–1175), `src/system.c`,
`src/pilot.cpp`, `src/debug.cpp`, `src/sdl/dos_intro.c`, `src/sdl/resources.c`, `src/pload.c`,
`src/disk.c` (1–120, 380–520), `src/hudmsg.c` (fragments), `src/cockpt.c` (text formatter,
objective flags), `src/geom.c` (`AnySavedGames`), `include/wcdata.h`, `include/globals.h`,
`src/globals.c` (tables), `docs/SDL2.md`.

### 0.3 Conventions used in this document

* **Logical file ids** are the numbers the code passes to `FetchDiskPacketRetrying(file, section)`.
  They index `pDiskFileRecords`, which `LoadInstallDat` builds from `INSTALL.DAT` and then
  advances by one (`pDiskFileRecords++`), so *code id = INSTALL.DAT id − 1*. §7 has the table.
* **Packet section** = entry in an Origin packet container (§2.1).
* **Ticks** = 1/60 s. Scene durations, `SetFrameTimerPeriodDirect(n)` and `WaitForSceneAdvance(n)`
  all use 60ths of a second (`milliseconds = period*1000/60`).
* Cinematic frame pacing is 16 fps (`fCinematicFrameRate = 16.0`), space flight 20 fps.
  `nFrameSkip`/`nFrameSkipCounter` throttle redraws inside the animation loops.
* "Escape" means `bEscapePressed` (set asynchronously by the event layer when ESC is pressed)
  or `CheckEscaped()` (true if an ESC/`type 10`, mouse-button/`type 2` or key/`type 3` event is
  queued; it flushes the queue). Almost every cutscene loop polls both and skips on either.
* Input event types seen in this layer: 2 = pointer/joystick button press, 3 = key press,
  5 = key (second channel, treated like 3), 10 = ESC/cancel button, 13 = pointer motion.
  Scan codes: `0x1c` Enter, `0x39` Space, `0x1f` S, `0x2e` C, `0x32` M, `0x24` J, `0x31` N,
  `0x12` E, `0x4c` keypad-5, `0x47/0x48/0x49/0x4b/0x4d/0x4f/0x50/0x51` keypad arrows.
* All game state is 16-bit (`short`) unless stated. Little-endian everywhere.
* Screen is 320×200 indexed colour. Conversation/cutscene scenes use a 320×128 off-screen
  buffer (`stSceneBuffer`, rows 0–127) blitted to screen rows 24–151, with a text area below
  (`stConversationTextViewport`, rows 152–199).

---

## 1. Game state machine

### 1.1 `GameMain` (process entry)

```
GameMain(argc, argv)
  hooks/init; argumentCount = LoadWingCmdrCfgFile()      ; WINGCMDR.CFG words + argv
  chdir gamedat; LoadInstallDat(); chdir ..
  if SDL DOS data or drive letter > 'B': DAT_0059ab34 = 1 (no floppy prompts)
  nMusicPlaybackMode = 4; ResetCampaignData()
  parse arguments (see table)
  SetCinematicFrameTiming(); stCampaignState.currentSeries = series(1); currentMission = mission(0)
  (also mirrored into stInitialCampaignState)
  LoadOriginFxDrivers(); load volume settings from registry
  if animationDemo: RunAnimationDemoLoop(mission)          ; dev cutscene viewer (-w<n>)
  if launchMission: init_mission(series,mission); LaunchPlayerShip(); RunSpaceFlight(); exit  ; dev -l
  [SDL] SdlPlayDosStartupIntro()                            ; DOS orchestra intro (§6)
  bEscapePressed = 0
  for (;;) {
      FrameStartHook(0); bCampaignStartupMode = 1
      selection = Title_Sequence()
      switch selection: 0 → StartNewCampaign(0); 2 → StartNewCampaign(1); 3 → StartNewCampaign(2); default (1) → nothing
      do gameFlowResult = GameFlow() while (gameFlowResult != 0)
  }
```

Startup arguments (`WINGCMDR.CFG` tokens are prepended to argv; `Origin` unlocks dev switches):

| Arg | Effect |
| --- | --- |
| `?` | print version `1.03F-95` then fall into `-` |
| `-m` | show memory status overlay |
| `-b` `-f` `-k` `-q` (dev) | no collision response / show frame rate / invulnerable / no DD mode cascade |
| `A<n>` / `a<n>` | arcade (TrainSim) startup parameter, music mode 2; `AS<n>` = start nav point override |
| `E`/`e` | `bSlowSceneAnimation = 1` (extra frame skip in scene animations) |
| `P`/`p`, `R`/`r` | music playback mode 3 (silent/"P") / 1 |
| `T`/`t` | `bSlowSceneAnimation = 3`; `V`/`v` → 0 |
| `Z`/`z` | `DAT_005a7d9c = 1` (set unconditionally anyway at startup) |
| `l` (dev) | launch straight into flight |
| `m<n>` (dev) | starting mission; `s<n>` starting series (+ `bCampaignActive = 1`); `w<n>` animation demo |

`CheckLauncherAndConfig` (`winmain.c`) also reads `WINGCMDR.CFG`: `$#SAGA.EXE` enables the extra
Kilrathi Saga credit cards, `c` = cockpitless view, and the registry key
`HKLM\Software\Origin Systems\WC: Kilrathi Saga` holds the volume settings and the cheat flag
(callsign is forced to `CHEATER` when set).

### 1.2 Top-level flow diagram

```
                 ┌──────────────────────────────────────────────────────────────┐
                 │ Title_Sequence  (attract loop until ESC, then menu)           │
                 │   returns 0 new game | 1 continue | 2 SM1 | 3 SM2            │
                 └──────────────┬───────────────────────────────────────────────┘
                                │ 0/2/3: StartNewCampaign(c) = Reset + TrainSim(name entry) + load CAMP.c
                                ▼
   ┌────────────────────────── GameFlow() ──────────────────────────────────────────────────┐
   │ loop:                                                                                   │
   │   room = bCampaignStartupMode ? 0 : RecRoom()         (bar: talk / chalkboard / door / sim)│
   │   room==5 → RunTrainSim() ; else BarracksScreen() → 6: return 0 | 7: launch | 8: loop  │
   │ until launch                                                                            │
   │ Briefing(series,mission) → PlayScrambleHangarScene() → scramble() → init_mission()      │
   │ LaunchPlayerShip() → result = RunSpaceFlight(-1)                                        │
   │   1 landed  : ShowCarrierLaunchSequence(carrier); landing(damage)                        │
   │   2 ejected : ejection_sequence(); check_stranded(); [stranded → stranded_sequence; ret 0]│
   │               promotionScore--, elapsedDate.year++ (ejection count), office visit        │
   │   3 stranded: stranded_sequence(); return 0                                              │
   │   4 killed  : death_sequence(); funeral_sequence(player); bCampaignActive=0; return 0    │
   │   5/other   : (Esc in TrainSim) free; return 0                                           │
   │ PostMission(); UpdateSeries()  (stats, badges, series branch, medal check)               │
   │ promotion roll; DeBriefing(); rank++ if promoted                                         │
   │ nextSeries == -1 → [medal] → ending (victory 0x40 / escape 0x41 / MIDGAME) → TheEnd → ret 0│
   │ wingman died → funeral_sequence(0); office visit → Office(); medal → AwardCampaignMedal  │
   │ post-series MIDGAME → ShowMeanwhileTransition(seq, failed)                               │
   │ advance series/mission; MoveNewCampaign() (date); AddRandomTrainSimHighScores(); ret 1   │
   └─────────────────────────────────────────────────────────────────────────────────────────┘
   GameFlow()==0  →  back to Title_Sequence (campaign over / abandoned)
```

### 1.3 `Title_Sequence` return codes and the menu

`Title_Sequence` plays the attract sequence (§5.1) until ESC/click, then shows the menu built
from `TITLE.VGA` section 4 (code logical file 9): `menuOptions[0] = 0` ("start new game",
frame 0) always; `menuOptions[1] = 1` ("continue"/go to barracks, frame 1) only when
`AnySavedGames()` finds an occupied slot in `SAVEGAME.WLD`; remaining entries −1 (hidden).
Options ≥ 3 would draw frame 0 of logical file `0x4b` (75 = `TITLE1.VGA`, Secret Missions
title art) but the Win32 code never fills them, so return values 2/3 are unreachable from the
menu as compiled (the `C`/`M` hotkeys are gated on `menuOptions[2] != -1`). `AnySavedGames`
also sets `DAT_005a7d9c = 1` if any save has `campaignIndex > 0`.

Return value = `menuOptions[selected] + 1 − 1`: **0** new campaign (Vega), **1** continue
(no `StartNewCampaign`; `bCampaignStartupMode = 1` makes `GameFlow` skip the rec room and go to
the barracks so the player can "Awaken" a save), **2**/**3** Secret Missions 1/2.

### 1.4 `StartNewCampaign(campaign)`

```
bCampaignActive = 1
ResetCampaignData()                 ; copy stInitialCampaignState + 9 initial PilotRecords, new TrainSim scores, fix pointers
bCampaignStartupMode = 1
RunTrainSim()                       ; forced arcade session (score 4000, mission 2, 25 s) → name/callsign entry
stCampaignState.campaignIndex = nCampaignDataSet = campaign
bCampaignStartupMode = 0
LoadPacketIntoBuffer(asCampaignPilotFiles[campaign], 1, pMissionCampaignData)   ; CAMP.xxx section 1 (series table)
bPanRoomTransition = 0; nPendingCampaignIndex = -1
```

`asCampaignPilotFiles = {58, 61, 74}` (CAMP.000/001/002), `asCampaignBriefingFiles = {10, 62, 73}`
(BRIEFING.000/001/002), `asMissionDataFiles = {15, 52, 72}` (MODULE.000/001/002).
Note `LoadGameFromSlot` additionally loads CAMP section 0 into `pConstellationDefinitions`;
`StartNewCampaign` does not (the constellation definitions for a fresh game come from whatever
was loaded before — see Open questions).

### 1.5 `GameFlow()` in detail

Per-iteration reset: `bPlayerEjectedThisMission = 0`, `nPostSeriesSequence = -1`,
`bPromotionPending = 0`, `nPendingMedalIndex = -1`, `bOfficeVisitPending = 0`,
`bPlayerShipTypeChanged = 0`. If `nPendingCampaignIndex != -1` (a save was loaded) the
campaign index/data set are taken from it.

Room loop:

| `RecRoom()` result | Meaning |
| --- | --- |
| 4 (barracks door) or anything else | `BarracksScreen()` |
| 5 (simulator) | `RunTrainSim()` then back to the rec room |

| `BarracksScreen()` result | Meaning |
| --- | --- |
| 7 ("Mission Hangar") | launch the mission |
| 8 ("Return to the Bar") | loop back to `RecRoom` |
| 6 | documented exit to title (`return 0`) but never produced by the Win32 `BarracksScreen`; "Quit Wing Commander" calls `exit_squadron` directly |

Before the briefing: `nDebriefingPersonality = series record +0` (the wingman shown in the
debriefing long shot). Then `Briefing → PlayScrambleHangarScene → playerShipType =
aMissionShips[nPlayerMissionShipIndex].type → scramble → init_mission → LaunchPlayerShip →
RunSpaceFlight(-1)`.

`RunSpaceFlight` returns `nArcadeState`:

| Code | Set by | Meaning |
| --- | --- | --- |
| 1 | `spc.c` (within 700 units of the carrier, facing it) / TrainSim wave end | landed / returned |
| 2 | `hudmsg.c` — Ctrl+E (scan 0x12) when the ejection system isn't destroyed; succeeds with probability `1/(damage+1)` | ejected |
| 3 | `check_stranded` — carrier destroyed and no enemy within 30000 | stranded (only after eject) |
| 4 | `ship.c` player destroyed; TrainSim time out | killed |
| 5 | `HandleSpaceFlightControls` returned −1: Esc in the training simulator only (Win32 Alt+X posts WM_QUIT and ends the process; the SDL port ignores Alt+X; see flight-ui.md §1) | quit flight |

After landing: `ShowCarrierLaunchSequence(nPlayerCollisionObject)` (approach to the Tiger's
Claw, §5.9), then `landing(calculate_damage_level())` where damage level 0–3 is derived from
armour/damage percentages (`<5 → 0, <40 → 1, <70 → 2, else 3`).

After ejection: `promotionScore = max(0, promotionScore−1)`, `elapsedDate.year++` (this field
is the **ejection counter**, see §2.6), first ejection (`== 1`) queues the Golden Sun
(`nPendingMedalIndex = 3`), `bOfficeVisitPending = 1`.

Then `PostMission()`, `UpdateSeries()` (§2.4/§2.5). `UpdateSeries` advances
`currentSeries/currentMission`; `GameFlow` stashes the new values in `nextSeries/nextMission`
and temporarily restores the flown series/mission so the debriefing, funeral, office and medal
scenes evaluate against the mission just flown.

Promotion roll (only if not ejected): `if RandomInRange(0,5) + promotionScore > 7 →
promotionScore = 0; bPromotionPending = rank < 3 (campaign 0) or rank < 4 (SM1/SM2);
bOfficeVisitPending |= bPromotionPending`. `DeBriefing` runs, then `rank++` if promoted.

Campaign end (`nextSeries == -1`): optional medal ceremony, then by `nPostSeriesSequence`:
`-1` nothing; `0x40` `ShowCampaignVictorySequence()` (fireworks on "The End"); `0x41`
`ShowTigerClawEscapeScene()`; otherwise `ShowMeanwhileTransition(seq, bSeriesFailed)` with
fireworks iff the series was failed (`flightResult = bSeriesFailed >= 1` — the "fireworks"
argument is literally the failure flag). `ShowTheEndScreen(flag)`, `bCampaignActive = 0`, return 0.

Otherwise: wingman funeral (if `nWingmanKilledThisMission`), `Office()` if pending, medal
ceremony if pending, `ShowMeanwhileTransition` if the series ended with a MIDGAME id, commit
`nextSeries/nextMission`, `MoveNewCampaign()` (date), `AddRandomTrainSimHighScores()`,
`bPanRoomTransition = 1` (rec room fades in), return 1.

### 1.6 Where the blocking happens

Every screen is a self-contained blocking loop that: loads its packets, runs
`while (!done) { draw; PollInputEvent; DIBslam(); DIBslamReal(); }`, releases packets,
returns a small integer. `DIBslam/DIBslamReal` present the frame and pace it. Cutscenes use
`nFrameSkipCounter` to redraw every `nFrameSkip` iterations and `WaitForSceneAdvance(ticks)`
to hold a frame until a timer expires or the player presses a key/button.

---

## 2. Campaign data model

### 2.1 Packet container format (all `.VGA`, `CAMP.xxx`, `BRIEFING.xxx`, `MODULE.xxx`, `MIDGAME.Vxx`, …)

```
u32 declaredFileSize
u32 entry[0..N-1]        ; top byte = compression, low 24 bits = absolute offset of section
                         ; N = (entry[0].offset − 4) / 4 ; section i spans entry[i]..entry[i+1] (last → file size)
compression: 1 = Origin LZW (section data = u32 uncompressedSize + LZW stream)
             0xff = empty placeholder (offset equals the next entry, length 0)
             anything else (0, 2, 0xe0) = raw
```

Origin LZW: 9→12-bit codes, LSB-first bit packing, `0x100` clear, `0x101` stop, dictionary
starts at `0x102`, width grows when `dictionarySize == 1<<width` (max 12), the classic
KwKwK case emits `previousCode` + first byte. Reference decoder: `SdlDecompressOriginLzw`
(`src/sdl/resources.c`). Kilrathi Saga data is stored uncompressed; DOS data uses LZW for
`CAMP`, `BRIEFING`, `MODULE`, `INTRO.DAT`. `SdlUsingDosData()` detects DOS data by
`MODULE.000` byte 7 == 1 (compression flag of the first entry).

Verified: `CAMP.000` = 651 bytes, 3 sections, all LZW: sec 0 → 416 bytes, sec 1 → 1170 bytes,
sec 2 → 104 bytes (same decoded sizes for CAMP.001/002).

### 2.2 `CAMP.xxx` sections

| Section | Decoded size | Loaded into | Content |
| --- | --- | --- | --- |
| 0 | 416 = 13 × 32 | `pConstellationDefinitions` (`LoadGameFromSlot`) | per series: 4 × `ConstellationObjectDefinition {s16 shapePacket (−1 none), s16 yaw, s16 pitch, s16 roll}` — planets (`PLANETS.VGA` section index) drawn around the rec-room/conversation star field |
| 1 | 1170 = 13 × 90 | `pMissionCampaignData` | series table (below) |
| 2 | 104 = 13 × 4 × 2 | `pRecRoomRoster` (temporary in `RecRoom`) | per (series, mission): two `s8` personality ids of the pilots sitting in the bar; −1 = nobody. Index `(mission + series*4)*2 − 8` |

Series are numbered **1..13**; record for series `s` is at `(s−1)*90`. Code addresses it as
`pMissionCampaignData + s*0x5a − 0x5a`.

**Series record (90 bytes)**

| Offset | Type | Field | Use |
| --- | --- | --- | --- |
| +0 | s16 | `debriefPersonality` | wingman portrait shown in the debriefing long shot (`nDebriefingPersonality`) |
| +2 | s8 | `missionCount` | when `currentMission >= missionCount` the series ends |
| +3 | s16 | `scoreThreshold` | `seriesScore < threshold` → series failed |
| +5 | s8 | `postSeriesSequence` | −1 none; 0–7 `MIDGAME.V0n` "Meanwhile…" cutscene; `0x40` campaign victory ending; `0x41` Tiger's Claw escape (loss) ending |
| +6 | s8 | `winNextSeries` | next series on success (−1 = campaign over) |
| +7 | s8 | `winShipType` | player ship on success (0 Hornet, 1 Rapier, 2 Scimitar, 3 Raptor) |
| +8 | s8 | `loseNextSeries` | next series on failure |
| +9 | s8 | `loseShipType` | player ship on failure |
| +10 + m*20 | mission block m (0..3) | | |
| +0 | s16 | `medalIndex` | medal awarded if earned this mission (0 Bronze Star, 1 Silver Star, 2 Gold Star, 3 Golden Sun, 4 Terran Medal of Valor) |
| +2 | s16 | `medalThreshold` | `nMissionMedalScore >= threshold` → medal (9000+/2000 = never) |
| +4 | s8[16] | `objectiveScore[16]` | points per mission objective (indexed like `aMissionObjectives`) |

The code addresses mission block fields as `base + series*0x5a + mission*0x14 − 0x50` (= record
+10 + mission*20) and objective scores as `scores[objective + 4]`.

**Decoded Vega campaign (CAMP.000)** — win/lose branch, ship, threshold, ending:

| Series | Missions | Threshold | Win → (series, ship) | Lose → (series, ship) | postSeq | Debrief wingman |
| --- | --- | --- | --- | --- | --- | --- |
| 1 Enyo | 2 | 10 | 2, Scimitar | 3, Hornet | −1 | 0 Spirit |
| 2 McAuliffe | 3 | 32 | 4, Raptor | 5, Scimitar | 0 | 5 Paladin |
| 3 Gateway | 3 | 35 | 5, Scimitar | 6, Hornet | 0 | 5 Paladin |
| 4 Gimle | 3 | 25 | 7, Raptor | 5, Scimitar | 1 | 4 Angel |
| 5 Brimstone | 3 | 25 | 7, Raptor | 8, Scimitar | 1 | 6 Maniac |
| 6 Cheng-Du | 3 | 35 | 5, Scimitar | 8, Scimitar | 1 | 4 Angel |
| 7 Dakota | 3 | 50 | 9, Rapier | 10, Raptor | 2 | 7 Knight |
| 8 Port Hedland | 3 | 50 | 10, Raptor | 11, Scimitar | 2 | 7 Knight |
| 9 Kurasawa | 3 | 40 | 12, Rapier | 10, Raptor | 3 | 2 Bossman |
| 10 Rostov | 3 | 40 | 12, Rapier | 13, Scimitar | 3 | 3 Iceman |
| 11 Hubble's Star | 3 | 65 | 10, Raptor | 13, Scimitar | 3 | 2 Bossman |
| 12 Venice (win) | 4 | −1 | −1 | −1 | 0x40 victory | 1 Hunter |
| 13 Hell's Kitchen (lose) | 4 | −1 | −1 | −1 | 0x41 escape | 1 Hunter |

(System names are not in CAMP — they come from `MODULE.xxx` section 5, §2.3; the names above
are the known Vega campaign tree, which matches the decoded branch structure exactly.)
Per-mission medal rows, e.g. series 2 mission 2: Silver Star at 80 points; series 12 mission 3:
Terran Medal of Valor at 245. Series 1 mission 0 objective scores `[2,1,2,1]` (full = 6).

SM1 (`CAMP.001`): 8 series, linear 1→2→3→4→5→6→7 with every failure going to series 8;
series 1 (threshold 9999, postSeq 4) and series 7/8 (postSeq 5, next −1). SM2 (`CAMP.002`):
9 series; series 1 postSeq 6, series 6 postSeq 7, series 8/9 postSeq 8 (see Open questions
about MIDGAME.V08). Rows 9–13 of SM1 and 10–13 of SM2 are zero-filled.

Constellation definitions in CAMP.000 series 1: planets `(2, 0,0,0)` and `(3, 150,30,0)`.

### 2.3 Mission data (`MODULE.xxx`) as used by this layer

`LoadMissionData(series, mission)` with `missionIndex = mission + series*4` (series ≥ 1, so the
first real mission is index 4; the table has room for 64):

| Section | Stride | Record |
| --- | --- | --- |
| 0 | 0x18 | header: `s16 entryNavPoint, s16 homeMissionShip, s16 playerMissionShip, s16 initialMissionShips[8], s16 field_16` |
| 1 | 0x4d0 (16 × 77) | nav points (`MissionNavPointDisk`) |
| 2 | 0x400 (16 × 64) | objectives: `s16 type (−1 end; 0 nav point, 1–4 ship kinds), s16 index, char description[60]` |
| 3 | 0x540 (32 × 42) | mission ships |
| 4 | 0x28 | mission name text (`abMissionAuxData`, shown as `* name *` on the nav map) |
| 5 | 0x28 per **series** | system name (`abSeriesAuxData`, `$S` macro, "System: …") |

`Build_objective_list` (`brains.c`) converts the 16 disk objectives into runtime
`MissionObjective` records (map X/Y via `nav_getxy`, display name from the nav point or ship
type name, `abFlightPath[]` order) — these are what the briefing map draws and what the save
game stores.

### 2.4 Scoring and series branching

* `stCampaignState.missionScore` accumulates `affect_mission_score` events during flight
  (kills: Salthi 7, Dralthi/Krant 10, Gratha/Jalthi 15, 25/50/75 for bigger targets, etc.);
  `nMissionMedalScore` counts only the player's own contribution; `personality_killed` of an
  enemy ace adds 25 and `promotionScore++`.
* Objective flags (`aMissionObjectives[i].flags`): 1 visited, 2 achieved, 4 sighted.
* `FullMissionScore()` = Σ objectiveScore[0..15]; `PlayersMissionScore()` = Σ over achieved
  objectives. The debriefing music is the "good" track (0x21) if player ≥ 70 % of full
  (or full == 0), else 0x22.
* `UpdateSeries()`:
  1. `stSavedCampaignDate = currentDate`; `promotionScore++` if player score == full score.
  2. `seriesScore += playerScore; currentMission++`.
  3. If `currentMission >= missionCount`: push `currentSeries` onto `seriesHistory[seriesHistoryCount++]`
     (max 8 entries, **unchecked**); `failed = seriesScore < threshold`; pick
     `(currentSeries, playerShipType)` from the win or lose pair; `bSeriesFailed = failed`;
     if the ship type changed → `bPlayerShipTypeChanged = bOfficeVisitPending = 1`;
     `seriesScore = 0; currentMission = 0`; `nPostSeriesSequence = record+5` **but** it is cleared
     to −1 when `pMissionCampaignData[newSeries*0x5a + 5] == nPostSeriesSequence && seq < 0x40`
     (as written this reads byte +5 of the record *after* the new series — see Open questions).
  4. Medal: if a wingman died this mission `nMissionMedalScore = max(0, missionScore − 15)`;
     if `medalThreshold <= nMissionMedalScore` and no medal pending → `nPendingMedalIndex =
     medalIndex`, `stSavedCampaignDate = currentDate` (used by `$E`).

### 2.5 `PostMission()` — pilot statistics and badges

```
oldKills = player.kills
badge 7 (FIVE_KILLS)        if oldKills < 5  and oldKills + nPlayerKillCount > 4
else badge 8 (25 KILLS)     if oldKills < 25 and oldKills + nPlayerKillCount > 24
badge 3 + playerShipType    (first flight in each fighter: 3 Hornet, 4 Rapier, 5 Scimitar, 6 Raptor)
player.missions++ ; missions == 1 → badge 2 (FIRST_MISSION) *and falls through to* badge 9; 5 → badge 9; 10 → badge 10; 15 → badge 11
player.kills += nPlayerKillCount ; promotionScore++ for every 5-kill boundary crossed
for personality 0..7:
   if it is your wingman: missions += 1, kills += nWingmanKillCount
   else if alive (personalityDeathMission == 0): missions += rand(0..2); kills += (missions ? rand(0..nPlayerKillCount) : 0)
```

Badges 0 and 1 are set from the start (`stInitialCampaignState.badges = {1,1,0,…}`).
`DrawMedals` draws badge `i` with sprite frame `13+i` and medal `m` with frames `28+m`
(stacked `25+m` ribbons for the three stars), rank insignia frame `rank + 33`.

### 2.6 `CampaignState` (runtime, 0x58 bytes) and the initial values

| Offset | Field | Initial | Notes |
| --- | --- | --- | --- |
| +0x00 | `PilotRecord *currentPilot` | → `aPilotRecords[8]` | fixed by `CorrectPointers` |
| +0x04 | `enum ObjectType playerShipType` | 0 Hornet | |
| +0x08 | `u8 medals[5]` | 0 | counts per medal type (stars stack) |
| +0x0D | `u8 badges[12]` | `{1,1,0…}` | §2.5 |
| +0x19 | `s8 currentMission` | 0 | 0..3 |
| +0x1A | `s8 currentSeries` | 1 | 1..13 |
| +0x1B | `s8 seriesHistoryCount` / +0x1C `s8 seriesHistory[8]` | 0 | series flown so far |
| +0x24 | `int personalityDeathMission[8]` | 0 | 0 = alive, else `mission + series*4` of death |
| +0x44 | `u8 aceFlags[4]` | `{1,1,1,1}` | per enemy ace: bit1 alive, bit2 killed, bit4/8/0x20 comm-greeting state |
| +0x48 | `CampaignDate currentDate {s16 day, s16 year}` | `{110, 2654}` | `$D` → `2654.110` |
| +0x4C | `CampaignDate elapsedDate` | `{6, 0}` | **misnamed**: low byte of `.day` = hour, high byte = minute (`$T` → `06:00`, nav map "Standard time"); `.year` = number of ejections |
| +0x50 | `s16 promotionScore` | 0 | |
| +0x52 | `s16 missionScore` | 0 | reset by `prepare_mission` |
| +0x54 | `s16 seriesScore` | 0 | |
| +0x56 | `s16 campaignIndex` | 0 | 0 Vega, 1 SM1, 2 SM2 |

`PilotRecord` (0x26): `char name[14]; char callsign[14]; s16 portrait; s16 rank; s16 missions;
s16 kills; s16 personality`. Initial table (index = personality id):

| # | Name | Callsign | portrait | rank | missions | kills | last |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | TANAKA | SPIRIT | 3 | 1 | 11 | 14 | 1 |
| 1 | ST.JOHN | HUNTER | 4 | 2 | 25 | 32 | 4 |
| 2 | CHEN | BOSSMAN | 1 | 3 | 35 | 37 | 2 |
| 3 | CASEY | ICEMAN | 0 | 3 | 28 | 43 | 1 |
| 4 | DEVEREAUX | ANGEL | 0 | 2 | 22 | 20 | 1 |
| 5 | TAGGART | PALADIN | 2 | 3 | 42 | 34 | 2 |
| 6 | MARSHALL | MANIAC | 4 | 0 | 5 | 6 | 1 |
| 7 | KHUMALO | KNIGHT | 3 | 2 | 18 | 23 | 3 |
| 8 | PELLEY | GOBLIN | 0 | 0 | 0 | 0 | 0 | ← player placeholder, overwritten by name entry (defaults "Blair"/"Maverick") |

Ranks: 0 `2ND LT.`, 1 `1ST LT.`, 2 `CAPTAIN`, 3 `MAJOR`, 4 `LT. COL.` (cap 3 in Vega, 4 in SM).
`apWingmanPilots[i] = &aPilotRecords[i]` (i<8).

### 2.7 Dates and time

`MoveNewCampaign()` after every completed mission: `day += rand(0..1)` normally, or
`rand(0..1) + 5` when the mission that just ended closed a series (`currentMission == 0`
after `UpdateSeries`). `day >= 366 → day −= 365, year++`. The hour/minute bytes are not
advanced by this layer (in flight `FormatNavCoordinates` writes the game clock into them).

### 2.8 Pilot death and funerals

* `personality_killed(p)` (`hudmsg.c`) for p < 8: `personalityDeathMission[p] = mission + series*4`
  (never 0 because series ≥ 1), `promotionScore = max(0, −1)`. Your own wingman also sets
  `nWingmanKilledThisMission = 1` (`ship.c`).
* `wing_status(p)`: 3 alive, 1 died this mission, 2 died earlier, otherwise returns the current
  mission index (a death recorded "in the future" — effectively only from loaded saves).
* Dead pilots: not seated in the rec room (label blank), "KIA" on the chalkboard, no random
  stat increments, excluded from TrainSim high-score generation, conversation tests 4/5/29/30
  branch on them.
* Wingman funeral: `funeral_sequence(0)` after debriefing, using BRIEFING section 0 pairs
  [12,13] (opening) and [10,11] (after the volleys).
* Player death: `death_sequence` (cockpit explosion + external view) → `funeral_sequence(1)`
  with pairs `[2 + 2*asFuneralSceneBySeries[series]]` and `[0,1]`; "THE END" subtitle after
  frame 110; campaign ends. `asFuneralSceneBySeries[15] = {0,0,1,1,1,1,1,2,3,2,3,3,2,3,0}`.

---

## 3. Save game format — `SAVEGAME.WLD`

File = **8 slots × 828 (0x33C) bytes = 6624 (0x19E0) bytes**. `EnsureSaveGameFile` recreates
the file if it is missing or its length is not 0x19E0; `CreateEmptySaveGameFile` writes 8 records
with `description = "game <n>"` and `occupied = 0` (everything else uninitialised — the GOG
DOS file visibly contains stack garbage in empty slots; `CRUSADE.WLD` in the same directory is
a zero-filled copy of the same layout and is never referenced by the code). The slot index is
the barracks bunk (0..7). All multi-byte values little-endian.

```
SaveGameDiskRecord (0x33C)
+0x000  char description[17]              slot name typed by the player (max 16 chars + NUL)
+0x011  u8   occupied                     1 = valid
+0x012  PilotRecord pilots[9]             9 × 0x26 (see §2.6; [8] is the player)
+0x168  SaveGameDiskCampaignState (0x44)
        +0x00 s16 currentPilot            truncated pointer, ignored on load (CorrectPointers)
        +0x02 s16 playerShipType
        +0x04 u8  medals[5]
        +0x09 u8  badges[12]
        +0x15 s8  currentMission
        +0x16 s8  currentSeries
        +0x17 s8  seriesHistoryCount
        +0x18 s8  seriesHistory[8]
        +0x20 s16 personalityDeathMission[8]
        +0x30 u8  aceFlags[4]
        +0x34 s16 currentDate.day, +0x36 s16 currentDate.year
        +0x38 s16 elapsedDate.day (hour | minute<<8), +0x3A s16 elapsedDate.year (ejections)
        +0x3C s16 promotionScore
        +0x3E s16 missionScore
        +0x40 s16 seriesScore
        +0x42 s16 campaignIndex           0/1/2 selects CAMP/BRIEFING/MODULE set
+0x1AC  SaveGameDiskObjective objectives[16]   16 × 0x19
        +0x00 s16 mapX, +0x02 s16 mapY, +0x04 u8 field_4, +0x05 s16 type, +0x07 s8 index,
        +0x08 u8 flags (1 visited, 2 achieved, 4 sighted), +0x09 s16 displayName (truncated ptr),
        +0x0B s16 name (truncated ptr), +0x0D FixedVector position (3 × s32)
= 0x33C
```

Semantics:

* **Save** (`SaveGameWithNamePrompt`): requires `bCampaignActive`, else modal "Load a game
  first."; default name = previous bunk label with "Awaken " and trailing "." stripped; prompt
  "Game Name: " (16 chars, upper-cased, letters/digits/space); writes pilots, campaign state and
  the current `aMissionObjectives` (0x1F0 bytes copied) — the objective pointers are
  meaningless on disk and are rebuilt by `LoadMissionData/Build_objective_list` before use.
* **Load** (`LoadGameFromSlot`): modal "Loading Game..."; copies pilots + campaign; sets
  `nPendingCampaignIndex = nCampaignDataSet = campaignIndex`; loads `CAMP.<c>` section 0
  (constellations) and section 1 (series table); copies objectives; `CorrectPointers()`;
  `bCampaignActive = 1`; cheat registry → callsign "CHEATER". Validity = file opened, read ok,
  `occupied != 0`.
* `AnySavedGames()` (title menu) loads all 8 slots.
* TrainSim high scores are **not** saved.

Bunk labels (`apszBarracksMenuLabels`): even index = `"Awaken <description>."`, odd =
`"Save this campaign  "`; unoccupied bunks show the save label on both halves.

---

## 4. Briefing / conversation scene engine

### 4.1 `BRIEFING.xxx` packet layout

Logical file `asCampaignBriefingFiles[campaign]`. Verified in `BRIEFING.000`: 56 sections.

| Section | Content | Header |
| --- | --- | --- |
| 0 | funerals | 14 × u32: [0,1] player eulogy follow-up (scene,text); [2..9] four (scene,text) pairs selected by `asFuneralSceneBySeries[series]`; [10,11] wingman follow-up; [12,13] wingman opening |
| 1 | Colonel's office (promotion / new ship / ejection reprimand) | 2 × u32 (scene, text) — `Office()` reads them as `BriefingPacketHeader.briefingScene/Text` |
| 2 | medal ceremony | 2 × u32 (scene, text) |
| 3 | empty (0xff) | |
| 4 + (series−1)*4 + mission | one per mission (`section = mission + series*4`); 0xff for missions that don't exist in that series | `BriefingPacketHeader` 10 × u32 |

`BriefingPacketHeader` (0x28): `briefingScene, briefingText, debriefingScene, debriefingText,
recRoomScene0, recRoomText0, recRoomScene2, recRoomText2, recRoomScene1, recRoomText1` — all
offsets from the start of the section. Note the rec-room order 0, **2**, 1 (Shotglass, right
pilot, left pilot). `LoadBriefingData` keeps the whole section in memory (`pBriefingPacket`)
and derives the six pointers; `RecRoom` and `Briefing/DeBriefing` each load and release it.

Example (`BRIEFING.000` section 4 = series 1 mission 0): header `(40, 365, 2696, 3112, 6380,
6497, 7348, 7452, 8462, 8605)`; the briefing has 25 records.

### 4.2 Scene record (`ConversationSceneRecord`, 13 bytes, packed)

| Off | Type | Field | Meaning |
| --- | --- | --- | --- |
| 0 | s8 | `shot` | camera/shot id (table §4.4). `−1` = keep previous shot; `−2` = end of scene. Bit `0x40` = draw the comm overlay (`TALKING.VGA` section 11) over the talking head; the low 6 bits are the shot |
| 1 | s8 | `textColour` | index into `asConversationTextColours[24]` (`−1` = unchanged). 0 = 0x25 blue (narration/Colonel), 1 = 0xb6 magenta, 9 = 0x47 yellow (player), 11 = 0x0b light grey (captions), … |
| 2 | s8 | `talker` | **context id**, not a speaker: selects the backdrop frame and overlays in `LoadFace` (0→frame 4, 1→5, 2→0 + star field, 4/9→2, 8→1 + star field, 3/11/13→1, 10/12→0, 5/6 funeral overlays, other → black). `−2` = unchanged. For shot 4 (map) it is the objective index to highlight (negated allowed) |
| 3 | s16 | `duration` | ticks (1/60 s) to hold after the text has been shown; skippable |
| 5 | s16 | `testsOffset` | 0 = none, else offset into the text block of a test string (§4.3) |
| 7 | s16 | `textOffset` | offset of the subtitle (NUL-terminated; empty = no display step) |
| 9 | s16 | `mouthAnimationOffset` | mouth script (§4.5) |
| 11 | s16 | `faceAnimationOffset` | face script (§4.5) |

Records are read sequentially; a test can redirect to another record index (`sceneData + n`).

### 4.3 Test strings (`ParseTests`)

Binary byte = test code, followed by decimal ASCII arguments each terminated by `,`
(`int_value` stops at `,` or `)`); the string ends with a NUL. Tests are evaluated in order; the
first one that fires returns `record[n]`; code 0 ends the list (record stays).

| Code | Args | Jump to `n` when … |
| --- | --- | --- |
| 1 | n | always (goto) |
| 2 | v, n | `missionScore < v` |
| 3 | v, n | `missionScore >= v` |
| 4 | p, n | `wing_status(p) != 3` (pilot p dead) |
| 5 | p, n | pilot p alive |
| 6 / 7 | n | player kills == 0 / != 0 |
| 8 / 9 | n | wingman kills == 0 / != 0 |
| 10 | n | no office visit pending |
| 11 / 12 | o, n | objective o not achieved / achieved |
| 13 | n | medal being awarded is 4 (TMV) |
| 14 | n | medal < 3 (a star) |
| 15 | n | medal == 3 (Golden Sun) |
| 16 | n | no promotion pending |
| 17 | n | player did not eject |
| 18 | n | ejected and it was the first ejection |
| 19 | n | ship type did not change |
| 20 / 23 / 21 / 22 | n | playerShipType != Hornet / Rapier / Scimitar / Raptor |
| 24 | n | not flying a Rapier and new ship index < previous (demoted) |
| 25 | n | flying a Rapier or ship index >= previous |
| 26 | n | ejected and not the first time |
| 27 / 28 | o, n | objective o sighted / not sighted |
| 29 / 30 | p, n | pilot p died earlier / died this mission |
| 31 / 32 | a, n | ace a dead (`!ace_status(a,1)`) / alive |
| 33 | a, n | ace a neither killed nor alive flags |
| 34 | a, n | ace a has been killed (bit 2) |
| 35 / 36 | n | player score == full / < full |
| 37 / 38 | n | no objective achieved / at least one |

Example from the real data: `b'\x040,5,'` = "if Spirit (0) is dead goto record 5";
`b'$10,'` = test 36 → record 10; `b'\r02,\x0f06,'` = medal-type branches.

### 4.4 Shot ids and the handlers `SceneDirector` dispatches

`SceneDirector(sceneType, scene, text)` with `sceneType` 0 briefing, 1 debriefing, 2 rec room,
3 funeral, 4 office, 5 medal, 6 meanwhile (`nConversationSceneType`, used by `CloseTalk` to pick
the backdrop clearing rule). Loop per record: apply tests, update `nConversationCharacter`,
"prepare" the shot (table), set text colour, parse mouth/face scripts, then if the text is
non-empty run the handler for the *current* shot with `(text, duration)`:

| Shot | Prepare | Handler when text present | Visual |
| --- | --- | --- | --- |
| 0 | — | `EstablishingShot` | briefing room long shot: 22-frame intro animation (`BRIEFING.VGA` sec 1 frames 0–21 at (241,60), frame 22 at (241,64)), 8 seated characters animated (§4.8); music 25 |
| 1 | `DrawBriefingLongShot` once | `CloseLook(shot 1)` = text only | static long shot |
| 2 | `DrawPodiumShot` once | `CloseLook(shot 2)`: mouth script drives `BRIEFING.VGA` sec 3 frames at (225,34) | Colonel at the podium |
| 3 | — | `Dismissed` → afterwards shot becomes 4 | panels slide apart, podium animation (`abBriefingPodiumFrames`), reveals the computer |
| 4 | `cCurrentObjective = |talker|` | `UpdateMap` → `BriefingMap_DisplayMap` | nav map with highlighted objective (§4.10) |
| 5 | — | `ReturnToBriefingLongShot` → shot 1 | characters stand up (12-pose animation), music 26 |
| 6 | — | `DrawMedalLongShot` (mouth script animates `MEDAL` sec 8 frames +1 at (121,8)) | hangar deck ceremony |
| 7 | — | `MedalEstablish` | zoom on medal (32 frames) |
| 8 | — | `PinMedal` | pinning (3-frame cycle 38–40) |
| 9 | — | `DrawFuneralLongShot` (static frame 0) | |
| 10 | — | `DebriefingEstablishingShot` (48-frame pan, `abDebriefingEstablishDeltas`) | debriefing room |
| 11 | — | `CloseLook(shot 11)`: `DrawDebriefingLongShot` + podium frames 17+ | Colonel in debriefing |
| 12–15 | star field | `DrawFuneralLongShot(shot)`: backdrop 3, frame `shot−8`, 8 | funeral panels |
| 16 | — | `DrawMedalChest` (doors open over 81 frames, then medal music 38/39/40) | |
| 17 | — | `funeral_wingman` (runs `funeral_player` frames) | |
| 20–30 | `LoadFace(shot−20)` | `LongTalk` (default) | talking head (§4.5) |
| 50–59 | — | `PlaySceneAnimation(shot−50)` | scene-animation bytecode (§4.9) |
| 0x40 \| n | sets `bConversationOverlay` | as `n` | comm-screen overlay |

Any other shot (`−1`, unknown) keeps the previous handler. ESC ends the whole scene.
`WaitForSceneAdvance(duration)` holds each shot; `−1` means "already skipped".

### 4.5 Talking heads (`TALKING.VGA`, logical file 6)

* Sections 0–10 = one head each (shot 20–30); section 11 = comm overlay. Frame 0 = full head
  background, frames 1–9 = mouth shapes drawn at `aTalkingHeadOrigins[face].mouth` (161,90 for
  most; head 1: 161,87; head 2: 159,90; head 10: 160,88), frames 11+ = eye/face overlays at
  `.face` (161,60 / 160,53 for head 10).
* **Mouth script** (`ParseMouthAnimation`): sequence of `letter[count]`; lowercase letters map
  via `asMouthFramesByPhoneme[26] = {0,5,4,4,1,8,4,7,0,4,4,7,5,4,2,5,6,4,4,4,3,4,6,4,4,4}`
  (a=0,b=5,c=4,d=4,e=1,f=8,g=4,h=7,i=0,j=4,k=4,l=7,m=5,n=4,o=2,p=5,q=6,r=4,s=4,t=4,u=3,v=4,w=6,x=4,y=4,z=4),
  `$` = frame 9; digits following give the tick count (default 1). Each entry runs
  `count*2` loop iterations. Example: `wevgotalotofwerktodopepulsolesgettoit`, `p10$100`.
* **Face script** (`ParseFaceAnimation`): `R` = loop marker (restart from the beginning),
  otherwise one hex digit `0–9,A–F` = frame followed by `<duration>,`. Frame 10 (`A`) is drawn
  as "no overlay". Example `RA45,81,01,A35,81,01,A50,81,02,` = blink loop.
* `LongTalk`: prints the text, advances both scripts on their own countdowns, redraws via
  `CloseTalk(talker, mouthFrame, faceFrame)` every `nFrameSkip` iterations; when the mouth
  script ends the duration timer starts; keypress skips.
* `CloseTalk` draw order: star field (if enabled) → backdrop frame (`nConversationBackdropFrame`
  from `pConversationBackdropShape`, scene types 0/1/2/4/5; funeral type 3 clears with colour
  0xbf) → head frame 0 → face overlay (`+11`) → mouth (`+1`) → comm overlay (frame
  `min(face,1)`) → funeral special overlays for talker 5/6.

### 4.6 Text macros (`AddPCName`, `$` escapes inside subtitles)

| Macro | Expands to |
| --- | --- |
| `$A` | medal name being awarded (`apszMedalNames[nConversationMedalIndex]`) |
| `$C` | player callsign |
| `$D` | current date `%03d.%03d` (year.day) |
| `$E` | `stSavedCampaignDate` (date the medal was earned) |
| `$K` / `$L` | player kills / wingman kills this mission |
| `$N` / `$P` | player name |
| `$R` | rank name; a trailing `.` of the rank is dropped if the next character is `.` |
| `$S` | system name (`abSeriesAuxData`) |
| `$T` | time `%02d:%02d` from `elapsedDate` bytes |
| `$W<d>` | name of wingman personality `d` |

### 4.7 Text formatter tokens (`FormatTextTokens`, used for all screen text)

`%X` cursor x (arg), `%Y` cursor y, `%F` foreground colour, `%B` background colour, `%J`
alignment (0 left, 2 centre), `%P` flush/draw the buffered text, `%s` string, `%d` s16,
`%u` u16, `%x` hex u16, `%D` s32, `%U` u32, `%c` char, `%%` literal. The conversation
text area uses `"%X%Y%F%s%P"` with x=0, y=160, colour, text, centred alignment; `\n` breaks
lines. Fonts: `ReleaseTextFont(n)` indices 0 (conversation), 1 (nav/trainsim), 2 (nav labels),
3 (chalkboard).

### 4.8 Briefing room characters

`aBriefingCharacters[8]` (`BriefingCharacterLayout`): body origin, portrait origin, scale
(176 = small/rear row, 256 = large/front row), `firstPortraitFrame/portraitFrameCount` into
`BRIEFING.VGA` sec 5, body frames from sec 4. `EstablishingShot` animates the 22-frame idle
(`abBriefingSmall/LargeCharacterAnimation`); `ReturnToBriefingLongShot` plays 12 "stand-up"
poses with per-pose portrait offsets/scales (`aBriefingPortraitOffsetX/Y[8][12]`,
`aBriefingPortraitScale`). Which seats are visible is a static `visible = 1` in the table
(the Win32 build does not hide dead pilots in the long shot — see Open questions).

### 4.9 Scene-animation bytecode (`MIDGAME.Vxx`, `ShowMeanwhileTransition`)

`LoadSceneAnimationResources(scene, variant)` with logical file `asSceneAnimationLogicalFiles
[scene] = 63..70` (`MIDGAME.V00..V07`) and `variant` 0 = series won, 1 = series failed:

| Section | Content |
| --- | --- |
| 0 | primary shape set (layer 0 objects — backgrounds, tiled horizontally) |
| 1 + variant | definitions: `u16 objectCount`, then `scenes × objectCount` `SceneAnimationObject` records (0x36 each), followed by the scripts (`scriptOffset` is relative to the section start) |
| 3 + variant | secondary shape set (layer 1/2 objects) |
| 5 + variant | conversation packet: `u32 sceneOffset, u32 textOffset`, scene records whose shots are 50–59 (= animation scene index) plus captions |

Verified `MIDGAME.V00` variant 0: 9 objects per scene, 2 scenes; `SceneDirector` records
shot 50 ("Terran Research Colony, McAuliffe VI."), then 51 ×4 captions, then −2.

`SceneAnimationObject` (0x36): `s16 layer (0 primary/tiled, 1 secondary, 2 secondary but not
drawn), s16 scriptOffset, ptr scriptStart, ptr scriptCursor, ptr repeatCursor, u16 goalFlags,
s16 delay, ptr shape, s16 x, y, rotation, scale(256 = 1:1), frame, s16 deltaX, deltaY,
deltaRotation, deltaScale, deltaFrame, s16 goalX, goalY, goalRotation, goalScale, goalFrame`.

`PlaySceneAnimation(text, scene, duration)`: prints the caption, binds every object of the
scene to its shape and script, then each iteration runs `UpdateSceneAnimationObject` for all
objects; when any object reports *complete* (or a `W` countdown expires) the animation ends;
if no `W` was used the caption is held `duration/2` ticks (skippable). ESC fast-forwards:
objects are stepped with `nFrameSkipCounter = 2` (no drawing) until one completes.

**Opcodes** (`UpdateSceneAnimationObject`; one byte opcode, operands little-endian):

| Op | Operands | Semantics |
| --- | --- | --- |
| `A` | prop `F/R/S/T/X/Y`, s16 v | **Add**: `prop += v` and remember `v` as the delta for goal checks. `R` wraps to 0..359, `S` clamps 0x40..0x1fff, `T` adds to the per-tick `delay` |
| `L` | prop, s16 v | **Load** absolute value (same props; `T` sets delay) |
| `Q` | prop `F/R/S/X/Y`, s16 v | set goal; `goalFlags |= {F:0x10, R:1, S:2, X:4, Y:8}`. After the tick the object is *complete* if `SceneAnimationGoalReached(delta, current, goal)` (delta<0: current<=goal; delta>0: current>=goal; delta==0: equal) for any flagged property |
| `X` | 5 × s16 | set x, y, rotation, scale, frame |
| `R` | s8 scene, s8 obj | copy x/y/rotation/scale/frame from definition record `scene*objectCount + obj` |
| `B` | s16 label | label definition (no-op when executed) |
| `G` | s16 label | goto label (scan from `scriptStart` for `B label`), continue this tick |
| `J` | s16 label | jump to label and end this tick |
| `D` | s8 frames…, 0xFF | **Draw**: draws each listed frame with `DrawSpriteScaled(x + xOffset, y, shape, frame, rotation, scale, object->frame)`; layer 0 advances `xOffset` by 320 per frame (tiling); layer 2 draws nothing. Sets `repeatCursor` to this command and ends the tick |
| `E` | s8 frames…, 0xFF | draw like `D` (always +320 per frame) and mark the scene **complete**; cursor stays on `E` |
| `P` | — | pause: end this tick |
| `W` | s16 n | global wait: scene ends after `n` more frames (`nSceneAnimationWaitFrames`) |
| `S` | 1 byte | skipped by both the executor and the scanner (no effect; purpose unknown) |
| `0` | — | end of script (object idle) |

Delay handling: when `object->delay != 0` at the start of a tick the object re-executes from
`repeatCursor` (its last `D`) and decrements `delay` instead of advancing — so `L T 5` + `D`
holds a frame for 5 ticks. `FindSceneAnimationCommand` (label scan) skips `A/L/Q` 3 bytes,
`B/G/J/R/W` 2, `D` to 0xFF, `E/P/S` 1, `X` 10.

Real example (`MIDGAME.V00` scene 0 obj 1): `B 0; D [1,2]; A X -2; G 0` = scroll a two-tile
background left 2 px per frame forever; obj 6: `Q X -80; B 0; D [3,4]; A X -4; G 0` = scroll
until x reaches −80 then the scene completes.

`ShowMeanwhileTransition(scene, variant)`: music `0x21 + variant`; prints "Meanwhile..."
with the intro font (`TITLE.VGA` sec 1) into the scene buffer and `PanToScreen` (palette fade
from black), waits 100 ticks, runs `SceneDirector(6, …)`, fades to black.

### 4.10 Briefing map (summary of `nav.c`)

`UpdateMap` → `BriefingMap_DisplayMap`: allocates a 260×156 virtual screen, loads
`COCKPIT.VGA` (logical 8) section 2 as the map art, calls `DrawNavLocationReadout("Briefing
Nav Map", 0)`: right-hand text column (title, "Sector: Vega XR-231.3", "System: <series>",
"* <mission name> *", "* <mission type> *", "Notes" + the current objective's description with
a leading `?` stripped), then `BuildMap(0)`: computes the world→map scale from the bounding box
of all objectives + player (`nav_getxy` = `(world/100) >> 8`, `SetScale`), draws asteroid/mine
field markers from the nav-point ship lists, then each visible objective with its style
(`aNavMapObjectiveStyles[type]`: 0 nav square, 1 home triangle, 2 cross, 3 green rectangle, 4
red rectangle), unvisited dot, label (yellow when it is `cCurrentObjective`), with collision-
avoiding label placement. The result is copied to the screen at `stScreen.top = 4`. The same
code draws the in-flight "ConFed Nav Scan" with the player marker and clickable objectives.

---

## 5. Screens: input handling and resources

Legend: LF = logical file (code numbering, §7). "Room menu" = `InitializeRoomMenu` +
`TitleMenuRegion` hit rectangles; cursor moves with mouse/joystick (`PollMenuInputDevices`)
or keypad arrows (`MoveMenuPointerFromKeyboard`, keypad-5 toggles step 1/4); Enter/Space or a
button activates the region under the pointer; ESC sets `bEscapePressed`.

| Screen | Function | Resources (LF:section) | Music | Input / exits |
| --- | --- | --- | --- | --- |
| Attract (3D intro + credits) | `Title_Sequence` part 1 | 9:0 title logo, 9:1 intro font, 3:2 explosion shapes, 3:5 debris (via `aIntroResourceDescriptors`), canned 3D scenes 16/17 | 23 | any key/button/ESC → skip to menu |
| Title menu | `Title_Sequence` part 2 | 9:4 menu images, 0x4b:0 alt menu | — | regions `aTitleMenuRegions` rows y 48/91/134/177 (x 49–283); Enter/Space/click selects pointer region; `S` = 0, `C`/`M` only if a 3rd option exists; `J` joystick calibration |
| TrainSim name entry | `EnterPilotNameAndCallsign` | 21:0 (PCSHIP.V04) backdrop | — | `ReadTextInput` LAST NAME (13), CALLSIGN (13), Enter accepts, ESC cancels (loops until non-empty) |
| TrainSim high scores / enemy select | `ShowTrainSimHighScores`, `SelectTrainSimMission` | 21:0 backdrop; 31–34:1 (SHIPTYPE.V09–V12) enemy portraits | 20–22 | high score table auto-scrolls (720 ticks per page), ESC skips; enemy selection: 4 regions `aTrainSimMissionRegions`; ESC cancels |
| Rec room | `RecRoom` | 5:0 background, 5:1 conversation backdrop, 5:2 chalkboard, 5:3+p pilot p (0–7), 5:11 Shotglass, CAMP:2 roster, BRIEFING mission section (rec-room scenes), star field 54–146×35–72 | 30 | regions: 0 Shotglass (94,59)–(130,95), 1 left pilot (161,79)–(180,95), 2 right pilot (210,79)–(240,95), 3 chalkboard (180,50)–(250,75), 4 barracks door (275,50)–(319,135) → return 4, 5 simulator console (0,100)–(120,190) → return 5. Talking runs `SceneDirector(2, …)` with the scene buffer clipped to 128 rows. Frame period 9 ticks; idle animations from `apRecRoomAnimations` (Shotglass: idle/glass/pour/wipe chosen randomly) |
| Chalkboard (kill board) | `ShowChalkBoard` | 5:2, font 3 | — | sorts the 9 pilots by `kills*1000 − missions + 1`, prints rank + upper-cased name, sorties/kills or `KIA`; any key/click exits |
| Barracks | `BarracksScreen` | 5:12 background, SAVEGAME.WLD, 4:8 medal shapes (View medals) | 35 | regions 0–15 bunks (slot = region/2; even = Awaken/load, odd = Save), 16 Mission Hangar → 7, 17 Return to the Bar → 8, 18 Quit (Y/N modal → `exit_squadron`), 19 View your medals (`LoadMissionData` first, then `ViewMedals`). Frame period 2 ticks; bunk sleepers animate, a figure drops from the top right every 20 animation ticks (frames 27–30 while falling, impact frames 37–48, sfx 35), and the sprite at (45,0) blinks (frame 49 eyes open / 26 closed) |
| View medals | `ViewMedals` | 4:8 (backdrop pointer is null here, so `DrawMedals` draws over black; `AwardCampaignMedal` uses 4:10) | — | `$R $N, aka $C.\n$S system, dateline $D.`; any key/click exits |
| Briefing | `Briefing` → `LoadBriefingRoom` → `SceneDirector(0)` | 4:0 backdrop, 4:1 clock/anim, 4:2 podium head, 4:3 close-up, 4:4 bodies, 4:5 portraits, MODULE, BRIEFING section | 24/25/26 (preloaded 0x18–0x1a) | per-record key/click advances; ESC ends |
| Scramble hangar walk | `PlayScrambleHangarScene` | 1:0 | 27 | 24 frames walk, 24 frames ladder (frames 21–26), 24 frames walk; ESC skips |
| Scramble (boarding) | `scramble` | (17+ship):8 cockpit, 1:1 background, 1:2 canopy, 1:3 ship, 1:4 actors; sfx 17, 15, 16 | 27 | 10 frames approach, 27 canopy open, 23 climb in; `WaitForSceneAdvance(60)`; ESC skips |
| Launch | `LaunchPlayerShip` | 1:7 launch door, cockpit; sfx 20 | `changetrack()` | 25 frames of door tunnel; ESC skips |
| Landing approach | `ShowCarrierLaunchSequence` | 1:8 carrier, 1:4 actors; sfx 18 | 28 | 100 frames fly-in + 35 frames deck + up to 50 frames third phase (flight-ui.md §5.4); ESC skips |
| Landing | `landing(damage)` | (17+ship):8, 1:1, 1:3, 1:4, 1:9 damage details, 1:5 overlay, 1:6 canopy; sfx 17/15 | 29 | 30 frames taxi, 30 frames canopy open; damage comment (`apszLandingDamageComments[damage]`) held 300 ticks |
| Debriefing | `DeBriefing` → `SceneDirector(1)` | 4:6 backdrop, BRIEFING section, MODULE | 33 (≥70 %) / 34 | as briefing |
| Office | `Office` → `SceneDirector(4)` | 4:7 backdrop, BRIEFING:1 | 36 | as briefing |
| Medal ceremony | `AwardCampaignMedal(m)` → `SceneDirector(5)` | 4:8 medal shapes, 4:10 backdrop, BRIEFING:2 | 37–40 by medal | as briefing; increments `medals[m]` before the scene |
| Funeral | `funeral_sequence(player)` → `SceneDirector(3)` twice + `funeral_player` loop | 4:9 special shapes, BRIEFING:0, 9:1 font (player), star field | 32 | fixed choreography: 10 frames, "Company..." 15, "Attention" 10 (sfx 0x24), guard frame 3 ×10, "Prepare arms" 10, rifles up (sfx 0x1f) ×10, follow-up scene, 3 volleys ("Fire" 10 frames, sfx 0x1e on 2nd, 24 frames particles sfx 0x1d), casket drifts until music ends (or 160 frames); ESC skips |
| Death | `death_sequence` | 2:0 death shape, cockpit:3 background; sfx 4 | 32 | 8 cockpit frames then 60 frames external explosion |
| Ejection | `ejection_sequence` | cockpit:3, 2:1 ejection shape, 8:8 view templates, 2:2 ejected pilot, cockpit:0/5 | 31 | 10 frames pod up, 10 frames canopy, up to 200 frames external (explosion at 10) |
| Stranded | `stranded_sequence` | 9:1 font | — | 400 frames, "With your carrier destroyed, you drift endlessly…" at 160, "THE END" at 300 |
| Meanwhile (MIDGAME) | `ShowMeanwhileTransition` → `SceneDirector(6)` | MIDGAME.V0n, 9:1 | 33/34 | §4.9 |
| Campaign victory | `ShowCampaignVictorySequence` | 9:3 planet, 9:2 projectiles, 9:5 celebration animation (frames 1–17 looping 12–17) | 33 | 250 frames 3D approach with captions at 0/100/180 (`apszCampaignVictoryText`), then 40 × 8-tick animation |
| Tiger's Claw escape (loss) | `ShowTigerClawEscapeScene` | 9:2, 3:14 hyperspace flash | 34 | 260 frames: captions at 0/150/210, jump flash at 190, white-out at 198 |
| The End | `ShowTheEndScreen(fireworks)` | 9:0x11 fireworks, 9:1 font, `ViewMedals` first | 23 | 320 frames: "THE END" (<160), "…for now" (>190), fireworks if flag; music fades from 190 |
| Get Ready / Victory / Game Over (TrainSim) | `ShowGetReadyScreen` etc. | 9:1 font, 9:0x11 fireworks | 22 (game over) | 40 / 80 / 80 frames of zooming text |

### 5.1 Attract sequence timing (`Title_Sequence`)

Loop until ESC: canned scene 16 (`set_up_action_sphere(16)`), scripted camera
`asIntroCameraSequence`; 25 frames with "In the distant future, mankind is locked in a deadly
war..." → 110 frames of dogfight → 100 frames title logo zoom (`DrawTitleLogo`: 3 sprites
scaled `0x1000/distance`, distance 200→16 step 4) → scene 17 + asteroid field → credits
(`apszIntroCredits`, 11 cards, +9 Saga cards when `bShowKilrathiSagaCredits`) each 70 frames
of text + 40 frames → 150 frames → repeat. Mission ships 32–45 host the canned dogfight.

### 5.2 Rec room drawing model

Background sprite frame 0; frame `characterMask` (1 right pilot, 2 left, 3 both) draws the
seated bodies at (158,128). Each tick only dirty regions are re-blitted: the union of the two
pilots' frame bounds (`pilotWork`), the Shotglass region including the star field
(`shotglassWork`), and the bottom label strip rows 187–199. Pilot heads are drawn as frame 0 +
animation frame from `apRecRoomAnimations[personality]` (−1 restarts). The current menu label
(`"Talk to SPIRIT."`, `"Check pilot scores"`, `"Enter barracks"`, `"Fly training mission"`) is
printed centred at y 188. `bPanRoomTransition` makes the first frame fade in via `PanToScreen`
(palette interpolation over the active palette entries, `StepPaletteTransition`).

### 5.3 Modal prompts

`ShowModalTextPanel`/`ShowModalMessage` draw a boxed text panel (`dwModalBoundsTopLeft =
(0x18,0x28)`, bottom-right `(0x128,0x3c)`) and wait for a key: "Load a game first.", "Quit Wing
Commander? (Y/N)", "Awaken %s? (Y/N)", "Replace %s? (Y/N)", "Error: data may be bad.",
"FAULTY DATA", "Loading Game...", "Error: Game %s not saved.", "Error: Game %d not loaded.".
`PromptForTextInput(x, y, prompt, buffer, max, mode)` draws an inline edit box (mode 1 =
upper-case, 2 = digits only); Enter accepts (non-empty), ESC cancels, Backspace edits.

### 5.4 TrainSim session (`RunTrainSim`)

Not a flight-layer concern except for its menus: `cCockpitView = 4`, cockpit file 21
(`PCSHIP.V04`). Outside startup: `ShowTrainSimHighScores` (6 entries, title bounces), enemy
selection (returns 0 = cancelled). Then for `nTrainSimMission` 0..3: `init_mission(0, m)`
(series 0 = simulator missions in MODULE section rows 0–3), `ShowGetReadyScreen`, flight;
result 1 → `ShowVictoryScreen` (mission 3) and next mission, otherwise `ShowGameOverScreen`.
Finally `UpdateTrainSimHighScores(score)` (startup mode → name entry; otherwise congratulations
or "YOUR SCORE IS ONLY %ld0") and the table again. High scores: `HighScoreEntry {s8 pilot; u32
score}[6]`, pilot 8 = player, 9–14 = `BISHOP, GOBLIN, JEFFTEP, MANGLER, THE MAN, MONGO`,
0–7 = wingmen by callsign. `InitializeTrainSimHighScores` seeds 5 random non-player entries
descending from ~10000–12000; `AddRandomTrainSimHighScores` perturbs three living pilots after
every campaign mission.

---

## 6. DOS startup intro (`SdlPlayDosStartupIntro`, SDL restoration of the Origin FX overlay)

Only runs with DOS data (or the GL renderer). Buffer 320×128 presented to screen rows 24–151;
each frame ends with `CheckEscaped()` (any key/click aborts the whole intro). Resources:
`TITLE.VGA` sections 6–17 (code LF 9): sec 6 = sky background (frame 0) + three logo pieces
(frames 1–3), secs 7–16 = ten orchestra actors, sec 17 = fireworks (8 frames × 3 variants);
planet = sec 3. Music: OriginFX track 19 (`nCurrentMusicTrack = 19`, restored afterwards); the
stages wait on `SdlGetOriginFxMusicSequencePosition()` when the music is sequenced, otherwise
they run on fixed frame counts:

1. **Orchestra** (actors at fixed positions, 32-frame strings like `"abcdefghijkaakkkkaaaalllllllmmll"`,
   frame = char − 'a'): with music, play 0→31 then bounce between a random minimum (9–22) and 31
   until sequence position ≥ 1; without music: 0→31, 31→12, 12→31.
2. **Conductor cue**: 20 frames of `"opoqopoqopoqopoqqrstrq"` for the tenth actor, then wait for
   position 2 and show the final cue frame.
3. **Push-in**: distance 1→120 (`distance += distance/4 + 1`), actors move by `velocity ×
   distance` and scale `0x100 + vy*distance*4`.
4. **Logo reveal**: distance 5000→1000 step 100; logo y starts 59, −2 per frame while distance
   > 3000 then +2; logo scale `256000/distance`; planet rises to `120000/distance` and is drawn
   behind or in front depending on overlap. Wait for position 3.
5. **Fireworks**: up to 5 slots spawn (randomly, or steadily once position ≥ 4); finish when
   position ≥ 5 (or after 10 frames unsynchronised) and the 5 slots are empty; then a final
   burst of 30 fireworks for 8 frames.

Frame pacing is the cinematic 16 fps. Hard-coded actor table is in `dos_intro.c`
(`g_aSdlDosIntroActors`). The Kilrathi Saga executable itself has no equivalent; it starts at
the 3D attract sequence.

---

## 7. Logical file table (code numbering)

Derived from the GOG `INSTALL.DAT` (16-byte records `name[13], disk, class, id`; code id =
id − 1 because `pDiskFileRecords++`). The SDL port appends the Secret Missions files at 72–75
(`SdlCompleteDosInstallTable`).

| LF | File | Sections used by this layer |
| --- | --- | --- |
| 0 | FONTS.FNT | fonts (via `ReleaseTextFont` 0–3) |
| 1 | SCRAMBLE.VGA | 0 hangar walk, 1 scramble/landing background, 2 open canopy, 3 ship exterior, 4 crew actors, 5 landing overlay, 6 closed canopy, 7 launch door, 8 carrier (landing approach), 9 damage decals |
| 2 | PILOTANM.VGA | 0 death, 1 ejection pod, 2 ejected pilot shape set |
| 3 | OBJECTS.VGA | 2 explosion, 5 debris (intro), 14 hyperspace flash |
| 4 | BRIEFING.VGA | 0 briefing backdrop (frames 0–3: room, panels, podium), 1 clock/establishing animation, 2 Colonel podium head, 3 Colonel close-up (mouth frames), 4 character bodies, 5 character portraits, 6 debriefing backdrop (frames 2–9: panels, pilot, officer, podium, +9 wingman portraits), 7 office backdrop, 8 medal ceremony shapes (0 hall, 1+ anim, 11 chest interior, 12 medal zoom, 13–24 badges, 25–32 medals, 33–37 rank insignia, 38–40 pinning, 41–44 chest), 9 funeral shapes, 10 medal backdrop |
| 5 | RECROOM.VGA | 0 bar, 1 bar conversation backdrop, 2 chalkboard, 3–10 pilots (personality+3), 11 Shotglass, 12 barracks |
| 6 | TALKING.VGA | 0–10 talking heads, 11 comm overlay |
| 7 | MUSIC.MID | music |
| 8 | COCKPIT.VGA | 1 in-flight computer background, 2 nav map art, 8 view templates |
| 9 | TITLE.VGA | 0 title logo (3 parts), 1 intro/subtitle font, 2 projectile/escape shape, 3 planet, 4 title menu images, 5 victory celebration animation, 6–17 DOS intro (6 sky+logo, 7–16 orchestra, 17 fireworks) |
| 10 / 62 / 73 | BRIEFING.000 / .001 / .002 | §4.1 |
| 11 | WINGMEN.VGA | in-flight comm portraits |
| 12 | PLANETS.VGA | constellation planet sprites (`ConstellationObjectDefinition.shapePacket`) |
| 13 | COMMUNIC.DAT | comm message text (flight) |
| 14 | ARROW.VGA | mouse cursor shapes |
| 15 / 52 / 72 | MODULE.000 / .001 / .002 | §2.3 |
| 16 | SAVEGAME.WLD | §3 |
| 17–21 | PCSHIP.V00–V04 | cockpit art per ship (`17 + playerShipType`; 21 = TrainSim cockpit); section 8 = scramble cockpit |
| 22–25, 31–35, 51 | SHIPTYPE.Vnn | ship resources; 31–34 (V09–V12) = TrainSim enemy portraits (section 1) |
| 58 / 61 / 74 | CAMP.000 / .001 / .002 | §2.2 |
| 59 | WINGLDR.TIM | AdLib timbres |
| 60 | INTRO.DAT | copy-protection Q&A (20 ciphered lines, `PromptForAnswerText` is unreachable in KS) |
| 63–70 | MIDGAME.V00–V07 | §4.9 (DOS INSTALL.DAT lists only V00–V05; V06–V08 exist on disk from the Secret Missions add-on) |
| 71 | SERIES.VGA | series/constellation art (listed in INSTALL.DAT; no direct use found in this layer) |
| 75 | TITLE1.VGA | Secret Missions title menu image |

`CRUSADE.WLD` is not referenced by the code.

---

## 8. Proposed C# design

### 8.1 Principles

* **No blocking loops.** Every original screen becomes a `Screen` with `Enter()`, `Update(dt,
  input)`, `Draw(frame)`, `Exit()`; cutscenes and conversation scenes become coroutines
  (`IEnumerator<Wait>` / `ValueTask` with a frame scheduler) so the sequential structure of
  the C code (frame loops with `WaitForSceneAdvance`) survives as readable `yield return
  Wait.Frames(n)` / `yield return Wait.TicksOrInput(duration)`.
* Keep the original **tick (1/60 s) and frame-pacing (16 fps cinematic)** as the simulation
  unit; render can run faster but logic steps at the original cadence.
* Data classes mirror the on-disk records exactly (immutable readers), separate from mutable
  runtime state.

### 8.2 Modules

```
WingCommander.Data
  OriginPacket           // container + LZW (§2.1): Section(int) → ReadOnlyMemory<byte>
  InstallTable           // INSTALL.DAT → LogicalFile enum/ids (+SM expansion)
  CampaignFile           // CAMP.xxx: SeriesRecord[13], MissionScoreRow, RecRoomRoster, ConstellationDefs
  BriefingFile           // BRIEFING.xxx: FuneralScenes, OfficeScene, MedalScene, MissionScenes[series][mission]
  SceneScript            // ConversationSceneRecord[], text block, Test parser, Mouth/Face script parsers
  SceneAnimationFile     // MIDGAME.Vxx: objects, scripts, captions
  MissionFile            // MODULE.xxx header/nav/objective/ship/name tables (shared with flight)
  SaveGameFile           // 8 × SaveSlot (exact 0x33C layout, round-trip safe)
WingCommander.Campaign
  CampaignState, PilotRecord, CampaignDate, Badges/Medals enums
  CampaignRules          // PostMission, UpdateSeries, promotion roll, medal check, MoveNewCampaign
  CampaignFlow           // the GameFlow state machine as an explicit enum + transitions
WingCommander.Screens
  ScreenHost             // stack/sequence of IScreen; drives Update/Draw; input routing
  TitleScreen, RecRoomScreen, ChalkboardScreen, BarracksScreen, MedalsScreen, ModalPrompt, TextInputBox
  ConversationScene      // SceneDirector as a coroutine: shot handlers = IShotPresenter
  TalkingHeadPresenter, BriefingRoomPresenter, DebriefingPresenter, MedalPresenter, FuneralPresenter, MapPresenter
  SceneAnimationPlayer   // bytecode VM (§4.9) – pure, testable
  Cutscenes: ScrambleHangar, Scramble, Launch, LandingApproach, Landing, Death, Ejection, Stranded,
             Meanwhile, CampaignVictory, TigerClawEscape, TheEnd, DosIntro, GetReady/Victory/GameOver
  TrainSimMenus          // high scores, enemy select, name entry
```

### 8.3 Flow state machine

```csharp
enum FlowState { Title, NewCampaignTrainSim, RecRoom, Chalkboard, TrainSim, Barracks,
                 Briefing, ScrambleHangar, Scramble, Launch, Flight,
                 LandingApproach, Landing, Ejection, Stranded, Death, PlayerFuneral,
                 Debriefing, WingmanFuneral, Office, Medal, Meanwhile,
                 Ending /* victory | escape | meanwhile */, TheEnd }
```

`CampaignFlow.Next(result)` encodes §1.5 exactly (including the "evaluate against the flown
mission, then commit next series/mission" dance — model it as `FlownMission` + `NextMission`
fields instead of mutating/restoring `currentSeries`). Flight returns a `FlightResult`
enum {Landed, Ejected, Stranded, Killed, Aborted}.

### 8.4 Conversation scene interpreter

* `ConversationScene.Run(SceneType, SceneScript, Context)` as a coroutine: iterate records,
  evaluate `Test`s against an `ISceneContext` interface (mission score, kills, objective flags,
  pilot status, ace flags, medal/promotion/eject flags, ship types) so it is unit-testable with
  fake contexts; shot preparation and handlers map to `IShotPresenter` implementations.
* Macro expansion (`$C`, `$D`, …) as a pure function over `CampaignState`.
* Mouth/face scripts compiled once to `(frame, ticks)[]` with a loop marker; `TalkingHead`
  presenter advances them per tick exactly as `LongTalk` (countdown `ticks*2`).
* `SceneAnimationPlayer`: port `UpdateSceneAnimationObject` verbatim as a VM over
  `ReadOnlySpan<sbyte>`; expose `Step()` returning `complete`; drawing via an injected sprite
  batch so the VM is testable headless. Fast-forward on ESC = step without drawing until complete.

### 8.5 Save games

Read/write the exact 0x33C layout for compatibility with DOS/KS saves (keep the truncated
pointer fields as opaque `short`s). Internally keep `CampaignState` as a record class; add a
modern sidecar (JSON) only if extra data (e.g. TrainSim scores, settings) is wanted — never
change the binary layout.

### 8.6 What can be modernised safely

* Frame pacing/timers (`SetFrameTimerPeriodDirect`, `nFrameSkip`) → a single tick scheduler.
* Palette fades (`PanToScreen`, `FadeViewportPaletteToColour`) → shader/colour-lerp on the
  indexed framebuffer; keep the same durations.
* Menu hit regions and keyboard pointer → mouse/gamepad abstraction; keep the original hot
  keys (S/C/M/J, Enter/Space, N for "new objective" in the map).
* Rec-room dirty-rect blitting → full redraw.
* Text formatter tokens → `string.Format`-style helper, but keep `%P` semantics (buffer then
  draw centred) for layout fidelity.
* Modal prompts and the text input box → reusable widgets.
* Memory-budget resource lists (`LoadPacketResourceList`) → simple caching loader.
* Known bugs that may be fixed: `seriesHistory[8]` overflow after 8 series; the off-by-one
  read in the post-series sequence check (§2.4, verify first); dead pilots still drawn in the
  briefing long shot; unreachable "quit to title" (return 6) from the barracks.

### 8.7 What must stay bit-faithful

Series branching, score/medal/promotion math and random ranges (`RandomInRange` semantics),
badge/medal frame numbering, scene record/test semantics, mouth/face timing, scene-animation
opcode semantics, save layout, and the packet/LZW reader.

---

## 9. Open questions

1. **Post-series sequence suppression** (`UpdateSeries`): the check reads
   `pMissionCampaignData[newSeries*0x5a + 5]`, i.e. byte +5 of the record *after* the new series
   (records are 1-based). Original bug or intentional? Compare with disassembly before porting.
2. **MIDGAME.V08 / SM2 endings**: `asSceneAnimationLogicalFiles` has 8 entries (V00–V07) but
   CAMP.002 uses `postSeq = 8`; the DOS INSTALL.DAT only lists V00–V05. How Kilrathi Saga maps
   the SM2 MIDGAME files (and whether index 8 reads garbage) is unresolved.
3. **Secret Missions entry from the title menu**: the Win32 title only offers "new game"/
   "continue"; SM campaigns appear to be entered via saves with `campaignIndex > 0`
   (`DAT_005a7d9c`) or the external launcher. Confirm the intended UI.
4. **Constellation definitions for a new game**: `StartNewCampaign` does not load CAMP
   section 0; only `LoadGameFromSlot` does. Check where `pConstellationDefinitions` is filled for
   a fresh campaign (flight-layer `init_constellation`?).
5. **`PilotRecord.personality` (last field)** values `{1,4,2,1,1,2,1,3,0}` don't equal the
   personality index; purpose unknown (voice set? comm style?).
6. **`talker` field** semantics for values 5/6 (funeral overlays) and ≥ 10 in the debriefing
   data (10/11/12/13 → backdrop frames) — verify against the KS BRIEFING data, which may differ
   from DOS.
7. **Barracks return 6** path and the `menuOptions[2]` title options are dead code in Win32;
   decide whether to restore the DOS behaviour (quit → title).
8. **Text colour table** index meanings beyond 0/1/9/11 are not documented; sample the data.
9. **`S` opcode** in the scene-animation bytecode is skipped; check MIDGAME data for occurrences
   and the DOS original for its meaning (sound trigger?).
10. **SERIES.VGA** (LF 71) and `PLANETS1.VGA`, `TALKING1.VGA`, `WINGMEN1.VGA`, `TITLE1.VGA`
    (SM variants) — which code paths select the `*1.VGA` files besides TITLE1?
11. **Event type numbering** (2/3/5/10/13) is inferred from use; confirm against `eventmgr.c`.
12. `aBriefingCharacters[].visible` is static; the DOS original may clear seats of dead pilots.

---

## 10. Corrections found while porting (2026-10-08; the C code wins)

Collected from `progress/screens-scenes.md`, `progress/screens-rooms.md` and
`analysis/flight-ui.md` §8.1. Where a section above disagrees, this list is right.

- §1.5/§1.2: `nArcadeState` 5 comes only from Esc in the training simulator (Win32 Alt+X posts
  WM_QUIT; the SDL port ignores it). The landing approach has a third phase of up to 50 frames
  (flight-ui.md §5.4).
- §2.2 CAMP section 2: CAMP.002 also lists personalities 11 and 12, which have no rec-room art.
- §2.6: `stSavedCampaignDate` starts as {day 20, year 340} (shown by `$E` before the first medal).
- §4.4/§5: the debriefing plays music 33 when the player scored more than 70 % (`> 70`).
- §4.4: shots 9 and 12..15 (DrawFuneralLongShot) are the Colonel's office: 9 the office door
  still, 12..15 the window view (frame shot - 8 over frame 3, desk frame 8) with the star field;
  shot 16 (DrawMedalChest) is the hangar deck doors sliding open, not a chest.
- §4.5: script entries last `ticks * 2 + 1` loop iterations; the first entry of every mouth and
  face script is skipped; face scripts start with 'R', which therefore never loops.
- §4.8: `aBriefingPortraitScale` is passed as the rotation angle (degrees), not as a scale.
- §4.10: the Kilrathi Saga briefing map is a 260x156 picture at screen row 4 that replaces the
  room (the DOS game drew the map inside the room's screen).
- §5 barracks: the "figure dropping from the top right" is a drop of water falling into the
  bucket (frames 27..29 at x 298, splash 37..48 at (298, 139), sfx 35); the "blinking" sprite at
  (45, 0) is a flickering ceiling light (frames 49 on / 26 off).
- §5 rec room: the three character regions are replaced on every visit by the frame bounds of
  Shotglass and the seated pilots (unseated: (400,400)-(401,401), unreachable).
- §5.3: `ShowModalMessage` waits for a key release (`WaitForKeyAcknowledge(0)`), not a press; the
  Y/N questions read a virtual-key code (`WaitForStreamInputKey`).
- §9 question 2: MIDGAME.V06..V08 exist (Secret Missions 2 Firekka scenes).
- §9 question 4: LoadOriginFxDrivers loads CAMP.000 sections 0 and 1 at start-up, so a fresh
  game has constellation definitions; StartNewCampaign reloads only section 1, so new Secret
  Missions campaigns keep CAMP.000's constellations in the original. The port loads the
  campaign's own sections 0 and 1 (documented deviation in progress/game.md).
- Secret Missions 2 lets the player fly a captured Dralthi (mission ship type 10); its cockpit
  falls back to the Hornet layout (ADR-012).
