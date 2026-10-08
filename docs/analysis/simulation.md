# Wing Commander 1 — 3D Space Simulation, Math, Objects, Ships, Weapons, AI

Analysis of the C reverse-engineering in `reference/wc1-re` (Kilrathi Saga Win32 build,
with the SDL2 port that also reads DOS data) for a port to C# / .NET 10.

This document is self-contained. Everything below was read out of the reconstructed
sources; function names are the reconstruction's names (most are the original Origin
names recovered from Mac/Amiga symbol tables, e.g. `rotate_about_i`, `ship_intelligence`,
`perform_maneuver`). Numbers in hex are as they appear in the code.

Source map for this subsystem (all paths relative to `reference/wc1-re/`):

| File | Contents |
|---|---|
| `src/mathfp.c` | Fixed-point multiply/divide, trig, magnitude, RNG wrappers |
| `src/mathutil.c` | `MinShort`/`MaxShort`, packet release |
| `src/geom.c` | Vector algebra, i/j/k orientation, rotations, projection (`transform_objects_to_your_view`), direction-sprite selection (`get_right_shape`), collision test, slot allocation, force application |
| `src/spc.c` | Camera (`set_eye_direction_and_position`, `new_view`), dust/star field, `house_keep_objects`, `update_objects_in_space`, `accelerate_and_move_object`, `animate_shape`, `object_collision`, `object_intelligence` |
| `src/disk.c` (tail) | `set_objects_data`, `new_object`, `initialize_ship`, `remove_weapon`, `rotate_object_to_goal`, `match_rotation_goal`, `celerate`, `approach_speed`, target-list sort |
| `src/ship.c` | Damage model (`inflict_damage`, `internal_damage`, `your_internal_damage`), explosions/debris, scoring, `fire`, `fire_weapon`, turrets/flak |
| `src/logic.c` | Weapon selection, afterburner, exhaust/child sprites, shield/energy/fuel housekeeping, alerts, crash prediction, squad/formation helpers, objective/tactic/maneuver setters, target scans, skill checks, ship-shape loading, cockpit/3space init |
| `src/brains.c` | Maneuver handlers (`M*`), `perform_maneuver`, mission-type handlers, capital/missile/mine intelligence, `init_mission`, `prepare_mission`, `init_ship`, `Set_up_ship_info`, `set_up_action_sphere`, `load_ship`, `Build_objective_list`, nav-map scaling |
| `src/smart.c` | Collision avoidance, formation flying, maneuver selection tables (`intelligence_events`, `handle_stress`, `process_maneuver_node`) |
| `src/auto.c` | Autopilot (`auto_pilot_sequence`) |
| `src/hudmsg.c` | `RunSpaceFlight` main loop, key handling, `Draw_3Space_Frame`, warp/unwarp, gun/missile selection |
| `src/cockpt.c` | Cockpit: VDUs, scanner, target lock, target box, nav pointer, objectives, messages |
| `src/nav.c` | Nav map, inflight computer, `GameFlow`, `PostMission`, `UpdateSeries` |
| `src/screen.c` | Comm menu, `request` (wingman orders), objective cleanup, landing permission |
| `src/winmain.c` (head) | Asteroid/mine hazard fields |
| `src/cmpgn.c` | `LoadMissionData` (MODULE packet parser), ejection |
| `src/mono.c` | Canned (scripted) ship sequences |
| `src/eventmgr.c` (tail) | Depth sort, draw list, `IsVectorWithinRange`, `shrink_vector` |
| `src/main.c` | `Update_3Space`, `house_keep`, player input → flight dynamics |
| `src/globals.c` | `aObjectTypeData` (the ship/weapon stat table is compiled into the EXE), formation tables, maneuver tables, hardpoint offsets, animation scripts |
| `include/globals.h`, `include/wcdata.h` | All per-object arrays and record layouts |

---

## 1. Numeric conventions

### 1.1 Fixed point: 24.8 in a 32-bit `int`

* `FixedVector { int x, y, z; }` — every component is **signed 32-bit with 8 fraction bits**
  (`0x100` = 1.0). Positions, velocities, orientation basis vectors and most intermediate
  values use this format. World units: one unit ≈ one metre (ranges printed on the HUD as
  `asObjectDistance` "m" are `fixed >> 8`).
* Integer ↔ fixed: `<< 8` / `* 0x100` to make fixed, `>> 8` (arithmetic, floors) to read
  the integer part. `FixedToShortSaturating(v)`: clamps to ±0x7fff after `>> 8`
  (`v < -0x7fff00 → -0x7fff`, `v > 0x7fff00 → 0x7fff`).
* `SignFixed(v)` returns `0x100`, `0`, or `0xffffff00` (i.e. −1.0 fixed).
* The original DOS game was 16-bit C; most game state is `short`. The Win32 build keeps
  `short` for those (wrapping 16-bit arithmetic is load-bearing in places — see
  `ChooseRandomSignedMagnitude`, `WrapDegrees`, hit-point counters). In C# use `short`
  fields and `unchecked((short)...)` casts where the C code casts.

### 1.2 Multiply / divide are done through the FPU (Kilrathi Saga specific)

These are *not* integer shifts. The KS build implements them with IEEE doubles/floats and
C truncation-toward-zero casts, which differs from integer `>> 8` for negative values and
for large operands. Replicate exactly:

```
MultiplyFixed(int l, int r):  (long)( ((double)l * (1.0/256.0)) * ((double)r * (1.0/256.0)) * 256.0 )
DivideFixed(int n, int d):    nf = (float)((double)n * (1.0/256.0));
                              df = d != 0 ? (float)((double)d * (1.0/256.0)) : 1.0f;
                              (long)( nf / df * 256.0 )        // float32 operands, double result
SinFixed(short deg):          (long)( sin(deg * DEGREES_TO_RADIANS) * 256.0 )
CosFixed(short deg):          (long)( cos(deg * DEGREES_TO_RADIANS) * 256.0 )
ArcSin(int v):                (long)( asin((double)v * 0.00390625f) * 57.295779513082323 )   // returns integer DEGREES
ArcCos(int v):                (long)( acos((double)v * 0.00390625f) * 57.295779513082323 )
Magnitude(int v):             (long)( sqrt((double)v * 0.00390625f) * 256.0 )               // fixed sqrt of a fixed value
PlanarMagnitude(x,y):         (long)( sqrt((x/256)^2 + (y/256)^2) * 256.0 )                  // doubles
Vector_magnitude(v):          (long)( sqrt((x/256)^2 + (y/256)^2 + (z/256)^2) * 256.0 )
DEGREES_TO_RADIANS = 0.017453292519943295
```

Notes for exactness:

* All `(long)` casts truncate toward zero (MSVC `_ftol`). `(short)` casts after that wrap.
  **Clarification (2026-10-07, simulation phase 3):** `_ftol` converts with a 64-bit
  `FISTP qword` and returns the low 32 bits, so a result outside the `int` range wraps modulo
  2^32. The 32-bit conversion of x64 code (`cvttsd2si`, e.g. clang/MSVC x64 builds of the SDL
  port) returns `INT_MIN` instead. Core's `FixedMath.Truncate` (`(int)(long)value`) is the `_ftol`
  behaviour. Found by the phase-3 differential cross-check (one AI scenario of 500 overflowed).
* `DivideFixed` rounds both operands to **float32** before dividing (precision loss for
  values ≥ 2^24 / 256). `DivideFixed(x, 0)` divides by 1.0 (returns `x`).
  **Clarification (2026-10-07, simulation phase 2):** the quotient `nf / df` itself is
  computed by the x87 FPU at the precision-control setting, which MSVC's CRT sets to 53 bits:
  a *double* division of the two float32 values (Core `FixedMath.Divide`). A float32 division
  (SSE code from clang/gcc x64, e.g. the SDL port's x64 build) differs by one for large
  operands — 24 of 22,062 lines of the phase-2 cross-check (forces, collision separations)
  until the harness divided in double precision. Confirm against the running KS executable.
* `MultiplyFixed` is exact as long as `l*r` fits in 53 bits; the product is then truncated
  toward zero, i.e. `MultiplyFixed(-3, 0x80) == -1` whereas `(-3*0x80)>>8 == -2`.
* Angles are **integer degrees** everywhere (`short`). `WrapDegrees(short d)`: `d % 360`
  (C remainder, sign of the dividend), then `< -180 → +360`, `> 180 → -360`. Result in
  `[-180, 180]`: **-180 stays -180** (corrected 2026-10-07; the argument is a `short`, so
  wider values are truncated by the call).
* The DOS original used integer trig tables; the KS build replaced them with CRT `sin`.
  The only table left is `awAbsoluteSine/awAbsoluteCosine[360]` (unsigned 16-bit, 256 =
  1.0) used by the sprite-bounds routine `GetTransformedShapeBounds`, and
  `anRLEQuarterCosine[901]` (RLE rotation rasteriser, rendering only). For the port, the
  observable behaviour is the KS one: compute `SinFixed`/`CosFixed` with `Math.Sin` on
  doubles and truncate — .NET's `Math.Sin` is correctly rounded for these inputs on x64 and
  will match MSVC's CRT except possibly at 1 ulp before the ×256 truncation.
  **Correction (2026-10-07, simulation port):** the result is *not* periodic in the
  argument, so an `int[360]` table indexed by `d mod 360` is wrong. The argument is rounded
  to a double before `sin`, and at the multiples of 30° (where `sin × 256` is an integer)
  the truncation depends on that rounding: `SinFixed(30) = 127`, `SinFixed(-30) = -127`,
  `SinFixed(330) = -128`, `SinFixed(390) = 128`, `CosFixed(60) = 128`, `CosFixed(120) = -127`.
  Compute directly from the signed `short` argument (`Simulation.Geometry.FixedTrig`); the
  golden values for every multiple of 30° in [-720, 720] are locked by
  `FixedTrigTests`.

### 1.3 Helpers

* `IsVectorWithinRange(v, range)`: `abs(range << 8) >= Vector_magnitude(v)`.
* `shrink_vector(v)`: repeatedly halves all three components until every component's
  integer part is 0 or −1 and its fraction ≤ 0x0f00 / ≥ 0xf100 — i.e. crude normalisation to
  roughly |c| ≤ 15 without division. Used by `point_at` and chase cam. Replicate bit-exactly
  (`shrink()` on `int` with `/ 2`, which truncates toward zero).
* `NormalizeFixedVector`: divides by `Vector_magnitude` with `DivideFixed`; returns 0 if
  magnitude is 0 (vector untouched).
* `dot_product`: sum of three `MultiplyFixed`. `vector_cross_product`: standard, via
  `MultiplyFixed`.
* `vector_angle(a,b)`: normalises both, returns `((short)dot * 100) / 0x100` — a cosine in
  percent (−100..100). `nFacingToTarget`/`nTargetFacing` are the same "percent cosine"
  (`(short)(((short)dot * 100) >> 8)`).
* `find_ratio(inMin,inMax,in,outMin,outMax)`: clamped linear remap in `int` arithmetic.
* `MinShort/MaxShort/MinInt/MaxInt/AbsInt`.

### 1.4 Random number generator

The game uses the **MSVC CRT `rand()`** (seeded with `srand(time(0))` at start-up in
`WinMain` and `SeedRandomFromClock`), i.e. the 32-bit LCG:

```
seed = seed * 214013 + 2531011;   return (seed >> 16) & 0x7fff;    // 0..32767
```

Wrappers (all `short`):

| Function | Result |
|---|---|
| `RandomBelow(n)` | `rand() % n` (n must be > 0) |
| `RandomInRange(lo, hi)` | `span = hi-lo; if span==0 span=1; lo + rand() % (span+1)` — note span==0 gives `lo + rand()%2`, so `RandomInRange(0,0)` can return 1 |
| `RandomBelowOrEqual(n)` | `n == 0 or -1 → 0`, else `rand() % (n+1)` |
| `signed_random(r)` | `RandomBelowOrEqual(2r) - r` |
| `ChooseRandomSignedMagnitude(lo,hi,neg)` | `v = RandomInRange(lo,hi); if neg && RandomInRange(0,1) v=-v` |
| `MakeRandomVectorFixed(lo,hi,v)` | three `ChooseRandomSignedMagnitude(lo,hi,1) << 8` (x then y then z) |
| `FillFixedVectorWithRandomComponents(limit,v)` | `MakeRandomVectorFixed(0, limit, v)` |
| `random_radial(c, r, out)` | `c + FillFixedVectorWithRandomComponents(RandomBelowOrEqual(r))` |
| `MakeRandomNormalizedVector` | components `RandomInRange(0x40,0xff)` then normalise |
| `rnd_sign(v)` / `rnd_aim(radius,speed,max)` | flak aiming |

The game is deterministic given the seed and the exact sequence/count of calls; the SDL
port deliberately keeps a separate xorshift generator for its display-static effect so as
not to consume game `rand()` calls. The port should implement the LCG explicitly (never
`System.Random`) and route every gameplay random through it; seeding from the clock is what
the original does, but a replay/test mode can seed deterministically.

---

## 2. Orientation, rotation and projection

### 2.1 Basis vectors ("i/j/k")

Each object `o` has three unit vectors (24.8): `aShipRightVector[o]` (i),
`aShipUpVector[o]` (j), `aShipForwardVector[o]` (k). `init_ijk(o)` sets i=(1,0,0),
j=(0,1,0), k=(0,0,1) and clears the three rotation rates. `copy_frame(src,dst)` copies all
three. The coordinate system is left-handed (forward = +z, right = +x, up = +y, screen y
grows downward after projection).

Rotations are applied by rotating two of the vectors about the third:

```
rotate_about_i(angle, j, k):  c=CosFixed, s=SinFixed
    j' = j*c - k*s ;  k' = j*s + k*c          (component-wise, MultiplyFixed)
rotate_about_j(angle, i, k):  i' = k*s + i*c ;  k' = k*c - i*s
rotate_about_k(angle, i, j):  i' = i*c - j*s ;  j' = i*s + j*c
alter_pitch(a,o) = rotate_about_i(a, up[o], fwd[o]); fix_objects_ijk(o)
alter_yaw(a,o)   = rotate_about_j(a, right[o], fwd[o]); fix_objects_ijk(o)
alter_roll(a,o)  = rotate_about_k(a, right[o], up[o]); fix_objects_ijk(o)
fix_objects_ijk(o): right = up × fwd ; up = fwd × right ; normalise right, up, fwd
```

`fix_objects_ijk` re-orthogonalises after every rotation; because it uses `DivideFixed`
(float) the basis drifts in a deterministic way that must be reproduced.

`transform_to_objects_frame(v, out, o)`: `out = (v·right, v·up, v·fwd)` — world → object
local. `point_at(o, p)`: `fwd = shrink_vector(p - pos)`, then `fix_objects_ijk` (up is
*kept*, right is recomputed). `position_relative_ijk(pos, o, r, u, f)`: offsets `pos` by
`r*right + u*up + f*fwd` (each via `NormalizeFixedVector` + `ScaleFixedVector(d, dist<<8)`).

### 2.2 Per-frame rotation rates and goals

* `anObjectPitchRotation/Yaw/Roll[o]` (short, degrees/frame). `rotate_object(o)` applies
  each non-zero rate with `alter_*` and then moves it one degree toward zero
  (`ClampVectorTo30` is actually a decay-by-one; `ClampTo30` clamps to ±30).
* The player writes rates directly from input every frame (`players_flight_dynamics`):
  `pitchRate = yawRate(type) * nPitchInput / 8`, `yawRate = -(pitchRate(type) * nYawInput / 8)`,
  `rollRate = -(rollRate(type) * nRollInput / 8)`, inputs in −9..9 (keyboard ramps, mouse
  thresholds, joystick). **Note the pitch/yaw fields of `ObjectTypeData` are swapped
  relative to their names here and in `rotate_object_to_goal`** (yawRate drives pitch).
* AI/missiles use goals `anYawGoal/anPitchGoal/anRollGoal[o]` (degrees still to turn).
  `rotate_object_to_goal(o)` (ships 1..9 and missiles, called after intelligence):
  `totalError = |yawRot-yawGoal| + |pitchRot-pitchGoal| + |rollRot-rollGoal|`, then
  `match_rotation_goal(&pitchRot, &pitchGoal, totalError, type.yawRate)`,
  `(&yawRot, &yawGoal, ..., type.pitchRate)`, `(&rollRot, &rollGoal, ..., type.rollRate)`.
  `match_rotation_goal` normalises the goal into ±180, computes
  `step = max(1, |rot-goal| * rate / totalError)`, moves `rot` toward the goal by at most
  `step` (sign-aware clamp), then **subtracts the applied rotation from the goal**
  (`goal = max(goal - rot, 0)` or `min(.., 0)`), so goals count down to zero as the
  rotation is consumed. `no_goal(o)` = all three goals zero. `steady_object(o)` zeroes
  goals. `trim_goals(o, n)` clamps yaw/pitch goals to ±n.
* If the special maneuver is `BLOWING_UP` (lost control), `rotate_object_to_goal` skips
  steering (rates keep spinning) unless collision alert bit 1 is set, or the counter has
  expired and `skill_check(o,7)` passes.
* `set_ship_rotation_goals(o, turnRate, dir, pointingMode, &yawGoal, &pitchGoal)`: converts a
  world direction to local; mode 1 (`acShipPointingMode[o]==1`, set for every ship by
  `Set_up_ship_info`) uses `rectangular_to_spherical` (yaw = `ArcCos(z / planar(x,z))`,
  negated if x<0; pitch = `ArcCos(y / r) - 90`), subtracts `turnRate` from the dominant
  axis, and writes `yawGoal = Wrap(-yaw)`, `pitchGoal = Wrap(-pitch)`; mode 0 uses
  `ArcSin` with a behind-you correction (±180). `point_ship(o, 0, dir)`,
  `point_ship_at_point`, `point_ship_at_object`, `point_capital_ship_at_object`
  (reverse direction), `point_ship_behind_object` (500 + radius behind target),
  `point_ship_below_object` (500 + radius above along target's up), `point_perpendicular*`
  (yaw goal ±90 after pointing), `point_parallel` (goal toward other ship's forward).
* `match_roll_orientation(o, ref)`: roll angle between o's up and ref's right/up:
  `ArcCos(normalised(up·refRight, up·refUp).y)`, `360-angle` if x ≥ 0, wrapped.
* The eye (camera) uses `nEyePitchGoal/YawGoal/RollGoal` + `nEye*Rate` and
  `rotate_eye_to_goal`.

### 2.3 Facing/range scratch globals

Many routines leave results in globals that later code reads:

* `get_facing_range_from_point(o, p)`: `vToTarget = p - pos[o]`,
  `nTargetRange = |vToTarget| (saturating short) - 2 * radius[o]` (it subtracts the radius
  twice: once in `distance_from_point` and once more), `vNormalizedToTarget`,
  `nFacingToTarget = percent cosine between o's forward and the target direction`.
* `get_facing_range_from_object(o, other)` = above with `nTargetRange -= radius[other]`,
  `vNormalizedToTarget` negated, and `nTargetFacing = percent cosine between *other's*
  forward and the direction from other to o` (i.e. is the target facing me).
* `ship_vs_point`, `ship_vs_ship` are aliases. `facing_to_object(o, p)` only sets
  `nFacingToTarget`. `distance_from_point(o,p)` = `|p-pos| - radius[o]`,
  `distance_from_object(o,other)` subtracts both radii, `distance_between_points`.
* `nTargetShip`, `nTargetRange` also serve as outputs of `scan_for_enemy`, `any_enemy`,
  `attacker_in_range`, `in_danger`, `select_safe_target`.

### 2.4 Projection (`transform_objects_to_your_view`, geom.c)

Run once per rendered frame for objects 0..60 whose class is not NULL / FIXED_OBJECT and
which are not the nav pointer:

1. `asPreviousObjectDistance = asObjectDistance; asObjectDistance = 0`.
2. FUTURION (hidden pre-warp ship) → not visible (`asObjectScreenX = 0x8001`).
3. `dir = pos[o] - pos[EYE]` (PLANET/STAR: `dir = pos[o]` directly, they are eye-relative
   "sky" objects); `dist = |dir|`.
4. Cull if `dist < radius[EYE] << 8`, if DUST and `dist > 1400 << 8`.
5. `view = transform_to_objects_frame(dir, EYE)`; cull if `view.z < radius[EYE] << 8`;
   cull if `DivideFixed(view.z, dist) < 0x94` (0.578 → half-angle ≈ 54.7°, a square-ish
   field of view).
6. `objR = radius[o] << 8; if dist <= objR: dist = objR + 1`.
7. For class > DUST: `scaleFactor = DivideFixed((W & ~1) << 15, dist - objR)`;
   `asObjectScreenScale = MultiplyFixed(asObjectScale, scaleFactor) >> 8`; clamp
   `> 0x1fff → 0x2000`; if `< 5` cull. (`W = nScreenWidth`, 320 normally; the `<< 15` is
   `(W/2) << 16`, i.e. the sprite scale is `objScale * (W/2) / distance` in 8.8).
8. `asObjectDistance = dist >> 8` (0 means "not visible" to other code).
9. `screenX = DivideFixed(MultiplyFixed((W&~1) << 7, view.x), view.z) >> 8`, same for y —
   a pinhole projection with focal length `W/2` pixels, origin at the view centre
   (`nViewCenterX/Y`, added later in the draw list). Y is *not* flipped: object up (+y)
   maps to screen +y; the cockpit view geometry compensates (see Open questions).
10. PLANET: if `asObjectScreenScale == 0xff` call `set_background_objects_rotation` (roll of
    the sprite from the eye's up vector, using scratch object 63). DUST: frame from
    `(counter + nSpaceFrame) & 3`, streak bits of `asObjectScreenAngle & 0x10`, and a size
    band from `0x900 * (radius[EYE]<<8 / dist)`. MISSILE/SHIP/CAPITAL_SHIP:
    `get_right_shape`.

`get_right_shape(o, dir)` picks the sprite frame and rotation for a 3D object drawn as a
pre-rendered sprite set:

* Builds a frame looking from the object toward the eye (negate `dir`,
  `rectangular_to_spherical`, rotate unit axes by `-yaw` about j and `-pitch` about i),
  expresses that forward and the eye's up in the object's local frame, converts the local
  forward to spherical, then quantises: `pitchBand = pitch/30 + 3` with ±16° hysteresis
  rounding (0..6), `yawSector = (12 - yaw/30) % 12` with the same rounding;
  `directionIndex = 0` (top), `61` (bottom), else `pitchBand*12 + yawSector - 11`
  (62 views total = `DIRECTION_VIEW_COUNT`).
* Sprite roll: `angle = ArcCos(normalised(eyeUpLocal·viewRight[idx], eyeUpLocal·viewUp[idx]).y)`,
  `360 - angle` if x ≥ 0. The 62 `aDirectionView{Right,Up,Forward}Vector` frames are built by
  `initialize_direction_view_frames` (pitch 90, then 60/30/0/−30/−60 × yaw 0..330 step 30,
  then −90).
* Three frame tables of 62 entries (`acDirectionShapeFrame`, `acDirectionShapeFlip`):
  table 0 ships/capitals (frames 0..36, 37 unique sprites), table 1 (`+62`) missiles and
  `OBJECT_TYPE_TURRET`, table 2 (`+124`) `KILRATHI_BASE` (frames 0..16). Frame 0 (top view)
  adds 90° to the roll; frame 36 (bottom) subtracts 90° unless missile.
  `asObjectFlip = flip << 4` (0x10 mirror X, 0x20 mirror Y, 0x30 both). Angle normalised to
  0..359 → `asObjectScreenAngle`.
* Capital ships keep one *shape per frame* (`aapPacketReferences[slot][frame]` or loaded on
  demand from logical file `type + 22`, section = frame); `asCapitalShipViewFrame` tracks
  the loaded frame; `asObjectViewFrame = 0` for them. Fighters use `asObjectViewFrame =
  frame` inside one shape set.

### 2.5 Draw list

`Draw_3Space_Frame` (hudmsg.c), executed only every `nFrameSkip` frames:
`UpdateSpacePaletteFade → transform_objects_to_your_view → update_star_field →
place_exhaust_on_ships → reposition_fixed_child_objects → sort_object_depth →
draw_sorted_objects_to_buffer → overlay_head_up_display (view mode 0 only)`.

* `sort_object_depth`: selection sort of all 64 slots by `(unsigned short)asObjectDistance`
  descending (far first, painter's order), only entries with `asObjectScreenX != 0x8001`
  after the first; output `anSortedObject[64]` terminated by −1. (The first entry is the
  slot with the largest distance whether visible or not, lowest slot on ties; later ties are
  also resolved by the lowest slot.)
* `draw_sorted_objects_to_buffer`: stops at the first entry < 0 *or whose type is negative*;
  for each: STAR/DUST (and PLANET in the original; the SDL
  port routes planets through the scaled path) draw `pConstellationShape` frame
  `asObjectViewFrame` unscaled at `(screenX + nViewCenterX, screenY + nViewCenterY)`; nav
  pointer object uses its own shape. Everything else:
  `DrawSpriteScaled(space buffer, x, y, apObjectShape[o], asObjectViewFrame, asObjectScreenAngle, asObjectScreenScale, asObjectFlip)`.
  `asObjectDrawX/Y` record the final position (used by `intro_drawbackgroundships` to erase).
  In the SDL "enhanced" renderer `SdlRecordSpaceSprite(viewport, floatX, floatY, shape,
  frame, angle, scale, flip)` records the sprite for GL rendering with sub-pixel
  coordinates recomputed from `aObjectViewPosition` (only when the integer projection
  agrees) and returns 0 to fall back to software; thrusters get their own float anchor.
* `GetTransformedShapeBounds(viewport, x, y, shape, frame, angle, scale, flip, bounds)` is
  the bounding box of a rotated/scaled sprite (uses the 16-bit |sin|/|cos| tables); used by
  `easy2see` (is the object actually visible on screen) and the target box.
* `reposition_fixed_child_objects`: FIXED_OBJECT children (THRUSTERS, TURRET flak origin)
  carry 2D offsets in `asObjectScreenX/Y` that are rotated by the parent's screen angle,
  scaled by the parent's screen scale, mirrored by the parent's flip, and added to the
  parent's screen position; `asObjectDistance += parent distance`;
  `screenScale = parentScreenScale * asObjectScale >> 8`.
* `place_exhaust_on_ships`: for ships/missiles 0..9 with speed ≠ 0, engines on and visible,
  the type's `animation` packet (section 2 of the ship file) is a table indexed by
  `asObjectViewFrame` giving an offset to a −1-terminated list of 6-short records
  `{frame, scale, distance, angle, x, y}`; each spawns a `THRUSTERS` object (class
  FIXED_OBJECT) with `scale - RandomInRange(0,32)` (−32 more if exhaust heat 0), angle via
  `flip_angle`, frame `frame*3 + RandomInRange(0,2)` on afterburner else
  `frame*2 + 12 + RandomInRange(0,1)`. These are removed every frame by
  `house_keep_objects` (FIXED_OBJECT case) and recreated. (Random consumption here is
  render-rate dependent.)

---

## 3. Object slots and lifecycle

`SPACE_OBJECT_COUNT = 64`, `SPACE_LAST_MOVING_OBJECT = 60`, `EYE_OBJECT = 61`.

| Slot(s) | Use |
|---|---|
| 0 | Player ship |
| 1..9 | Ships, capital ships, missiles, FUTURION placeholders (`get_ship_slot` scans 1..9; writes last result to `DAT_0046c010`). All "ship" arrays are sized 10, 12 or 16 and are only meaningful here. |
| 10..60 | Effects: projectiles, mines, debris, explosions, sparks, thrusters, asteroids/hazards, rock chunks, ejected pilot, jump flash, nav pointer, constellation planets (`find_vacant_3d_object` scans 10..60 and marks `asObjectScreenX = 0x8001`). |
| 34..41 | Dust streaks (class DUST, type SPACE_DUST) — `generate_stars` fills these; `borrow_dust` steals 34..41 for a player projectile when no slot is free; `extra_hazard` frees dust slots while a hazard field is active. |
| 42..48 | Background stars (class STAR, frames 32..37 of the constellation shape, placed 15000 units out along a random rotation of the eye frame). |
| 61 | Eye/camera (position, velocity, basis, `asObjectCollisionRadius[61]` = near plane: `max(10, radius of viewed object)`). |
| 62 | Saved copy of the player's frame at flight start (`copy_frame(0, 62)` in `RunSpaceFlight`). |
| 63 | Scratch frame (planet rotation, star field rotation, hazard spawn aiming, fleet-overview camera, mine intercept point `aShipPosition[63]`). |

Class values (`aeObjectClass`, `0 = NULL = free slot`): FUTURION 1, STAR 2, PLANET 3,
DUST 4, EXPLOSION 5, DEBRIS 6, FIXED_OBJECT 7, PROJECTILE 8, ASTEROID 9, MINE 10,
MISSILE 11, SHIP 12, CAPITAL_SHIP 13. Ordering matters: `>= PROJECTILE` = collidable,
`>= MISSILE` = steerable/targetable, `>= SHIP` = has shields/armor/weapons/fuel/AI.

### 3.1 Creation

* `set_objects_data(o, type, owner)` — the universal initialiser from `aObjectTypeData`:
  - `SPACE_DUST` only sets type/class.
  - If the type's `shapeSet` is not loaded, substitutes: ASTEROID2→1, 4→3, 6→5,
    METAL_SHEET→GIRDER_CHUNK, WING→PIPE, EXPLOSION1/2→EXPLOSION0.
  - Sets type, class, `apObjectShape` (ROCK_CHUNK uses ASTEROID1's set), `init_ijk`,
    `asObjectCollisionRadius`, `asObjectRadarRadius`, `asObjectScale`,
    `asObjectAfterburnerVelocity`, `acObjectOwner`, `asShipAccumulatedDamage = 0`,
    `asObjectFlip = 0`, `acLastCollisionObject = -1`, `asObjectScreenAngle = 0`.
  - class ≥ MISSILE: `asObjectViewFrame = 0`, `acShipTarget = -1`; class ≥ SHIP additionally
    shields = max shields = `{shieldFore, shieldAft}`, armor `[0]=front [1]=rear [2]=left
    [3]=right`, `anShipFuel = *(int*)&lifetime` (the 32-bit int overlaying
    `lifetime|weaponDamage<<16`), ion-drive damage 0, `acShipDamage = 0`,
    `recalc_max_velocity`, `acPilotHitPoints = 4`, weapon loadout copied (0x47 bytes),
    player: initial gun/release-weapon selection (the loop runs from the last slot down to
    slot 0, so the result is the gun type of the *lowest* enabled gun slot and the *lowest*
    enabled non-gun slot index; e.g. Hornet: laser, slot 2), `acLastAttacker = -1`,
    `asShipWeaponEnergy = 100`.
  - Otherwise (effects): if the type has an animation script, `asObjectAnimationDelay = 1`,
    `asObjectAnimationIndex = 0`, run `animate_shape` once; else `asObjectViewFrame =
    typeData.yawRate` (for non-animated effects that field is the static frame).
* `new_object(type, owner)` = `find_vacant_3d_object` (+`borrow_dust` if owner is the
  player) + `initialize_object` (zero position & velocity). `initialize_ship(type, owner)`
  = `get_ship_slot` + initialise + `aeShipSide = NEUTRAL`.
* `child_object(hardpoint, child, parent)` positions a child at `aChildOffsets[hardpoint]`
  (56 `ShortVector` offsets in object local units; note `position_child` multiplies the
  unit basis vectors (8.8) by the integer offset, so the result is already fixed) and sets
  the owner.

### 3.2 Removal

`remove_object(o)`: `asObjectScreenX = 0x8001`, `asObjectDistance = 0`, clears nav pointer /
wingman references, removes from `abHazardObjects[20]`, for ships (`o < 10`) frees capital
shape, `acShipRating = -1`, `acWingmanMessageState = -1`, side NEUTRAL, maneuver NONE,
`clear_alert`, `asCapitalShipViewFrame = -1`; finally class NULL, shape NULL. Other ship
fields are *not* cleared — a reused slot inherits stale values until `set_objects_data` /
`Set_up_ship_info` overwrite them.

### 3.3 `house_keep_objects` (per frame, slots 0..60)

| Class | Behaviour |
|---|---|
| DUST | `DEBRIS_DUST`: remove when counter reaches −1 and off-screen |
| DEBRIS | `count_down` → remove at −1 |
| FIXED_OBJECT | TURRET/THRUSTERS removed every frame (re-spawned by render) |
| PROJECTILE | counter → 0: TURRET (flak shell) `explode(owner, o)`, else remove |
| MINE | grace ticks−−; counter → 0: `explode(o,o)` |
| MISSILE | exhaust heat 0; grace−−; tactic SIT_STILL (just launched): when counter ≤ 0 → tactic RAM, counter = type lifetime, DUMB_FIRE keeps only the velocity component along its forward; otherwise counter ≤ 0 → `explode` |
| SHIP/CAPITAL | exhaust heat 0; counter > 0: WARPING_OUT halves `asObjectScale` each frame; a dying capital ship (special 9) at counter 7 spawns the big `ShipExplosion` + shock wave, otherwise random `onboard_explosion`s (while `RandomInRange(0,100) < 50`). counter == 0 & special 9 → 10% chance wingman says line 6 for a Kilrathi kill not by player, then `Create_explosion_debris` (removes the ship). counter == 0 & WARPING_IN & tactic ≠ WARP_IN: if `acObjectOwner == self` the slot becomes the ship type stored in `abShipNavPointIndex` (see `unwarp`) else removed. counter == 0 & WARPING_OUT & side ≠ NEUTRAL: mission record `state = 2` (left), removed. Tiger's Claw: landing check (`nTargetRange < 700 && nFacingToTarget > 75 && nTargetFacing > 70` with `bLandingAuthorized`, `normal_speed(0)`) → `nArcadeState = 1`, `nPlayerCollisionObject`; the SDL port drops the `nTargetFacing` term (lands from any bearing; the C# port's default, `LandFromAnyBearing`). The landing test also runs for a Claw that was just removed in the same case (its type is unchanged). |

`count_down(o)`: decrements `asObjectCounter` unless it is −1; returns the new value.

### 3.4 `update_objects_in_space` (per frame)

```
clear_crash_cache()
for o in 0..60:
    FUTURION: futurion_intelligence(o)
    class > PLANET:
        animate_object(o)                      // animation scripts; ship damage sparks
        if class >= PROJECTILE:
            object_collision(o)
            rotate_object(o)
            if o >= 10 or special != 9 (not dying):
                if o != 0: object_intelligence(o)
                if o < 10 and class >= MISSILE:
                    if o != 0: rotate_object_to_goal(o)
                    if class == SHIP: replenish_weapon_energy_bank(o)
for o in 0..60:
    class > PLANET: accelerate_and_move_object(o)
                    class >= SHIP: replenish_shields(o); housekeep_power_plant_and_fuel(o)
```

`animate_object`: EXPLOSION/DEBRIS/FIXED/ASTEROID/MINE run `animate_shape`; SHIP: every
4th rendered frame, if visible and `asShipAccumulatedDamage >= damageCapacity/2 - 1`, spawn
a RED/BLUE/… spark (`RED_SPARK + RandomInRange(0,2)`) behind the ship with a random ±20
offset, 25% chance of SFX 7.

`animate_shape(o)`: script words (`unsigned int` entries in the type's `animation`
table, 4 bytes each): decrement `asObjectAnimationDelay`; when ≤ 0 reload it with
`typeData.yawRate` (frames per step). Command `c`: `0x9000 | n` = jump to index n (debris
WING/METAL_SHEET play SFX 13 when visible); `0xa000` = remove object; `(c & 0xc00) ==
0x400` scale `+= (c & 0x3f) * (scale >> 6)`; `== 0x800` scale `-= ...`; else frame `=
c & 0x3f`; `flip = (c & 0xc0) >> 2`; index++. (e.g. EXPLOSION1: `0, 0x406, 1, 0x406, ...`
= frame, grow 6/64, frame, grow…; mine: `0,1,2,0x41,0x9000`.) **Correction (2026-10-07):**
the C code masks `c &= 0x3f` in the frame branch *before* extracting the flip bits, so frame
commands always set flip 0 (the mine's `0x41` shows frame 1 unmirrored, the glass debris'
`0x8c..0x92` frames 12..18 unmirrored); scale commands have no flip bits either. A jump
(`0x9000`) re-reads the target command and falls through to the frame/scale handling.

### 3.5 Movement (`accelerate_and_move_object`)

For class ≥ MISSILE (ships, capitals, missiles):

* Special `KILL_ENGINES` (5): exhaust 0; 10%/frame chance to clear the special.
  `STOP_DRIFT` (6): `approach_zero_speed`, velocity re-scaled to `anShipSpeed`, special
  cleared at speed 0.
* If special < 5 and tactic ≠ SIT_STILL, compute desired velocity `delta`:
  - `AFTERBURNER` (1): `asShipAfterburnerTimer--`; at 0 → special NONE, stop AB sound,
    desired = `fwd * anShipSpeed`; else desired = `fwd * (maximumVelocity + 20) * 0x200`
    (i.e. twice `(max+20)` in 24.8 — afterburner is `2*(Vmax+20)` units/frame),
    `drain_fuel(o, 200)`, exhaust heat 3.
  - `SUPER_BRAKE` (3): timer−−; at 0 → NONE, desired = `fwd * speed`; else desired = 0,
    `drain_fuel(o, 140)`.
  - default: desired = `fwd * anShipSpeed`.
  - `delta = desired - velocity; mag = |delta|`. If mag > 0:
    `acc = GetShipAccelerationRate(o)` (type `acceleration`, +1/3 for aces rating > 8);
    if collision-alert bit 1 set, `acc = max(acc, 0x500)`; AB/SB double it;
    `acc = MultiplyFixed(acc, DivideFixed(delta·fwd, mag) + 0x200)` (1× to 3× depending
    on how aligned the correction is with the nose); `accelVec = delta * min(DivideFixed(acc/2, mag), 0x100)`;
    exhaust heat 2 unless afterburning.
  - `velocity += accelVec`; for the player `vPlayerAcceleration = accelVec`.
* Every object: `position += velocity` (velocity is per-frame displacement, 24.8).

Speed control (disk.c/logic.c): `anShipSpeed[o]` is the commanded throttle in 24.8
units/frame; `asShipMaximumSpeed[o]` is an integer. `celerate(o, d)`: `speed += d`,
clamp to `[0, max << 8]`. `approach_speed(o, target)`: `acc = GetShipAccelerationRate`,
doubled under collision alert; `delta` limited to `±acc` (via `MultiplyFixed(SignFixed(delta), acc)`),
then `celerate`. Convenience: `approach_zero_speed`, `approach_min_speed` (0x500 = 5),
`approach_half_speed` (`(cruise & ~1) << 7`), `approach_cruise_speed` (`cruise << 8`),
`approach_full_speed` (`max << 8`), `approach_ship_speed(o, other)`. `set_speed(o, s)` sets
speed and `fix_velocity` (velocity = fwd*speed). `real_velocity(o)` = |velocity| as
short. `normal_speed(o)` = not afterburning and `real_velocity <= max`.

Player: `accelerate(amount)` adds `amount << 8` to speed (`-2` and SFX 3 if the ion drive
"malfunctions", `malf(0)`); keys +/− add ±1 per frame, backspace zeroes, `\` (0x2b) sets
9000 (= max). `your_afterburner`: needs fuel > 0 and no ion-drive malfunction; calls
`fire_afterburner(0, 8)` (or 2 to extend when ≤ 2 frames left); `fire_afterburner(o,t)`
only engages if `|velocity| < Vmax * 0x500` (5× max) and sets `set_special(AFTERBURNER)`,
timer = t. `fire_super_brake`: timer 10, special SUPER_BRAKE.

Max velocity: `get_ship_max_velocity(o)` = type `maximumVelocity`, ×4/3 for rating > 8.
`recalc_max_velocity(o)`: fuel ≤ 0 → 5; else `max * (4 - ionDriveDamage) >> 2`; if changed,
`celerate(o, 0)` re-clamps. `drain_fuel(o, n)`: `anShipFuel -= n` (the following
`if (anShipFuel == 0)` compares the array *address* and never fires — fuel exhaustion is
only detected by the next `recalc_max_velocity` call, e.g. from `damage_ion_drive`).
`housekeep_power_plant_and_fuel`: −5 fuel per frame while speed > 0.

Fuel capacities (int at `+0x10` of the type record): Hornet 200000, Rapier 250000,
Scimitar 280000, Raptor 300000, Kilrathi fighters 200000; capital ships 200000.

`set_special(o, s)`: only overrides when the current special < LOST_CONTROL (7) or `s` is
higher priority; a BLOWING_UP (8) is cancelled while collision-alert bit 1 is set.
Special values: 0 NORMAL/NONE, 1 AFTERBURNER, 2 BOGUS_LOOP, 3 SUPER_BRAKE, 4 BOGUS_PUSH,
5 KILL_ENGINES, 6 STOP_DRIFT, 7 LOST_CONTROL, 8 BLOWING_UP (= tumbling / lost control in
practice), 9 UNKNOWN_9 = **dying** (ship is exploding; counter drives the sequence).
`check_for_lost_control(o)` (after forces): `skill_check(o, (|roll|+|yaw|+|pitch|) /
(rollRate+yawRate+pitchRate))` fails → `BLOWING_UP` with counter `RandomBelowOrEqual(6)+5`.

### 3.6 Camera (`set_eye_direction_and_position`, `new_view`)

`nCameraViewMode`: 0 cockpit front, 1 right (eye forward = player right, eye right = −player
forward), 2 left (eye forward = −player right), 3 rear (eye frame copied from the player with
axis swaps), 4 chase (lags behind `cViewObject` by 700 or 500 units, velocity =
delta/25 or /7, roll matched), 5 "external/strafe" fixed offset view, 6 missile camera
(follows `nExternalViewShip`), 7 target view, 8 capital-ship chase (`nCapitalShipViewDistance`
0x7d000 = 2000 units), 9 death view (rear), 10 ride object (ejected pilot), 11, 12/13
canned launch/landing views, 14 fleet overview (`SetFleetOverviewView`), 15 scripted
(`update_scripted_view`, `pViewScript` of `{pitch,yaw,roll,frames}` commands). `new_view`
also calls `initialize_cockpit(mode)` and `generate_stars`. Only mode 0 draws the HUD.

Star field: `update_star_field` moves `vStarFieldMotion = fwd*200 + eyeVel*20`; for each
of 34..48 that went off-screen: 1/8 chance to re-roll; stars re-placed when the player is
rotating; dust re-spawned ahead with distances derived from the eye's local velocity ×10
(see code — it consumes several randoms per re-spawn). With an active hazard field,
dust slots < 42 are freed for hazards (`extra_hazard`) and `update_hazards` runs.
**Corrections (2026-10-07, phase 2):** every off-screen slot of 34..48 draws
`RandomInRange(0, 7)` until the first respawn, which ends the loop (`break`): at most one
star or dust speck is placed per rendered frame. Without a hazard field an off-screen slot of
class ASTEROID or NULL first becomes dust (and is respawned at once); the extra
`class == 0x21` test compares the class with `OBJECT_TYPE_SPACE_MINE` and is never true.
Stars need roll 0 (1/8) and a turning player; dust needs roll 0..1 (1/4).
`generate_stars` passes two `signed_random(distance)` calls as the right and up offsets of
`start_dust`; MSVC evaluates call arguments right to left, so the *up* offset is drawn first.
View 6 (missile camera) returns to the cockpit on the 22nd frame after the tracked missile is gone;
view 7 without a target and every `new_view` regenerate the stars (random numbers).
**Correction (2026-10-07, from the flight-UI analysis):** an earlier version of this section
called view 1 "left" and view 2 "right". The code gives 1 = right and 2 = left; F2 (key 0x3C)
calls `new_view(2)` and F3 (0x3D) `new_view(1)`, matching the manual's F2 left / F3 right.

---

## 4. Per-object state inventory

Every array below is indexed by object slot. "Objects" = 64 entries; "ships" = 10/12/16
entries (only slots 0..9 are valid ships; the 12/16 sizes are padding). Types are the C
types. This is the complete set that must become fields of the C# object record.

### 4.1 All objects (64)

| Array | Type | Meaning |
|---|---|---|
| `aeObjectType` | enum ObjectType | Type index into `aObjectTypeData` (0..57; 59 SPACE_DUST, 60 DEBRIS_DUST are virtual) |
| `aeObjectClass` | enum ObjectClass | 0 = free slot; see §3 |
| `aShipPosition` | FixedVector | World position, 24.8 |
| `aShipVelocity` | FixedVector | Displacement per frame, 24.8 |
| `aShipRightVector`, `aShipUpVector`, `aShipForwardVector` | FixedVector | Orientation basis i/j/k (unit, 24.8) |
| `anObjectPitchRotation`, `anObjectYawRotation`, `anObjectRollRotation` | short | Degrees rotated per frame; decay 1/frame |
| `anShipSpeed` | int | Commanded speed, 24.8 units/frame (ships, missiles; capitals) |
| `asObjectCounter` | short | Multi-use: ships = gun refire countdown (−1 idle) / warp & death sequence timer / flak cadence; projectiles, mines, debris, missiles = lifetime; dust = phase; futurion = saved class |
| `acObjectOwner` | signed char | Parent/creator slot or −1 (projectiles, missiles, mines, debris, thrusters, explosion → killer attribution via `the_creator`) |
| `asObjectCollisionRadius` | short | Collision/near-plane radius (units) |
| `asObjectRadarRadius` | short | "Mass" in the collision/force model (and radar size) |
| `asObjectAfterburnerVelocity` | short | Misnamed: rotational-inertia constant for `apply_force_to_object` |
| `asObjectScale` | short | Base sprite scale (0x100 = 1.0; fighters 0x400) |
| `asObjectScreenScale` | short | Projected scale this frame |
| `asObjectScreenX`, `asObjectScreenY` | short | Projected position relative to view centre; `0x8001` (−32767) = not visible |
| `asObjectDrawX`, `asObjectDrawY` | short | Final draw position (with centre offset) |
| `asObjectDistance` | short | Eye distance in units (`fixed >> 8`), 0 = not visible |
| `asPreviousObjectDistance` | short | Previous frame's distance |
| `aObjectViewPosition` | FixedVector | Eye-space position (also used by scanner and hit-direction flash) |
| `asObjectScreenAngle` | short | Sprite roll 0..359 (dust: `streak*0x10 + frame`) |
| `asObjectFlip` | short | 0x10 mirror X, 0x20 mirror Y |
| `asObjectViewFrame` | short | Sprite frame within `apObjectShape` |
| `apObjectShape` | byte* | Shape packet (RLE sprite set) |
| `asObjectAnimationDelay`, `asObjectAnimationIndex` | short | Animation script state (effects) |
| `acLastCollisionObject` | signed char | Last ship/asteroid collided with (debounce) |
| `acObjectCollisionGraceTicks` | signed char | Missiles/mines: frames during which the owner is immune |
| `asShipAccumulatedDamage` | short | Projectiles: damage payload; ships: count of internal damage events (drives sparks, capital-ship death); asteroids: damage taken |
| `anSortedObject`, `anObjectDepthPlaced` | int | Render sort scratch |

### 4.2 Ships / missiles only (slots 0..9)

| Array | Type | Meaning |
|---|---|---|
| `aeShipSide[12]` | enum Side | 0 IMPERIAL (Confed), 1 KILRATHI, 2 NEUTRAL |
| `aiPilotLevel[12]` | int | Pilot id: 0..4 generic skill, 5..12 named wingmen, 13 player, 14..17 Kilrathi aces |
| `acShipRating[16]` | signed char | `pilot − 5` (−1 for generic): 0..7 wingman personality, 9..12 ace; indexes `aRatedManeuverChoices`, callsign tables |
| `anShipFuel[12]` | int | Fuel |
| `asShipMaximumSpeed[16]` | short | Current max speed (integer) |
| `asShipAfterburnerTimer[16]` | short | Frames of afterburner / super-brake left |
| `aeSpecialManeuver[12]` | enum | See §3.5 |
| `aeShipMissionType[12]`, `aeShipObjective[12]`, `aeShipTactic[12]`, `aeShipManeuver[12]` | enum | AI hierarchy (§6) |
| `asShipCount[16]` | short | Maneuver/tactic counter |
| `acShipSequence[10]` | char | Maneuver step index |
| `anYawGoal[16]`, `anPitchGoal[16]`, `anRollGoal[16]` | short | Remaining degrees to turn |
| `acShipPointingMode[16]` | signed char | 1 = spherical goal solver |
| `acShipTarget[16]` | signed char | Current target slot or −1 |
| `aasShipShield[10][2]` | short | [0] fore, [1] aft |
| `aasShipMaximumShield[10][2]` | short | Max per side (reduced by shield-generator hits) |
| `aasShipArmor[10][4]` | short | [0] front, [1] rear, [2] left, [3] right |
| `asShipWeaponEnergy[10]` | short | Gun energy 0..100 |
| `aShipWeapons[10][0x47]` | byte | Loadout: `[0]` = count, then 10 × 7-byte `ShipWeaponSlot{int type; short hardpoint; sbyte disabled}` |
| `acShipDamage[10]` | signed char | Core damage events (explode when > damageCapacity) |
| `acShipIonDriveDamage[16]` | signed char | 0..3; max speed × (4−n)/4 |
| `acShipDestroyedWeaponCount[16]` | signed char | 0..5 (NPC "gun hit" events) |
| `acShipCommunicator[16]` | signed char | −1 = comm destroyed (FF missiles then treat it as hostile) |
| `acPilotHitPoints[16]` | signed char | 4 → 0 (player death / NPC tumble) |
| `acLastAttacker[16]` | signed char | Last projectile owner that hit |
| `acShipAiCooldown[16]` | signed char | +4 per hit, −1 per AI tick; > 0 → "just hit" event |
| `acShipStress[16]` | signed char | Morale/stress 0..~30 |
| `anShipAlertFlags[12]` | uint | bit 1 collision alert active, bit 2 alert ending |
| `abCollisionAlertTarget[16]` | byte | Object being avoided (0xff none) |
| `asCollisionCountdown[16]` | short | Alert timer (3) |
| `asCollisionPartner[10]`, `asCollisionTime[10]` | short | Per-frame crash-prediction cache |
| `acTurnRegulator[16]`, `acTurnInterval[16]`, `abShipTurn[16]` | signed char | AI tick scheduling: think every `interval` frames, phase-staggered; `abShipTurn` counts AI ticks (used as `& 7` phase selectors) |
| `asShipWingLeader[16]` | short | Leader slot or −1 |
| `aShipFormationOffset[10]` | ShortVector | Offset from leader (formation table difference) |
| `anShipMissionShip[16]` | short | Mission-record index of the escort/strike/defend target (or nav index for GOTO_WARP) |
| `nShipMissionIndices[10]` | short | Mission-record index of this ship |
| `acShipSpawnNavPoint[16]` | signed char | Nav point that spawned it (−1 = team member, persists across nav points) |
| `abShipNavPointIndex[16]` | signed char | Index into `abFlightPath` for COME_HOME/GOTO_WARP; also stashes the pre-warp type |
| `aShipDestination[10]` | FixedVector | Current travel destination |
| `aShipMissionSpot[10]` | FixedVector | Mission spot (patrol centre / warp point / home) |
| `abShipExhaustHeat[10]` | signed char | 0 idle, 2 thrusting, 3 afterburner (heat-seeker priority) |
| `acWingmanMessageState[16]` | signed char | Pending comm line (−1 none) |
| `asCapitalShipViewFrame[16]` | short | Loaded capital-ship frame (−1 none) |
| `asCannedCommand[16]`, `asActionCount[10]`, `apCannedSequence[12]` | short / short / short* | Scripted-sequence state (intro) |
| `asViableTargetDistance[16]`, `acViableTarget[16]`, `cViableTargetCount` | | Scratch target list (sorted ascending by `sort_viable_target_list`, a bubble-ish selection sort) |
| `asTargetListRange[16]`, `acFormationMemberList[16]` | | Scratch list for `build_target_list` / `build_squad_list` |
| `aiIntelligenceEvent[10]` | int | Last AI event |

### 4.3 Related globals

`nYourWingman` (slot or −1), `nTargetShip`, `nTargetRange`, `nFacingToTarget`,
`nTargetFacing`, `vToTarget`, `vNormalizedToTarget`, `vCollisionDelta`, `nSpaceFrame`
(simulation frame counter), `nRenderedSpaceFrame`, `nFrameSkip`/`nFrameSkipCounter`,
`nCameraViewMode`, `cViewObject`, `nExternalViewShip` (missile being tracked),
`nNavPointerObject`, `nEjectedPilotObject`, `nPlayerCollisionObject`, `nArcadeState`
(0 flying, 1 landed, 2 ejected, 3 stranded, 4 dead, 5 quit), `bEngageAllowed`,
`nAutoEngageTimer`, `nCurrentNavPoint`, `nCurrentWave`, `nEnemySighting`,
`pActiveHazardField`, `aHazardFields[7]`, `abHazardObjects[20]`, `nHazardFieldCount`,
`nActiveHazards`, `nHazardReferenceSpeed`, `acPlayerComponentDamage[9]`,
`eSelectedGunType`, `nSelectedReleaseWeaponIndex`, `nTargetLockCountdown`,
`nTargetLockMode` (auto-target lock toggle), `bTargetLockAcquired`, `nTargetLockMarkerAngle`,
`bMissileCameraEnabled`, `DAT_00475e78` (maneuver "too close" range scratch),
`bCurrentManeuverReroll`, `DAT_0046c010` (last allocated ship slot), `nLastFoundShip`,
`bInitialFormationSetup`, `nAutopilotFormationShipCount`, `cCurrentNavPointIndex`,
`cCurrentObjective`, `cMissionObjectiveCount`, `abFlightPath[16]`,
`aMissionObjectives[16]`, `aMissionNavPoints[20]`, `aMissionShips[48]`,
`aMissionObjectiveSources[16]`, `nMissionEntryNavPoint`, `nHomeMissionShipIndex`,
`nPlayerMissionShipIndex`, `nInitialMissionShipIndices[8]`, `nCarrierMissionShipIndex`,
`abMissionAuxData[0x28]`, `abSeriesAuxData[0x28]`, `nPlayerKillCount`,
`nWingmanKillCount`, `nWingmanKilledThisMission`, `bPlayerDestroyed`, `nMissionMedalScore`,
`stCampaignState`, `bLandingAuthorized`, `bRadioSilence`, `nCannedSceneMode`
(0 normal, 1 cinematic, 2 canned-sequence AI, 4 autopilot), `nTrainSimActive`,
debug/cheat flags `bPlayerVulnerable`, `bPlayerCollisionsEnabled`,
`bPlayerCollisionResponse`, `nOriginDevUnlock`, `nStartNavPointOverride`.

Player component damage indices (`acPlayerComponentDamage`, 0..4 severity = OK/Light/
Moderate/Heavy/Destroyed): 0 Ion Drive, 1 Power Plant, 2 Shield Generator, 3 Computer
System, 4 Intercom Unit, 5 Target Tracking, 6 Acceleration Absorbers, 7 Ejector System,
8 Repair Systems. `malf(c)` = `RandomInRange(0,15) < dmg²` (random malfunction test).

---

## 5. Object type data (`ObjectTypeData`, 0x87 bytes, 58 entries)

**The stat table is compiled into the executable** (`aObjectTypeData` at 0x00466458 in
`globals.c`); it is *not* read from `SHIPTYPE`/`SHIP.Vxx`/`OBJECTS.VGA`. Those files only
supply graphics: `shapeSet` (sprite set), `animation` (exhaust table / effect script),
`shape` (target-VDU silhouette) are patched in at load time (§5.3).

```
+0x00 char*  displayName        +0x1C byte*  animation      (ships: exhaust table; effects: script)
+0x04 int    objectClass        +0x20 int    acceleration   (24.8 units/frame²)
+0x08 short  collisionRadius    +0x24 short  pitchRate      (deg/frame; used for YAW goal/input)
+0x0A short  radarRadius        +0x26 short  yawRate        (deg/frame; used for PITCH; effects: anim step / static frame)
+0x0C short  scale              +0x28 short  rollRate
+0x0E short  animationDelay     +0x2A short  afterburnerVelocity (rotational inertia constant)
+0x10 short  lifetime           +0x2C byte[0x47] weaponLoadout (count + 10×7)
+0x12 short  weaponDamage       +0x73 short  shieldFore     +0x75 short shieldAft
+0x14 short  damageCapacity     +0x77 short  armorFront     +0x79 short armorRear
+0x16 short  explosionDamage    +0x7B short  armorLeft      +0x7D short armorRight
+0x18 short  maximumVelocity    +0x7F byte*  shapeSet       +0x83 byte* shape
+0x1A short  cruiseVelocity
```

Field meaning by class:

* **Ships/capitals**: `animationDelay` = shield regen period (frames per +1 point);
  `lifetime|weaponDamage` = 32-bit fuel; `damageCapacity` = core hits before death;
  `explosionDamage` = blast when destroyed; `maximumVelocity`, `cruiseVelocity`,
  `acceleration`, turn rates, shields/armor.
* **Projectiles (24..27)**: `animationDelay` = gun energy cost; `lifetime` = frames;
  `damageCapacity` = damage dealt; `maximumVelocity` = muzzle speed; `explosionDamage`
  (turret flak only, 1000).
* **Missiles (28..32)**: `animationDelay` = ?(500/400, unused by sim), `lifetime` = flight
  frames, `damageCapacity` = 4 (core hits for missiles themselves), `explosionDamage` =
  blast damage, `maximumVelocity`, `acceleration`, turn rates, `afterburnerVelocity` 100.
* **Mines (33)**: lifetime 120 (not used: set by `drop_mine`), explosion 10000.
* **Effects**: `yawRate` = animation step delay (or static frame when no script),
  `rollRate` = frame count/variant, `damageCapacity` −1 = indestructible, `explosionDamage`
  for explosions (6000, unused).

Full table (class, cRadius, radarR, scale, animDelay, lifetime, wpnDmg, dmgCap, explDmg,
Vmax, Vcruise, accel, pitch, yaw, roll, rotInertia, shields F/A, armor F/R/L/R, weapons as
`type@hardpoint(disabled)`):

| # | Name | Values |
|---|---|---|
| 0 | Hornet | SHIP 100 125 1024 5 fuel 200000 cap 5 expl 4000 V 42/30 acc 819 rates 8/9/8 inert 900; sh 40/40 ar 45/40/30/30; 5 wpns: laser@0, laser@1, DF@2, DF@3(off), HS@4(off) |
| 1 | Rapier | SHIP 120 135 1024 3 fuel 250000 cap 6 expl 6000 V 45/25 acc 1075 10/10/10 1000; sh 80/75 ar 60/55/50/50; laser@14(off) laser@18(off) neutron@12 neutron@20 IR@16(off) FF@15 FF@17(off) DF@13(off) DF@19(off) |
| 2 | Scimitar | SHIP 165 160 1152 6 fuel 280000 cap 7 expl 6000 V 36/15 acc 614 6/6/7 1300; sh 60/50 ar 85/80/65/65; MD@6 MD@11 DF@5 DF@10(off) HS@7(off) HS@8(off) HS@9(off) |
| 3 | Raptor | SHIP 180 200 1152 3 fuel 300000 cap 8 expl 8000 V 40/25 acc 588 6/5/6 2000; sh 70/70 ar 100/90/80/80; neutron@23 neutron@28 MD@21(off) MD@30(off) HS@22 HS@29(off) IR@24(off) IR@27(off) FF@26(off) mine@25(off) |
| 4 | Venture | CAP 240 400 1024 5 fuel 200000 cap 70 expl 20000 V 25/10 acc 256 3/3/3 4000; sh 150/150 ar 110/100/100/110; turret@51 turret@50 |
| 5 | Dilligent | CAP 240 400 1024 4 cap 60 expl 20000 V 15/10 acc 128 2/2/2 10000; sh 120/120 ar 80/80/60/60; turret@54 |
| 6 | Drayman | CAP 240 400 1024 4 cap 60 expl 10000 V 15/10 acc 128 2/2/2 20000; sh 120/120 ar 80/80/60/60; turret@54 |
| 7 | Exeter | CAP 500 5000 2048 2 cap 200 expl 30000 V 20/15 acc 256 2/2/2 20000; sh 240/240 ar 220/200/200/200; IR@47 turret@48..51 |
| 8 | Tiger's Claw | CAP 700 10000 4096 1 cap 560 expl 30000 V 0/0 acc 256 1/1/1 20000; sh 300/300 ar 240/200/250/250; 8 turrets @33..40 |
| 9 | Salthi | SHIP 120 120 1024 10 cap 5 expl 4000 V 48/30 acc 972 14/12/22 1000; sh 35/35 ar 30/20/15/15; laser@0 laser@1 DF@31(off) |
| 10 | Dralthi | SHIP 160 140 1024 6 cap 7 expl 6000 V 40/23 acc 768 10/14/10 1200; sh 50/50 ar 45/35/30/30; laser@0 laser@1 mine@32 mine@32(off)×2 HS@31 HS@31(off) |
| 11 | Krant | SHIP 140 126 1024 5 cap 6 expl 6000 V 36/20 acc 716 7/10/7 1200; sh 80/80 ar 90/100/80/80; laser@0 laser@1 FF@31(off) HS@31 HS@31(off)×2 |
| 12 | Gratha | SHIP 140 126 1024 4 cap 7 expl 7000 V 32/20 acc 614 6/6/14 1400; sh 100/95 ar 140/120/100/100; laser@0 laser@1 MD@21 MD@30 IR@31(off) HS@31 HS@31(off) mine@32(off)×3 |
| 13 | Jalthi | SHIP 160 180 1024 7 cap 7 expl 8000 V 28/20 acc 512 5/5/5 1600; sh 160/160 ar 200/100/170/170; neutron@41 neutron@44 laser@42 laser@43 laser@45 laser@46 FF@31(off) HS@31 |
| 14 | Spikeri | CAP 200 200 1536 4 cap 45 expl 12000 V 15/10 acc 460 4/4/4 4000; sh 70/70 ar 80/80/60/60; no weapons |
| 15 | Dorkir | CAP 260 400 2048 5 cap 35 expl 24000 V 15/10 acc 204 2/2/2 5000; sh 170/100 ar 90/60/90/90; turret@54 mine@55(off)×3 |
| 16 | Lumbari | CAP 260 400 2048 5 cap 35 expl 16000 V 15/10 acc 204 2/2/2 5000; sh 70/70 ar 80/80/60/60; turret@54 mine@55(off)×3 |
| 17 | Ralari | CAP 325 3000 4096 3 cap 90 expl 20000 V 15/10 acc 256 2/2/2 18000; sh 200/120 ar 200/90/180/180; IR@47 turret@48..53 |
| 18 | Fralthi | CAP 450 10000 4096 2 cap 110 expl 30000 V 15/10 acc 256 2/2/2 10000; sh 270/170 ar 280/140/260/260; IR@47×2 turret@48..53 |
| 19 | Snakeir | CAP 600 10000 2048 1 cap 320 expl 30000 V 15/10 acc 204 1/1/1 10000; sh 70/70 ar 80/80/60/60; none |
| 20 | Sivar | CAP 400 12000 4096 1 cap 200 expl 32000 V 20/15 acc 179 1/1/1 15000; sh 270/170 ar 280/140/260/260; IR@47×2 turret@48..53 |
| 21 | Kilrathi base ("Star post") | CAP 400 20000 2048 4 cap 120 expl 32000 V 0/0 acc 0 0/0/0 10000; sh 200/200 ar 180×4; turret@33,36,37,40 FF@33,36,37,40 |
| 22/23 | Asteroid field / Mine field | zero records (hazard descriptors) |
| 24 | Laser cannon | PROJ r10 radar0 scale512 energy 7 life 30 dmg 25 V 160 |
| 25 | Neutron gun | PROJ 10 1 832 energy 14 life 20 dmg 40 V 140 |
| 26 | Mass driver | PROJ 10 0 512 energy 9 life 25 dmg 30 V 120 |
| 27 | Turret (flak) | PROJ 15 0 832 energy 15 life 40 dmg 50 expl 1000 V 150 |
| 28 | Dumb fire | MISSILE 20 5 768 500 life 120 cap 4 expl 14500 V 130 acc 1433 rates 15 inert 100 |
| 29 | Heat seeker | MISSILE 20 5 768 400 life 140 cap 4 expl 13500 V 110 acc 1689 rates 11 |
| 30 | Friend-or-foe | MISSILE 20 5 768 400 life 160 cap 4 expl 10500 V 90 acc 1689 rates 11 |
| 31 | Image recognition | MISSILE 20 5 768 400 life 110 cap 4 expl 11500 V 110 acc 1689 rates 11 |
| 32 | Torpedo | MISSILE 25 10 768 400 life 200 cap 4 expl 30000 V 50 acc 1280 rates 10 |
| 33 | Space mine | MINE 20 5 768 110 life 120 cap 4 expl 10000 V 20/20, anim mine script |
| 34–39 | Asteroid 1–6 | ASTEROID 100 300 640, cap −1, asteroid anim scripts (13/12 frames fwd/rev) |
| 40 | Rock chunk | DEBRIS 10 4 192, cap −1 |
| 41–47 | Debris girder/tubing/sheet/wing/glass/o-ring/pipe | DEBRIS radii 10/10/20/20/20/2/6, scales 2048/2048/1280/1280/768/1792/1536 |
| 48–50 | Explosion 0/1/2 | EXPLOSION scale 768/256/256, expl 6000 |
| 51–54 | Laser spark, red spark, blue spark, spark trail | EXPLOSION |
| 55 | Thrusters | FIXED_OBJECT |
| 56 | Ejected pilot | DEBRIS 6 1 512 |
| 57 | Hyperspace jump flash | EXPLOSION 0 0 1024 |

Display names (`aszObjectTypeDisplayNames`, used for mobile objectives and the target VDU):
the name pointers of types 27 (turret) and 32 (torpedo) land on string padding and those of
34..57 lie past the last string, so they are all empty strings; 22/23 have a null pointer.
Ported as `Simulation.Data.ObjectTypeTable` (2026-10-07).

Hardpoint offsets `aChildOffsets[56]` (`ShortVector {x right, y up, z forward}`): see
`globals.c:2510`. Entries 0..32 fighter gun/missile points, 33..40 carrier/base turrets
(ring at radius 500), 41..46 Jalthi guns, 47..53 capital turrets, 54 tanker turret,
55 tanker mine rack.

### 5.1 Weapon loadout format

`aShipWeapons[ship][0]` = count (signed byte); slot `i` at `[1 + 7*i]`:
`int type (4 bytes, ObjectType)`, `short hardpoint`, `sbyte disabled` (1 = not selected /
removed). `remove_weapon(ship, i)` shifts later slots down, decrements count; for the player
it re-selects a gun (`select_new_gun`) or a release weapon (`select_new_release_weapon`) when
no weapon of the removed class stays enabled, and refreshes the left VDU. **Correction
(2026-10-07):** it does not mark the vacated slot: it writes 1 to byte `count * 7 + 7`
(computed before the decrement), the `disabled` byte of slot `count`, one past the vacated
slot. For a full ten-weapon loadout (Raptor, Gratha) that is byte 77 of the 71-byte record,
i.e. byte 6 (high byte of slot 0's hardpoint) of the *next* ship's loadout: an
out-of-bounds write that the port skips (memory-safety fix, see progress/simulation.md).

Player gun selection: `select_guns` enables all gun slots of the chosen type (or all guns
when 0x80 = "full guns"); `find_next_gun` cycles types then 0x80. Release weapons
(missiles/mines) are selected by index `nSelectedReleaseWeaponIndex`; only that slot is
enabled. `fire_fixed_projectile_weapon(o)` fires every enabled gun slot (one projectile per
slot, stops at the first failed allocation). `fire_missile` fires the first missile slot
(player: the enabled one; refuses HS/IR unless `nTargetLockCountdown == 0`, message
"Need Lock"). `drop_player_mine` drops the enabled mine slot (lifetime 20).

### 5.2 Animation / exhaust packets

Ship `animation` (section 2 of the ship file) = `short offsets[frameCount]` then
−1-terminated 6-short exhaust records (§2.5). Effect scripts are compiled-in `anAnim*`
arrays (§3.4).

### 5.3 Resource loading and logical files

`pDiskFileRecords[n]` is INSTALL.DAT's 16-byte record whose last byte is `n+1` (the table
is built with `pDiskFileRecords[logicalFile] = record` and then **`pDiskFileRecords++`**).
Known logical file numbers used by this subsystem:

| Logical | File (by evidence) | Sections used |
|---|---|---|
| 1 | scramble/landing scenes | hangar shapes |
| 2 | `PILOTANM.VGA` (eject/death shapes) | 0 death, 1 ejection, 2 ejected-pilot shape set, 3 pilot hand |
| 3 | `OBJECTS.VGA` (common 3-space effects) | 0 thrusters, 1 explosion0, 2 explosion1, 3 explosion2, 4 debris pipe, 5 metal sheet, 6 laser, 7 mass driver, 8 neutron, 9 laser spark, 10 blue spark, 11 red spark, 12 spark trail, 13 rock chunk, 14 jump flash, 15 mine, 16 asteroid5 (also 1,3), 17 asteroid6 (also 2,4; only when `nMemoryConfiguration == 2`) |
| 8 | `COCKPIT.VGA` (cockpit common) | 0 target-lock/crosshair shape, 1 inflight computer background, 2 nav map art, 4 indicator, 5 cockpit explosion, 6 cinematic backdrop, 7 escape-pod interior (`pRearViewBackdrop` is a misnomer), 8 view-geometry packet (eject) |
| 10 / 62 / 73 | `BRIEFING.000/.001/.002` | packet `mission + series*4` |
| 11 | comm faces | 0 Confed background, 9 Kilrathi background, 11 static, 1..8 portraits |
| 12 | planets/constellation | 0 constellation sprites (stars/dust frames 32..37), n+1 planet shapes |
| 13 | pilot speech text | raw file, `(personality*11 + line) * 80` byte records |
| 14 | mouse cursor | |
| 15 / 52 / 72 | `MODULE.000/.001/.002` | 0 header, 1 nav, 2 objectives, 3 ships, 4 mission aux, 5 series aux |
| 17..21 | `PCSHIP.Vxx` (cockpit for `cCockpitView + 17`; view 4 = training sim/Hornet-like) | 0..3 cockpit backdrops, 4 damage decals, 5 eject canopy, 6 view geometry (`ScreenViewportPacket`), 7 light and bar sprites, 8 scramble cockpit, 9 weapon display |
| 22 + type | `SHIP.Vxx` per ObjectType 0..21 (`cObjectResourceLogicalFile = type + 22`) | fighters: 0 shape set, 1 silhouette (`shape`), 2 exhaust table; capitals: 0..0x24 one shape per view frame, 0x25 silhouette |
| 58 / 61 / 74 | `CAMP.000/.001/.002` | 0 constellation definitions, 1 campaign/series data (`pMissionCampaignData`) |

**Correction (2026-10-07, from the flight-UI analysis, `analysis/flight-ui.md` §3.1):** logical
files 2, 8 and 17..21 above were completed with the file names and the sections the cockpit code
uses (COCKPIT.VGA 2 nav map, 7 escape-pod interior; PCSHIP 4 damage decals, 7 lights and bars,
9 weapon display; PILOTANM.VGA 3 pilot hand).

`aObjectResourceSlots[4]` (`ObjectResourceSlot {type, shapeSet, animation, shape}`): slot 0
= player ship type, slots 1..2 = the current nav point's two `preloadObjectTypes`, slot 3 =
heat-seeker missile (shared by all missiles). `load_ship(type, slot)` / `free_ship(slot)`,
`new_sphere_shapes(nav)` swaps slots 1..2 when entering a nav point (`set_up_action_sphere`),
`load_common_3Space_objects` aliases shape sets (all debris → pipe/metal sheet, turret →
laser, all missiles → heat seeker). `ASTEROID_FIELD` as a preload type loads the asteroid
and rock-chunk sets instead of a ship.

The SDL port decides DOS vs KS data by byte 7 of `MODULE.000` (1 = LZW-compressed packets).

---

## 6. Weapons and combat

### 6.1 Firing (`fire_weapon(o, slot)`)

* TURRET slots fire as LASER_CANNON projectiles. MINE → `drop_mine`. MISSILE → `initialize_ship`
  (takes a ship slot 1..9!), else `new_object` (10..60).
* `copy_frame(o, p)`; projectiles: `asShipAccumulatedDamage[p] = damageCapacity (damage)`,
  `projectileSpeed = maximumVelocity`, `asShipWeaponEnergy[o] -= animationDelay`.
  Position at the hardpoint (`child_object`), `asObjectCounter[p] = lifetime`,
  velocity = component of the shooter's velocity along the projectile forward.
  Projectiles are aimed (`point_at`) at `pos + fwd * (lifetime+5) * Vmax` (a point far ahead;
  without the cockpit in cockpit set 3 (`bCockpitlessView && cCockpitView == 3`, the Raptor;
  corrected 2026-10-07, it is not the rear view) the aim point is raised by `up * 0x12200`), then
  `velocity += fwd * speed`.
* Missiles: `velocity += up * 0xa00` (10 units kick), the slot is removed from the loadout
  (`RemovePlayerReleaseWeapon` / `remove_weapon`), grace 20, special NONE, maneuver NONE,
  tactic SIT_STILL, counter 5 (frames before the motor fires). DUMB_FIRE: counter 1,
  target = shooter's target, speed = Vmax << 8, aimed at a lead point
  `targetPos + targetVel * (range / Vmax)` (note: raw fixed `range` divided by integer Vmax
  gives a fixed time; `ScaleFixedVector` by it — matches the original). HS/IR inherit the
  target. FF: target −1, counter 15.
* Refire: player `asObjectCounter[0] = acGunRefireDelay[type-24] = {6,10,4,0}` (laser 6,
  neutron 10, mass driver 4 frames); NPC 12. The player can fire only when
  `asObjectCounter[0] == -1 && energy > 0` (`fire_players_lasers`); `count_down` brings the
  counter to −1.
* SFX: lasers/neutron 8, mass driver/turret 5, missiles 1.

Energy: `replenish_weapon_energy_bank` (ships, every frame): player with power-plant damage
skips with probability `dmg/5`; `energy < 100`: +1 if shields are below max, else +2
(capped 100). Shields: `replenish_shields`: player with power-plant damage only on frames
where `nSpaceFrame % (dmg+1) == 0`; each side +1 when `nSpaceFrame % animationDelay == 0`,
clamped to max.

### 6.2 NPC fire decision (`fire(o, target)`)

`canFire = asObjectCounter[o] <= 0`; `closingSpeed = (speed[target] * nTargetFacing / 100) >> 8`;
`fireMissile = RandomBelowOrEqual(19) == 0 && RandomBelowOrEqual(7000) > range` and no
missile of ours already chasing the target. For each loadout slot: `weaponVelocity = Vmax
(+ closing/100 if closing < 0)`, `targetInRange = lifetime * weaponVelocity > range`.
Guns fire when facing > 70 (laser) / 80 (neutron) / 85 (mass driver) / 10 (turret); the
slot's `disabled` byte is set to `!shouldFire` so `fire_fixed_projectile_weapon` later fires
only the aimed guns. Turret shells get `launch_object` toward the target. Mines: dropped
behind when the target is tailing (range < 2000, facing < −50, target facing > 90) and
predicted separation > 50, timed `range / (closing + 20)` and launched toward an intercept
point (uses `aShipPosition[63]`). **Correction (2026-10-07):** that drop can never happen —
the enclosing test requires `nFacingToTarget >= -50 && nTargetFacing <= 90`, the inner one
the opposite, so NPCs never lay mines through `fire` (the enclosing test also reads
`velocityAngle` before it is assigned; unobservable). The loadout count is re-read every
iteration, so a fired missile (which shifts the slots) makes the loop skip the next slot. Missiles: DF facing > 97, HS facing > 40 and target facing
< −60 (behind it), FF always, IR facing > 40; one missile per call.
`fire_when_ready(o)`: the player's wingman (except pilot 11, Maniac) will not fire while
the player is within an 80-percent cone ahead of him.

### 6.3 Missiles, mines, turrets

* `object_intelligence` runs missile brains only every 4th frame (`nSpaceFrame & 3 == 0`)
  unless it is the camera-tracked missile. DF: speed `(Vmax+10) << 8`. HS: if a target is
  ahead (facing ≥ 0) steer `point_ship(vToTarget)`, speed `(Vmax+10)<<8`; else re-acquire:
  list ships within 9000 that are ahead and facing away, sorted by range, prefer capital
  ships or the hottest exhaust (heat 3, then 2, then 1); none → explode. IR: always steers
  at `vToTarget`. FF (tactic RAM): picks the nearest ship within 9000 that is not
  (same side as the owner with a working communicator); then steers.
* Missiles explode on collision (`explode(o,o)` → `Explosion` → shock wave with
  `explosionDamage`), on lifetime end, or (HS) when losing track. **Correction:** the shock
  wave of a non-ship uses the `explosionDamage` of the type it was just turned into
  (EXPLOSION2, or EXPLOSION0 for turret shells and asteroids: 6000 each), not the missile's own
  value; a missile hit at point-blank range thus does `6000 / 8 / 8 = 93`.
* Mines (`mine_intelligence`): only when `asObjectCounter == -1`; explode if any ship is
  within `collisionRadius` (20) or within 50 with 1/8 chance. Hazard mines are
  `OBJECT_TYPE_SPACE_MINE` placed by the hazard code and "approach" the player.
  Hazard mines start with counter 0 (armed from the next frame, −1); mines dropped by
  `drop_mine` get counter = grace = 20 (the player's) and simply explode when the counter
  reaches 0 (timed, not proximity mines).
* Turrets: `fire_turrets(o)` builds a target list within 5000; each turret slot has a 1/3
  chance per AI tick; picks a target in the turret's hemisphere (`hemisphere` = percent
  cosine between hardpoint offset and target direction ≥ 25); non-turret capital weapons
  (IR missiles) fire with 1/15 chance when hemisphere > 50. `pop_flack` spawns an
  EXPLOSION0 near the target (random aim box `max(400, range/4)` + target speed×16, max
  1500) — 8% (or 40% when cadence allows) it bursts instantly as a shock wave with 1000
  damage, otherwise `fire_flack` launches a TURRET shell (life `range/150 - rnd(8) - 5`
  clamped 5..27) and sets the ship's counter `RandomBelow(10)+7`. **Correction
  (2026-10-07):** with `chance = RandomBelowOrEqual(100)` the burst happens when
  `(counter != -1 || chance >= 40) && chance >= 8`, i.e. 92 % while the gun cools down and
  60 % when it is ready; the shell is the rare case (8 % / 40 %). A shell whose fuse runs out
  explodes like any non-ship (`explode(owner, shell)` → EXPLOSION0, shock wave 6000).

### 6.4 Target lock (player)

`target_locking(target)` every rendered frame: needs an enemy target with target-tracking
component < Destroyed, target on screen and within 60 px of centre (`x²+y² ≤ 0xe10`),
release weapon HS (target must be facing away: `nTargetFacing <= -0x41`, lock time 0x12 = 18
frames) or IR (0x20 = 32 frames). `nTargetLockCountdown`: −1 off, > 0 counting (lock
marker spirals in: angle += player roll + pitch rates, radius = countdown*2), 0 locked
("MISSILE LOCKED", SFX 0x16), < −1 malfunction cooldown (`malf(5)`). Lock is lost when the
target leaves the circle or the weapon changes.

Auto-targeting (`check_target`, every frame): drops dying targets; `nTargetLockMode`
(T key) keeps the current target; otherwise, if the target is off-screen or friendly, pick
from `build_your_target_list` (ships 1..9, not dying, on screen, distance < 12000, sorted by
distance) the nearest enemy, else nearest friendly. `cycle_onscreen_targets` (T VDU
re-press) steps through the list preferring enemies.

### 6.5 Collision model

`check_for_collision(o)` (objects 0..60, class ≥ PROJECTILE, not self):
`vCollisionDelta = pos[other] - pos[o]`; `range = r[o] + r[other]`, halved when both are
SHIP; hit if `IsVectorWithinRange`. First hit wins.

`object_collision(o)` (if `bPlayerCollisionResponse` or neither party is the player):
normalise `vCollisionDelta` and the relative velocity, then by class of `o`:

* **PROJECTILE** hitting anything but its owner: if the victim is MISSILE/SHIP/CAPITAL:
  player victim → cockpit flash quadrant from `aObjectViewPosition[o]` (front/left/right/
  back/top/bottom → `aPaletteFadeEntries[n][0] = 0x38`); `acLastAttacker[victim] = owner`;
  `acShipAiCooldown[victim] += 4`; `damage = payload - counter/2` (projectiles lose damage
  with age); push `force = normalise(velocity) * damage` at the contact point
  (`apply_force_to_object(-delta, force, victim)`); `inflict_damage(o, victim, damage,
  relVel)`. The projectile becomes a LASER_SPARK at double scale moving with the victim.
  Asteroid victim: `hit_asteroid(partner, 3)` (1/3 chance to shatter into 2–3 rock chunks
  + explosion, else 1/8 chance of one shard).
* **ASTEROID vs ASTEROID**: off-screen → remove, else `hit_asteroid(o, 0)` (always shatters).
* **MINE**: ignores owner during grace; if not visible and the camera is the cockpit (or the
  victim isn't the player) the mine is silently removed, else `explode`.
* **MISSILE** (not owner, or grace expired): push `velocity * radarRadius` on the victim,
  `explode(o,o)`, zero velocity.
* **SHIP/CAPITAL**: invisible asteroids/unowned mines are removed instead of colliding;
  against ASTEROID/SHIP/CAPITAL with debounce (`acLastCollisionObject`): SFX 0x1c; separate
  the partner to `pos[o] + delta * min((r1+r2)<<8 / |delta|, 0x7d000)`; decompose velocities
  along the contact normal; `collisionSpeed = |Δ normal components| >> 8`,
  `damage = collisionSpeed² / 2`; masses `m = radarRadius`; `responseScale` =
  `clamp((m1-m2)*256/(m1+m2), 0x40, 0x400)` for `o` and `clamp(m1<<9/(m1+m2), ..)` for the
  partner; `impulse = Δ * responseScale + partnerNormalComponent` added to each velocity;
  ships (not capitals) also receive a tangential `rotational_acceleration` of magnitude
  `MultiplyFixed(m1*0x600, |impulse|) + 0xa00` and `inflict_damage(partner, o, damage)`;
  hitting a capital ship rewinds `o` one step, zeroes its throttle and takes the capital's
  velocity.

Forces: `apply_force_to_objects_center(force, o)`: `velocity += force / (radarRadius<<8)`.
`apply_force_to_object(point, force, o)`: torque terms from the local point/force cross
products divided by `rotInertia = DivideFixed(afterburnerVelocity<<8, collisionRadius<<8)`
added (`>> 8`) to the three rotation rates (clamped ±30), plus a translational term
scaled by `(0x16a - planarDistance) / (0x16a * mass)`; then `check_for_lost_control`.
`rotational_acceleration` is the torque-only variant with denominator
`DivideFixed(inertia<<8, MultiplyFixed(radius<<8, 0x123c))`.

AI avoidance uses a *predictive* test, separate from the physical one: `detect_collisions`
→ `crash_time(o, other)` (cached per frame in `asCollisionPartner/Time`) → `real_crash_time`
(closest approach via `|Δpos| / |Δvel|`, radius sum + 30, bisection over the predicted
interval; returns 0x7fff "never", 0x7fbc "parallel", 25 "near miss", 32000, or frames).
A predicted hit within 30 frames starts a collision alert (`start_collision_alert`: goals
cleared, countdown 3, alert bit 1, afterburner cancelled); `prevent_collision` then steers
away (`steer_away_from_predicted_object`) with full speed, zero speed if head-on, or
afterburner if the other ship is behind and facing us; `try2end_collision_alert` clears it
after the countdown. `handle_collisions` is called from `regulate_turn` for every
non-capital ship AI tick and suppresses the rest of the AI while the alert is active.

### 6.6 Damage model

`inflict_damage(attacker, victim, damage, impactDir)` (returns 1 if destroyed):

1. Ignore if `bPlayerVulnerable == 0 && victim == 0`, damage 0, victim dying, or class <
   MISSILE.
2. Class MISSILE/MINE…(< SHIP): `asShipAccumulatedDamage += damage`; explode when it reaches
   `damageCapacity` (4 for missiles; −1 = never).
3. Ships: player → red flash; wingman hit by a player-owned projectile → message 10.
   `quadrant = impactDir · forward[victim] > 0 ? 1 (aft) : 0 (fore)`;
   `damage -= shield[q]`. If damage ≤ 0 the shield absorbs it (`shield[q] = -damage`, SFX 10
   for projectiles). Else shield 0, SFX 9; armor quadrant: `side = impactDir · right`;
   `> 0xb5` (0.707) → 3 (right), `< -0xb5` → 2 (left), else the fore/aft index (0 front,
   1 rear). `damage -= armor[q]`; if ≤ 0 armor absorbs (`armor[q] = -damage`). Else armor 0:
   50% chance of one `Create_ship_hit_debris` (random girder/tubing/o-ring, life 40) when
   visible and not a capital; **1% chance (`RandomBelowOrEqual(99) == 0`) of instant
   destruction** when the attacker is an NPC ship (Kilrathi attacker says line 6), otherwise
   `internal_damage(attacker, victim, damage, q)`. A kill triggers
   `send_appropriate_message`.

`internal_damage` (NPC victims; player → `your_internal_damage`):

* Capital ship: `events = max(1, damage/8)` (Kilrathi; also `call_enemy` if the attacker
  has no enemy within 10000: 50% of enemy ships retarget the attacker) or `max(1,
  damage/10)` (Confed; 3.5% chance of message 4); `asShipAccumulatedDamage += events`;
  destroyed at `damageCapacity`; otherwise `onboard_explosion`.
* Fighter: `events = max(1, damage/40)` capped to `RandomInRange(3,4)` for rated pilots,
  `max(1, damage/6)` for generic; `asShipAccumulatedDamage += events`; while events > 0
  roll a system (the last event for a rated pilot is always 4):
  0 pilot hit (`pilot_hit`: hp−−, at 0 player dies; NPC: failed `skill_check(9)` → tumble
  30–50 frames); 1 (aft only) ion drive +1 (max 3); 2 (aft only) **instant explode**;
  3 shields and max shields zeroed (does not consume an event!); 4 `acShipDamage++`,
  explode when > `damageCapacity`; 5 (fore) lose a random weapon; 6 (fore) destroyed-weapon
  count +1 (max 5); 7 (aft) fuel −capacity/4 and 50% chance (or negative fuel) to explode;
  8 (fore) communicator destroyed. Systems that do not apply loop again (events not
  decremented).
* Player (`your_internal_damage`): event count by attacker class: projectile
  `max(1, damage>>4)` with table group 0 (fore) / 2 (aft); asteroid or ship ram
  `max(1, damage>>7)` group 4; other (missile/mine/explosion) `max(1, damage>>5)` group
  1/3. `severity = RandomBelowOrEqual(10)`; `asShipAccumulatedDamage[0] += events`; events > 1
  → cockpit damage sprite. Each event picks `system = asPlayerDamageSystemTable[group*10 +
  rnd(0..9)]`:

  ```
  group0 (proj fore): 0 8 6 5 0 3 5 5 7 6     group1 (other fore): 0 8 6 5 4 3 4 0 4 4
  group2 (proj aft):  1 2 5 2 7 3 4 7 5 1     group3 (other aft):  1 4 1 5 2 3 4 7 2 1
  group4 (ram):       4 4 4 4 0 8 6 5 4 0
  ```
  0: severity < 4 pilot hit, < 7 ejector +2, else acceleration absorbers +4; 1 (aft)
  ion-drive component +1 and `damage_ion_drive`; 2 (aft) 25% explode, power plant +1,
  reaching 4 explodes; 3: severity > 8 repair systems +2 else shield generator +1 and
  `revise_shields` (max shields −25% of type value each); 4: `acShipDamage++` (first one
  also pilot hit), explode when > capacity; 5 (fore) random weapon destroyed ("Weapon
  destroyed"); 6 (fore) destroyed-weapon count, computer +1; 7 fuel tanks hit (−capacity/4,
  50% explode); 8 (fore) severity > 6 target tracking +4 else intercom +2 (≥ 4 kills comm).
  Fore-only/aft-only systems that don't apply re-roll (`events++`).
* `repair_internal_damage` (player, every frame): 2/501 chance to repair: shield generator
  (if > 1), ion drive (if > 2, also `damage_ion_drive(0,-1)`), or core damage (if within 3 of
  capacity). Repairs are reported "%s FIXD".
* `evaluate_damage(o)` (0..100 health score used by AI): `-26*core/cap + 27*rear/maxRear +
  23*front/maxFront + 12*left/maxLeft + 12*right/maxRight + 26`.
  `calculate_damage_level()` (landing scene 0..3) from armor loss ×4 each, core ×30/cap,
  `asShipAccumulatedDamage*5`: < 5 → 0, < 40 → 1, < 70 → 2.

`explode(attacker, victim)`: named NPCs get a **50% survival roll** (`acShipRating` is
`pilot - 5`: 0..7 the Confed personalities Spirit..Knight, 8 the player persona — pilot 13,
excluded by the `!= RATING_ACE_ICEMAN` test — and 9..12 the Kilrathi aces, ace = rating − 9); Kilrathi
aces with ace flag 0x20 instead survive once: stress −25, maneuver OUTA_HERE, core damage
halved, message 6. Player: `bPlayerDestroyed = 1`, `nArcadeState = 4`. Otherwise
`analyze_kill(creator, victim)` (music change, message 5, `score_for_kill`, kill counters)
and `Explosion(victim)`: ships send line 7 (rated, or mission objective, or 2%),
`personality_killed` (campaign death record / ace kill), wingman death bookkeeping, special
9 with counter 8 and mission `state = 3`; capitals spawn 4 onboard explosions and count
`damageCapacity/4 + 8` frames; fighters `ShipExplosion` (EXPLOSION1 at the ship's scale,
owner = ship); non-ships become EXPLOSION2 (turret/asteroid: EXPLOSION0; the following
`asteroid → scale 0x380` test runs after `set_objects_data` changed the class and never
applies). Non-capital: `explosion_shock_wave(obj, explosionDamage of obj's type *after* the
change)`: ships within 1000
(minus their radius) take `blast / d / d` where `d = 40 (>750), 30 (≤750), find_ratio(0,500,
dist, 8, 25)` (≤500); player takes ×3/4 (min 1); damage > 1 applies a push and
`inflict_damage(the_creator(obj), other, min(100, dmg))`. `analyze_kill(creator, victim)`
indexes `aeShipSide[creator]`; the creator of an unowned asteroid, hazard mine or rock chunk
is that object (slot ≥ 10, past the 12-entry side table) — the C# port treats it as NEUTRAL.
`inflict_damage`'s `destroyed` is uninitialised when the 1 % instant-kill roll hits but the
attacker is not an NPC fighter (projectile hits, the player, the wingman); the result is
never used by the callers.

Debris: `Create_explosion_debris` = 7 debris of one of 4 sets + 8 DEBRIS_DUST, life 40,
random ±50 offset, ±25 velocity + ship velocity/2. Debris `WING`/`METAL_SHEET` share sets.

Scoring (`affect_mission_score(pilot, event, amount)`): events 1→7, 2→10, 3/4→15, 5/6→25,
7→50, 8→75, 9/10/11→25, 12→2×amount, 0/default→amount; added to
`stCampaignState.missionScore`, and for the player to `nMissionMedalScore` and
`nArcadeScore*10`. `score_for_kill`: Salthi 1, Dralthi/Krant 2, Gratha/Jalthi 3,
Dorkir/Lumbari 4, Spikeri/Ralari 6, Fralthi/Snakeir 7, Sivar/base 8.

---

## 7. AI architecture

### 7.1 Scheduling

`object_intelligence(o)` is called for every non-player collidable object each frame
(unless `nCannedSceneMode == 4` autopilot; mode 2 runs `update_canned_sequence` for
ships/capitals instead). Ships → `ship_intelligence`, capitals → `capital_ship_intelligence`.

Both start with `regulate_turn(o)`: dying → skip; non-capital `handle_collisions` → skip
while avoiding; `--acTurnRegulator > 0` → skip; else `abShipTurn++`, reload the regulator
with `acTurnInterval = anPilotTurnInterval[pilotLevel]`
(`{5,5,4,4,3,3,3,2,2,1,3,3,3,3,2,2,2,1}` frames). `find_next_ship_turn_slot` staggers the
initial phase so ships with equal intervals think on different frames. Thus an AI "tick"
happens every 1–5 frames per ship; **all the maneuver counters below count ticks, not
frames**. At the end of each tick `acShipAiCooldown` decays by 1.

### 7.2 Hierarchy: mission type → objective → tactic → maneuver

`aeShipMissionType` (from the mission record, changed by `reset_mission_type` /
`change_mission_type`): 0 PATROL, 1 ESCORT, 2 STRIKE, 3 DEFEND, 4 WINGMAN, 5 ROUT (flee),
6 GOTO_WARP, 7 WARP_ARRIVE, 8 CANNED_SEQUENCE, 9 RENDEZVOUS, 10 COME_HOME, −1 NONE
(inherit leader's). `aeShipObjective`: −1 NONE, 0 NAV_POINT, 1 HOME_BASE, 2 GUARD,
3 REACH_SHIP, 4 DESTROY_SHIP, 5 WANDER, 6 ENGAGE_ENEMY, 7 EVADE_ENEMY, 8 HOLD_FORMATION,
9 BREAK_FORMATION. `aeShipTactic`: −1 NONE, 0 CRUISE, 1 SIT_STILL, 2 SCOUT_AHEAD,
3 LAG_BEHIND, 4 RAM, 5 AVOID_OBJECT, 6 WARP_OUT, 7 WARP_IN, 8 HEAD_HOME, 9 CHASE,
10 LOOK_OUT, 11 APPROACH_TARGET, 12 TARGETTING, 13 SHAKE_ENEMY, 14 ZIP_AWAY, 15 RETREAT,
16 SELF_DEFENSE, 17 PICK_ATTACK. Setters: `reset_objective` (steady, tactic NONE →
maneuver NONE, target −1), `alter_objective`/`alter_tactic` (keep target),
`reset_tactic`, `reset_maneuver(o, m)` (maneuver, count 0, sequence 0),
`try2reset_maneuver`, `maneuver_complete` (special NONE, maneuver NONE), `engage(o, target,
objective)` (also triggers an ace greeting the first time), `fail` (objective NONE).

Mission handlers (`ship_intelligence` switch):

* **PATROL** (`kilrathi_patrol`; the Imperial arm is unreachable because the original tests
  the array address): objective NONE → WANDER with tactic APPROACH_TARGET; WANDER /
  HOLD_FORMATION → `patrol_area`: tactic HEAD_HOME cruises to `aShipMissionSpot`
  (scanning 14000 for enemies → APPROACH_TARGET), within 3000 → LOOK_OUT; LOOK_OUT cruises,
  scans, > 8000 from home → HEAD_HOME; APPROACH_TARGET full speed at the target, within
  10000 → `init_formation_burst` (whole squad breaks formation and engages). ENGAGE_ENEMY →
  `maneuvering(check_engage_target)`; BREAK_FORMATION → `formation_burst`.
* **ESCORT**: buddy = `find_ship_index(anShipMissionShip)`; gone → PATROL. Every 4th tick,
  if the buddy is `in_danger` within 3000 → engage; every 8th tick if > 5000 from buddy →
  HOME_BASE (`return_to_buddy`, within 1000 → WANDER and fly parallel). WANDER →
  `escort_buddy` (match speed, parallel). ENGAGE_ENEMY → maneuvering.
* **STRIKE**: goal ship gone → `check_goal` (ROUT if destroyed/left, else PATROL).
  HOME_BASE/HOLD_FORMATION → `approach_and_engage`: if healthy
  (`evaluate_damage > 70 - 15*min(4,level)`) and > 5000 → `streak_toward` (afterburner);
  nearer enemy fighters within 10000 (range×3 < goal range, or goal is FUTURION) →
  formation burst against them; within 5000 → DESTROY_SHIP; else streak. DESTROY_SHIP /
  ENGAGE_ENEMY → `maneuvering(check_destroy_target)` (sticks to the mission target while
  healthy, otherwise dogfights whoever is closest; 3% chance per tick to return to the
  goal).
* **DEFEND**: master gone → PATROL; every 10th tick master `in_danger` within 6000 →
  engage; every 8th tick > 10000 away → HOME_BASE (`return_to_master`, within 5000 → WANDER
  perpendicular). WANDER: scan 7000 → engage, else half speed orbiting perpendicular.
* **WINGMAN**: Imperial (`imperial_wingman`): NONE → HOLD_FORMATION; HOLD_FORMATION →
  `imperial_formation` (see §7.6); BREAK_FORMATION → `formation_break` (scripted peel-off:
  yaw −30, roll −45, pitch −20, then engage); ENGAGE/DESTROY → maneuvering. Kilrathi
  (`kilrathi_wingman`): no leader → PATROL; leader dead → `inherit_leader` (takes over the
  leader's mission, target, spot, and his wingmen); copies the leader's ENGAGE/DESTROY
  objective; HOLD_FORMATION → `maintain_formation`; BREAK → `formation_burst`.
* **ROUT** (`run_away`): if the wing leader is also routing → stay in formation; Imperial →
  `coming_home`; Kilrathi: pitch straight up (odd slots down), afterburner 40 (50% or when
  enemies within 16000) else full speed; removed when > 16000 from the player.
* **GOTO_WARP** (`reach_warp`): tactic NONE → CRUISE toward the first follow point; CRUISE →
  `cruise_to_destination` (every 8th tick scans 15000; speed depends on target facing; every
  8th tick checks arrival < 1500: flags the objective reached (Imperial), and at the mission
  spot → SIT_STILL + KILL_ENGINES, else next follow point); SIT_STILL → `prepare_for_jump`
  (stop drift; after 25 ticks (Kilrathi 250) turn away from the player, then after 45 (270)
  ticks or if the player is behind within 6000 → WARP_OUT + afterburner); WARP_OUT →
  `accelerate_and_jump` (full speed, at count 4 `warp(o)`: jump flash spawned, maneuver
  WARPING_OUT, counter 6 → scale halves each frame, then removed with state 2).
* **WARP_ARRIVE** (`warp_arrival`): tactic WARP_IN → `arrive_from_warp`: marks the nav
  objective visited, `approve_xyz` (no-op placement), `unwarp` (flash; maneuver NONE,
  counter 6; if no slot, the ship itself becomes the flash and its type is parked in
  `abShipNavPointIndex` to be restored by `house_keep_objects`), full speed, then
  COME_HOME (Imperial) or PATROL. Ships spawned with WARP_ARRIVE are turned into class
  FUTURION by `check_futurion` (class saved in `asObjectCounter`); `futurion_intelligence`
  restores the class when the player is > 1000 away after 1000 frames, or after 200 frames
  when the player is 1000..4000 away and facing it (> 80) — i.e. they "arrive" on camera.
  **Correction (2026-10-07, simulation phase 3):** the restore in `house_keep_objects`
  (counter 0, maneuver WARPING_IN, tactic ≠ WARP_IN, owner = itself) sits in the ship/capital
  ship case and never sees that fallback: the slot became a HYPERSPACE_JUMP_FLASH effect, which
  animates and is removed like any explosion, so the arriving ship is lost. `warp`'s fallback
  (jump out without a free slot) likewise turns the ship into the flash, and its mission record
  is not set to 2 ("left").
* **RENDEZVOUS**: REACH_SHIP toward the goal (engage attackers within 3500; within 2500 →
  DEFEND mission).
* **COME_HOME** (`coming_home`): tactic NONE → CRUISE with the first follow point (Imperial
  ROUT ships head for the home-base objective); CRUISE → `cruise_home` (every 8th tick phase
  5: wingman > 16000 from the player is removed; capitals cruise, fighters afterburner;
  arrival < 1500 flags the objective and advances; at the mission spot within 5000 →
  HEAD_HOME + KILL_ENGINES, stop); HEAD_HOME → fly parallel to the carrier.
* **CANNED_SEQUENCE**: handled by `update_canned_sequence` (mono.c) when
  `nCannedSceneMode == 2`: command stream of shorts: `0 n` wait n frames, `1 yaw pitch roll
  speed` set goals and speed (advance when goals reach 0 and the *player's* velocity is near
  the requested speed — original quirk), `2` explode, `3` fire guns, `4` afterburner.
* `abandoned(o)`: every 8th tick Confed ships have a 1/9 chance to self-destruct if they are
  in a hostile nav sphere far (> 10000) from the player and not at the current nav point
  (cleans up stragglers).

Capital ships (`capital_ship_intelligence`): ROUT/GOTO_WARP/WARP_ARRIVE/COME_HOME as above;
mission NONE → `stationary_intelligence` (Kilrathi base rotates yaw 4°/frame and fires
turrets); Dorkir/Lumbari → `tanker_intelligence` (attackers within 3000: full speed, fire,
random yaw/roll or turn tail-on; else cruise and `orbit_sphere` the nav point at half its
radius); destroyers/cruisers/base → `destroyer_intelligence` (`fire_turrets`, half speed when
firing, else cruise, orbit); Confed capitals: scan 15000, tactic SELF_DEFENSE, full speed
and turrets while a target is alive.

### 7.3 Dogfight tick (`maneuvering(o, target)`)

```
acShipTarget[o] = target
intelligence_events(o):
    event = -1; if missile_on_tail → 6
    else if target unactive → targetGone
    else if target dying → 8
    else: event 0; ship_vs_ship(o, target)
          range > 8000 → 2 (far)        | acShipAiCooldown > 0 → 7 (just hit)
          facing > 55 && tFacing < -55 → 5 (on its tail)
          facing > 75 && tFacing > 75  → 4 (head on)
          facing < -60 && tFacing > 85 && range < 7000 → 3 (being tailed)
          speed[target] < 20 → 1 (target stopped)
    handle_stress(o, event); if event != -1 process_maneuver_node(o, event)
    if targetGone: any_enemy(16000) ? select_target : reset_objective(NONE); reset_stress
    wingman chatter (stress crossing 15 → line 4; 0.4%/tick compare damage → line 8 or 4)
    aiIntelligenceEvent[o] = event
perform_maneuver(o)
```

`handle_stress`: `aggr = acPilotAggression[level]` (`{3,3,3,2,2,3,2,3,0,3,2,5,4,3,3,3,3,3}`);
events 3/4/7 `+aggr`, 5 `-aggr`, 6 `+2*aggr`, 8 `/2`, −1/2 `-acPilotRecovery[level]`
(`{6,7,8,8,9,8,6,8,10,7,8,9,7,0,7,7,8,8}`); then by health: < 40 `+2*aggr`, < 75
`min(+aggr, 28)`, else `min(stress, 7)`; event 6 caps at 29; floor 0.
`stress_morale`: < 15 → 0 (calm), < 30 → 1, else 2 (panic → OUTA_HERE).
`reset_stress` is only meaningful for `obj >= 12` (never for real ships — bug; stress reset
effectively does nothing).

`process_maneuver_node(o, event)`: rated pilot (`acShipRating != -1`) →
`pick_from_list(&aRatedManeuverChoices[rating][event][morale])`; generic Kilrathi →
`pick_kilrathi_maneuver` (`aKilrathiManeuverChoices[level][event][morale]`, 45 → STRAFE_ENEMY,
46 → `any_defense`); generic Confed → `pick_regular_maneuver`. The chosen maneuver replaces
the current one via `reset_maneuver` only if different.

`pick_from_list(choice, o)`: keep the current maneuver unless it is NONE, or 10% when the
current maneuver is neither of the pair (and both < 45), or an extra 5%; when re-choosing:
`RandomBelowOrEqual(100) < threshold ? primary : secondary` (−1 = NONE → `maneuver_complete`
on the next `perform_maneuver`). `pick_regular_maneuver`: panic → OUTA_HERE; with 20% (or
same event as last tick) keep the maneuver for events 0/3/4/7 unless a 3% re-roll; else
event 0: capital target → STRAFE_ENEMY, `RandomBelow(100) < level*5+60` → ZIP_PAST, else
defense; 2 → TRY2TAIL; 3 → 10% DROP_A_MINE if available else defense; 4 →
`rnd ≥ level*20+30` ? defense : STRAFE_ENEMY; 5 → TAIL_FIRE; 6 → level ≥ 2 ? HARD_TURN :
WABBLE; 7 → defense; 8 → LINE_UP_DROP; else ROLL_OVER. `any_defense` picks uniformly from
`apDefenseManeuvers[level]` (novice `{24,34,13,14}`, veteran `{8,13,15,14,19,24}`, elite
`{8,15,17,23,19,9,20,34,14}`, ace `{17,23,15,19,9,14,20,12}`, boss `{15,19,12,11,17,23,7,35}`).

The two choice tables (`ManeuverChoice {threshold, primary, secondary}`, `[rating 0..12]
[event 0..8][morale 0..2]` and `[level 0..4][event][morale]`) are data; copy them verbatim
from `globals.c:1198-1399`. Rating 8 (the player's own slot in the table) is all −1/0.

`perform_maneuver(o)`: `bCurrentManeuverReroll = abManeuverRerollChance[maneuver]` (3% for
SIT_N_FIRE and GET_DISTANCE, 5% for BUZZ_DEBRIS); `ship_vs_ship(o, target)`;
`DAT_00475e78 = (r[target] + r[o]*4 or *6 (target facing us)) / 2` — the "too close"
range; target gone → only VEER_AWAY/GLOAT/LINE_UP_DROP continue, else complete; dispatch
`apShipAiManeuverHandlers[maneuver](o, target)`; afterwards if `range < too close` →
`try2reset_maneuver(VEER_AWAY)`, else if the maneuver did not change and the reroll chance
hits → complete.

**Corrections (2026-10-07, simulation phase 3):**
* `bCurrentManeuverReroll` is read before the maneuver is validated: maneuver NONE (−1) reads the
  zero alignment byte before `abManeuverRerollChance` (0x00465677).
* With no target (−1) the Kilrathi Saga build does not return early (the SDL port does):
  `ship_vs_ship(o, −1)` computes facing and range against "object −1", i.e. the globals in front
  of the object tables of the image: `aShipPosition[−1]` = `asViableTargetDistance[10..15]` read
  as three ints, `aShipForwardVector[−1]` = the up vector of slot 63, `asObjectCollisionRadius[−1]`
  = 0 (padding), `aShipVelocity[−1]` = bytes 4..15 of `abFlightPath`. `unactive(−1)` then
  completes the maneuver (VEER_AWAY, GLOAT and LINE_UP_DROP continue), the too-close test can
  still switch to VEER_AWAY, and when the maneuver is unchanged (e.g. it was already NONE) the
  reroll number is drawn (`RandomBelowOrEqual(100)`), which the SDL port skips. `Mkill_missile`
  uses `nTargetShip` from `missile_on_tail` the same way.
* The enum names of the values 30..38 (`include/wcdata.h`) are shifted against the handler table
  (`apShipAiManeuverHandlers`, globals.c:3022): 30 STRAFE_N_ROLL and 36 GET_DISTANCE run
  `Mbest_strafe`, 31 KILL_MISSILE runs `Mstrafe_n_roll`, 32 SUICIDE_RUN `Mkill_missile`,
  33 ZIG_ZAG_PITCH `Msuicide_run`, 34 SAFE_BRAKE `Mzig_zag_pitch`, 35 TURN_N_FIRE the unnamed
  handler, 37 CORKSCREW `Mget_distance`, 38 INTERCEPT `Mcorkscrew`. The choice tables and
  `pick_regular_maneuver` use the numbers, so the handler table decides; the table below is by
  handler.

Maneuver handlers (`M*`, brains.c; counters in ticks; "complete" = `maneuver_complete`):

| # | Name | Behaviour |
|---|---|---|
| 0/1 | WARPING_IN/OUT | no-op |
| 2 | VEER_AWAY | step 0: steer away 40 (facing > 80) else 10; then if range > 3×target radius → random veer 8, complete; else steer away / random veer; afterburner 10 when closer than the threshold or 10% |
| 3/46 | DRIFT/reset | complete |
| 4 | FULL_AHEAD | full speed, count down |
| 5 | THINKING | cruise speed for ~2 ticks |
| 6 | RAM_MISSILE | full speed at target; facing > 75 and range < 6000 → `fire_missile`, complete |
| 7/20 | KICK_STOP / TURN_N_KICK | random veer 90, wait for goals, afterburner 10, complete when it ends |
| 8 | TIGHT_LOOP | pitch 180 twice at cruise speed |
| 9 | HARD_BRAKE | KILL_ENGINES, 3 ticks, super brake, 3 ticks |
| 10 | SIT_N_SPIN | 11-step script: match speed/point ahead of target, CHILL if not behind it, KILL_ENGINES, point at target, fire 6 ticks, random veer 35, ROLL_OVER |
| 11 | TURN_N_SPIN | veer 90, 2 ticks, if target not facing: KILL_ENGINES + point at it |
| 12 | BURNOUT | afterburner 10, then yaw 180 |
| 13 | WABBLE | 20 ticks of random yaw/pitch/roll ×5 at full speed |
| 14 | ROLL_OVER | roll ±180 (or 0) at full speed |
| 15 | HARD_TURN | yaw ±180/0 at full speed |
| 16 | FISH_HOOK | yaw ±120 + AB 5, wait, super brake + yaw ±45, wait for cruise speed, AB 10, complete at normal speed |
| 17/23 | SPLIT_LEFT/RIGHT | yaw ±90 |
| 18 | SIT_N_FIRE | capital target → best strafe; point at target, cruise (> 3000) or stop, fire |
| 19 | KICKIT | afterburner 10 |
| 21 | OUTA_HERE | `try2rout`: if a friendly capital exists (or training sim) stress 0 and complete, else mission ROUT (wingman says line 9) |
| 22 | DROP_A_MINE | drop if range > 1500, full speed, complete |
| 24 | ZIG_ZAG | 12-step yaw ∓35 zigzag at full speed |
| 25 | GLOAT | KILL_ENGINES, pitch +15/−30 nodding up to 10 times |
| 26 | TAIL_FIRE | point at target, `chase_speed` to `(r[t] + 6 r[o]) / 2`, fire |
| 27 | TARGET_LASER | = best strafe |
| 28 | TARGET_MISSILE | point at target, cruise; if one of our missiles is live → STRAFE_ENEMY; facing > 85, range < 6000, target facing ±80, 1/6 → fire missile |
| 29 | STRAFE_ENEMY | cruise, point at target when goals are zero, fire when aimed |
| 30/36/45 | best strafe | target facing < 80 → STRAFE_ENEMY else ZIP_PAST |
| 31 | STRAFE_N_ROLL | roll 45 while the gun counter runs, else strafe |
| 32 | KILL_MISSILE | missile on tail: facing < 0 → FISH_HOOK, < 80 → BURNOUT, else point at the missile and fire if < 8000 |
| 33 | SUICIDE_RUN | full speed at target |
| 34 | SAFE_BRAKE | zig-zag with pitch 35 |
| 35 | (unnamed) | 4-step: wait range < 750 or 10 ticks, veer 45, until target facing < 75, then point at it at speed 5 → TAIL_FIRE when facing > 10 |
| 37 | GET_DISTANCE | complete when > 2000; AB 10 if < 700 else full; steer away |
| 38 | INTERCEPT (corkscrew) | 8-step yaw/roll ±20 |
| 39 | TRY2TAIL | full speed, point at target, 4% random veer 5 |
| 40 | ZIP_PAST | close behind (within r+2000) → TAIL_FIRE; else full speed pointing below (target facing > 80) or behind the target |
| 41 | BUZZ_DEBRIS | veer 10, afterburner 10, veer while facing > 95 |
| 42 | LINE_UP_DROP | point at target, roll 360 |
| 43 | CHILL | chase a spot 900 ahead of the target, back to the stored maneuver when behind within 1000 |
| 44 | (reset stress) | stress 0, complete |

Target selection helpers: `select_target(o)` (Kilrathi: 50% pick the player if within 5000
and nobody is attacking him within 16000; else `scan_for_enemy(16000)` = nearest enemy
ship), `check_engage_target` (switch to whoever is tailing me, else re-validate),
`target_valid`, `select_safe_target` (nearest enemy not already in danger),
`being_tailed(o, other)` (other faces me > 85, I face away < −60, range < 7000),
`any_enemy_tail`, `detect_enemy_tail`, `missile_on_tail`, `in_danger`,
`attacker_in_range`, `nearest_enemy_range`, `build_target_list`.

### 7.4 Skill and ratings

`aiPilotLevel` comes from the mission ship record's pilot byte: 0..4 generic AI (also the
`rating` disk field "AI level 0–4"), 5..12 named Confed pilots in `Rating` order (Spirit,
Hunter, Bossman, Iceman, Angel, Paladin, Maniac, Knight), 13 player, 14..17 Kilrathi aces
(Bhurak, Dakhath, Khajja, Bakhtosh). `acShipRating = level - 5` (−1 if level < 5).
`skill_rating(o)`: level ≤ 4 → `max(2, level)`; 13 → 5; 5..12 → `((level-5)>>1) + 4`
(4..7); ≥ 14 → `level - 10` (4..7). `skill_check(o, difficulty)`: `skill_rating >
RandomBelowOrEqual(min(8, difficulty))`. Aces (rating > 8) get +33% max speed and
acceleration. `is_alive(pilot)`: generic always; 13 → not dead; 5..12 →
`personalityDeathMission[pilot-5] == 0`; 14..17 → ace flag bit 1. **Correction
(2026-10-07):** `init_ship` skips a record whose pilot is dead when the pilot id is below 9
(Spirit, Hunter, Bossman, Iceman) and otherwise replaces the pilot by generic level 3 —
this covers Angel, Paladin, Maniac, Knight (9..12) as well as the Kilrathi aces (14..17).
Canned-sequence records skip the check.

### 7.5 Formation

`aaFormationPositions[5][8]` (`ShortVector`, units; see `globals.c:360`) give the slot
offsets of five formation shapes. `set_formation_position(o, record)`: offset =
`positions[myFormation][mySpot] - positions[leaderFormation][leaderSpot]` where the leader
is found by following `leaderMissionIndex` to the root; when spawning (or on initial setup)
the ship copies the root leader's frame and is placed at the root's sphere point + offset
with the root's speed. Runtime: `maintain_formation(o)` → `compute_formation_destination`
(leader position + offset in the leader's frame + 3× leader velocity) → `goto_formation`
(speed by range: < 40% facing → min speed; > 2000 full (+AB 5 when > 3000 and aligned);
> 200 `control_speed` toward the leader's speed; else match; within 200 copy the leader's
frame; within 700 trim goals to 10 and match roll). `chase_location`/`goto_location` are
the generic travel primitives. Autopilot regroups via `auto_position` (650 lateral,
−1800×n forward, 500 up if overlapping the wingman).

### 7.6 Player wingman and comm orders

`imperial_formation(o)`: hold formation on the leader (the player). If an enemy is
attacking the leader within 12000: the wingman's auto-engage timer counts
(`nAutoEngageTimer`: −1 idle; set to 40 after asking permission (line 3) and to −40 /
−150 as refusal cool-downs); `try2allow_engage(level)`: generic pilots and personalities
8 (Iceman), 11 (Maniac), 6 (Hunter) always, 5 (Spirit) 50%, others wait. When
`bEngageAllowed` → `engage(ENGAGE_ENEMY)`. Otherwise when enemies first appear within 16000
in this wave → line 2 ("enemy sighted"). If > 9000 from the leader: afterburner toward him.

`request(requester, ship, command)` (comm menu; `apszCommMenuText` indices):
0 "Never mind…"; 1 "Attack my target!" (engage the player's target unless same side /
routing → reply 1/0); 2 "Help me out here" (engage whoever targets the player, else treated
as 9); 3 "Return to base." (`i_wanna_rout` by personality: generic yes, Hunter only without
enemies within 5000, Bossman/Maniac never, Iceman only in canned sequences, Angel when the
player's mission is complete, Paladin when no enemy within 10000; then `try2rout`); 4/5/6
taunts to Kilrathi ("Die furball!", "Slag off!", …: 70% (always for aces) they answer with
line 2..4, and if not busy they retarget the player); 7 "Break and attack" (from formation
→ BREAK_FORMATION); 8 "Keep radio silence"-style cancel engage (disobey for pilots 10
(Paladin? level 10 = Paladin) when being tailed, 11 (Maniac) when Kilrathi are still
around); 9 "Form on my wing" (HOLD_FORMATION); 10/11 radio silence on/off; 12 request
landing (`cleanup_objectives` + `can_land`: no enemy within 20000 and (damaged < 50 or any
kill or fuel < 1000 or all non-home objectives achieved/visited) → `bLandingAuthorized`,
line 8, else line 9). `reply(ship, ok)` sends line 0 (yes) or 1 (no).

`send_message(o, line)` queues `acWingmanMessageState[o] = line` for rated pilots, the
carrier/mission ship, or Kilrathi; `npc_communication` displays one per frame via
`vid_equiv` → `real_vid_transmit` (portrait + text "Callsign: speech[line]"), and gives
engaged Kilrathi a 2/5001 chance per frame of a random taunt (lines 2..4). Line usage as
observed: 0 affirmative, 1 negative, 2 enemy sighted/taunt, 3 request to engage, 4 under
stress/need help, 5 kill boast, 6 ace/Kilrathi taunt or "got away", 7 dying, 8 landing
cleared / "you're hurt worse", 9 refuse landing / returning, 10 "stop shooting me".

**Corrections (2026-10-07, simulation phase 3):**
* "Enemy sighted" (line 2) is only sent while no cockpit message shows (`message_showing()`) and
  in camera view 0; the engage target is `nTargetShip`, the attacker `attacker_in_range` found.
* The comm commands by their `apszCommMenuText` entries: 1 "Attack my target!" allows engaging
  even when refused; with no player target (−1) `bad_target` reads `aeShipSide[−1]`, in the KS
  image `anRollGoal[14..15]` (never written: 0 = Imperial), so Confed ships refuse. 3 "Return to
  base." needs `i_wanna_rout` **and** a successful `try2rout`, then disallows engaging. 4..6
  taunts draw `RandomBelow(100)` first (also for aces). 8 is "Keep formation!": disallow engaging;
  Paladin (level 10) refuses while an enemy is on the player's tail and Maniac (11) while any
  Kilrathi is around (`disobey_formation`; the refuser switches to BREAK_FORMATION, reply no);
  otherwise the auto-engage cool-down −150 and reply yes. 9 "Form on my wing.": the same refusal
  test, else HOLD_FORMATION, cool-down −150, yes. 10 "Keep radio silence" / 11 "Broadcast
  freely": the reply goes out before the silence flag changes. Any other value, including −1
  from the comm menu's off-by-one, does nothing.
* `can_land` needs no enemy within 20000 and **one** of: health (`evaluate_damage(0)`) below 50,
  a kill, fuel below 1000, or **any** objective other than the home base that is achieved, or
  visited unless it is an escort objective (type 2). A single qualifying objective is enough.

---

## 8. Mission loading and flow

### 8.1 Files

Three campaigns: `nCampaignDataSet` 0 = original (`MODULE.000`, `BRIEFING.000`,
`CAMP.000`), 1 = Secret Missions 1 (`.001`), 2 = Secret Missions 2 (`.002`). Logical file
ids per campaign: mission data `{15, 52, 72}`, briefing `{10, 62, 73}`, camp `{58, 61, 74}`.
`missionIndex = mission + series * 4` (4 missions per series; `currentSeries`/`currentMission`
in `CampaignState`). `CAMP.xxx` section 1 (`pMissionCampaignData`) holds per-series records
of 0x5a bytes starting at `series*0x5a - 0x5a` (series are 1-based in the data): `[0..1]`
debriefing personality, `[2]` mission count, `[3..4]` required series score (short), `[5]`
post-series sequence id, `[6]` next series on success, `[7]` ship type on success, `[8]`
next series on failure, `[9]` ship type on failure; then per mission (`+ mission*0x14 -
0x50` from the series base) `[0..1]` medal index, `[2..3]` medal score threshold,
`[4..19]` 16 objective score values.

### 8.2 MODULE packet sections (`LoadMissionData`, cmpgn.c)

| Section | Stride per mission | Record (packed, little-endian) |
|---|---|---|
| 0 header | 0x18 (64 missions) | `short entryNavPoint; short homeMissionShip; short playerMissionShip; short initialMissionShips[8]; short field_16` |
| 1 nav points | 0x4d0 = 16 × 77 | `char name[30]; sbyte type; FixedVector position (3 × int32, already 24.8); ushort proximityRadius; sbyte triggers[4][2]; short preloadObjectTypes[2]; short missionShips[10]` |
| 2 objectives | 0x400 = 16 × 64 | `short type; short index; char description[60]` |
| 3 ships | 0x540 = 32 × 42 | `short type; short side; sbyte leader; sbyte field_5; short missionType; sbyte navPoint; FixedVector position; short pitch, yaw, roll; sbyte formationSpot; short speed; short rating; short pilot; short field_2c; short field_2e; sbyte state; sbyte leaderMissionIndex; sbyte formationIndex; sbyte targetMissionIndex` |
| 4 mission aux | 0x28 | copied to `abMissionAuxData`: the mission (wing) name, e.g. "Alpha Wing" |
| 5 series aux | 0x28 per *series* | `abSeriesAuxData`: the system name, e.g. series 1 "Enyo" (series 0 "Squadron" = simulator) |

**Verified against the GOG DOS files (2026-10-07, open question 9 resolved):** the DOS
`MODULE.000/.001/.002` records use exactly this KS layout (section sizes 64 × stride:
1536 / 78848 / 65536 / 86016 / 2560 / 640). Coordinates are 32-bit little-endian 24.8 fixed
values with a zero fraction byte (e.g. nav 1 of mission index 4 at section 1 offset 0x138D:
type byte `01` at +30, x bytes `00 30 75 00` = 30000.0 at +31, radius `98 3a` = 15000 at
+43). The community "3-byte coordinate" description reads the three integer bytes of the
same values (its "sphere radius ×1000" byte at +31 is actually the x fraction byte). Unused
ship records have type -1; a nav record of type 0 with an empty name ends the list. The
player's record is usually a Hornet..Raptor, but Secret Missions 2 has the player fly a
Dralthi. Mission counts: 44 (original incl. 4 simulator missions), 22 (SM1), 22 (SM2).

Runtime records: `MissionNavPoint` (0x51 bytes): `name[0x1e]; sbyte type (0 = unused /
terminator, 1 = active, 2..5 = follow-up waves, −1 consumed); FixedVector position; short
proximityRadius; sbyte triggers[4][2] {newType, navIndex}; ObjectType preload[2]; short
missionShips[10]`. `MissionShipRecord` (0x36): fields as the disk record with `int` type/
side/missionType/rating and `behaviour.pilot` (int) or, for the 14 built-in intro records
32..45, a pointer to a canned command stream; `state`: 0 alive/unspawned, 1 arrived home,
2 left (warped out), 3 destroyed. Positions are **relative to the nav point** the ship is
attached to (`set_sphere_point` = nav position + record position). Asteroid/mine fields
are ship records of type 22/23: `speed + 3000` = radius, `pilot` = density.

### 8.3 Mission start

`GameFlow` → `init_mission(series, mission)`: `LoadMissionData`, `init_3Space_objects`
(clear all 64 slots, constellation for the series), load mission effect shapes,
`prepare_mission`, `InitializeCockpitResources(series == 0 ? 4 : playerShipType)`.

`prepare_mission`: zero mission counters; `playerShipType = aMissionShips[player].type`;
`load_ship(type, 0)`; `set_objects_data(0, type, -1)`; player nav = entry nav (or
`nStartNavPointOverride`); `Set_up_ship_info(0, player, -1)`; clear component damage;
spawn the 8 `initialMissionShips` at the entry nav point (alive pilots not assigned to a
nav sphere); the first with pilot 5..13 becomes `nYourWingman`; `Build_objective_list`;
`nCarrierMissionShipIndex` = first record of type TIGERS_CLAW.

`Set_up_ship_info(o, missionShip, navPoint)`: resets per-ship counters, `nShipMissionIndices`,
spawn nav, pointing mode 1, position = sphere point, orientation `alter_yaw(-pitch)`,
`alter_pitch(-yaw)`, `alter_roll(roll)` (note the swapped names), side, speed `<< 8`,
pilot level, `reset_mission_type(record.missionType)`, `anShipMissionShip =
targetMissionIndex`, wing leader = `find_ship_index(leaderMissionIndex)`,
`set_formation_position`, zero velocity, `init_intelligence_data` (alerts, special NONE,
mission spot = current nav point, ESCORT/STRIKE/DEFEND/WINGMAN keep the target index,
GOTO_WARP spot = nav `missionTarget`, WARP_ARRIVE tactic WARP_IN + maneuver WARPING_IN,
COME_HOME (and Confed WARP_ARRIVE) spot = home ship location, CANNED → script; rating,
stress 0). `init_ship(missionShip, nav)` wraps this with the hazard-field case, duplicate /
dead checks, `find_next_ship_turn_slot`, `check_futurion`.

`RunSpaceFlight(entryNav)` → `set_up_action_sphere(nav)`: `nCurrentNavPoint = nav`,
`nCurrentWave = (next nav type == 2) ? 2 : -1` (follow-up waves live in the *next* nav
records), `nEnemySighting = 0x7fff`; every ship 1..9 that belongs to a nav sphere
(`acShipSpawnNavPoint != -1`) is destroyed (visible → `explode`, else removed; ROUT ships
are marked state 3); hazards cleared; `new_sphere_shapes`; spawn the nav's 10 mission
ships; apply the nav's triggers (`aMissionNavPoints[target].type = newType`); relocate
mobile objectives; `clean_up_cockpit`; landing not authorised. `house_keep` re-evaluates
`FindNearestNavPoint(0)` every 32 frames (within `proximityRadius` of a type-1 nav) and
switches spheres (`ReleaseStaleNavTarget`); hazard fields are checked every 16 frames.

Waves: `check_next_wave` (after each kill / rout check) → when no Kilrathi ship remains
and `nCurrentWave != -1`, `set_up_next_wave` spawns the mission ships of nav record
`nCurrentNavPoint + nCurrentWave - 1` if its `type == nCurrentWave`, marks it −1 and
increments the wave; otherwise waves end.

### 8.4 Objectives and the nav computer

`Build_objective_list` converts the 16 `MissionObjectiveSource {type, index, text}` into
`MissionObjective {mapX, mapY, type, index, flags, displayName, name, position}` until type
−1: type 0 = nav point (index = nav), 1..4 = mobile (index = mission ship; 1 home/carrier,
2 escort, 3 "green circle" reach, 4 "red circle" destroy); types 0..4 are appended to the
flight path `abFlightPath[]` (−1 terminated). Flags: 1 visited, 2 achieved, 4 sighted.
`set_new_objective(pathIndex)` selects the current destination (`cCurrentNavPointIndex`,
`cCurrentObjective`, `aeShipObjective[0] = type`), skipping `hidden_objective`s (names
starting with `.`/`?`, dead/left ships, un-arrived WARP_ARRIVE ships). `set_next_destination`
advances to the first unvisited path entry. `update_objective_location` (one objective per
frame, round robin) marks sighted (< 16000 and visible), and visited/reached
(`check_visit`: < 1500, or < 6000 for types 3/4 → `flag_reached` → messages "Objective
reached"/"Already visited", wait-for-escort logic, advance). `objective_lost` cycles past
destroyed targets. `cleanup_objectives` (at landing request / end) converts visited/
sighted/destroyed states into "achieved" flags and scores (`affect_mission_score` events
5/9 for escorts). `PlayersMissionScore` sums the campaign's per-objective score values for
achieved objectives; `UpdateSeries` branches the campaign.

**Clarification (2026-10-07, simulation phase 3):** ships that run past the end of their path use
the flight path's −1 terminator as an objective index (`get_follow_point`, `cruise_home`,
`cruise_to_destination`, `arrive_from_warp`, `coming_home`, and the autopilot without
objectives). The original then reads the record before `aMissionObjectives` (0x0059dac0), which in
the KS image overlays `asObjectAnimationIndex` of slots 59..61 (type = the indices of slots 59
and 60, index and flags = the two bytes of slot 61's) and zero padding (position = origin);
`flag_objective(−1, f)` ORs `f` into the high byte of slot 61's animation index.
`update_objective_location` passes `LocateMobileObjective`'s 1/0 result as an object slot to
`check_sighting` and always counts mobile objectives as located. `nSpaceFrame % count` (a
`short`) turns negative after 32767 frames of one flight (about 27 minutes), and the original
then reads before the table.

Nav map (`BuildMap`, `SetScale`, `nav_getxy`): map coordinates are `(x/100) >> 8` and
`(z/100) >> 8` (XZ plane, 1 px = 100 units before scaling), scaled around the bounding box
of all objectives + player.

### 8.5 Hazard fields (winmain.c)

`aHazardFields[7]` `{type, center, innerRadius, outerRadius, density}` (`add_hazard_field`
from mission records). `check_hazards`: entering within `innerRadius + 4300` of a field
activates it (`start_hazard_field`: 3 initial hazards); leaving deactivates. `update_hazards`
(every rendered frame while active): reference speed = player's real velocity; each of 20
slots is managed on frame `nRenderedSpaceFrame % 20 == slot` (removed when > 4300 ahead /
`rear_sphere()` behind); mines approach the player; a new hazard spawns with probability
`(speed + 30) / 216` per frame at a spot 3050 units out at a random yaw ±35 / pitch ±20
(biased by the player's turn rates), "moving" asteroids are aimed to intercept the player's
predicted position in `travelTime` frames (56/52/75/73 by cockpit view, minus up to
`difficulty()` = `|25 - speed| * 2`). Asteroid types 34..39 random, speed 10..17 (20% still),
mines snapped to a 200-unit grid.

### 8.6 Autopilot (`auto_pilot_sequence`)

Valid when no objective within 8000, no Kilrathi within 16000, no active hazard
(`auto_pilot_valid`). Removes non-team Confed ships (if leaving the nav sphere), stops all
team ships, points the player at the destination at speed 60, positions team ships in
`auto_position` formation, runs a 120-frame cinematic (view 12) with `nCannedSceneMode = 4`
(AI disabled) while moving the player 400 units/frame (`0x19000`) toward the destination
until within 1000, a hazard activates, a non-team ship is within 4000, or Kilrathi are
present; then everyone is set to the slowest cruise speed, repositioned, and view 0 is
restored.

**Correction (2026-10-07, simulation phase 3):** the cinematic comes before the travel. Order:
`auto_pilot_valid(1)` (no objectives: silently refused; within 8000 of the current objective
"Already Near", Kilrathi within 16000 "Enemy Near", in a hazard field "Hazard Near"),
`clean_up_cockpit`, `ResetSoundState`, **every** ship stopped; Confed team members without
Kilrathi within 10000 travel along, other Confed ships are removed when the destination lies
outside the current nav sphere; the player points at the destination at speed 60; travellers that
are the player's wingmen, or within 20000 and heading for the same destination, take the
`auto_position` slots with the player's frame and speed; `nCannedSceneMode = 4` (AI off); the
120-frame cinematic (view 12) runs; then an **instant** loop (no frames) moves the player
400 units per step (`0x19000`) until within 1000 of the destination, in a hazard field, within
4000 of a ship that does not travel along, or `report_kilrathi_rout(1)`; the player steps back the
last step, travellers get the slowest cruise speed of the group (at most the player's maximum)
and their formation slots (far ones with their own destination are put at it when it is nearer
than the trip), one `Update_3Space` runs, then `force_view(0, 0)`.

### 8.7 Flight end states

`RunSpaceFlight` loops until `nArcadeState != 0`: 1 landed (Tiger's Claw proximity with
clearance → landing scene, `calculate_damage_level`), 2 ejected (Ctrl+E, ejector damage
roll; `ejection_sequence`), 3 stranded (carrier destroyed and no enemies), 4 dead
(`death_sequence`, funeral), 5 quit. After landing: `PostMission` (kills, badges, wingman
stats), `UpdateSeries`, debriefing, promotion roll (`RandomInRange(0,5) + promotionScore > 7`).

---

## 9. Frame loop summary

```
RunSpaceFlight:
  per frame (target fSpaceFlightFrameRate 20 Hz, adjustable 8..32; cinematics use fCinematicFrameRate):
    HandleSpaceFlightControls()      // player_input → nPitch/Yaw/RollInput, keys; players_flight_dynamics → rotation rates
    Update_3Space():
        house_keep()                  // nav-sphere switch (every 32 frames), hazards (every 16), cockpit palette fades
        house_keep_objects()          // lifetimes, deaths, warps, landing check
        update_objects_in_space()     // §3.4: animate, collide, rotate, think, steer, energy; then move, shields, fuel
        set_eye_direction_and_position()
        servicetrack()                // music
        nSpaceFrame++
    RenderSpaceViewFrame()            // only every nFrameSkip frames: Draw_3Space_Frame (§2.5), messages, present
    update_cockpit()                  // check_target, repair, objective tracking (1 per frame), lights, missile warning,
                                      // scanner, readouts, VDUs, pilot hand, cockpit explosion, npc_communication,
                                      // fire_computer_graphic_missile, check_stranded
```

Random-number consumption happens in all four phases (input: none; sim: many; render:
dust/star/exhaust; cockpit: comm chatter, VDU portrait frames), so frame skipping changes
the sequence. Simulation runs every frame regardless of `nFrameSkip`.

Cockpit data flow (read-only from sim state): scanner (`draw_3d_scanner`) uses
`aObjectViewPosition` → spherical → `rotational_pos_to_scanner_pos` (yaw/4 or /6, pitch/−3
pixels, clamped to the cockpit's scanner box), colours by side/class; shields/armor bars
and digital readouts from `aasShipShield/Armor[0]`, `anShipSpeed[0] >> 8 * 10` ("set
speed"), `|velocity| * 10` ("actual"), fuel %, weapon energy; target VDU from the target's
shields/armor and `asObjectDistance`; left VDU weapons list from `aShipWeapons[0]`;
damage VDU from `acPlayerComponentDamage`; nav VDU range from `set_objective_range`
(`spherical.radius >> 8` in km after `/ 100`... displayed as `nCurrentObjectiveRange` "km");
nav pointer (`draw_nav_pointer`) is a pseudo-object projected like any other.

---

## 10. Proposed C# design

### 10.1 Object storage: array-of-structs, with the slot index as identity

Use **AoS**: one `SpaceObject` struct per slot in a fixed `SpaceObject[64]` (or a
`readonly` wrapper over `Span<SpaceObject>`), plus a `ShipState[10]` side table for the
ship-only fields (§4.2). Rationale:

* The working set is 64 objects × ~200 bytes — cache behaviour is irrelevant; SoA buys
  nothing here and costs readability. Every original routine touches several fields of one
  object at a time (position + basis + velocity + radius), which AoS serves best.
* Slot numbers are semantic (0 player, 1..9 ships, 34..48 dust/stars, 61 eye, 62/63
  scratch), are stored in other fields (`owner`, `target`, `leader`, `collisionPartner`) and
  compared numerically (`obj < 10`, `obj >= 10`). Keep `int`/`sbyte` slot ids rather than
  object references; never reorder.
* NativeAOT: plain structs with blittable fields, fixed-size arrays (`InlineArray` for
  `Armor[4]`, `Shield[2]`, the 0x47-byte loadout) and no reflection or generics-over-interfaces
  in the hot loop; `Span<T>`/`ref` locals avoid copies. Mark the sim assembly
  `IsAotCompatible`.
* Keep the ship-only fields in a separate `ShipState` struct indexed by the same slot
  (only valid for 0..9) rather than fattening all 64 objects; several original arrays are
  sized 12/16 for padding only. Where the original indexes past 9 (documented quirks:
  `aeSpecialManeuver[victim]` for effects in `explode`, `reset_stress` for `obj >= 12`) the
  SDL port already guards them — replicate the *guarded* behaviour.

Suggested shape (names only):

```
struct Fixed32 (int raw)                // 24.8, with Mul/Div/Sin/Cos/Asin/Acos/Sqrt exactly as §1.2
struct FixedVector { Fixed32 X, Y, Z; } // + Add/Sub/Dot/Cross/Scale/Normalize/Magnitude/Shrink
struct Basis { FixedVector Right, Up, Forward; }

struct SpaceObject   { ObjectType Type; ObjectClass Class; FixedVector Position, Velocity; Basis Frame;
                       short PitchRate, YawRate, RollRate; int Speed; short Counter; sbyte Owner;
                       short CollisionRadius, RadarRadius, RotInertia, Scale, ScreenScale, ScreenX, ScreenY,
                       DrawX, DrawY, Distance, PrevDistance, ScreenAngle, Flip, ViewFrame, AnimDelay, AnimIndex,
                       AccumulatedDamage; FixedVector ViewPosition; sbyte LastCollision, GraceTicks; ShapeRef Shape; }
struct ShipState     { Side Side; int PilotLevel; sbyte Rating; int Fuel; short MaxSpeed, AfterburnerTimer;
                       SpecialManeuver Special; MissionType Mission; Objective Objective; Tactic Tactic; Maneuver Maneuver;
                       short Count; sbyte Sequence; short YawGoal, PitchGoal, RollGoal; sbyte PointingMode, Target;
                       Shield2 Shield, MaxShield; Armor4 Armor; short WeaponEnergy; Loadout Weapons; sbyte CoreDamage,
                       IonDriveDamage, DestroyedWeapons, Communicator, PilotHp, LastAttacker, AiCooldown, Stress;
                       uint AlertFlags; byte AlertTarget; short AlertCountdown, CollisionPartner, CollisionTime;
                       sbyte TurnRegulator, TurnInterval, TurnCount; short WingLeader; ShortVector FormationOffset;
                       short MissionShip, MissionIndex; sbyte SpawnNav, NavPathIndex; FixedVector Destination, MissionSpot;
                       sbyte ExhaustHeat, PendingMessage; short CapitalViewFrame; short CannedCommand, ActionCount; ... }
class Simulation     { SpaceObject[64] Objects; ShipState[10] Ships; Rng Rng; int Frame, RenderedFrame;
                       Camera Eye; TargetingScratch (nTargetShip/Range/Facing/vToTarget...); MissionState Mission;
                       Hazards; PlayerState (component damage, lock, selection...);
                       void HandleInput(InputFrame); void Update(); void PrepareRenderList(RenderFrame); }
```

Keep the "scratch globals" (`nTargetRange`, `nFacingToTarget`, `nTargetFacing`,
`vToTarget`, `vCollisionDelta`, `nTargetShip`, `DAT_00475e78`) as fields of `Simulation`:
many routines rely on them being set by a previous call in the same tick.

### 10.2 Deterministic math library

* `Fixed32` static helpers implemented exactly as §1.2 (double/float/truncation), unit
  tested against values captured from the reference build. Do **not** replace
  `DivideFixed` with integer division — AI geometry (facing percentages, spherical angles)
  depends on its float rounding. Use `MathF`/`Math` in `unchecked` context, and
  `(long)` truncation semantics (`(int)Math.Truncate` or direct cast, which truncates in C#).
* Trig: `SinFixed/CosFixed` via a 360-entry `int` table (generated with `Math.Sin` and
  verified), `ArcSin/ArcCos` via `Math.Asin/Acos` with the same `0.00390625f` scaling and
  `57.295779513082323` multiplier.
* RNG: explicit MSVC LCG with the four wrappers; a single instance owned by `Simulation`;
  rendering-only effects that consume randoms in the original (dust, exhaust, debris SFX
  chance, VDU portrait frames) must keep consuming from the same instance in the same order
  if replay-exactness against the original is wanted — otherwise isolate them behind a
  second generator and accept divergence (recommended: keep one generator and keep the
  original call order; it is cheap and makes behaviour comparable).
* 16-bit semantics: wrap `short` arithmetic explicitly where the original does
  (`unchecked((short)(a + b))`), especially `WrapDegrees`, `match_rotation_goal`,
  `FixedToShortSaturating`, damage arithmetic, `MeasureMessageWidth`.

### 10.3 Data-driven AI

* `ObjectTypeData[58]`, `ChildOffsets[56]`, `FormationPositions[5][8]`,
  `RatedManeuverChoices[13][9][3]`, `KilrathiManeuverChoices[5][9][3]`, `DefenseManeuvers[5]`,
  `PilotAggression[24]`, `PilotRecovery[20]`, `PilotTurnInterval[18]`,
  `ManeuverRerollChance[47]`, `PlayerDamageSystemTable[50]`, `GunRefireDelay[4]`, effect
  animation scripts, direction-frame/flip tables (62×3) — all become static readonly arrays
  (or an embedded JSON/binary resource loaded once; static arrays are simplest for AOT).
* Maneuvers: a `delegate void Maneuver(Simulation s, int ship, int target)` table of 47
  entries mirroring `apShipAiManeuverHandlers` (map 7→turn_n_kick, 30/36/45→best_strafe,
  3/46→reset, 0/1→none).
* Mission handlers: a switch on `MissionType`; objectives/tactics as enums.
* Comm orders: `Request(requester, ship, command)` with the 13 command codes.

### 10.4 What must be behaviour-exact vs. where modernisation is safe

Exact (gameplay-visible, determines mission outcomes and feel):
fixed-point arithmetic incl. float rounding; rotation/`fix_objects_ijk`; projection and
culling thresholds (they decide visibility, which feeds `easy2see`, targeting, hazards, AI
"on screen" checks); slot allocation order; `house_keep_objects` / `update_objects_in_space`
order; movement/acceleration formula; collision and force model; all damage tables and
chances; weapon numbers; AI tick scheduling, event classification thresholds, stress,
maneuver tables and handlers; mission parsing, nav triggers, waves, objectives; RNG and
its consumption order; frame rate semantics (sim at 20 Hz nominal; the KS build is
frame-locked, the sim is per-frame not per-second).

Safe to modernise (presentation only, no sim feedback): sprite rasterisation (rotated RLE
sprites can be drawn with GPU quads using the same frame/angle/scale/flip — the SDL port's
"enhanced" renderer does this), sub-pixel positions (the SDL port recomputes float screen
coordinates from `ViewPosition` only when they agree with the integer projection — do the
same to keep targeting identical), palette flashes, HUD text layout, star/dust rendering
(but keep their *random consumption* if sequence-exactness matters), sound. Frame skipping
(`nFrameSkip`) can be dropped if rendering keeps up, as long as code that keys off
`nRenderedSpaceFrame` (ship sparks every 4th rendered frame, hazard slot scheduling `% 20`,
target VDU refresh `% 8`, `check_target` lock malfunction `% 8`) is driven by an equivalent
render counter. Higher simulation rates are **not** safe without re-tuning every per-frame
constant (fuel, regen, lifetimes, speeds are per frame).

---

## 11. Open questions

1. **Pitch/yaw naming**: `ObjectTypeData.pitchRate` is applied to yaw and `yawRate` to pitch
   (in `rotate_object_to_goal`, `players_flight_dynamics`, and `Set_up_ship_info`'s
   `alter_yaw(-record.pitch)`). The reconstruction's field names may simply be swapped
   relative to the original header; the *behaviour* (which table column drives which axis)
   is what matters and is documented above. Verify against the DOS SHIP data if a table is
   ever externalised.
2. **Screen Y orientation**: the projection maps object-space +y to screen +y without
   negation; the sprite/view-geometry data must already be flipped (shapes drawn "upside
   down" relative to world up?). Confirm by comparing with the DOS build before changing the
   coordinate handedness in C#.
3. **`DivideFixed` float32 rounding**: the KS executable is a debug build (locals spilled
   to memory) so float32 rounding of the operands is real; confirm with `binary-comp`
   / runtime capture that no x87 extended-precision survives into the division. The port
   divides the float32 operands in double precision (x87 with 53-bit precision control,
   §1.2); a float32 division differs in rare cases.
4. **RNG identity**: MSVC 4.2 `rand()` is assumed to be the 214013/2531011 LCG (true for
   every MSVC CRT); the DOS original used Borland/Microsoft 16-bit `rand()` with different
   constants — irrelevant for a KS-faithful port but matters if DOS replays are a goal.
5. ~~**`explode` ace check**~~ **Resolved 2026-10-07:** `acShipRating = pilot − 5`, so the
   compared value 8 (named RATING_ACE_ICEMAN in the reconstruction) is the player persona
   (pilot 13); the test excludes the player from the survival roll and from
   `personality_killed`, values > 8 are the Kilrathi aces (§6.6).
6. **`drain_fuel`'s dead `if (anShipFuel == 0)`** (array address) — fuel exhaustion speed
   cap applies only after the next `recalc_max_velocity`. Keep the quirk.
7. **`reset_stress`** only acts for `obj >= 12` (never) — stress is never reset on target
   loss. Keep.
8. **Capital-ship shape-per-frame packets** (`aapPacketReferences[4][0x25]`) and the exact
   `SHIP.Vxx` section numbering for each type (`type + 22`) should be cross-checked against
   INSTALL.DAT of the KS install; the logical ids in §5.3 come from code constants.
9. ~~**DOS data**: whether DOS `MODULE` records use 3-byte coordinates.~~ **Resolved
   2026-10-07:** the DOS records use the KS layout with 32-bit 24.8 coordinates (§8.2).
   Related DOS/KS difference found while porting: the Spikeri (type 14) is a capital ship in
   the KS table, but the DOS data ships it as a fighter-style `SHIPTYPE.V14` (3 sections:
   sprite set, silhouette, exhaust table) while INSTALL.DAT names the logical file
   `SHIP.V14`, which does not exist in the GOG install. It appears in Secret Missions 2
   (24 records).
10. **Frame-rate**: `fSpaceFlightFrameRate` defaults to 20 (adjustable 8..32 in the KS
    build with a cheat key). The DOS game ran as fast as the machine allowed with
    `nFrameSkip` compensation; the port should fix the sim at 20 Hz and verify "feel"
    against KS.
11. The training-simulator/arcade mode (`nTrainSimActive`, waves via nav records, score)
    shares this code but was not analysed in depth.
