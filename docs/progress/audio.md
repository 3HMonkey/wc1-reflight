# Progress: audio (WingCommander.Audio)

## Status

Done and verified 2026-10-07: the complete DOS audio path — OPL2 emulator (C# port of ymfm's
YM3812), OriginFX timbre bank / MIDI sequence parser / AdLib driver model / music sequencer /
sound-effect engine, the 22050 Hz stereo mixer with a lock-free command queue, and the
game-facing `MusicDirector` / `SoundEffectManager` with small game-state interfaces. The Kilrathi
Saga WAV/stream (`ix`) path is future work.

Verification:

- **Bit-exact against the C++ reference.** The vendored ymfm sources and the SDL port's
  `src/sdl/originfx.cpp` were compiled unchanged with MSVC 19.51 (VS 18 Insiders) into a small
  harness (kept outside the repo, see "Reference harness" below) and compared with the C# port:
  - YM3812: two random register scripts (every register except the timer counters, so rhythm
    mode, LFO depths, waveforms, KSL, feedback, key-ons) — 2 x 256000 samples identical.
  - Music: all 41 MUSIC.MID sections rendered to the end in 1470-frame callbacks — PCM hashes,
    event lists (tick, frame, type, data) and end frames identical (1615 s of audio).
  - Sound effects: every sound 1..36 alone, plus a 600-command random script (invalid numbers,
    out-of-range volume/pan, tags, priorities, stop-all, gain 20000) — identical.
  - Mixer: a 50-callback scenario (music start/switch/stop, overlapping lasers, stop-all, glide,
    gain changes) emulating `SdlMixDosAdlibMusic` — identical.
  The golden hashes are pinned in the unit tests, so the tests keep enforcing bit-exactness.
- **240 xunit tests** (`tests/WingCommander.Audio.Tests`, ~25 s in Debug), all passing; data tests
  use `[DataFact]/[DataTheory]`. They cover the spec list (41 sections parse, 79 timbres, section 19
  cues, register-script hashes, sound 20 glide, every section renders non-silent, fixed mixer
  scenario) plus the director logic with fakes, the lock-free queue under concurrent producers,
  and allocation-free rendering.
- **wc1tool**: rendered sections 0, 19, 20, 23, 30, 32, 35 and all 36 effects to WAV
  (scratchpad `audio-out`, not in the repo) and checked them with an independent Python reader:
  durations match the spec table (e.g. 63.43 s, 25.18 s, 164.56 s), music peaks 5k..16k, RMS
  -16..-33 dBFS, effects peaks 0.6k..4.3k, **no sample at full scale anywhere**; centred effects
  have L == R, panned ones are attenuated on the other side.
- `Render` is allocation-free (measured with `GC.GetAllocatedBytesForCurrentThread`, Debug and
  Release), lock-free and guarded (an unexpected exception is stored in `Fault`, output is silence).
- Zero warnings with the AOT/trim analyzers; no unsafe code, no reflection.

## Integration (for the Game project)

```csharp
var audio = DosAudioBackend.Create(gameDirectory);   // MUSIC.MID + WINGLDR.TIM; audio.PreloadAllTracks() optional
host.StartAudio(audio.Mixer);                        // IAudioSource, 22050 Hz stereo s16
var volumes = new AudioVolumeSettings();
volumes.Load(Path.Combine(host.UserDataDirectory, AudioVolumeSettings.DefaultFileName));
var music = new MusicDirector(audio, volumes, sharedCRandom) { MusicPlaybackMode = modeFromCommandLine };
var sfx = new SoundEffectManager(audio, flightSoundWorld);

// every event pump (C: SdlPumpEvents -> SdlServiceOriginFxMusic):   music.Service(virtualNow);
// once per simulation frame (C: Update_3Space -> servicetrack):    music.ServiceTrack(flightState, flightSoundWorld, sfx, nSpaceFrame);
// sounds (C: PlaySfxWaveFileByNumber):                             sfx.PlaySfx(number, sourceObject, 0);
// DOS intro: int previous = music.BeginStartupIntroMusic(now); poll music.SequencePosition each tick; music.EndStartupIntroMusic(previous, now);
```

All game-facing members are non-blocking and cheap per tick (ADR-009). The original busy wait
`wait_for_end_of_music` is `BeginWaitForEndOfMusic()` + `ContinueWaitForEndOfMusic(escape)` per
tick. Game implements `IFlightMusicState` (mission/threat queries) and `IFlightSoundWorld`
(object class/distance/screen position plus two vector computations specified in the interface
docs) on top of the simulation.

## Mapping

### ymfm (`third_party/ymfm`, C++) -> `Opl/`

| C++ | C# | Status |
| --- | --- | --- |
| `bitfield`, `clamp`, `roundtrip_fp` (ymfm.h) | `YmfmTables.Bitfield/Clamp/RoundtripFp` | done |
| `abs_sin_attenuation`, `attenuation_to_volume`, `attenuation_increment` (ymfm_fm.ipp) | `YmfmTables` (tables verbatim) | done |
| `opl_key_scale_atten`, `fm_registers_base::effective_rate` | `YmfmTables.OplKeyScaleAtten/EffectiveRate` | done |
| `opl_registers_base<2>`: waveforms, `reset`, `write`, `clock_noise_and_lfo`, `cache_operator_data`, `compute_phase_step`, `operator_map`, offsets, register accessors | `OplRegisters`, `YmfmTables.Waveforms` | done |
| `fm_operator`: `reset`, `prepare`, `clock`, `compute_volume`, `keyonoff`, `start_attack/release`, `clock_keystate`, `clock_envelope`, `clock_phase`, `envelope_attenuation` | `FmOperator` | done |
| `fm_channel`: `assign`, `reset`, `keyonoff`, `prepare`, `clock`, `output_2op`, `output_rhythm_ch6/7/8` | `FmChannel` | done |
| `fm_engine_base<opl2>`: ctor, `reset`, `clock`, `output`, `write`, `status`, `set_reset_status`, `assign_operators`, `update_timer`, `engine_check_interrupts`, `engine_mode_write`, `sample_rate` | `OplFmEngine` | done |
| `ym3812`: `reset`, `read_status`, `read`, `write_address`, `write_data`, `write`, `generate`, `sample_rate` | `Ym3812 : IOplChip` | done |
| `ymfm_interface` default callbacks | inlined in `OplFmEngine` (timers never fire, as in the reference) | done |
| `output_4op`, SSG-EG, noise channel, OPN/OPM tables, `save_restore`, `log_keyon`, ADPCM/PCM | not needed for OPL2 | n/a |

### OriginFX player (`src/sdl/originfx.cpp`) -> `OriginFx/`

| C | C# | Status |
| --- | --- | --- |
| `g_abOriginFx*` tables | `OriginFxTables`, `OriginFxSoundRecords` | done |
| `OriginFxReadVariableLength`, `OriginFxParseTrack`, `OriginFxAppendEvent`, `OriginFxCompareEvents`, `OriginFxLoadMidi` | `OriginFxSequence.Parse` (+ `OriginFxEvent`, meta events kept for inspection) | done |
| `OriginFxLoadTimbres`, `OriginFxFindTimbre`, `OriginFxNextTimbre` | `OriginFxTimbreBank.Parse/FindTimbre/NextTimbre`, `OriginFxTimbre` view | done |
| `OriginFxWriteRegister`, `OriginFxWriteStereoRegister`, `OriginFxResetOpl`, operator/voice/rhythm helpers, `OriginFxEnableRhythmMode` | `OriginFxSynth` (private) | done |
| `OriginFxArithmeticShiftRight`, `OriginFxClampTotalLevel`, `OriginFxCalculate{Velocity,Carrier,Panned}Level`, `OriginFxWriteVoiceLevels`, `OriginFxWriteVoiceFrequency`, `OriginFxProgramVoice`, `OriginFxChooseVoice` | `OriginFxSynth` (private) | done |
| `OriginFxStartTimbre`, `OriginFxNoteOn`, `OriginFxStopTimbre`, `OriginFxNoteOff`, `OriginFxAllNotesOff`, `OriginFxDisableRhythmModeIfIdle`, `OriginFxUpdateChannelVoices`, `OriginFxSetProgram` | `OriginFxSynth.NoteOn/NoteOff/AllNotesOff/DisableRhythmModeIfIdle/SetProgram` | done |
| `OriginFxService`, `OriginFxAdvanceService` | `OriginFxSynth.Service`, `AdvanceServiceClock/TryConsumeServiceTick` | done |
| `OriginFxMapPercussionNote`, `OriginFxApplyControlChange`, `OriginFxControlChange`, `OriginFxDispatchEvent` (channel part) | `OriginFxSynth.ProcessChannelMessage/ControlChange/PitchBend` | done |
| `OriginFxGenerateOutputSample`, `OriginFxScaleOutputSample`, `OriginFxMixOutputSample`, `OriginFxInitializePlayer` | `OriginFxSynth.GenerateOutputSample/ScaleOutputSample/MixOutputSample`, constructor | done |
| `OriginFxProcessDueEvents`, `OriginFxDispatchEvent` (cue part), `SdlCreateOriginFxPlayer`, `SdlRenderOriginFxPlayer`, `SdlMixOriginFxPlayer`, `SdlOriginFxPlayerFinished`, `SdlOriginFxPlayerSequencePosition` | `OriginFxSequencer` (`Render`, `Mix`, `Finished`, `SequencePosition`) | done |
| `OriginFxStopSoundEffect`, `OriginFxStartSoundEffectRecord`, `OriginFxChooseSoundEffectChannel`, `OriginFxServiceSoundEffects`, `SdlCreateOriginFxSoundPlayer`, `SdlPlayOriginFxSoundEffect`, `SdlStopOriginFxSoundEffects`, `SdlMixOriginFxSoundEffects` | `OriginFxSoundEffectEngine` (`Play`, `StopAll`, `Mix`, `ServiceSoundEffects`, state snapshots) | done |
| `SdlDestroyOriginFxPlayer` | garbage collection | n/a |

### Host glue (`src/sdl/music.c`, `audio.c`, `dos_intro.c`) -> `Dos/`, `Director/`

| C | C# | Status |
| --- | --- | --- |
| `SdlMixDosAdlibMusic` | `DosAudioMixer.Render` | done |
| mutex around player access | `AudioCommandQueue` (lock-free, Vyukov bounded queue) | done (deviation 3) |
| `SdlInitializeOriginFxAudio(1)`, `SdlLoadDosMusicFile` | `DosAudioBackend.Create` + `IGameHost.StartAudio(backend.Mixer)` | done |
| `SdlServiceOriginFxMusic` | `MusicDirector.Service` + `DosAudioBackend.Update/TryStartTrack/StopTrack` | done |
| `SdlDeleteDosAdlibTrack`, `SdlCalculateDosAudioGain`, `SdlUpdateDosAdlibMusicVolume` | `DosAudioBackend.StopTrack/CalculateGain/ApplyVolumeSettings` | done |
| `SdlPlayDosSoundEffect` (laser tags 64/65), `SdlStopDosSoundEffects` | `DosAudioBackend.Play/StopAll`, `SoundEffectManager.StopAllSounds` | done |
| `SdlPlayGameSoundEffect` (DOS branch: distance volume, pan) | `SoundEffectManager.PlaySfx` | done |
| `SdlPlayGameSoundEffect` (Saga branch), `SdlPlayWaveWithPan`, `SdlMixOriginFxMusic` (intro over ix) | - | todo (Saga) |
| `SdlGetOriginFxMusicSequencePosition` | `MusicDirector.SequencePosition` (virtual clock) | done |
| `SdlStartAudio`, `SdlStopAudio`, `SdlAudioCallback` (audio.c) | host (`IGameHost.StartAudio/StopAudio`, Host.Sdl) | n/a here |
| music part of `SdlPlayDosStartupIntro` (dos_intro.c) | `MusicDirector.BeginStartupIntroMusic/EndStartupIntroMusic`, `SequencePosition` | done |
| `SdlShutdownOriginFxAudio` | `host.StopAudio()` + GC | n/a |

### Game side (`music.c`, `sound.c`, `logic.c`, `cockpt.c`, `spc.c`, `main.c`, `hudmsg.c`, `gr.c`) -> `Director/`

| C (address, file) | C# | Status |
| --- | --- | --- |
| globals `nMusicPlaybackMode`, `bMusicCommandSuppressed`, `nMusicTrackComplete`, `nCurrentMusicTrack`, `nMusicStreamSet`, `nWaitForMusicEnabled`, `nInFlightMusicActive`, `nCombatMusicActive`, `nInitialFlightMusicPending`, `nPassingShipSound*` | `MusicDirector` properties | done |
| `spacetrack` (0x42E880), `ProcessMusicScriptCommand` (0x42E6F0) | `MusicDirector.SpaceTrack/ProcessMusicScriptCommand` | done (DOS; Saga streamer routing todo) |
| `SelectFlightMusicTrack` (0x42E3F0), `MapMusicTrackToStreamerCommand` (0x42E520) | `MusicDirector` stream-set bookkeeping, `GetStreamSet`, `MapMusicTrackToStreamerCommand` | partial (no streamer) |
| `StopMusic` (0x42E350), `StopMusicUnlessSuppressed` (0x42E8B0), `GetMusicMode` (0x42E8D0), `EnableMusicForScene` (0x42EEE0) | `MusicDirector` | done |
| `wait_for_end_of_music` (0x42E900) | `BeginWaitForEndOfMusic` + `ContinueWaitForEndOfMusic` | done (split, ADR-009) |
| `FadeMusic`, `SetMusicOn`, `SetMusBreakpt` (stubs), `SetMusicStreamVolume` (0x442590, gr.c) | `MusicDirector` no-ops | done |
| `PreloadMusicTrackHook` (0x424CE0), `ReleaseMusicTrackHook` (0x424CF0) (stubs) | `MusicDirector.PreloadMusicTrackHook` (parses ahead) / `ReleaseMusicTrackHook` | done |
| `new_space_music_changes` (0x42E9E0), `changetrack` (0x42EAD0), `gametrack` (0x42EB60), `servicetrack` (0x42ECB0) | `MusicDirector.NewSpaceMusicChanges/ChangeTrack/GameTrack/ServiceTrack` | done |
| `init_inflight_music` (0x424C60), `free_inflight_music` (0x424C80) | `MusicDirector.InitInflightMusic/FreeInflightMusic` | done (legacy slot bookkeeping is never armed: not ported) |
| `PlaySfxWaveFileByNumber` (0x42EF30) | `SoundEffectManager.PlaySfx` | done (DOS) |
| `stop_all_sounds` (0x42B640), `FlushSoundEffect` (0x42E3A0), `FlushSoundEffects` (0x42E3C0), `FlushSoundEffectsAndLog` (0x42EF10) | `SoundEffectManager.StopAllSounds/Flush*` | done |
| `ResetSoundState` (0x42EE80), `...ForScene` (0x42EEA0), `...ForFlight` (0x42EEB0) | `SoundEffectManager.ResetSoundState*` | done |
| `PlaySnowStaticSound` (0x42B680), `ServiceSoundSystem` (0x42B7D0), `SetSoundEffectsVolume` (0x42B7E0) | `SoundEffectManager` (DOS: no-ops) | done |
| `SoundFxTick` (0x42EF00), `sound_effect` (0x42EF20) | `SoundEffectManager.SoundFxTick` stub; `sound_effect` has no callers | done / n/a |
| `aiSoundEffectSourceActive`, `bAfterburnerSfxActive`, `nAfterburnerSoundDeadline`, `nDamageAlarmSfxHandle`, `nFlightSoundEffectsEnabled` | `SoundEffectManager` state | done |
| sound part of `your_afterburner` (0x421920, logic.c) | `SoundEffectManager.ServiceAfterburnerSound` | done |
| afterburner expiry in `accelerate_and_move_object` (0x4129A0, spc.c) | `SoundEffectManager.OnAfterburnerExpired` | done |
| damage alarm in `update_lights` (0x4145B0, cockpt.c) and `house_keep` (0x427D40, main.c) | `SoundEffectManager.ServiceDamageAlarm/ReleaseDamageAlarm` | done |
| `PlayCockpitSelectionSfx` (0x417F00, cockpt.c) | `SoundEffectManager.PlayCockpitSelectionSfx` | done |
| `nSfxVolumeSetting`, `nMusicVolumeSetting`, `anVolumeLevels`, `LoadVolumeSettingsFromRegistry` (0x42B870), `SaveVolumeSettingsToRegistry` (0x42B930), volume hotkeys (main.c, hudmsg.c) | `AudioVolumeSettings` (`Load/Save/ApplyText/ToText`, `Step*`, `Toggle*`) | done (key handling and the on-screen message belong to Game) |
| `playWAVE`, `ReleaseFinishedSoundEntries`, `StopSoundsUsingWave` (sound.c), `Streamer_*`, `UpdateStreamerStoppedFlag` (gr.c), `src/ix/*` | - | todo (Saga, future) |

### Belongs to Game/Graphics (non-audio code in the audio source files)

- `music.c`: `parse_view_script`, `update_scripted_view`, `initialize_scripted_view` (scripted
  camera, Game/Simulation); `InitializeFireworks`, `TheEndFireWorks` (Game/Graphics; calls the
  `SoundFxTick` stub and `FlushSoundEffectsAndLog`); `InitializeConstellationField`,
  `DrawConstellationField` (Graphics); `show_target_disp`, `DrawTargetRangeReadout`,
  `GetTargetColourIndex` (cockpit, Game); `LogDisplayMode`, `CalcRectangleArea`,
  `AllocateViewport`, `AlignSpriteFrameToRectCorner`, `PaletteFadeHook` (Graphics);
  `SceneLeaveHook` (Game, empty); `OpenPacketSection`, `CloseDataFileByHandle`,
  `DecompressPacketSection` (Core resources, already covered by `PacketFile`).
- `sound.c`: `DrawLaunchDoorFrame`, `LaunchPlayerShip`, `ShowCarrierLaunchSequence` (scenes,
  Game; they call `spacetrack`/`PlaySfx`); `InitializeDiskPromptTextContext` (Graphics/Game);
  `RewriteDiskFileGraphicsExtensions`, `LoadWingCmdrCfgFile`, `LoadInstallDat` (Core/Game);
  `show_damage_disp`, `UpdateDamageDisplay` (cockpit, Game); `GetJoystickPresentUnused` (Game/Host);
  `RegistryQueryValue`, `RegistryStoreValue`, `FxDriverShutdownHook` (Win32/driver stubs, n/a).
- `dos_intro.c`: everything except the music calls (orchestra, logo, fireworks drawing: Game/Graphics).
- `logic.c` `your_afterburner` (fuel, malfunction, `fire_afterburner`), `cockpt.c` `update_lights`
  (warning lights), `malf_noise` (calls `PlaySnowStaticSound`/`PlaySfx`): Game.

## Deviations

1. **Game-visible music state follows the virtual clock (ADR-009).** The reference reads
   `finished` and the cue position back from the audio thread. The port derives them from the
   virtual time since the track was started (`played frames = (now - start) x 22050`, evaluated
   with the same end frame and cue frames the audio thread renders; `OriginFxSequence.IsFinishedAfter`
   / `GetSequencePositionAfter`), so flight-music restarts, the funeral end and the intro sync are
   deterministic and work without an audio device. Audio and game clock agree up to device
   buffering; a game pause that stops the virtual clock would need a mixer pause (not implemented).
2. **No mutex.** Effects, stop-all, music swaps and gains go through a bounded lock-free queue and
   are applied at the start of the next `Render` (host chunk granularity instead of the
   reference's 1470-frame callback). A full queue (no audio thread running) drops commands and
   counts them (`DroppedCommandCount`).
3. **Play result.** `DosAudioBackend.Play` returns true when a valid effect was queued; the
   engine's own accept/reject happens later on the audio thread. With the priority 0 that every
   call site passes the engine can only reject invalid numbers, which are checked up front, so
   the game-side bookkeeping (`aiSoundEffectSourceActive`, afterburner flag) is unchanged.
4. **Music player uses one chip.** The reference also writes a right chip for music but never
   renders it; the port omits it (identical output, half the register writes).
5. **Parsed sequences are cached** (immutable, shared); each start creates a fresh player like the
   reference. `PreloadMusicTrackHook` / `PreloadAllTracks` parse ahead so no tick has to.
6. **`FindTimbre(channel program)` is cached per channel** (the reference searches the bank on
   every frequency write); same results.
7. **`wait_for_end_of_music`** is split into begin/continue steps (no busy wait).
8. **Volume persistence** uses a text file (`MusicVolume=`/`SFXVolume=` lines) instead of the
   HKLM registry key; loaded values are clamped to 0..20 (the C code would index out of range).
9. **`SelectFlightMusicTrack(19)`** reads an uninitialised local in the reconstruction; the port
   treats track 19 as "no stream" (only matters for the Saga streamer and the simulator check).
10. **`Render` is guarded**: an unexpected exception is stored in `DosAudioMixer.Fault` and the
    mixer outputs silence instead of crashing the audio thread.
11. **Spec class design** (analysis §7.2) adapted: one `DosAudioBackend` implements both backend
    interfaces; the folder is `Director/` (not `Game/`, to avoid confusion with the Game
    project); no audio device class in Audio (the host owns it).
12. Non-byte constant tables used on the audio thread are static arrays, not span literals (span
    literals of `ushort`/`int` allocated in Debug builds).

## Open questions / TODO

- Saga path (future): WAV sound effects through an `ix`-style voice mixer, `.STR` streamer with
  intensity/trigger branching (analysis §6). Not needed for the GOG DOS data.
- The DOS-original behaviours the reference does not reproduce stay open (analysis §8): music
  loops/`SetMusBreakpt`, meaning of `FE 00/01/02/05/FF`, VDU static sound 23 (the port plays
  nothing, like the reference), per-handle `FlushSoundEffectsAndLog`, tag/priority semantics,
  the inline firework sound descriptor (`SoundFxTick` is a stub).
- **Pan direction** (analysis §8.12): a source on the eye's right vector plays on the left chip;
  verify by ear in game whether WC1's right vector points to screen-left.
- **Half-frame ties** (analysis §3.2): 103 events round down with 64-bit accumulation (MSVC, the
  port) and up with exact arithmetic; an 80-bit GCC build of the reference could differ by one
  frame there. Inaudible; the tests pin the current behaviour.
- Program 139 (bongo pseudo-channel) is missing from WINGLDR.TIM; those notes play with the first
  record, as in the reference.

## Requests

- **Game:** implement `IFlightMusicState` and `IFlightSoundWorld`, call `MusicDirector.Service(now)`
  from the event pump with the scheduler's virtual time and `ServiceTrack(...)` once per
  simulation frame; volume hotkeys use `AudioVolumeSettings` (+ `Save`) and show the message.
- **Packaging/docs owner:** BSD-3 binary redistribution requires shipping the ymfm licence text
  with `wc1`/`wc1tool` binaries: please include `src/WingCommander.Audio/Opl/LICENSE-ymfm.txt`
  in the publish output or a third-party notices file (I did not touch other csproj files).
- **Core:** nothing required. (If a deterministic test wants PCM it can call `DosAudioMixer.Render`
  directly; game logic no longer depends on audio being rendered.)

## Reference harness (how bit-exactness was checked)

Kept in the session scratchpad (`audio-harness/`), not in the repo, because it compiles
third-party C++. To recreate: copy `third_party/ymfm/*.{h,ipp,cpp}` and `src/sdl/originfx.cpp`
from the reference into a folder, add a stub `wc1sdl.h` that only declares the
`SdlOriginFxPlayer` functions, and a `harness.cpp` that `#include`s `originfx.cpp` and provides
`opl` (random register script), `music` (render a decoded section in 1470-frame blocks), `sfx`
(scripted effects) and `mix` (`SdlMixDosAdlibMusic` emulation) modes printing 64-bit FNV-1a hashes
of the PCM. Build with `vcvars64.bat` (needs `C:\Program Files (x86)\Microsoft Visual Studio\Installer`
on PATH). Note: in this environment cl.exe sporadically exits with code 1 without output; retry,
and compile `ymfm_opl.cpp` with `/Od` (integer code, same results). MSVC's `long double` is 64-bit,
which is what the port reproduces (see the half-frame ties above).

wc1tool commands added: `music <section> --out <wav> [--seconds N]`, `sfx <number> --out <wav>
[--seconds N]`, `timbres`, `sequence <section> [--events]`.
