# Wing Commander 1 — 2D Graphics Core (Origin "screens" library + game raster layer)

Analysis of the C reverse-engineering in `reference/wc1-re` (Kilrathi Saga Win32 build plus the
SDL2 port that also reads DOS data), prepared as the specification for porting this layer to
C# / .NET 10. Every byte-level claim about file formats below was verified against the GOG DOS data
in `<GOG install>/GAMEDAT` (TITLE.VGA, ARROW.VGA, OBJECTS.VGA, FONTS.FNT, GAME.PAL,
PCSHIP.V00) with a throw-away Python decoder; claims about the Kilrathi Saga runtime come from the
C/asm sources. Addresses in parentheses are the original Win32 code addresses used throughout the
reference repository (useful for cross-referencing `include/wc1funcs.h`).

The packet container / LZW / memory-handle layer is documented separately in
`docs/analysis/resources.md`; this document repeats only what is needed to read graphics data.

Source files covered (paths relative to `reference/wc1-re`):

| File | What lives there (graphics-relevant part) |
| --- | --- |
| `src/gr.c` | Game-side rasteriser wrappers on `Viewport`: `ValidateViewportBounds`, sprite draw entry points, `PrepareShapeRLEData`, `DrawFontGlyph`, cursor background capture/restore, `CopyViewportContents`, `ClearViewport`, pixel/line/rect/ellipse wrappers, `GetTransformedShapeBounds`, `fizzle_fade`, `snow_viewport`, palette get/set thunks |
| `src/screens.c` | **Not** the raster library proper: it is mostly cinematic/briefing screen code. The raster library is at its tail (line 1663 onward) as `__declspec(naked)` x86 asm bodies (`#include "screens_*.inc"`), compiled only on MSVC. `SetViewportRect`, `PanToScreen` are here too |
| `src/screens_portable.inc` | SDL-port C re-implementations of the asm raster library (what the SDL build actually runs). **Not all are pixel-identical to the asm** — see §5 |
| `src/screens_*.inc` (other) | the original hand-written asm bodies (rotate/scale mapper, clipped line, ellipse, RLE encoder, GIF/PCX/ILBM decoders, palette fades, …) |
| `src/screen.c` | `InitializeDIBScreenViewport`, `InitFullScreenViewport`, `ReserveContiguousPaletteEntries`, `ThrottleFrameAndDrawFps`; the rest is AI/comm-menu code |
| `src/dib.c` | Win32 "DIB" back end: DirectDraw (`#ifndef SDL_PORT`) or SDL (`#ifdef SDL_PORT`) presentation, palette cache |
| `src/text.c` | only `show_info_disp` (a VDU page); the text renderer lives in `mathfp.c`/`cockpt.c`/`disk.c` |
| `src/mathfp.c` | `DrawTextString`, `DrawTextCharacter`, `GetFontCharWidth`, `SetTextContext`, `SetTextCursor`, `GetShapeFrameBounds`, `MeasureShapeFrameStorage`, `SetWholePaletteFromTriplets` |
| `src/cockpt.c` | `FormatTextTokens`, `DrawFormattedText`, `FormatTextBufferFromStart`, `AppendFormattedText` |
| `src/disk.c` | `InitializeTextContextFromFont`, `ReleaseTextFont`, `DrawTextAt` |
| `src/killbrd.c` | `GetPreparedShapeData`, `GetShapeFrameCount`, `GetShapeFrameExtents`, `DecodeShapeFrame`, `SignExtendClipCoord`, `CheckHeapBlockSignature` |
| `src/music.c` | `AllocateViewport`, `CalcRectangleArea` |
| `src/nav.c` | `free_viewport` |
| `src/geom.c` | `CollectActivePaletteIndices`, `MeasureTextPixelWidthClamped`, `ModalTextPanel` functions |
| `src/barracks.c` | `StepPaletteTransition` |
| `src/hudmsg.c` | `FadeViewportPaletteToColour`, `Draw_3Space_Frame` |
| `src/cmpgn.c` | `LoadPaletteTripletsFile` (GAME.PAL) |
| `src/logic.c` | `LoadGamePaletteFile`, `ResetCockpitPaletteEntries`, `init_vdus`, `InitializeGameTextContexts` |
| `src/main.c` | `dump_buffer_to_screen`, `clear_view_buffer`, `initialize_view_buffer`, flight palette flash |
| `src/winmain.c` | `SaveGamePalette`, `RestoreGamePalette`, `easy2see` |
| `src/eventmgr.c` | mouse cursor draw/capture/restore, `set_up_screen_viewport`, `draw_sorted_objects_to_buffer` |
| `src/mono.c` | `print_subtitle`, `GetLineLength` (shape-based "intro font") |
| `src/cdrom.c` | `AllocateFontWorkspace`/`FreeFontWorkspace` (unused scratch) |
| `src/sdl/video.c`, `video_state.c`, `video_internal.h` | SDL presentation of the 320x200 indexed frame, 4:3 viewport mapping, EGA dither, thruster positions, static noise |
| `src/sdl/gl_renderer.c` | optional "enhanced" OpenGL space-object renderer (interface only analysed) |
| `include/wc1.h` | `Viewport`, `RasterSurface`, `RasterClip`, `RLEFrameHeader`, `RLETransformVertex`, `FontWorkspace`, `TextContext`, `MouseCursorState`, `ModalTextPanel` |
| `include/globals.h`, `src/globals.c` | the globals listed in §2.7 |

---

## 0. Big picture

* The whole game renders into **8-bit indexed 320x200 surfaces**. There is exactly one "screen"
  buffer (`stScreen`, 64000 bytes, owned by the DIB layer) plus any number of off-screen
  `Viewport`s (space view buffer, scene buffer, saved backgrounds, modal panels).
* Every drawing call goes `Viewport` → (`ValidateViewportBounds`) → `RasterSurface`+`RasterClip`
  → raster-library primitive. Coordinates handed to the game-side wrappers are **absolute screen
  coordinates** in the viewport's coordinate space; wrappers subtract `viewport.left/top` before
  calling the library, which works in clip-relative coordinates.
* Sprites ("shapes") are stored in packet sections as a nested packet of frames, each frame a
  row-oriented RLE stream with a hotspot. On first use a shape is re-encoded into an in-memory
  "prepared" RLE form (`PrepareShapeRLEData`) that the raster library draws directly (unrotated
  fast path) or rasterises through a scratch bitmap (rotate/scale path, ships in space).
* Presentation: the screen buffer is marked dirty (`DIBslam`) by any draw that touches
  `stScreen.pixels`; `DIBslamReal` draws the mouse cursor into it, pushes pixels + palette to the
  host (DirectDraw palette surface or SDL ARGB texture), restores the cursor pixels, throttles to
  the frame rate, and services audio.
* Palette: 256 x RGB. The Win32/SDL build stores **8-bit** components (straight from GAME.PAL's
  ILBM CMAP). Fades step components by 4 (= one 6-bit DAC step), once per vertical blank.

---

## 1. Data model

### 1.1 `Viewport` (`include/wc1.h`)

```c
typedef struct Viewport {
    unsigned char  *pixels;      /* +0x00 base of the pixel buffer (NOT the top-left of the rect) */
    unsigned short *rowOffsets;  /* +0x04 u16 table, see below */
    short left, top, right, bottom;   /* +0x08.. inclusive rectangle in "screen" coordinates */
    unsigned char  *allocation;  /* +0x10 owning block (free_viewport) or NULL for aliases */
} Viewport;                      /* 20 bytes */
```

Semantics (all verified against the code):

* `right`/`bottom` are **inclusive**. Width = `right-left+1`, height = `bottom-top+1`.
* **Pixel addressing invariant**: the pixel at absolute `(x, y)` with `left<=x<=right`,
  `top<=y<=bottom` is at `pixels + (uint16)rowOffsets[y] + x` **in 16-bit wrap-around arithmetic**.
  The table is indexed by the absolute row number; the value is the byte offset of column 0 of that
  row relative to `pixels`, so it may be negative (`-left`), stored as a 16-bit two's complement.
  `SignExtendClipCoord(v)` (0x440BE0) decodes it: values `>= 0xFDC0` are negative, i.e. the valid
  negative range is `-576..-1`.
* Allocation (`AllocateViewport`, music.c 0x42E090): buffer = `width*height` bytes;
  `rowOffsets` has `top + height + 2` entries, filled for rows `top .. top+height+1` with
  `r*width - left` (`r` = 0-based row in the buffer). Entries below `top` are **uninitialised**.
  Then `ClearViewport` with the clear colour unless it is `-1`. `nAllocateViewportCalls` counts.
  Buffers are registered in `apViewportAllocations[128]` so `ValidateViewportBounds` can reject
  stale pointers ("bad viewport").
* Screen (`InitializeDIBScreenViewport`, screen.c 0x42F740): rect (0,0,319,199), `pixels` = DIB
  buffer, `rowOffsets = awScreenRowOffsets[202]` with `r*320` for r in 0..201 (two spare rows).
  Requires `nVideoMode == 0x13` (MCGA); every other video mode is a DOS leftover and exits.
* **Aliases**: the game freely copies a `Viewport` struct and then shrinks its rectangle
  (`stModalSourceViewport = stScreen`, `stLeftVdu = stScreen; stLeftVdu.left = ...`,
  `targetViewport = stRightVdu; targetViewport.left = ...`, `stScreen.top = 24; stScreen.bottom = 151`
  in `InitializeConversationViewport`). The alias shares `pixels`/`rowOffsets`, so a rectangle is
  purely a clip rectangle + coordinate frame; the underlying surface is unchanged. A C# port must
  model "surface" and "viewport = surface + rect" separately.
* `free_viewport` (nav.c 0x40F940): unregisters, frees `rowOffsets` and the allocation.
* `SetViewportRect(vp, l, t, r, b)` (screens.c 0x439400) just assigns the four shorts.

Live viewport instances (globals.h): `stScreen` (0x005a6ba0), `stModalSourceViewport` (alias of
the screen used as "the current page" by menus/text), `stSpaceBuffer` (off-screen space view,
allocated with flag 0x20 and cleared to `cPrimaryViewBufferColour`; its rect is
`(0,0,nScreenWidth-1,nScreenHeight-1)` where width/height come from the active view geometry, e.g.
320x105 for cockpit view 0, 320x128 for "mode 4", 320x200 cockpitless), `stSceneBuffer`
(0,0,319,127, cinematics), `stLeftVdu`/`stRightVdu`/`stCockpitBar` (aliases of the screen with
cockpit sub-rects), `stConversationTextViewport`, `stRoom*Viewport`, `ModalTextPanel.viewport` /
`savedBackground`.

### 1.2 `RasterSurface` / `RasterClip` (raster-library view of a viewport)

```c
typedef struct RasterSurface { unsigned char *pixels; int maximumX; int maximumY; int field_C, field_10; } RasterSurface;
typedef struct RasterClip    { RasterSurface *surface; int left, top, right, bottom; } RasterClip;
```

Built by `ValidateViewportBounds(vp, &surface, &clip)` (gr.c 0x440C00), normally via
`ClipViewportToScreen(vp)` into the globals `stRasterSurface`/`stRasterClip`:

```
surface.pixels   = vp.pixels + vp.left + signext(rowOffsets[vp.top])   // address of the rect's top-left pixel
surface.maximumX = signext(rowOffsets[vp.top+1]) - signext(rowOffsets[vp.top]) - 1   // = stride - 1
surface.maximumY = vp.bottom - vp.top
clip = { &surface, 0, 0, vp.right - vp.left, vp.bottom - vp.top }
```

So `maximumX+1` is the **stride** (320 for screen aliases, `width` for allocated buffers), the
surface's row count is the viewport height, and the clip rectangle is the viewport in local
coordinates. If `vp.pixels == stScreen.pixels` the DIB is marked dirty (`DIBslam`) as a side
effect — *every* wrapper that validates a screen viewport marks the frame dirty, even reads.

**Common clip rule** (identical in every raster primitive, asm and portable):

```
w = surface.maximumX + 1; h = surface.maximumY + 1;  if w <= 0 || h <= 0 → return -1
cl = max(clip.left, 0); ct = max(clip.top, 0); cr = min(clip.right, w-1); cb = min(clip.bottom, h-1)
if cr < cl || cb < ct → return -2
x += clip.left; y += clip.top          // caller coordinates are relative to the clip origin
pixel(x,y) = surface.pixels[y*w + x]  only if cl <= x <= cr && ct <= y <= cb
```

Return codes: `-1` bad surface, `-2` empty clip, `-3` (pixel) outside / (RLE) wholly outside,
`-4` degenerate RLE frame, `0` ok. `DrawClippedLine` returns `2` for a rejected line, `1` if any
clipping happened, `0` otherwise. The game never inspects these codes.

### 1.3 Shape handle and prepared RLE cache

A "shape" pointer (`unsigned char *shape`) is the start of a packet section loaded with the tagged
flag (`AllocateTaggedMemory(size, 0x40)` → 8-byte prefix `"jeff\0\0\0\0"` immediately before the
data). `CheckHeapBlockSignature` verifies `*(int*)(shape-8) == 0x6666656a` ('jeff');
`GetPreparedShapeData(shape)` reads the pointer stored in the prefix's second dword (`shape-4`;
the SDL port stores a full pointer before the prefix). The pointer is `NULL` until
`PrepareShapeRLEData` builds the cache (§3.3). Freeing a shape (`ReleasePacketHandle`) **does not**
free the prepared block (the prepared block is a separate tagged allocation and simply leaks; this
is original behaviour).

```c
typedef struct RLEFrameHeader {          /* 24 bytes, packed */
    short height, width;                 /* +0 +2  (note: height first) */
    short topExtent, leftExtent;         /* +4 +6  (note: top first) */
    int left, top, right, bottom;        /* +8 +12 +16 +20 : pixel bounds relative to the hotspot, inclusive */
} RLEFrameHeader;
```

`RLETransformVertex` (20-byte stride: `destinationX, destinationY, reserved, sourceX, sourceY`) is
the workspace of the asm scan converter (`aRLETransformVertices[4]`, `anRLESourceSteps[4]`), see §5.3.

### 1.4 Text

```c
typedef struct FontWorkspace { int width; int height; unsigned char *pixels; } FontWorkspace;
#pragma pack(1)
typedef struct TextContext {
    Viewport *viewport;             /* +0x00 target + wrap/centre rectangle */
    short cursorX, cursorY;         /* +0x04 +0x06 absolute coordinates */
    unsigned char *font;            /* +0x08 FONTS.FNT section (see §3.4) */
    unsigned char colour;           /* +0x0C replaces the font's ink index */
    unsigned char backgroundColour; /* +0x0D replaces the font's background index; 0xFF = transparent */
    char *text, *textCursor;        /* +0x0E +0x12 string-builder buffer used by %P / FormatTextBufferFromStart */
    unsigned char alignment;        /* +0x16 2 = centre each line in the viewport, anything else = left */
    FontWorkspace **fontWorkspace;  /* +0x17 allocated but never read by any renderer */
} TextContext;                      /* 27 bytes */
```

`pCurrentTextContext` is the implicit target of `DrawTextString`/`DrawTextCharacter`/
`FormatTextTokens`/`GetFontCharWidth`. `apTextFonts[4]` caches the four FONTS.FNT sections
(logical file 0); `InitializeTextContextFromFont(ctx, fontIndex, colour, background)` loads on
demand (font 1 is loaded with flags 0x10 and is never released; `ReleaseTextFont(1)` is a no-op).
`AllocateFontWorkspace` allocates one 5x5 scratch bitmap per font that nothing uses — drop it.

### 1.5 Mouse cursor

```c
#pragma pack(1)
typedef struct MouseCursorState {
    volatile short x, y;                  /* +0 +2  hotspot position in screen coords */
    unsigned char primaryButton, secondaryButton, reserved;
    unsigned short flags;                 /* +7 */
    unsigned char *volatile shape;        /* +9  ARROW.VGA section 0 (logical file 14) */
    unsigned short frame;                 /* +0xD  0 arrow, 1 crosshair, 2 target ring (verified) */
    unsigned int reservedAfterFrame;
    Viewport *volatile viewport;          /* +0x13 where the cursor is composited (usually &stScreen) */
    unsigned int reservedAfterViewport;
    unsigned char shapeChanged;           /* +0x1B */
} MouseCursorState;                       /* 0x1C bytes; snapshotted by the event manager */
```

The cursor is a normal shape drawn with `DrawSpriteDefault` into `stMouseCursorState.viewport` just
before presenting and undone right after (§4.8). `abCursorSaveArea[0x1000]` holds the covered pixels.

### 1.6 `ModalTextPanel` (geom.c) — the one place a background is saved by viewport copy

`InitializeModalTextPanel(panel, fontIndex, topLeft, bottomRight, clearColour, background, border)`:
copies `stDefaultTextContext`, sets `panel->viewport = stModalSourceViewport` restricted to the panel
rect, allocates `panel->savedBackground` with the same rect, `CopyViewportContents(viewport →
savedBackground)`, erases, draws a border with `DrawViewportBorder`. `RestoreModalTextPanel` copies
it back and frees. Good test case for the rect-to-rect copy semantics (§4.3).

### 1.7 Globals touched by this layer (`include/globals.h`)

| Global | Type / size | Role |
| --- | --- | --- |
| `stScreen` | `Viewport` | the presented 320x200 frame |
| `stModalSourceViewport`, `stSpaceBuffer`, `stSceneBuffer`, `stLeftVdu`, `stRightVdu`, `stCockpitBar`, `stConversationTextViewport` | `Viewport` | see §1.1 |
| `awScreenRowOffsets[202]` | `u16` | screen row table |
| `apViewportAllocations[128]`, `nViewportAllocationCount` | | registry used by `ValidateViewportBounds` |
| `stRasterSurface`, `stRasterClip` | | scratch surface/clip for the current wrapper call |
| `abShapeTransformScratch[0xFA00]` | 64000 bytes | rotate/scale decode scratch (max frame area) |
| `abShapeRLEScratch[0x100000]` | 1 MiB | `PrepareShapeRLEData` build buffer |
| `abRasterPaletteTranslation[256]` | | raster-library colour translation (mode 1 / `flags&1`) |
| `abSolidColourTranslation[256]` | | built by `SetSolidColourTranslation` |
| `abPaletteTranslation[256]` | identity initially | font ink/background remap (restored after each glyph) |
| `anRLEQuarterCosine[901]` | `int` 16.16 | cos(a/10 deg), a = 0..900, [0] = 65536 |
| `awAbsoluteCosine[360]`, `awAbsoluteSine[360]` | `u16` 8.8 (max 255) | bounds estimation only |
| `aRLETransformVertices[4]`, `anRLESourceSteps[4]` | | asm mapper workspace |
| `szShapeRLEVersion` | `"1.00"` | prepared-shape magic |
| `pDIBPixelBuffer`, `abDIBPixelBackup[0xFA00]`, `nDIBWidth`, `nDIBHeight`, `bDIBSlamPending`, `nDIBSlamCount` | | DIB state |
| `abDIBPaletteCache[1024]` | `[B,G,R,flag]` x256 | the live palette |
| `awPaletteRgbWords[0x300]` | `u16` R,G,B x256 | "saved game palette" (`SaveGamePalette`/`RestoreGamePalette`) and mirror of every `DIBsetPalette` |
| `abPaletteTriplets[256][3]` | `u8` | scratch whole palette used by `FadeViewportPaletteToColour` |
| `awPaletteEntryAllocation[256]` | | `ReserveContiguousPaletteEntries` bookkeeping — allocated but **no caller outside screen.c** (dead) |
| `asDamageFlashColour[3]`, `aPaletteFadeEntries[6][3]` | `short` RGB | flight palette flashes (§4.4) |
| `pCurrentTextContext`, `apTextFonts[4]`, `apFontWorkspaces[4]`, `stDefaultTextContext`, `st*TextContext` | | text |
| `stMouseCursorState`, `stHostMouseState`, `abCursorSaveArea[0x1000]`, `nMouseCursorShowCount`, `nMouseCursorDrawnX/Y`, `nMouseCursorDamage*`, `bMouseCursorDrawn`, `pDrawnMouseCursorShape` | | cursor |
| `cScreenViewportMode`, `pScreenViewportGeometry`, `pScreenViewportPacket`, `aScreenViewportGeometry[6]`, `nScreenWidth/Height`, `nViewCenterX/Y`, `nViewportOriginX/Y`, `bCockpitlessView` | | space-view geometry (§3.6) |
| colour constants: `cBlackColour=0`, `cViewportClearColour=15`, `cBlueColour=0x25`, `cYellowColour=0x47`, `cRedColour=0x50`, `cPrimaryViewBufferColour=0xBF`, `cPrimaryTextColour=0xA6`, `cDarkGreenColour=0xAA`, `cOrangeColour=0x85`, `cDarkBlueColour=0x27`, `cDarkGreyColour=7`, `cLightGreyColour=0x0B` | `u8` palette indices | |
| `nVideoMode` | `u16` | always 0x13 in the Win32 build |
| `nFrameIntervalMs`, `nFrameDeadlineMs`, `fSpaceFlightFrameRate` (8..32), `fCinematicFrameRate`, `bSpaceFlightFrameTiming` | | frame pacing |

---

## 2. Binary formats

### 2.1 Packet container (summary; full spec in resources.md §1.1)

```
u32 declaredFileSize
u32 entry[i] for i in 0..N-1 : bits 31..24 compression flag, bits 23..0 absolute offset of section i
      N = (entry[0] & 0xFFFFFF) / 4 - 1 ; section i spans [offset_i, offset_{i+1}) (last: to declaredFileSize)
flag 0x01 = Origin LZW with u32 uncompressed-size prefix; 0x00 / 0x02 = stored; 0xFF = empty (length 0)
```

Verified: `TITLE.VGA` = 18 stored sections (flag 2); `FONTS.FNT` = 4 LZW sections (12949, 6660,
4342, 17470 bytes decoded); `COCKPIT.VGA` section 3 is flag 0xFF/empty; `ARROW.VGA` = 1 stored
section. In Kilrathi Saga data the top-level flags are 0x00 (stored), which the loader treats the
same way.

### 2.2 Shape table (a stored section of `*.VGA`, `SHIP.*`, `PCSHIP.*`, `OBJECTS.VGA`, …)

A shape is itself a packet (nested, one level) whose "sections" are frames:

```
+0x00 u32 size            == length of the section (TITLE.VGA s0: 0x5A45 = 23109 ✓)
+0x04 u32 frameOffset[0]  low 24 bits = offset of frame 0 from the shape start; also = 4 + 4*frameCount
+0x08 u32 frameOffset[1] ...
frameCount = (*(u16*)(shape+4) >> 2) - 1                          (GetShapeFrameCount, 0x4408D0)
frame f exists iff f*4+4 < *(u16*)(shape+4)                       (bounds check used everywhere)
frame f header at shape + *(u32*)(shape + 4 + 4*f)                (the code does NOT mask the flag byte; it is 0 in all data)
```

Frame record:

```
+0 i16 rightExtent   (xmax  >= 0)        pixel x range relative to hotspot: [-leftExtent, +rightExtent]
+2 i16 leftExtent    (= -xmin >= 0)
+4 i16 topExtent     (= -ymin >= 0)      pixel y range: [-topExtent, +bottomExtent]
+6 i16 bottomExtent  (ymax >= 0)
+8 row stream (below)
width  = leftExtent + rightExtent + 1 ;  height = topExtent + bottomExtent + 1   (GetShapeFrameExtents, 0x4408F0)
```

Note the hotspot does not have to be inside the image: TITLE.VGA section 0 frame 0 has
`right=0, left=102, top=45, bottom=63` → 103x109 pixels all at x <= 0 (it is the left half of the
title logo drawn at the screen centre); frame 2 is the mirror (`right=102, left=0`).

**Row stream** (`DecodeShapeFrame` 0x440960, `CaptureSpriteBackground` 0x441450,
`MeasureShapeFrameStorage` 0x435340 all parse it identically):

```
repeat:
    u16 rowCode ; if rowCode == 0 → end of frame
    i16 dx, i16 dy                      ; span start = hotspot + (dx, dy)
    if rowCode & 1:
        N = rowCode >> 1                ; pixel count of this span
        while N > 0:
            u8 c
            if c & 1: n = c >> 1 ; u8 colour ; emit n × colour      (fill)
            else:     n = c >> 1 ; emit next n bytes                (literal)
            N -= n                      ; (n <= 127; the decoders do not guard against N going negative)
    else:
        N = rowCode >> 1 ; emit next N bytes                        (one literal span, N <= 32767)
```

A span is always contiguous and opaque; transparency is expressed by *gaps between spans* (several
spans may share the same `dy`). The stream carries no explicit transparent colour, but the decoders
pre-fill the target bitmap with 0xFF and the prepared-RLE encoder treats 0xFF as transparent, so a
0xFF byte inside a span becomes transparent after preparation. *(Corrected 2026-10-07: this does
occur: 65 frames of the DOS data contain 0xFF inside spans (SHIP.Vnn, OBJECTS.VGA s0, PLANETS.VGA
s6, BRIEFING.VGA s9); those pixels are transparent in the game. Census of all graphics files:
670 shape tables, 2945 frames, largest frame 320x200.)*

Verified decode of ARROW.VGA frame 0 (10x14, hotspot (1,2) = the arrow tip) and TITLE.VGA frame 0
(103x109) — see the decoder in the analysis notes; both render as expected.

Where shapes are used: everything 2D (backgrounds are single-frame shapes drawn at (0,0), e.g.
`DrawSpriteDefault(&stScreen, 0, 0, shape, 0)`), ships in space (`OBJECTS.VGA` and `SHIP.Vnn`
frames via `DrawSpriteScaled`), cockpit art, the mouse cursor, the shape-based "intro font"
(TITLE.VGA section 1: 60 frames, index = `c - 'A'` for `'A'..'z'`, 58 = '.', 59 = ',';
frames 26..31 — the six characters between 'Z' and 'a' — have all-zero extents, i.e. 1x1 frames
with no spans, verified).

### 2.3 Prepared RLE block (`PrepareShapeRLEData`, gr.c 0x440D50; in-memory only)

Built once per shape from the decoded frames (each frame is decoded into a `width*height` bitmap
pre-filled with 0xFF, then re-encoded row by row):

```
+0x00 char[4] "1.00"
+0x04 u32 frameCount
+0x08 frameCount × { u32 frameOffset (from block start) ; u32 paletteOffset (always 0 — ApplyRLEFramePalette & co. are dead) }
then per frame: RLEFrameHeader (24 bytes, §1.3) with
      height, width, topExtent, leftExtent (shorts) and
      left = -leftExtent, top = -topExtent, right = width-leftExtent-1 (= rightExtent), bottom = height-topExtent-1 (= bottomExtent)
      followed by exactly `height` rows, each:
         u8 op:
           0x00            end of row
           0x01, u8 n      skip n pixels (transparent)                       (n <= 255)
           odd  c >= 3     copy (c>>1) literal pixel bytes                    (encoder caps runs at 127 → c <= 0xFF)
           even c >= 2     fill (c>>1) pixels with the next byte              (decoders support it; the encoder never emits it)
```

Every row begins at x = `header.left` (bitmap column 0). The block is assembled in
`abShapeRLEScratch` (1 MiB; overflow → `exit_squadron("qq copy overflow")`) and copied into a
tagged allocation. Encoder details (replicate exactly if you keep the format): scan each row; a
non-0xFF pixel starts a literal run that continues while pixels are non-0xFF and the run is shorter
than 0x7F; a 0xFF pixel starts a skip run while 0xFF and shorter than 0xFF; each row is terminated by
0x00 even if empty.

The asm decoders (`DrawRLEImage` 0x43A974 and friends) confirm the opcode semantics: `shr al,1` →
carry = literal/skip bit, zero flag = (count==0): `ja` even non-zero → fill, `jnz` odd → literal,
`jc` (odd & count 0 → op 1) → skip, else (op 0) → end of row.

### 2.4 Fonts (`FONTS.FNT`, logical file 0, four LZW sections; verified by decoding all four)

```
+0x000 i16 height                 glyph height in pixels (all glyphs share it)
+0x002 u8  inkIndex               palette index used for "ink" pixels in the glyph bitmaps
+0x003 u8  backgroundIndex        palette index used for "background" pixels
+0x004 u8  width[256]             advance/bitmap width per byte value (0 for unused codes)
+0x104 u8  offsetLow[256]         glyph bitmap offset (from the section start), low byte
+0x204 u8  offsetHigh[256]        high byte  (offset = low | high<<8, fits because sections < 64K)
+0x304 glyph bitmaps, tightly packed in code order: width[c] * height bytes, row-major, one byte per pixel
```

Verified values: font 0: height 11, ink 15, bg 0 (12949 = 0x304 + Σ width·height ✓); font 1:
height 8, ink 166 (0xA6 = `cPrimaryTextColour`), bg 0 — the VDU/cockpit font; font 2: height 6,
ink 198, bg 0 — the tiny font; font 3: height 11, ink 15, bg 1, glyph pixels use indices
1,2,3,4,5,6,8,10,12 (a multi-colour display font; lowercase codes have width 1). 253 of the codes
1..255 have a non-zero width in every font (code 0 never does); width[' '] = 4 in all four fonts. Glyph bitmap pixel values: `inkIndex` → drawn with
`TextContext.colour`, `backgroundIndex` → drawn with `TextContext.backgroundColour` (0xFF = not
drawn), 0xFF → never drawn, anything else → drawn as-is (that is how font 3 is multi-coloured).

### 2.5 Palettes

**`GAME.PAL`** (1120 bytes) is an IFF ILBM with only `BMHD` (320x200, 8 planes, masking 2,
transparent colour 0xFF, aspect 5:6) and a 768-byte `CMAP`, no `BODY`:

```
0x00 "FORM" u32be 1112 "ILBM"  0x0C "BMHD" u32be 20 + 20 bytes  0x28 "CMAP" u32be 0x300  0x30 768 bytes R,G,B (8-bit, 0..255)
```

`LoadPaletteTripletsFile("game.pal")` (cmpgn.c 0x404610) ignores the IFF structure: it seeks to
**0x30** and reads 768 bytes straight into the DIB palette as 8-bit components
(`SetWholePaletteFromTriplets` → `DIBwholePaletteFromTriplets`). The raster library's own ILBM/PCX/
GIF palette readers (`CopyILBMPalette` etc.) shift each component `>> 2` to 6 bits — they are the
DOS-era path and are **not called by the game**. 614 of the 768 CMAP bytes have the low 2 bits set,
so 8-bit vs 6-bit matters for exact colour matching with the DOS version (DOS = `value >> 2`).

Startup sequence (`LoadGamePaletteFile`, logic.c 0x4219C0): load GAME.PAL →
`ResetCockpitPaletteEntries` (entries 185..190 ← black, entry `cPrimaryViewBufferColour` (191) ←
(0,0,32)) → `SaveGamePalette` (copy the 256 live entries into `awPaletteRgbWords`). From then on
`RestoreGamePalette` = wait for vblank + reload that copy. GAME.PAL itself defines 185..190 as
magenta shades (191,0,191)… and 191 as black; the runtime overrides win.

**`CONVERT.PAL`** (256 bytes) is the EGA conversion table: byte `i` holds two 4-bit EGA colour
indices for VGA index `i` (low nibble / high nibble); the SDL `--ega` filter picks the high nibble
on `(x+y)` odd and the low nibble on even pixels and displays through the fixed 16-colour EGA
palette. Not used by the VGA game path.

**Palette representation in the Win32 build**: `abDIBPaletteCache[i*4] = {B, G, R, flag}`;
`DIBsetPalette(index, short rgb[3])` (0x432F10) writes R,G,B (low byte of each short) into the
cache and into `awPaletteRgbWords[index*3..]`, only if changed; `GetPaletteEntryAsWords` reads
back `{R,G,B}` as three `u16`. The DirectDraw build pushed single entries to the hardware palette
immediately; the SDL build applies the cache on the next presented frame (palette changes during
flight therefore show up with the next frame — intended, the comment in dib.c says so).

### 2.6 Space-view geometry (`PCSHIP.Vnn` section 6, and the two built-in records)

```c
typedef struct ScreenViewportGeometry { short width, height, originX, originY; short fadeData[]; } ;
```

Section 6 of a `PCSHIP` file: `u16 bufferSize` (0xBB80 = 320*150 in PCSHIP.V00 — not a count),
`i16 offset[4]` (one geometry per cockpit view 0..3), then the records. *(Corrected 2026-10-07:
PCSHIP.V04 holds a single geometry: its first offset is 4 (232x81 at (40,29), 91 runs, buffer size
18792), and sections 1..5 of that file are empty. The number of offsets is `(offset[0] - 2) / 2`.
Views whose width is 320 use runs that continue into the next row (e.g. V00 view 0 run (0,19,11840)
covers 37 rows); narrower views never do.)* Each record:
`width, height, originX, originY`, then a run list of `(i16 destX, i16 screenY, u16 byteLength)`
terminated by `destX == -1`. Verified PCSHIP.V00: view 0 = 320x105 at (0,10), 105 runs; view 1 =
313x150, 213 runs; view 2 = 320x150, 213 runs; view 3 = 317x50 at (0,19), 78 runs. The built-in
records (`aScreenViewportGeometry[4]` = `{320,128,0,24, runs: (0,24,40960), -1}` and `[5]` =
`{320,200,0,0, runs: (0,0,64000), -1}`) each hold one run covering the whole buffer.
`set_up_screen_viewport(mode)` (eventmgr.c 0x436740) selects the record and sets
`nScreenWidth/Height`, `nViewCenterX/Y`, `nViewportOriginX/Y`.

`fizzle_fade(src, dst, geometry)` (gr.c 0x442200 — the name is historical, there is no fizzle):

```
sourceLeft = originX ; sourceTop = originY
for each run (destX, screenY, length) until destX == -1:
    memcpy(dst.pixels + dst.rowOffsets[screenY] + destX,
           src.pixels + src.rowOffsets[screenY - sourceTop] - sourceLeft + destX, length)
if dst is the screen → DIBslam
```

i.e. the space buffer row `screenY - originY` is copied to screen row `screenY` for the pixels not
covered by the cockpit frame (runs may span several rows because rows are contiguous: 40960 =
320x128). This is how `dump_buffer_to_screen` composites the space view under the cockpit in modes
0..3; mode 4 uses `CopyViewportContents` into `stScreen` temporarily restricted to rows 24..152;
mode 5 copies the whole buffer; cockpitless copies the whole buffer.

### 2.7 GIF / PCX / ILBM decoders — dead code

The raster library contains full decoders (`DecodeIFFImage` 0x43E7C6 with ByteRun1 + planar→
chunky, `DecodePCXImage` 0x43E9EB, `ExpandGIFLZWImage` 0x43EC29 and its five register-convention
helpers, `BlitRawFrame`/`BlitRawScanline`, `EncodeRasterClipToRLEFrame`, `TranslateRLEFramePalette`,
`FadeRasterPaletteToPalette`, `CollectRasterClipColours`, `ScrollRasterClipWrapped`,
`FillRasterClipCheckerboard`, `GetRLEFrameBounds`, `CopyRLEFramePalette`/`SetRLEFramePalette`/
`ApplyRLEFramePalette`, driver-callback glue). None of them is referenced from any game file (grep
over `src/*.c` excluding `screens.c`: zero callers); the repository marks most as "believed
unreachable". **Do not port them.** The only raster-library entry points the game uses are:
`SetPaletteTranslationTable`, `RotateRLEImage` (→ `DrawRLEImage`, `DrawRLEImageColor`, the
`*Unclipped` fast paths, `GetRLEImageSize`, `GetRLEImageOrigin`, `GetRLETransformTrig`,
`TransformRLEPoint`, `FillRasterClip`), `BlitRasterClip`, `FillRasterClip`, `SetRasterClipPixel`,
`ReadRasterClipPixel`, `DrawClippedLine`, `DrawRasterEllipse`, `FillRasterEllipse`
(+ `GetRLETransformTrig` from the SDL GL renderer).

---

## 3. Function inventory

Legend for the "Platform" column: **L** = pure logic (port as is), **D** = DIB/host dependent,
**S** = SDL-port addition, **X** = dead in the shipped game.

### 3.1 Viewport management

| Function (addr) | Signature | Purpose / semantics | Globals | Platform |
| --- | --- | --- | --- | --- |
| `AllocateViewport` (music.c 0x42E090) | `u16 (Viewport*, short clearColour, short flags)` | allocate buffer + row table for the rect already in the struct (§1.1); `flags+2` goes to the allocator; clear unless `clearColour == -1`; returns 0 on OOM | `apViewportAllocations`, `nViewportAllocationCount`, `nAllocateViewportCalls` | L |
| `free_viewport` (nav.c 0x40F940) | `void (Viewport*)` | unregister + free | same | L |
| `InitializeDIBScreenViewport` (screen.c 0x42F740) | `u16 (Viewport*, u16)` | bind `stScreen` to the DIB buffer, fill `awScreenRowOffsets` | `awScreenRowOffsets`, `pAllocatedScreenViewport` | D |
| `InitFullScreenViewport` (0x42F7E0) | | set (0,0,319,199) then `AllocateViewport` | | L |
| `SetViewportRect` (screens.c 0x439400) | `void (Viewport*, u16 l,t,r,b)` | assign rect | | L |
| `CalcRectangleArea` (music.c 0x42E050) | `short (const Viewport*)` | `w*h` as short | | L |
| `ValidateViewportBounds` (gr.c 0x440C00) / `ClipViewportToScreen` (0x440CF0) | | build `RasterSurface`/`RasterClip` (§1.2); exits on unknown buffer; marks DIB dirty for the screen | `stRasterSurface`, `stRasterClip` | L (+dirty flag) |
| `SignExtendClipCoord` (killbrd.c 0x440BE0) | `int (short)` | `v >= 0xFDC0 ? (short)v : (u16)v` | | L |

### 3.2 Fills, copies, pixels, lines, rectangles, ellipses (game wrappers in gr.c)

| Function (addr) | Signature | Semantics | Platform |
| --- | --- | --- | --- |
| `ClearViewport` (0x441AE0) | `void (Viewport*, short colour)` | `FillRasterClip` over the whole rect (skipped if `pixels`/`rowOffsets` null); if `viewport == &stScreen` (pointer compare!) → `DIBslam(); DIBslamReal()` i.e. **presents immediately** | L (+present) |
| `CopyViewportContents` (0x441A90) | `void (Viewport *src, Viewport *dst)` | `BlitRasterClip(srcClip,0,0,dstClip,0,0,0xFFFFFFFF)`: copies `min(w)×min(h)` pixels from the **top-left of src's rect to the top-left of dst's rect** (rects need not share coordinates); overlap-safe (`memmove`, bottom-up when same buffer and `dstY > srcY`) | L |
| `DrawViewportPixel` (0x441B20) / `GetViewportPixel` (0x441B60) | `(Viewport*, short x, short y[, short colour])` | clipped single pixel (absolute coords); `Get` returns -1/-2/-3 for invalid | L |
| `DrawViewportLine` (0x441BA0) | `(Viewport*, x1,y1,x2,y2, colour)` | `DrawClippedLine(mode 0)` — §5.4 | L |
| `DrawFilledViewportRect` (0x441C70) | `(Viewport*, l,t,r,b, colour)` | one horizontal `DrawClippedLine` per row `t..b` inclusive (no x ordering requirement: lines handle either direction) | L |
| `DrawViewportBorder` (0x441CF0) | `(Viewport*, l,t,r,b, colour)` | four clipped lines: top, bottom, left, right (corners drawn twice) | L |
| `DrawViewportEllipse` (0x441DD0), `DrawViewportEllipseShadow` (0x441E70, identical body) | `(Viewport*, x, y, short verticalRadius, short horizontalRadius, colour)` — **note argument order: vertical radius first** | outline, §5.5; marks DIB dirty if screen. *(Corrected: unlike every other wrapper, x and y are passed to the raster library unchanged, i.e. they are relative to the viewport's top-left corner, not absolute.)* | L |
| `FillViewportEllipse` (0x441E20) | same order | filled ellipse (§5.5) | L |
| `snow_viewport` (0x442300) | `(Viewport*, int effect, u16 colour)` | retail: **draws nothing** (body reduced to `DIBslam` + empty hook); SDL port adds xorshift static (`SdlDrawViewportStatic`) — a port can choose | X / S |
| `TriangleRasterizerHook` (0x441BF0), `RasterLineHook` (0x441140), `shadow_draw` | empty | DOS leftovers | X |
| `FillRasterClipCheckerboard` | | unreferenced | X |

Raster-library bodies used by the above: `FillRasterClip(clip, colour)` = memset of each clipped row;
`BlitRasterClip(src, sx, sy, dst, dx, dy, colour)`: clip both rects, offset the start points into
each other's valid range, copy `min` width/height; if `(colour & 0xFFFFFF00) == 0` it **fills** with
`colour` instead of copying (never used that way by the game); `SetRasterClipPixel` returns the
previous pixel value. *(Corrected 2026-10-07 from the asm: only the difference `(sx-dx, sy-dy)` matters;
the asm copies the whole intersection of the source's valid rect and the translated destination
rect, also left/above of the given points. Copy order is decided on clip-relative coordinates, not
on buffer identity: rows bottom-up when the source copy top `<=` the destination copy top, columns
right-to-left (dword `movsd` with `std`) when the source copy left `<=` the destination copy left.
For `CopyViewportContents` both are 0, so it always copies bottom-up, right-to-left. The portable C's
"same buffer and dstY > srcY" rule is the SDL port's.)*

### 3.3 Sprites (gr.c)

| Function (addr) | Signature | Semantics | Platform |
| --- | --- | --- | --- |
| `DrawSpriteDefault` (0x441400) | `(Viewport*, short x, y, u8 *shape, short frame)` | = `DrawSpriteTransformed(vp, x, y, shape, frame, angle 0, scale 0x100, 0x100, flip 0, blend 0)`; hotspot at (x,y) | L |
| `DrawSpriteScaled` (0x441FC0) | `(vp, x, y, shape, frame, short angleDeg, short scale8_8, short flip)` | uniform scale; used for every space object | L |
| `DrawSolidColourSprite` (0x441A40) / `DrawSolidColourSpriteScaled` (0x442000) | `(..., u8 colour)` | `SetSolidColourTranslation(colour)` (all 0..254 → colour, 255 stays 255) then draw with blend 1 (translated) — silhouettes (e.g. constellations, intro background ships) | L |
| `DrawSpriteTransformed` (0x440FE0) | `(vp, int x, y, shape, int frame, angleDeg, scaleX8_8, scaleY8_8, flip, blend)` | guards: `shape && frame >= 0 && pixels && rowOffsets && frame < frameCount`; `PrepareShapeRLEData`; flip 0x10 → `scaleX = -scaleX`, 0x20 → `scaleY = -scaleY`, 0x30 → both, any other non-zero → `exit_squadron("bad flip")`; then `RotateRLEImage(&stRasterClip, prepared, frame, x - vp.left, y - vp.top, abShapeTransformScratch, angle*10, scaleX*256, scaleY*256, blend ? 1 : 0)` | L |
| `PrepareShapeRLEData` (0x440D50) | `(u8 *shape)` | build the prepared block (§2.3) once | `abShapeRLEScratch` | L |
| `GetTransformedShapeBounds` (0x442050) | `int (vp, x, y, shape, frame, angleDeg, scale, flip, short bounds[4])` | conservative screen bounds of a rotated/scaled frame (§5.9); returns 1 if it intersects the viewport rect and fills `bounds = {left, top, right, bottom}`; with `shape == NULL` tests whether (x,y) is inside the rect; `flip` ignored | L |
| `CaptureSpriteBackground` (0x441450) / `RestoreSpriteBackground` (0x441740) | `(vp, u8 *save, x, y, shape, frame)` | walk the **raw** frame's row stream (§2.2) at hotspot (x,y) in absolute coordinates, clip each span to the viewport rect, and copy the covered screen bytes to/from `save` sequentially (only the clipped part is stored, in stream order). `Restore` marks the DIB dirty if the viewport is the screen | L |
| `MeasureShapeFrameStorage` (mathfp.c 0x435340) | `int (shape, frame)` | total span pixel count of a raw frame (= save-buffer size needed when unclipped) | L |
| `GetShapeFrameBounds` (mathfp.c 0x435020) | `short (short bounds[4], x, y, shape, frame)` | `bounds = {x-left, y-top, x+right, y+bottom}` from the raw extents; returns -1 if the frame exists else 0. **Note**: reads the frame offset as `u16` (`*(u16*)(shape+off)`) — fine while shapes are < 64K; it also tests `frame*4 < dirSize` (off by one vs. the other helpers) | L |
| `GetShapeFrameExtent` (brains.c 0x407710) | `short (x, y, shape, frame, which)` | one of the four bounds above (`which` 0..3) | L |
| `GetShapeFrameCount` / `GetShapeFrameExtents` / `DecodeShapeFrame` (killbrd.c) | §2.2 | `DecodeShapeFrame(shape, frame, bitmap, width, height, leftExtent, topExtent)` writes spans at `(leftExtent+dx, topExtent+dy)` into a `width`-stride bitmap, clipping x to `[0,width-1]` and dropping rows outside `[0,height-1]` | L |

### 3.4 Text

| Function (addr) | Signature | Semantics | Platform |
| --- | --- | --- | --- |
| `SetTextContext` (mathfp.c 0x434FA0) / `SetTextCursor` (0x434F70) / `ResetTextCursor` (0x4353F0) | | select current context / set absolute cursor | L |
| `InitializeTextContextFromFont` (disk.c 0x41D510) | `(TextContext*, short fontIndex, u8 colour, s8 background)` | load font if needed, set font/colour/background, make current | L |
| `ReleaseTextFont` (disk.c 0x41D590) | `(short)` | free unless index 1 | L |
| `GetFontCharWidth` (mathfp.c 0x434FF0) | `u16 (char)` | `font[4 + (u8)c]` of the current context's font (Win32 used signed indexing; SDL unsigned — use unsigned) | L |
| `DrawTextString` (mathfp.c 0x4350F0) | `void (const char*)` | word-wrapped, optionally centred line rendering — §5.7 | L |
| `DrawTextCharacter` (0x435290) | `void (char)` | `'\n'`: `cursorX = viewport.left; cursorY += fontHeight`; `'\r'`: `cursorX = viewport.left`; `0`: nothing; else `DrawFontGlyph` | L |
| `DrawFontGlyph` (gr.c 0x441150) | `(char, TextContext*, int height, int width, int y)` | blit one glyph at `(cursorX, y)` — §5.8; **no clipping**; advances `cursorX += width[c]` | `abPaletteTranslation` | L |
| `AppendTextCharacter` (0x435310) | | append to `textCursor`, keep NUL-terminated | L |
| `FormatTextTokens` (cockpt.c 0x413A40; helper `EmitTextString` 0x413A10) | `(writer, format, va_list)` | tiny printf with its own tokens (§5.7) writing through `writer` (= `DrawTextCharacter` or `AppendTextCharacter`) | L |
| `DrawFormattedText` (0x413C40) | `(format, ...)` | tokens → draw; marks DIB dirty if the context's viewport is the screen | L |
| `FormatTextBufferFromStart` (0x413C70) / `AppendFormattedText` (0x413CB0) | | tokens → context string buffer (reset / append) | L |
| `DrawTextAt` (disk.c 0x41D5F0) | `(TextContext*, x, y, text, u8 alignment)` | set context+cursor, draw with temporary alignment, restore `text`/`alignment` | L |
| `MeasureTextPixelWidthClamped` (geom.c 0x418080) | `short (const char*)` | width of the longest prefix whose width stays `< 320` | L |
| `MeasureMessageWidth` (hudmsg.c 0x428E70) | | misnamed: returns a *display duration* `(min(5, len/2) + 5) * (messageSpeed + 1)` | L |
| `print_subtitle` (mono.c 0x403920) / `GetLineLength` (0x403890) | `(Viewport*, colour(ignored), text)` | cinematic captions with the TITLE.VGA shape font: lines 16 px apart, block vertically centred in 128, each line horizontally centred in 320; advance = frame `rightExtent + 2`, space = 6 | L |

### 3.5 Palette

| Function (addr) | Signature | Semantics | Platform |
| --- | --- | --- | --- |
| `SetPaletteEntry` (gr.c 0x4413E0) | `(short index, short rgb[3])` | → `DIBsetPalette` (R,G,B 0..255) | D (cache) |
| `GetPaletteEntry` (0x4413C0) | `(short index, u16 rgb[3])` | → `GetPaletteEntryAsWords` | D (cache) |
| `MarkActivePaletteEntries` (0x441370) | `(Viewport*, u8 active[256])` | sets `active[i]=1` for every palette entry that is not (0,0,0). **Ignores the viewport** (the DOS version scanned pixels) | L |
| `CollectActivePaletteIndices` (geom.c 0x418140) | `short (vp, u8 *indices, short capacity)` | list of active indices | L |
| `StepPaletteTransition` (barracks.c 0x41C510) | `u16 (short *current, const short *target, short componentCount)` | one step of a Bresenham-style fade; returns 0 when finished — §5.6 | L |
| `FadeViewportPaletteToColour` (hudmsg.c 0x42A700) | `(vp, u16 colourIndex, short unused)` | fade all active entries to the RGB of `colourIndex`, one step per vblank — §5.6 | L + vblank |
| `PanToScreen` (screens.c 0x439430) | `(Viewport *src, Viewport *dst)` | "fade in": set active entries to the colour of dst's top-left pixel, copy src→dst, present, then step back to the real palette — §5.6 | L + vblank |
| `SaveGamePalette` (winmain.c 0x401000) / `RestoreGamePalette` (0x401020) | | snapshot / reload 256 entries (`awPaletteRgbWords`) | D |
| `LoadPaletteTripletsFile` (cmpgn.c 0x404610) | `u16 (path)` | GAME.PAL → whole palette (§2.5) | L |
| `LoadGamePaletteFile` (logic.c 0x4219C0), `ResetCockpitPaletteEntries` (0x423E10) | | startup palette (§2.5) | L |
| `TriggerPlayerHitPaletteFlash` (main.c 0x427C80), `UpdateSpacePaletteFade` (0x427CD0), `FadeFlightPaletteEntry` (0x427CA0), `house_keep` part | | flight flashes (§4.4) | L |
| `SetSolidColourTranslation` (gr.c 0x440D10), `SetPaletteTranslationTable` (0x43AE3F) | | fill the raster translation table | L |
| `ReserveContiguousPaletteEntries` / `ReleaseContiguousPaletteEntries` / `PrintPaletteAllocationMap` (screen.c) | | first-fit allocator over `awPaletteEntryAllocation`; **no callers** | X |

### 3.6 Presentation (dib.c, screen.c) — host dependent

| Function (addr) | Semantics |
| --- | --- |
| `DIBinstall(hwnd)` 0x432310 / `DIBunInstall` 0x432680 / `DIBreInstall` 0x4322B0 / `DIBcascade` 0x432410 | create the host surface (DirectDraw 320x200x8 or cascade to 640x400/640x480 with a system-memory secondary surface; SDL: renderer + streaming ARGB texture) |
| `DIBmakeDIB` 0x4326E0 / `DIBdestroyDIB` 0x4328A0 | allocate/free the 64000-byte `pDIBPixelBuffer`; contents preserved across re-creation through `abDIBPixelBackup`; binds `stScreen.pixels` |
| `DIBslam` 0x432960 | `bDIBSlamPending = 1` |
| `DIBslamReal` 0x432970 | if pending: cursor capture+draw (if the cursor viewport is the screen) → copy pixels to host (DirectDraw: wait vblank then row copy; cascade: blit 320x200 → 640x400 (or at y 40..439 for 640x480); SDL: `SdlPresentIndexedFrame`) → cursor restore → clear pending. Always: `nDIBSlamCount++`, `ServiceSoundSystem()`, `ThrottleFrameAndDrawFps()` |
| `DIBupdate(l,t,r,b)` 0x432C60 | partial update of the primary surface (cursor-only redraws); SDL presents the whole frame |
| `DIBsetPalette` 0x432F10 / `GetPaletteEntryAsWords` 0x433020 / `CachePaletteEntryFromWords` 0x432E30 / `DIBramPalette` 0x432EA0 / `DIBwholePaletteFromTriplets` 0x433060 / `DIBwholePaletteFromWords` 0x433120 / `DIBwaitForVerticalBlank` 0x4331E0 | palette cache maintenance; the "whole palette" setters first wait for vertical blank |
| `ThrottleFrameAndDrawFps(dc)` (screen.c 0x431F00) | busy-wait (`Sleep(0)`) until `nFrameDeadlineMs`; while waiting, if the cursor lives on the screen viewport, `RefreshMouseCursorDisplay()` re-presents the cursor so it moves smoothly between game frames; then `deadline = now + nFrameIntervalMs` (`1000/fSpaceFlightFrameRate` in flight, clamped 8..32 fps, or `1000/fCinematicFrameRate`) |
| SDL: `SdlInitializeVideo`, `SdlPresentIndexedFrame(pixels, bgraPalette)`, `SdlWaitForVerticalBlank` (re-presents the last frame), `SdlCalculateOutputViewport` (largest centred 4:3 rect), `SdlMapLogicalToWindow`/`SdlMapWindowToLogical` (pointer mapping, rounded scaling), `SdlEnableEgaDither` | |

### 3.7 SDL "enhanced" GL renderer hook (gl_renderer.c) — optional

Interface (`video_internal.h`): `SdlBeginSpaceFrame(geometry, viewportMode, fullViewportCopy,
clearColour)` is called from `Draw_3Space_Frame` after depth sorting and before
`draw_sorted_objects_to_buffer`; each object draw site then does
`if (!SdlRecordSpaceSprite(&stSpaceBuffer, fx, fy, shape, frame, angle, scale, flip)) DrawSpriteScaled(...)`
(eventmgr.c 0x4364C0 object loop, nav.c title halves, sound.c launch doors). `RecordSpaceSprite`
returns 1 only in GL mode, when recording, the frame area is <= 0xFA00 or unrotated, and the
frame could be cached in a texture atlas (decoded with `DecodeShapeFrame`); once one sprite falls
back to software all later ones do (`useSoftwareForRestOfFrame`) to keep painter order.
`SdlCompleteSpaceFrame` is called from `dump_buffer_to_screen`; at present time the recorded quads
are drawn over the base frame with the view-geometry run list as a mask (`BuildSpaceViewMask`),
transformed with the same `GetRLETransformTrig` cos/sin, scale/256, flip sign conventions, corner
rectangle `[-leftExtent-0.5, -topExtent-0.5]..+width/height`, and sub-pixel float positions supplied by the
game (`enhancedScreenX/Y`). It is a presentation-side enhancement; the indexed pipeline remains the
source of truth. A C# port can expose the same seam ("sprite sink") if it wants a GPU path.

### 3.8 Misc wrappers that just mark/present

`DrawTextAt`, `DrawFormattedText`, `FormatTextBufferFromStart`, `AppendFormattedText`,
`print_subtitle`, `RestoreSpriteBackground`, `DrawViewportEllipse*`, `fizzle_fade`, `snow_viewport`
call `DIBslam()` when the target buffer is the screen; `ClearViewport(&stScreen)`, `PanToScreen`,
`FadeViewportPaletteToColour` and every game loop call `DIBslam(); DIBslamReal();`.

---

## 4. Pipeline details

### 4.1 Drawing a sprite, step by step (unrotated)

`DrawSpriteDefault(vp, x, y, shape, f)` → `DrawSpriteTransformed(..., 0, 0x100, 0x100, 0, 0)` →
`PrepareShapeRLEData` (first time) → `ClipViewportToScreen(vp)` → `RotateRLEImage(clip, prep, f,
x - vp.left, y - vp.top, scratch, 0, 0x10000, 0x10000, 0)` → fast path `DrawRLEImage(clip, prep, f,
lx, ly)`:

```
header H = prep + prepFrameOffset[f]
if H.right < H.left || H.bottom < H.top → -4
px = lx + clip.left ; py = ly + clip.top           (clip.left/top are 0 for viewports)
frame box on surface: [px+H.left, px+H.right] × [py+H.top, py+H.bottom]
outcodes against [cl,cr]×[ct,cb]; both ends share a bit → -3 (nothing drawn)
no bit set → DrawRLEImageUnclipped (row pointer = pixels + (py+H.top+row)*w + px+H.left; ops: 0 end, 1 skip, odd memcpy, even memset)
else row loop for row = 0..H.bottom-H.top: skip rows above ct by parsing, stop when the row > cb,
     per op clip the run to [cl, cr] (left-trim for literal: advance both pointers; fill: advance dest; right-trim: shorten)
```

Result: pixel `(H.left + c, H.top + r)` of the frame lands at absolute `(x + H.left + c, y + H.top + r)`,
i.e. the hotspot lands on `(x, y)`. Transparent pixels (skip ops) are not touched. With
`blend != 0` (`flags & 1`) every stored pixel is replaced by `abRasterPaletteTranslation[pixel]`
(`DrawRLEImageColor*`).

### 4.2 Space frame (`Draw_3Space_Frame`, hudmsg.c 0x429DD0)

`UpdateSpacePaletteFade` → frame skip counter → `transform_objects_to_your_view`, `update_star_field`,
`place_exhaust_on_ships`, `reposition_fixed_child_objects`, `sort_object_depth` → (SDL: `SdlBeginSpaceFrame`) →
`draw_sorted_objects_to_buffer` (eventmgr.c 0x4364C0): for each sorted object, class-dependent:
ships/objects → `DrawSpriteScaled(&stSpaceBuffer, screenX + nViewCenterX, screenY + nViewCenterY,
shape, viewFrame, screenAngle, screenScale, flip)`; stars/dust → `DrawSpriteDefault` of
`pConstellationShape` frame. KS draws planets through this unscaled constellation branch too
(they look like a dust dot); the SDL port scales them, which the port adopts by default (ADR-012). Then `overlay_head_up_display` in view mode 0. The caller
(`RenderSpaceViewFrame`/cockpit code) draws cockpit overlays and finally `dump_buffer_to_screen`
(main.c 0x427A40): cockpitless → `CopyViewportContents(&stSpaceBuffer, &stScreen)`; mode 4 →
copy into screen rows 24..152; mode 5 → copy; else `fizzle_fade` (§2.6). `clear_view_buffer` =
`ClearViewport(&stSpaceBuffer, cPrimaryViewBufferColour)`.

### 4.3 `CopyViewportContents` is rect-to-rect

Because `ValidateViewportBounds` yields clip rectangles starting at (0,0) for both viewports,
`CopyViewportContents(src, dst)` copies the top-left-aligned overlap of the two rects regardless of
their absolute positions. Examples in the game: space buffer (0..319 x 0..127) into
`stScreen` with `top=24, bottom=152` lands at screen rows 24..151; `ModalTextPanel` saves/restores
exactly its rect because both viewports carry the same rect.

### 4.4 Flight palette effects

* `cPrimaryViewBufferColour` (191) is the space background colour index. Base (0,0,32). On a
  player hit (`TriggerPlayerHitPaletteFlash`, only in camera modes <= 3) R is set to 0x30; every
  `Draw_3Space_Frame` call, i.e. every tick and before the frame-skip test (correction from
  flight-ui.md), `UpdateSpacePaletteFade` decrements R by 4 and re-sets the entry until R==0.
* Entries 185..190 (`aPaletteFadeEntries[0..5]`, 0xB9..0xBE) are the cockpit damage-direction
  flashes: on a hit from a direction, spc.c sets `R = 0x38` of entry 1 (front), 3/5 (left/right),
  0 (rear), 2/4 (above/below); `house_keep` (in view mode 0) fades each by 4 per call via
  `FadeFlightPaletteEntry` (`R -= 4; G = B = 0`). Cockpit artwork uses these indices for the
  flashing regions.

### 4.5 Mouse cursor compositing (eventmgr.c)

`CaptureMouseCursorBackground` → `CaptureSpriteBackground(cursorVp, abCursorSaveArea, x, y, shape,
frame)` and records the drawn position; `DrawMouseCursor` → `DrawSpriteDefault`;
`RestoreMouseCursorBackground` → `RestoreSpriteBackground` at the recorded position. All three are
no-ops unless `nMouseCursorShowCount != 0` (`EnterAllocationScope`/`LeaveAllocationScope` are the
misnamed show/hide counters) and shape/viewport are set. `DIBslamReal` wraps the host copy with
capture+draw / restore, so the game's own frame never contains the cursor. `RefreshMouseCursorDisplay`
does capture+draw+`DIBupdate(damage rect ±16)`+restore for cursor-only updates while waiting for the
next frame. `SetMouseCursorShape(shape, frame)` restores the previously drawn cursor first.

### 4.6 Fades are frame-synchronous

`StepPaletteTransition` is called once per iteration and each iteration ends with a vblank wait +
whole-palette upload (`WaitForVerticalBlankThunk(); DIBramPalette()` or
`SetWholePaletteFromTriplets`). In SDL `SdlWaitForVerticalBlank` re-presents the previous frame
(vsync-bound), so a fade of maximum component delta `d` takes `d/4 + 1` presented frames.

---

## 5. Algorithms to reproduce exactly

All integer arithmetic below is two's complement; `>>` on signed values is arithmetic.

### 5.1 Row stream decoding (raw frames)

See §2.2. `DecodeShapeFrame` clips each span: rows outside `[0, height-1]` are skipped (stream
still consumed); for a span `[x, x+n-1]` with `x < 0` the first `-x` pixels are dropped and with
`x+n-1 > width-1` the tail is dropped (`memset`/`memcpy` of the remaining length — the C code
passes the length as `(short)`, harmless).

### 5.2 Unrotated prepared-RLE draw

§4.1. Pixel-exact by construction; the only subtlety is that **both** the asm and the portable
version treat the prepared frame bounds as `[H.left, H.right] × [H.top, H.bottom]` relative to
`(x, y)` and that `clip.left/top` are added to `x, y` before clipping (so clip-relative
coordinates). For viewports `clip.left = clip.top = 0` always.

### 5.3 Rotate / scale renderer (`RotateRLEImage`, asm 0x43B469 — ships in space)

**Inputs**: clip, prepared shape, frame, `(x, y)` clip-relative hotspot position, 64000-byte
scratch, `angleTenths` (game passes `angleDeg * 10`), `scaleX`, `scaleY` in 16.16 (game passes
`scale8_8 * 256`; negative = mirrored), `flags` (bit0 = colour-translate, bit1 = scratch already
decoded — never set by the game).

**Trig** (`GetRLETransformTrig`, 0x43E2D3): reduce `a` to `[0, 3600]` (`while a < 0: a += 3600;
while a > 3600: a -= 3600`), `T = anRLEQuarterCosine` (16.16):

```
a <= 900 : cos =  T[a],        sin =  T[900-a]
a <= 1800: b = 1800-a; cos = -T[b], sin =  T[900-b]
a <= 2700: b = a-1800; cos = -T[b], sin = -T[900-b]
else     : b = 3600-a; cos =  T[b], sin = -T[900-b]
```

Rotation convention: `rx = sx*cos - sy*sin`, `ry = sx*sin + sy*cos` with y down (positive angle =
clockwise on screen).

**Step 0 — fast path**: `scaleX == scaleY == 0x10000 && angleTenths == 0` → §5.2.
`scaleX == 0 || scaleY == 0` → nothing (portable; the asm has no such guard but `idiv` by zero
cannot occur because the quad collapses to a line and the edge walk divides by dy, not scale).

**Step 1 — decode the frame into scratch**: `W = H.right-H.left+1`, `SH = H.bottom-H.top+1`,
scratch surface `W×SH`, fill 0xFF, `DrawRLEImage[Color](scratchClip, prep, frame, -H.left, -H.top)`
so scratch `(c, r)` = frame pixel `(H.left+c, H.top+r)`. The hotspot is at scratch
`P = (-H.left, -H.top)`. The asm has **no size guard**: a frame larger than 64000 pixels would
overrun `abShapeTransformScratch` into `stRasterClip`. The portable C returns -4 for
`W*SH > 0xFA00`; keep that guard. *(Corrected 2026-10-07: no frame of the GOG DOS data is larger
than 320x200 = 64000 px; the "MIDGAME.V03 325x325" frames do not exist there (MIDGAME.V03's
frames are 320x128 and smaller). The guard is purely defensive.)*

**Step 2 — transform the four corners** (`TransformRLEPoint`, 0x43E3B1), source corners
`S0=(0,0), S1=(W-1,0), S2=(W-1,SH-1), S3=(0,SH-1)` (this order matters for the edge walk):

```
d  = s - P                                              (integer)
ex = (d.x * scaleX * 65536 + 0x8000) >> 32              (64-bit signed; i.e. floor(d.x*scaleX/65536 + 2^-17) — effectively FLOOR, not round)
ey = (d.y * scaleY * 65536 + 0x8000) >> 32
t.x = P.x + ((ex*cos + 0x8000) >> 16) - ((ey*sin + 0x8000) >> 16)      (each product rounded separately, 64-bit products)
t.y = P.y + ((ey*cos + 0x8000) >> 16) + ((ex*sin + 0x8000) >> 16)
V[k].dst = (x + H.left + t.x + clip.left, y + H.top + t.y + clip.top)   == (x, y) + clipOrigin + R(S(s - P))
V[k].src = s
```

(The portable C rounds the scaled value `((d*scale) + 0x8000) >> 16` and rounds the *sum* of the
rotation terms — both differ from the asm by up to one pixel for some inputs. Follow the asm.)

**Step 3 — reject / find the top vertex**: compute for each vertex the sign bits of
`(dst.x - cl)`, `(cr - dst.x)`, `(dst.y - ct)`, `(cb - dst.y)`; if the AND over all four vertices
is non-zero (all on the same outside side) → return. Top vertex = the vertex with minimum `dst.y`,
**ties resolved to the later index** (loop keeps `<=`). `minY`, `maxY` over vertices; if
`maxY == minY` → return (degenerate).

**Step 4 — edge chains**. Left chain walks *backwards* through the vertex ring from the top vertex
(`k-1`, wrapping 0→3); right chain walks forwards (`k+1`, wrapping 3→0). Choosing the first edge of
a chain (`cur` = top vertex, `nxt` = neighbour): skip the edge and advance if
`(cur.dst.y < ct && nxt.dst.y <= ct)` or `nxt.dst.y == cur.dst.y`. For the chosen edge:

```
dy      = nxt.dst.y - cur.dst.y                                   (rows the edge spans)
sDstX   = ((nxt.dst.x - cur.dst.x) << 16) / dy                    (16.16, truncated toward zero; numerator built as 64-bit)
sSrcX   = ((nxt.src.x - cur.src.x) << 16) / dy
sSrcY   = ((nxt.src.y - cur.src.y) << 16) / dy
aDstX   = (cur.dst.x << 16) + 0x8000 ;  aSrcX = (cur.src.x << 16) + 0x8000 ;  aSrcY = (cur.src.y << 16) + 0x8000
```

Later edges (switched to when a chain's `dy` counter reaches 0 inside the row loop) use the same
formulas except that `dy == 0` becomes 1 and there is no skipping. *(Corrected 2026-10-07: the asm
is `cmp ecx,1; adc ecx,0`, an unsigned compare, so only 0 is adjusted; a negative `dy` is kept.)*
This is harmless for a convex quad because a horizontal edge can only be first or last. Note that a
quad whose four corners share one row (`maxY == minY`, e.g. a one-row frame at angle 0 forced
through the mapper) draws nothing.

**Step 5 — rows**: `curY = minY`; `rows = (maxY > cb ? cb : maxY) - curY`. If `ct > curY`: `rows
-= ct - curY; curY = ct;` and for each chain `n = ct - cur.dst.y; dy -= n; aDstX += sDstX*n; aSrcX
+= sSrcX*n; aSrcY += sSrcY*n` (exact products). Then loop:

```
row:
   L = (aDstX_left, aSrcX_left, aSrcY_left), R = (…right); if R.aDstX <= L.aDstX swap L and R    (so L is the smaller x)
   xa = L.aDstX >> 16 ; xb = R.aDstX >> 16
   if xa > cr || xb < cl → skip row
   span = xb - xa
   if span != 0:
        stepX = (R.aSrcX - L.aSrcX) / span          (16.16 / int, truncated toward zero; the asm divides the 64-bit (d<<16) by (span<<16))
        stepY = (R.aSrcY - L.aSrcY) / span
        if cl > xa: n = cl - xa; xa = cl; L.aSrcX += stepX*n; L.aSrcY += stepY*n
        if xb > cr: xb = cr
   (if span == 0 the previous row's stepX/stepY are reused — irrelevant, one pixel)
   for i in 0 .. xb - xa:                      (inclusive)
        sx = floor(L.aSrcX + i*stepX) ; sy = floor(L.aSrcY + i*stepY)        (16.16 → int, i.e. >> 16)
        p = scratch[sy*W + sx]                   (NO bounds check in the asm — clamp or pad in the port)
        if p != 0xFF: dest[curY*w + xa + i] = p
   curY++ ; rows--
   if rows < 0 → done ; if rows == 0 → advance both chains by one step, draw the final row, done
   left:  dy--; if dy == 0 → switch to next left edge (accumulators reset to that vertex) else aDstX += sDstX, aSrcX += sSrcX, aSrcY += sSrcY
   right: same
```

The asm realises `floor(acc + i*step)` with a 16-bit fractional accumulator and a 4-entry pointer
step table (`anRLESourceSteps[(carryX<<1)|carryY]` = `xInt + yInt*W` plus `±1` / `±W` on carries, with
the two's-complement trick for negative steps); the mathematical form above is equivalent. One
practical limit: `yInt*W` is computed with a 16-bit `imul`, so `|stepY_int| * W` must stay below
32768 (true for every scale the game uses: scales < 16/256 are culled by the callers).

Properties worth knowing when testing: vertices land exactly on their rows; the footprint is the
convex hull of the four integer corners, inclusive on all edges, so a 1:1 unrotated draw through
this path would cover `[x+H.left, x+H.right]` exactly like the fast path; sampling is centre-based
(`+0x8000`) so the image shifts by at most half a source pixel.

### 5.4 Clipped line (`DrawClippedLine`, asm 0x439E39; mode 0 only is used)

The **portable C is a Bresenham loop with per-pixel clipping and does not reproduce the asm**. The
asm is a 32-bit-fraction DDA with Cohen–Sutherland style clipping:

```
x1 += clip.left … (all four) ; dx = x2-x1, dy = y2-y1, adx = |dx|, ady = |dy|
if dx == 0: vertical — reject if x1 outside [cl,cr] or the y range misses [ct,cb]; draw from clamp(y1) to clamp(y2) inclusive, stepping ±1 row toward y2
if dy == 0: horizontal — symmetric
else:
   diffSign = (dx < 0) != (dy < 0)
   major = max(adx, ady), minor = min(adx, ady)
   S = (adx == ady) ? 0xFFFFFFFF : floor(minor * 2^32 / major)        (unsigned 0.32 slope)
   outcode(p) = (p.x < cl)<<3 | (p.x > cr)<<2 | (p.y < ct)<<1 | (p.y > cb)
   loop:
      c1 = outcode(P1), c2 = outcode(P2); if c1|c2 == 0 → draw ; if c1&c2 → return 2
      clip ONE coordinate, first match in this order: P1.x<cl, P1.x>cr, P1.y<ct, P1.y>cb, P2.x<cl, P2.x>cr, P2.y<ct, P2.y>cb,
      always computing from the ORIGINAL endpoints (x1o,y1o):
        moving along the major axis by distance d (e.g. d = cl - x1o when clipping x of a shallow line):
            minorCoord = y1o ± ((d * S + 2^31) >> 32)              (sign: + unless diffSign, mirrored for the "> cr" / P2 cases)
        moving along the minor axis by distance d (e.g. clipping y of a shallow line):
            P1 cases: steps = ceil(((d - 1) * 2^32 + 2^31) / S)        (asm: 64-bit div, +1 when remainder != 0)
            P2 cases: steps = floor((d * 2^32 + 2^31) / S) - (remainder == 0 ? 1 : 0)
            majorCoord = origin ± steps
      (these give exactly the first/last DDA pixel that reaches the clip edge)
   draw: count = (major extent of the clipped segment) + 1
         k0  = |clipped start major coord - original start major coord|
         acc = (k0 * S + 2^31) mod 2^32
         for k in 0..count-1: plot(major = start + k*dirMajor, minor = startMinor + dirMinor * carries)
              where carries = number of 32-bit carries of acc += S since the start  (== floor(((k0+k)*S + 2^31)/2^32) - floor((k0*S + 2^31)/2^32))
         adx == ady: no fraction; both coordinates step every pixel
return (any outcode seen) ? 1 : 0
```

In other words, the minor coordinate of pixel `k` is `round-half-up(k * minor/major)` measured from
the original start point. Modes: 0 = store colour; 1 = pixel = `table[pixel]` where **the colour
argument is the table pointer**; >=2 = call the colour argument as a per-pixel callback (DOS
shading hooks, unused).

### 5.5 Ellipses (`DrawRasterEllipse`, asm 0x43CE80; `FillRasterEllipse` 0x43D1C1)

`DrawViewportEllipse(vp, x, y, ry, rx, colour)` → `DrawRasterEllipse(clip, x, y, rx, ry, colour)`
(the wrapper swaps the two radii into the library's horizontal-first order). The asm:

```
if rx == 0 || ry == 0 → DrawClippedLine(clip, x-rx, y-ry, x+rx, y+ry, 0, colour); return
cx = x + clip.left ; cy = y + clip.top
px = 0 ; py = ry ; ry2 = ry*ry ; rx2 = rx*rx ; two_ry2 = 2*ry2 ; two_rx2 = 2*rx2    (32-bit unsigned)
dX = 0 ; dY = two_rx2 * ry ; d = (rx2 >> 2) + ry2 - rx2*ry ; remaining = ry
region 1: while (dX - dY < 0):          (signed compare)
      plot4 ; if d >= 0: { py--; remaining--; dY -= two_rx2; d -= dY } ; px++ ; dX += two_ry2 ; d += dX + ry2
region 2: e = rx2 - ry2 ; d += ((e + (e >> 1)) - dX - dY) >> 1
      loop: plot4 ; if d < 0: { px++; dX += two_ry2; d += dX } ; py-- ; dY -= two_rx2 ; d -= (dY - rx2) ; remaining-- ; if remaining < 0 → exit
plot4 = set (cx+px,cy+py), (cx+px,cy-py), (cx-px,cy+py), (cx-px,cy-py) each individually clipped to [cl,cr]×[ct,cb]
```

The portable `DrawRasterEllipse` uses a different (Kennedy-style, 64-bit) formulation and may
choose different pixels on some ellipses; port the asm recurrence above for exactness.
*(Corrected 2026-10-07: the filled version's asm **is** in `screens.c` (`FillRasterEllipse`,
0x43D1C1). It runs the identical recurrence and, instead of `plot4`, fills at every step the rows
`cy+py` and `cy-py` from `max(cx-px, cl)` to `min(cx+px, cr)` (the step is skipped when
`cx+px < cl`, `cx-px > cr` or `cy+py < ct`; the lower row is skipped when `cy+py > cb`, the upper
row when it is outside `[ct, cb]`). The portable C's per-row extent formula is different; the port
follows the asm. The game has no caller of `FillViewportEllipse`.)*

### 5.6 Palette transitions

`StepPaletteTransition(current[], target[], count)` keeps state in globals (`nPaletteTransition*`):

```
if initialise flag:
     for i: diff = current[i] - target[i]; if diff < 0 { dir[i] = +4; delta[i] = -diff } else { dir[i] = -4; delta[i] = diff }
     maxDelta = max(delta); acc[i] = maxDelta / 4 for all i; countdown = maxDelta / 4; initialise = 0
prev = countdown; countdown--
if prev == 0: free state; initialise = 1; return 0            (fade finished)
for i: acc[i] += delta[i]; if acc[i] > maxDelta { acc[i] -= maxDelta; current[i] += dir[i] }
return 1
```

→ `maxDelta/4` steps; each component moves in multiples of 4 and may stop up to 3 short of the
target (callers never snap to the target). `FadeViewportPaletteToColour(vp, colourIndex)`: collect
active indices (all non-black entries), `current` = their RGB, `target` = RGB of `colourIndex`, then
`while Step(): write current into abPaletteTriplets[index]; SetWholePaletteFromTriplets` (each a
vblank); afterwards `DIBslam(); DIBslamReal()`. **The palette stays faded**; callers typically
`ClearViewport` and `RestoreGamePalette`. `PanToScreen(src, dst)`: active indices as above,
`target` = RGB of `dst`'s top-left pixel; set all active entries to `target` in the cache
(`CachePaletteEntryFromWords`), vblank + `DIBramPalette`, `CopyViewportContents(src, dst)`,
`DIBslam/DIBslamReal`, then `while Step(transition, original): write; vblank; DIBramPalette`. Both
allocate/free scratch arrays with the tagged allocator — irrelevant for the port.

### 5.7 Text layout

`FormatTextTokens(writer, format, args)` — `%` tokens (everything else, including `\n`, is passed
to `writer` as a character):

| token | effect |
| --- | --- |
| `%B` | `context.backgroundColour = (u8)arg` |
| `%F` | `context.colour = (u8)arg` |
| `%J` | `context.alignment = (u8)arg` |
| `%X` / `%Y` | `context.cursorX/Y = (short)arg` |
| `%P` | `DrawTextString(context.text)` (draw the string-builder buffer now) |
| `%c` | one character arg |
| `%d` | `(short)arg` decimal; `%u` `(u16)arg` decimal; `%x` `(u16)arg` upper-case hex; `%D` long decimal; `%U` unsigned long decimal |
| `%s` | string arg |
| other | the character after `%` literally |

`DrawTextString(text)` (0x4350F0), per line (replicate verbatim, including the quirks):

```
loop:
  lineWidth = cursorX ; skip ' ' ; lineStart = cursor ; right = viewport.right
  if lineWidth < right:                                   (otherwise nothing is consumed → the original loops forever)
     forever: c = *cursor++
        if c == '\n' or '\r' → break (consumed, drawn by DrawTextCharacter → newline/CR handling)
        if c == 0 → finished = 1 ; break
        lineWidth += width[c]
        if lineWidth >= right:
            cursor-- ; wrapped = 1 ; lineWidth -= width[c]
            if *cursor != ' ':
                 if cursor <= text → fatal "INVALID STRING"
                 do { c = *cursor ; cursor-- ; lineWidth -= width[c] } while *cursor != ' '     (NB: subtracts the overflowing char twice)
                 if cursor <= text → fatal
            break
  if alignment == 2: savedX = cursorX ; cursorX = viewport.left + ((viewport.right - viewport.left) - lineWidth + savedX + 1) / 2
                     (== left + (viewportWidth - textWidth) / 2, C division; textWidth = lineWidth - savedX, including the double-subtraction quirk)
  draw lineStart .. cursor-1 through DrawTextCharacter
  if alignment == 2: cursorX = savedX
  if wrapped: cursorX = viewport.left ; cursorY += fontHeight ; wrapped = 0
  if finished → return
```

Only `alignment == 2` (centre) is special; any other value is left-aligned from `cursorX`. Lines
wrap at `viewport.right` (exclusive) and restart at `viewport.left`; nothing clips vertically.
*(Notes 2026-10-07: the "INVALID STRING" tests compare with the start of the whole text, not of the
line. When the first word of a line does not fit, the backtrack lands before `lineStart`: nothing is
drawn, the cursor wraps and the word is retried at `viewport.left`; a word wider than the whole
viewport therefore loops forever. With centring, a newline inside the line sets `cursorX = left`
but the saved x is restored after the line, so the next line is centred from the original x.)*

### 5.8 Glyph blit (`DrawFontGlyph`)

```
font = ctx.font ; ink = font[2] ; bg = font[3] ; w = font[4+c] ; h = font height ; src = font + (font[0x104+c] | font[0x204+c] << 8)
abPaletteTranslation[ink] = ctx.colour ; abPaletteTranslation[bg] = ctx.backgroundColour
for row = y .. y+h-1:
    dst = vp.pixels + rowOffsets[row] + ctx.cursorX            (32-bit add of the u16 offset)
    QUIRK: if row == vp.top && (rowOffsets[row] & 0x8000) then dst = vp.pixels + ctx.cursorX   (see below)
    for col in 0..w-1: v = translated ? abPaletteTranslation[*src] : *src ; if v != 0xFF: *dst = v ; src++ ; dst++
       (fast path "translated = false" when ctx.colour == ink && ctx.background == bg)
ctx.cursorX += w ; restore abPaletteTranslation[ink] = ink, [bg] = bg
```

No clipping whatsoever; the row table is indexed with the absolute row and the x offset is added
without the viewport's `left` (the table already contains `-left`).

**The quirk branch is a real, visible Kilrathi-Saga-era bug.** In the DOS original the row offset
was added in 16-bit segment arithmetic, so a "negative" offset (`-left`, stored as `0x10000-left`)
wrapped correctly. The Win32 port adds the `u16` in 32-bit arithmetic and tried to special-case the
first row; the test it uses is "bit 15 of the offset is set", which is true for

* allocated viewports with `left > 0` (first offset is `-left`), where the branch yields
  `pixels + cursorX`, i.e. the glyph row lands in buffer row 0 shifted by `+left`; and
* **every screen alias whose `top >= 103`** (`103*320 = 0x80C0`), where the branch yields
  `pixels + cursorX` = **screen row 0**.

The second case fires every frame in flight: both VDUs of cockpit view 0 have `top = 133`
(`stCockpitLayout.leftVduBounds[0] = (10,133,82,198)`, `rightVduBounds[0] = (236,133,309,198)`)
and their first text line is drawn with `cursorY = top`. Verified on
`reference/wc1-re/screenshots/cockpit-combat.png` (SDL build, this exact C): screen row 0 is black
over x≈9..75 and x≈240..301 — precisely the horizontal extents of the two VDU text lines — while
rows 1..2 there show the green cockpit frame. The VDU font (font 2, `init_vdus` uses font index 2)
has no ink in glyph row 0 (verified for all printable glyphs), so the displaced row consists only of
background pixels (`cBlackColour`), which is why the text itself looks intact and the artefact is a
1-pixel black line at the top of the screen. With a font whose glyphs have ink in row 0 (fonts 0, 1,
3) the top row of the text would visibly move to screen row 0 — e.g. a `ModalTextPanel` placed in the
lower half of the screen and drawn with `y == 0`.

For the port: compute the address correctly (`pixels + (row - bufferTop) * stride + (x - bufferLeft)`,
which is also what the DOS original did) and, only if bit-exact reproduction of the Saga build is
required, replicate the displacement as an option.

The Win32 build also refused to draw the seven CP437 umlaut codes (0x81, 0x84, 0x8E, 0x94, 0x99,
0x9A, 0xE1) because of signed-char indexing; the SDL port draws them. Use unsigned. *(Note
2026-10-07: as reconstructed, `characterIndex = (short)(signed char)c` can never equal 0x81 etc., so
the check never matches and every byte >= 0x80 indexes the font tables negatively; either way the
port uses unsigned indexing. In the DOS fonts the glyphs above 0x7F are blank or placeholders.)*

### 5.9 `GetTransformedShapeBounds` (gr.c 0x442050)

```
E = raw extents of the frame: right = E[0], left = E[1], top = E[2], bottom = E[3]
ac = (awAbsoluteCosine[angle] * scale) >> 8 ; as = (awAbsoluteSine[angle] * scale) >> 8     (angle in degrees 0..359, scale 8.8; tables are |cos|,|sin| * 255)
if ac == 0: ac = 1 ; if as == 0: as = 1
wx = right + left ; hy = top + bottom                                  (width-1, height-1)
th = as*wx + ac*hy ; if (th & 0xFF) th += 0x100 ; th >>= 8            (ceil)
tw = ac*wx + as*hy ; same rounding
top    = y - (as*left >> 8) - (ac*top >> 8)
bottom = top + th
leftB  = x + (as*top >> 8) - (ac*left >> 8) - ((as*hy >> 8) + 1)
rightB = leftB + tw
intersects viewport rect (inclusive) ? bounds = {leftB, top, rightB, bottom}, return 1 : return 0
```

Used for culling (`easy2see`) and, importantly, for *placing* multi-part sprites (the title logo
halves and the launch doors are drawn at `bounds[0]-1` / `bounds[2]` of the centre part), so port it
verbatim. *(Note 2026-10-07: because it combines |cos|, |sin| with the unrotated extents, the box is
a true bound (within about a pixel) only for 0..90 degrees; for other quadrants it assumes the frame
is symmetric about its hot spot, e.g. at 180 degrees the top uses `topExtent` although the rotated
frame extends `bottomExtent` upwards. The frame test is `frame*4+4 <= dirSize`, accepting
`frame == frameCount`.)*

### 5.10 `MeasureTextPixelWidthClamped`

```
w = 0 ; scan = text
while *scan: w += width[*scan++] ; if w >= 320 break
if *scan != 0 (stopped early): w -= width[*(scan-1)]        (asm detail: `*scan--` then width of the new *scan)
return w
```

---

## 6. Platform-specific vs. pure logic

| Layer | Original | Port classification |
| --- | --- | --- |
| Pixel buffers, viewports, clipping, sprites, text, palette math, fades, run-list composite, cursor compositing | pure C/asm on byte arrays | **logic** — port 1:1 |
| `DIB*` (dib.c) | DirectDraw surfaces + palette object / SDL renderer | **host** — replace with `IFramePresenter` |
| Vertical-blank waits (`DIBwaitForVerticalBlank`, `WaitForVerticalBlankThunk`) | DirectDraw `WaitForVerticalBlank` / SDL re-present | host; fades need "present one frame" semantics |
| Frame pacing (`ThrottleFrameAndDrawFps`) | busy-wait on `timeGetTime` + cursor refresh | host; redesign (frame scheduler) |
| Mouse position mapping (`SdlMapWindowToLogical`) | SDL | host |
| EGA dither (`video.c`) | SDL extra | optional |
| GL sprite layer (`gl_renderer.c`) | SDL extra | optional seam |
| `snow_viewport` static | SDL extra (retail: nothing) | optional |
| Memory tagging / handle push (`AllocateTaggedMemory`, `jeff` prefix, prepared-pointer slot) | debug heap | drop; objects own their caches |

---

## 7. Proposed C# design (.NET 10, NativeAOT-friendly, no `unsafe` required)

### 7.1 Surfaces and viewports

```csharp
public sealed class IndexedSurface            // owns one 8-bit buffer
{
    public int Width { get; }  public int Height { get; }  public int Stride => Width;
    public byte[] Pixels { get; }                                    // Width*Height
    public Span<byte> Row(int y) => Pixels.AsSpan(y * Stride, Width);
}

public readonly record struct IntRect(int Left, int Top, int Right, int Bottom)   // inclusive, like the original
{ public int Width => Right - Left + 1; public int Height => Bottom - Top + 1; … }

public sealed class Viewport                   // = surface + coordinate frame + clip rect (the original's "window")
{
    public IndexedSurface Surface { get; }
    public int OriginX, OriginY;               // absolute coordinate of surface pixel (0,0); replaces rowOffsets[] (= row*stride - left)
    public IntRect Rect;                       // absolute, inclusive; must lie inside the surface
    public Viewport WithRect(IntRect r) => new(Surface, OriginX, OriginY, r);   // "alias" copies (stLeftVdu = stScreen; …)
    internal RasterClip ToClip() => new(Surface, Rect.Left - OriginX, Rect.Top - OriginY, Rect.Right - OriginX, Rect.Bottom - OriginY);
    public int ToSurfaceX(int absX) => absX - OriginX;  …
}
```

`RasterClip` (a `readonly struct` holding the surface and the clip rect in *surface* coordinates)
is what every primitive takes; the "common clip rule" of §1.2 becomes a single helper
`TryClip(out cl, out ct, out cr, out cb)`. The 16-bit row table, `SignExtendClipCoord`, the
allocation registry and `ValidateViewportBounds` disappear — the invariant "pixel (x,y) ↦
`Pixels[(y-OriginY)*Stride + (x-OriginX)]`" captures all of them. Keep `Rect` inclusive to avoid
off-by-one translation errors against the C sources. The screen is an `IndexedSurface(320, 200)`
with `OriginX = OriginY = 0`.

### 7.2 Raster primitives (static class `Raster`)

`Fill(clip, colour)`, `Blit(srcClip, sx, sy, dstClip, dx, dy)` (rect-to-rect, overlap-safe via
`Span.CopyTo`, which handles overlap), `SetPixel/GetPixel`, `Line(clip, x1,y1,x2,y2, colour)`
implementing the DDA of §5.4, `Ellipse`/`FillEllipse` (§5.5), `DrawRle(clip, frame, x, y,
translation?)` and `DrawRleTransformed(clip, frame, x, y, angleTenths, scaleX, scaleY, translation?,
scratch)` (§5.3). All loops are plain `Span<byte>` indexing; no pointers needed. Provide the
"solid colour" translation as a `ReadOnlySpan<byte>`/`byte[256]` argument instead of a global.

### 7.3 Shapes

```csharp
public sealed class ShapeTable      // one packet section
{
    public static ShapeTable Parse(ReadOnlyMemory<byte> section);   // header + frame offsets, validates bounds
    public int FrameCount { get; }
    public ShapeFrameInfo GetExtents(int frame);                    // right,left,top,bottom
    public ReadOnlySpan<byte> RowStream(int frame);                 // raw stream for Capture/Restore
    public PreparedFrame Prepare(int frame);                        // cached lazily (== PrepareShapeRLEData)
}
public sealed class PreparedFrame   // the "1.00" representation, managed
{
    public int Left, Top, Right, Bottom;           // relative to hotspot, inclusive
    public int Width => Right-Left+1, Height => …;
    public byte[] Ops;                             // the row-op stream exactly as §2.3 (0/1/odd literal), or:
    public int[] RowStart;                         // index into Ops per row (lets the draw loop skip clipped rows without parsing)
}
```

Keep the prepared op stream rather than a flat bitmap: it is what makes the unrotated path cheap
and the clipping semantics obvious; a `Decode(Span<byte> bitmap)` helper (fill 0xFF then run the
ops) feeds the rotate path and the GPU/atlas path. Transparent index is 0xFF everywhere; keep it.
The shape "handle" of the C code becomes the `ShapeTable` object; the prepared cache lives inside
it (so freeing the shape frees the cache, unlike the original leak).

### 7.4 Palette

```csharp
public struct Rgb24 { public byte R, G, B; }
public sealed class Palette { public Rgb24[] Entries = new Rgb24[256]; public event Action? Changed; … }
public sealed class PaletteTransition { /* exact StepPaletteTransition state: dir[], delta[], acc[], maxDelta, countdown */ public bool Step(Span<short> current, ReadOnlySpan<short> target); }
```

Store 8-bit components (Saga behaviour); offer a `DosDac` option (`value >> 2 << 2`) for the DOS look.
Fades are coroutines/state machines that yield one presented frame per step
(`FadeToColour(viewport, index)` / `PanToScreen`), not blocking loops.

### 7.5 Text

`BitmapFont.Parse(ReadOnlyMemory<byte> section)` → `Height, Ink, Background, Widths[256],
GlyphOffsets[256]` + the section bytes; `TextContext` as a small mutable class mirroring §1.4
(minus `fontWorkspace`); `TextRenderer.DrawString(ctx, string)` implementing §5.7 verbatim
(including the double-subtraction centring quirk, exposed as a flag), `DrawGlyph` implementing §5.8
with *correct* addressing (and optional Saga top-row quirk), `FormatTokens` with the token table.
Strings are byte strings (CP437); keep `byte`/`ReadOnlySpan<byte>` APIs and add a `string`
convenience overload.

### 7.6 Presentation

*(Superseded by ADR-009/ADR-010, 2026-10-07: the graphics library never presents or waits. Fades
are step objects (`FadeToColour`, `PanToScreenFade`) that the Game drives with `await` on its
scheduler between steps; presenting copies the working buffer into the Core `ClassicLayer` front
buffer. The original sketch below is kept for reference.)*

```csharp
public interface IFramePresenter
{
    void Present(ReadOnlySpan<byte> indices /*320*200*/, ReadOnlySpan<Rgb24> palette);
    void WaitVBlank();                        // fades: present the previous frame again and wait for one refresh
}
```

`Screen` object = screen surface + palette + "dirty" flag + cursor compositor (capture → draw →
present → restore, exactly as `DIBslamReal`). Frame pacing becomes a `FrameClock` (target interval
from the fps settings) instead of `Sleep(0)` spinning; cursor-only refreshes between frames can be
kept as a feature of the compositor.

### 7.7 Space-view composite

`ViewGeometry` (width, height, originX, originY, `(destX, screenY, length)[]`) parsed from
`PCSHIP` section 6 or the two built-ins; `Composite(spaceBuffer, screen)` = the run copy of §2.6.

### 7.8 NativeAOT notes

Everything above is plain classes/structs/arrays; avoid `dynamic`, reflection-based serialisation
and `Marshal.PtrToStructure`. Use `BinaryPrimitives.ReadInt16LittleEndian` for parsing, `Span<T>`
slicing for rows, and `[MethodImpl(AggressiveInlining)]` on the inner clip test. The only
performance-sensitive loops (rotated sprites: ~64K pixels per large ship per frame) are fine with
bounds-checked spans at 320x200.

---

## 8. Port faithfully vs. redesign

Port **pixel-exactly** (they determine what the player sees and tests can diff frames):

1. Shape/row-stream parsing and prepared-RLE semantics (§2.2–2.3, §5.1–5.2).
2. `RotateRLEImage` as the asm scanline mapper (§5.3), not the portable inverse mapper.
3. `DrawClippedLine` DDA (§5.4); `DrawRasterEllipse` recurrence (§5.5).
4. Text wrapping/centring/token semantics and glyph colour translation (§5.7–5.8).
5. `StepPaletteTransition`, fade/pan sequences, flight flash decay (§5.6, §4.4), startup palette.
6. Cursor capture/restore through the raw stream (clipping to the viewport), `GetTransformedShapeBounds`,
   `MeasureTextPixelWidthClamped`, `print_subtitle` metrics.
7. View-geometry run composite and the mode 4/5 special cases of `dump_buffer_to_screen`.
8. `ClearViewport(&stScreen)` presenting immediately (timing-visible), `CopyViewportContents`
   rect-to-rect semantics.

**Redesign**: DIB/DirectDraw/SDL presentation, vblank waits and frame throttling, the 16-bit row
offset tables, the viewport allocation registry, tagged memory/prepared-pointer slot, dead raster
library (GIF/PCX/ILBM, palette-delta streams, scroll, checkerboard, driver callbacks), EGA mode
branches (`nVideoMode` 9/13), `FontWorkspace`, the top-row glyph quirk (make it optional),
`snow_viewport` (decide: nothing like retail, or the SDL static), and the GL enhanced path.

---

## 9. Open questions

1. ~~**`FillRasterEllipse` asm (0x43D1C1–0x43E2D2)** was not decoded~~. Resolved 2026-10-07: the
   asm is in screens.c and is ported (see §5.5); the game has no caller of `FillViewportEllipse`.
2. **`DrawFontGlyph` top-row quirk** (§5.8) is confirmed in the SDL build (screenshot) and is
   implied for Kilrathi Saga by the matched assembly; it is certainly absent from the DOS original
   (16-bit wrap-around). Decide whether the port reproduces the Saga artefact (1-pixel black line at
   the top of the cockpit) or the DOS behaviour. Also check whether any `ModalTextPanel` /
   `stConversationTextViewport` (top = 152) text is drawn at `cursorY == top` with fonts 0/1/3,
   which would displace visible ink.
3. **DOS vs Saga palette depth**: Saga uses 8-bit CMAP values; DOS used the top 6 bits. Decide which
   the port targets (or make it a switch). Fade residuals (components stop up to 3 short) are invisible
   at 6-bit but could be noticed when diffing 8-bit frames.
4. **Scratch overflow for frames > 64000 pixels**: the asm has no guard and corrupts
   `stRasterClip`; the SDL port returns -4 (draws nothing). Resolved for DOS data 2026-10-07: no
   frame exceeds 320x200 (the 325x325 MIDGAME.V03 frames do not exist in the GOG data).
5. **`DrawTextString` with `cursorX >= viewport.right`** never consumes input in the original
   (infinite loop); the port should break out — confirm no screen relies on it.
6. The portable `TransformRLEPoint`/`RotateRLEImage` differ from the asm (rounding of the scaled
   corner, per-term rounding, inverse vs forward mapping). The SDL port therefore is **not** a
   pixel-exact reference for rotated ships; the asm description in §5.3 is. Worth building a
   reference test harness that runs the asm (under MSVC/wibo) and the C# mapper on the same inputs.
7. `ReserveContiguousPaletteEntries`/`awPaletteEntryAllocation` and `MarkActivePaletteEntries`'
   ignored viewport argument suggest the DOS version computed "active" colours from the viewport's
   pixels; the Saga build fades every non-black entry. If a DOS-accurate fade is wanted, the
   pixel-scan variant (`CollectRasterClipColours`-like) must be reconstructed from the DOS executable.
8. `MouseCursorState.flags` and the `reserved*` fields are never interpreted by the graphics code
   (event-manager territory).
9. `GetShapeFrameBounds` tests `frame*4 < dirSize` while every other helper tests `frame*4+4 < dirSize`
   (so it accepts `frame == frameCount` and would read the first frame's bytes as an offset);
   harmless with valid frame indices — keep the strict check in the port.
