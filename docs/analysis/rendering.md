# Rendering: Vulkan renderer architecture (R1) and roadmap design (R2-R4)

Last updated 2026-10-07. Decision record: ADR-010 (`DECISIONS.md`). Progress, verification and
open items: `docs/progress/rendering.md`. Reference behaviour of the space view:
`docs/analysis/graphics.md`, `docs/analysis/simulation.md` §2, and the reference's SDL2
"enhanced" GL renderer `reference/wc1-re/src/sdl/gl_renderer.c` (summarised in §4.1).

| Stage | What | Status |
| --- | --- | --- |
| R1 | Classic 320x200 indexed layer, palette lookup in a shader, letterbox, filters | done, verified |
| R2 | Space sprites at output resolution under the cockpit/HUD pixels of the classic layer | contract + sprite pass done and tested with synthetic data (2026-10-07); game wiring open |
| R3 | glTF ship meshes, PBR, HDR, bloom, MSAA; sprites as fallback | design only |
| R4 | Ray-traced shadows / reflections / AO via ray query on capable GPUs | design only |

The classic layer stays the source of truth for everything the original draws in software
(cockpit art, HUD, menus, movies, nav map). Higher stages only replace what the original draws
*inside the space window*, and they always lose against a classic pixel that is not space
background (§4.4). Without a space view in the `RenderFrame` the renderer is exactly R1.

---

## 1. R1 as built

### 1.1 Projects and boundaries

```
WingCommander.Core            Rendering/: IRenderer, RendererSettings, RenderFrame, ClassicLayer,
                              PresentationLayout (letterbox math shared with the host's mouse mapping)
                              + the R2 contract (§3)
WingCommander.Render.Vulkan   VulkanRenderer : IRenderer (Vortice.Vulkan 3.3, unsafe, AOT-clean);
                              references Core only. The window system is reached through
                              IVulkanSurfaceSource (SDL3 implements it in the host).
tools/WingCommander.ShaderBuild   GLSL -> SPIR-V with shaderc from NuGet (no Vulkan SDK needed)
wc1 (exe)                     Presentation.cs + SdlVulkanSurfaceSource.cs: Vulkan by default,
                              SDL_Renderer fallback on VulkanRendererException
```

### 1.2 Objects

| Type | Lifetime | Holds |
| --- | --- | --- |
| `VulkanRenderer` | renderer | settings, statistics, capture queue, frame counter; drives the frame |
| `Internal/GpuContext` | renderer | instance, debug messenger, surface, physical + logical device, the one graphics+present queue, core-1.3-or-KHR entry points (`CmdBeginRendering`, `CmdPipelineBarrier2`, `QueueSubmit2`), memory-type and resource helpers |
| `Internal/PhysicalDeviceCandidate` | creation | per GPU: suitability (reason when not), score, roadmap capabilities |
| `Internal/Swapchain` | until resize | swapchain, image views, one render-finished semaphore per image, chosen format/colour space/present mode |
| `Internal/ClassicPass` | renderer | R8_UINT 320x200 index image, RGBA8 256x1 palette image, staging buffer (64 KiB region per frame in flight), sampler, descriptor set, pipeline (per colour format) |
| `Internal/FrameSlot` x2 | renderer | command pool + buffer, fence, acquire semaphore, capture readback buffer |
| `Internal/ShaderLibrary` | static | embedded SPIR-V (`GetManifestResourceStream`), shader modules |
| `Internal/Readback` | static | B8G8R8A8 / R8G8B8A8 / A2B10G10R10 / *_SRGB -> RGBA8 |
| `Internal/DebugMessenger` | renderer | VK_EXT_debug_utils callback (`[UnmanagedCallersOnly]`, GCHandle) -> log + statistics |
| `Internal/VulkanLoader` | process | installs SDL's `vkGetInstanceProcAddr` or opens the system loader |

### 1.3 Creation

1. Loader: SDL's `vkGetInstanceProcAddr` when the surface source has one (same loader as SDL),
   else `vulkan-1.dll` / `libvulkan.so.1` / `libvulkan.1.dylib` / `libMoltenVK.dylib`.
2. Instance: API 1.3 if the loader has it, else 1.2 (1.1 loaders -> `VulkanUnavailableException`);
   `MaxApiVersion` caps it (tests run the 1.2 path on a 1.4 driver). Window-system extensions from
   the source; `VK_KHR_portability_enumeration` + `ENUMERATE_PORTABILITY_BIT` when present.
   Validation: `VK_LAYER_KHRONOS_validation` when installed (`ValidationMode.Auto`, env
   `WC1_VULKAN_VALIDATION=0|1`), with `VK_EXT_debug_utils` messenger (also chained into
   `vkCreateInstance`) and synchronization validation through `VK_EXT_layer_settings`
   (`validate_sync`) or `VK_EXT_validation_features`.
3. Device: every GPU is evaluated (API >= 1.2, graphics queue family that can present to the
   surface, swapchain extension, surface formats/modes, dynamic rendering + synchronization2 from
   core 1.3 or the KHR extensions with their feature bits). Score: discrete 1000 > integrated 500 >
   virtual 200 > CPU 50, +10 for core 1.3; `PreferredDevice` / `WC1_VULKAN_DEVICE` (name part or
   index) overrides. `VK_KHR_portability_subset` is enabled whenever exposed. Roadmap features are
   *queried only* (acceleration structure + deferred host ops, ray query, ray tracing pipeline,
   buffer device address, descriptor indexing subset, timeline semaphores, MSAA, limits) and land
   in `VulkanCapabilities` + the start-up log.
4. Swapchain lazily on the first frame (drawable size known); offscreen target immediately.

### 1.4 Frame flow (swapchain mode)

```
Render(frame):
  drawable = source.DrawableSize            0x0 (minimised) -> FramesSkipped, return
  recreate swapchain if dirty / size changed / VSync changed      (vkDeviceWaitIdle first)
  slot = frame % 2; wait slot.fence (frame-2), collect its capture
  vkAcquireNextImageKHR(timeout 1 s, slot.imageAvailable)
      OUT_OF_DATE -> recreate + retry once; TIMEOUT/NOT_READY -> skip; SUBOPTIMAL -> render, recreate later
  record slot.cmd (pool reset):
      ClassicPass.RecordUploads: copy changed pixels/palette from the slot's staging region
          barrier SHADER_READ_ONLY|UNDEFINED -> TRANSFER_DST (src FRAGMENT_SHADER, no access: WAR)
          vkCmdCopyBufferToImage
          barrier TRANSFER_DST -> SHADER_READ_ONLY (COPY/TRANSFER_WRITE -> FRAGMENT_SHADER/SAMPLED_READ)
      target UNDEFINED -> COLOR_ATTACHMENT (src COLOR_ATTACHMENT_OUTPUT: chains with the acquire wait)
      vkCmdBeginRendering(clear black) ; viewport/scissor = PresentationLayout.Compute(...)
      classic pipeline: fullscreen triangle, palette lookup + filter ; vkCmdEndRendering
      [capture: -> TRANSFER_SRC, copy to slot.readback, buffer barrier COPY -> HOST/HOST_READ]
      -> PRESENT_SRC (dst ALL_COMMANDS, chains with the semaphore signal)
  vkQueueSubmit2(wait imageAvailable @ COLOR_ATTACHMENT_OUTPUT, signal renderFinished[image] @ ALL_COMMANDS, slot.fence)
  vkQueuePresentKHR(wait renderFinished[image]); OUT_OF_DATE/SUBOPTIMAL -> recreate next frame
```

Offscreen mode is the same without acquire/present: the target ends each frame in
`TRANSFER_SRC_OPTIMAL` (so `CaptureLastFrame` can copy at any time); the next frame's transition
waits for the previous frame's colour writes and copies (`COLOR_ATTACHMENT_OUTPUT|COPY`).

Synchronization rules that the tests and sync validation check:

- Two frames in flight; everything a slot owns (command pool, staging region, capture buffer,
  acquire semaphore) is reused only after the slot's fence signalled.
- Render-finished semaphores are per swapchain image (a present has no completion signal; the
  image's next acquire proves the previous present consumed the semaphore).
- Host writes to the staging buffer (HOST_COHERENT) are made visible by the submission;
  device writes for the host need the explicit `COPY -> HOST` buffer barrier before the fence.
- Swapchain recreation idles the device (rare) instead of tracking old images.
- No heap allocation per frame (tested: 200 frames with uploads every frame allocate 0 bytes).

### 1.5 Classic pass

- Index image R8_UINT 320x200 and palette image R8G8B8A8_UNORM 256x1, device-local, uploaded
  only when `ClassicLayer.PixelsVersion` or `Palette.Version` changed (or another layer/palette
  object is passed). Palette writes are visible at the next `Render`, like the VGA DAC.
- Fragment shader (`Shaders/classic.frag`): source position from `gl_FragCoord` and the
  letterbox rectangle (push constants); per-texel palette lookup, so filtering blends palette
  colours, never indices. Filters: Nearest; SharpBilinear = analytic "integer nearest prescale +
  bilinear" (prescale = floor(rect / source) per axis; identical to Nearest at integer scales);
  Linear. Blending happens in the palette's encoded space (like SDL's linear scaling).
- Letterbox: viewport and scissor are the `PresentationLayout.Compute` rectangle, the black clear
  provides the bars; both `AspectMode`s and integer scaling come from that one function, so the
  mouse mapping of the host always matches the picture.

### 1.6 Colour

The palette holds sRGB-encoded 8-bit values meant to be displayed as they are (VGA DAC on a CRT,
and what SDL_Renderer does). The swapchain prefers B8G8R8A8_UNORM / R8G8B8A8_UNORM with
SRGB_NONLINEAR: the shader output is stored unchanged, so palette bytes reach the screen exactly
(verified with readback). 10-bit UNORM is next. If a surface offers only *_SRGB formats the shader
decodes to linear and the hardware re-encodes (+-1). HDR output (R3) changes this: see §5.4.

### 1.7 Shaders and toolchain

GLSL in `src/WingCommander.Render.Vulkan/Shaders/`, compiled by `tools/WingCommander.ShaderBuild`
(Vortice.ShaderCompiler -> shaderc native from Silk.NET.Shaderc.Native) to SPIR-V 1.5 (Vulkan 1.2
target, optimised, no debug info, CRLF-normalised), checked in under `Shaders/Compiled/`, embedded
as resources. `ShaderTests` fail when the checked-in SPIR-V is stale; `--check` does the same for
CI. The game never contains a shader compiler.

### 1.8 Capture, diagnostics

`RequestCapture()` + `TakeCapture()` read back what was actually presented (copy recorded into
the frame, both modes); `CaptureLastFrame()` re-reads the offscreen target. `Statistics` counts
frames, skips, uploads, recreations, captures and validation messages; `Capabilities` describes
the GPU. Objects are named for RenderDoc when debug utils are active.

### 1.9 Platform notes

- **Windows** (verified, RTX 2060 SUPER, driver 616.92, Vulkan 1.4): window, offscreen, resize,
  fullscreen, minimise (0x0 extent -> skipped frames), FIFO/MAILBOX, NativeAOT (2.3 MB wc1).
  Third-party implicit layers (Overwolf, OBS, Steam) load into every Vulkan app; their loader
  notices are logged at Info.
- **Linux** (untested): loader `libvulkan.so.1`; X11 reports `currentExtent`, Wayland reports
  0xFFFFFFFF, so the extent comes from `SDL_GetWindowSizeInPixels` (fractional scaling gives a
  larger drawable, handled by the letterbox math). FIFO can block while a Wayland window is hidden:
  the acquire timeout (1 s) turns that into skipped frames instead of a hang. Mesa (RADV/ANV)
  exposes Vulkan 1.3; llvmpipe works as a CPU device (score 50).
- **macOS / MoltenVK** (untested): SDL loads `libvulkan.1.dylib` (Vulkan loader from the SDK) or
  MoltenVK directly; with the loader `VK_KHR_portability_enumeration` must be enabled, otherwise
  MoltenVK is not enumerated (done). `VK_KHR_portability_subset` is enabled when exposed (done; R1
  uses none of the restricted features: no triangle fans, no image view swizzles, no point
  sizes). Recent MoltenVK exposes Vulkan 1.3 core; older versions 1.2 + the KHR extensions (the
  tested 1.2 path). MAILBOX is not offered (vsync off -> IMMEDIATE). HiDPI: the drawable size is
  in pixels, the CAMetalLayer's `contentsScale` is set by SDL. No ray tracing on MoltenVK (R4 off).
  To verify: portability flags, R8_UINT sampling, swapchain format list, present timing.

---

## 2. What the original draws in a space frame (summary of the evidence)

From `gl_renderer.c`, `graphics.md` and `simulation.md` (line numbers in the progress notes):

- The game renders the space view into `stSpaceBuffer` (the window's size, cleared to the
  background index **0xBF**, palette (0,0,32) at runtime), draws every visible object as an RLE
  sprite in painter order (`sort_object_depth`: far first by `asObjectDistance`), then the HUD
  (brackets, gunsight, lock marker, texts) into the same buffer, and finally copies the buffer
  through the cockpit window **run mask** (`ViewGeometry` runs, PCSHIP section 6) onto the
  320x200 screen that already holds the cockpit art.
- Sprites: shape frame (RLE, transparent index 255, hot spot at (`leftExtent`, `topExtent`)),
  integer screen position (sub-pixel float position available when it agrees with the integer
  projection), angle in degrees (rotation table in 0.1 degree steps, `anRLEQuarterCosine`
  16.16 = `round(cos * 65536)`), 8.8 scale, flip bits 0x10 (x) / 0x20 (y). View frame selection
  (`get_right_shape`): 62 view directions -> 37 frames + flips for fighters, capital ships load one
  packet per view frame.
- Stars, dust, planets, projectiles, explosions, debris, exhaust are all such sprites (no lines,
  no background art). Palette effects: 0xBF hit flash, cockpit entries 185..190, whole-palette fades.
- 20 Hz simulation, frame-locked; no interpolation in the original or in the reference port.

The reference GL renderer records exactly those sprite draws (between the sort and the buffer
copy), keeps the 320x200 frame indexed, and draws each sprite as a quad at output resolution
**only where the indexed frame still shows the background index 0xBF inside the window mask**.
That one test keeps cockpit art, HUD, text and the software cursor on top without any other
bookkeeping. R2 adopts the same rule.

---

## 3. Renderer-facing data contract (Core.Rendering)

The renderer references only Core, so everything it needs about space flight is plain data in
`WingCommander.Core.Rendering`. Game fills it from Simulation and Graphics; the renderer never
calls back. All types are reusable (no per-frame allocation when the producer reuses them).

```csharp
public sealed class RenderFrame                    // existing; additive member:
{
    public ClassicLayer Classic { get; }
    public float Interpolation { get; set; } = 1;  // 0..1 from the tick before to the latest (R2b, §4.3)
    public SpaceView? Space { get; set; }          // null: classic only (R1 behaviour, wc1 today)
}

public sealed class SpaceView                      // the space window of one displayed frame
{
    public SpriteDrawList Sprites { get; }         // R2 input: screen-space sprites, back to front
    public SpriteImageCache Images { get; set; }   // decoded indexed sprite frames (shared, long-lived)
    public SpaceViewMask? WindowMask { get; set; } // cockpit window mask; null = whole screen
    public byte BackgroundIndex { get; set; }      // 0xBF: sprites only replace these classic pixels
    public SpaceViewState? Current { get; set; }   // R3: 3D state of the last 20 Hz tick
    public SpaceViewState? Previous { get; set; }  // tick before (interpolation)
    public double PresentedAt, TickMilliseconds;   // R2b: virtual time of the present, tick length
    public float InterpolationAt(double now);      // clamp((now - PresentedAt) / TickMilliseconds, 0, 1)
}
```

Implemented in `src/WingCommander.Core/Rendering/` (`SpaceView.cs`, `SpriteDrawList.cs`,
`SpriteImage.cs`, `SpriteImageCache.cs`, `SpaceViewMask.cs`, `SpaceViewState.cs`,
`ScreenRect.cs`) and consumed by `Render.Vulkan`'s sprite pass (§4).

### 3.1 Sprites (R2)

```csharp
public readonly record struct SpriteImageKey(short LogicalFile, short Section, short Frame);
    // = Simulation ShapeRef(logicalFile, section) + frame; capital ships: (type + 22, viewFrame, 0)

public sealed class SpriteImage                    // one decoded RLE frame, immutable
{
    public const byte TransparentIndex = 255;
    public int Width, Height;                      // leftExtent + rightExtent + 1, top + bottom + 1
    public int OriginX, OriginY;                   // hot spot = (leftExtent, topExtent)
    public ReadOnlyMemory<byte> Pixels;            // Width * Height palette indices, 255 = transparent
}

public sealed class SpriteImageCache               // Game: ShapeFrameDecoder.DecodeShapeFrame into it
{
    public int Generation { get; }                 // bumped by Clear() or a replaced image: renderers drop their atlas
    public bool TryGet(SpriteImageKey key, out SpriteImage image);
    public void Set(SpriteImageKey key, SpriteImage image);   // decode once, on first use
    public void Clear();                           // e.g. new mission / memory pressure
}

[Flags] public enum SpriteFlip : byte { None = 0, Horizontal = 0x10, Vertical = 0x20 }  // asObjectFlip bits

public struct SpriteInstance                       // one draw, in logical 320x200 screen pixels
{
    public SpriteImageKey Image;
    public float X, Y;                             // screen pixel of the hot spot (sub-pixel allowed)
    public float Angle;                            // degrees, clockwise on screen (asObjectScreenAngle)
    public float Scale;                            // 1 = 0x100 (asObjectScreenScale / 256)
    public float ScaleY;                           // vertical scale when it differs (HUD lines); 0 = Scale
    public SpriteFlip Flip;
    public short ObjectSlot;                       // simulation slot or -1 (pairing, R3 mesh replacement)
    public bool HasPrevious;                       // R2b: state of the same sprite one tick earlier
    public float PreviousX, PreviousY, PreviousAngle, PreviousScale, PreviousScaleY;
    public SpriteInstance At(float t);             // between the ticks (§4.3)
}

public sealed class SpriteDrawList                 // reusable array + Count, Clear()/Add()
{
    public ReadOnlySpan<SpriteInstance> Items { get; }
    public ScreenRect Clip { get; set; }           // space buffer rectangle on the screen (geometry origin + size)
}

public sealed class SpaceViewMask                  // 320x200 bytes, non-zero = space window
{
    public int Version { get; }                    // renderers re-upload when it changes
    public void SetAll(bool), SetRect(...), SetRun(x, y, length)  // Game: ViewGeometry.Runs
}
```

Semantics (identical to the software path at 1:1): frame pixel (i, j) of an unscaled, unrotated,
unflipped sprite lands on screen pixel (X - OriginX + i, Y - OriginY + j). Scale, flip and
rotation act around the centre of the hot-spot pixel: a sprite-space point p (pixels relative to
the hot spot centre) goes to `(X, Y) + R(Angle) * S * F * p` with `S = diag(Scale, ScaleY or Scale)`,
`R = [cos -sin; sin cos]` (y down, clockwise) and `F = diag(flipX ? -1 : 1, flipY ? -1 : 1)`;
cos/sin are taken from the original 0.1 degree table (`round(cos * 65536) / 65536`).

Producers: Game builds the list in `Draw_3Space_Frame`'s place, from the snapshot's per-object
view data (sorted draw order) plus the non-object sprites the reference also records (nav pointer,
title logo, launch doors). Fallback rule (from the reference): if a sprite cannot be drawn on the
GPU (no image, size rule `w*h > 0xFA00` when rotated/scaled), the Game draws it and **every later
sprite of that frame** in software into the classic layer, so painter order stays exact.

### 3.2 3D state (R2b interpolation, R3, R4)

The simulation captures its view in `WingCommander.Simulation.SpaceViewSnapshot` /
`SpaceObjectView` (`SpaceSimulation.CaptureSpaceView`, after `PrepareSpaceView`): fixed point,
simulation enums and `ShapeRef`s, screen positions relative to the view centre. Core cannot
reference those types, so the renderer-facing copy has its own names (`SpaceViewState`,
`SpaceObjectState`, `SpaceCamera`) and float/absolute-screen units; the field names are the
simulation's. Game converts once per tick (64 objects, no allocation):

| `Core.Rendering` | from `Simulation.SpaceViewSnapshot` / `SpaceObjectView` |
| --- | --- |
| `SpaceViewState.SpaceFrame`, `CameraViewMode` | `SpaceFrame`, `CameraViewMode` |
| `Camera.Position/Velocity/Right/Up/Forward` | `CameraPosition/...` (`FixedVector` / 256; basis in 8.8 -> unit) |
| `Camera.NearRadius`, `FocalLength` | `CameraNearRadius`, `(ScreenWidth & ~1) / 2` |
| `Camera.CenterX/Y`, `Viewport` | view geometry origin + `ViewCenterX/Y`; geometry rectangle (Game) |
| `Objects[i]` (+ `DrawOrder` as indices) | `ActiveObjects[i]` (+ `DrawOrder` as slots -> indices) |
| `Slot`, `SpawnId`, `Type`, `Class`, `Owner` | `Slot`, `SpawnId`, `(short)Type`, `(short)Class`, `Owner` |
| `Visible`, `IsNavPointer`, `IsSkyObject` | `Visible`, `IsNavPointer`, `Class` is Star or Planet |
| `Position`, `Velocity`, `Right/Up/Forward`, `ViewPosition` | same fields, converted to float |
| `Scale`, `CollisionRadius` | `Scale / 256f`, `CollisionRadius` |
| `Sprite` | `SpriteImageKey(DrawShape.LogicalFile, DrawShape.Section, ViewFrame)` |
| `Flip`, `ScreenAngle`, `ScreenScale` | `(SpriteFlip)Flip`, `ScreenAngle`, `ScreenScale / 256f` |
| `ScreenX/Y` | geometry origin + `ViewCenterX/Y` + `ScreenX/Y` |
| `Distance`, `ExhaustHeat` | `Distance`, `ExhaustHeat` |

```csharp
public sealed class SpaceViewState                 // one 20 Hz tick, reusable
{
    public int SpaceFrame, CameraViewMode;
    public SpaceCamera Camera;                     // world units, basis, FocalLength, CenterX/Y, Viewport
    public Span<SpaceObjectState> Objects;         // Add() / Clear(), capacity grows once
    public ReadOnlySpan<short> DrawOrder;          // indices into Objects, far -> near
    public void CopyFrom(SpaceViewState other);    // Previous <- Current without allocating
    public int IndexOfSlot(short slot);            // interpolation partner
}
```

Ownership: Game converts the simulation snapshot into `Current` once per tick (after copying the
old `Current` into `Previous`); `GameRuntime` sets `RenderFrame.Interpolation` after every host
update from the scheduler (`SpaceView.InterpolationAt`: time since the present / 50 ms, clamped
0..1). Everything is read-only for the renderer. The sprite list of §3.1
is built from the same data (`DrawOrder`, `Sprite`, `Flip`, `ScreenX/Y`, `ScreenScale`,
`ScreenAngle`), so both views of a tick always agree.

---

## 4. R2: space sprites at output resolution

### 4.1 Pass structure

Inside the same dynamic-rendering scope as the classic pass, after the classic draw:

1. **Atlas**: R8_UINT 2048x2048 (4 MiB; later a 2D array of up to 4 layers), shelf packer with
   1 px padding, keyed by `SpriteImageKey`. Images are uploaded once, through a per-frame-slot
   staging buffer (grows on demand; steady state allocates nothing). When the cache `Generation`
   changes or the atlas is full, the mapping is reset and refilled from this frame's sprites.
2. **Instances**: one 64-byte record per sprite in a per-slot host-visible vertex buffer
   (instance rate): hot spot, the 2x2 matrix (rotation x scale x flip), quad extents
   (`-OriginX - 0.5`, `-OriginY - 0.5`, width, height), atlas rectangle, magnification.
3. **Draw**: one instanced draw (4-vertex strip per sprite), viewport = the classic letterbox
   rectangle, so sprite coordinates are classic logical pixels (4:3 stretches sprites with the
   picture, exactly like the original monitor).
4. **Fragment** (`sprite.frag`): occlusion test first (logical pixel under the fragment: inside
   the clip rectangle, window mask set, classic index == `BackgroundIndex`), then the sprite
   texel(s), each looked up in the **same palette image** as the classic layer (index 255 =
   transparent), premultiplied alpha, blend `ONE, ONE_MINUS_SRC_ALPHA`, painter order = list order.

Filters follow `RendererSettings.Filter`: Nearest (exact texels), SharpBilinear (the reference's
anti-aliased sharp sampling, band = min(1, 1 / magnification)), Linear.

As built (`Internal/SpritePass.cs`, `Internal/ShelfAtlas.cs`, `Internal/SpriteTrig.cs`,
`Shaders/sprite.vert|frag`): the pass is created by the first frame that carries sprites; the
atlas starts cleared to 255 and the window mask to "all window"; uploads use the same
synchronization2 pattern as the classic pass (`FRAGMENT_SHADER|ALL_TRANSFER -> COPY` before,
`COPY -> FRAGMENT_SHADER` after); the window mask is re-uploaded only when its object or `Version`
changes. A sprite whose image is missing or larger than the atlas is skipped (the Game's
software fallback covers it). Counters: `SpritesDrawn`, `SpriteUploads`, `AtlasResets`,
`WindowMaskUploads`. Verified against a CPU reference of §3.1 (`SpriteReference` in the tests).

### 4.2 Why this order of tests

- HUD/cockpit on top for free: any classic pixel that is not 0xBF wins, including text the game
  draws into the space buffer after the sprites, the software cursor, cockpit explosions and
  software-fallback sprites. (The target brackets and the lock spiral are sprites themselves since
  R2b, §4.3, drawn after the objects.)
- Caveat inherited from the reference: a software pixel that happens to be 0xBF lets sprites show
  through (never seen in practice; a dedicated "HUD coverage" mask from the Game would remove it).
- Palette effects need nothing extra: the 0xBF flash, entries 185..190 and fades change the
  palette image, which both passes read at draw time.

### 4.3 Interpolation (R2b, display-rate motion; ADR-019)

The simulation stays at 20 Hz; the renderer draws the space view between the last two ticks. As
built, the sprites themselves are interpolated in screen space (the original's own projection);
they are not re-projected from the 3D state:

- **Pairing** (Game, `FlightSession.SpaceSprites`): while a frame is recorded, each sprite looks up
  its partner in the frame shown before: same `ObjectSlot`, same occurrence of that slot in the
  frame (an object's own sprite first, then its HUD marks), same object
  (`SpaceObjectState.SpawnId`), same kind (image, horizontal or vertical line), at most 64 logical
  pixels away. Frames pair only when they continue each other: 1-4 ticks apart, same camera view,
  same window and clip, view directions within about 25 degrees. A paired sprite gets
  `HasPrevious` and the partner's `PreviousX/Y/Angle/Scale/ScaleY`.
- **Spawn ids**: slots are reused when objects die, often within the same tick (a new bolt in a
  dead bolt's slot). `SpaceObject.SpawnId` is a port addition that changes whenever a slot gets a
  new object (`SetObjectsData`, `FindVacant3dObject`, star placement); the simulation never reads it.
- **Fixed children**: engine flames and capital-ship turrets are new objects every tick, placed
  relative to their parent (`reposition_fixed_child_objects`). They take the motion of the parent's
  sprite: their previous position is the parent's previous position plus the current offset,
  turned by the parent's turn and scaled by its change of scale; angle and scale likewise.
- **HUD marks**: the target brackets are recorded as sprites of a synthetic one-pixel image per
  colour (`SpriteImageKey(-1, 0, colour)`, stretched with `Scale`/`ScaleY`), the lock spiral as the
  cockpit shape, both tied to the target's slot, so they move with it. Erasing (space colour) and
  the CPU path draw lines as before; the CPU composer (`ComposeSpaceSprites`) fills the stretched
  pixels as rectangles.
- **Drawing**: `SpriteInstance.At(t)`: position and scale linear, the angle the short way; image,
  flip and painter order from the latest tick. Sprites without a partner (new objects, cuts,
  slot -1 extras) are drawn as recorded.
- **Timing**: the flight stamps a published frame with its present time (`SpaceView.PresentedAt`,
  virtual clock) and the tick length (`TickMilliseconds` = 50). After every host update
  `GameRuntime` sets `RenderFrame.Interpolation = clamp((now - PresentedAt) / 50, 0, 1)`: at a
  present the view shows the tick before and reaches the new tick when the next one is due, so the
  motion is even at any display rate for one tick (50 ms) of latency. The classic layer (cockpit,
  HUD text, radar) changes at the ticks. Headless runs keep `Interpolation = 1`;
  `wc1tool snap --interpolate` renders what the window shows at the given time.

Why screen space: at t = 1 the picture is exactly the original's (integer projection, sky objects,
nav pointer, canned scenes, attract mode) and every sprite producer is covered without extra math.
Within one 50 ms tick the difference to a perspective-correct re-projection is small; it shows only
for objects passing very close to the camera. R3 meshes will use `SpaceViewState.Previous/Current`
for real 3D interpolation (camera position lerp and basis slerp, object transforms).

Verified by `SpriteInterpolationTests` (Core), `InterpolatedSprites_AreDrawnBetweenTheirTicks`
(Vulkan against the CPU reference at t = 0, 0.37, 0.5, 1), `SpaceSpriteTests` (pairing; brackets
composed like the CPU draws them) and `SmoothFlightTests` (a turn at 144 Hz moves the stars on
every display frame; flames stay on their ships).

### 4.4 Game wiring (open)

Game builds a `SpaceView` per `Draw_3Space_Frame`: `Images` (process-wide cache, filled through
`ShapeFrameDecoder`), `WindowMask` from the current `ViewGeometry` runs (`SetRun`), clip = view
geometry rectangle, sprites from `SpaceViewState.DrawOrder` (+ nav pointer etc.), and stops
drawing those sprites in software when the renderer reports R2 support (an `IRenderer` capability
flag or `VulkanRenderer.SupportsSpaceSprites`). The SDL_Renderer fallback ignores `Space` and the
game keeps the software path.

---

## 5. R3: glTF ship meshes, PBR, HDR

### 5.1 Assets and loader

- Per ship type (and later capital ships, missiles, asteroids) an optional glTF 2.0 model
  (`.glb`) from a user/mod folder; nothing ships with the game data (licence).
- Loader evaluation: **SharpGLTF.Core 1.0.x** (MIT, .NET 8/10 targets, hand-written
  `Utf8JsonReader` schema reader, trim annotations, no `IsAotCompatible` flag yet) must pass a
  NativeAOT publish probe without ILC warnings; alternative is a small glTF/GLB reader on
  `System.Text.Json` source generation (meshes, metallic-roughness materials, textures, node
  transforms; no skins/animation). Preferred pipeline either way: a **tools-side converter**
  (`wc1tool model import`) bakes glTF into a compact binary (vertex/index buffers, KTX2/BC7
  textures, material table), so the game's runtime path never parses JSON.
- Orientation: model space +Z forward, +Y up, +X right, metres -> world units scale per model.

### 5.2 Rendering

- Scene target: HDR `R16G16B16A16_SFLOAT` + `D32_SFLOAT`, MSAA up to `Capabilities.MaxColorSamples`
  (4x default), the space window only (scissor = window bounds).
- Object transform from `SpaceObjectState` (Position + Right/Up/Forward, interpolated as in §4.3),
  camera from `SpaceCamera` with a projection that reproduces the original framing:
  `fovY = 2 * atan(100 / FocalLength)` for the 200-row logical screen, principal point at
  (`CenterX`, `CenterY`), reversed-Z, infinite far plane.
- Shading: glTF metallic-roughness PBR, one directional "sun" (nearest planet/star direction)
  plus an ambient term from a low-resolution star-field environment; emissive for engines.
- Stars, dust, effects, ships without a model: sprites as in R2 (drawn after the opaque meshes
  with depth test against the mesh depth; sprites get the depth of their object's view-space z).
- Post: bloom (13-tap downsample/upsample chain), exposure, tone mapping to the swapchain format;
  then the R2-style composite: mesh/sprite colour only where the classic pixel is background
  inside the window mask, classic pixels elsewhere.
- Palette effects on meshes: hit flash and fades are global colour transforms; derive them from
  the palette (`current palette / saved game palette` per channel for the fade, the 0xBF entry for
  the flash) and apply them in the composite.
- Selection per object: model available for `Type` -> mesh, else sprite (`SpaceObjectState.Sprite`).

### 5.3 Data needed in addition to §3.2 (later)

Damage state for decals (shield hit direction, armour), engine glow intensity (afterburner),
explosion phase for the effect system; all can be added as fields of `SpaceObjectState` (and of the simulation snapshot).

### 5.4 HDR output

With `VK_EXT_swapchain_colorspace` and an HDR10 surface the tone mapper targets ST2084; the
classic layer is then converted from sRGB to the output colour space with a paper-white level
(200 nits), keeping the 1:1 palette look on SDR displays.

---

## 6. R4: ray-traced effects (capable GPUs only)

- Gate: `VulkanCapabilities.SupportsRayTracedEffects` (acceleration structure + ray query +
  buffer device address + descriptor indexing) and a user setting; MoltenVK and older GPUs use
  the raster fallbacks. Features are enabled at device creation only when the setting is on.
- Acceleration structures: one BLAS per mesh (built once at load, compacted), one TLAS per frame
  (instance per visible ship, transform from the interpolated snapshot), rebuilt with
  `PREFER_FAST_BUILD` (<= 64 instances; ~0.1 ms).
- Effects in the R3 lighting shader via `GL_EXT_ray_query` (no RT pipeline needed):
  hard sun shadows between ships (1 ray per pixel), ambient occlusion (2-4 short rays + temporal
  accumulation), mirror-like reflections on high-metalness materials (1 ray, fall back to the
  environment on miss). Raster fallback: cascaded shadow map for the sun, SSAO, environment-only
  reflections.
- Budget: < 2 ms at 1440p on an RTX 2060 class GPU; the effects are optional per setting.
- `VK_KHR_ray_tracing_pipeline` stays unused unless path-traced cinematics are ever wanted.

---

## 7. Risks and open questions

- Software-fallback sprites must keep painter order; the Game needs a cheap "GPU can draw this"
  test per sprite (image present, size rule) before it starts drawing in software.
- Exactness: at integer output scales with Nearest the sprite pass reproduces the software
  rasteriser for unrotated sprites; rotated/scaled sprites differ at texel boundaries (the
  original resamples with its own integer stepping). Accepted for output-resolution rendering.
- The 0xBF test makes a 0xBF-coloured HUD pixel transparent (reference behaviour).
- Interpolated sprites vs. HUD marks: resolved in R2b by drawing the brackets and the lock spiral as
  sprites (§4.3). `--classic-space` turns off the sprite path and with it the interpolation.
- glTF loader AOT status must be verified by a publish probe before R3 starts.
