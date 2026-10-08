# Progress: graphics (WingCommander.Graphics)

## Status

The 2D graphics core is ported and verified (2026-10-07). It covers surfaces and viewports,
the raster primitives (fill, blit, pixel, asm-exact clipped line DDA, asm midpoint ellipse
outline and fill), shape tables with the prepared op stream, the unscaled clipped renderer,
the asm-derived rotate/scale scan converter, bounds helpers, sprite background capture and
restore (software cursor), FONTS.FNT fonts with the text layout and printf-like token
formatter, GAME.PAL, the palette state with the saved mirror, the fades as step objects
(ADR-009), flight palette flashes, and the PCSHIP cockpit view geometry with the run-list
composite (`fizzle_fade`).

Verification:

- `tests/WingCommander.Graphics.Tests`: 153 tests, all green in about 5 s, zero warnings (Graphics, tests and wc1tool).
  - Data sweep over every graphics file (`*.VGA`, `SHIP.V*`, `PCSHIP.V*`, `SHIPTYPE.V*`,
    `MIDGAME.V*`, `INTRO1.DAT`). Every frame of every shape table is prepared, compared pixel
    by pixel with the raw-stream decoder, drawn centred, and drawn scaled, rotated and mirrored.
    Census: 670 shape tables, 2945 frames, 65 frames with the value 0xFF inside spans, no frame
    larger than 64000 px. Non-shape sections are exactly the documented raw tables.
  - Goldens derived by hand: ARROW.VGA frame 0 drawn pixel by pixel and its prepared op rows;
    font heights 11/8/6/11, inks 15/166/198/15, backgrounds 0/0/0/1, section sizes; GAME.PAL
    entries; transformed bounds; line pixel sets (round-half-up, direction-dependent);
    ellipse rx=2, ry=1 outline and fill; mapper output for a 3x2 frame at scale 2.0, at 90
    degrees and mirrored; `StepPaletteTransition` sequences, including the stop-short residual.
  - Property tests: 5000 random lines clipped equal to the unclipped DDA inside the clip;
    the rotator clipped left/top/right equals the unclipped draw inside the clip (300 random
    angles and scales); the mapper at 0 degrees and scale 1.0 equals the unscaled draw for
    every frame of ARROW, OBJECTS, SHIPTYPE.V00 and COCKPIT (height >= 2).
- `wc1tool` exports, inspected visually (PNGs in the session scratchpad, not in the repo):
  TITLE.VGA s0 (logo), s1 (shape font), s5 (title animation background and deltas); ARROW.VGA
  (cursor); COCKPIT.VGA s1; OBJECTS.VGA s5 (debris); SHIPTYPE.V00 s0 (Hornet, 37 views);
  TALKING.VGA s0 (talking head plus mouth and eye frames); fonts 0 to 3 as glyph sheets;
  wrapped and centred text; GAME.PAL swatches; PCSHIP.V00 views 0 to 3, PCSHIP.V02 view 0 and
  PCSHIP.V04 cockpit composites (a test pattern fills exactly the space window); rotated and
  scaled renders of a Hornet frame (30 deg / 1.0, 90 deg / 2.0, -20 deg / 1.56 mirrored) and
  the title logo (10 deg / 1.17), with the `GetTransformedShapeBounds` box drawn around them.

## API overview

| Area | C# |
| --- | --- |
| Surfaces | `Raster.IndexedSurface` (byte buffer placed in absolute coordinates; `FromFramebuffer` wraps `Framebuffer.Pixels` without a copy) |
| Viewports | `Raster.Viewport` (mutable class: surface + inclusive `short` rect; `Clone`/`CopyFrom` for the C struct copies; `Allocate`/`AllocateViewport`/`FreeViewport`) |
| Raster library | `Raster.RasterClip`, `Raster.RasterPrimitives` (static) |
| Game wrappers and state | `GraphicsContext` (partial: viewports, sprites, text, palette); `ScreenDirty` = `bDIBSlamPending` |
| Shapes | `Shapes.ShapeTable` (+ prepared cache), `ShapeExtents`, `PreparedFrame`, `ShapeFrameDecoder`, `RleRenderer`, `ShapeBounds`, `SpriteBackground`, `TrigTables` |
| Text | `Text.BitmapFont`, `FontCache`, `TextContext`, `TextArg`, `TextSink` |
| Palettes | `Palettes.GamePalette`, `PaletteTransition`, `FadeToColour`, `PanToScreenFade`, `GamePaletteFile`, `FlightPaletteEffects`, `PaletteColours` |
| Cockpit | `Cockpit.ViewGeometry` (+ `Mode4`/`Mode5`), `ViewGeometrySet`, `ViewRun` |
| Cursor | `Cursor.MouseCursorCompositor` |

The library never presents and never waits (ADR-009). Game-side call patterns:

```csharp
if (gfx.ClearViewport(gfx.Screen!, colour)) await PresentScreen();        // C: ClearViewport(&stScreen)
var fade = gfx.BeginFadeViewportPaletteToColour(screen, PaletteColours.Black);
while (fade.Step()) await scheduler.VerticalBlank();                       // C: FadeViewportPaletteToColour
await PresentScreen();
var pan = gfx.BeginPanToScreen(sceneBuffer, screen);                        // C: PanToScreen
await scheduler.VerticalBlank(); await PresentScreen();
while (pan.Step()) await scheduler.VerticalBlank();
await PresentScreen();
gfx.Palette.RestoreGamePalette();   // the original waited two vertical blanks first
```

## Mapping

| C function (address, file) | C# member | Status |
| --- | --- | --- |
| `AllocateViewport` (0x42E090, music.c) | `Viewport.AllocateViewport`, `Viewport.Allocate` | done |
| `free_viewport` (0x40F940, nav.c) | `Viewport.FreeViewport` | done |
| `InitializeDIBScreenViewport` (0x42F740, screen.c) | `Viewport.InitializeDIBScreenViewport`, `GraphicsContext.Screen` | done |
| `InitFullScreenViewport` (0x42F7E0, screen.c) | `Viewport.InitFullScreenViewport` | done |
| `SetViewportRect` (0x439400, screens.c) | `Viewport.SetViewportRect` | done |
| `CalcRectangleArea` (0x42E050, music.c) | `Viewport.CalcRectangleArea` | done |
| `ValidateViewportBounds` (0x440C00) / `ClipViewportToScreen` (0x440CF0, gr.c) | `Viewport.GetClip`, `GraphicsContext.ClipViewportToScreen` | done |
| `SignExtendClipCoord` (0x440BE0, killbrd.c) | replaced by `IndexedSurface` addressing (`GetRowOffset16` for the glyph quirk) | n/a |
| `ClearViewport` (0x441AE0, gr.c) | `GraphicsContext.ClearViewport` (returns true where the original presented) | done |
| `CopyViewportContents` (0x441A90, gr.c) | `GraphicsContext.CopyViewportContents` | done |
| `DrawViewportPixel` (0x441B20) / `GetViewportPixel` (0x441B60, gr.c) | `GraphicsContext.DrawViewportPixel` / `GetViewportPixel` | done |
| `DrawViewportLine` (0x441BA0, gr.c) | `GraphicsContext.DrawViewportLine` | done |
| `DrawFilledViewportRect` (0x441C70, gr.c) | `GraphicsContext.DrawFilledViewportRect` | done |
| `DrawViewportBorder` (0x441CF0, gr.c) | `GraphicsContext.DrawViewportBorder` | done |
| `DrawViewportEllipse` (0x441DD0) / `DrawViewportEllipseShadow` (0x441E70) / `FillViewportEllipse` (0x441E20, gr.c) | `GraphicsContext.DrawViewportEllipse` / `DrawViewportEllipseShadow` / `FillViewportEllipse` | done |
| `snow_viewport` (0x442300, gr.c) | `GraphicsContext.SnowViewport` (retail: draws nothing) | done |
| `fizzle_fade` (0x442200, gr.c) | `GraphicsContext.FizzleFade` | done |
| `FillRasterClip` (screens.c asm) | `RasterPrimitives.FillRasterClip` | done |
| `BlitRasterClip` (screens.c asm) | `RasterPrimitives.BlitRasterClip` (copy mode) | done |
| `SetRasterClipPixel` / `ReadRasterClipPixel` (screens.c asm) | `RasterPrimitives.SetRasterClipPixel` / `ReadRasterClipPixel` | done |
| `DrawClippedLine` (asm 0x439E39) | `RasterPrimitives.DrawClippedLine` (mode 0, the only mode the game uses) | done |
| `DrawRasterEllipse` (asm 0x43CE80) | `RasterPrimitives.DrawRasterEllipse` | done |
| `FillRasterEllipse` (asm 0x43D1C1, screens.c) | `RasterPrimitives.FillRasterEllipse` (from the asm) | done |
| `FillRasterBytes` (screens.c) | `Span.Fill` | n/a |
| `DrawSpriteTransformed` (0x440FE0, gr.c) | `GraphicsContext.DrawSpriteTransformed` | done |
| `DrawSpriteDefault` (0x441400) / `DrawSpriteScaled` (0x441FC0, gr.c) | `GraphicsContext.DrawSpriteDefault` / `DrawSpriteScaled` | done |
| `DrawSolidColourSprite` (0x441A40) / `DrawSolidColourSpriteScaled` (0x442000, gr.c) | `GraphicsContext.DrawSolidColourSprite` / `DrawSolidColourSpriteScaled` | done |
| `SetSolidColourTranslation` (0x440D10, gr.c) / `SetPaletteTranslationTable` (0x43AE3F) | `GraphicsContext.SetSolidColourTranslation` / `SetPaletteTranslationTable` | done |
| `PrepareShapeRLEData` (0x440D50, gr.c) | `ShapeTable.GetPreparedFrame` / `PrepareShapeRLEData`, `PreparedFrame.Prepare` / `Encode` | done |
| `GetPreparedShapeData` (0x4408C0) / `CheckHeapBlockSignature` (0x4408A0, killbrd.c) | the cache lives in `ShapeTable` | n/a |
| `GetShapeFrameCount` (0x4408D0) / `GetShapeFrameExtents` (0x4408F0, killbrd.c) | `ShapeTable.FrameCount` / `GetExtents` | done |
| `DecodeShapeFrame` (0x440960, killbrd.c) | `ShapeFrameDecoder.DecodeShapeFrame` / `DecodeRowStream` | done |
| `DrawRLEImage`, `DrawRLEImageColor`, `*Unclipped` (asm 0x43A974 and following) | `RleRenderer.DrawRLEImage` (translation span = Color variant) | done |
| `RotateRLEImage` (asm 0x43B469) | `RleRenderer.RotateRLEImage` | done |
| `GetRLETransformTrig` (0x43E2D3) / `TransformRLEPoint` (0x43E3B1) / `CalculateRoundedRLEFixedProduct` (0x43E38B) | `RleRenderer.GetRLETransformTrig` / `TransformRLEPoint` (the product helper is inlined) | done |
| `GetRLEImageSize`, `GetRLEImageOrigin`, `GetRLEFrameDimensions`, `GetRLEFrameExtents`, `GetRLEFrameCount` | `PreparedFrame` properties | done |
| `anRLEQuarterCosine`, `awAbsoluteCosine`, `awAbsoluteSine` | `TrigTables` (verbatim) | done |
| `GetTransformedShapeBounds` (0x442050, gr.c) | `ShapeBounds.GetTransformedShapeBounds` | done |
| `GetShapeFrameBounds` (0x435020, mathfp.c) / `GetShapeFrameExtent` (0x407710, brains.c) | `ShapeBounds.GetShapeFrameBounds` / `GetShapeFrameExtent` | done |
| `CaptureSpriteBackground` (0x441450) / `RestoreSpriteBackground` (0x441740, gr.c) | `SpriteBackground.*`, `GraphicsContext.CaptureSpriteBackground` / `RestoreSpriteBackground` | done |
| `MeasureShapeFrameStorage` (0x435340, mathfp.c) | `SpriteBackground.MeasureShapeFrameStorage` | done |
| `CaptureMouseCursorBackground` (0x435E20) / `DrawMouseCursor` (0x435EF0) / `RestoreMouseCursorBackground` (0x435FA0, eventmgr.c) | `MouseCursorCompositor.CaptureBackground` / `DrawCursor` / `RestoreBackground` (show count and cursor state stay in the Game's event manager) | done |
| `RefreshMouseCursorDisplay` (0x436060) / `SetMouseCursorShape` (0x4360F0, eventmgr.c) | primitives `ResetDamage` / `RestoreBackgroundAt`; the functions belong to Game | partial |
| `SetTextContext` (0x434FA0) / `SetTextCursor` (0x434F70) / `ResetTextCursor` (0x4353F0, mathfp.c) | `GraphicsContext.SetTextContext` / `SetTextCursor` / `ResetTextCursor` | done |
| `InitializeTextContextFromFont` (0x41D510) / `ReleaseTextFont` (0x41D590, disk.c) | `GraphicsContext.InitializeTextContextFromFont` / `ReleaseTextFont`, `FontCache` | done |
| `GetFontCharWidth` (0x434FF0, mathfp.c) | `GraphicsContext.GetFontCharWidth`, `BitmapFont.GetWidth` | done |
| `DrawTextString` (0x4350F0) / `DrawTextCharacter` (0x435290, mathfp.c) | `GraphicsContext.DrawTextString` / `DrawTextCharacter` | done |
| `DrawFontGlyph` (0x441150, gr.c) | `GraphicsContext.DrawFontGlyph` | done |
| `AppendTextCharacter` (0x435310, mathfp.c) | `GraphicsContext.AppendTextCharacter` | done |
| `FormatTextTokens` (0x413A40) / `EmitTextString` (0x413A10, cockpt.c) | `GraphicsContext.FormatTextTokens` | done |
| `DrawFormattedText` (0x413C40) / `FormatTextBufferFromStart` (0x413C70) / `AppendFormattedText` (0x413CB0, cockpt.c) | `GraphicsContext.DrawFormattedText` / `FormatTextBufferFromStart` / `AppendFormattedText` | done |
| `DrawTextAt` (0x41D5F0, disk.c) | `GraphicsContext.DrawTextAt` | done |
| `MeasureTextPixelWidthClamped` (0x418080, geom.c) | `GraphicsContext.MeasureTextPixelWidthClamped` | done |
| `print_subtitle` (0x403920) / `GetLineLength` (0x403890, mono.c) | `GraphicsContext.PrintSubtitle` / `GetLineLength` | done |
| `AllocateFontWorkspace` / `FreeFontWorkspace` (cdrom.c) | dropped (unused scratch) | n/a |
| `SetPaletteEntry` (0x4413E0) / `GetPaletteEntry` (0x4413C0, gr.c) | `GamePalette.SetPaletteEntry` / `GetPaletteEntry` | done |
| `DIBsetPalette`, `GetPaletteEntryAsWords`, `CachePaletteEntryFromWords`, `DIBwholePaletteFromTriplets`, `DIBwholePaletteFromWords` (dib.c) | palette-state part in `GamePalette` (`SetPaletteEntry`, `GetPaletteEntry`, `CachePaletteEntry`, `SetWholePaletteFromTriplets`, `RestoreGamePalette`) | done |
| `SetWholePaletteFromTriplets` (0x434FD0, mathfp.c) | `GamePalette.SetWholePaletteFromTriplets` | done |
| `MarkActivePaletteEntries` (0x441370, gr.c) / `CollectActivePaletteIndices` (0x418140, geom.c) | `GamePalette.MarkActivePaletteEntries` / `CollectActivePaletteIndices` | done |
| `StepPaletteTransition` (0x41C510, barracks.c) | `PaletteTransition.Step` | done |
| `FadeViewportPaletteToColour` (0x42A700, hudmsg.c) | `GraphicsContext.BeginFadeViewportPaletteToColour` + `FadeToColour.Step` | done |
| `PanToScreen` (0x439430, screens.c) | `GraphicsContext.BeginPanToScreen` + `PanToScreenFade.Step` | done |
| `SaveGamePalette` (0x401000) / `RestoreGamePalette` (0x401020, winmain.c) | `GamePalette.SaveGamePalette` / `RestoreGamePalette` | done |
| `LoadPaletteTripletsFile` (0x404610, cmpgn.c) | `GamePalette.LoadPaletteTripletsFile`, `GamePaletteFile` | done |
| `LoadGamePaletteFile` (0x4219C0, logic.c) | `GraphicsContext.LoadGamePaletteFile` (VGA branch) | done |
| `ResetCockpitPaletteEntries` (0x423E10, logic.c) | `FlightPaletteEffects.ResetCockpitPaletteEntries` | done |
| `TriggerPlayerHitPaletteFlash` (0x427C80) / `UpdateSpacePaletteFade` (0x427CD0) / `FadeFlightPaletteEntry` (0x427CA0, main.c) | `FlightPaletteEffects.*` | done |
| `house_keep` (0x427D40, main.c), cockpit light loop | `FlightPaletteEffects.FadeCockpitFlashEntries` (the rest of house_keep belongs to Game) | done |
| hit-direction flash in spc.c (`aPaletteFadeEntries[n][0] = 0x38`) | `FlightPaletteEffects.FlashCockpitEntry` | done |
| `ScreenViewportGeometry` / `ScreenViewportPacket` (PCSHIP section 6), `aScreenViewportGeometry[4/5]` | `ViewGeometry`, `ViewGeometrySet`, `ViewGeometry.Mode4` / `Mode5` | done |
| `set_up_screen_viewport` (0x436740, eventmgr.c) | belongs to Game (selects a `ViewGeometry`, sets view centre/origin globals) | Game |
| `dump_buffer_to_screen` (0x427A40), `clear_view_buffer` (0x427B00), `initialize_view_buffer` (main.c) | belong to Game (use `CopyViewportContents`, `FizzleFade`, `ClearViewport`) | Game |
| `Draw_3Space_Frame` (0x429DD0, hudmsg.c), `draw_sorted_objects_to_buffer` (0x4364C0, eventmgr.c), `easy2see` (winmain.c) | belong to Game/Simulation (use `DrawSpriteScaled`, `GetTransformedShapeBounds`) | Game |
| `InitializeModalTextPanel` (0x41A9D0) / `DrawModalTextPanel` (0x41AAE0) / `RestoreModalTextPanel` (0x41AB60) / `ShowModalTextPanel` (0x41AB90, geom.c) | belong to Game (UI built on these primitives; the save/restore pattern is covered by a test) | Game |
| `MeasureMessageWidth` (0x428E70, hudmsg.c) | belongs to Game (HUD message duration, not a width) | Game |
| `DIBinstall`, `DIBmakeDIB`, `DIBslam`, `DIBslamReal`, `DIBupdate`, `DIBramPalette`, `DIBwaitForVerticalBlank`, `ThrottleFrameAndDrawFps`, `Sdl*` (dib.c, screen.c, sdl/video.c) | belong to Game/Host (ADR-009/010: front buffer, renderer, scheduler) | Host |
| SDL GL "enhanced" sprite layer (sdl/gl_renderer.c) | belongs to the renderer (ADR-010 R2) | Host |
| `ApplyRLEFramePalette`, `CopyRLEFramePalette`, `SetRLEFramePalette`, `TranslateRLEFramePalette`, `CollectUniqueRLE*`, `EncodeRasterClipToRLEFrame`, `EncodeRLEScanline`, `EmitRLEScanlineRun`, `GetRLEFrameBounds` | dead (no callers; prepared palette offsets are always 0) | not ported |
| `DecodeIFFImage`, `DecodePCXImage`, `ExpandGIFLZWImage` and helpers, `CopyILBMPalette`, `BlitRawFrame`, `BlitRawScanline`, `ScrollRasterClipWrapped`, `FillRasterClipCheckerboard`, `CollectRasterClipColours`, `FadeRasterPaletteToPalette`, `InstallRasterDriverCallbacks`, `CopyRasterDriverName` | dead (no callers) | not ported |
| `ReserveContiguousPaletteEntries` / `ReleaseContiguousPaletteEntries` / `PrintPaletteAllocationMap` (screen.c) | dead | not ported |
| `TriangleRasterizerHook`, `RasterLineHook`, `shadow_draw` | empty DOS leftovers | not ported |

## Deviations

1. **Coordinates and addressing.** Everything works in absolute coordinates on an
   `IndexedSurface` that knows its origin; the 16-bit row tables, `SignExtendClipCoord`, the
   allocation registry and the prepared-pointer slot are gone. Clip rectangles are intersected
   with the surface, so an invalid viewport rectangle cannot touch memory outside the buffer
   (identical results for valid viewports).
2. **No presenting, no waiting (ADR-009).** `ClearViewport` returns true where the original
   presented (`viewport == &stScreen`); `FadeViewportPaletteToColour` and `PanToScreen` are
   split into a `Begin*` call plus `Step()` objects; `SetWholePaletteFromTriplets` and
   `RestoreGamePalette` write immediately and document the vertical-blank waits the caller
   owns. `ScreenDirty` keeps the DIBslam flag for the Game's present.
3. **Glyph addressing.** `DrawFontGlyph` uses the correct (DOS) address. The Kilrathi Saga
   top-row displacement is available behind `EmulateSagaGlyphRowQuirk` (off by default). The
   Saga's other addressing fault (`u16 rowOffset + x` adding 64 KB for negative offsets of rows
   below the top in allocated viewports with `width <= left`) is not reproduced. Writes are
   linear within the buffer like the original (a glyph crossing the right edge continues on
   the next row); writes outside the buffer are dropped.
4. **DrawTextString never hangs.** The original loops forever when the cursor starts at or
   right of the viewport's right edge (the port returns) and when a word is wider than the
   whole viewport (the port breaks the word at the overflow). The "FATAL : INVALID STRING"
   exit becomes `InvalidOperationException`.
5. **Rotator guards.** Frames over 64000 px return -4, like the SDL port (none in the DOS
   data). Samples outside the decoded bitmap are skipped (the asm would read stale scratch
   memory; never reached for valid edges). The edge search is capped at 8 iterations. The
   16-bit `imul` of `intY * W` in the asm's step table is computed in full (differs only when
   |intY * W| >= 32768, impossible at the game's scales).
6. **DrawClippedLine.** The vertical/horizontal paths return 0 (the asm returned an
   uninitialised flag). The asm's divide overflow (impossible for sane lines) returns 2. A
   per-pixel bounds check guards the draw loops (never triggers; the clipping property test
   proves it).
7. **BlitRasterClip.** Row order is reproduced (bottom-up when the copy's clip-relative source
   top is not below the destination's; this is always the case for `CopyViewportContents`).
   Within a row the copy has memmove semantics; the asm copied right-to-left dword-wise when
   `srcLeft <= dstLeft`, which differs only for horizontally overlapping copies within one
   surface in the unsafe direction (the game never does that).
8. **FillRasterEllipse** follows the asm body in screens.c (the spec assumed only the portable
   C existed; see the analysis correction).
9. **Prepared shapes** are built lazily per frame, owned by `ShapeTable` (no leak, no 1 MiB
   build buffer); the op streams are byte-identical to the original encoder.
10. **Palette transition state** is per fade object instead of one global; equivalent because
    the original's fades never overlap and each starts from a reset state.
11. **Bounds helpers.** `GetShapeFrameBounds` and `GetTransformedShapeBounds` use the strict
    frame test (the original accepted `frame == frameCount`); `GetTransformedShapeBounds`
    reduces the angle modulo 360 (the original indexed its tables unchecked);
    `GetShapeFrameExtent` returns 0 for a missing frame (uninitialised in C).
12. **FormatTextTokens.** Missing arguments read as 0 / empty; a `%` at the very end emits a
    NUL and stops (the original read past the terminator); arguments are a
    `params ReadOnlySpan<TextArg>` instead of a va_list.
13. **Fonts** are indexed unsigned (SDL port behaviour).
14. **snow_viewport** reproduces the retail no-op (the SDL port's static is not ported).
15. **PanToScreen** with an invalid destination pixel (-1..-3) uses entry `value & 0xFF`
    (the original read before the palette cache).
16. **DrawTextAt** omits the temporary swap of `context->text` (no observable effect).
17. **Buffers.** `AppendTextCharacter` drops characters that would overflow the string
    builder; sprite background capture stops when the save buffer is full (C overflowed).
18. **ViewGeometrySet** derives the geometry count from the first offset (PCSHIP.V04 holds a
    single geometry); `FizzleFade` clamps runs to both buffers.

## Open questions

1. The rotator, line and ellipse ports are verified against hand-derived expectations and
   properties, not against the real asm. A harness that runs the asm (MSVC/wibo) on the same
   inputs would make the pixel-exactness claim airtight (analysis open question 6).
2. Palette depth: the port keeps 8-bit Kilrathi Saga components; the DOS game showed
   `value >> 2` (analysis open question 3). A 6-bit DAC switch would live in the Game/renderer.
3. Glyph quirk default: off (DOS look); the Game decides whether the Saga artefact (1-pixel
   black line at the top of the cockpit) is wanted (analysis open question 2).
4. `GetTransformedShapeBounds` is only a true bound for 0..90 degrees (original formula);
   `easy2see` culling and the multi-part sprite placement inherit that.
5. `MarkActivePaletteEntries` ignores the viewport (Saga); the DOS version probably faded only
   colours used on screen (analysis open question 7).

## Requests

None. Core's `Framebuffer`, `Palette`, `PacketFile` and `GameDirectory` were sufficient;
`WingCommander.Core.Rendering` is not duplicated (the Graphics screen wraps the Game's working
`Framebuffer`, the front buffer stays `ClassicLayer`).
