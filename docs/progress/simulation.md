# Progress: simulation (WingCommander.Simulation)

## Status

**Phase 1 (foundations) done 2026-10-07.** Static data, exact fixed-point vector/orientation
math, the 64-slot object model with slot allocation and `set_objects_data`, ship speed and
rotation-goal control, weapon loadouts and the player's weapon selection, the MODULE.00x
mission parser and the complete mission setup (`init_mission` → `prepare_mission` →
`set_up_action_sphere` → nav sphere switches and follow-up waves, objectives/flight path,
ship shape slots, constellation, hazard field registration). The data tests parse every
mission of MODULE.000/.001/.002 (44 + 22 + 22) with plausibility checks, set up every mission
through `InitMission` and walk all of its active nav spheres, and check hand-decoded golden
values (Enyo "Alpha Wing"). Mission setup consumes no random numbers (asserted). The geometry
was cross-checked against the reference C compiled with clang (18,363 identical output lines).

**Phase 2 (the flight model) done 2026-10-07.** The per-frame loop (`Update_3Space`,
`house_keep`, `house_keep_objects`, `update_objects_in_space`, movement, rotation, animation),
collisions and forces, damage/internal damage/explosions/debris/shock waves/scoring, all
weapons (guns, missiles with their heat-seeking/friend-or-foe/image-recognition brains, mines,
NPC fire decision, capital ship turrets and flak), afterburner/super brake, energy/shield/fuel
housekeeping, the player's controls, target selection and missile lock, hazard fields
(asteroids and mines around the player), the camera (16 views, scripted views of the
cinematics), the star field and dust, the original sprite projection (`transform_objects_to_your_view`,
`get_right_shape`, engine flames, child placement, depth sort, nav pointer) and a
renderer-facing snapshot (`SpaceViewSnapshot`). Both NotImplementedException stubs are gone.
At the end of phase 2 the AI and the in-flight objective tracking were no-op hooks; phase 3
replaced them.

**Verification of phase 2 (346 tests, 0 warnings):**
- Determinism: six real missions (Vega series 1, 3 and 7, Secret Missions 1 and 2) flown 600
  frames with a scripted pilot (turns, throttle, guns, afterburner, missiles), and two combat
  missions flown 3000 frames by a test pilot that hunts the nearest enemy (kills, debris,
  shock waves, scoring): the same seed gives identical state hashes (`ComputeStateHash`) after
  every frame, another seed a different final state. Invariants are checked every frame
  (valid classes, ships only in slots 1..9, orthonormal frames, shields ≤ maxima, rotation
  rates within ±30, throttle within the maximum, stars 15000 out, draw list sorted).
- Targeted tests: shield/armor quadrants, internal damage events (rated/unrated, player by
  attacker class, capital ships), named-pilot survival rolls and the ace escape, kill scoring by
  type, shock wave fall-off, refire delays (laser 6, neutron 10, mass driver 4, NPC 12),
  energy and shield recharge, afterburner thrust and fuel, the NPC fire decision, capital ship
  turrets and flak, missile launch, dumb-fire lead point, heat-seeker homing until impact,
  heat-seeker target choice by exhaust heat,
  friend-or-foe acquisition, image recognition steering, mines (armed hazard mines, timed
  player mines), bolt hits (damage by age, push, spark), ship-ship bounce, collision damage
  speed²/2, ramming a capital ship, missile contact explosions, invisible hazards, torque,
  missile lock (heat seeker 18 frames, image recognition 32, loss off-centre/when the target
  turns, damaged tracker), automatic targeting and cycling, cockpit/side/rear/chase cameras,
  projection (centre, focal length, scale, culling, view cone), sprite view selection,
  capital ship per-view shapes, draw order, frame skip, engine flames from an exhaust table,
  nav pointer slot, view scripts, hazard spawning/recycling, the `align` quirk, asteroid
  shattering, the snapshot and the state hash.
- Cross-check against the reference C (scratch harness, not committed: it contains copies of
  reference code): `initialize_direction_view_frames`, `transform_objects_to_your_view` with
  `get_right_shape`/`set_background_objects_rotation` (6,000 random cases over all classes,
  screen widths and eye frames), `apply_force_to_object`/`rotational_acceleration`/
  `apply_force_to_objects_center` (3,000), the ship branch of `object_collision` (3,000 ship and
  capital ship pairs), `accelerate_and_move_object` (3,000 incl. afterburner and super brake),
  `flip_angle` + `reposition_fixed_child_objects` (3,000), `sort_object_depth` (1,000) and the
  shock-wave damage formula (3,000): all 22,062 output lines are identical to the C# port when
  the C `DivideFixed` divides at x87 53-bit precision like the MSVC original (see Open question 7).
- `wc1tool flight <series> <mission> [frames] [seed] [--campaign N] [--aim]` flies a mission
  headless and prints the flight (e.g. McAuliffe Beta wing with `--aim`: 3 Dralthi kills, 30
  points).

**Phase 3 (AI) done 2026-10-07.** The complete NPC AI and the in-flight mission logic: the AI
helpers of logic.c (crash prediction, scans, tails, squads and formation burst, follow points,
`engage`, `triumph`, `evaluate_damage`), smart.c (steering away and collision avoidance, the AI
tick regulator, target selection, formation flying, stress and morale, maneuver choice from the
rated, Kilrathi and generic tables, the dogfight tick `intelligence_events`), the 47 maneuver
handlers of brains.c dispatched by number (`perform_maneuver`), every mission handler (patrol,
escort, strike, defend, Confed and Kilrathi wingmen with formation burst and break, rout, jump
out, jump in, rendezvous, come home, stragglers) and the capital ship AI (tankers, destroyers and
cruisers, the Kilrathi starbase, Confed capitals), the hyperspace jumps (`warp`, `unwarp`,
`arrive_from_warp`), objective tracking in flight (`check_objectives` with sightings, visits,
"objective reached" and the escort wait), the comm orders (`request` and its helpers, landing
clearance) and the autopilot computation, split around the UI's cinematic. The phase-2 hooks are
replaced and `SpaceSimulation.Pending.cs` is deleted. Additions for the UI are listed under
"Interface changes for Game"; the flight-UI corrections to docs/analysis/simulation.md (§3.6 view
1 = right / 2 = left, §5.3 logical files) are applied and the camera test checks the eye's forward
vector of views 1..3.

**Verification of phase 3 (417 tests, 0 warnings, wc1tool builds):**
- Differential cross-check against the reference C (scratch harness, not committed because it
  contains copies of reference code): the 32 gameplay units of `reference/wc1-re/src` compiled
  with clang and the C# port run the same 1,000 random scenarios of 1,000 frames each. A scenario
  is 2..7 random fighters and capital ships of both sides with every mission type, generic and
  named pilot levels, wing leaders, formation offsets and mission targets, and the player at a
  random speed and heading; every frame runs `Update_3Space`, the cockpit simulation
  (`check_target`, `repair_internal_damage`, `update_objective_location`, `check_stranded`),
  every 7th frame `check_objectives`, every 50th a `request` with a random command from −1 to 12,
  and at frames 200 and 700 the autopilot (36 completed trips in the first 100 scenarios). Both
  sides dump the full state every frame (all fields of the 64 objects and 10 ships, the AI scratch
  globals, objectives, flight path, mission records, random seed): all 17,786,104 lines are
  identical. The generator leaves out three cases that read outside the tables in the original and
  are guarded in the port (an Imperial wingman without a leader, a Kilrathi COME_HOME/GOTO_WARP
  target outside the nav table, mission type NONE for a fighter); a scan of the shipped missions
  found none of them. Two findings changed only the C side: index −1 reads must use the Kilrathi
  Saga memory layout (Deviations 13; clang lays the globals out differently), and float-to-int
  conversions must wrap like MSVC's `_ftol` (next point).
- `_ftol`: the KS build converts with a 64-bit `FISTP` and keeps the low 32 bits, so an
  out-of-range value wraps; x64 code returns `INT_MIN`. One AI scenario of 500 overflowed; Core's
  `FixedMath.Truncate` (`(int)(long)value`) already behaves like `_ftol` (analysis §1.2 clarified).
- Harness recipe (to rebuild it): clang `-DSDL_PORT=1` with a stub `SDL.h`; the presentation
  functions (music.c sound and music, gr.c palette/viewport/shape bounds, the cockpit messages and
  VDU calls of cockpt.c, `initialize_cockpit`, `visit_the_cinema`) renamed to harness no-ops that
  match `NullSimulationEvents`; `rand` in mathfp.c routed through the harness; `DivideFixed` with
  a double division (open question 7); `return (long)(long long)(...)` for `_ftol`; the index −1
  reads routed through a KS-layout emulation; the `remove_weapon` guard; stubs generated for the
  ~100 unresolved host symbols; one process per scenario on the C side.
- Unit tests (`tests/WingCommander.Simulation.Tests/Ai`, 71): crash time and collision detection,
  `unactive`, scans, attackers and danger, tails, missiles on the tail, health, squad burst,
  `engage` and the ace greeting, follow points over the −1 terminator, object −1 reads, weighted
  choice; the turn regulator, a Kilrathi patrol (scan, burst, engage), the player's wingman
  (permission request, Hunter engaging on his own after 40 ticks, "enemy sighted" once per wave and
  only with a free message line), Confed capital self-defence, the Kilrathi starbase, rout, strike
  (approach, attack, missing goal), escort, a Kilrathi jump-out; stress and morale, panic,
  `any_defense` drawing the terminator, the rated tables, hard brake, tight loop, roll over,
  dispatch by number, too close → veer away, no target → reroll draw, chill, the head-on event,
  dying and vanished targets; objective tracking (nav point reached → next destination, already
  visited, waiting for the escort, 6000 for escort and destroy objectives, lost objectives, the −1
  record, `FindObjective`), comm orders (attack my target, help me out, return to base by pilot,
  taunts, break and attack, Paladin's disobedience, radio silence, landing clearance, unknown
  commands and recipients), the autopilot (refusals, travel with the team, formation behind the
  player), jumps out and in.
- Determinism with the AI active: the phase-2 determinism tests (six missions with a scripted
  pilot, two combat missions with the hunting test pilot) now fly with the AI; four more missions
  (McAuliffe Beta wing, Brimstone Theta wing, Port Hedland Sigma wing with capital ships and an
  ace, Secret Missions Midgard Delta wing) are flown 2,500 frames twice with the same state hash
  after every frame and must show an engagement and dogfight maneuvers. The Tiger's Claw stays
  within 50 units over 600 frames with the AI flying around it (open question 8). `MissileTests` makes its NPCs inert (mission type CANNED_SEQUENCE) so the
  missile physics stay isolated. `ComputeStateHash` now also covers the AI scratch globals, the
  objective records and flight path, the comm and landing flags and the autopilot state.
- `wc1tool flight 2 0 3000 7 --aim` (McAuliffe, Beta wing, AI active): the Dralthi attack the
  player; one kill, 10 points.

## Code layout

| Folder / file | Content |
| --- | --- |
| `Data/` | Enums (`ObjectClass`, `ObjectType`, `Side`, `Rating`, `ShipMissionType`, `ShipObjective`, `ShipTactic`, `ShipManeuver`, `SpecialManeuver`, `CommCommand`), `ObjectTypeTable` (the 58 compiled-in records, transcribed in C initializer order) + `ObjectTypeData`, `AnimationScripts` (anAnim*), `GeometryTables` (child/hardpoint offsets, formation slots, direction sprite frame/flip tables), `AiTables` (rated/Kilrathi maneuver choices, defense maneuvers, aggression, recovery, turn intervals, reroll chances), `DamageTables` (player damage systems, gun refire delays, debris sets), `IntroMissionData` (nav points 16..19, mission records 32..45, canned sequences), `ShortVector`, `ManeuverChoice` |
| `Geometry/` | `FixedTrig` (exact SinFixed/CosFixed), `VectorMath` (vector algebra, rotations about i/j/k, shrink_vector, spherical conversion, ranges), `ScalarMath` (WrapDegrees, Min/MaxShort, find_ratio, rate clamps, percent cosine), `RandomVectors` (CRandom-based vector helpers), `SphericalVector` |
| `Objects/` | `SpaceObject` (all 64-slot arrays + basis operations init_ijk/fix_objects_ijk/alter_*), `ShipState` (all ship arrays, slots 0..9), `WeaponLoadout` (byte-exact 0x47 record, `[InlineArray]`), `ShieldValues`, `ArmorValues`, `ShapeRef`, `ObjectTypeResources`, `ObjectResourceSlot`, `CannedSequenceCursor`, `ExhaustTable` (engine flame table, section 2 of a ship file), `ObjectSlots` (slot layout constants) |
| `Missions/` | `MissionModule` (MODULE.00x parser), `MissionData`, `MissionHeader`, `MissionNavPoint`, `MissionShipRecord`, `MissionObjectiveSource`, `MissionObjective`, `HazardField`, `NavTriggers`/`NavShipList`/`NavPreloadTypes`, `ConstellationObjectDefinition` (CAMP section 0 parser) |
| root, phase 1 | `SpaceSimulation` partials by C unit: `.Objects` (slots, set_objects_data, animate_shape), `.Frames` (frames, facing, steering goals), `.Motion` (speed/rotation goals, ship state helpers), `.Weapons` (loadout, player weapon selection), `.Ai` (AI setters, waves, canned sequences), `.Mission` (loading and setup), `.Objectives`, `.Resources` (shape slots, exhaust tables, 3-space init) |
| root, phase 2 | `.Update` (Update_3Space, house_keep(_objects), update_objects_in_space, movement, animation, missile/mine/futurion brains, canned sequence driver), `.Collisions` (check_for_collision, forces, object_collision), `.Damage` (ship.c damage/kills/explosions/scoring, component damage, send_message, any_enemy), `.Firing` (fire_weapon, fire, turrets/flak, missiles, mines, afterburner, energy/shields/fuel), `.TargetLock` (lock, check_target, target lists), `.Hazards` (hazard fields, easy2see, shards), `.Camera` (views, scripted views, dust and stars), `.Projection` (projection, get_right_shape, nav pointer, flames, child placement, depth sort), `.Flight` (player controls, PrepareSpaceView, UpdateCockpitSimulation, CheckStranded), `.Snapshot` (CaptureSpaceView, ComputeStateHash), `.FlightState` (frame skip, view geometry, camera and lock state) |
| root, phase 3 | `.AiHelpers` (logic.c AI helpers: crash prediction, scans, tails, squads, follow points, engage, triumph, evaluate_damage), `.Smart` (smart.c: steering away, collision avoidance, AI tick regulator, target selection, formation flying, stress, maneuver choice, the dogfight tick), `.Maneuvers` (brains.c maneuver handlers, `PerformManeuver` with the dispatch by number), `.Brains` (brains.c mission handlers, `ShipIntelligence`, the capital ship AI), `.Warp` (hudmsg.c `FindObjective`, `ArriveFromWarp`, `Warp`, `Unwarp`), `.ObjectiveTracking` (cockpt.c `CheckObjectives` and its helpers), `.Comm` (screen.c `Request`, landing clearance, comm queries), `.Autopilot` (auto.c and `AutoPilotValid`: the autopilot split) |
| root, interfaces | `ISimulationEvents` + `NullSimulationEvents`, `SimulationHudMessage`, `SimulationCockpitMessage`, `ICockpitState` + `DefaultCockpitState`, `IShapeBounds` + `PointShapeBounds`, `ISimulationResources` + `GameDirectoryResources`, `ICampaignState` + `SimulationCampaignState`, `SpaceViewSnapshot` + `SpaceObjectView` |
| `src/WingCommander.Tools/SimulationCommands.cs` | `wc1tool missions [MODULE.00x] [--series N]`, `wc1tool shiptypes`, `wc1tool flight <series> <mission> [frames] [seed] [--campaign N] [--aim]` |

### How to use (Game)

- `var sim = new SpaceSimulation(random, new GameDirectoryResources(dir), events, campaign)`;
  the `CRandom` is the game-wide instance. Set `Cockpit` (`ICockpitState`: the VDU modes) and
  `ShapeBounds` (`IShapeBounds` on the space view buffer with Graphics' `ShapeBounds`), then
  `CampaignDataSet` (0/1/2), `TrainSimActive`, `ConstellationDefinitions` (CAMP.xxx section 0),
  `CockpitView` (0..3 cockpit set, 4 training simulator), `CockpitlessView`, `FrameSkip`.
- Mission: `InitMission(series, mission)`; launch (`LaunchPlayerShip`): `ForceView(0, 0)` and
  frames of `Update3Space` + `PrepareSpaceView` with `CannedSceneMode = 1`; flight start
  (`RunSpaceFlight`): `FrameSkipCounter = 1`, `SetUpActionSphere(entryNav)`, `ArcadeState = 0`.
- One flight frame (20 Hz, nothing blocks, everything synchronous):
  1. Game input → `PlayersFlightDynamics(pitch, yaw, roll)` (the original's −8..8 input
     units), then the key actions in the original order: `Accelerate(n)`, `ZeroPlayerSpeed()`,
     `YourAfterburner()`, `FirePlayersLasers()`, `PlayerReleaseWeapon()` (key 0x1c incl. the
     missile camera), `ToggleTargetLockMode()` (+ Game: target VDU refresh), `TryEject()`,
     `NewView(view, obj)` / `ForceView(view, obj)`; VDU keys are Game code that calls `Malf(n)`.
  2. `Update3Space()` — stops the frame if `ArcadeState != 0` afterwards (flight ended).
  3. Game `UpdateSpacePaletteFade`, then `if (PrepareSpaceView())` the Game draws
     `SortedObjects` (or `CaptureSpaceView(snapshot)` for a modern renderer) and the HUD.
  4. `UpdateCockpitSimulation()` (`check_target`, `repair_internal_damage`,
     `update_objective_location`), then in camera view 0 the Game's cockpit-view block (lights,
     missile warning, scanner, readouts, VDUs — the navigation VDU calls `CheckObjectives()` every
     tick and draws "calculating" itself —, pilot, cockpit explosion, then `npc_communication`,
     which reads and clears `Ships[].WingmanMessageState` and draws from the shared `CRandom`),
     `fire_computer_graphic_missile` (Game), `CheckStranded()`.
- Presentation callbacks arrive synchronously through `ISimulationEvents` at the original
  statement: sounds, `ServiceTrack` (once per frame) and `NewSpaceMusicChanges` (kills) for
  the music director (it consumes randoms), `HouseKeepCockpit` (palette fades / damage alarm),
  `AfterburnerExpired` / `PlayerAfterburnerEngaged`, `TriggerPlayerHitPaletteFlash`,
  `FlashCockpitPaletteEntry`, `PlaceDamageOnCockpit`, `ShowComponentHitHudMessage`,
  `VduMalfunction`, `SelectCockpitVduMode` (fire key; the handler calls `sim.Malf(3/4)`),
  `ShowMissileLockedMessage` / `RemoveMissileLockedMessage`, `PlayerReleaseWeaponLaunched`,
  `InitializeCockpitView` (must set `ScreenWidth`/`ScreenHeight`/`ViewCenterX/Y` from the
  viewport geometry before returning: the projection uses them in the same call).
- Phase 3 for the UI: comm menu choices and the keys H/B → `Request(0, ship, command)` (any value,
  including −1); key A → the autopilot split (see "Interface changes for Game" and the comment at
  the top of `SpaceSimulation.Autopilot.cs`); cockpit light 4 → `AutoPilotValid(false)`; after the
  flight with `ArcadeState == 1` → `FlagObjective(FindObjective(1, -1), 2)`. New callbacks:
  `SpaceBufferFlash` (hyperspace flash), `ShowCockpitMessage` (autopilot refusals, objective
  messages), `ResetSoundState` (autopilot start); `ICockpitState.MessageShowing()` must report the
  HUD message timer (the wingman's "enemy sighted" waits for a free line).
- Renderer: `CaptureSpaceView(SpaceViewSnapshot)` after a prepared view (camera position,
  basis and near radius, view geometry, per object slot/type/class/owner, position, velocity,
  basis, scale, radius, `Shape`/`DrawShape` + view frame + flip + screen angle/scale, screen
  position relative to the view centre, distance, view position, side/special/exhaust heat,
  nav pointer flag; the draw order). Stars and planets have eye-relative "sky" positions.
  The original draw loop (`draw_sorted_objects_to_buffer`, Game) also stops at the first entry
  whose type is negative. `docs/analysis/rendering.md` §3.2 maps the fields to Core.Rendering.
- `ComputeStateHash()` (FNV-1a over all simulation state and the random seed) is the replay /
  save-state checksum.
- Per-slot state: `ref var o = ref sim.Objects[i]; ref var s = ref sim.Ships[i];` (ships only
  for 0..9). The original's scratch globals (`TargetRange`, `FacingToTarget`, `TargetFacing`,
  `ToTarget`, `NormalizedToTarget`, `CollisionDelta`, ...) are public fields: routines
  communicate through them exactly as in C.

## Interface changes for Game

All changes are additive. New interface members have empty default implementations (`void M() { }`,
`bool MessageShowing() => false`), so existing implementations keep compiling; override them.
"Added" = in the code now; "planned" = not yet in the code.

| Date | Status | Interface / API | Member | Meaning |
| --- | --- | --- | --- | --- |
| 2026-10-07 | added (phase 1) | ISimulationEvents | `PlaySoundEffect`, `ReleaseSoundSource`, `WeaponSelectionChanged`, `DestinationChanged`, `ClearHudGunReadouts`, `InitializeCockpit`, `TrainSimWaveCleared` | see the XML docs |
| 2026-10-07 | added (phase 2) | ISimulationEvents | `InitializeCockpitView`, `ServiceTrack`, `NewSpaceMusicChanges`, `HouseKeepCockpit`, `AfterburnerExpired`, `PlayerAfterburnerEngaged`, `TriggerPlayerHitPaletteFlash`, `FlashCockpitPaletteEntry`, `PlaceDamageOnCockpit`, `ShowComponentHitHudMessage`, `VduMalfunction`, `SelectCockpitVduMode`, `ShowMissileLockedMessage`, `RemoveMissileLockedMessage`, `PlayerReleaseWeaponLaunched` | `ServiceTrack`, `NewSpaceMusicChanges` and `SelectCockpitVduMode` (through `Malf`) draw random numbers in the receiver |
| 2026-10-07 | added (phases 1, 2) | ICockpitState, IShapeBounds, ICampaignState | `GetVduMode(vdu)`; `GetTransformedShapeBounds(...)`; `PlayerShipType`, `MissionScore`, `PromotionScore`, `CurrentMission`, `CurrentSeries`, `Get/SetPersonalityDeathMission`, `Get/SetAceFlags` | see the XML docs |
| 2026-10-07 | doc fix | ISimulationEvents | `InitializeCockpitView(mode)` | modes 0..3 = front/right/left/rear, 4 letterbox, 5 letterbox geometry, 6 full screen, 7 escape-pod interior |
| 2026-10-07 | added (phase 3) | ISimulationEvents | `SpaceBufferFlash()` | `warp`/`unwarp` (raised by `Warp`, `Unwarp`, `ArriveFromWarp`): `ClearViewport(&stSpaceBuffer, 0x0F)`, the white hyperspace flash of the next frame; no randoms |
| 2026-10-07 | added (phase 3) | ISimulationEvents + new enum `SimulationCockpitMessage` | `ShowCockpitMessage(SimulationCockpitMessage message, ObjectType shipType)` | right message slot, yellow. `AlreadyNear`/`EnemyNear`/`HazardNear` from `AutoPilotValid(true)` (`set_global_message`, 3 flashes); `WaitFor` (with the ship type for "Wait for %s"), `ObjectiveReached`, `AlreadyVisited` from `FlagReached` (`CockpitMessage`, 4 flashes, not restarted while the same text shows); no randoms |
| 2026-10-07 | added (phase 3) | ISimulationEvents | `ResetSoundState()` | `BeginAutopilot` stops all sounds (music.c `ResetSoundState`); no randoms |
| 2026-10-07 | added (phase 3) | ISimulationEvents | `DestinationChanged()` (existing) | now also raised by `CheckObjectives` when the current objective was lost (its `InvalidateVduMode(1)`) |
| 2026-10-07 | added (phase 3) | ICockpitState | `bool MessageShowing()` (default false; `DefaultCockpitState.MessageActive`) | `message_showing()` (`nMessageTimer > 0`); the wingman's "enemy sighted" line waits for a free message line (`ImperialFormation`) |
| 2026-10-07 | added (phase 3) | SpaceSimulation | `bool BeginAutopilot()`, `bool AutopilotTravelStep()`, `void AutopilotTravel()`, `void EndAutopilot()`, `AutoPilotValid(bool showReason)`, `AutoPosition`, `SetSpeed`, `PlayerWingman`, field `AutopilotFormationShipCount` | `auto_pilot_sequence` split (ADR-012), see the comment at the top of `SpaceSimulation.Autopilot.cs`: key A → UI `SelectCockpitVduMode(1, 5)` unless already → `if (sim.BeginAutopilot())` { UI `visit_the_cinema(12, 0, 120)` (save and clear `PlayerVulnerable`/`PlayerCollisionResponse`, `ForceView(12, 0)`, 120 × `Update3Space` + draw + present, restore); `sim.AutopilotTravel()` (instant; or `AutopilotTravelStep()` until false); `sim.EndAutopilot()` (includes the one `Update3Space`); UI `force_view(0, 0)` with the cockpitless dance, mouse reset } → input flush. Begin returns false when refused (message already raised). `AutoPilotValid(false)` is the cockpit light 4 poll |
| 2026-10-07 | added (phase 3) | SpaceSimulation | `Request(short requester, short ship, CommCommand command)` | comm orders (menu choice; key H (scan code 0x23) = 9 when the wingman is not in formation, key B (0x30) = 7 when he is and an enemy is within 14000); any value is accepted, `(CommCommand)(-1)` and unknown values do nothing; a recipient outside 0..9 is ignored; the taunts (4..6) draw one `RandomBelow(100)`, nothing else draws |
| 2026-10-07 | added (phase 3) | SpaceSimulation | `CheckObjectives()` (returns true when it cycled), `UpdateObjectiveLocation(objective)`, `ObjectiveLost`, `FindObjective(type, index)`, `FlagReached`, `CheckSighting`, `CheckVisit`, `SomeoneComing`, `EscortingAShip`, `CleanupObjectives`, `CanLand`, `IWannaRout`, `DisobeyFormation`, `BadTarget`, `TooBusy`, `Reply`, `WingmanDead`, `HaveTarget`, `AllowEngage`, `DisallowEngage`, `Try2AllowEngage` | objective tracking (the nav VDU calls `CheckObjectives` every tick in mode 5, then draws "calculating" itself when its displayed range differs from `CurrentObjectiveRange`), landing flag after flight (`FlagObjective(FindObjective(1, -1), 2)` when `ArcadeState == 1`), comm-menu queries |
| 2026-10-07 | added (phase 3) | SpaceSimulation | `Warp`, `Unwarp`, `ArriveFromWarp` | hyperspace jumps (called by the ship AI) |
| 2026-10-07 | added (phase 3) | SpaceSimulation | `AnyEnemyTail`, `IsShipTailingPlayerTarget`, `Triumph`, `MissileOnTail`, `EvaluateDamage`, `Unactive`, `ScanForEnemy`, `AttackerInRange`, `InDanger`, ... (all logic.c AI helpers) | queries for the music director, lights and comm menu |
| 2026-10-07 | no member | NPC communication | — | `npc_communication`, `vid_equiv`, `real_vid_transmit` are UI (ADR-012) and need no simulation callback: the UI calls its `npc_communication` itself in its cockpit-view block (camera view 0) right after `cockpit_explosion`, i.e. after `UpdateCockpitSimulation()` and before `CheckStranded()`. It reads and clears `Ships[obj].WingmanMessageState` (queued by the simulation's `SendMessage`), reads `Objects[obj].Class`, `Ships[obj].Side/Objective/Rating`, and draws from the shared `CRandom`: `RandomBelowOrEqual(5000)` on every call (not canned, not simulator), then per generic engaging Kilrathi `RandomBelowOrEqual(100)`, and `RandomBelowOrEqual(2)` for the chosen taunt |
| 2026-10-07 | added (phase 3) | SpaceSimulation | `PositionOf(obj)`, `VelocityOf(obj)`, `ForwardOf(obj)`, `CollisionRadiusOf(obj)`, `ObjectiveRecord(objective)` | reads that accept −1 and return what the Kilrathi Saga image holds there (Deviations 13); UI code that indexes the objectives with a flight-path entry (`aMissionObjectives[abFlightPath[i]]`) should use `ObjectiveRecord` |
| 2026-10-07 | added (phase 3) | SpaceSimulation | `ShipIntelligence`, `CapitalShipIntelligence`, `PerformManeuver`, the `Maneuver*` handlers and the mission handlers (`KilrathiPatrol`, `WingmanMission`, `StrikeMission`, ...) | public for tests and tools; `ObjectIntelligence` calls them every frame, the UI never needs to |
| 2026-10-07 | semantics (phase 3) | SpaceSimulation | `BadTarget(ship, -1)` | no player target counts as an Imperial target (the KS image's `aeShipSide[−1]`), so Confed wingmen refuse "Attack my target!" without a target |
| 2026-10-07 | changed (phase 3, additive) | SpaceSimulation | `ComputeStateHash()` | also hashes the AI scratch globals, objectives, flight path, comm and landing flags and the autopilot state; hash values differ from phase-2 builds (nothing stores golden hashes) |

**DivideFixed precision (open question 7):** the x87-versus-float question still needs confirmation
against the running Kilrathi Saga executable; until then the port keeps the x87 behaviour (float32
operands divided at double precision).

## Deviations

1. **Memory-safety guards** (the original reads/writes out of bounds; the port does the safe
   thing, none of these cases occurs in the shipped data unless noted):
   - `remove_weapon` writes 1 to loadout byte `count * 7 + 7`; for a ten-weapon loadout
     (Raptor, Gratha) that is byte 6 of the next ship's loadout (its first hardpoint) — the
     write is skipped (`WeaponLoadoutTests`). Reachable in the original when such a ship
     fires a missile or loses a weapon.
   - `set_formation_position` copies the frame of slot -1 when the root leader is not
     spawned: the ship keeps its own frame.
   - `init_intelligence_data` GOTO_WARP with a target outside the nav table keeps the
     current nav position as mission spot.
   - `set_up_action_sphere(19)` reads the "next" nav type past the 20-entry table: treated
     as "no wave".
   - `find_ships_sphere` stops the backward scan at record 0.
   - `Build_objective_list` reads at most 16 sources (the original has no bound) and gives up
     selecting a destination after 51 attempts when every objective is hidden (the original
     loops forever); `set_new_objective` with an empty flight path uses entry 0 instead of
     reading `abFlightPath[-1]`; `hidden_objective` of an invalid index counts as hidden.
   - `aMissionObjectives` and `abFlightPath` have one extra entry: the original writes and
     reads index `count`, which is out of bounds for 16 objectives.
   - SDL port guards kept: `LoadMissionData` refuses unused mission slots (returns false,
     `InitMission` too); `prepare_mission`'s carrier scan stops at record 48 (so
     `CarrierMissionShipIndex` is 48 without a Tiger's Claw; the DOS/KS original scans to
     64); `free_ship` ignores empty resource slots; `explode` reads the ship-only state only
     for ship slots; `send_appropriate_message` ignores an unowned attacker;
     `target_locking` without a selected release weapon drops the lock;
     `repair_internal_damage` guards each repaired component (the Win32 build tested an
     uninitialised component first).
   - `set_objects_data` only touches ship-only state for slots 0..9 (the original writes
     `acShipTarget[obj]` for any missile/ship slot; missiles only ever use slots 1..9).
   - Phase 2: `analyze_kill` gets an unowned asteroid, hazard mine or rock chunk (slot ≥ 10)
     as the creator of a kill and reads `aeShipSide[creator]` past the 12-entry table: the port
     treats such a creator as `Side.Neutral` (`SideOf`), also for the friend-or-foe missile's
     owner. `send_appropriate_message`, hull debris (`Create_ship_hit_debris`) and the player's
     internal damage with an attacker of -1 read index -1: no message / debris at the origin /
     attacker class NULL. A turret shell that could not be created is not launched (`fire` read
     `aShipPosition[-1]`). `draw_nav_pointer` with no objective (index -1) projects the origin.
     A thruster without a valid parent, a camera view object or view-script object outside the
     table and a carrier index outside the mission table are skipped. Division-by-zero guards
     where the data never divides by zero: turn rates summing to 0 (`check_for_lost_control`),
     shield interval 0 (`replenish_shields`), massless collision pairs (`object_collision`).
     `the_creator` stops after 64 owner links (an owner cycle would hang the original).
   - Phase 3 (none of these occurs in the shipped missions): `unactive` treats a slot outside 0..9
     as inactive (ships only live there); `maintain_formation` without a wing leader (−1: an
     Imperial wingman whose leader is not in space) does nothing (the original reads slot −1);
     `get_follow_point` leaves the point unchanged for a Kilrathi mission ship or a home-base record
     index outside the 20 nav points (the original reads past the nav table); `cruise_home` and
     `cruise_to_destination` skip the reached flag while the path index is −1 (no waypoint was
     ever found; the original reads `abFlightPath[−1]`); `update_objective_location` is skipped
     while `nSpaceFrame % count` is negative (after 32767 frames of one flight; the original reads
     before the objective table); `request` ignores a recipient outside 0..9 (the original reads
     and writes the −1 entries); `cleanup_objectives` also stops at the end of the 16-entry table
     (the original only at the −1 type terminator). `strike_mission` without its goal takes the
     SDL port's branch (`check_goal`), which the KS build takes as well: its extra
     `aeObjectClass[−1]` test reads the previous distances of slots 62/63, which are never
     written (class NULL).
2. **Graphics pointers become `ShapeRef`** (logical file, section). `FetchDiskPacketRetrying`
   success is `ISimulationResources.SectionExists`; `FreePacketAndClear` just clears the
   reference (no ownership). The 37-frame capital ship packet cache `aapPacketReferences`
   (expanded memory only) is a renderer concern and not modelled: `get_right_shape` sets
   `Shape = ShapeRef(type + 22, frame)` (a missing section is the renderer's/IShapeBounds'
   business). The nav pointer's cockpit shape (`pTargetLockShape`) is not known to the
   simulation (`Shape = None`, `SpaceObjectView.IsNavPointer`). `nMemoryConfiguration`
   defaults to 2 like the SDL port. Ship exhaust packets (section 2) are parsed into
   `ExhaustTable`s when a ship type is loaded (`LoadShip`), so no I/O happens during a frame
   (except where the original loads mid-flight: a nav sphere change loads its ship types).
3. **Side effects into other subsystems go through interfaces** (all synchronous, at the
   original statement): `ISimulationEvents` (see How to use; phase 1: `PlaySoundEffect`,
   `ReleaseSoundSource`, `WeaponSelectionChanged`, `DestinationChanged`,
   `ClearHudGunReadouts`, `InitializeCockpit`, `TrainSimWaveCleared`), `ICockpitState`
   (`get_mode`: the nav pointer's slot and the callers' HUD message conditions depend on the
   VDU modes), `IShapeBounds` (`GetTransformedShapeBounds` for `easy2see`). Render-only
   statements (`GetScreenUpdateFlag`, `initialize_view_buffer`, `SetViewportRect`, the SDL
   port's sub-pixel thruster anchors and joystick rumble) are omitted.
4. **Campaign state** (`playerShipType`, `currentMission`, `currentSeries`, `missionScore`,
   `promotionScore`, `personalityDeathMission`, `aceFlags`) is accessed through
   `ICampaignState` so the Game's campaign record can be used directly.
5. **`SinFixed`/`CosFixed`** are computed from the signed argument (Core `FixedMath.Sin/Cos`,
   `Geometry.FixedTrig` delegates). `WrapDegrees` takes a `short` like the original.
6. `ShipState` exists for slots 0..9 only (the original arrays are sized 10/12/16, the extra
   entries are padding); every access with a possibly larger index is guarded.
7. Text fields (nav names, objective descriptions, mission/series names) are decoded as
   Latin-1 strings up to the first NUL; objective `displayName`/`name` pointers become
   strings.
8. The canned-sequence pointer `apCannedSequence` is a cursor (array + position); the
   intro record 35 keeps the trailing 0 of its `short[42]` declaration. The view-script pointer
   `pViewScript` is likewise an array + position (`InitializeScriptedView(script, start)`);
   reads past its end count as -1 (end of script).
9. **Tiger's Claw landing** follows the SDL port (`LandFromAnyBearing = true`): within 700 units
   while facing the Claw (> 75 %). The Kilrathi Saga original also required the Claw's bow to
   face the player (`nTargetFacing > 70`); set `LandFromAnyBearing = false` for it.
10. **Argument evaluation order**: MSVC evaluates call arguments right to left. Where one call
    takes several random numbers as arguments the port draws them into locals in that order
    (`generate_stars`: the up offset of `start_dust` before the right offset). Every other random
    draw sits in its own statement.
11. **Uninitialised locals of the original** get defined values where they are observable at
    all: `inflict_damage`'s `destroyed` (1 % kill roll with an attacker that is not an NPC
    fighter) and the end of `your_internal_damage` return false (callers ignore the result);
    `score_for_kill` for a Kilrathi ship of an unlisted type uses the default event (score −1);
    `fire`'s `velocityAngle` starts at 0 (unobservable: the mine branch it guards can never fire
    because its outer and inner facing tests contradict each other).
12. **Split of the flight frame**: `Draw_3Space_Frame` and `update_cockpit` mix simulation and
    drawing; the simulation parts are `PrepareSpaceView` (frame skip, `RenderedSpaceFrame`,
    projection, star field, hazards, flames, children, sort, and `target_locking` from
    `overlay_head_up_display`, which runs after the drawing in the original — the drawing
    consumes no random numbers) and `UpdateCockpitSimulation` (`check_target`,
    `repair_internal_damage`). `HandleSpaceFlightControls`' key cases that change simulation
    state are methods; key decoding and repeat tests stay in the Game.
13. **Index −1 reads follow the Kilrathi Saga memory image** (faithful, not a guard): where the AI
    or the objective code uses −1 as an object or objective index and only reads, the port returns
    what the KS image holds at that address (from the globals.c address map, padding taken as
    zero): `PositionOf(−1)` = `asViableTargetDistance[10..15]` as three ints, `VelocityOf(−1)` =
    bytes 4..15 of `abFlightPath`, `ForwardOf(−1)` = the up vector of slot 63,
    `CollisionRadiusOf(−1)` = 0, `aeShipSide[−1]` = Imperial (`anRollGoal[14..15]`, never
    written), `ObjectiveRecord(−1)` = type from the animation indices of slots 59 and 60, index and
    flags from slot 61's, position at the origin; `FlagObjective(−1, f)` ORs `f` into the high
    byte of slot 61's animation index. Used by `perform_maneuver` and `Mkill_missile` without a
    target, `bad_target` without a player target, and every reader of the flight path's −1
    terminator (`get_follow_point`, `cruise_home`, `cruise_to_destination`, `arrive_from_warp`,
    `coming_home`, the autopilot). The SDL port returns early from `perform_maneuver` instead
    (skipping a random draw); the port follows KS. Other −1 accesses are guarded (item 1).
14. **AI presentation split (ADR-012)**: `auto_pilot_sequence` is split around the UI's cinematic
    (`BeginAutopilot` → UI `visit_the_cinema` → `AutopilotTravel` → `EndAutopilot` → UI
    `force_view`) with the statement order and random draws unchanged; `npc_communication`,
    `vid_equiv`, `real_vid_transmit` and `check_objectives`' "calculating" label are UI; the warp
    flash, the cockpit messages and the autopilot's sound reset are `ISimulationEvents` callbacks
    at the original statement positions. Maneuvers are dispatched by number like
    `apShipAiManeuverHandlers`: the enum names of 30..38 do not match the handlers (analysis §7.3).

## Open questions

1. **Cross-platform trig:** `Math.Sin/Cos` results are only sensitive to the last bit at
   multiples of 30° (sin) / 60° (cos), which `FixedTrigTests` locks with the Windows CRT
   values. Linux/macOS libm should agree (correctly rounded); verify on CI.
2. **Spikeri (type 14):** a capital ship in the KS table, but the DOS data has a
   fighter-style `SHIPTYPE.V14` (3 sections) and INSTALL.DAT names logical file 36
   `SHIP.V14`, which does not exist in the GOG install. `LoadShip(Spikeri)` therefore finds
   no silhouette (section 0x25) and the renderer cannot use capital-ship frames. Used by 24
   ship records of Secret Missions 2. Needs a decision (resource fallback + render as
   fighter sprite set, or keep KS class); ADR-007 says the stats stay KS.
3. Empty packet sections (e.g. `SHIPTYPE.V29` section 1, flag 0xFF) count as existing; the
   original `FetchDiskPacketRetrying` result for empty sections was not verified.
4. `init_intelligence_data` uses `nCurrentNavPoint` for the default mission spot, which is
   stale (previous mission's value) while `prepare_mission` spawns the team — faithful and kept
   now that the AI flies to it.
5. `CarrierMissionShipIndex` is 48 (SDL guard) instead of 64 when a mission has no carrier
   (training simulator); readers must range-check it (`CheckStranded` does).
6. Secret Missions 2 has a record with formation -1 and spot 9; harmless.
7. **`DivideFixed` precision:** Core divides the float32-rounded operands at double precision
   (x87 with MSVC's default 53-bit precision control). A float32 division (SSE: clang/gcc x64,
   i.e. the SDL port's x64 build) differs by 1 for large operands: 24 of the 22,062 phase-2
   cross-check lines (forces and collision separations) until the harness used double
   division. Not yet confirmed against the running KS executable.
8. ~~Without phase-3 AI, NPC ships fly straight at their mission speed and the Tiger's Claw
   drifts from collision impulses.~~ **Resolved (phase 3):** the AI avoids collisions and the
   Claw stays within 50 units over 600 frames (`AiFlightTests`).
9. `npc_communication`, `vid_transmit` and the VDU code consume random numbers in the
   cockpit-view block of `update_cockpit` (Game.Flight, ADR-012); the UI must call them in the
   original order (after `UpdateCockpitSimulation`, before `CheckStranded`) with the shared `CRandom`.
10. **Index −1 layout** (Deviations 13): reproduced from the globals.c address map with zero
    padding; the cross-check can only compare it with itself (clang lays the globals out
    differently). Confirm with a capture of the running KS executable when possible.

## Requests (for other owners)

1. Done in integration: Core `FixedMath.Sin/Cos(short)` direct, `WrapDegrees(short)`,
   wc1tool `--series/--campaign/--part` value options.
2. **Core resources**: INSTALL.DAT logical file 36 is `SHIP.V14`, the GOG install has
   `SHIPTYPE.V14` (see open question 2); a name fallback in `GameDirectory` would let the
   Spikeri shapes load.
3. **Game**: implement `ISimulationEvents` (incl. the phase-2 members: music via
   `MusicDirector.ServiceTrack`/`NewSpaceMusicChanges`, `SoundEffectManager.ServiceAfterburnerSound`
   / `OnAfterburnerExpired` / damage alarm, palette flashes, cockpit damage and messages, VDU
   malfunction/selection, `InitializeCockpitView` with `set_up_screen_viewport`'s viewport
   values), `ICockpitState` (the VDU mode stack), `IShapeBounds` (Graphics `ShapeBounds` on the
   space buffer, resolving `ShapeRef`s; a missing section is a point test), `ICampaignState`
   (the campaign record); set `ConstellationDefinitions`; drive the frame as in How to use;
   convert `SpaceViewSnapshot` for the renderer (`docs/analysis/rendering.md` §3.2). Phase 3:
   `SpaceBufferFlash`, `ShowCockpitMessage`, `ResetSoundState`, `ICockpitState.MessageShowing`,
   the comm menu → `Request`, the nav VDU → `CheckObjectives`, the autopilot split and
   `npc_communication` at its position (How to use; "Interface changes for Game").

## Next steps

Phase 3 completes the simulation port: every function of the mapping is done, partial by design
(the drawing half belongs to the UI) or owned by the Game. Remaining work, none of it blocking the
flight UI:
1. Game.Flight integration (flight-UI work stream): implement the phase-3 callbacks and drive the comm
   menu, the nav VDU (`CheckObjectives`), the autopilot split and `npc_communication` as in "How
   to use"; further simulation needs go under "Requests".
2. Confirm against the running Kilrathi Saga executable when a capture is possible: the
   `DivideFixed` precision (open question 7) and the index −1 layout (open question 10).
3. Spikeri resources (open question 2, request 2).

## Mapping

Status: **done** = ported and tested; **partial** = ported with the noted gap; **todo** with
the phase that ports it (3 AI); **Game** = flight-loop/cockpit code the Game port owns. The
Phase column is the phase that ported the function. Address = original function start.

Totals: 532 done, 2 partial, 0 todo, 11 Game.

### mathfp.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `RandomBelow` | 0x434CD0 | Core CRandom.Below | done | 1 |
| `SeedRandomFromClock` | 0x434CF0 | Core CRandom.SetSeed (caller seeds) | done | 1 |
| `RandomInRange` | 0x434D20 | Core CRandom.InRange | done | 1 |
| `RandomBelowOrEqual` | 0x434D50 | Core CRandom.BelowOrEqual | done | 1 |
| `MultiplyFixed` | 0x434D80 | Core FixedMath.Multiply | done | 1 |
| `DivideFixed` | 0x434DB0 | Core FixedMath.Divide | done | 1 |
| `SinFixed` | 0x434E00 | Geometry.FixedTrig.SinFixed (exact; see Requests) | done | 1 |
| `CosFixed` | 0x434E30 | Geometry.FixedTrig.CosFixed (exact; see Requests) | done | 1 |
| `ArcSin` | 0x434E60 | Core FixedMath.ArcSin | done | 1 |
| `ArcCos` | 0x434E90 | Core FixedMath.ArcCos | done | 1 |
| `Magnitude` | 0x434EC0 | Core FixedMath.Sqrt | done | 1 |
| `PlanarMagnitude` | 0x434EE0 | Core FixedMath.PlanarMagnitude | done | 1 |
| `Vector_magnitude` | 0x434F20 | Core FixedVector.Magnitude / VectorMath.Magnitude | done | 1 |

### mathutil.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `MinShort` | 0x41D0C0 | Geometry.ScalarMath.MinShort | done | 1 |
| `MaxShort` | 0x41D0E0 | Geometry.ScalarMath.MaxShort | done | 1 |

### geom.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `get_ship_max_velocity` | 0x4181C0 | SpaceSimulation.GetShipMaxVelocity | done | 1 |
| `recalc_max_velocity` | 0x418210 | SpaceSimulation.RecalcMaxVelocity | done | 1 |
| `drain_fuel` | 0x418280 | SpaceSimulation.DrainFuel | done | 1 |
| `damage_ion_drive` | 0x4182B0 | SpaceSimulation.DamageIonDrive | done | 1 |
| `GetShipAccelerationRate` | 0x4182F0 | SpaceSimulation.GetShipAccelerationRate | done | 1 |
| `point_at` | 0x418330 | SpaceObject.PointAt / SpaceSimulation.PointAt | done | 1 |
| `look_at` | 0x4183A0 | SpaceSimulation.LookAt | done | 1 |
| `position_relative` | 0x4183D0 | VectorMath.PositionRelative | done | 1 |
| `position_relative_ijk` | 0x418420 | SpaceObject.PositionRelativeIjk | done | 1 |
| `FixedToShortSaturating` | 0x4184C0 | Core FixedMath.ToShortSaturating | done | 1 |
| `MinInt` | 0x4184E0 | ScalarMath.MinInt | done | 1 |
| `MaxInt` | 0x4184F0 | ScalarMath.MaxInt | done | 1 |
| `AbsInt` | 0x418500 | ScalarMath.AbsInt | done | 1 |
| `intfract_sign` | 0x418510 | ScalarMath.IntFractSign | done | 1 |
| `SignShort` | 0x418520 | ScalarMath.SignShort | done | 1 |
| `SignFixed` | 0x418540 | Core FixedMath.Sign | done | 1 |
| `WrapDegrees` | 0x418560 | ScalarMath.WrapDegrees (short argument) | done | 1 |
| `equ_vector` | 0x418590 | VectorMath.AreEqual | done | 1 |
| `zero_vector` | 0x4185F0 | FixedVector.Zero | done | 1 |
| `negate_vector` | 0x418600 | VectorMath.Negate | done | 1 |
| `AddFixedVectors` | 0x418620 | VectorMath.Add | done | 1 |
| `SubtractFixedVectors` | 0x418650 | VectorMath.Subtract | done | 1 |
| `ComputeVectorDelta` | 0x418680 | VectorMath.Delta | done | 1 |
| `ScaleFixedVector` | 0x4186B0 | VectorMath.Scale | done | 1 |
| `divide_vector` | 0x418700 | VectorMath.Divide | done | 1 |
| `ChooseRandomSignedMagnitude` | 0x418750 | RandomVectors.ChooseRandomSignedMagnitude | done | 1 |
| `MakeRandomVectorFixed` | 0x418780 | RandomVectors.MakeRandomVectorFixed | done | 1 |
| `FillFixedVectorWithRandomComponents` | 0x4187E0 | RandomVectors.FillFixedVectorWithRandomComponents | done | 1 |
| `random_radial` | 0x418800 | RandomVectors.RandomRadial | done | 1 |
| `MakeRandomNormalizedVector` | 0x418840 | RandomVectors.MakeRandomNormalizedVector | done | 1 |
| `rectangular_to_spherical` | 0x418890 | VectorMath.RectangularToSpherical | done | 1 |
| `ConvertShortVectorToFixedVector` | 0x418980 | ShortVector.ToFixed | done | 1 |
| `ConvertFixedVectorToShortVector` | 0x4189B0 | ShortVector.FromFixed | done | 1 |
| `dot_product` | 0x4189E0 | VectorMath.Dot | done | 1 |
| `vector_angle` | 0x418A30 | VectorMath.VectorAngle | done | 1 |
| `vector_cross_product` | 0x418A80 | VectorMath.Cross | done | 1 |
| `NormalizeFixedVector` | 0x418B10 | VectorMath.Normalize | done | 1 |
| `vector_length_in_dir` | 0x418B60 | VectorMath.LengthInDirection | done | 1 |
| `vector_component_in_dir` | 0x418BB0 | VectorMath.ComponentInDirection | done | 1 |
| `rotate_about_i` | 0x418BE0 | VectorMath.RotateAboutI | done | 1 |
| `rotate_about_j` | 0x418D00 | VectorMath.RotateAboutJ | done | 1 |
| `rotate_about_k` | 0x418E40 | VectorMath.RotateAboutK | done | 1 |
| `init_ijk` | 0x418F60 | SpaceObject.InitIjk | done | 1 |
| `copy_frame` | 0x418FD0 | SpaceObject.CopyFrameFrom / SpaceSimulation.CopyFrame | done | 1 |
| `fix_objects_ijk` | 0x419050 | SpaceObject.FixIjk | done | 1 |
| `transform_to_objects_frame` | 0x4190B0 | SpaceObject.TransformToObjectsFrame | done | 1 |
| `alter_pitch` | 0x419110 | SpaceObject.AlterPitch | done | 1 |
| `alter_yaw` | 0x419150 | SpaceObject.AlterYaw | done | 1 |
| `alter_roll` | 0x419190 | SpaceObject.AlterRoll | done | 1 |
| `distance_between_points` | 0x4191D0 | VectorMath.DistanceBetweenPoints | done | 1 |
| `distance_from_point` | 0x419210 | SpaceSimulation.DistanceFromPoint | done | 1 |
| `distance_from_object` | 0x419260 | SpaceSimulation.DistanceFromObject | done | 1 |
| `get_facing_range_from_point` | 0x419290 | SpaceSimulation.GetFacingRangeFromPoint | done | 1 |
| `get_facing_range_from_object` | 0x419310 | SpaceSimulation.GetFacingRangeFromObject | done | 1 |
| `ship_vs_point` | 0x419390 | SpaceSimulation.ShipVsPoint | done | 1 |
| `ship_vs_ship` | 0x4193B0 | SpaceSimulation.ShipVsShip | done | 1 |
| `facing_to_object` | 0x4193D0 | SpaceSimulation.FacingToObject | done | 1 |
| `match_roll_orientation` | 0x419440 | SpaceSimulation.MatchRollOrientation | done | 1 |
| `set_ship_rotation_goals` | 0x4194D0 | SpaceSimulation.SetShipRotationGoals | done | 1 |
| `point_ship` | 0x419620 | SpaceSimulation.PointShip | done | 1 |
| `point_ship_at_point` | 0x419660 | SpaceSimulation.PointShipAtPoint | done | 1 |
| `point_ship_at_object` | 0x4196A0 | SpaceSimulation.PointShipAtObject | done | 1 |
| `point_capital_ship_at_object` | 0x4196C0 | SpaceSimulation.PointCapitalShipAtObject | done | 1 |
| `point_ship_behind_object` | 0x419710 | SpaceSimulation.PointShipBehindObject | done | 1 |
| `point_ship_below_object` | 0x419790 | SpaceSimulation.PointShipBelowObject | done | 1 |
| `point_perpendicular_to_point` | 0x419810 | SpaceSimulation.PointPerpendicularToPoint | done | 1 |
| `point_perpendicular` | 0x419850 | SpaceSimulation.PointPerpendicular | done | 1 |
| `point_parallel` | 0x419870 | SpaceSimulation.PointParallel | done | 1 |
| `MoveObjectAlongDirection` | 0x4198A0 | SpaceSimulation.MoveObjectAlongDirection | done | 1 |
| `NormalizeAndScaleVector` | 0x419950 | VectorMath.NormalizeAndScale | done | 1 |
| `SetVectorFixedPoint` | 0x419970 | VectorMath.SetLength | done | 1 |
| `IsPointWithinRange` | 0x419990 | VectorMath.IsPointWithinRange | done | 1 |
| `check_for_collision` | 0x4199C0 | SpaceSimulation.CheckForCollision | done | 2 |
| `position_child` | 0x419A70 | SpaceSimulation.PositionChild | done | 1 |
| `child_object` | 0x419B40 | SpaceSimulation.ChildObject | done | 1 |
| `get_ship_slot` | 0x419B70 | SpaceSimulation.GetShipSlot | done | 1 |
| `find_vacant_3d_object` | 0x419BA0 | SpaceSimulation.FindVacant3dObject | done | 1 |
| `remove_object` | 0x419BD0 | SpaceSimulation.RemoveObject | done | 1 |
| `apply_force_to_objects_center` | 0x419CC0 | SpaceSimulation.ApplyForceToObjectsCenter | done | 2 |
| `apply_force_to_object` | 0x419D10 | SpaceSimulation.ApplyForceToObject | done | 2 |
| `rotational_acceleration` | 0x419F70 | SpaceSimulation.RotationalAcceleration | done | 2 |
| `ClampVectorTo30` | 0x41A0F0 | ScalarMath.DecayTowardZero | done | 1 |
| `ClampTo30` | 0x41A110 | ScalarMath.ClampTo30 | done | 1 |
| `IsPointWithinEyeViewCone` | 0x41A130 | SpaceSimulation.IsPointWithinEyeViewCone | done | 2 |
| `transform_objects_to_your_view` | 0x41A1D0 | SpaceSimulation.TransformObjectsToYourView | done | 2 |
| `set_background_objects_rotation` | 0x41A530 | SpaceSimulation.SetBackgroundObjectsRotation | done | 2 |
| `get_right_shape` | 0x41A610 | SpaceSimulation.GetRightShape (capital ships: Shape = ShapeRef(type + 22, frame)) | done | 2 |

### disk.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `initialize_object` | 0x41DEE0 | SpaceSimulation.InitializeObject | done | 1 |
| `borrow_dust` | 0x41DF40 | SpaceSimulation.BorrowDust | done | 1 |
| `new_object` | 0x41DF70 | SpaceSimulation.NewObject | done | 1 |
| `initialize_ship` | 0x41DFA0 | SpaceSimulation.InitializeShip | done | 1 |
| `any_selected` | 0x41DFE0 | SpaceSimulation.AnySelected | done | 1 |
| `remove_weapon` | 0x41E040 | SpaceSimulation.RemoveWeapon | done | 1 |
| `set_objects_data` | 0x41E120 | SpaceSimulation.SetObjectsData | done | 1 |
| `match_rotation_goal` | 0x41E400 | SpaceSimulation.MatchRotationGoal | done | 1 |
| `rotate_object_to_goal` | 0x41E520 | SpaceSimulation.RotateObjectToGoal | done | 1 |
| `celerate` | 0x41E710 | SpaceSimulation.Celerate | done | 1 |
| `approach_speed` | 0x41E750 | SpaceSimulation.ApproachSpeed | done | 1 |
| `steady_object` | 0x41E7C0 | SpaceSimulation.SteadyObject | done | 1 |
| `real_velocity` | 0x41E7F0 | SpaceObject.RealVelocity / SpaceSimulation.RealVelocity | done | 1 |
| `fix_velocity` | 0x41E820 | SpaceObject.FixVelocity / SpaceSimulation.FixVelocity | done | 1 |
| `sort_viable_target_list` | 0x41E860 | SpaceSimulation.SortViableTargetList | done | 2 |

### spc.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `SetFleetOverviewView` | 0x410740 | SpaceSimulation.SetFleetOverviewView | done | 2 |
| `rotate_eye_to_goal` | 0x410A30 | SpaceSimulation.RotateEyeToGoal | done | 2 |
| `GetVectorMagnitude` | 0x410AD0 | SpaceSimulation.GetVectorMagnitude | done | 2 |
| `set_eye_direction_and_position` | 0x410AF0 | SpaceSimulation.SetEyeDirectionAndPosition | done | 2 |
| `force_view` | 0x4117B0 | SpaceSimulation.ForceView | done | 2 |
| `new_view` | 0x4117D0 | SpaceSimulation.NewView (initialize_cockpit via ISimulationEvents.InitializeCockpitView) | done | 2 |
| `start_dust` | 0x411EC0 | SpaceSimulation.StartDust | done | 2 |
| `generate_stars` | 0x411FE0 | SpaceSimulation.GenerateStars | done | 2 |
| `update_star_field` | 0x412100 | SpaceSimulation.UpdateStarField | done | 2 |
| `count_down` | 0x412410 | SpaceSimulation.CountDown | done | 2 |
| `house_keep_objects` | 0x412430 | SpaceSimulation.HouseKeepObjects | done | 2 |
| `update_objects_in_space` | 0x412820 | SpaceSimulation.UpdateObjectsInSpace | done | 2 |
| `rotate_object` | 0x412920 | SpaceSimulation.RotateObject | done | 2 |
| `accelerate_and_move_object` | 0x4129A0 | SpaceSimulation.AccelerateAndMoveObject | done | 2 |
| `animate_shape` | 0x412CD0 | SpaceSimulation.AnimateShape | done | 1 |
| `animate_object` | 0x412E30 | SpaceSimulation.AnimateObject | done | 2 |
| `hit_asteroid` | 0x413030 | SpaceSimulation.HitAsteroid | done | 2 |
| `object_collision` | 0x4130D0 | SpaceSimulation.ObjectCollision | done | 2 |
| `object_intelligence` | 0x413880 | SpaceSimulation.ObjectIntelligence | done | 3 |

### ship.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `check_for_lost_control` | 0x41E650 | SpaceSimulation.CheckForLostControl | done | 2 |
| `send_appropriate_message` | 0x41E900 | SpaceSimulation.SendAppropriateMessage | done | 2 |
| `inflict_damage` | 0x41E9B0 | SpaceSimulation.InflictDamage | done | 2 |
| `pilot_hit` | 0x41EC60 | SpaceSimulation.PilotHit | done | 2 |
| `onboard_explosion` | 0x41ECE0 | SpaceSimulation.OnboardExplosion | done | 2 |
| `call_enemy` | 0x41EDB0 | SpaceSimulation.CallEnemy | done | 2 |
| `internal_damage` | 0x41EE20 | SpaceSimulation.InternalDamage | done | 2 |
| `revise_shields` | 0x41F1A0 | SpaceSimulation.ReviseShields | done | 2 |
| `your_internal_damage` | 0x41F220 | SpaceSimulation.YourInternalDamage | done | 2 |
| `check_computer_damage` | 0x41F5D0 | SpaceSimulation.CheckComputerDamage | done | 2 |
| `ReportComponentRepaired` | 0x41F5F0 | SpaceSimulation.ReportComponentRepaired | done | 2 |
| `repair_internal_damage` | 0x41F660 | SpaceSimulation.RepairInternalDamage | done | 2 |
| `Create_ship_hit_debris` | 0x41F700 | SpaceSimulation.CreateShipHitDebris | done | 2 |
| `check_next_wave` | 0x41F7C0 | SpaceSimulation.CheckNextWave | done | 1 |
| `Create_explosion_debris` | 0x41F800 | SpaceSimulation.CreateExplosionDebris | done | 2 |
| `affect_mission_score` | 0x41F9E0 | SpaceSimulation.AffectMissionScore | done | 2 |
| `score_for_kill` | 0x41FA90 | SpaceSimulation.ScoreForKill | done | 2 |
| `analyze_kill` | 0x41FB40 | SpaceSimulation.AnalyzeKill | done | 2 |
| `ShipExplosion` | 0x41FBC0 | SpaceSimulation.ShipExplosion | done | 2 |
| `Explosion` | 0x41FCD0 | SpaceSimulation.Explosion | done | 2 |
| `the_creator` | 0x41FEB0 | SpaceSimulation.TheCreator | done | 2 |
| `explosion_shock_wave` | 0x41FEE0 | SpaceSimulation.ExplosionShockWave + ShockWaveDamage | done | 2 |
| `explode` | 0x420040 | SpaceSimulation.Explode | done | 2 |
| `send_at_point` | 0x420190 | SpaceSimulation.SendAtPoint | done | 2 |
| `find_child_object` | 0x4201D0 | SpaceSimulation.FindChildObject | done | 2 |
| `find_child_ship` | 0x420210 | SpaceSimulation.FindChildShip | done | 2 |
| `launch_object` | 0x420260 | SpaceSimulation.LaunchObject | done | 2 |
| `fire` | 0x4202D0 | SpaceSimulation.Fire | done | 2 |
| `hemisphere` | 0x4207E0 | SpaceSimulation.Hemisphere | done | 2 |
| `fire_flack` | 0x420840 | SpaceSimulation.FireFlack | done | 2 |
| `rnd_sign` | 0x4208C0 | SpaceSimulation.RndSign | done | 2 |
| `rnd_aim` | 0x4208E0 | SpaceSimulation.RndAim | done | 2 |
| `pop_flack` | 0x420920 | SpaceSimulation.PopFlack | done | 2 |
| `fire_turrets` | 0x420AA0 | SpaceSimulation.FireTurrets | done | 2 |
| `fire_weapon` | 0x420C20 | SpaceSimulation.FireWeapon | done | 2 |

### logic.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `find_weapon` | 0x421100 | SpaceSimulation.FindWeapon | done | 1 |
| `fire_missile` | 0x421150 | SpaceSimulation.FireMissile | done | 2 |
| `fire_fixed_projectile_weapon` | 0x421220 | SpaceSimulation.FireFixedProjectileWeapon | done | 2 |
| `drop_mine` | 0x4212A0 | SpaceSimulation.DropMine | done | 2 |
| `fire_afterburner` | 0x421350 | SpaceSimulation.FireAfterburner | done | 2 |
| `fire_super_brake` | 0x4213B0 | SpaceSimulation.FireSuperBrake | done | 2 |
| `flip_angle` | 0x4213D0 | SpaceSimulation.FlipAngle | done | 2 |
| `place_exhaust_on_ships` | 0x421430 | SpaceSimulation.PlaceExhaustOnShips (Objects.ExhaustTable from section 2 of the ship file) | done | 2 |
| `reposition_fixed_child_objects` | 0x4215E0 | SpaceSimulation.RepositionFixedChildObjects | done | 2 |
| `housekeep_power_plant_and_fuel` | 0x421760 | SpaceSimulation.HousekeepPowerPlantAndFuel | done | 2 |
| `replenish_shields` | 0x421780 | SpaceSimulation.ReplenishShields | done | 2 |
| `replenish_weapon_energy_bank` | 0x421830 | SpaceSimulation.ReplenishWeaponEnergyBank | done | 2 |
| `accelerate` | 0x4218D0 | SpaceSimulation.Accelerate | done | 2 |
| `your_afterburner` | 0x421920 | SpaceSimulation.YourAfterburner (sound via ISimulationEvents.PlayerAfterburnerEngaged) | done | 2 |
| `initialize_direction_view_frame` | 0x421E20 | SpaceSimulation.InitializeDirectionViewFrame (private) | done | 1 |
| `initialize_direction_view_frames` | 0x421EF0 | SpaceSimulation.InitializeDirectionViewFrames | done | 1 |
| `LoadSpaceflightResources` | 0x421F50 | — | todo (Game owns it) | Game |
| `ace_status` | 0x422010 | SpaceSimulation.AceStatus | done | 1 |
| `unflag_ace` | 0x422030 | SpaceSimulation.UnflagAce | done | 1 |
| `flag_ace` | 0x422050 | SpaceSimulation.FlagAce | done | 1 |
| `kill_ace` | 0x422060 | SpaceSimulation.KillAce | done | 1 |
| `ace_greeting` | 0x422090 | SpaceSimulation.AceGreeting | done | 3 |
| `prepare_ace` | 0x4220D0 | SpaceSimulation.PrepareAce | done | 1 |
| `signed_random` | 0x4220F0 | Core CRandom.Signed / RandomVectors.SignedRandom | done | 1 |
| `alert_flag` | 0x422110 | SpaceSimulation.AlertFlag | done | 1 |
| `set_alert` | 0x422140 | SpaceSimulation.SetAlert | done | 1 |
| `clear_alert` | 0x422160 | SpaceSimulation.ClearAlert | done | 1 |
| `start_collision_alert` | 0x422180 | SpaceSimulation.StartCollisionAlert | done | 3 |
| `try2end_collision_alert` | 0x4221E0 | SpaceSimulation.Try2EndCollisionAlert | done | 3 |
| `normal_speed` | 0x422220 | SpaceSimulation.NormalSpeed | done | 1 |
| `real_crash_time` | 0x422260 | SpaceSimulation.RealCrashTime | done | 3 |
| `clear_crash_cache` | 0x422440 | SpaceSimulation.ClearCrashCache | done | 2 |
| `crash_time` | 0x422460 | SpaceSimulation.CrashTime | done | 3 |
| `detect_collisions` | 0x4224F0 | SpaceSimulation.DetectCollisions | done | 3 |
| `unactive` | 0x422560 | SpaceSimulation.Unactive | done | 3 |
| `are_alive` | 0x422590 | SpaceSimulation.AreAlive | done | 3 |
| `trim_goals` | 0x4225C0 | SpaceSimulation.TrimGoals | done | 1 |
| `report_kilrathi_rout` | 0x422640 | SpaceSimulation.ReportKilrathiRout | done | 1 |
| `find_ship_index` | 0x422710 | SpaceSimulation.FindShipIndex | done | 1 |
| `try2rout` | 0x422780 | SpaceSimulation.Try2Rout | done | 3 |
| `no_goal` | 0x422830 | SpaceSimulation.NoGoal | done | 1 |
| `being_tailed` | 0x422860 | SpaceSimulation.BeingTailed | done | 3 |
| `any_enemy_tail` | 0x4228A0 | SpaceSimulation.AnyEnemyTail | done | 3 |
| `detect_enemy_tail` | 0x422930 | SpaceSimulation.DetectEnemyTail | done | 3 |
| `is_ship_tailing_player_target` | 0x4229B0 | SpaceSimulation.IsShipTailingPlayerTarget | done | 3 |
| `missile_on_tail` | 0x4229F0 | SpaceSimulation.MissileOnTail | done | 3 |
| `select_weighted_value` | 0x422A30 | SpaceSimulation.SelectWeightedValue | done | 3 |
| `build_squad_list` | 0x422A70 | SpaceSimulation.BuildSquadList | done | 3 |
| `find_squad_center` | 0x422AC0 | SpaceSimulation.FindSquadCenter | done | 3 |
| `init_formation_burst` | 0x422B30 | SpaceSimulation.InitFormationBurst | done | 3 |
| `reset_mission_type` | 0x422BE0 | SpaceSimulation.ResetMissionType | done | 1 |
| `change_mission_type` | 0x422C30 | SpaceSimulation.ChangeMissionType | done | 1 |
| `reset_objective` | 0x422C70 | SpaceSimulation.ResetObjective | done | 1 |
| `alter_objective` | 0x422CA0 | SpaceSimulation.AlterObjective | done | 1 |
| `reset_tactic` | 0x422CD0 | SpaceSimulation.ResetTactic | done | 1 |
| `alter_tactic` | 0x422D00 | SpaceSimulation.AlterTactic | done | 1 |
| `reset_maneuver` | 0x422D30 | SpaceSimulation.ResetManeuver | done | 1 |
| `try2reset_maneuver` | 0x422D60 | SpaceSimulation.Try2ResetManeuver | done | 1 |
| `set_special` | 0x422D90 | SpaceSimulation.SetSpecial | done | 1 |
| `approach_zero_speed` | 0x422DD0 | SpaceSimulation.ApproachZeroSpeed | done | 1 |
| `approach_min_speed` | 0x422DF0 | SpaceSimulation.ApproachMinSpeed | done | 1 |
| `approach_half_speed` | 0x422E10 | SpaceSimulation.ApproachHalfSpeed | done | 1 |
| `approach_cruise_speed` | 0x422E50 | SpaceSimulation.ApproachCruiseSpeed | done | 1 |
| `approach_full_speed` | 0x422E80 | SpaceSimulation.ApproachFullSpeed | done | 1 |
| `approach_ship_speed` | 0x422EA0 | SpaceSimulation.ApproachShipSpeed | done | 1 |
| `get_front_spot` | 0x422EC0 | SpaceSimulation.GetFrontSpot | done | 1 |
| `get_rear_spot` | 0x422F10 | SpaceSimulation.GetRearSpot | done | 1 |
| `close_behind` | 0x422F60 | SpaceSimulation.CloseBehind | done | 3 |
| `scan_for_enemy` | 0x422F80 | SpaceSimulation.ScanForEnemy | done | 3 |
| `any_enemy` | 0x423070 | SpaceSimulation.AnyEnemy | done | 2 |
| `nearest_enemy_range` | 0x4230F0 | SpaceSimulation.NearestEnemyRange | done | 3 |
| `fire_when_ready` | 0x423210 | SpaceSimulation.FireWhenReady | done | 3 |
| `ships_within_range` | 0x423260 | SpaceSimulation.ShipsWithinRange | done | 3 |
| `attacker_in_range` | 0x4232B0 | SpaceSimulation.AttackerInRange | done | 3 |
| `in_danger` | 0x423350 | SpaceSimulation.InDanger | done | 3 |
| `target_within_range` | 0x423400 | SpaceSimulation.TargetWithinRange | done | 3 |
| `build_target_list` | 0x423440 | SpaceSimulation.BuildTargetList | done | 2 |
| `select_safe_target` | 0x4234C0 | SpaceSimulation.SelectSafeTarget | done | 3 |
| `inherit_leader_mission` | 0x423530 | SpaceSimulation.InheritLeaderMission | done | 3 |
| `inherit_leader` | 0x4235B0 | SpaceSimulation.InheritLeader | done | 3 |
| `dead_ship` | 0x423610 | SpaceSimulation.DeadShip | done | 1 |
| `gone_ship` | 0x423640 | SpaceSimulation.GoneShip | done | 1 |
| `skill_rating` | 0x423670 | SpaceSimulation.SkillRating | done | 1 |
| `skill_check` | 0x4236B0 | SpaceSimulation.SkillCheck | done | 1 |
| `find_ships_sphere` | 0x4236F0 | SpaceSimulation.FindShipsSphere | done | 1 |
| `locate_ship` | 0x423780 | SpaceSimulation.LocateShip | done | 1 |
| `get_follow_point` | 0x423820 | SpaceSimulation.GetFollowPoint | done | 3 |
| `get_first_follow_point` | 0x423930 | SpaceSimulation.GetFirstFollowPoint | done | 3 |
| `hostile_sphere` | 0x423970 | SpaceSimulation.HostileSphere | done | 3 |
| `abandoned` | 0x4239D0 | SpaceSimulation.Abandoned | done | 3 |
| `engage` | 0x423A50 | SpaceSimulation.Engage | done | 3 |
| `target_valid` | 0x423AC0 | SpaceSimulation.TargetValid | done | 3 |
| `triumph` | 0x423B00 | SpaceSimulation.Triumph | done | 3 |
| `find_ratio` | 0x423BA0 | ScalarMath.FindRatio | done | 1 |
| `evaluate_damage` | 0x423C00 | SpaceSimulation.EvaluateDamage | done | 3 |
| `mine_available` | 0x423CD0 | SpaceSimulation.MineAvailable | done | 3 |
| `LoadShapeSet` | 0x423CE0 | SpaceSimulation.LoadShapeSet (private, OBJECTS.VGA descriptors) | done | 1 |
| `FreeShapeSet` | 0x423D50 | SpaceSimulation.Free3SpaceObjects (inlined) | done | 1 |
| `LoadPacketResourceList` | 0x423D80 | SpaceSimulation.LoadMissionResources | done | 1 |
| `InitializeConstellationObject` | 0x4242D0 | SpaceSimulation.InitializeConstellationObject | done | 1 |
| `FreeConstellationObject` | 0x4243B0 | SpaceSimulation.FreeConstellationObject | done | 1 |
| `init_constellation` | 0x4243E0 | SpaceSimulation.InitConstellation | done | 1 |
| `free_constellation` | 0x424490 | SpaceSimulation.FreeConstellation | done | 1 |
| `init_3Space_objects` | 0x424A80 | SpaceSimulation.Init3SpaceObjects | done | 1 |
| `load_common_3Space_objects` | 0x424B00 | SpaceSimulation.LoadCommon3SpaceObjects | done | 1 |
| `remove_all_3d_objects` | 0x424B80 | SpaceSimulation.RemoveAll3dObjects | done | 1 |
| `free_3Space` | 0x424BA0 | SpaceSimulation.Free3Space | done | 1 |
| `free_3Space_objects` | 0x424BE0 | SpaceSimulation.Free3SpaceObjects | done | 1 |

### smart.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `steer_away_from_object` | 0x433AC0 | SpaceSimulation.SteerAwayFromObject | done | 3 |
| `steer_away_from_predicted_object` | 0x433B90 | SpaceSimulation.SteerAwayFromPredictedObject | done | 3 |
| `prevent_collision` | 0x433C80 | SpaceSimulation.PreventCollision | done | 3 |
| `handle_collisions` | 0x433D90 | SpaceSimulation.HandleCollisions | done | 3 |
| `regulate_turn` | 0x433DE0 | SpaceSimulation.RegulateTurn | done | 3 |
| `select_target` | 0x433E50 | SpaceSimulation.SelectTarget | done | 3 |
| `veer_random` | 0x433EC0 | SpaceSimulation.VeerRandom | done | 3 |
| `offset_location` | 0x433F50 | SpaceObject.OffsetLocation / SpaceSimulation.OffsetLocation | done | 1 |
| `compute_formation_destination` | 0x433FF0 | SpaceSimulation.ComputeFormationDestination | done | 3 |
| `control_speed` | 0x434040 | SpaceSimulation.ControlSpeed | done | 3 |
| `chase_location` | 0x4340F0 | SpaceSimulation.ChaseLocation | done | 3 |
| `goto_location` | 0x4342C0 | SpaceSimulation.GotoLocation | done | 3 |
| `goto_formation` | 0x434360 | SpaceSimulation.GotoFormation | done | 3 |
| `maintain_formation` | 0x4344E0 | SpaceSimulation.MaintainFormation | done | 3 |
| `reset_stress` | 0x434550 | SpaceSimulation.ResetStress (no effect for ship slots, as in the original) | done | 3 |
| `stress_morale` | 0x4345D0 | SpaceSimulation.StressMorale | done | 3 |
| `any_defense` | 0x4345F0 | SpaceSimulation.AnyDefense | done | 3 |
| `pick_regular_maneuver` | 0x434630 | SpaceSimulation.PickRegularManeuver | done | 3 |
| `pick_from_list` | 0x434800 | SpaceSimulation.PickFromList | done | 3 |
| `pick_kilrathi_maneuver` | 0x4348A0 | SpaceSimulation.PickKilrathiManeuver | done | 3 |
| `process_maneuver_node` | 0x434900 | SpaceSimulation.ProcessManeuverNode | done | 3 |
| `handle_stress` | 0x434980 | SpaceSimulation.HandleStress | done | 3 |
| `intelligence_events` | 0x434A80 | SpaceSimulation.IntelligenceEvents | done | 3 |
| `chase_speed` | 0x434C70 | SpaceSimulation.ChaseSpeed | done | 3 |

### brains.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `SetShipAiScratchWord` | 0x4060A0 | SpaceSimulation.TooCloseRange (field, set by PerformManeuver) | done | 3 |
| `maneuver_complete` | 0x4060B0 | SpaceSimulation.ManeuverComplete | done | 1 |
| `Mline_up_drop` | 0x4060D0 | SpaceSimulation.ManeuverLineUpDrop | done | 3 |
| `Mwabble` | 0x406130 | SpaceSimulation.ManeuverWabble | done | 3 |
| `advance` | 0x4061E0 | SpaceSimulation.Advance | done | 3 |
| `ShipAiState35` | 0x406200 | SpaceSimulation.ManeuverTurnAndFire | done | 3 |
| `Mfull_ahead` | 0x406310 | SpaceSimulation.ManeuverFullAhead | done | 3 |
| `Mchill` | 0x406350 | SpaceSimulation.ManeuverChill | done | 3 |
| `Mdrop_a_mine` | 0x4063B0 | SpaceSimulation.ManeuverDropAMine | done | 3 |
| `Mthink` | 0x406400 | SpaceSimulation.ManeuverThink | done | 3 |
| `Mtight_loop` | 0x406440 | SpaceSimulation.ManeuverTightLoop | done | 3 |
| `Mhard_break` | 0x4064F0 | SpaceSimulation.ManeuverHardBrake | done | 3 |
| `Msit_n_spin` | 0x4065A0 | SpaceSimulation.ManeuverSitNSpin | done | 3 |
| `Mturn_n_spin` | 0x4067A0 | SpaceSimulation.ManeuverTurnNSpin | done | 3 |
| `Mburnout` | 0x406860 | SpaceSimulation.ManeuverBurnout | done | 3 |
| `Mkickit` | 0x4068D0 | SpaceSimulation.ManeuverKickit | done | 3 |
| `Mturn_n_kick` | 0x406910 | SpaceSimulation.ManeuverTurnNKick | done | 3 |
| `Mroll_over` | 0x406990 | SpaceSimulation.ManeuverRollOver | done | 3 |
| `Mhard_turn` | 0x4069F0 | SpaceSimulation.ManeuverHardTurn | done | 3 |
| `Mfish_hook` | 0x406A50 | SpaceSimulation.ManeuverFishHook | done | 3 |
| `Mtry2tail` | 0x406B60 | SpaceSimulation.ManeuverTry2Tail | done | 3 |
| `Msplit_left` | 0x406BD0 | SpaceSimulation.ManeuverSplitLeft | done | 3 |
| `Msplit_right` | 0x406C20 | SpaceSimulation.ManeuverSplitRight | done | 3 |
| `Mgloat` | 0x406C70 | SpaceSimulation.ManeuverGloat | done | 3 |
| `Mtail_fire` | 0x406D20 | SpaceSimulation.ManeuverTailFire | done | 3 |
| `Mzip_past` | 0x406D80 | SpaceSimulation.ManeuverZipPast | done | 3 |
| `Mtarget_missile` | 0x406E10 | SpaceSimulation.ManeuverTargetMissile | done | 3 |
| `Mram_missile` | 0x406EC0 | SpaceSimulation.ManeuverRamMissile | done | 3 |
| `Mbuzz_debris` | 0x406F20 | SpaceSimulation.ManeuverBuzzDebris | done | 3 |
| `Mstrafe_enemy` | 0x406FB0 | SpaceSimulation.ManeuverStrafeEnemy | done | 3 |
| `Mbest_strafe` | 0x407030 | SpaceSimulation.ManeuverBestStrafe | done | 3 |
| `Msit_n_fire` | 0x407060 | SpaceSimulation.ManeuverSitNFire | done | 3 |
| `Mstrafe_n_roll` | 0x4070D0 | SpaceSimulation.ManeuverStrafeNRoll | done | 3 |
| `Mkill_missile` | 0x407100 | SpaceSimulation.ManeuverKillMissile | done | 3 |
| `Msuicide_run` | 0x4071B0 | SpaceSimulation.ManeuverSuicideRun | done | 3 |
| `Mget_distance` | 0x4071E0 | SpaceSimulation.ManeuverGetDistance | done | 3 |
| `general_zig` | 0x407270 | SpaceSimulation.GeneralZig | done | 3 |
| `Mzig_zag` | 0x407350 | SpaceSimulation.ManeuverZigZag | done | 3 |
| `Mzig_zag_pitch` | 0x407370 | SpaceSimulation.ManeuverZigZagPitch | done | 3 |
| `Mcorkscrew` | 0x407390 | SpaceSimulation.ManeuverCorkscrew | done | 3 |
| `Mveer_away` | 0x407450 | SpaceSimulation.ManeuverVeerAway | done | 3 |
| `ShipAiState44` | 0x407560 | SpaceSimulation.ManeuverResetStress | done | 3 |
| `Mtarget_laser` | 0x407580 | SpaceSimulation.ManeuverTargetLaser | done | 3 |
| `Mrout_me` | 0x4075A0 | SpaceSimulation.ManeuverRoutMe | done | 3 |
| `Mnone` | 0x4075B0 | DispatchManeuver (cases 0 and 1: nothing) | done | 3 |
| `Mreset` | 0x4075C0 | DispatchManeuver (cases 3 and 46: ManeuverComplete) | done | 3 |
| `perform_maneuver` | 0x4075D0 | SpaceSimulation.PerformManeuver | done | 3 |
| `cruise_home` | 0x409760 | SpaceSimulation.CruiseHome | done | 3 |
| `fail` | 0x4098C0 | SpaceSimulation.Fail | done | 3 |
| `coming_home` | 0x4098D0 | SpaceSimulation.ComingHome | done | 3 |
| `run_away` | 0x4099C0 | SpaceSimulation.RunAway | done | 3 |
| `check_engage_target` | 0x409AC0 | SpaceSimulation.CheckEngageTarget | done | 3 |
| `check_destroy_target` | 0x409B10 | SpaceSimulation.CheckDestroyTarget | done | 3 |
| `maneuvering` | 0x409C20 | SpaceSimulation.Maneuvering | done | 3 |
| `formation_burst` | 0x409C50 | SpaceSimulation.FormationBurst | done | 3 |
| `disallow_engage` | 0x409CE0 | SpaceSimulation.DisallowEngage | done | 3 |
| `allow_engage` | 0x409CF0 | SpaceSimulation.AllowEngage | done | 3 |
| `try2allow_engage` | 0x409D10 | SpaceSimulation.Try2AllowEngage | done | 3 |
| `imperial_formation` | 0x409D60 | SpaceSimulation.ImperialFormation | done | 3 |
| `formation_break` | 0x409F00 | SpaceSimulation.FormationBreak | done | 3 |
| `imperial_wingman` | 0x409F80 | SpaceSimulation.ImperialWingman | done | 3 |
| `kilrathi_wingman` | 0x40A030 | SpaceSimulation.KilrathiWingman | done | 3 |
| `wingman_mission` | 0x40A130 | SpaceSimulation.WingmanMission | done | 3 |
| `dist_from_home` | 0x40A160 | SpaceSimulation.DistFromHome | done | 3 |
| `scan_and_lock` | 0x40A180 | SpaceSimulation.ScanAndLock | done | 3 |
| `patrol_area` | 0x40A1C0 | SpaceSimulation.PatrolArea | done | 3 |
| `kilrathi_patrol` | 0x40A360 | SpaceSimulation.KilrathiPatrol | done | 3 |
| `imperial_wingleader` | 0x40A400 | SpaceSimulation.ImperialWingleader | done | 3 |
| `cruise_to_destination` | 0x40A410 | SpaceSimulation.CruiseToDestination | done | 3 |
| `prepare_for_jump` | 0x40A540 | SpaceSimulation.PrepareForJump | done | 3 |
| `accelerate_and_jump` | 0x40A630 | SpaceSimulation.AccelerateAndJump | done | 3 |
| `reach_warp` | 0x40A670 | SpaceSimulation.ReachWarp | done | 3 |
| `warp_arrival` | 0x40A710 | SpaceSimulation.WarpArrival | done | 3 |
| `return_to_buddy` | 0x40A740 | SpaceSimulation.ReturnToBuddy | done | 3 |
| `escort_buddy` | 0x40A7A0 | SpaceSimulation.EscortBuddy | done | 3 |
| `escort_mission` | 0x40A7D0 | SpaceSimulation.EscortMission | done | 3 |
| `check_goal` | 0x40A900 | SpaceSimulation.CheckGoal | done | 3 |
| `streak_toward` | 0x40A940 | SpaceSimulation.StreakToward | done | 3 |
| `approach_and_engage` | 0x40A9B0 | SpaceSimulation.ApproachAndEngage | done | 3 |
| `strike_mission` | 0x40AAC0 | SpaceSimulation.StrikeMission | done | 3 |
| `return_to_master` | 0x40ABB0 | SpaceSimulation.ReturnToMaster | done | 3 |
| `defend_mission` | 0x40AC00 | SpaceSimulation.DefendMission | done | 3 |
| `rendezvous_mission` | 0x40AD80 | SpaceSimulation.RendezvousMission | done | 3 |
| `ship_intelligence` | 0x40AE80 | SpaceSimulation.ShipIntelligence | done | 3 |
| `orbit_sphere` | 0x40AF70 | SpaceSimulation.OrbitSphere | done | 3 |
| `tanker_intelligence` | 0x40B010 | SpaceSimulation.TankerIntelligence | done | 3 |
| `destroyer_intelligence` | 0x40B0C0 | SpaceSimulation.DestroyerIntelligence | done | 3 |
| `stationary_intelligence` | 0x40B110 | SpaceSimulation.StationaryIntelligence | done | 3 |
| `capital_ship_intelligence` | 0x40B140 | SpaceSimulation.CapitalShipIntelligence | done | 3 |
| `futurion_intelligence` | 0x40B320 | SpaceSimulation.FuturionIntelligence | done | 2 |
| `mine_intelligence` | 0x40B3A0 | SpaceSimulation.MineIntelligence | done | 2 |
| `heat_seeking_missile_intelligence` | 0x40B430 | SpaceSimulation.HeatSeekingMissileIntelligence | done | 2 |
| `FF_missile_intelligence` | 0x40B570 | SpaceSimulation.FfMissileIntelligence | done | 2 |
| `set_sphere_point` | 0x40B670 | SpaceSimulation.SetSpherePoint | done | 1 |
| `is_alive` | 0x40B6A0 | SpaceSimulation.IsAlive | done | 1 |
| `check_futurion` | 0x40B700 | SpaceSimulation.CheckFuturion | done | 1 |
| `init_mission` | 0x40B730 | SpaceSimulation.InitMission | done | 1 |
| `prepare_mission` | 0x40B7A0 | SpaceSimulation.PrepareMission | done | 1 |
| `release_all_capital_ship_shapes` | 0x40B940 | SpaceSimulation.ReleaseAllCapitalShipShapes | done | 1 |
| `release_capital_ship_shapes` | 0x40B990 | SpaceSimulation.ReleaseCapitalShipShapes | done | 1 |
| `load_ship` | 0x40B9F0 | SpaceSimulation.LoadShip | done | 1 |
| `free_ship` | 0x40BC70 | SpaceSimulation.FreeShip | done | 1 |
| `free_all_slots` | 0x40BE20 | SpaceSimulation.FreeAllSlots | done | 1 |
| `load_all_slots` | 0x40BE60 | SpaceSimulation.LoadAllSlots | done | 1 |
| `remove_nav_point_objects` | 0x40BEA0 | SpaceSimulation.RemoveNavPointObjects | done | 1 |
| `get_shape_slot` | 0x40BEC0 | SpaceSimulation.GetShapeSlot | done | 1 |
| `shape_loaded` | 0x40BEF0 | SpaceSimulation.ShapeLoaded | done | 1 |
| `shape_needed` | 0x40BF20 | SpaceSimulation.ShapeNeeded | done | 1 |
| `new_sphere_shapes` | 0x40BF50 | SpaceSimulation.NewSphereShapes | done | 1 |
| `set_up_action_sphere` | 0x40BFF0 | SpaceSimulation.SetUpActionSphere | done | 1 |
| `free_pilot_talk` | 0x40C150 | — | todo (Game owns it) | Game |
| `get_pilot_talk` | 0x40C1C0 | — | todo (Game owns it) | Game |
| `init_personalities` | 0x40C2B0 | — | todo (Game owns it) | Game |
| `room_for_me` | 0x40C350 | SpaceSimulation.RoomForMe | done | 1 |
| `approve_xyz` | 0x40C360 | SpaceSimulation.ApproveXyz | done | 1 |
| `set_up_next_wave` | 0x40C3C0 | SpaceSimulation.SetUpNextWave | done | 1 |
| `sub_int_vector` | 0x40C4A0 | ShortVector.Subtract | done | 1 |
| `set_formation_position` | 0x40C4E0 | SpaceSimulation.SetFormationPosition | done | 1 |
| `Set_up_ship_info` | 0x40C5E0 | SpaceSimulation.SetUpShipInfo | done | 1 |
| `is_team_member` | 0x40C740 | SpaceSimulation.IsTeamMember | done | 1 |
| `find_next_ship_turn_slot` | 0x40C780 | SpaceSimulation.FindNextShipTurnSlot | done | 1 |
| `init_ship` | 0x40C800 | SpaceSimulation.InitShip | done | 1 |
| `init_intelligence_data` | 0x40C950 | SpaceSimulation.InitIntelligenceData | done | 1 |
| `SetNavMapCoordinateScaling` | 0x40CBB0 | SpaceSimulation.NavMapCoordinateScaling (field) | done | 1 |
| `ScaleNavMapMarkerSize` | 0x40CBC0 | SpaceSimulation.ScaleNavMapMarkerSize | done | 1 |
| `ScaleNavMapCoordinates` | 0x40CBE0 | SpaceSimulation.ScaleNavMapCoordinates | done | 1 |
| `nav_getxy` | 0x40CC30 | SpaceSimulation.NavGetXY | done | 1 |
| `CheckPoint` | 0x40CC80 | SpaceSimulation.CheckPoint | done | 1 |
| `IncludeNavMapWorldPoint` | 0x40CCF0 | SpaceSimulation.IncludeNavMapWorldPoint | done | 1 |
| `SetScale` | 0x40CD30 | SpaceSimulation.SetScale | done | 1 |
| `Build_objective_list` | 0x40CED0 | SpaceSimulation.BuildObjectiveList | done | 1 |

### auto.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `visit_the_cinema` | 0x403E50 | — | todo (Game owns it) | Game |
| `player_wingman` | 0x403EE0 | SpaceSimulation.PlayerWingman | done | 3 |
| `set_speed` | 0x403F10 | SpaceSimulation.SetSpeed | done | 3 |
| `auto_position` | 0x403F40 | SpaceSimulation.AutoPosition | done | 3 |
| `auto_pilot_sequence` | 0x404050 | BeginAutopilot / AutopilotTravelStep / AutopilotTravel / EndAutopilot (the cinematic and the final force_view are UI) | done | 3 |

### mono.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `advance_canned_sequence` | 0x403A80 | SpaceSimulation.AdvanceCannedSequence | done | 1 |
| `update_canned_sequence` | 0x403B70 | SpaceSimulation.UpdateCannedSequence | done | 2 |

### cmpgn.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `ejection_sequence` | 0x4046A0 | — | todo (Game owns it) | Game |
| `stranded_sequence` | 0x404BE0 | — | todo (Game owns it) | Game |
| `LoadMissionData` | 0x4059B0 | Missions.MissionModule.GetMission + SpaceSimulation.LoadMissionData | done | 1 |

### winmain.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `easy2see` | 0x401040 | SpaceSimulation.Easy2See (sprite bounds through IShapeBounds) | done | 2 |
| `make_shard` | 0x4010C0 | SpaceSimulation.MakeShard | done | 2 |
| `remove_hazard` | 0x4011D0 | SpaceSimulation.RemoveHazard | done | 1 |
| `remove_all_hazards` | 0x401210 | SpaceSimulation.RemoveAllHazards | done | 1 |
| `difficulty` | 0x401250 | SpaceSimulation.Difficulty | done | 2 |
| `asteroid_velocity` | 0x401270 | SpaceSimulation.AsteroidVelocity | done | 2 |
| `skew_randomly` | 0x401290 | SpaceSimulation.SkewRandomly | done | 2 |
| `align` | 0x401390 | SpaceSimulation.Align (low 16 bits of a component) | done | 2 |
| `init_hazard` | 0x4013B0 | SpaceSimulation.InitHazard | done | 2 |
| `near_field` | 0x401680 | SpaceSimulation.NearField | done | 2 |
| `within_field` | 0x4016A0 | SpaceSimulation.WithinField | done | 2 |
| `try_far_spot` | 0x4016C0 | SpaceSimulation.TryFarSpot | done | 2 |
| `rear_sphere` | 0x401870 | SpaceSimulation.RearSphere | done | 2 |
| `ok_hazard_spot` | 0x401890 | SpaceSimulation.OkHazardSpot | done | 2 |
| `make_hazard` | 0x4018D0 | SpaceSimulation.MakeHazard | done | 2 |
| `extra_hazard` | 0x401930 | SpaceSimulation.ExtraHazard | done | 2 |
| `approach` | 0x401950 | SpaceSimulation.Approach | done | 2 |
| `manage_hazard` | 0x4019E0 | SpaceSimulation.ManageHazard | done | 2 |
| `match_ship_to_eye` | 0x401A60 | SpaceSimulation.MatchShipToEye | done | 2 |
| `update_hazards` | 0x401B30 | SpaceSimulation.UpdateHazards | done | 2 |
| `start_hazard_field` | 0x401BC0 | SpaceSimulation.StartHazardField | done | 2 |
| `add_hazard_field` | 0x401C00 | SpaceSimulation.AddHazardField | done | 1 |
| `check_hazards` | 0x401C60 | SpaceSimulation.CheckHazards | done | 2 |

### eventmgr.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `sort_object_depth` | 0x436460 | SpaceSimulation.SortObjectDepth | done | 2 |
| `draw_sorted_objects_to_buffer` | 0x436520 | — | todo (Game owns it) | Game |
| `intro_drawbackgroundships` | 0x436650 | — | todo (Game owns it) | Game |
| `IsVectorWithinRange` | 0x436A00 | VectorMath.IsVectorWithinRange | done | 1 |
| `shrink_vector` | 0x436A30 | VectorMath.ShrinkVector | done | 1 |
| `shrink` | 0x436A70 | VectorMath.Shrink | done | 1 |

### main.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `Update_3Space` | 0x427C50 | SpaceSimulation.Update3Space (servicetrack via ISimulationEvents.ServiceTrack) | done | 2 |
| `house_keep` | 0x427D40 | SpaceSimulation.HouseKeep (palette/alarm half via ISimulationEvents.HouseKeepCockpit) | done | 2 |
| `fire_players_lasers` | 0x428480 | SpaceSimulation.FirePlayersLasers | done | 2 |
| `players_flight_dynamics` | 0x4284D0 | SpaceSimulation.PlayersFlightDynamics(pitch, yaw, roll input) | done | 2 |

### hudmsg.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `GetShipDistanceToNavPoint` | 0x42A0E0 | SpaceSimulation.GetShipDistanceToNavPoint | done | 1 |
| `FindNearestNavPoint` | 0x42A120 | SpaceSimulation.FindNearestNavPoint | done | 1 |
| `ReleaseStaleNavTarget` | 0x42A170 | SpaceSimulation.ReleaseStaleNavTarget | done | 1 |
| `RunSpaceFlight` | 0x42A190 | — | todo (Game owns it) | Game |
| `calculate_damage_level` | 0x42A520 | SpaceSimulation.CalculateDamageLevel | done | 2 |
| `find_objective` | 0x42A8F0 | SpaceSimulation.FindObjective | done | 3 |
| `arrive_from_warp` | 0x42A950 | SpaceSimulation.ArriveFromWarp | done | 3 |
| `unwarp` | 0x42AA10 | SpaceSimulation.Unwarp (flash via ISimulationEvents.SpaceBufferFlash) | done | 3 |
| `warp` | 0x42AAF0 | SpaceSimulation.Warp (flash via ISimulationEvents.SpaceBufferFlash) | done | 3 |
| `drop_player_mine` | 0x42ABD0 | SpaceSimulation.DropPlayerMine | done | 2 |
| `personality_killed` | 0x42AC50 | SpaceSimulation.PersonalityKilled | done | 2 |
| `clean_up_cockpit` | 0x42ACC0 | SpaceSimulation.CleanUpCockpit | partial: HUD part via ISimulationEvents.ClearHudGunReadouts | 1 |
| `find_next_gun` | 0x42AD00 | SpaceSimulation.FindNextGun | done | 1 |
| `select_guns` | 0x42ADA0 | SpaceSimulation.SelectGuns | done | 1 |
| `select_new_gun` | 0x42AE10 | SpaceSimulation.SelectNewGun | done | 1 |
| `select_new_release_weapon` | 0x42AE50 | SpaceSimulation.SelectNewReleaseWeapon | done | 1 |

### cockpt.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `kilrathi_near` | 0x414300 | SpaceSimulation.KilrathiNear | done | 2 |
| `auto_pilot_valid` | 0x414380 | SpaceSimulation.AutoPilotValid (message via ISimulationEvents.ShowCockpitMessage) | done | 3 |
| `malf` | 0x414AF0 | SpaceSimulation.Malf | done | 2 |
| `damage_your_component` | 0x414BF0 | SpaceSimulation.DamageYourComponent | done | 2 |
| `RemovePlayerReleaseWeapon` | 0x414CB0 | SpaceSimulation.RemovePlayerReleaseWeapon (display via ISimulationEvents.PlayerReleaseWeaponLaunched) | done | 2 |
| `sighted` | 0x415050 | SpaceSimulation.Sighted | done | 1 |
| `visited` | 0x415070 | SpaceSimulation.Visited | done | 1 |
| `achieved` | 0x415090 | SpaceSimulation.Achieved | done | 1 |
| `flag_objective` | 0x4150B0 | SpaceSimulation.FlagObjective | done | 1 |
| `hidden_objective` | 0x4151F0 | SpaceSimulation.HiddenObjective | done | 1 |
| `set_new_objective` | 0x4152C0 | SpaceSimulation.SetNewObjective | done | 1 |
| `cycle_next_objective` | 0x415370 | SpaceSimulation.CycleNextObjective | done | 1 |
| `set_next_destination` | 0x4153D0 | SpaceSimulation.SetNextDestination | done | 1 |
| `LocateMobileObjective` | 0x415470 | SpaceSimulation.LocateMobileObjective | done | 1 |
| `someone_coming` | 0x4154C0 | SpaceSimulation.SomeoneComing | done | 3 |
| `escorting_a_ship` | 0x415510 | SpaceSimulation.EscortingAShip | done | 3 |
| `flag_reached` | 0x415530 | SpaceSimulation.FlagReached (messages via ISimulationEvents.ShowCockpitMessage) | done | 3 |
| `check_sighting` | 0x4156D0 | SpaceSimulation.CheckSighting | done | 3 |
| `check_visit` | 0x415720 | SpaceSimulation.CheckVisit | done | 3 |
| `update_objective_location` | 0x415770 | SpaceSimulation.UpdateObjectiveLocation | done | 3 |
| `objective_lost` | 0x415850 | SpaceSimulation.ObjectiveLost | done | 3 |
| `check_objectives` | 0x4158A0 | SpaceSimulation.CheckObjectives (DrawCalculatingLabel is UI) | done | 3 |
| `mobile_objective` | 0x415A30 | SpaceSimulation.MobileObjective | done | 1 |
| `set_objective_range` | 0x415B70 | SpaceSimulation.SetObjectiveRange | partial: range only; scanner marker is cockpit drawing (Game) | 1 |
| `start_lock` | 0x415FC0 | SpaceSimulation.StartLock | done | 2 |
| `starting_lock` | 0x415FF0 | SpaceSimulation.StartingLock | done | 2 |
| `lock_off` | 0x416010 | SpaceSimulation.LockOff | done | 2 |
| `CheckTargetLockMalfunction` | 0x416040 | SpaceSimulation.CheckTargetLockMalfunction | done | 2 |
| `decrement_lock_time` | 0x416090 | SpaceSimulation.DecrementLockTime | done | 2 |
| `target_locking` | 0x416120 | SpaceSimulation.TargetLocking | done | 2 |
| `remove_nav_pointer` | 0x4168A0 | SpaceSimulation.RemoveNavPointer | done | 2 |
| `draw_nav_pointer` | 0x4168C0 | SpaceSimulation.DrawNavPointer (occupies an effect slot while the right VDU shows navigation: gameplay-visible) | done | 2 |
| `build_your_target_list` | 0x416E90 | SpaceSimulation.BuildYourTargetList | done | 2 |
| `cycle_onscreen_targets` | 0x416F30 | SpaceSimulation.CycleOnscreenTargets | done | 2 |
| `check_target` | 0x416FD0 | SpaceSimulation.CheckTarget | done | 2 |
| `send_message` | 0x417420 | SpaceSimulation.SendMessage (queues the message; npc_communication shows it, phase 3) | done | 2 |
| `npc_communication` | 0x4174F0 | — | todo (Game.Flight owns it, ADR-012; reads Ships[].WingmanMessageState) | Game |
| `check_stranded` | 0x417B30 | SpaceSimulation.CheckStranded | done | 2 |

### music.c (scripted camera; the rest of music.c is Audio/Game, see docs/progress/audio.md)

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `parse_view_script` | 0x42CDB0 | SpaceSimulation.ParseViewScript | done | 2 |
| `update_scripted_view` | 0x42D1C0 | SpaceSimulation.UpdateScriptedView | done | 2 |
| `initialize_scripted_view` | 0x42D230 | SpaceSimulation.InitializeScriptedView(short[] script, int start) | done | 2 |

### screen.c

| C function | Address | C# member | Status | Phase |
| --- | --- | --- | --- | --- |
| `cleanup_objectives` | 0x42EFC0 | SpaceSimulation.CleanupObjectives | done | 3 |
| `too_busy` | 0x42F1F0 | SpaceSimulation.TooBusy | done | 3 |
| `reply` | 0x42F210 | SpaceSimulation.Reply | done | 3 |
| `disobey_formation` | 0x42F240 | SpaceSimulation.DisobeyFormation | done | 3 |
| `bad_target` | 0x42F270 | SpaceSimulation.BadTarget | done | 3 |
| `can_land` | 0x42F2B0 | SpaceSimulation.CanLand | done | 3 |
| `i_wanna_rout` | 0x42F350 | SpaceSimulation.IWannaRout | done | 3 |
| `request` | 0x42F3F0 | SpaceSimulation.Request | done | 3 |
| `CreateCannedSceneObject` | 0x42FB40 | SpaceSimulation.CreateCannedSceneObject | done | 1 |
| `wingman_dead` | 0x430E10 | SpaceSimulation.WingmanDead | done | 3 |
| `have_target` | 0x430E30 | SpaceSimulation.HaveTarget | done | 3 |

### Functions of these files owned by other subsystems (not listed above)

- **mathfp.c**: `SetTextCursor`, `SetTextContext`, `WaitForVerticalBlankThunk`, `IdentityHandle`, `SetWholePaletteFromTriplets`, `ReadWord`, `GetFontCharWidth`, `ReleaseVideoResourcesHook`, `GetShapeFrameBounds`, `IsPointInRect`, `SplitPackedPoint`, `DrawTextString`, `DrawTextCharacter`, `AppendTextCharacter`, `MeasureShapeFrameStorage`, `ResetTextCursor`
- **mathutil.c**: `FreePacketAndClear`
- **geom.c**: `MeasureTextPixelWidthClamped`, `SeekPacketSection`, `GetMusicDriverPresent`, `CollectActivePaletteIndices`, `IsPairEqualityDifferentFromFlag`, `InitializeModalTextPanel`, `DrawModalTextPanel`, `RestoreModalTextPanel`, `ShowModalTextPanel`, `ReleaseModalTextPanel`, `AnySavedGames`
- **disk.c**: `ReportPacketLoadError`, `LoadPacketIntoBuffer`, `LoadPacketAllocated`, `FetchDiskPacketRetrying`, `InitializeTextContextFromFont`, `ReleaseTextFont`, `DrawTextAt`, `SortSignedByteValuesAscending`, `OpenDiskDataFile`, `PromptInsertNumberedDisk`, `GetZeroUnused`, `CheckEscaped`, `WaitForInputKey`, `WaitForSceneAdvance`, `MoveMenuPointerFromKeyboard`, `EraseLastTextInputCharacter`, `WaitForStreamInputKey`
- **spc.c**: `CalibrateJoystickInteractive`, `WaitForJoystickButtonRelease`, `WaitForJoystickButtonPress`
- **logic.c**: `LoadGamePaletteFile`, `EMShutDown`, `InitializeEventManagerResources`, `EMStartUp`, `LoadOriginFxDrivers`, `InitializeGameTextContexts`, `GetFxDriverInitResult`, `GetMessagePumpResult`, `GetFxDriverStatus`, `HasSpeechBuffer`, `ResetCockpitPaletteEntries`, `initialize_cockpit`, `init_vdus`, `InitializeCockpitResources`, `free_cockpit`, `init_inflight_music`, `free_inflight_music`, `PreloadMusicTrackHook`, `ReleaseMusicTrackHook`, `LoadSceneAnimationResources`, `ReleaseSceneAnimationResources`, `FindSceneAnimationCommand`, `SceneAnimationGoalReached`, `UpdateSceneAnimationObject`, `PlaySceneAnimation`
- **brains.c**: `GetShapeFrameExtent`, `AnimateScrambleWalk`, `PlayScrambleHangarScene`, `DrawScrambleActor`, `ConfigureScrambleActor`, `DrawScrambleFrame`, `scramble`, `landing`, `funeral_player`, `funeral_wingman`, `funeral_sequence`, `RunAnimationDemoLoop`, `SampleBothJoysticks`, `SampleJoystickDevice`, `SampleActiveJoystickDevice`, `DrawNavTextLine`
- **mono.c**: `CloseDataFile`, `WriteDataFileAtOffset`, `CreateDataFile`, `ReadDataFileAtOffset`, `SeekDataFile`, `MeasureScaledIntroTextWidth`, `DrawCenteredScaledIntroText`, `GetLineLength`, `print_subtitle`, `SplitGameClockTicks`, `MonoDebug_install`, `MonoDebug_remove`, `SoundDebugPrintf`, `MonoDebug_print`, `ReadPerformanceCounter`, `ResetStringBuilder`
- **cmpgn.c**: `LoadPaletteTripletsFile`, `ParseFaceAnimation`, `ParseMouthAnimation`, `AddPCName`, `LoadFace`, `LongTalk`, `CloseTalk`, `Briefing`, `DeBriefing`, `Office`, `LoadBriefingData`, `UpdateMap`, `CloseLook`
- **winmain.c**: `SaveGamePalette`, `RestoreGamePalette`, `WarpMouseTo`, `CheckLauncherAndConfig`, `WinMain`, `ShutdownGameWindow`, `ShowNoticeMessageBox`, `AbortToDesktop`, `CreateMainWindow`, `PumpWindowMessages`, `GetF1KeyLatch`, `MainWindowProc`, `GetJoystickPosition`, `GetJoystickButtons`, `GetJoystickDevCaps`, `GetApplicationInstance`, `GetMainWindowHandle`, `GetMainWindowDeviceContext`, `AllocateGuardedMemory`, `ReportHeapGuardCorruption`, `CheckAllGuardedAllocations`, `FreeGuardedAllocation`
- **eventmgr.c**: `TranslatePolledInputEvent`, `QueueInputEventAtCursor`, `AllocateInputEvent`, `ReleaseInputEvent`, `QueueInputEvent`, `ReleaseInputEventQueue`, `RetainInputEventsOfType`, `RemoveInputEvent`, `GetNextInputEvent`, `PollInputEvent`, `PeekInputEvent`, `IsInputEventQueued`, `FlushInputEvents`, `ResetAllocationDepth`, `CheckCursor`, `CaptureMouseCursorBackground`, `DrawMouseCursor`, `RestoreMouseCursorBackground`, `RefreshMouseCursorDisplay`, `EnterAllocationScope`, `LeaveAllocationScope`, `SetMouseCursorShape`, `SetMouseHomePosition`, `ApplyPackedMousePosition`, `SetFrameTimerPeriod`, `SetFrameTimerAndWait`, `SetFrameTimerPeriodDirect`, `WaitForFrameTick`, `IsFrameTickElapsed`, `GetSoundHardwareFlag`, `TimerResetHook`, `GetVideoReleaseResult`, `IdentityWord`, `TimerStopHook`, `GetFixedOneMillion`, `GetFixedOneMillionAlt`, `ClearInputKeyStatePreservingModifiers`, `ClearInputKeyState`, `SetInputKeyState`, `set_up_screen_viewport`, `MouseIdleHook`, `GetNavRangeSentinel`, `GetOriginalFreeMemory`, `StartupHook`, `JoystickEdgeHook`, `FreeIfNotNull`, `GetStartupErrorCode`, `ShutdownHook`, `SelectDiskDriveHook`, `GetCurrentDiskDriveHook`, `GetShutdownErrorCode`, `VideoReleaseHook`, `ExitCleanupHook`, `FillGraphicSuffix`, `ConvertChar_Int`
- **main.c**: `SDL_PORT`, `GetScreenUpdateFlag`, `initialize_view_buffer`, `dump_buffer_to_screen`, `clear_view_buffer`, `InitializeConversationViewport`, `ResetScreenClipToFullHeight`, `InitializeConversationText`, `RefreshMemoryStatusOverlay`, `TriggerPlayerHitPaletteFlash`, `FadeFlightPaletteEntry`, `UpdateSpacePaletteFade`, `init_player_input`, `get_player_input`, `process_player_input`, `player_input`, `SelectNextExternalViewObject`, `SelectPreviousExternalViewObject`, `HandleFleetOverviewInput`
- **hudmsg.c**: `MeasureMessageWidth`, `WaitForKeyAcknowledge`, `ShowModalMessage`, `ReportOutOfMemoryAndExit`, `ShowOnScreenMessage`, `ShowGamePausedBanner`, `ShowVersionBanner`, `SetMessageDisplaySpeed`, `ReportFramesSkipped`, `HandleSpaceFlightControls`, `Draw_3Space_Frame`, `GetArcadeBonus`, `FigureArcadeTime`, `DrawArcadeScorePanel`, `UpdateArcadeScoreDisplay`, `RenderSpaceViewFrame`, `RefreshCockpitStatus`, `UpdateTrainSimMenuCursor`, `ResetMouseCursorFrame`, `UpdateRoomMenuCursor`, `FadeViewportPaletteToColour`, `WaitForDebugStep`, `FrameTimerCallback`, `SetMultimediaTimerCallback`
- **cockpt.c**: `EmitTextString`, `FormatTextTokens`, `DrawFormattedText`, `FormatTextBufferFromStart`, `AppendFormattedText`, `FatalErrorAndExit`, `IsCockpitExplosionActive`, `EraseCockpitReadoutRegion`, `vdu_polygon`, `InitializeCockpitReadout`, `DrawCockpitReadout`, `EraseCockpitReadoutAtPosition`, `DrawHudMessageSlot`, `ClearHudMessageSlot`, `ClearHudMessageIfMatching`, `ClearHudGunReadouts`, `SetHudMessageSlot`, `UpdateMessage`, `set_global_message`, `CockpitMessage`, `remove_message`, `reset_cockpit`, `SetCockpitLightBlink`, `draw_cockpit_lights`, `update_lights`, `update_bars`, `get_mode`, `set_mode`, `SetVduModeIfChanged`, `GetVduModeStackDepth`, `push_mode`, `pop_mode`, `set_new_vdu`, `update_vid_disp`, `InvalidateVduMode`, `clear_message_time`, `message_showing`, `set_message_time`, `check_message`, `update_digital_readouts`, `PlayTargetLockSfx`, `malf_sound`, `vdu_malf`, `ShowComponentHitHudMessage`, `fire_computer_graphic_missile`, `show_weapon_disp`, `update_status_text`, `DrawCalculatingLabel`, `objective_name`, `show_navigation_disp`, `rotational_pos_to_scanner_pos`, `ResetScannerContacts`, `clear_head_up_display`, `get_color`, `draw_3d_scanner`, `SetRectBounds`, `GetRectHeight`, `print_message_text`, `ShowHudTextLine`, `SetHudTextColour`, `draw_target_box`, `overlay_head_up_display`, `RestoreCockpitExplosionIfVisible`, `RestoreTransientCockpitGraphics`, `SetHudMessageText`, `malf_noise`, `update_missile_warning`, `determine_pilot_hand`, `DrawPilotHandFrame`, `CopyTrainSimPilotViewToRightVdu`, `animate_pilot`, `ResetPilotHandAnimation`, `clear_cockpit_damage`, `explosion_draw`, `DrawPendingCockpitDamage`, `RestoreCockpitExplosionBackground`, `cockpit_explosion`, `place_damage_on_cockpit`, `vid_transmit`, `vid_equiv`, `update_dead_disp`, `update_VDUs`, `update_cockpit`, `PlayCockpitSelectionSfx`, `vdu_pop_all`, `SelectCockpitVduMode`
- **screen.c**: `ShouldSuspendCursorForRect`, `InitializeDIBScreenViewport`, `InitFullScreenViewport`, `GetPacketSize`, `GetFreeNearHeapBytes`, `FrameStartHook`, `IsSoundHardwarePresent`, `MessagePumpHook`, `PushMemoryStackFrame`, `IsPushedPacketHandle`, `MapPacketHandleToBlock`, `AllocateTaggedMemory`, `ReleasePacketHandle`, `GetFixedOneMillionThunk`, `GetFixedOneMillionThunkAlt`, `ShowCampaignVictorySequence`, `ShowTigerClawEscapeScene`, `ShowTheEndScreen`, `UpdateInputDeviceTransitions`, `PollJoystickButtonEvents`, `PollMenuInputDevices`, `get_face`, `LoadCommPortraitShape`, `ResetCommMenuChoices`, `IsCommMenuIdle`, `AppendCommMenuChoice`, `SendCommMenuChoice`, `OpenCommMenuForTarget`, `IsCommChoiceMenuOpen`, `GetPendingMenuAction`, `SetPendingMenuAction`, `OpenCommRecipientMenu`, `CloseCommChoiceMenu`, `CanOpenCommMenu`, `SelectCommRecipient`, `BuildCommunicationRecipientMenu`, `BuildCommunicationCommandMenu`, `RefreshCommunicationMenu`, `HandleCommunicationMenuRequest`, `show_communications_disp`, `Chosen_communicate_option`, `talk_equiv`, `FreeCommDisplayResources`, `EndCommSessionWithWingman`, `EndCommMenu`, `ShowCentredPrompt`, `LoadCommDisplayResources`, `ExpandCommMessageTokens`, `real_vid_transmit`, `ShutdownVideoHook`, `ReserveContiguousPaletteEntries`, `ReleaseContiguousPaletteEntries`, `PrintPaletteAllocationMap`, `LoadJoystickCalibrationFile`, `ReadCalibratedJoystick`, `UnionRectBounds`, `ThrottleFrameAndDrawFps`
