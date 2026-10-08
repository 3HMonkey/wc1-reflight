# Progress: rendering (WingCommander.Render.Vulkan)

Architecture and roadmap design: `docs/analysis/rendering.md`. Build/shader workflow:
`docs/BUILD.md` ("Vulkan renderer and shaders").

## Status

- **R1 done and verified** (2026-10-07): offscreen tests on the RTX 2060 SUPER, and on screen by
  integration through `wc1 --window-test --vulkan-validation` (resize, fullscreen,
  minimise/restore, filter/vsync/aspect switches, 8 swapchain captures checked against the front
  buffer, 0 validation messages, also as a 2.3 MB NativeAOT publish without ILC warnings). wc1 uses
  the Vulkan renderer by default with SDL_Renderer fallback; the separate smoke sample was dropped.
- **Validation hardening done** (session 2): with the Vulkan SDK 1.4.363 installed the renderer
  enables synchronization validation itself (VK_EXT_layer_settings `validate_sync`, fallback
  VK_EXT_validation_features, silently skipped otherwise; option `SynchronizationValidation`, env
  `WC1_VULKAN_VALIDATION_SYNC=0`). Every GPU test runs under validation when the layer is
  installed and fails if it produced any message (see "Verified"). Nothing had to be fixed.
- **R2 core done with synthetic data** (session 2): the renderer-facing space contract in
  `Core.Rendering` and the sprite pass in Render.Vulkan; drawn only when a `RenderFrame` carries
  `Space` with sprites, so wc1 is unaffected. Not wired into Game yet (see "Next steps").
- **Output-resolution text and key help (ADR-013) done in the renderer** (2026-10-08): text
  layer pixels, glyph atlas + text mask, game text and overlay draws, verified with synthetic
  glyphs against `GlyphRasterizer`; see the section at the end.

## Done

R1 (unchanged since the first session):
- `VulkanRenderer : IRenderer`: `Create(IVulkanSurfaceSource, ...)`, `CreateOffscreen(w, h, ...)`,
  `ResizeOffscreen`, `IsAvailable(out reason)`, capture (`RequestCapture` + `TakeCapture`,
  `CaptureLastFrame` offscreen), `Statistics`, `Capabilities`, `TargetSize/TargetFormat/PresentMode`.
- `Internal/GpuContext` (loader from SDL or system, API 1.3/1.2 + `MaxApiVersion`, portability
  enumeration/subset, validation + debug utils + sync validation, device selection with reasons,
  core-1.3-or-KHR entry points, roadmap capability detection), `Internal/Swapchain` (UNORM formats
  for exact palette colours, FIFO / MAILBOX > IMMEDIATE, recreation rules, 0x0 skip),
  `Internal/ClassicPass` (R8_UINT index + RGBA8 palette images, uploads only on version change,
  three filters, letterbox via `PresentationLayout`), `Internal/FrameSlot` (2 frames in flight),
  shaders compiled by `tools/WingCommander.ShaderBuild` and embedded.

Validation (session 2):
- `VulkanRendererOptions.SynchronizationValidation` (default true), `VulkanCapabilities.SynchronizationValidation`.
- Internal test hooks: `VulkanRendererOptions.UnlimitedValidationMessages` (layer setting
  `enable_message_limit = false`, so a repeated error fails every test that causes it) and
  `VulkanRenderer.SubmitDebugMessage` (injects a message through the real messenger).
- Tests: `ValidationLog` + `VulkanTestBase` (each test's `Dispose` drains the GPU and asserts no
  validation message); the shared `OffscreenRendererFixture` and every renderer a test creates log
  into it. The integration's `ValidationTests` (1.3 and 1.2 paths with `ValidationMode.Required`)
  stay as they are.

R2 core (session 2):
- `src/WingCommander.Core/Rendering/` (new files, plus the additive `RenderFrame.Space`):
  `SpaceView` (sprites, image cache, window mask, background index 0xBF, `Current`/`Previous`
  3D state), `SpriteDrawList` / `SpriteInstance` / `SpriteFlip` (screen-space sprites in painter
  order, reusable), `SpriteImage` / `SpriteImageKey` / `SpriteImageCache` (decoded indexed frames,
  255 transparent, `Generation`), `SpaceViewMask` (cockpit window from view geometry runs),
  `SpaceViewState` / `SpaceObjectState` / `SpaceCamera` (float copy of the simulation's
  `SpaceViewSnapshot` for interpolation and meshes; field names aligned, mapping table in the design
  doc), `ScreenRect`.
- `Internal/SpritePass` + `Shaders/sprite.vert|frag`: R8_UINT 2048x2048 atlas with a shelf packer
  (`Internal/SpriteAtlas`, since 2026-10-08 the generic `Internal/ShelfAtlas<TKey>` shared with the
  glyph atlas; reset on cache generation change or when full), per-slot instance vertex
  buffer and staging (grow only), 64-byte instance records, one instanced 4-vertex strip draw after
  the classic draw in the same rendering scope, premultiplied alpha, painter order = list order.
  Fragment: occlusion first (clip rectangle, window mask, classic index == background index, the
  reference's gl_renderer.c rule), then nearest / sharp (reference band filter) / bilinear taps
  looked up in the live palette. Rotation uses the original 0.1 degree quarter-cosine table
  (`Internal/SpriteTrig`). Window mask uploaded only on change. Created lazily.
- Statistics: `SpritesDrawn`, `SpriteUploads`, `AtlasResets`, `WindowMaskUploads`;
  `VulkanRenderer.SupportsSpaceSprites` for the Game.

## Verified (how)

`WC1_VULKAN_VALIDATION=1 dotnet test tests/WingCommander.Render.Vulkan.Tests --artifacts-path <scratch>`:
**73/73 pass, 0 skipped, ~16 s**, every GPU test under VK_LAYER_KHRONOS_validation with
synchronization validation and zero validation messages. Core, Render.Vulkan, the shader tool and
the tests build with **0 warnings** (AOT/trim analyzers on). A NativeAOT probe in the scratchpad
(1.4 MB exe: offscreen classic + sprite frame under validation) published without ILC warnings
and rendered the expected pixels.

- R1 (40 tests): exact palette colours at all 64000 source-pixel centres, letterbox ==
  `PresentationLayout` in 9 cases, filters, upload policy, 120 frames in flight with late
  captures, 0 bytes allocated per frame, resize incl. 0x0, BGRA/10-bit/sRGB targets, the Vulkan
  1.2 KHR path, lifecycle, shader staleness.
- Validation (5 tests): shared renderer runs with sync validation; an injected error/warning
  reaches the statistics and the test log; sync validation can be turned off; the integration two
  `ValidationMode.Required` tests. Experiments (reverted): a removed copy->shader dependency was
  reported as SYNC-HAZARD-WRITE-AFTER-WRITE and a wrong layout as VUID-vkCmdDraw-None-09600, both
  failed the tests.
- R2 (23 sprite tests, 22 of them on the GPU, + 5 contract tests), compared with a double-precision CPU reference of the
  contract (`SpriteReference`; pixels within 0.01 texel of a texel/quad edge are skipped as
  ambiguous, at most 1 %): unscaled sprites have exactly the software footprint (frame pixel (i, j)
  -> screen (X - OriginX + i, Y - OriginY + j)), incl. off-screen parts, hot spot outside the frame
  and overlap order; transparent index 255 shows the classic layer; occlusion by HUD pixels,
  cockpit art, the window mask and the clip rectangle; quarter turns, scales 0.5..3 and flips
  exact; arbitrary rotations (30, 45, 197.3, -12.5 degrees) match away from edges; 4:3 stretch;
  palette changes recolour sprites without re-upload; atlas uploads once per image and resets on a
  new cache generation; a full atlas resets and still draws the frame correctly; mask uploaded only
  on change; sharp/bilinear exact at texel centres; frames without sprites are bit-identical to
  classic-only frames; 200 steady-state frames with 64 rotated/scaled sprites allocate 0 bytes;
  `SpriteTrig` reproduces `anRLEQuarterCosine`. A mutation (sprites shifted by a quarter pixel in
  sprite.vert) made 19 of the sprite tests fail (reverted, SPIR-V identical again).

**Not verified:** Linux, macOS/MoltenVK; R2 on screen (no game data path yet).

## Deviations

- Capture of swapchain frames needs `RequestCapture()` before `Render()`; `CaptureLastFrame()` is
  offscreen-only.
- Filters blend palette colours in the palette's sRGB-encoded space (like SDL), not in linear light.
- One queue family must support graphics and present; swapchain recreation idles the device.
- R2 follows the reference GL renderer, not the software rasteriser, for rotated/scaled sprites:
  output-resolution sampling instead of the original integer stepping (identical for unrotated
  sprites at 1:1 and at integer output scales with Nearest).
- The Core 3D types are named `SpaceViewState` / `SpaceObjectState` because the simulation owns
  `SpaceViewSnapshot` / `SpaceObjectView` (same field names, float units, absolute screen positions).

## Open questions

- Blending in linear light as an option?
- R2 interpolation: draw sprites at display rate from interpolated 3D state (design §4.3) or keep
  the 20 Hz screen positions (exactly the original)? Proposed: setting, default on.
- A software pixel of index 0xBF lets sprites show through (reference behaviour); a HUD coverage
  mask from the Game would close that gap.

## Requests

- Game (integration of R2, design §4.4): keep a process-wide `SpriteImageCache` filled through
  `ShapeFrameDecoder` (bitmap pre-filled with 255, origin = left/top extents); per space frame fill
  `RenderFrame.Space` (window mask from the current `ViewGeometry` runs via `SetRun`, clip = the
  geometry rectangle, sprites from the simulation snapshot's draw order: key
  `(DrawShape.LogicalFile, DrawShape.Section, ViewFrame)`, position geometry origin + view centre
  + `ScreenX/Y`, `ScreenAngle`, `ScreenScale / 256`, `Flip`), and skip drawing those sprites in
  software when `VulkanRenderer.SupportsSpaceSprites` (never for the SDL_Renderer fallback). If a
  sprite cannot go to the GPU, draw it and all later sprites of the frame in software.
- Simulation: nothing needed; the Core contract maps 1:1 from `SpaceViewSnapshot` (table in the
  design doc, §3.2).

## Next steps (exact)

1. Game wiring of R2 (owner: Game; see Requests) and an on-screen check in `wc1` with
   `--vulkan-validation` in a mission (compare a capture with the software-rendered frame at 1:1).
2. R2b interpolation in the renderer: project interpolated `SpaceViewState` objects (camera lerp +
   basis slerp, original projection) for display-rate motion; tests with synthetic snapshots.
3. Linux/macOS runs (MoltenVK: portability flags, formats, present modes).
4. R3 preparation: NativeAOT publish probe of SharpGLTF.Core, or the tools-side glTF converter.

## Output-resolution text and key help (ADR-013), 2026-10-08

Contract (`Core.Rendering`, written in integration): `RenderFrame.Text` (`TextLayer`: shared
`GlyphImageCache`, `Pixels` = the presented frame without the listed glyphs' foreground, `Mask`
= 320x200 `ushort`, `GlyphInstance` list in painter order, `Version`), `RenderFrame.KeyHelp`
(`KeyHelpOverlay`, laid out by `KeyHelpLayout` into an `OverlayDrawList` in render-target
pixels), `GlyphImage` (signed distance field, 8 texels per source pixel, 4 texels padding, two
bytes per texel [distance, palette index]) and `GlyphRasterizer` (the CPU reference of the
sampling rules, also used by `wc1tool hd-text`).

### Done (Render.Vulkan)

- `ClassicPass.RecordUploads(..., TextLayer? text, ...)`: while `RenderFrame.Text` is set the index
  image holds `TextLayer.Pixels` (tracked by layer reference + `Version`) instead of
  `ClassicLayer.Pixels` (reference + `PixelsVersion`, as before); switching between the two
  sources re-uploads; the palette stays `ClassicLayer.Palette`. The sprite pass samples that image,
  so sprites show behind text over space, and the glyphs are drawn on top afterwards.
- `Internal/TextPass` + `Shaders/text.vert|frag`, created on the first frame with text instances or
  key help items (frames without either never touch it):
  - glyph atlas `R8G8_UNORM` 2048x2048: R (distance) through a LINEAR clamp-to-edge sampler with
    the lookup clamped to the glyph's own field rectangle, which equals the CPU's clamped bilinear
    taps; G (palette index) with `texelFetch` and `round(g * 255)`. Packed by
    `Internal/ShelfAtlas<TKey>` (the sprite packer, now generic; the sprite pass uses
    `ShelfAtlas<SpriteImageKey>`), glyph entries keyed by `GlyphImage` identity, so glyphs of
    different caches (text layer, key help) never mix. Reset when the text layer's or the overlay's
    cache is replaced or changes `Generation`, and when full: the frame is retried with only its
    own glyphs; if even those do not fit, the glyphs that fit are drawn in list order and a warning
    is logged once. Initialised to distance 0 (far outside).
  - text mask `R16_UINT` 320x200, uploaded when the `TextLayer` reference or `Version` changes;
    palette image and nearest sampler shared with the classic pass.
  - per frame in flight: host-visible instance vertex buffer (48-byte records: quad, the glyph's
    field rectangle in the atlas, colour, list index, flags multicolour/rectangle, ink index) and
    staging buffer (mask + new glyph fields), both grow-only.
  - two instanced 4-vertex-strip draws after the sprite draw in the same rendering scope, one
    pipeline with a push-constant mode (32 bytes, vertex + fragment):
    - game text (mode 0): quads over the glyph cell in logical 320x200 pixels, viewport/scissor =
      the letterbox `PresentationRect`. Per fragment: the logical pixel from `gl_FragCoord`
      (classic.frag's formula); mask rule `mask != 0 && listIndex >= mask - 1` with the list
      index carried in the instance (skipped instances do not shift it); coverage
      `clamp(d / texelsPerPixel + 0.5, 0, 1)` with texelsPerPixel = mean of `fwidth` of the field
      coordinate in x and y (computed before any discard; 4:3 at 1280x960: 2 and 1.67 texels);
      colour = palette[text colour], or for multicolour glyphs the bilinear blend at source
      resolution of the four nearest source pixels' palette colours (ink -> text colour).
    - overlay (mode 1): the key help items over the whole target in target pixels: rectangles
      solid, glyphs = the cell mapped onto the item rectangle in the item colour (Multicolour
      ignored); RGBA8 with red in the lowest byte, straight alpha.
    - output premultiplied (blend ONE, ONE_MINUS_SRC_ALPHA) in painter order, colours blended in
      the palette's sRGB space like classic.frag; sRGB targets get linear colours like sprite.frag.
- `Internal/KeyHelpLayoutCache`: the renderer-owned `OverlayDrawList`, laid out with
  `KeyHelpLayout.Build` again only when an input changes (overlay reference, `Version`, `Font`,
  glyph count and generation, target size, picture rectangle). Same items as a build every frame,
  but `Build` allocates (interface enumerators over the sections), which would break the
  allocation-free frame loop.
- `VulkanRenderer.SupportsText` (draws `RenderFrame.Text` and `RenderFrame.KeyHelp`); statistics
  `GlyphsDrawn`, `GlyphUploads`, `TextAtlasResets`, `TextMaskUploads`, `OverlayItemsDrawn`
  (`PixelUploads` also counts text layer uploads); swapchain format changes recreate the text
  pipeline; disposed before the classic pass (shares its palette and sampler).

### Verified (how)

`dotnet test tests/WingCommander.Render.Vulkan.Tests --artifacts-path <scratch>`: **100/100 pass**
(the 73 earlier tests + 27 new), every GPU test under the validation layer with synchronization
validation and zero messages; Render.Vulkan and the tests build with 0 warnings; shader check
(`ShaderBuild -- --check`) clean. Tests (`TextTests`, `TextContractTests`, helpers in
`TextReference.cs`): glyph images are built without game data like `GlyphImageBuilder`
(`PixelOutline.Trace/Smooth/SignedDistance`, nearest-foreground colours); `TextReference` is the
CPU reference of the whole contract at output resolution (classic nearest pass, mask rule, painter
order, `GlyphRasterizer` coverage/colour, overlay items, 8-bit quantisation after every draw,
optional linear-light blending); pixels whose centre lies within 0.02 output pixels of a logical
pixel edge or a visible quad edge are not compared.

- source switching: text pixels shown while `Text` is set, classic pixels after removing it, both
  directions re-upload, classic version bumps are ignored while text is shown;
- glyphs vs. the reference at 960x600 (3x), 1280x960 (4 x 4.8), 1000x625 (3.125x), 640x480,
  1600x1200 and 1366x768 (pillarbox), incl. glyphs partly off screen, a space, an overlapping pair:
  0 mismatches at tolerance 3 (at tolerance 1 only the sRGB test, 29 pixels, and 2 pixels of the
  resize test fail; everything else matches within 1);
- mask: 0 draws nothing, threshold vs. list index, painter order; mask 0 everywhere gives exactly
  the text layer's pixels; the mask is uploaded only for a new text version;
- multicolour glyphs (ink + two fixed colours) blend bilinearly, fixed colours stay;
- palette change recolours text without glyph, mask or pixel uploads;
- key help in the margins (1920x1080) and as a translucent panel (1280x960) matches the layout in
  target pixels with straight alpha; hidden or absent key help leaves the frame bit-identical;
- atlas: each image uploaded once, reset + correct output after a replaced glyph (Generation),
  3 frames of 40 large glyphs (full atlas, resets, still exact), an overflowing frame draws the 64
  glyphs that fit and warns once;
- 30 text frames with late captures (each shows its own text), 12 resizes with text + key help,
  sprites behind text with glyphs on top, sRGB target (linear-light reference), Vulkan 1.2 path;
- 200 steady-state frames with 120 glyphs, new pixels/mask/instances every frame, palette changes
  and a visible key help (second glyph cache) allocate 0 bytes; the layout cache rebuilds only on
  input changes (0 bytes for 100 unchanged updates).

Mutation check (scratchpad script, reverted, SPIR-V checksums identical afterwards): a quarter
pixel shift of the quads failed 20 text tests, x-only anti-aliasing width 7 (the 4:3 cases), mask
rule off by one 17, multicolour ignored 2, nearest instead of bilinear multicolour 2, rectangle
alpha ignored 3, wrong field spread 20, missing sRGB conversion 1. NativeAOT probe in the
scratchpad (1.4 MB exe: text + key help offscreen under validation) published without ILC
warnings and drew the expected pixels.

**Not verified:** real fonts and the Game's text producer (no producer yet); on screen; Linux,
macOS/MoltenVK.

### Notes and deviations

- Anti-aliasing width = arithmetic mean of the x and y texels per output pixel (as specified);
  `wc1tool hd-text` uses the geometric mean, `FieldScale / sqrt(sx * sy)` (0.42 % apart at 4:3).
- Glyph quads cover exactly the glyph cell (contract), so foreground touching the cell edge loses
  the outer half of its anti-aliasing ramp there.
- `KeyHelpOverlay.Font` does not change `Version`; the layout cache compares the font itself.
- The text layer's glyphs ignore `RendererSettings.Filter` (always output resolution); the
  text-free classic pixels under them are filtered as usual.

### Requests

- Integration / exe: `wc1 --window-test` compares swapchain captures with the front buffer; with
  `RenderFrame.Text` published the glyph pixels differ by design (compare against
  `TextLayer.Pixels` outside the glyph cells, or run the window test without text).
- Core (optional): `KeyHelpOverlay.Font` could bump `Version`; `KeyHelpLayout.Build` could avoid
  the interface enumerators (index loops) so renderers can call it every frame.

### Next steps

1. Game: publish `RenderFrame.Text` / `KeyHelp` (integration), then an on-screen check in `wc1`
   with `--vulkan-validation` at 4:3 and square pixels, filters, integer scaling.
2. Optional: blend text in linear light (same open question as the classic filters).
