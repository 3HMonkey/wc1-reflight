# Wing Commander 1 — Audio and Music Subsystem Analysis

Target: port of the audio layer to C# / .NET 10 (NativeAOT-friendly).
Primary goal: the **DOS data path** (OriginFX sequences in `MUSIC.MID`, AdLib timbres in
`WINGLDR.TIM`, OPL2/YM3812 synthesis for both music and sound effects). Secondary goal: the
**Kilrathi Saga path** (WAV sound effects through the `ix` mixer, streamed `.STR` music), documented
far enough to decide what the port needs.

Sources analysed (reference repo `reference/wc1-re`, read-only):

| File | Role |
|---|---|
| `src/sound.c` | Wave playback (Saga), volume settings/registry, launch/landing scene sequencing that triggers audio |
| `src/music.c` | Music state machine: `spacetrack`, `gametrack`, `changetrack`, `servicetrack`, `new_space_music_changes`, `PlaySfxWaveFileByNumber`, Origin packet section reader |
| `src/gr.c` (lines 905–1120) | Thin wrappers over the `ix` streamer (`Streamer_open/play/stop/trigger`, `SetStreamerIntensity`, `SetMusicStreamVolume`) |
| `src/pload.c` | `PacketLoad`, `InitializeAudioSystem`, wave-table / active-sound bookkeeping |
| `src/logic.c` | `LoadOriginFxDrivers` (DOS driver bootstrap, now a memory-config stub), `init_inflight_music`, afterburner sound timing, driver stubs |
| `src/main.c`, `src/hudmsg.c` | Command-line music mode, volume hotkeys, `Update_3Space` → `servicetrack` |
| `src/sdl/audio.c` | SDL2 audio device open + callback |
| `src/sdl/music.c` | DOS OriginFX music/SFX host glue: file loading, track service, gains, mixing callback |
| `src/sdl/originfx.cpp` | The OriginFX replayer: MIDI parse, timbre bank, OPL2 register programming, 60 Hz service, sound-effect engine; uses `third_party/ymfm` (YM3812) |
| `src/sdl/wave.cpp` | Saga WAV playback with pan (SDL addition) |
| `src/sdl/dos_intro.c` | DOS startup intro synchronised to music cue points |
| `src/sdl/resources.c` | Origin packet container + LZW decoder, `SdlUsingDosData` |
| `src/ix/*.cpp`, `src/ix/ix.h` | Kilrathi Saga `ix` audio library (DirectSound mixer, voices, samples, streamer) |
| `include/globals.h`, `src/globals.c` | Audio globals |

Real data inspected: `<GOG install>/GAMEDAT\MUSIC.MID`, `WINGLDR.TIM`, `STRAX.DRV`,
`SUPERTM.DRV`, `TM.DRV` (DOS version; no `.WAV`/`.STR` files exist there).

---

## 1. Big picture

There are two completely different audio stacks selected at start-up by `SdlUsingDosData()`
(byte 7 of `GAMEDAT/MODULE.000` == 1 means "DOS compressed data"):

```
                      game logic (music.c / sound.c / cockpt.c / ship.c ...)
                                       |
          PlaySfxWaveFileByNumber(n, srcObj, loop)       spacetrack(track, mode, flag)
          stop_all_sounds()  SetSoundEffectsVolume()     StopMusic()  nMusicTrackComplete
                    |                                              |
   +----------------+-----------------+              +-------------+----------------+
   | DOS data                         |              | DOS data                     |
   | SdlPlayDosSoundEffect            |              | SdlServiceOriginFxMusic      |
   |   -> OriginFX "sound player"     |              |   -> OriginFX "music player" |
   |      (26 ch, 11 voices, 2x YM3812|              |      (26 ch, 11 voices,      |
   |       for L/R panning)           |              |       2x YM3812, mono used)  |
   +----------------------------------+              +------------------------------+
   | Kilrathi Saga data               |              | Kilrathi Saga data           |
   | playWAVE("sfxNN.wav")            |              | ix streamer: MISSION.STR,    |
   |   -> ix sample/sound/voice mixer |              |   PREFLITE.STR, POSFLITE.STR |
   |      (up to 16 voices)           |              |   (branching PCM stream)     |
   +----------------------------------+              +------------------------------+
                                       |
                          SDL audio callback, 22050 Hz, S16, stereo, 1470 frames
```

In DOS mode the SDL port disables the `ix` library completely (`bIxAudioEnabled = 0`) and the
OriginFX host owns the audio device; the mixer callback renders the music player and then the
sound-effect player. In Saga mode the `ix` mixer owns the device and the OriginFX player is only used
for the restored startup intro (track 19) mixed on top.

The game logic is identical in both modes; only the leaf functions differ. The port should keep this
shape: one `MusicDirector`/`SoundEffectManager` pair on the game side, pluggable backends underneath.

---

## 2. Game-facing audio API

### 2.1 Globals (with original addresses from `globals.h`)

| Global | Type / default | Meaning |
|---|---|---|
| `nMusicPlaybackMode` (0x46a9f8) | short, set to **4** at startup | Music "mode" from the command line. `R`→1, `A<n>`→2 (arcade/training), `P`→3, otherwise 4. **Music is enabled iff mode ≠ 0 and ≠ 3.** Modes 1/2 additionally select the "Full/Limited music" memory configuration in `LoadOriginFxDrivers` (DOS-era conventional memory logic, irrelevant to the port). |
| `nMusicTrackComplete` (0x46aa04) | short = 1 | 1 when no music track is playing / the current one has ended. Saga: `UpdateStreamerStoppedFlag()` (called from `DIBslam` every frame) sets it from the streamer state bit 0x4. DOS: set by `SdlServiceOriginFxMusic`. |
| `nCurrentMusicTrack` (0x46aa14) | int = -1 | Track number last requested through `spacetrack`; -1 = none. |
| `nMusicStreamSet` (0x46aa18) | int = -1 | Saga only: which `.STR` is open (0 preflite, 1 posflite, 2 mission). |
| `bMusicCommandSuppressed` (0x46a9fc) | int = 0 | When non-zero `ProcessMusicScriptCommand` ignores all requests. Never set in the reconstructed code. |
| `nWaitForMusicEnabled` (0x46aa30) | int = 1 | Scenes (funeral) wait for music end only if set; `EnableMusicForScene` sets it. |
| `nInFlightMusicActive` (0x46aa40) | int | Set by `init_inflight_music` (entering space flight), cleared by `free_inflight_music`. Gate for `gametrack`. |
| `nCombatMusicActive` (0x46aa3c) | int | Inside flight: 1 while enemies present (combat music family), 0 otherwise. |
| `nInitialFlightMusicPending` (0x46aa38) | int | Set by `init_inflight_music`; cleared at first combat selection (no other effect). |
| `nFlightSoundEffectsEnabled` (0x46aa34) | int = 1 | Gate for the per-frame proximity sounds in `servicetrack`. `ResetSoundStateForScene` clears, `ResetSoundStateForFlight` sets. |
| `nTrainSimActive` | | Training simulator (arcade) active → different music rules. |
| `nSfxVolumeSetting`, `nMusicVolumeSetting` (0x469fbc/0x469fc0) | int, default 0x14 = 20 | 0..20; stored in registry `HKLM\Software\Origin Systems\WC: Kilrathi Saga` as DWORD `SFXVolume` / `MusicVolume`. Displayed as `setting/2` (0..10). |
| `anVolumeLevels[11]` (0x469fc8) | `{0, 40000, 50000, 55000, 60000, 61000, 61500, 62000, 63000, 63500, 64000}` | Level table indexed by `setting/2`. |
| `aiSoundEffectSourceActive[0x41]` (0x5a66ec) | int[] | Index `sourceObject+1` (so -1 → slot 0). Set to 1 whenever a positional sound is started for that object; cleared by `remove_hazard`. Used by `servicetrack` to avoid re-triggering the asteroid sound per object. (Slot 0 is clobbered by `screen.c` with a pointer — original bug, harmless.) |
| `bAfterburnerSfxActive` (0x5a7cec) | int | DOS path: set when sound 12 was started for the player; cleared when afterburner ends (`spc.c`) via `FlushSoundEffectsAndLog` → stops *all* sounds. |
| `nAfterburnerSoundDeadline` (0x5a7ce8) | int | Frame number until which the afterburner sound is considered running; `your_afterburner` re-fires sound 12 when `deadline < frame` and sets `deadline = frame + 6`, i.e. **every 7 frames** (corrected; the test is strict). `nSpaceFrame` is a `short`: after it wraps the deadline resets to 0 and no afterburner sound plays until the frame counter is positive again (original quirk). |
| `nDamageAlarmSfxHandle` (0x5a7ec0) | int | Always 0 in the Saga build (nothing assigns it; `SoundFxTick` is a stub returning 0). `update_lights` plays sound 32 when `handle == 0 \|\| nSpaceFrame % 10 == 0`, so the alarm is re-fired **every frame** while shields are collapsed (corrected; with DOS data each play replaces the previous tag -1 effect). |
| `nPassingShipSoundObject/Countdown/Cooldown` | | State for the "ship passing by" sound (2) in `servicetrack`. |
| `bIxAudioEnabled` (0x465058) | int | Saga `ix` library enabled. SDL port forces 0 with DOS data. |

### 2.2 Sound effects

`void PlaySfxWaveFileByNumber(int soundNumber, int sourceObject, int looping)` — the only sound
effect entry point used by game logic (≈45 call sites). Semantics:

* `soundNumber` is **1-based** (1..36). Saga maps it to file `sfx%02i.wav` with `soundNumber-1`
  (`sfx00.wav`..`sfx35.wav`); DOS maps it to row `soundNumber-1` of the OriginFX sound record table
  (section 4.6).
* `sourceObject` = space object index (0 = player ship, 1.. = other objects) or -1 for
  non-positional (cockpit/UI/player) sounds.
* `looping` is passed through (Saga: sample loop flag; DOS: used as the *priority* argument of the
  OriginFX engine). Every call site passes 0.
* Distance attenuation:
  * DOS path: `volume = 127 - (|eye - source| / 500) >> 8` (fixed-point magnitude with 8 fractional
    bits, so one volume step per 500 m), clamped at 0; if `volume < 10` the sound is skipped.
    Pan (SDL addition, `SdlPlayGameSoundEffect`): `pan = clamp(64 - dot(normalize(delta), eyeRight) * 64 / 256, 0, 127)`,
    64 = centre; non-positional sounds use pan 64. `tag = sourceObject`, `priority = looping`.
  * Saga path (original behaviour, quirky): `distance = min(|delta|, 32000)` (or 32000 when no
    source) is passed **as the ix volume** (0..65535 scale), and sounds closer than 10 units are
    *not* played. The SDL port halves that volume (`SDL_WAVE_SOUND_EFFECT_GAIN_DIVISOR 2`) to give
    the streamed music headroom and applies the same pan.
* Side effects: `aiSoundEffectSourceActive[sourceObject+1] = 1`; in DOS mode
  `bAfterburnerSfxActive = (soundNumber == 12)` for player sounds.
* Tag collision rule (DOS path, `SdlPlayOriginFxSoundEffect`): a new effect with the same `tag`
  as an active effect **replaces it** (stops the old one, inherits its channel, age and priority).
  Since all player/UI sounds use tag -1, only one of them plays at a time. Exception: sound 8
  (laser/neutron fire) is given alternating tags 64/65 by `SdlPlayDosSoundEffect` so the paired
  gun sounds can overlap.

Other sound-effect entry points:

| Function | Semantics |
|---|---|
| `stop_all_sounds()` | Stop every effect (DOS: `SdlStopDosSoundEffects`; Saga: delete all ix sounds and samples, free wave table, drop the snow-static sound). |
| `FlushSoundEffect()`, `FlushSoundEffects()`, `FlushSoundEffectsAndLog()` | All three call `stop_all_sounds()`. Call sites pass a handle argument (`nDamageAlarmSfxHandle`, firework handle) — in the original DOS build these stopped one effect; Saga flushes everything. |
| `ResetSoundState()` | `FlushSoundEffects()`, clear `bAfterburnerSfxActive`, `nDamageAlarmSfxHandle`. `ResetSoundStateForScene/ForFlight` additionally clear/set `nFlightSoundEffectsEnabled`. |
| `PlaySnowStaticSound()` | Saga only: loads `sfx22.wav` once (volume 50000, kept alive) and restarts it if stopped. Triggered by `malf_noise(..., sound == 0x17)` (VDU static). **In DOS mode nothing plays** (the ix path is disabled) — the original DOS game would have played OriginFX sound 23 here. |
| `PlayCockpitSelectionSfx(short)` | Ignores its argument and plays sound 25. |
| `SoundFxTick(descriptor, 0, 127, pan, index, 1)` / `sound_effect()` | Saga stubs (`WriteDebugString`). In DOS these are the raw OriginFX "play descriptor" calls: `abFireworkSoundDescriptor = {00 80 40 40 3C 00 00}` is an inline sound record (flags 0, program 0x80-1 = 127, note 64, velocity 64, 60 ticks, no glide) — same layout as the table in 4.6. |
| `ServiceSoundSystem()` | `ix_system_service_sounds()`; called from `DIBslamReal` every presented frame. |
| `SetSoundEffectsVolume(int level)` | `ix_system_set_master_volume(level)` if `0 <= level < 65000`. DOS path instead recomputes its gain from `nSfxVolumeSetting` each service call. |

Sound number catalogue (1-based number → use → DOS record):

| # | Used for (call sites) | DOS record `{flags, prog+1, note, vel, dur, glide}` | Notes |
|---|---|---|---|
| 1 | missile launch (`ship.c` fire) | `{0, 1, 64, 64, 60}` | prog 0 |
| 2 | ship passing close (`servicetrack`) | `{2, 2, 64, 64, 1, 0, 0}` | glide 64→0, 1 tick/step (falling sweep) |
| 3 | engine malfunction / afterburner failure (`accelerate`, `your_afterburner`) | `{0, 39, 64, 64, 60}` | |
| 4 | explosion (`explode`, briefing/campaign cutscenes) | `{0, 4, 64, 64, 60}` | |
| 5 | mass driver / turret fire | `{0, 7, 64, 64, 6}` | |
| 6 | asteroid passing | `{0, 8, 64, 64, 30}` | |
| 7 | sparks/hit effect (`spc.c`) | `{0, 9, 64, 64, 10}` | |
| 8 | laser / neutron gun | `{0, 10, 64, 64, 60}` | tags 64/65 alternate |
| 9 | hit, armour damaged | `{0, 11, 64, 64, 6}` | |
| 10 | hit absorbed by shields | `{0, 12, 64, 64, 10}` | |
| 11 | launch catapult (`ShowCarrierLaunchSequence` frame 9) | `{4, 13, ...}` | sustain |
| 12 | afterburner | `{4, 14, ...}` | sustain |
| 13 | debris (wing / metal sheet) | `{0, 15, 64, 64, 6}` | |
| 14 | scramble klaxon (`PlayScrambleHangarScene`) | `{8, 19, 64, 64, 60}` | retrigger every 60 ticks |
| 15 | landing scene | `{4, 20, ...}` | sustain |
| 16 | landing scene | `{0, 21, 64, 64, 60}` | |
| 17 | landing scene | `{4, 22, ...}` | sustain |
| 18 | landing approach scene start | `{2, 23, 84, 64, 6, 0, 57}` | glide 84→57 |
| 19 | launch (frame 23) | `{0, 24, 64, 64, 60}` | |
| 20 | launch doors (`LaunchPlayerShip`) | `{2, 41, 24, 64, 2, 0, 127}` | glide 24→127 (rising whine) |
| 21 | cockpit (`cockpt.c` 1790) | `{0, 42, 64, 64, 40}` | |
| 22 | cockpit (`cockpt.c` 1785) | `{0, 43, 64, 64, 40}` | |
| 23 | VDU static (`malf_noise` 0x17) | `{0, 45, 64, 64, 5}` | prog 44 uses OPL rhythm bass-drum voice |
| 24 | cockpit (`cockpt.c` 920) | `{0, 46, 64, 64, 5}` | |
| 25 | VDU select / target lock / nav map select | `{0, 47, 64, 64, 5}` | |
| 26 | (no call site found) | `{0, 48, 64, 64, 40}` | |
| 27 | scramble (`cockpt.c` 2737) | `{0, 64, 64, 64, 40}` | |
| 28 | collision (`spc.c`) | `{0, 125, 64, 64, 40}` | |
| 29 | landing (`brains.c` 2081) | `{0, 62, 64, 64, 40}` | |
| 30 | funeral rifle volley | `{0, 63, 64, 64, 40}` | |
| 31 | cockpit / funeral | `{0, 106, 64, 64, 60}` | |
| 32 | damage alarm (shields collapsed) | `{8, 107, 64, 64, 40}` | retrigger every 40 ticks |
| 33 | ejection / debrief | `{0, 109, 64, 64, 60}` | |
| 34 | ejection / debrief | `{0, 110, 64, 64, 80}` | |
| 35 | barracks | `{0, 111, 64, 64, 40}` | |
| 36 | funeral | `{0, 112, 64, 64, 60}` | |

### 2.3 Music control

`unsigned int spacetrack(int track, int mode, short enabled)` — the universal "request music"
call. If `nMusicPlaybackMode ∈ {0, 3}` it does nothing. Otherwise
`ProcessMusicScriptCommand(track, mode, enabled)`:

* `track == -1` or `bMusicCommandSuppressed` → ignored.
* `mode == 4` ("queue_stop") → `StopMusic()`, `nCurrentMusicTrack = -1`.
* Re-requesting the *same* track while it is current is ignored for tracks 25, 38, 39, 40
  ("skipping for QA"); for other tracks `nCurrentMusicTrack` is simply re-assigned (no restart).
* `nCurrentMusicTrack = track`, then `SelectFlightMusicTrack(track)` (Saga: opens the right `.STR`),
  then: if the mission stream is active, tracks 0..5 and 12..18 are sent as **intensity**
  (`SetStreamerIntensity(track)`) and the others as **triggers** (`Streamer_trigger(track)`);
  for non-mission streams the track is mapped through `MapMusicTrackToStreamerCommand` and sent as
  `Streamer_trigger` (modes 0 "queue_start", 2 "queue_switch") or `ForceStreamerTrigger`
  (modes 1 "queue_break", 3 "queue_interrupt").
* **DOS path: only `nCurrentMusicTrack` matters.** `SdlServiceOriginFxMusic` (called every event
  pump, i.e. every frame) notices the change and starts the section. `mode` and `enabled` are
  ignored. Modes seen in calls: 1 (in-flight / cutscene cues, `enabled` -1 or 0) and 2 (scene
  entry, `enabled` 1).

Other functions:

| Function | Semantics |
|---|---|
| `StopMusic(short)` | `nCurrentMusicTrack = -1`, `Streamer_stop()`. DOS: service routine sees -1 and destroys the player, sets `nMusicTrackComplete = 1`. |
| `StopMusicUnlessSuppressed()` | `StopMusic(0)` if music mode allows music. Called when leaving every scene. |
| `GetMusicMode()` | Returns 1 iff music enabled **and** `nMusicTrackComplete != 0` — i.e. "the music has finished". Used by `funeral_sequence` to end the scene when the track ends. |
| `wait_for_end_of_music()` | Spin until `nMusicTrackComplete` or Escape; then `StopMusic`. Not referenced by any caller in the reconstruction. |
| `SetMusBreakpt(a, b)`, `FadeMusic()`, `SetMusicOn(x)`, `PreloadMusicTrackHook(t)`, `ReleaseMusicTrackHook(t)`, `SceneLeaveHook`, `FxDriverShutdownHook`, `GetFxDriverStatus/InitResult`, `GetMusicDriverPresent` | Stubs in the Saga build (debug print only). In the DOS original these were OriginFX driver calls: preload/release a `MUSIC.MID` section into memory, set a "music breakpoint" (end at next cue), fade. The port needs none of them beyond keeping the preload hints as an optional cache warm-up. |
| `EnableMusicForScene()` | `nWaitForMusicEnabled = 1; SetMusicOn(1)`. |
| `init_inflight_music()` / `free_inflight_music()` | Enter/leave flight: set/clear `nInFlightMusicActive`, reset combat flag; `free_` stops music. |
| `SetMusicStreamVolume(u16)` | Saga: `ix_streamer_set_volume`. DOS: gain derived from `nMusicVolumeSetting` in the service routine. |

### 2.4 In-flight music selection (`servicetrack` → `gametrack`)

`Update_3Space()` (once per simulation frame) calls `servicetrack()`:

1. `gametrack()`:
   * Only when `nInFlightMusicActive`.
   * Training simulator (`nTrainSimActive`): keep track **20** (Arcade Theme) requested whenever
     the current track differs (this is what loops it).
   * Otherwise, every 16th frame (`(nSpaceFrame & 0xf) == 0`) **or whenever `nMusicTrackComplete`**:
     * if `nCombatMusicActive`: pick 3 if `missile_on_tail`, else 1 if `any_enemy_tail`, else 2 if
       `is_ship_tailing_player_target`, else by `calculate_damage_level()`: 0 → track 0, 1 → 5,
       ≥2 → 4. If `report_kilrathi_rout(1) == 0` (no enemies left) clear `nCombatMusicActive`.
     * else: `changetrack()`, and if `report_kilrathi_rout(2)` (enemies present) set
       `nCombatMusicActive`.
   * `spacetrack(track, 1, 0)`.
   * Because the DOS service sets `nMusicTrackComplete = 1` and `nCurrentMusicTrack = -1` when a
     section ends, the next `gametrack` immediately re-requests → flight music effectively
     **loops with ≤ 1 frame gap** (restart from the beginning).
2. Proximity effects (if `nFlightSoundEffectsEnabled`), per object: asteroid at distance 0 with
   previous distance < 50 and not already flagged → sound 6; ship/capital ship on screen closer
   than 0x55a with a trajectory passing the eye (`dot < 0xdd`) → sound 2. Rate limit (corrected):
   the "10-frame hold per object" never takes effect because the release test
   `class != SHIP || class != CAPITAL_SHIP` is always true, so the tracked object is dropped on
   every pass; only the cooldown limits the sound (`cooldown < nSpaceFrame`, then
   `cooldown = nSpaceFrame + 6`): at most one passing sound every 7 frames.

`changetrack()` (non-combat cruise track): escort → 18, strike → 17, defend/rendezvous → 16,
else 15; if the current objective is the home base: triumph → 13 (patrol) / 14 (other), else 12.

`new_space_music_changes(attacker, victim)` (called from `ship.c` when something is destroyed):
Kilrathi victim → 10 (Overall Victory) if that was the last enemy, else if the player killed it:
6 (Target Hit) with 3/4 probability for unrated ships, otherwise 9 (Enemy Ace Killed); friendly
victim → 8 if it was the wingman, 11 if it was the escort/defend target, else 7 (Ally Killed).
`update_missile_warning()` requests 3 while a missile is tracking. All use mode 1.

### 2.5 Scene music (track → scene)

Track numbers are indices of `MUSIC.MID` sections. Names come from the SMF track-name meta events
in the real file; durations are computed from the real file at the embedded tempi.

| # | Name (from file) | Length | Requested by |
|---|---|---|---|
| 0 | Regular Combat | 63.4 s | gametrack, combat, no damage |
| 1 | Being Tailed | 10.6 s | gametrack |
| 2 | Tailing An Enemy | 16.8 s | gametrack |
| 3 | Missile Tracking You | 19.1 s | gametrack, `update_missile_warning` |
| 4 | You're Severely Damaged | 25.1 s | gametrack, damage ≥ 2 |
| 5 | Intense Combat | 21.5 s | gametrack, damage 1 |
| 6 | Target Hit | 4.8 s | kill cue |
| 7 | Ally Killed | 14.3 s | kill cue |
| 8 | Your Wingman's Been Hit | 12.0 s | kill cue |
| 9 | Enemy Ace Killed | 5.9 s | kill cue |
| 10 | Overall Victory | 7.1 s | last enemy destroyed |
| 11 | Overall Defeat | 19.1 s | escort target lost |
| 12 | Returning Defeated | 30.6 s | changetrack |
| 13 | Returning Normal | 28.9 s | changetrack |
| 14 | Returning Triumphant | 31.7 s | changetrack |
| 15 | Flying to Dogfight | 49.4 s | changetrack (patrol/default) |
| 16 | Goal Line – Defending the Claw | 32.1 s | changetrack (defend/rendezvous) |
| 17 | Strike Mission | 29.7 s | changetrack (strike) |
| 18 | Grim or Escort Mission | 30.7 s | changetrack (escort) |
| 19 | "Current" (startup intro orchestra) | 25.2 s | `SdlPlayDosStartupIntro`; the only track with cue events |
| 20 | Arcade Theme | 40.0 s | training simulator (looped by gametrack) |
| 21 | Arcade Victory | 1.6 s | `set_up_next_wave` |
| 22 | Arcade Death | 1.7 s | `ShowGameOverScreen` |
| 23 | Fanfare (title) | 164.6 s | `Title_Sequence`, `ShowTheEndScreen` |
| 24 | Briefing intro | 16.7 s | preloaded by `Briefing` only |
| 25 | Briefing middle | 31.0 s | `LoadBriefingRoom`, `EstablishingShot` |
| 26 | Briefing end | 11.2 s | `ReturnToBriefingLongShot` |
| 27 | Scramble through launch | 23.9 s | `PlayScrambleHangarScene` |
| 28 | Landing | 14.1 s | `ShowCarrierLaunchSequence` (landing approach fly-in) |
| 29 | Medium Damage Assessment | 20.0 s | `landing()` |
| 30 | Rec Room | 96.5 s | `RecRoom` |
| 31 | Eject – Imminent Rescue | 47.5 s | `ejection_sequence` |
| 32 | Funeral | 100.0 s | `funeral_sequence`, `death_sequence` |
| 33 | Debriefing – Successful | 52.5 s | `DeBriefing`, victory sequence, meanwhile variant 0 |
| 34 | Debriefing – Unsuccessful | 43.8 s | `DeBriefing`, Tiger's Claw escape, meanwhile variant 1 |
| 35 | Barracks | 126.3 s | `BarracksScreen` |
| 36 | Commander's Office | 108.2 s | `Office` |
| 37 | Medal Ceremony – General | 23.9 s | preloaded by `AwardCampaignMedal` (not requested) |
| 38 | Medal Ceremony – Purple Heart | 24.0 s | medal 3 |
| 39 | Minor Bravery | 16.7 s | medals 0, 1 |
| 40 | Major Bravery | 16.7 s | medals 2, 4 |

Saga stream mapping (for completeness): tracks 0–18, 27, 31, 32 → `MISSION.STR`; 20–26, 30, 35 →
`PREFLITE.STR`; 28, 29, 33, 34, 36–40 → `POSFLITE.STR`; 19 → no stream ("ofx music");
`MapMusicTrackToStreamerCommand` gives the trigger tag per track (see `music.c` lines 978–1066).

### 2.6 Volume hotkeys and persistence

* Startup: `LoadVolumeSettingsFromRegistry()` (defaults 20/20 and writes them back), then
  `SetSoundEffectsVolume(anVolumeLevels[sfx/2])` and `SetMusicStreamVolume(anVolumeLevels[music/2])`.
* In flight: Ctrl+Up/Down = SFX ±1 (clamped 0..20, saved to registry, message "SFX VOLUME: n"),
  Ctrl+Left/Right = music ±1, Ctrl+S toggles SFX 0 ↔ 20, Ctrl+M toggles music 0 ↔ 20.
* DOS gain conversion (`SdlCalculateDosAudioGain`): `level = anVolumeLevels[clamp(setting/2, 0, 10)]`,
  `gain = clamp(level, 0, 64000) * 0x7fff / 64000` (0..32767, applied as `sample * gain / 0x7fff`).

---

## 3. Data formats

### 3.1 Origin packet container (used by `MUSIC.MID`, `WINGLDR.TIM`, `*.DRV`, and most `.VGA`/`.000` files)

```
u32 LE   declaredFileSize            (must equal file length)
u32 LE   entry[0]  = (comp << 24) | offsetOfSection0   ; low 24 bits also == directory size
u32 LE   entry[1]  = (comp << 24) | offsetOfSection1
...
sectionCount = (entry[0].offset / 4) - 1
section i occupies [entry[i].offset, entry[i+1].offset) ; the last one ends at declaredFileSize
```

`comp == 1` → LZW with a `u32 LE uncompressedSize` prefix; any other value (0, 0xE0 in Saga's
`MUSIC.MID`, 0xFF for empty sections) → raw bytes. The game-side reader `OpenPacketSection` reads
entry `section*4+4` — identical indexing.

Origin LZW (`SdlDecompressOriginLzw`): codes are **LSB-first bit-packed**, initial width 9 bits,
max 12; `0x100` = clear, `0x101` = stop, first dynamic code `0x102`. After a clear: width 9,
`next = 0x102`, `prev = none`. For each code `c`: if `c == next` it is the KwKwK case (emit
string(prev) + first byte of string(prev)); otherwise emit string(c). If `prev` exists, add
`dict[next++] = (prev, firstByte(emitted))`; when `next == 1 << width` and `width < 12` then
`width++`. No automatic reset at 4096 — the encoder emits an explicit clear. Decoding must produce
exactly `uncompressedSize` bytes.

### 3.2 `MUSIC.MID` (DOS)

Real file: 139,707 bytes, 41 sections (0..40), **all LZW compressed**, every section decodes to a
Standard MIDI File:

* `MThd`, header length 6, **format 1**, 5..19 tracks, **division 480 PPQN**. Track 0 holds the
  tempo map and the song name; other tracks each carry one channel (track names such as
  "Percussion", "Fr. hn 2", "Syn Brass 2", "Square wave bass").
* Default tempo 120 BPM; 11 sections contain tempo changes (e.g. 12, 13, 14, 15, 16, 19, 31, 35, 36).
* Channel events used: note on/off (`0x90/0x80`, running status), control change (`0xB0`:
  **7** volume, **10** pan, **121** reset-all-controllers only), program change (`0xC0`), pitch
  bend (`0xE0`, tracks 19, 30, 31). No sysex. Channels 1..9 (0-based 0..8) melodic; **channel 10
  (index 9) is percussion**, note number selects the drum.
* Program numbers that appear: 2, 4, 24, 25, 47–51, 58, 70, 72–75, 82–84, 93, 94, 97, 112, 113,
  121, 122. (72, 73, 82 have no AdLib timbre and fall back to the first bank entry.)
* Meta events: 3 (track name), 6 (marker), 0x2F (end), 0x51 (tempo), 0x54 (SMPTE offset),
  0x58 (time signature).
* **OriginFX-specific status `0xFE`**: `FE subtype len payload[len]` (len is a single byte, not a
  VLQ). Observed in the real file:
  * `FE 03 01 <n>` — **sequence cue / position marker**, n = 0..6. Only in section 19 (intro). The
    SDL port stores `n` in `sequencePosition`; `SdlPlayDosStartupIntro` polls it to synchronise the
    orchestra animation (positions coincide with markers "Start Tuning" tick 0, "Tap on music
    stand" 5300, "Start Piano intro" 9600, "Start fireworks" 18240, "More fireworks" 21240, then
    23040 and 24480 = end). Every track of the section carries the same cue list.
  * `FE 02 02 <int16 LE>` — appears in most tracks at irregular ticks with slowly ramping
    negative values (e.g. -188, -139, -132, …, -13 in steps of 7). Meaning unknown (looks like
    sequencer-tool timing/pitch correction data). Ignored by the port.
  * `FE 00 02 <u16 LE>` — one per track at the end tick, value differs per track. Unknown. Ignored.
  * `FE FF 00` — appears at loop boundaries (section 31 at 30720 "Loop to here, infinite" and 39840;
    section 23 at 67891 and the end) and at the end of many tracks. Possibly a loop/segment
    boundary. Ignored.
  * `FE 01 00` — section 30 (Rec Room) every 7680 ticks (4 bars). Unknown (bar marks?). Ignored.
  * `FE 05 03 <..>` — at tick 0 of sections 23 and 33 tracks. Unknown. Ignored.
  * Text markers "Loop back to here" (tick 3840) / "Loop from here" (38400) in section 20 and
    "Loop to here, infinite" (30720) / "Loop from here-infinite" (38400) in section 31 show the
    composer intended loops; **the SDL port implements no loops** (sections play linearly to the
    end). See Open Questions.

Any `0xF0/0xF7` sysex is skipped; status `0xFF` resets running status; any other status byte is
a parse error.

Event scheduling (`OriginFxLoadMidi`): all tracks are parsed into one list `(tick, order, ...)`,
stably sorted by tick; walking it with the tempo map converts ticks to **output frames at
22050 Hz**: `frame += Δticks * 22050 * tempo / (480 * 1e6)` (accumulated in long double, rounded
when stored). `endFrame` = frame of the largest tick over all tracks (the end-of-track meta
counts), at least one frame past the last event.

Rounding note (added while porting): 103 events of sections 14, 18, 19 and 35 fall exactly on a
half frame (x.5). With a 64-bit `long double` (MSVC) the accumulated value lands just below .5 and
rounds down; exact arithmetic rounds up, and an 80-bit x86 GCC build may go either way. The C#
port accumulates in `double` and is bit-identical to the MSVC-built reference (all 41 sections);
the difference elsewhere would be one frame (45 µs).

### 3.3 `WINGLDR.TIM` (timbre bank)

Real file: 5,596 bytes, 3 LZW sections:

| Section | Decoded size | Content |
|---|---|---|
| 0 | 9,140 = 1 + 37 × 247 | `"%Wingleader"` header; **Roland MT-32 custom timbres** (37 entries: 246-byte MT-32 timbre + 1 program byte; names "Sonic Boom", "SquareWave", "Explosion", "Obj_pass", "Spark1" …). Not needed for the OPL port. |
| **1** | 3,793 = 1 + **79 × 48** | **AdLib/OPL2 timbres** — the section the port uses (`SDL_PORT_ADLIB_TIMBRE_SECTION 1`). |
| 2 | 961 = 1 + 30 × 32 | 30 × 32-byte records; unknown target (probably the third driver: Tandy/PC speaker or Game Blaster). Not needed. |

AdLib section layout: `u8 count` then `count` records of 48 bytes. Record layout as consumed by
`originfx.cpp` (all offsets relative to the record):

| Offset | Size | Meaning |
|---|---|---|
| 0 | u8 | Modulator reg 0x20 (AM/VIB/EGT/KSR/MULT) |
| 1 | u8 | Modulator reg 0x40 (KSL/Total Level) — base TL, bits 6-7 = KSL |
| 2 | u8 | Modulator reg 0x60 (Attack/Decay) |
| 3 | u8 | Modulator reg 0x80 (Sustain/Release) |
| 4 | u8 | Modulator reg 0xE0 (waveform) |
| 5–9 | u8 ×5 | Carrier regs 0x20, 0x40, 0x60, 0x80, 0xE0 |
| 10 | u8 | Channel reg 0xC0 (feedback bits 1-3, connection bit 0). Bit 0 set (additive) also means "modulator is an audible operator → apply panning to it too". |
| 11 | u8 | **Rhythm voice**: 0 = melodic; 6 = bass drum, 7 = snare, 8 = tom, 9 = cymbal, 10 = hi-hat (OPL2 rhythm mode). Also used as "carrier not programmed if ≥ 7 while in rhythm mode" (single-operator drums). |
| 12 | u8 | Carrier velocity sensitivity 0..7 (0 = ignore velocity) |
| 13 | u8 | Modulator velocity sensitivity 0..7 |
| 14 | u8 | Pitch-bend range: `pitchOffset += (bend - 0x2000) * t[14] >> 8`; 96 → ±12 semitones for full bend (pitch units are 256 per semitone) |
| 15 | u8 | Mod-wheel scale: CC1 value v → `modulationDepth = (t[15] * v >> 7) + t[17]` |
| 16 | u8 | Vibrato rate (phase increment per 60 Hz tick, 8-bit phase) |
| 17 | u8 | Vibrato base depth |
| 18 | s16 LE | Initial pitch-envelope offset at note on (pitch units) |
| 20 / 22 | s16 / s16 | Envelope stage A: rate per tick / target |
| 24 / 26 | s16 / s16 | Stage B: rate / target |
| 28 / 30 | s16 / s16 | Stage C: rate / target |
| 32 / 34 | s16 / s16 | **Release** stage (after key off): rate / target |
| 36 | s16 LE | Fixed detune added to pitch (e.g. -4864 = -19 semitones, 6144 = +24) |
| 38 | u8 | **Link**: non-zero → the *next* record in the bank is layered on the same note (both start and stop together). Only program 127 uses it (→ 149). |
| 39 | s8 | Key-tracking shift: `keyPitch = ((note-60)*256) >> t[39]` (+60*256); negative value inverts the sign and uses `~t[39]` as shift. 0 = normal tracking; 2/3/4 = compressed; 253 (-3) = inverted. |
| 40–46 | | zero in every record |
| 47 | u8 | **Program number** this record answers to (0..127 melodic; 128..149 percussion pseudo-programs; SFX programs reuse the 0..127 space) |

Lookup is a linear search for `t[47] == program`; a miss returns the **first record**.

### 3.4 `*.DRV` files

`STRAX.DRV` (4,869 B, 3 sections: 1192/3838/1231 B decoded; section 0 contains the string
"Origin Sound System!"), `SUPERTM.DRV` (65,281 B, 4 sections: 30053/57133/0/35650 B; contains
large waveform/sine tables), `TM.DRV` (29,779 B, 4 sections: 19935/19496/0/14043 B). Each decoded
section begins with a 16-bit offset jump table — these are **real-mode x86 code overlays** (the
DOS OriginFX sound-card drivers: AdLib/"STRAX", and two others — likely Roland MT-32 and a
software/Tandy synth). They are loaded by the DOS `LoadOriginFxDrivers` and are **not needed**
when the OPL2 is emulated directly; the SDL port never opens them. The port should ignore them
(only verify their presence if you want a "DOS install" sanity check).

---

## 4. The OriginFX/AdLib synthesis model (as implemented in `originfx.cpp`)

The replayer is a faithful re-implementation of the behaviour of `STRAX.DRV` + the WC.EXE music
code (comments cite `STRAX.DRV 0000:01dc-020e`, `WC.EXE 2231:051e-063d`, `2231:7967/79b4`,
`19c5:1682-174a`). Two independent instances exist: the **music player** (one per playing section)
and the long-lived **sound player**.

### 4.1 Constants

| Name | Value |
|---|---|
| Output rate | 22050 Hz |
| OPL clock | 3,579,545 Hz → native sample rate `3579545/72` = **49715 Hz** (integer division in ymfm `sample_rate(clock)`) |
| Service (driver tick) rate | **60 Hz** (envelopes, vibrato, SFX durations) |
| Logical channels | 26 (0..8 melodic MIDI channels, 9 = GM percussion input, **9..25 = percussion pseudo-channels** each with its own program, 1..8 also used by the SFX engine) |
| Voice states | 11 (0..8 = OPL2 melodic channels 0..8; 6..10 = rhythm-mode BD/SD/TT/CY/HH when rhythm mode is on, at which point only 0..5 are melodic) |
| SFX channels / slots | MIDI channels 1..8 (`ORIGINFX_FIRST_SOUND_CHANNEL 1`, 8 channels); 32 effect slots |
| Timbre size | 48 bytes |

### 4.2 Chip setup

Each player owns **two YM3812 instances** (`ymfm::ym3812`, each with its own
`ymfm::ymfm_interface`), "left" and "right". Every register write goes to both chips; only the
operator level registers (`0x40+op`) may differ per side (panning). The music player leaves
`stereoPanningEnabled = 0` so the right chip is written to but never rendered (its output is a copy
of the left sample); the sound player sets it to 1. A port can use one chip for music and two for
effects (or one chip plus post-mix panning per *voice* is impossible, because OPL2 mixes all
channels internally — hence the two-chip trick).

`OriginFxResetOpl`: `reset()` both chips; write `0x01 = 0x20` (waveform select enable),
`0x08 = 0`, `0xBD = 0`, `0xA0..0xA8 = 0`, `0xB0..0xB8 = 0`; melodic voices = 9, rhythm reg = 0.

Operator register offsets per voice (index into OPL operator slots), two tables selected by rhythm
mode (`melodicVoiceCount < 9` → use the second half, index + 9):

```
carrier   = {3,4,5,11,12,13,19,20,21,   3,4,5,11,12,13,19,20,18,21,17}
modulator = {0,1,2, 8, 9,10,16,17,18,   0,1,2, 8, 9,10,16,20,18,21,17}
OPL channel for voice v: v <= 8 ? v : 17 - v   (voices 9,10 → channels 8,7)
rhythm bit for voice 6..10: 0x10, 0x08, 0x04, 0x02, 0x01  (BD, SD, TT, CY, HH)
```

### 4.3 Programming a voice (`OriginFxProgramVoice`)

```
0x20+mod = t[0]; 0x40+mod = t[1]; 0x60+mod = t[2]; 0x80+mod = t[3]; 0xE0+mod = t[4]
if (melodicVoiceCount == 9 || t[11] < 7):          // skip carrier for single-op drums
    0x20+car = t[5]; 0x40+car = t[6]; 0x60+car = t[7]; 0x80+car = t[8]; 0xE0+car = t[9]
    0xC0+chan = t[10]
then OriginFxWriteVoiceLevels
```

Levels (`OriginFxWriteVoiceLevels`), with channel volume `vol` (0x80..0xFF, 0xFF default) and
note velocity `vel`:

```
velAtten(sens, vel)  = sens == 0 ? 0 : (63 - vel) >> (7 - min(sens,7))     // arithmetic, rounds toward -inf
carrierTL  = t[6] & 0x3f + velAtten(t[12], vel)
carrierTL  = 63 - ((vol * (63 - carrierTL)) >> 8)          // channel volume scaling
            (written only if t[12] != 0 || vol < 0x100; keep KSL bits of t[6]; clamp 0..63)
modulatorTL = t[13] != 0 ? (t[1] & 0x3f) + velAtten(t[13], vel) : t[1]
            (written only if t[13] != 0, or stereo && (t[10] & 1))
pan (stereo players only, pan 0..127, 64 = centre):
    left  TL' = pan <= 64 ? TL : 63 - (127 - pan) * (63 - TL) / 63
    right TL' = pan >= 64 ? TL : 63 - pan * (63 - TL) / 64
    applied to the carrier always, to the modulator only if t[10] & 1 (additive connection)
```

Frequency (`OriginFxWriteVoiceFrequency`): pitch units = 1/256 semitone, 60*256 = middle C.

```
keyPitch = (note - 60) * 256, key-tracked by t[39] (see 3.3), + 60*256
pitch    = keyPitch + voice.envelopePitch + t[36] (detune) + voice.modulationPitch
         + ((bend - 0x2000) * channelTimbre[14]) >> 8      // channelTimbre = timbre of channel program (not the layered one)
note = floor(pitch / 256), frac = pitch mod 256 (0..255)
idx = (note + 6) mod 12 ; block = clamp((note + 6) / 12 - 2, 0, 7)   // floor division
fnum = F[idx] + (F[idx+1] - F[idx]) * frac / 256 ; clamp 0x3ff
F = {485, 514, 544, 577, 611, 647, 686, 727, 770, 816, 864, 915, 970}
0xA0+chan = fnum & 0xff ; 0xB0+chan = (fnum >> 8) | block << 2 | (keyOn ? 0x20 : 0)
```

(Note 60 → F[6]=686, block 3 ≈ 260 Hz; note 69 → 577, block 4 ≈ 438 Hz.)

### 4.4 Note on / off, voice allocation, rhythm mode

Note on (`OriginFxNoteOn`): find timbre for the channel program; for it and every *linked*
record (t[38] ≠ 0 → next record) call `OriginFxStartTimbre`:

* If `t[11] != 0` (percussion drum): enable rhythm mode if needed (`OriginFxEnableRhythmMode`:
  writes `0xA6/0xB6 = 0`, `0xA7 = 0, 0xB7 = 0x0a`, `0xA8 = 0x54, 0xB8 = 0x09`, melodic voices := 6,
  `0xBD = 0x20`, clears voices 6..10) and use voice `t[11]`; clear that drum's bit in `0xBD`.
* Else pick a voice among the melodic ones: the **least recently used free voice**; if none is
  free, **steal the oldest active voice** (`age` counter incremented on every start/stop). A stolen
  melodic voice gets `0xA0/0xB0 = 0` first (hard key-off).
* Initialise the voice: timbre, channel, note, velocity, `envelopeState = 2`, `modulationPhase = 0`,
  `envelopePitch = t[18]`, `modulationPitch = 0`; program it; then melodic → write frequency with
  key-on; drum → write frequency without key-on only for the bass drum (voice 6), then set the
  drum bit in `0xBD` (key-on for rhythm voices is the `0xBD` bit).

Note off: for the channel's timbre chain, find the active voice with matching channel/note/timbre;
drums → clear the `0xBD` bit; melodic → rewrite `0xB0+chan` with the stored block/fnum-high and
key-on cleared (release phase starts; the envelope keeps running). Voice marked inactive,
`envelopeState = 0` (release pitch stage), age bumped. "All notes off" (CC 123 or channel -1) does
the same for all voices; for channel -1 it also leaves rhythm mode (`0xBD = 0`, 9 melodic voices).
`OriginFxDisableRhythmModeIfIdle` returns to 9 melodic voices when no drum voice is active
(called after CC 123).

Program change (`OriginFxSetProgram`): all notes off on that channel, detach voices, set program,
load `modulationRate = t[16]`, `modulationDepth = t[17]`; if the timbre is a drum, enable rhythm
mode immediately. (Verified on the data: only program 44 — used by sound 23 — is a rhythm-mode
timbre; the percussion pseudo-channel programs below are ordinary 2-operator timbres, so music
never switches the OPL into rhythm mode.) Initial state (`OriginFxInitializePlayer`): every channel `pitchBend = 0x2000`,
`volume = 0xff`, `pan = 64`, program = program number of the first bank record; then channels
9..25 get the fixed percussion programs
`{0x80, 0x72, 0x83, 0x71, 0x86, 0x87, 0x85, 0x84, 0x81, 0x88, 0x8d, 0x8f, 0x90, 0x91, 0x93, 0x8c, 0x8b}`
(128, 114, 131, 113, 134, 135, 133, 132, 129, 136, 141, 143, 144, 145, 147, 140, 139).
Program 139 (channel index 25, GM notes 60/61 "bongos") is **not in the bank**, so those notes
play with the first record (program 2), as in the reference.

GM percussion input (channel index 9) is translated per note into (pseudo-channel, fixed pitch)
and then handled as a normal note on that channel:

| GM note | → channel (0-based) | pitch | | GM note | → channel | pitch |
|---|---|---|---|---|---|---|
| 35, 36 | 9 | 48 | | 51 | 18 | 77 |
| 37 | 17 | 48 | | 56 | 20 | 71 |
| 38 | 10 | 48 | | 60 / 61 | 25 | 72 / 79 |
| 40 | 11 | 48 | | 62 | 24 | 79 |
| 41, 43 | 12 | 42 | | 63 / 64 | 19 | 64 / 58 |
| 42 | 16 | 71 | | 67 / 68 | 20 | 89 / 84 |
| 44 | 15 | 71 | | 69 | 21 | 48 |
| 45, 47 | 12 | 47 | | 70 | 22 | 72 |
| 46 | 13 | 71 | | 73 | 23 | 36 |
| 48, 50 | 12 | 52 | | 75 | 19 | 96 |
| 49 | 14 | 79 | | others (<35, 39, 52–55, 57–59, 65, 66, 71, 72, 74, 76, ≥77) | ignored | |

(The table in the source is `g_abOriginFxPercussionChannels[77]` holding channel+1 and
`g_abOriginFxPercussionPitches[77]`.)

Control changes (`OriginFxApplyControlChange`): CC1 → `modulationDepth = (t[15]*v >> 7) + t[17]`;
CC7 → `volume = v + 0x80`; CC10 (stereo players only) → pan, re-write levels of active voices;
CC121 → `modulationDepth = t[17]`, `volume = 0xff`, `pitchBend = 0x2000`, re-write frequencies;
CC123 → all notes off + maybe leave rhythm mode. A CC on channel index 9 is also broadcast to
channels 10..25 (so "percussion volume" affects all drum pseudo-channels). Pitch bend sets the
14-bit value and re-writes the frequency of the channel's active voices.

### 4.5 60 Hz service (`OriginFxService`)

For every melodic voice with a timbre (drums are not serviced):

```
state = envelopeState         // 2,3,4 = stages A,B,C (offsets 20/22, 24/26, 28/30); 0 = release (32/34); 1 and ≥5 = idle
if stage active:
    target = s16 t[targetOff]; rate = u16 t[rateOff]
    if |target - envelopePitch| < rate: envelopePitch = target; state++   // next stage
    else envelopePitch += rate toward target
if channel.modulationRate != 0:            // vibrato, 8-bit triangle
    phase = (phase + rate) & 0xff ; tri = phase as s8 ; if tri > 63 || tri < -64: tri = (0x80 - phase) as s8
    modulationPitch = (modulationDepth * tri) >> 4
if anything changed: rewrite frequency with keyOn = (state > 1)
```

Rates/targets are in pitch units per tick; e.g. laser program 9: start +1000, stages
(200→-500), (100→-1500), (50→-2000): a descending zap.

The service accumulator runs inside the render loop: per output frame `acc += 60; while acc >= 22050: acc -= 22050; service SFX; service voices`
— i.e. the tick is phase-locked to the output stream, not wall-clock.

### 4.6 Sound-effect engine (DOS data)

Record table `g_aabOriginFxSoundRecords[36][8]` (origin `WC.EXE 2231:051e-063d`), indexed by
`soundNumber - 1`; row layout `{flags, program+1, note, velocity, durationLo, durationHi, glideTarget, unused}`
(values listed in 2.2). Flags:

| bit | meaning |
|---|---|
| 1 | **chain**: when the record expires, continue with the next row of the table (not used by any shipped row, but supported) |
| 2 | **glide**: on expiry step `currentNote` by ±1 toward `glideTarget` and retrigger; when the target is reached the note is reset to `note` but **not** retriggered, so the effect ends after one sweep unless flag 8 is also set (corrected; verified: sound 20 = 103 steps x 2 ticks = 3.43 s, sound 2 = 64 x 1 tick, sound 18 = 27 x 6 ticks) |
| 4 | **sustain**: never expires; stopped only by `stop_all_sounds`/tag replacement |
| 8 | **retrigger**: on expiry restart the same note (periodic beeps/klaxons) |

`SdlPlayOriginFxSoundEffect(player, number, volume 0..127, pan 0..127, tag, priority)`:

1. If an active slot has the same `tag`, stop it (note off) and reuse its channel, age and priority.
2. Else choose a channel: first free MIDI channel in 1..8; if all busy, steal the **oldest active
   effect whose priority ≤ requested priority** (fail → return 0 → sound dropped).
3. Fill a free slot (`record`, `age`, `tag`, `priority`, `channel`, `volume`, `pan`) and start it:
   program change to `record[1]-1` (which also forces notes off on that channel), CC7 = volume,
   CC10 = pan, `remainingTicks = duration`, note on (`note`, `velocity`).
4. `OriginFxServiceSoundEffects` (60 Hz): skip sustain records; decrement ticks; at 0 → note off,
   then glide/retrigger/chain/stop as per flags; a stopped effect sends CC123 on its channel.

`SdlStopOriginFxSoundEffects`: stop all slots (CC123 each) and all notes off (-1).

Because effects are played through the same channel/voice machinery as music, an effect uses one
OPL voice per timbre layer; with 8 SFX channels and 9 (or 6) voices, the LRU/oldest stealing in
4.4 decides what survives. Effects and music never share a chip, so they cannot steal each other's
voices.

### 4.7 Rendering and resampling

`OriginFxGenerateOutputSample`: `acc += nativeRate (49715, see below); while acc >= 22050: generate one native
sample from the left chip (and the right chip if stereo), accumulate; acc -= 22050`; output = mean
of the 2–3 native samples generated for that output frame (sample-and-hold if none). This is a
crude box-filter decimation; a C# port may instead run the mixer at the native 49715 Hz or use a
proper resampler — the result is audibly equivalent but not bit-identical. (The C# port keeps the
box filter: it is bit-identical to the reference.)

Native rate (corrected): ymfm's `sample_rate()` is an integer division, `3579545 / (4 * 18)` =
**49715**, which is the value added to the accumulator.

Per output frame (`SdlMixOriginFxPlayer`): dispatch all events with `frame <= currentFrame`; if no
events remain and `currentFrame >= endFrame` → all notes off, `finished = 1`; else service tick
accumulation, generate, scale by gain (`sample * gain / 0x7fff`, saturate), add into the stereo
S16 buffer with saturation, `currentFrame++`.

### 4.8 ymfm API used

`ymfm::ym3812` (header `ymfm_opl.h`, sources `ymfm_opl.cpp` + `ymfm_fm.ipp`; `ymfm_adpcm`/`ymfm_pcm`
are only compiled because `ymfm_opl.cpp` references them). Calls: constructor
`ym3812(ymfm_interface&)`, `reset()`, `write(0, addr)` then `write(1, data)` (address/data port
pairs; no inter-write timing constraints are needed), `generate(output_data*)` producing one
`int32` sample in `data[0]` (OPL2 mono; verified: `roundtrip_fp` clamps it to -32768..32767), and
`sample_rate(clock)`. The interface subclass is empty (no timers/IRQ needed). Vendored at commit
`81aec25ccbb98f4873a255f7551ac4dadac59b4a`, BSD-3-Clause.

---

## 5. Mixer / output model

### 5.1 SDL device

`SdlStartAudio(mixer, criticalSection, tickPtr)`: `SDL_OpenAudioDevice` with **22050 Hz,
AUDIO_S16SYS, 2 channels, 1470 sample frames per callback** (= 1/15 s). The callback:
`EnterCriticalSection; mixer(stream, bytes); SdlMixOriginFxMusic(stream, frames); tick++ ; Leave`.
The `tick` counter is `dwDspTick` — the `ix` library's clock therefore runs at **15 Hz**
(`stopTime = start + sampleCount * 15 / frequency`).

### 5.2 DOS mode (`SdlInitializeOriginFxAudio(useStandaloneAudio = 1)`)

* Loads `GAMEDAT/MUSIC.MID` (whole file kept in memory) and section 1 of `GAMEDAT/WINGLDR.TIM`
  (decoded, kept); creates the sound player; creates an `SDL_mutex`; opens the device with
  `SdlMixDosAdlibMusic` as the mixer:
  `memset 0; lock; if music player: render (zero then mix) with music gain; if sound player: mix SFX with sfx gain; unlock`.
* Game thread (`SdlPumpEvents` → `SdlServiceOriginFxMusic`, every frame):
  1. refresh gains from the two volume settings;
  2. if the current player `finished`: destroy it, `nMusicTrackComplete = 1`, and if
     `nCurrentMusicTrack` still names that track set it to -1;
  3. if `nCurrentMusicTrack` differs from the active track: -1 → destroy player and set complete;
     else extract the section (outside the lock), build a new player (parse + timbre init),
     re-check the request under the lock, swap players, `nMusicTrackComplete = 0`.
* `SdlPlayDosSoundEffect` / `SdlStopDosSoundEffects` take the mutex and drive the sound player
  from the game thread; all OPL register writes for effects therefore happen on the game thread,
  rendering on the audio thread, serialised by the mutex (no command queue).
* `SdlGetOriginFxMusicSequencePosition()` returns the last `FE 03` cue value (or -1 if no player).

### 5.3 Saga mode (`useStandaloneAudio = 0`, only with `--enhanced` or DOS data absent)

The `ix` DSP owns the device (`ix_dsp_init` spawns the "mixer thread", which on SDL just calls
`SdlStartAudio(ix_dspv_mix, &csMixer, &dwDspTick)` and sleeps). The OriginFX host only serves
track 19 (intro); any other `nCurrentMusicTrack` destroys the player. `SdlMixOriginFxMusic` adds it
after `ix_dspv_mix` inside the same callback.

### 5.4 Threading summary for the port

* Audio thread: pull-model callback; must not allocate; needs read access to the synth state.
* Game thread: posts note/program/controller changes (SFX) and track swaps. The reference
  serialises with a mutex; a lock-free SPSC command ring (game → audio) with the synth living
  entirely on the audio thread is the cleaner NativeAOT-friendly design. Track changes should be
  pre-parsed on the game thread and handed over as an immutable object.
* As ported (see `docs/progress/audio.md`): the sound player lives on the audio thread behind a
  lock-free command queue; sequences are parsed once on the game thread and each start hands a
  fresh player to the mixer. Under ADR-009 (deterministic virtual clock) the game-visible
  `nMusicTrackComplete` and the intro cue position are computed from the virtual time since the
  track started (same `endFrame`/cue frames the audio thread renders), not read back from the
  audio thread.

---

## 6. Kilrathi Saga wave/stream path (`ix` library) — summary

Everything below is secondary for the port but defines what a Saga backend must provide.

### 6.1 Samples and sounds (`sample.cpp`, `sound.cpp`, `system.cpp`)

* `IxSample`: PCM 8-bit (converted to signed by subtracting 0x80) or 16-bit, mono/stereo,
  4000–44100 Hz, loaded from RIFF/WAVE (`fmt ` must be PCM) or AIFF. `loopStart/loopEnd` default to
  whole sample; flag `IX_SAMPLE_LOOPING (2)`.
* `IxSound`: a playing instance of a sample with `volume` (0..0xFFFF), `pan` (0..0xFFFF, mapped to
  a 128-step circular pan table — `panAngle = pan >> 9`), `pitchOffset` (Hz added to the sample
  frequency, clamped to ±rate), `basePriority` (default 0x80), `priority = volume*256/0xFFFF + pitchOffset*256/44100 + basePriority`.
* Voice management: `nSystemVoiceCount` voices (game sets 16; max 32 + 2 stream voices). `ix_sound_start`
  takes a free voice, otherwise steals the **lowest-priority playing** sound with priority below the
  new one (the victim goes to a waiting list and is re-assigned when a voice frees). `stopTime` is
  computed from sample length so finished sounds are reaped by `ix_system_service_sounds` (called
  once per presented frame) which also pushes dirty volume/pan/frequency to the voice.
* Game-side wrappers (`sound.c`/`pload.c`): a wave table caches loaded files by name
  (`WaveTableEntry {name, sample}`), an active list tracks looping sounds; `playWAVE(name, loop, vol)`
  loads on first use (reads the entire file through `_open`), sets `delete_on_stop` for one-shots.
  The SDL `SdlPlayWaveWithPan` wrapper sets a pending pan picked up by `SdlNewWaveSound`.

### 6.2 Mixer (`dsp.cpp`, `dspv.cpp`, `dsps.cpp`, `mixer.cpp`)

* 22050 Hz S16 stereo output. Original: DirectSound primary (or 32 KiB secondary) buffer, mixing
  0x16F8 bytes (= 1470 frames) at a time with a 0x42 ms sleep; SDL: the callback.
* `ix_dspv_mix`: zero the buffer; for each active voice: fixed-point 8.8 playback rate
  `(freq << 8) / 22050`, nearest-neighbour resampling (no interpolation), per-voice left/right gains
  from `pan table[pos] * (volume * master / 0xFFFF) >> 16`, saturating add. Loop flag
  (`IX_VOICE_FLAG4`) wraps at buffer end.
* Pan table (`ix_dsp_build_pan_tables`): 256 positions × (left, right) shorts; positions 0..63
  left = 0x7FFF, right = (32 - pos) * 0x3FF; 64..127 right = 0x8001, left = (96 - pos) * 0x3FF; the
  upper 128 entries are the negated mirror (phase-inverted rear). Volume units everywhere are
  0..0xFFFF, internally scaled to 0..0x7FFF.
* Streams (`dsps`): up to 2 ring buffers with their own voice slots after the regular voices;
  `lock/unlock` write, `get_buffer_free` tracks consumption via the voice cursor.

### 6.3 Streamer (`streamer.cpp`, `thread.cpp`, `lzo1x.cpp`) — the `.STR` music format

A dedicated thread services a package file ("STRM" id `0x4d525453`, version 1). Header (0x68 bytes)
gives PCM format (channels, bits, frequency, `audioBufferSize`), tables of **audio chunks**
`{fileOffset, fileEnd, triggerCount, firstTrigger, branchCount, firstBranch}`, **branches**
`{u8 intensity, u32 chunk}`, **triggers** `{u8 tag, u32 chunk}`, plus a packed-file directory
(name-hash entries, LZO1X-compressed chunks) for non-audio data (unused by WC1 — `.STR` is only
audio here). Playback: chunks are streamed sequentially into the ring buffer; at each chunk end
(`ix_thread_advance_audio_chunk`): if a trigger with tag `'A'` exists → pop the branch stack and
return there; tag `'@'` → mark end-triggered (stop after buffer drains); a trigger equal to the
pending `cStreamerBranchTag` (set by `Streamer_trigger(n)`, 0..0x40) → push current chunk and jump;
else if the chunk has branches → jump to the branch whose `intensity` is closest to
`bStreamerIntensity` (set by `SetStreamerIntensity`, 0..100); else next chunk (wrapping to 0).
`ForceStreamerTrigger` jumps immediately (searching forward from the current chunk) and restarts
the DSP stream. Volume via `ix_dsps_set_volume`. This is how Saga's `MISSION.STR` provides
intensity-driven adaptive music with triggered stingers, replacing the DOS `spacetrack` track
numbers (2.3). A Saga backend in C# needs: the header/table parser, a PCM ring-buffer voice, the
branch/trigger state machine, and LZO1X only if packed files are ever used.

---

## 7. Recommendations for the C# port

### 7.1 OPL2 emulator choice

Only YM3812/OPL2 features are needed: 9 two-operator channels, rhythm mode (`0xBD`), waveform
select (`0x01` bit 5, waveforms 0–3), KSL/TL, standard envelopes. Any OPL3 core in OPL2
compatibility mode (OPL3 mode bit off) is also fine.

Candidates (verify license and availability before adopting; none of these are claimed to be on
NuGet):

| Candidate | Language / license | Notes |
|---|---|---|
| **ymfm** (Aaron Giles) `ym3812` | C++17, **BSD-3-Clause** | What the reference SDL port uses. A C# port gives bit-identical output to the reference for regression testing. ~3k lines of template-heavy C++ (`ymfm_fm.ipp`, `ymfm_opl.cpp`); porting the OPL2 subset (`fm_engine_base<opl_registers_base<2>>`, `opl_registers_base`, `fm_operator`, `fm_channel`, the sin/exp tables, `ym3812` wrapper) is maybe 1.5–2k lines of C#. Permissive license is the main attraction. |
| **Nuked-OPL3** (nukeykt) | C, **LGPL-2.1** | Cycle-accurate OPL3 (OPL2 compatible), ~1.7k lines of plain C with fixed-size tables — the easiest to transliterate to C#. LGPL: with NativeAOT static linking you must ship relinkable object code or source for the combined work; acceptable for an open-source port, a problem for a closed one. Several unofficial C# transliterations exist (search "Nuked OPL3 C#"); treat them as LGPL. |
| **DOSBox `dbopl`** | C++, **GPL-2.0-or-later** | Very accurate, C# transliterations exist (e.g. inside C#-based emulators); GPL is viral for the whole program. |
| **MAME `ymfm`-era YM3812 / older fmopl.c** | BSD-3 (new MAME) / older GPL/MAME license | `fmopl.c` is the classic small OPL2 core (~2.5k lines); check the exact license of the copy. |
| **OPL3.java (Robson Cozendey)** and its C# ports | Java, **LGPL** | Used by several managed emulators (e.g. the `Ymf262Emu`/`Opl3` classes found in Spice86 and other C# DOS emulators are derived from it). Simpler but less accurate than Nuked; LGPL again. |
| Writing a fresh OPL2 from the datasheet | — | Not recommended; subtle envelope/KSL/rhythm behaviours matter for these timbres. |

Recommendation: **port ymfm's YM3812 to C#** (BSD-3, bit-exact parity with the reference port,
no license friction with NativeAOT), behind an `IOplChip` interface so a Nuked-OPL3 port can be
swapped in later. Keep the register-write/generate API identical to ymfm (`Write(addr, data)`,
`Generate(out int sample)`, `SampleRate`) so the sequencer code is emulator-agnostic.

### 7.2 Proposed class design (NativeAOT friendly: no reflection, no dynamic codegen, struct-heavy, `Span<T>`, pre-allocated buffers)

> As implemented (2026-10-07) the layout differs in details: `Opl/`, `OriginFx/`, `Dos/` (mixer,
> command queue, one `DosAudioBackend` implementing both backend interfaces) and `Director/`
> (game-facing API); the audio device stays in the host (`IGameHost.StartAudio`). See
> `docs/progress/audio.md`.

```
WingCommander.Audio
├── Backend
│   ├── IAudioBackend            // Open(sampleRate=22050, channels=2, framesPerBuffer=1470, IAudioRenderer); Start(); Stop(); Dispose
│   ├── IAudioRenderer           // void Render(Span<short> interleavedStereo)   (called on the audio thread)
│   ├── SdlAudioBackend          // P/Invoke SDL2/SDL3 audio; callback marked [UnmanagedCallersOnly], static, pinned context
│   └── (alt) WasapiAudioBackend / MiniaudioBackend / NullAudioBackend (tests)
├── Opl
│   ├── IOplChip                 // Reset(); Write(byte addr, byte data); int Generate(); int NativeSampleRate
│   ├── YmfmYm3812               // managed port of ymfm ym3812
│   └── OplResampler             // native 49715 Hz → 22050 Hz (box or polyphase); or run the whole mix at native rate
├── OriginFx
│   ├── OriginPacketFile         // container parser (also used by graphics/data loaders), OriginLzw decoder
│   ├── OriginFxTimbreBank       // 48-byte records, FindTimbre(program) with first-record fallback, Linked(t) → next record
│   ├── OriginFxSequence         // parsed MIDI section: immutable sorted event array with absolute 22050 Hz frames,
│   │                            // endFrame, cue events (FE 03), text markers, loop metadata (future)
│   ├── OriginFxSynth            // the driver model of §4: 26 channels, 11 voices, rhythm mode, two IOplChip (L/R),
│   │                            // NoteOn/NoteOff/ControlChange/ProgramChange/PitchBend/AllNotesOff, ServiceTick() at 60 Hz,
│   │                            // GenerateFrame(out int l, out int r)
│   ├── OriginFxSequencer        // plays an OriginFxSequence on an OriginFxSynth; Finished, SequencePosition, Mix(Span<short>, gain)
│   ├── OriginFxSoundEffectEngine// 36-row record table, 32 slots, 8 channels, tag/priority stealing, glide/retrigger/chain/sustain
│   └── DosAudioMixer : IAudioRenderer // owns music sequencer (swappable), SFX engine+synth, gains; applies the 60 Hz service
│                                      // accumulator and the saturating mix exactly as §4.7
├── Game
│   ├── MusicDirector            // nMusicPlaybackMode, nCurrentMusicTrack, nMusicTrackComplete; spacetrack/StopMusic/GetMusicMode;
│   │                            // gametrack/changetrack/new_space_music_changes logic (pure game state in, track requests out)
│   ├── IMusicBackend            // RequestTrack(int) / Stop() / bool IsComplete / int SequencePosition / SetVolumeSetting(int)
│   ├── SoundEffectManager       // PlaySfx(number, sourceObject, looping): distance/pan math of §2.2, afterburner/alarm bookkeeping
│   ├── ISoundEffectBackend      // Play(number, volume, pan, tag, priority) → bool; StopAll(); SetVolumeSetting(int)
│   ├── DosMusicBackend / DosSoundEffectBackend   // glue to DosAudioMixer (track change = parse on game thread, swap via command queue)
│   └── (later) SagaMusicBackend (StreamerPlayer for .STR) / SagaSoundEffectBackend (WaveCache + IxMixer voice allocator)
└── Settings
    └── AudioSettings            // sfx/music 0..20, VolumeLevels table, persistence (JSON file instead of HKLM registry)
```

Design notes:

* Keep the game-side state machine (`MusicDirector`) a literal transcription of `music.c` so
  behaviour (16-frame re-evaluation, "skipping for QA", loop-by-restart) is preserved; expose
  `nMusicTrackComplete` as a property the scenes can poll.
* Make `OriginFxSynth` independent of MIDI parsing so the SFX engine and the sequencer share it;
  one synth per player (music, SFX) as in the reference.
* Thread model: `DosAudioMixer.Render` runs on the audio thread. Game → audio messages (play
  effect, stop all, swap sequencer, set gains) go through a bounded lock-free queue
  (`System.Threading.Channels` is fine under NativeAOT, or a hand-written SPSC ring of structs).
  Avoid taking a mutex inside the callback.
* Buffers: pre-allocate `short[1470*2]` and the native-rate scratch; `stackalloc`-free in the
  callback; no LINQ, no boxing. `[UnmanagedCallersOnly]` callbacks must not throw.
* Determinism/testing: feed a sequence through `OriginFxSequencer` with a null backend and hash the
  generated PCM; compare with the C reference (`out-modern` build) rendering the same section to
  WAV. The ymfm port makes this bit-exact.
* The packet/LZW reader belongs to the shared data layer (graphics use the same container).

### 7.3 Suggested implementation order

1. `OriginPacketFile` + LZW (unit test: decode all 41 `MUSIC.MID` sections, all start with `MThd`;
   `WINGLDR.TIM` section 1 → 79 records).
2. `OriginFxTimbreBank`, `OriginFxSequence` parser (test: section 19 yields cues 0..6 at ticks
   0, 5300, 9600, 18240, 21240, 23040, 24480; durations match table 2.5).
3. `IOplChip` + ymfm port (test: register script → PCM hash against C reference).
4. `OriginFxSynth` + `OriginFxSequencer` → render sections to WAV, listen/compare.
5. `OriginFxSoundEffectEngine` (test: sound 20 glides 24→127 in 2-tick steps; 8 alternates tags).
6. `DosAudioMixer` + SDL backend; then `MusicDirector`/`SoundEffectManager` wired to the game loop
   (`servicetrack` per simulation frame, music service per presented frame).
7. Saga path later: WAV cache + voice mixer (§6.1–6.2), `.STR` streamer (§6.3).

---

## 8. Open questions

1. **Looping semantics of the DOS driver.** The SDL port plays every section linearly and relies on
   the game re-requesting tracks (gametrack every 16 frames / on completion). The real file carries
   loop markers (sections 20 and 31: "Loop back to here"/"Loop from here") and `FE FF`/`FE 00`
   events at those boundaries. Whether the original `STRAX.DRV`/WC.EXE sequencer honoured a loop
   (e.g. "Eject – Imminent Rescue" looping infinitely until the scene ends, the arcade theme
   looping from bar 2) is not established. Decide: emulate the port (restart from the start) or
   implement marker-based loops (risk: deviating from both DOS and port behaviour).
2. **Meaning of `FE 00`, `FE 01`, `FE 02`, `FE 05`, `FE FF` events** (section 3.2). Only `FE 03` is
   understood. Disassembly of the DOS WC.EXE music parser (segment `2231`) would settle it.
3. **`SetMusBreakpt` / "music breakpoint"**: the DOS original could presumably end a track at the
   next cue (used by `funeral_sequence` and `ejection_sequence` before waiting for
   `nMusicTrackComplete`). Unknown whether this truncated playback; the port ignores it.
4. **DOS VDU static sound**: `malf_noise(..., 0x17)` routes to the Saga-only `PlaySnowStaticSound`, so
   in DOS mode nothing plays. The DOS original presumably played OriginFX sound 23 (program 44, a
   rhythm-mode noise). Recommend playing sound 23 in the DOS backend. (The port is faithful to the
   reference for now: nothing plays.)
5. **`FlushSoundEffectsAndLog(handle, …)`** stops *all* sounds in the Saga build; the DOS original
   likely stopped one handle (damage alarm, afterburner, firework). Consider giving the port's SFX
   engine per-handle stop (the engine already has tags) to avoid cutting unrelated effects.
6. **Tag -1 collision rule** (one player/UI sound at a time) is an SDL-port design decision modelled
   on the DOS "tag" behaviour comment in `originfx.cpp`; confirm against the DOS binary whether
   `tag` really was the object index and whether lasers used two slots.
7. **Resampling**: the port's box-filter decimation from 49715 Hz to 22050 Hz is lossy; rendering the
   whole mix at 49715 Hz (or 44100 with a proper resampler) is cleaner but will not be bit-identical
   to the reference.
8. **Timbre section 2 and the Saga `MUSIC.MID`**: section 2 of `WINGLDR.TIM` (30 × 32 bytes) and the
   `SUPERTM.DRV`/`TM.DRV` targets are unidentified (MT-32 variants vs. Tandy/PC speaker). The Saga
   `MUSIC.MID` uses compression flag 0xE0 (raw) — the port handles both.
9. **Saga volume quirk**: `PlaySfxWaveFileByNumber` passes the eye distance (≤ 32000) as the ix
   volume, so far objects are louder than near ones (and objects within 10 units are silent). Keep
   for fidelity or fix?
10. **Pitch bend range source**: the bend uses the *channel program's* timbre `t[14]`, not the
    layered record's — fine for the shipped bank (only program 127/149 is linked and it has no
    bend), but note it when writing the synth.
11. **Priority argument**: all call sites pass `looping = 0`, which becomes SFX `priority = 0`; the
    stealing rule "victim.priority ≤ new priority" therefore always allows stealing the oldest
    effect. Whether the DOS original had meaningful priorities per sound is unknown.
12. **Pan direction** (added while porting): `SdlPlayGameSoundEffect` computes
    `pan = 64 - dot(normalize(source - eye), eyeRight) * 64 / 256`, so a source on the side the
    eye's right vector points to gets pan < 64, which `OriginFxCalculatePannedLevel` plays on the
    **left** chip. Either `aShipRightVector` points to screen-left in WC1's coordinate system or
    the SDL port's stereo image is mirrored; verify by ear in game.
13. **Half-frame event ties** (added while porting, §3.2): 103 events round differently with 64-bit
    and exact/80-bit arithmetic; the port follows the MSVC (64-bit) reference.
