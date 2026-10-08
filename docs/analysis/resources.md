# Wing Commander 1 — Resource / Packet / File Layer

Analysis of the C reverse-engineering in `reference/wc1-re` (Kilrathi Saga Win32 build plus the
SDL2 port that also reads DOS data), prepared as the primary specification for porting this
layer to C# / .NET 10. All byte-level claims below were verified against the GOG DOS data in
`<GOG install>/GAMEDAT` unless explicitly marked as unverified.

Source files covered (paths relative to `reference/wc1-re`):

| File | What lives there |
| --- | --- |
| `src/pload.c` | `PacketLoad` (the loader core) + unrelated audio list helpers |
| `src/disk.c` | retrying loaders, disk prompting, error reporting, font loading |
| `src/sdl/resources.c` | SDL port: Origin LZW decoder, in-memory packet extractor, DOS data detection, DOS install-table fixup |
| `src/system.c` | `LogMemoryUsage`, `exit_squadron`, memory status debug |
| `src/strdos.c` | 16-bit DOS string/memory shims (`DosStrlen`, `DosMemcpy`, …) |
| `src/cdrom.c` | CD/streams location, `OpenDataFileOrDie` |
| `src/mono.c` | `CloseDataFile`, `ReadDataFileAtOffset`, `SeekDataFile`, `WriteDataFileAtOffset`, `CreateDataFile` |
| `src/music.c` | `OpenPacketSection`, `CloseDataFileByHandle`, `DecompressPacketSection` |
| `src/killbrd.c` | `ReadPacketSectionData`, `CheckHeapBlockSignature`, `GetPreparedShapeData`, `GetShapeFrameCount` |
| `src/geom.c` | `SeekPacketSection` |
| `src/screen.c` | `GetPacketSize`, `GetFreeNearHeapBytes`, `PushMemoryStackFrame`, `IsPushedPacketHandle`, `MapPacketHandleToBlock`, `AllocateTaggedMemory`, `ReleasePacketHandle` |
| `src/winmain.c` | `AllocateGuardedMemory`, `FreeGuardedAllocation` (the Win32 debug heap wrapper) |
| `src/nav.c` | the legacy DOS near-heap allocator (`InitializeNearHeap`, `AllocateNearHeapBlockByFlags`, …) |
| `src/sound.c` | `RewriteDiskFileGraphicsExtensions`, `LoadWingCmdrCfgFile`, `LoadInstallDat` |
| `src/mathutil.c` | `FreePacketAndClear` |
| `src/eventmgr.c` | `EnterAllocationScope`/`LeaveAllocationScope` (misnamed — see §4.5), `FillGraphicSuffix`, drive hooks, memory stubs |
| `src/main.c` | startup order (`LoadWingCmdrCfgFile` → `chdir gamedat` → `LoadInstallDat`) |
| `include/wcdata.h` | `DiskFileRecord`, `PacketSectionHandle`, `BriefingPacketHeader`, `PacketDecompressionWorkspace`, `PacketResourceDescriptor`, `NearHeapBlock` |
| `include/globals.h` | `pDiskFileRecords`, `nPacketError`, `apTextFonts`, `apPacketHandles`, … |

Note: `src/screens.c` and `src/screens_portable.inc` do **not** implement any packet API; they are
only consumers (`FetchDiskPacketRetrying(4, n, 0)` etc.). The packet functions the task listed
live in the files above.

---

## 1. Binary formats

All multi-byte integers are little-endian. "u32" = 32-bit unsigned, "u16" = 16-bit unsigned.

### 1.1 Packet container (`MODULE.nnn`, `*.VGA`, `SHIP.Vnn`, `PCSHIP.Vnn`, `SHIPTYPE.Vnn`, `BRIEFING.nnn`, `CAMP.nnn`, `MUSIC.MID`, `WINGLDR.TIM`, `*.DRV`, `INTRO.DAT`, `MIDGAME.Vnn`, `FONTS.FNT`, …)

```
offset  size  field
0x00    u32   declaredFileSize      must equal the file length (the in-file reader trusts it,
                                    the SDL in-memory reader requires declared <= buffer length)
0x04    u32   entry[0]              bits 31..24 = compression flag of section 0
                                    bits 23..0  = absolute offset of section 0
                                                = directorySize = 4 * (sectionCount + 1)
0x08    u32   entry[1]              same layout, section 1
...
4+4*i   u32   entry[i]
4+4*N   ...   section 0 data        (N = sectionCount)
```

Derived values:

* `directorySize = entry[0] & 0x00FFFFFF`
* `sectionCount  = directorySize / 4 - 1`
* `offset[i]     = entry[i] & 0x00FFFFFF`
* `compression[i]= entry[i] >> 24`
* `end[i]        = (i == sectionCount-1) ? declaredFileSize : offset[i+1]`
* `length[i]     = end[i] - offset[i]`  (may be 0 — see flag 0xFF)

The original `OpenPacketSection` computes `sectionCount = (short)(entry[0] >> 2) - 1`, i.e. it
shifts the *whole* dword (flag included) and truncates to 16 bits. That works because the flag bits
land in bits 22..29 and are discarded by the cast; the port should simply mask with `0x00FFFFFF`
first. The port should also reject `directorySize < 8`, `directorySize & 3 != 0`,
`offset[i] < directorySize`, `end[i] < offset[i]`, `end[i] > declaredFileSize` (these are exactly the
checks in `SdlExtractOriginPacketSection`).

Section indices are addressed by callers as `short section`; `-1` is used only as a "no section"
marker in error reporting.

#### Compression flag values (byte 31..24)

| Flag | Meaning | Where observed |
| --- | --- | --- |
| `0x00` | raw bytes | inner (nested) packets inside every `.VGA`/`SHIP`/`PCSHIP`/`SHIPTYPE`/`MIDGAME` section; Kilrathi Saga top-level files are also expected to be raw (the SDL port decides "DOS data" by `MODULE.000[7] == 1`, anything else is treated as Saga) |
| `0x01` | Origin LZW, 4-byte uncompressed-size prefix (§1.2) | all DOS `MODULE.*`, `CAMP.*`, `BRIEFING.00x`, `MUSIC.MID`, `WINGLDR.TIM`, `*.DRV`, `INTRO.DAT`, `FONTS.FNT` |
| `0x02` | raw bytes (DOS graphics containers) | all DOS `*.VGA`, `SHIP.*`, `PCSHIP.*`, `SHIPTYPE.*`, `MIDGAME.*`, `INTRO1.DAT`; `GetPacketSize` has an explicit `case 2` that is identical to the default |
| `0xFF` | empty / absent section, `length == 0` | `BRIEFING.000` (13 of 56), `COCKPIT.VGA` [3], `PCSHIP.V04`, `SHIP.V21`, `MIDGAME.V04-06/08`, `SHIPTYPE.V29`, `SUPERTM.DRV`, `TM.DRV` — the offset equals the next section's offset |
| `0xE0` | raw (Kilrathi Saga `MUSIC.MID` MIDI sections, per comment in `resources.c`) | not verifiable here (no Saga data) |

The loader rule is: **`compression == 1` → LZW; anything else → raw copy** (`PacketLoad` `switch`
default). A zero-length section yields `nPacketError = 8` and a null result; it is *not* fatal.
`DecompressPacketSection` (the dead DOS decompressor retained in the Win32 build) additionally
rejects `compression & 0xC0 != 0` when `wPacketCompressionFormatFlags != 0`; that global is never
set, so it is irrelevant.

#### Verified examples

`MODULE.000` (22085 bytes; `declared = 0x00005645`; `entry[0] = 0x0100001C` → directory 0x1C → **6
sections, all flag 0x01**):

| i | entry | offset | stored len | uncompressed size (first u32 of section) |
| --- | --- | --- | --- | --- |
| 0 | `0x0100001C` | 0x00001C | 234 | 1536 |
| 1 | `0x01000106` | 0x000106 | 6473 | 78848 |
| 2 | `0x01001A4F` | 0x001A4F | 3888 | 65536 |
| 3 | `0x0100297F` | 0x00297F | 10960 | 86016 |
| 4 | `0x0100544F` | 0x00544F | 319 | 2560 |
| 5 | `0x0100558E` | 0x00558E | 183 | 640 |

`FONTS.FNT` (7664 bytes; `declared = 0x00001DF0`; `entry[0] = 0x01000014` → **4 sections, all 0x01**):

| i | entry | offset | stored len | uncompressed |
| --- | --- | --- | --- | --- |
| 0 | `0x01000014` | 0x0014 | 1690 | 12949 |
| 1 | `0x010006AE` | 0x06AE | 1210 | 6660 |
| 2 | `0x01000B68` | 0x0B68 | 1016 | 4342 |
| 3 | `0x01000F60` | 0x0F60 | 3728 | 17470 |

The four sections are text fonts 0..3 (`apTextFonts[4]`); the first u16 of a decompressed font is
its glyph height (`ReadWord(font)` in `InitializeDiskPromptTextContext`/`EraseLastTextInputCharacter`).

`TITLE.VGA` (271553 bytes; `declared = 0x000424C1`; `entry[0] = 0x0200004C` → **18 sections, all
0x02**; every section is a nested packet, e.g. [0] 23109 bytes → inner dir 0x10 = 3 frames,
[5] 86214 bytes → inner dir 0x4C = 18 frames, [16] 29893 bytes → 21 frames, [17] 12679 bytes → 24 frames).

`CAMP.000` (651 bytes; 3 sections, all 0x01; uncompressed 416 / 1170 / 104).

`BRIEFING.000` (222366 bytes; 56 sections; flags 0x01 and 0xFF; e.g. `[3] = 0xFF00237A` and
`[4] = 0x0100237A` share offset 0x237A, so section 3 has length 0).

`COCKPIT.VGA` (50008 bytes; 9 sections; 0x02 + one 0xFF at [3]; 7 of the raw sections are nested packets).

`GAME.PAL` is **not** a packet: it is an IFF `FORM` / `ILBM` file (`BMHD` + `CMAP` chunks, 1120 bytes).
`CONVERT.PAL` is a raw 256-byte table. `COMMUNIC.DAT` / `COMMSM2.DAT` are fixed-size text records.
`SAVEGAME.WLD` / `CRUSADE.WLD` are save-game images (start with `"game 1"`). `DISK.nnn` are tiny text
marker files (§1.6). `INSTALL.DAT` is §1.4.

Full census of the GOG `GAMEDAT` (85 files): 67 are packets; every one of the ~170 LZW sections
decoded with the algorithm in §1.2 to exactly its declared size (0 failures); every raw section of
the `.VGA`/`SHIP`/`PCSHIP`/`SHIPTYPE`/`MIDGAME`/`TITLE1`/`WINGMEN1`/`PLANETS1`/`TALKING1` files that
is ≥ 8 bytes is a nested packet with inner flag 0x00 (§1.3), with these exceptions: `SHIPTYPE.Vnn[2]`
(1000 bytes, raw table), `COCKPIT.VGA[8]` (938 bytes), `INTRO1.DAT[0]` (735 bytes).

### 1.2 Origin LZW (compression flag 1)

Layout of a flag-1 section:

```
offset 0   u32  uncompressedSize       (PacketLoad / GetPacketSize read this first)
offset 4   ...  LZW code stream, (length - 4) bytes
```

Decoder (this is `SdlDecompressOriginLzw`; it was validated bit-exactly against all DOS data):

* Codes are read **LSB-first** from a little-endian bit stream: `code |= ((byte[pos>>3] >> (pos&7)) & mask) << bitsAlreadyRead`.
  Example: `MODULE.000` section 0 stream begins `00 01 00 F8 …` → first 9 bits = `0x100` (CLEAR).
* Code width starts at **9** bits, grows to 10, 11, **12** (max) — never beyond 12.
* Reserved codes: `0x100` = CLEAR, `0x101` = STOP, first dictionary code = `0x102`, dictionary
  capacity `0x1000` (4096) entries.
* The very first code **must** be CLEAR (or STOP for an empty payload — only valid if the
  destination size is 0).
* After each CLEAR: `width = 9`, `threshold = 512`, `dictSize = 0x102`, `prev = CLEAR`.
* For each code:
  1. STOP → finished; success iff `written == uncompressedSize`.
  2. CLEAR → reset as above and continue.
  3. `code > dictSize` → corrupt.
  4. `special = (code == dictSize)` (the KwKwK case). `decoded = special ? prev : code`.
     `decoded == CLEAR` is corrupt (happens only if the first code after CLEAR is `dictSize`).
  5. Emit the string for `decoded` (walk `prefix` links back to a literal ≤ 0xFF, then output in
     forward order); `first` = that literal byte.
  6. If `prev != CLEAR`: add entry `dict[dictSize] = (prefix = prev, value = first)`, `dictSize++`;
     if `special`, additionally emit `first`; if `dictSize == threshold && width < 12` then
     `width++`, `threshold <<= 1`.
  7. `prev = code` (note: the *raw* code, even in the special case).
* Width growth happens **after** adding the entry, when `dictSize` reaches `1 << width`
  (i.e. the encoder switches to 10 bits when entry 0x200 is created — "early change" is *not*
  used). When the dictionary is full (0x1000) and no CLEAR arrives, further additions are an
  error; in practice the encoder emits CLEAR.
* Any output beyond `uncompressedSize` is an error; a short output at STOP is also an error.

Note `SdlDecompressOriginLzw`'s check `if (code > dictionarySize) return 0;` is evaluated before
the `prev == CLEAR` case, so `code == dictSize` right after a CLEAR is rejected via step 4.

### 1.3 Nested packets ("shape" containers)

Raw sections of the graphics files are themselves packets with the same layout (§1.1), inner flag
byte 0x00, and the inner `declaredFileSize` equal to the section length. The game never
re-dispatches through `PacketLoad` for them; instead the shape code reads them in place:

* `GetShapeFrameCount(shape) = (*(u16*)(shape + 4) >> 2) - 1` — inner directory size → frame count.
* frame `f` header is at `shape + *(u32*)(shape + 4 + 4*f)` (offset relative to the *section* start;
  inner flag byte is 0 so no masking is needed, but the port should mask anyway).
* A frame begins with 4 × `i16`: `rightExtent, leftExtent, topExtent, bottomExtent`
  (`width = left + right + 1`, `height = top + bottom + 1`), followed at `+8` by RLE command bytes.

Verified example — `ARROW.VGA` (513 bytes): one outer section (flag 0x02, 505 bytes at 0x08) whose
bytes are `F9 01 00 00 | 10 00 00 00 | AC 00 00 00 | 54 01 00 00 | 08 00 01 00 02 00 0B 00 …`:
inner size 505, inner directory 0x10 → 3 frames at inner offsets 0x10, 0xAC, 0x154; frame 0 extents
right=8, left=1, top=2, bottom=11 → 10 × 14 pixels.

Nesting is one level deep in all DOS data (inner sections are frame records, not packets). The
port should expose a generic `Packet.Parse(ReadOnlyMemory<byte>)` that works on any buffer, so
nested containers are just "parse the section bytes again".

Also note `aapPacketReferences[4][0x25]` (4 ship slots × 37 sections): capital-ship `SHIP.Vnn`
files have exactly 38 sections (0..36 = view frames, 37 = `shape`), matching `FetchDiskPacketRetrying(file, section, 4)` for `section < 0x25` plus section `0x25`.

### 1.4 `INSTALL.DAT`

Fixed 16-byte records, terminated by a record whose first byte is 0 (`name[0] == 0`). Verified GOG
file: 1184 bytes = 74 records = 73 named + 1 zero terminator.

```
offset  size  field
0x00    13    name          ASCII, NUL-padded, 8.3 upper-case ("MODULE.000")
0x0D    i8    diskNumber    floppy number used to build "DISK.nnn" (1..10 in GOG data)
0x0E    u8    fileClass     7 = data/driver/exe, 2 = VGA graphics, 3 = SHIPTYPE, 4 = PCSHIP,
                            5 = COCKPIT, 6 = MIDGAME.  (NOT read by any game code — only
                            diskNumber and logicalFile are used.)
0x0F    u8    logicalFile   "raw id": index into the runtime table; 0xFF = not addressable
```

C struct (`#pragma pack(1)`, `sizeof == 0x10` asserted in `wcdata.h`):

```c
typedef struct DiskFileRecord { char name[13]; signed char diskNumber;
                                unsigned char fileClass; unsigned char logicalFile; } DiskFileRecord;
```

Full decoded content of the GOG `INSTALL.DAT` (offset, name, disk, class, raw id):

```
0x000 DISK.001      1 7 255    0x250 BRIEFING.VGA  3 2   5
0x010 WC.EXE        1 7   0    0x260 DISK.004      4 7 255
0x020 INSTALL.DAT   1 7 255    0x270 TALKING.VGA   4 2   7
0x030 GAME.PAL      1 7 255    0x280 SHIPTYPE.V09  4 3  32
0x040 ARROW.VGA     1 2  15    0x290 SHIPTYPE.V10  4 3  33
0x050 FONTS.FNT     1 7   1    0x2a0 SHIPTYPE.V12  4 3  35
0x060 CONVERT.PAL   1 7 255    0x2b0 SHIPTYPE.V13  4 3  36
0x070 PCSHIP.V00    1 4  18    0x2c0 SHIP.V06      4 2  29
0x080 SAVEGAME.WLD  1 7  17    0x2d0 SHIP.V07      4 2  30
0x090 INSTALL.EXE   1 7 255    0x2e0 SHIP.V08      4 2  31
0x0a0 MODULE.000    1 7  16    0x2f0 SHIP.V15      4 2  38
0x0b0 PLANETS.VGA   1 2  13    0x300 DISK.005      5 7 255
0x0c0 OBJECTS.VGA   1 2   4    0x310 SCRAMBLE.VGA  5 2   2
0x0d0 STRAX.DRV     1 7 255    0x320 MUSIC.MID     5 7   8
0x0e0 WINGLDR.TIM   1 7  60    0x330 SHIPTYPE.V11  5 3  34
0x0f0 COCKPIT.VGA   1 5   9    0x340 DISK.006      6 7 255
0x100 SHIP.V17      1 2  40    0x350 SUPERTM.DRV   6 7  57
0x110 PCSHIP.V04    1 4  22    0x360 TM.DRV        6 7  58
0x120 INTRO.DAT     1 7  61    0x370 MIDGAME.V00   6 6  64
0x130 DISK.002      2 7 255    0x380 MIDGAME.V01   6 6  65
0x140 PCSHIP.V01    2 4  19    0x390 MIDGAME.V02   6 6  66
0x150 PCSHIP.V02    2 4  20    0x3a0 MIDGAME.V03   6 6  67
0x160 PCSHIP.V03    2 4  21    0x3b0 TITLE.VGA     6 2  10
0x170 PILOTANM.VGA  2 2   3    0x3c0 DISK.007      7 7 255
0x180 SHIP.V18      2 2  41    0x3d0 BRIEFING.001  7 7  63
0x190 SHIP.V21      2 2  44    0x3e0 CAMP.001      7 7  62
0x1a0 SHIPTYPE.V00  2 3  23    0x3f0 MODULE.001    7 7  53
0x1b0 SHIPTYPE.V01  2 3  24    0x400 SHIP.V04      7 2  27
0x1c0 SHIPTYPE.V02  2 3  25    0x410 SHIP.V05      7 2  28
0x1d0 SHIPTYPE.V03  2 3  26    0x420 SHIP.V14      7 2  37   (file absent from GOG GAMEDAT)
0x1e0 SHIPTYPE.V29  2 3  52    0x430 SHIP.V16      7 2  39
0x1f0 COMMUNIC.DAT  2 7  14    0x440 SHIP.V20      7 2  43
0x200 WINGMEN.VGA   2 2  12    0x450 DISK.008      8 7 255
0x210 DISK.003      3 7 255    0x460 MIDGAME.V04   8 6  68
0x220 CAMP.000      3 7  59    0x470 MIDGAME.V05   8 6  69
0x230 BRIEFING.000  3 7  11    0x480 SERIES.VGA   10 2  72
0x240 RECROOM.VGA   3 2   6    0x490 <all-zero terminator>
```

Maximum raw id = 72 (`SERIES.VGA`). `WC.EXE` has raw id 0 (and is absent from the GOG directory
because GOG ships it one level up).

### 1.5 `WINGCMDR.CFG`

A whitespace-separated token file read with `fscanf("%s")` in text mode; each token becomes one
startup argument, *before* the real command-line arguments. The GOG file is 11 bytes:
`"v a904 z\r\n"` → tokens `v`, `a904`, `z`. (`CRUSADE.CFG` beside it is the SM2 equivalent.)
Only the first character of each token (case-insensitive) is examined by `main`:

| token | effect |
| --- | --- |
| `v` / `e` / `t` | graphics mode: `bSlowSceneAnimation = 0 / 1 / 3` → `.V??` / `.E??` / `.T??` data-file extension (§2.4) |
| `a<n>` | `nMusicPlaybackMode = 2` (AdLib), `nArcadeStartupParameter = n` (`a904`) ; `as<n>` = nav-point override |
| `p` / `r` | `nMusicPlaybackMode = 3 / 1` |
| `z` | `DAT_005a7d9c = 1` (unidentified flag; also set unconditionally by `main`) |
| `?`, `-m`, `-b/f/k/q`, `l`, `m<n>`, `s<n>`, `w<n>`, `Origin` | debug/dev switches (most require `Origin` unlock) |

`LoadWingCmdrCfgFile(argc, argv)` returns `argumentCount - 1` and stores pointers in
`pStartupArguments[30]`, with the strings packed into `szTextScratchBuffer[256]` (no overflow
checks). The port should treat the CFG as "extra argv prepended".

### 1.6 `DISK.nnn` disk markers

`szDiskMarkerFile = "DISK.000"`; `FillGraphicSuffix(buf, diskNumber, 3)` overwrites the three
characters after the `.` with the zero-padded decimal disk number. The game only checks
*existence* (open+close). Contents are irrelevant text (`DISK.001` = `"    Wing Commander Version
B2.4: HI 3.5 Disk 1\r\n"`, `DISK.007` = `"hello \r\n"`). GOG ships `DISK.001/002/003/007/008/009/010`
only — 004/005/006 are missing — but the check is bypassed on hard-disk installs (§3.3).

### 1.7 `BriefingPacketHeader` (first 40 bytes of each `BRIEFING.nnn` mission section)

Ten `u32` offsets, relative to the start of the decompressed section:
`briefingScene, briefingText, debriefingScene, debriefingText, recRoomScene0, recRoomText0,
recRoomScene2, recRoomText2, recRoomScene1, recRoomText1` (`sizeof == 0x28`). Mission section index
is `mission + series * 4` in `asCampaignBriefingFiles[nCampaignDataSet]`.

`ScreenViewportPacket` (`PCSHIP.*` section 8, see `cmpgn.c`): `u16 geometryCount; i16 geometryOffsets[4]`
(pack 1) followed by `ScreenViewportGeometry` records (`width,height,originX,originY,fadeData[…]`, i16).

---

## 2. The logical-file table

### 2.1 Build procedure (`LoadInstallDat`, `sound.c` 0x42C660)

1. `_chdir("gamedat")` (done by `main`), open `"install.dat"` (lower case; Windows is
   case-insensitive — the port must match case-insensitively on Linux/macOS).
2. Read the whole file into a temporary buffer (`AllocateTaggedMemory(filelength, 0)`).
3. Scan records until `name[0] == 0`; `maximumId = max(logicalFile where != 0xFF) + 1` → **73**.
4. Allocate the runtime table: Win32 = `0x4B0` bytes = 75 records; SDL = 78 records, zero-filled.
5. Set `name[0] = ' '` on records `0 .. maximumId-1` (so that unused ids are *non-terminating*).
6. For each record with `logicalFile != 0xFF`: `table[logicalFile] = record` (whole 16 bytes).
7. Free the temporary buffer.
8. **`pDiskFileRecords++`** — the global pointer is advanced by one record. From now on every
   `pDiskFileRecords[n]` in the game means raw id `n + 1`. Raw id 0 (`WC.EXE`) becomes `[-1]` and
   is unreachable.
9. SDL port only: if DOS data detected (`MODULE.000[7] == 1`) → `SdlCompleteDosInstallTable(pDiskFileRecords)`.

**Rule for the port:** `codeId = rawId - 1`. Every `logicalFile` argument in game code
(`FetchDiskPacketRetrying(9, …)`, `asCampaignPilotFiles = {58, 61, 74}`, `cCapitalShipLogicalFile = type + 22`,
`PromptInsertNumberedDisk(0x38)`, …) is a *code id*. Verified cross-checks: code 0 = `FONTS.FNT`
(fonts), 4 = `BRIEFING.VGA`, 9 = `TITLE.VGA` (intro font), 15/52/72 = `MODULE.000/001/002`
(`asMissionDataFiles`), 10/62/73 = `BRIEFING.000/001/002` (`asCampaignBriefingFiles`),
58/61/74 = `CAMP.000/001/002` (`asCampaignPilotFiles`), 14 = `COMMUNIC.DAT`, 75 = `TITLE1.VGA`.

### 2.2 `SdlCompleteDosInstallTable` (DOS data only)

The GOG `INSTALL.DAT` lacks the Secret Missions 2 files. The fixup zeroes code slots 72..76 and
writes four records at code 72..75 (raw 73..76):

```
{ "MODULE.002",   disk 1, class 7, rawId 73 }
{ "BRIEFING.002", disk 1, class 2, rawId 74 }
{ "CAMP.002",     disk 1, class 2, rawId 75 }
{ "TITLE1.VGA",   disk 1, class 2, rawId 76 }
```

Code slot 76 (raw 77) remains all-zero and acts as the table terminator for
`RewriteDiskFileGraphicsExtensions`. This is why the SDL allocation is 78 records (raw 0..77).

### 2.3 The resulting 77-slot runtime table (DOS data, after the `++` and the fixup)

`codeId rawId name disk class` — `<unused>` slots have `name = " "` (one space), the last is all zero.

```
 0   1 FONTS.FNT      1 7     26  27 SHIP.V04       7 2     52  53 MODULE.001     7 7
 1   2 SCRAMBLE.VGA   5 2     27  28 SHIP.V05       7 2     53  54 <unused>
 2   3 PILOTANM.VGA   2 2     28  29 SHIP.V06       4 2     54  55 <unused>
 3   4 OBJECTS.VGA    1 2     29  30 SHIP.V07       4 2     55  56 <unused>
 4   5 BRIEFING.VGA   3 2     30  31 SHIP.V08       4 2     56  57 SUPERTM.DRV    6 7
 5   6 RECROOM.VGA    3 2     31  32 SHIPTYPE.V09   4 3     57  58 TM.DRV         6 7
 6   7 TALKING.VGA    4 2     32  33 SHIPTYPE.V10   4 3     58  59 CAMP.000       3 7
 7   8 MUSIC.MID      5 7     33  34 SHIPTYPE.V11   5 3     59  60 WINGLDR.TIM    1 7
 8   9 COCKPIT.VGA    1 5     34  35 SHIPTYPE.V12   4 3     60  61 INTRO.DAT      1 7
 9  10 TITLE.VGA      6 2     35  36 SHIPTYPE.V13   4 3     61  62 CAMP.001       7 7
10  11 BRIEFING.000   3 7     36  37 SHIP.V14       7 2     62  63 BRIEFING.001   7 7
11  12 WINGMEN.VGA    2 2     37  38 SHIP.V15       4 2     63  64 MIDGAME.V00    6 6
12  13 PLANETS.VGA    1 2     38  39 SHIP.V16       7 2     64  65 MIDGAME.V01    6 6
13  14 COMMUNIC.DAT   2 7     39  40 SHIP.V17       1 2     65  66 MIDGAME.V02    6 6
14  15 ARROW.VGA      1 2     40  41 SHIP.V18       2 2     66  67 MIDGAME.V03    6 6
15  16 MODULE.000     1 7     41  42 <unused>               67  68 MIDGAME.V04    8 6
16  17 SAVEGAME.WLD   1 7     42  43 SHIP.V20       7 2     68  69 MIDGAME.V05    8 6
17  18 PCSHIP.V00     1 4     43  44 SHIP.V21       2 2     69  70 <unused>
18  19 PCSHIP.V01     2 4     44  45 <unused>               70  71 <unused>
19  20 PCSHIP.V02     2 4     45  46 <unused>               71  72 SERIES.VGA    10 2
20  21 PCSHIP.V03     2 4     46  47 <unused>               72  73 MODULE.002     1 7   (fixup)
21  22 PCSHIP.V04     1 4     47  48 <unused>               73  74 BRIEFING.002   1 2   (fixup)
22  23 SHIPTYPE.V00   2 3     48  49 <unused>               74  75 CAMP.002       1 2   (fixup)
23  24 SHIPTYPE.V01   2 3     49  50 <unused>               75  76 TITLE1.VGA     1 2   (fixup)
24  25 SHIPTYPE.V02   2 3     50  51 <unused>               76  77 <zero terminator>
25  26 SHIPTYPE.V03   2 3     51  52 SHIPTYPE.V29   2 3
```

Files present in GOG `GAMEDAT` but reachable through **no** table slot: `BRIEFING.002`, `CAMP.002`,
`MODULE.002`, `TITLE1.VGA` (only via the SDL fixup), and `COMMSM2.DAT`, `CRUSADE.WLD`, `INTRO1.DAT`,
`MIDGAME.V06/07/08`, `PCSHIP.V05`, `PLANETS1.VGA`, `SHIP.V19`, `SHIPTYPE.V14`, `TALKING1.VGA`,
`WINGMEN1.VGA` (these are SM2 assets used by `SM2.EXE`, which this RE does not cover). `SHIP.V14`
is listed (code 36) but absent on disk. Capital-ship views are requested as
`cCapitalShipLogicalFile = type + 22` → `SHIP.Vnn` slots 26..43.

### 2.4 `RewriteDiskFileGraphicsExtensions(videoMode)` (`sound.c` 0x42C510)

Walks the table from code slot 0 until `name[0] == '\0'`, and for every name whose character
after the last `.` is `V`/`v` replaces it with `'v'` (mode 0, VGA), `'e'` (mode 1, EGA) or `'t'`
(mode 3, Tandy). Called once from `LoadOriginFxDrivers` with `bSlowSceneAnimation` (set by the
`v`/`e`/`t` CFG token). Only the extension's first letter changes (`PCSHIP.V00` → `PCSHIP.v00`);
on Windows this is a no-op for lookup. GOG only ships `.V??` files, so the port can keep VGA only
but must tolerate the lower-case `v`.

### 2.5 Opening a logical file

`OpenDataFileOrDie(pDiskFileRecords[codeId].name)` is called with the **bare name** and the
process CWD is `…/GAMEDAT` (`LoadOriginFxDrivers` does `_chdir("gamedat")` and never returns to the
parent; `LogMemoryUsage` does `_chdir("..")` at shutdown). The SDL port's `SdlResolvePath` performs a
case-insensitive component-wise resolution on POSIX. The port should model this as a
`GameDataDirectory` root plus case-insensitive name lookup, not as CWD changes.

---

## 3. Runtime behaviour of the loader

### 3.1 Core primitives

* `OpenDataFileOrDie(path)` — `_open(path, O_RDWR|O_BINARY)` (note `0x8002` = **read/write**!), sets
  `nPacketError = errno` (stale errno on success), returns fd or -1. Does *not* die despite its name.
* `ReadDataFileAtOffset(fd, offset, length, buf)` — `nPacketError = 0`; `_lseek` + `_read`; on failure
  stores `errno` and returns 0. **Short reads are not detected** (`_read` ≠ -1 is success).
* `SeekDataFile(fd, offset, origin)` — `_lseek`; returns new position or -1 (and stores errno).
* `CloseDataFile(fd)` — **`nPacketError = _close(fd)`**, i.e. a successful close resets the error
  code to 0. See §5 for the consequences.
* `OpenPacketSection(filename, section, &handle)` → fills `PacketSectionHandle` (0x14 bytes):

  ```c
  typedef struct PacketSectionHandle {
      short file;          /* fd, left OPEN on success */
      short finalSection;  /* 1 if section == sectionCount-1 */
      short sectionCount;
      short compression;   /* entry >> 24 */
      unsigned int dataOffset;   /* absolute file offset of the section */
      unsigned int position;     /* cursor relative to dataOffset, starts 0 */
      unsigned int dataSize;     /* end - offset (0 for 0xFF entries) */
  } PacketSectionHandle;
  ```
  Returns 1 and seeks the fd to `dataOffset`, or returns 0 after closing the fd. Sets
  `nPacketError = 3` when `section >= sectionCount`.
* `ReadPacketSectionData(&handle, buf, length)` — clamps `length` to the remaining bytes of the
  section (`length == 0xFFFFFFFF` means "rest"), reads at `dataOffset + position`, advances `position`.
* `SeekPacketSection(&handle, offset, origin)` — like lseek within the section; clamps below to
  `dataOffset`, clamps above to the section end unless `finalSection` (the original code falls off
  the end without `return result` — the return value is garbage/EAX, treat as `position`).
* `CloseDataFileByHandle(&handle)` — closes `handle.file`.
* `GetPacketSize(filename, section)` — opens the section; flag 1 → reads the 4-byte uncompressed
  size (returns `0xFFFFFFFF` if the read errored), any other flag → `dataSize`; `0xFFFFFFFF` if the
  open failed. Closes the file. Used by `ReportPacketLoadError`, `LoadPacketAllocated`,
  `FetchDiskPacketRetrying` (free-memory check), `cmpgn.c` (`PCSHIP` section 8).

### 3.2 `PacketLoad(filename, section, destination, flags, workspace)` (`pload.c` 0x42B050)

```
open section (else return NULL)
switch compression:
  default (raw, incl. 0/2/0xE0/0xFF):
      dataSize == 0            -> nPacketError = 8, result NULL
      destination == NULL      -> destination = AllocateTaggedMemory(dataSize, flags | 0x40)
                                  pLastPacketAllocation = destination; NULL -> nPacketError = 4
      !IsPushedPacketHandle(destination) -> exit_squadron("qq PacketLoad with non-pushed dest")
      ReadPacketSectionData(dataSize) fails -> result NULL
  case 1 (SDL port):
      dataSize < 4 or size-prefix read fails -> nPacketError = 6
      read remaining dataSize-4 bytes into a temp buffer (failure -> NULL)
      destination == NULL -> destination = AllocateTaggedMemory(outputSize, flags)   /* NB: no |0x40 */
      pLastPacketAllocation = destination; NULL -> nPacketError = 4
      LZW decode into destination (exact size) ; failure -> release (if allocated), nPacketError = 6
  case 1 (Win32 Kilrathi Saga build): prints "Compressed data in '%s'" and _exit(0)  (dead path)
CloseDataFileByHandle   /* resets nPacketError to 0 on success! */
return result
```

The "non-pushed dest" check means **any caller-supplied destination must have been allocated with
flag 0x40** (`LoadPacketAllocated`, `cmpgn.c` `pScreenViewportPacket`). The SDL LZW path allocates
without 0x40, so compressed packets loaded with a NULL destination are *not* tagged — a latent
inconsistency (shapes are never LZW-compressed in DOS data, so `CheckHeapBlockSignature` never sees
them). The port should tag/register uniformly.

### 3.3 Wrappers in `disk.c`

* `FetchDiskPacketRetrying(codeId, section, flags)` — the workhorse (≈150 call sites).
  1. `PromptInsertNumberedDisk(codeId)`.
  2. if `flags == 0` and `GetPacketSize > GetFixedOneMillionThunkAlt(0)` (= `0x3E8000` = 4 096 000,
     a stub for "free far memory") → fatal `"LP1"`.
  3. up to 5 × `PacketLoad(name, section, NULL, flags, NULL)`; the loop breaks when
     `nPacketError == 0` (always true after `CloseDataFile`) → effectively one attempt.
  4. if NULL: free `stSpaceBuffer`, retry, re-allocate (`"LP2"` on failure); then same with
     `stSceneBuffer` (`"LP3"`). (Memory-pressure recovery from DOS; pointless in the port.)
  5. if still NULL and `(flags & 4) == 0` and `nPacketError ∉ {0, 8}` → fatal `"LP4"`. Because the
     error was reset to 0 by the close, **a missing/empty section silently returns NULL**. Flag
     `4` = "optional" (used for capital-ship view frames, `aapPacketReferences`).
  6. `ClearInputKeyStatePreservingModifiers()`.
* `LoadPacketAllocated(codeId, section)` — `GetPacketSize`, `AllocateTaggedMemory((short)size, 0x40)`
  (**truncates to 16-bit signed** — fine for CAMP/`pRecRoomRoster` data, a bug for anything ≥ 32 KiB),
  then `PacketLoad` into it, with the same (ineffective) retry loop. Used for `CAMP.nnn` sections 0/1
  (`pConstellationDefinitions`, `pMissionCampaignData`) and `RECROOM` roster.
* `LoadPacketIntoBuffer(codeId, section, destination)` — `PacketLoad` into an existing tagged buffer.
* `ReportPacketLoadError(packet, codeId, retry, section, tag)` — fatal unless
  `(packet != NULL && error ∈ {0, 8}) || (packet == NULL && error == 8)`; message
  `"Sorry, an error has occured while %s … %s #%d (ERR %d PS%ld LB%ld FL%d) at %s"` where `%s` is
  `"allocating memory"` or `"reading from disk"`, `PS` = packet size, `LB` = free memory stub, tag ∈
  `RP, LPN, LP1..LP4`.
* `OpenDiskDataFile(codeId)` — builds `DISK.nnn` from `diskNumber`, returns 1 if it opens; else if
  `DAT_0059ab34 != 0` returns 1 (**hard-disk install flag**: set by `main` when the current drive
  letter > 'B' — the Win32 hook returns 0 so… see open question §8.3 — and by the SDL port whenever
  DOS data is detected); otherwise toggles A:/B: via hooks (no-ops on Win32) and retries.
* `PromptInsertNumberedDisk(codeId)` — if `OpenDiskDataFile` fails, draws
  `"Please insert disk %d\ninto any drive\nPress any key when ready."` in a bordered box and loops.
  The port should skip this entirely (treat as hard-disk install).
* `InitializeTextContextFromFont(ctx, fontIndex, colour, bg)` — lazily loads `apTextFonts[fontIndex]`
  = `FetchDiskPacketRetrying(0, fontIndex, fontIndex == 1 ? 0x10 : 0)` + `AllocateFontWorkspace`.
  `ReleaseTextFont(i)` never releases font 1 (the system font).

### 3.4 Startup order (`main`)

```
LoadWingCmdrCfgFile(argc, argv)   ; CWD = game root (where WINGCMDR.CFG lives)
_chdir("gamedat"); LoadInstallDat(); _chdir("..")
[SDL] if SdlUsingDosData() DAT_0059ab34 = 1
if GetCurrentDiskDriveHook() > 'B' DAT_0059ab34 = 1
… parse pStartupArguments …
LoadOriginFxDrivers(): _chdir("gamedat") (permanent), PromptInsertNumberedDisk(0x38 = TM.DRV),
                       RewriteDiskFileGraphicsExtensions(bSlowSceneAnimation), …
```

`SdlUsingDosData()` opens `GAMEDAT/MODULE.000` (or `MODULE.000`), reads 8 bytes, and returns 1 iff
`header[7] == 1` (the compression flag of section 0). Cached in a static.

### 3.5 SDL in-memory extractor

`SdlExtractOriginPacketSection(archive, archiveSize, index, &out, &outSize)` — the same container
logic over a whole-file buffer, returning a malloc'ed copy of the section (raw) or its LZW
expansion (flag 1, `outputSize` must be non-zero and ≤ 16 MiB). Used by `sdl/music.c` for
`MUSIC.MID` tracks (section = track number) and `WINGLDR.TIM` (AdLib timbres). This is the cleanest
model for the C# port (§6).

---

## 4. API inventory

Signatures are from `include/wc1funcs.h` / `wc1sdl.h`. "Globals" lists what the function reads (r)
or writes (w).

### 4.1 `src/pload.c`

| Function | Purpose | Globals |
| --- | --- | --- |
| `void *__stdcall PacketLoad(const char *filename, short section, void *destination, unsigned short flags, void *decompressionWorkspace)` | Load one section (raw or LZW) into `destination` or a new tagged block | `nPacketError` (w), `pLastPacketAllocation` (w) |
| `void InitializeAudioSystem(HWND)` / `void ServiceAudioStream(void)` | ix audio init/shutdown (misfiled, not resource related) | `bIxAudioEnabled`, `bAudioSystemInitialized` |
| `WaveTableEntry *AllocateWaveTableEntry(void)`, `*FindWaveTableEntryByName(const char*)`, `void RemoveWaveTableEntry(WaveTableEntry*)`, `void FreeWaveTable(void)` | singly-linked list of loaded wave names | `pWaveTableHead/Tail` |
| `ActiveSoundEntry *AllocateActiveSoundEntry(void)`, `void RemoveActiveSoundEntry(ActiveSoundEntry*)`, `ActiveSoundEntry *FindActiveSoundEntryBySample(IxSample*)` | active-sound list | `pActiveSoundHead/Tail` |

### 4.2 `src/disk.c` (resource-relevant part; lines 1–560; the rest is input/3-D object code)

| Function | Purpose | Globals |
| --- | --- | --- |
| `void ReportPacketLoadError(void *packet, short logicalFile, short retry, short section, const char *sourceTag)` | fatal error dialog unless benign | `nPacketError` (r/w), `pDiskFileRecords` (r), `szDefaultTextBuffer` (w) |
| `void *LoadPacketIntoBuffer(short logicalFile, short section, void *destination)` | load into caller buffer (`"RP"`) | `pDiskFileRecords` |
| `void *LoadPacketAllocated(short logicalFile, short section)` | size-then-load into tagged block (`"LPN"`) | `pDiskFileRecords`, `nPacketError` |
| `void *FetchDiskPacketRetrying(short logicalFile, short section, unsigned short flags)` | main loader with memory-pressure retries (`"LP1".."LP4"`) | `pDiskFileRecords`, `nPacketError`, `stSpaceBuffer`, `stSceneBuffer`, `cPrimaryViewBufferColour`, `cBlackColour` |
| `unsigned int InitializeTextContextFromFont(TextContext*, short fontIndex, unsigned char colour, signed char background)` | lazy font packet load | `apTextFonts[4]`, `apFontWorkspaces[4]` |
| `unsigned int ReleaseTextFont(short fontIndex)` | free font ≠ 1 | `apTextFonts`, `apFontWorkspaces` |
| `unsigned int DrawTextAt(...)`, `unsigned int SortSignedByteValuesAscending(signed char*, short)` | unrelated helpers | — |
| `short OpenDiskDataFile(short logicalFile)` | `DISK.nnn` existence check | `szDiskMarkerFile` (w), `pDiskFileRecords`, `DAT_0059ab34`, `abDiskPromptDriveState` |
| `void __stdcall PromptInsertNumberedDisk(short logicalFile)` | insert-disk UI loop | `bGraphicsActive`, `stDiskPrompt*`, `dwDiskPromptTopLeft/BottomRight`, `nDiskPromptBorderColour`, `pCurrentTextContext`, `szTextScratchBuffer` |
| `short CheckEscaped(void)`, `short WaitForInputKey(void)`, `void WaitForSceneAdvance(short,short)`, `void MoveMenuPointerFromKeyboard(InputEventState*)`, `void EraseLastTextInputCharacter(void)`, `short WaitForStreamInputKey(void)` | input helpers | input globals |
| `initialize_object`, `borrow_dust`, `new_object`, `initialize_ship`, `any_selected`, `remove_weapon`, `set_objects_data`, `match_rotation_goal`, `rotate_object_to_goal`, `celerate`, `approach_speed`, `steady_object`, `real_velocity`, `fix_velocity`, `sort_viable_target_list` | 3-D object layer (out of scope) | object arrays |

### 4.3 `src/sdl/resources.c`

| Function | Purpose | Globals |
| --- | --- | --- |
| `static uint32_t SdlReadLittleEndian32(const unsigned char*)` | LE read | — |
| `static int SdlReadOriginLzwCode(SdlOriginLzwBitReader*, unsigned width, uint16_t *code)` | LSB-first bit reader | — |
| `static int SdlWriteOriginLzwCode(const SdlOriginLzwEntry *dict, uint16_t code, unsigned char *dst, size_t dstSize, size_t *dstPos, unsigned char *firstValue)` | emit dictionary string | — |
| `int SdlDecompressOriginLzw(const unsigned char *src, size_t srcSize, unsigned char *dst, size_t dstSize, size_t *written)` | full decoder (§1.2); returns 1 only on exact size | — |
| `int SdlExtractOriginPacketSection(const unsigned char *archive, size_t size, unsigned sectionIndex, unsigned char **section, size_t *sectionSize)` | in-memory section extract (malloc'ed) | — |
| `int SdlUsingDosData(void)` | `MODULE.000[7] == 1` | `g_nSdlDosData` (static cache) |
| `void SdlCompleteDosInstallTable(DiskFileRecord *records)` | add SM2 records at code 72..75 | — (writes through pointer) |

### 4.4 `src/system.c`

| Function | Purpose | Globals |
| --- | --- | --- |
| `void RunTrainSim(void)` | arcade mode driver (out of scope; sets `cCockpitLogicalFile = 21` = `PCSHIP.V04`) | many |
| `short LogMemoryUsage(void)` | shutdown hooks, `_chdir("..")`, debug memory print | `DAT_0059ab4c`, `nOriginDevUnlock`, `dwOriginalFreeMemory` |
| `void exit_squadron(const char *msg)` | fatal exit with message | — |
| `unsigned int ShowMemoryStatusDebug(void)` | on-screen NMem/FMem | `nShowMemoryStatus`, `dwOriginalFreeMemory`, text contexts |
| `unsigned int GetJoystickButtonEdge(unsigned, short)` | hook | — |

### 4.5 `src/strdos.c` — 16-bit shims (all `__stdcall`)

`DosFarPtrToNear(void*)→uint`, `DosNearPtrToFar(uint)→void*` (identity casts), `DosStrrchr`,
`DosStrchr`, `DosStrcpy`, `CopyFarString` (= strcpy), `DosStrlen` (returns `short`), `DosMemcpy`
(= `memmove`), `DosMemset(dst, count, value)` (**argument order differs from C `memset`**, count is
truncated to u16), plus event-manager stubs `GetEventManagerStatus`, `RegisterEventManagerShutdown`,
`InitializeEventManager`, `ShutdownEventManager`, `ConfigureEventManagerPointer`, `EventManagerHook`,
`SetEventManagerPump` (`pEventManagerPump` w). In C# all of these vanish.

`EnterAllocationScope` / `LeaveAllocationScope` (`eventmgr.c` 0x4360D0/E0) are **misnamed**: they
only `++`/`--` `nMouseCursorShowCount` (hide/show the software mouse cursor around drawing). They
have nothing to do with memory; the port should rename them `HideCursor`/`ShowCursor`.

### 4.6 `src/cdrom.c`

| Function | Purpose | Globals |
| --- | --- | --- |
| `FontWorkspace **AllocateFontWorkspace(short)` / `void FreeFontWorkspace(FontWorkspace**)` | tiny scratch for font rendering (odd loop, allocates one 5×5 buffer) | — |
| `char *LocateStreamsDirOnDisc(void)` | find `\wc1\streams\` on a CD or `<cwd>/streams/` (or `../streams/` if cwd contains "gamedat") | `szStreamsPath[256]` (w) |
| `char FindCdRomDriveByVolumeLabel(const char *label, const char *dir)` | scan a:..z: for CD-ROMs; `"<anydisc>"` matches any | — |
| `int SetCurrentDirOnDrive(char drive, const char *dir)` | probe directory existence | — |
| `int PromptInsertCorrectCd(void)` | MessageBox loop for Kilrathi Saga disc 1 | — |
| `short __stdcall OpenDataFileOrDie(const char *path)` | `_open(path, 0x8002)`; `nPacketError = errno` | `nPacketError` (w) |

(`STREAMS/*.STR` are Kilrathi Saga video streams, not used with DOS data.)

### 4.7 Functions in other files that belong to this layer

| Function (file, address) | Purpose | Globals |
| --- | --- | --- |
| `void __stdcall CloseDataFile(unsigned short fd)` (`mono.c` 0x403500) | `nPacketError = _close(fd)` | `nPacketError` (w) |
| `short __stdcall WriteDataFileAtOffset(fd, int offset, unsigned len, const void*)` (0x403520) | seek+write; errno → `nPacketError`, `szWriteDataFileError` | `nPacketError` |
| `short __stdcall CreateDataFile(const char *path)` (0x4035C0) | `_open(path, O_WRONLY|O_CREAT|O_TRUNC|O_BINARY, 0600)`; returns 0 on failure | `nPacketError` |
| `int __stdcall ReadDataFileAtOffset(fd, int offset, unsigned len, void*)` (0x403610) | seek+read | `nPacketError`, `szReadDataFileError` |
| `int __stdcall SeekDataFile(fd, int offset, unsigned origin)` (0x4036B0) | lseek | `nPacketError`, `szSeekDataFileError` |
| `short __stdcall OpenPacketSection(const char*, short, PacketSectionHandle*)` (`music.c` 0x42D730) | parse directory, position fd | `nPacketError` |
| `void __stdcall CloseDataFileByHandle(unsigned short *p)` (0x42D870) | close `handle->file` | via `CloseDataFile` |
| `void *__stdcall DecompressPacketSection(PacketSectionHandle*, void *dest, unsigned short flags, void *workspace)` (0x42D880) | DOS-era streaming decompressor front end; allocates 0x3020 + 0x410 scratch (or 0x3000/0x400 with flags 0x22), reads the first 0x400 bytes and hands off to a video hook. **Non-functional in Win32; `PacketLoad` never reaches it.** | `pLastPacketAllocation`, `wPacketCompressionFormatFlags`, `pPacketDecompressionWorkspace`, `wPacketDecompressionInputSizeOverride`, `pPacketDecompressInput`, `wPacketDecompressInputSize`, `nPacketDecompressSourceFile/InputPosition/Pending/WorkspaceSegment/Result`, `nPacketError` |
| `short __stdcall ReadPacketSectionData(PacketSectionHandle*, void*, unsigned len)` (`killbrd.c` 0x440840) | bounded read within section | — |
| `int __stdcall SeekPacketSection(PacketSectionHandle*, int offset, short origin)` (`geom.c` 0x4180C0) | seek within section | `nPacketError` via `SeekDataFile` |
| `unsigned int __stdcall GetPacketSize(const char*, short)` (`screen.c` 0x42F810) | size without loading | `nPacketError` |
| `void CheckHeapBlockSignature(unsigned char *shape)` (`killbrd.c` 0x4408A0) | `*(int*)(shape-8) == 0x6666656A ('jeff')` else `exit_squadron("not jefftep")` | — |
| `unsigned char *GetPreparedShapeData(unsigned char *shape)` (0x4408C0) | cached RLE-prepared copy pointer stored at `shape-4` (Win32) / `shape-8-sizeof(ptr)` (SDL) | — |
| `short __stdcall GetShapeFrameCount(unsigned char *shape)` (0x4408D0) | `(u16@+4 >> 2) - 1` | — |
| `void FreePacketAndClear(void *slot, unsigned short releaseFlags)` (`mathutil.c` 0x41D100) | `if (*slot) { ReleasePacketHandle(*slot); *slot = 0; }` — `releaseFlags` unused | — |
| `void __stdcall FillGraphicSuffix(char *path, short number, short digits)` + `ConvertChar_Int` (`eventmgr.c` 0x436C70) | write zero-padded number after the `.` | — |
| `short LoadWingCmdrCfgFile(short argc, char **argv)` (`sound.c` 0x42C580) | §1.5 | `pStartupArguments[30]`, `szTextScratchBuffer` |
| `unsigned short LoadInstallDat(void)` (0x42C660) | §2.1 | `pDiskFileRecords` (w) |
| `unsigned short RewriteDiskFileGraphicsExtensions(short videoMode)` (0x42C510) | §2.4 | `pDiskFileRecords` (r/w) |
| `unsigned short __stdcall SelectDiskDriveHook(short)` / `short GetCurrentDiskDriveHook(void)` (`eventmgr.c`) | DOS drive-select stubs, return 0 | — |
| `unsigned int GetFixedOneMillion(void)` / `GetFixedOneMillionAlt(void)` / thunks `GetFixedOneMillionThunk(short)` / `…Alt(short)` | "free far memory" stub = `0x3E8000` | — |
| `unsigned short GetOriginalFreeMemory(void)` (0x4368F0) | "free near memory" stub = `0x8000` | — |

### 4.8 Memory-handle functions (`screen.c`, `winmain.c`, `nav.c`) — see §5 for semantics

| Function | Purpose | Globals |
| --- | --- | --- |
| `void *AllocateGuardedMemory(unsigned size)` (`winmain.c` 0x402BB0) | `malloc(size + 0x800)`, 0x400-byte `0xAB` guard bands before and after, zero-fills payload, tracks in a linked list | `pGuardedAllocationHead/Tail`, `nGuardedAllocationBytes/TotalBytes/PeakBytes` |
| `void FreeGuardedAllocation(void *p)` (0x402DB0) | verifies both guard bands (`ReportHeapGuardCorruption`), unlinks, frees | same |
| `void *PushMemoryStackFrame(void *memory, int offset)` (`screen.c` 0x42F960) | registers `(memory ± offset, offset)` in a 4096-entry table; `exit_squadron("qq mem push overflow")` when full | `apPacketHandles[0x1000]`, `aiPacketHandleOffsets[0x1000]`, `nPacketHandleCount` |
| `int IsPushedPacketHandle(void *h)` (0x42F9E0) | 1 iff `h` registered with negative offset | same (r) |
| `void *MapPacketHandleToBlock(void *h)` (0x42FA20) | undo the push (returns block base), swap-removes the table entry (loops in case of duplicates) | same (w) |
| `void *AllocateTaggedMemory(unsigned size, unsigned short flags)` (0x42FA90) | if `flags & 0x40`: size += 8 (+ pointer on SDL), write `"jeff\0\0\0\0"` at block start, return `block + 8` and push with offset −8; else plain guarded alloc | `abTaggedAllocationPrefix` |
| `void ReleasePacketHandle(void *h)` (0x42FAE0) | nulls every matching slot in `aapPacketReferences[4][0x25]`, then `FreeGuardedAllocation(MapPacketHandleToBlock(h))` | `aapPacketReferences` (w) |
| `int GetFreeNearHeapBytes(void)` (0x42F890) | sum of free near-heap descriptors | `nNearHeapBase/Size/FirstDescriptor` |
| `unsigned short InitializeNearHeap(void)`, `void PurgeNearHeapBlocks(unsigned short flags)`, `int ReleaseNearHeapBlock(int)`, `unsigned short MergeAdjacentNearHeapBlocks(int)`, `void *AllocateNearHeapBlockFromEnd(int, unsigned short)`, `void *AllocateNearHeapBlockByFlags(int, unsigned short)` (`nav.c` 0x40E890–0x40ED30) | DOS near-heap (§5.2) — **never called by Win32 game code** except `GetFreeNearHeapBytes` sums | `nNearHeapActive`, `nNearHeapMaxDescriptors`, `nNearHeapRelocationBytes`, `nNearHeapSize`, `nNearHeapBase`, `nNearHeapFirstDescriptor`, `pNearHeapAllocation` |

### 4.9 Relevant globals (`include/globals.h`)

| Global | Address | Role |
| --- | --- | --- |
| `short nPacketError` | 0x465460 | last file/packet error (§5) |
| `DiskFileRecord *pDiskFileRecords` | 0x5A7CF0 | runtime logical-file table, **already incremented by one** |
| `char szDiskMarkerFile[9] = "DISK.000"` | 0x469688 | scratch for marker name |
| `short DAT_0059ab34` | 0x59AB34 | hard-disk-install flag (skip disk prompts) |
| `unsigned char abDiskPromptDriveState[2]` | 0x5A7D20 | DOS floppy drive letters (unused on Win32) |
| `unsigned char *apTextFonts[4]`, `FontWorkspace **apFontWorkspaces[4]` | 0x5A6C00/10 | loaded `FONTS.FNT` sections |
| `void *pLastPacketAllocation` | 0x5A68F0 | last block allocated by `PacketLoad`/`DecompressPacketSection` |
| `void *apPacketHandles[0x1000]`, `int aiPacketHandleOffsets[0x1000]`, `int nPacketHandleCount` | 0x59E530 / 0x5A2530 / 0x5A6530 | pushed-handle registry |
| `unsigned char abTaggedAllocationPrefix[8] = "jeff"` | 0x46AD88 | tag written before tagged blocks |
| `void *aapPacketReferences[4][0x25]` | 0x465C88 | capital-ship view-frame packets per resource slot |
| `unsigned char *pBriefingPacket` | 0x598AEC | current `BRIEFING.nnn` section |
| `ScreenViewportPacket *pScreenViewportPacket` | 0x5A6B94 | `PCSHIP.*` section 8 |
| `PacketResourceDescriptor a*Resources[]` (`aIntroResourceDescriptors[3]`, `aCommon3SpaceResources[12]`, `aMissionResourceDescriptors[5]`, `aCockpitResourceDescriptors[19]`, `aCockpitSecondaryResources[5]`, `aCockpitPrimaryResources[8]`) | 0x468AC0… | `{ unsigned char **resource; short logicalFile; short section; }` tables driving bulk loads in `logic.c` |
| `const short asCampaignPilotFiles[3] = {58,61,74}`, `asCampaignBriefingFiles[3] = {10,62,73}`, `asMissionDataFiles[3] = {15,52,72}` | 0x469450.. | code ids of `CAMP`, `BRIEFING`, `MODULE` per campaign (WC1, SM1, SM2) |
| `signed char cCockpitLogicalFile`, `cCapitalShipLogicalFile`, `cObjectResourceLogicalFile` | | current `PCSHIP`/`SHIP`/`SHIPTYPE` code ids |
| `char *pStartupArguments[30]`, `char szTextScratchBuffer[256]`, `char szDefaultTextBuffer[0xC8]` | | CFG/argv storage; error text |
| `unsigned int dwOriginalFreeMemory` | 0x5A7CD8 | = `GetFixedOneMillionThunkAlt(0)` at startup |
| `wPacketCompressionFormatFlags`, `pPacketDecompressionWorkspace`, `wPacketDecompressionInputSizeOverride`, `nPacketDecompress*` | 0x46A91C… | dead DOS decompressor state (never set) |
| `char szStreamsPath[0x100]` | 0x475C18 | Kilrathi Saga streams directory |

---

## 5. Memory model

### 5.1 Tagged blocks and pushed handles (what the Win32 build actually does)

`AllocateTaggedMemory(size, flags)` is the single allocation entry point for every packet, shape,
palette, and scratch buffer. On Win32 only bit `0x40` matters:

```
flags & 0x40 == 0 :   [0x400 guard 0xAB..][ size bytes, zeroed ][0x400 guard]   -> returns payload
flags & 0x40 != 0 :   [guard]['j','e','f','f', 0,0,0,0][ size bytes ][guard]       -> returns payload+8
                                      ^magic     ^prepared-shape cache ptr (set by PrepareShapeRLEData)
```

For tagged blocks the returned pointer is registered in `apPacketHandles` with offset −8 so that
`ReleasePacketHandle` can recover the real base. `IsPushedPacketHandle` is used as a type check
("this pointer is a packet"); `CheckHeapBlockSignature` is the same check via the magic.

All other flag bits (`0x10`, `0x20`, `0x22`, `0x03`, `4`, `8`) are DOS near-heap hints that are
**ignored** on Win32 (`AllocateGuardedMemory` takes no flags):

| bit | DOS meaning (from `AllocateNearHeapBlockByFlags`) |
| --- | --- |
| `0x03` | alignment: 1 = word (size+1), 2 = paragraph/16 bytes (size+15) |
| `0x10` | persistent — survives `PurgeNearHeapBlocks(0)` (descriptor bit 0x40000000) |
| `0x20` | allocate from the top end of the heap (`AllocateNearHeapBlockFromEnd`) |
| `0x40` | tagged with `"jeff"` header + pushed handle (both builds) |
| `4` | (loader-level, not heap) "optional section, don't fatal" in `FetchDiskPacketRetrying` |
| `8` | passed to `FreePacketAndClear` by `screens.c`; unused |

`ReleasePacketHandle(p)` also scrubs `p` out of the `aapPacketReferences[4][37]` table (so freeing a
capital-ship frame through any alias clears the cache), then frees.

### 5.2 Legacy near heap (`nav.c`) — for completeness only

A 32 KiB (`GetOriginalFreeMemory() = 0x8000`) region managed by 8-byte descriptors
`{ int address; uint sizeAndFlags; }` stored at the *end* of the region growing downward
(`nNearHeapFirstDescriptor`). `sizeAndFlags`: bits 19..0 size, bit 31 allocated, bit 30 persistent,
bits 29..28 alignment kind. First-fit from the front (`ByFlags`) or from the back (`FromEnd`), with
block splitting and neighbour merging on release. `InitializeNearHeap` is never called by the
Win32 game, so `nNearHeapActive == 0` and `GetFreeNearHeapBytes` iterates an empty range. **The port
does not need any of this.**

### 5.3 What the C# port should do

* Replace every tagged/guarded block with a managed `byte[]` (or `IMemoryOwner<byte>` from
  `MemoryPool<byte>.Shared` for transient decode buffers). The GC removes `ReleasePacketHandle`,
  `FreePacketAndClear`, the push registry, and the guard bands. Keep `null` semantics for "not
  loaded".
* The 8-byte `jeff` header carried two things: a type check and a **side slot for the prepared
  (RLE-expanded) shape cache**. Model that as a class:
  `sealed class Resource { byte[] Data; object? PreparedCache; }` (or a dedicated `Shape` wrapper
  holding `ReadOnlyMemory<byte>` + cached expansion). Consumers that did `packet + header->offset`
  pointer arithmetic become `data.AsSpan(offset)`.
* `pLastPacketAllocation`, `aapPacketReferences` scrubbing, the 5-attempt retry loops and the
  viewport-freeing memory-pressure dance have no equivalent: drop them.
* `DosMemset/DosMemcpy/…` become `Span<T>.Fill/CopyTo`.
* `EnterAllocationScope`/`LeaveAllocationScope` are cursor show/hide (see §4.5) — port them in the
  input/cursor module, not here.
* `LoadPacketAllocated`'s `(short)` size truncation and `FetchDiskPacketRetrying`'s
  `> 4 096 000` check are DOS artefacts; do not reproduce them.

---

## 6. Error handling — `nPacketError`

| value | set by | meaning |
| --- | --- | --- |
| `0` | `ReadDataFileAtOffset`, `WriteDataFileAtOffset`, successful `CloseDataFile` | no error |
| `1` | `PacketLoad` (SDL: temp buffer malloc failed); `DecompressPacketSection` (large scratch) | out of memory (scratch) |
| `2` | `DecompressPacketSection` | out of memory (small scratch) |
| `3` | `OpenPacketSection` | section index ≥ section count |
| `4` | `PacketLoad`, `DecompressPacketSection` | destination allocation failed |
| `5` | `DecompressPacketSection` | seek within section failed |
| `6` | `PacketLoad` (SDL) / `DecompressPacketSection` | unsupported compression flag, bad size prefix, or LZW stream error |
| `8` | `PacketLoad` | **empty section** (`dataSize == 0`, flag 0xFF) — benign: callers treat `packet == NULL && error == 8` as "resource absent" |
| `errno` (e.g. 2 ENOENT, 9 EBADF, 22 EINVAL) | `OpenDataFileOrDie`, `CloseDataFile`, `Read/Write/SeekDataFile` | CRT failure; note `OpenDataFileOrDie` copies errno even on success |

Important quirk: because `PacketLoad` ends with `CloseDataFileByHandle` → `CloseDataFile` →
`nPacketError = _close(fd)`, **every code set inside `PacketLoad` is overwritten with 0 before the
caller sees it** (unless the close itself fails). Consequences in the original: retry loops run
once, `ReportPacketLoadError` decides on `packet == NULL` alone, and empty/absent sections return
NULL silently. The only codes that survive to a caller are those set when `OpenPacketSection` fails
before opening the fd… except that it also calls `CloseDataFile(-1)` which sets `nPacketError = -1`
(EBADF path returns −1 from `_close`). So effectively: **non-zero `nPacketError` after a load ≈ "the
file could not be opened"** (`ReportPacketLoadError` prints `ERR -1`).

For the port: do **not** replicate the global. Use a `PacketResult`/exception model:
`FileNotFound`, `SectionOutOfRange(3)`, `EmptySection(8)` (→ return `null`/`Empty`),
`CorruptPacket(6)` with a message that includes file, section, flag and the `PS`/tag diagnostics the
original showed, so error dialogs can remain recognisable.

---

## 7. Proposed C# API shape

Target: .NET 10, NativeAOT-friendly (no reflection, no dynamic code), no `unsafe` (use
`BinaryPrimitives` / `MemoryMarshal.Read` only on blittable structs where needed), `Span<byte>` /
`ReadOnlyMemory<byte>` throughout, immutable parsed descriptors, synchronous I/O (files are small;
the largest is 516 KiB).

```
namespace WingCommander.Data
{
    // ---- container -------------------------------------------------------
    public enum PacketCompression : byte { Raw = 0, Lzw = 1, RawGraphics = 2, Absent = 0xFF /* 0xE0 Saga MIDI = Raw */ }

    public readonly record struct PacketEntry(int Index, int Offset, int Length, byte Flag)
    {
        public bool IsEmpty => Length == 0;
        public bool IsLzw   => Flag == 1;
    }

    /// Parsed directory over an in-memory packet (whole file or a nested section).
    public sealed class Packet
    {
        public static Packet Parse(ReadOnlyMemory<byte> bytes);            // throws PacketFormatException
        public static bool TryParse(ReadOnlyMemory<byte> bytes, out Packet? packet);
        public int DeclaredSize { get; }
        public int SectionCount { get; }
        public ReadOnlySpan<PacketEntry> Entries { get; }
        public PacketEntry this[int section] { get; }
        public ReadOnlyMemory<byte> RawSection(int section);                // slice, no copy, no decode
        public byte[]? LoadSection(int section);                            // null for empty (flag 0xFF); LZW expanded; raw copied
        public int GetSectionSize(int section);                             // == GetPacketSize (uncompressed size for LZW)
        public Packet? ParseNested(int section);                            // Packet.TryParse(RawSection(section))
    }

    // ---- LZW --------------------------------------------------------------
    public static class OriginLzw
    {
        public const int ClearCode = 0x100, StopCode = 0x101, FirstCode = 0x102, MaxCodes = 0x1000;
        public static byte[] Decode(ReadOnlySpan<byte> sectionWithSizePrefix);              // reads u32 size, decodes rest
        public static bool TryDecode(ReadOnlySpan<byte> stream, Span<byte> destination, out int written);
        public static int ReadUncompressedSize(ReadOnlySpan<byte> sectionWithSizePrefix);
    }

    // ---- install table ----------------------------------------------------
    public readonly record struct DiskFileRecord(string Name, sbyte DiskNumber, byte FileClass, byte RawId);

    public sealed class InstallTable
    {
        public static InstallTable Load(ReadOnlySpan<byte> installDat, bool completeDosTable);  // applies the +1 shift and SM2 fixup
        public const int SlotCount = 77;                                     // code ids 0..76
        public DiskFileRecord? this[int codeId] { get; }                     // null for ' ' / zero slots
        public string FileName(int codeId);                                  // throws if unmapped
        public static readonly int[] CampaignPilotFiles  = { 58, 61, 74 };   // CAMP.*
        public static readonly int[] CampaignBriefingFiles = { 10, 62, 73 }; // BRIEFING.*
        public static readonly int[] MissionDataFiles = { 15, 52, 72 };      // MODULE.*
        public InstallTable WithGraphicsExtension(char firstLetter);         // 'v' | 'e' | 't' (RewriteDiskFileGraphicsExtensions)
    }

    public static class LogicalFile   // well-known code ids, for readability at call sites
    {
        public const int Fonts = 0, Scramble = 1, PilotAnim = 2, Objects = 3, BriefingVga = 4, RecRoom = 5,
                         Talking = 6, Music = 7, Cockpit = 8, Title = 9, Briefing000 = 10, Wingmen = 11,
                         Planets = 12, Communic = 13, Arrow = 14, Module000 = 15, SaveGameWld = 16,
                         PcShip0 = 17 /* ..21 */, ShipType0 = 22 /* ..25 */, Ship4 = 26 /* capital ship = type + 22 */,
                         Module001 = 52, SuperTmDrv = 56, TmDrv = 57, Camp000 = 58, WingLdrTim = 59, IntroDat = 60,
                         Camp001 = 61, Briefing001 = 62, MidGame0 = 63 /* ..68 */, Series = 71,
                         Module002 = 72, Briefing002 = 73, Camp002 = 74, Title1 = 75;
    }

    // ---- file system ------------------------------------------------------
    public interface IGameFileSystem
    {
        bool Exists(string relativeName);                 // case-insensitive, relative to GAMEDAT
        ReadOnlyMemory<byte> ReadAll(string relativeName);// whole file (cache allowed)
        Stream OpenWrite(string relativeName);            // SAVEGAME.WLD / high scores
    }
    public sealed class DirectoryGameFileSystem : IGameFileSystem { public DirectoryGameFileSystem(string gameDatDirectory); }

    public sealed class GameDataLocator
    {
        public static bool IsDosData(IGameFileSystem fs);          // MODULE.000[7] == 1
        public static string? FindGameDat(string root);             // root or root/GAMEDAT
    }

    // ---- loader (replaces PacketLoad / FetchDiskPacketRetrying / LoadPacketAllocated / LoadPacketIntoBuffer) ----
    public sealed class ResourceLoader
    {
        public ResourceLoader(IGameFileSystem fs, InstallTable table);
        public Packet OpenPacket(int codeId);                               // cached parsed container
        public byte[]? Load(int codeId, int section);                      // null if section empty (error 8) — no exception
        public byte[]  LoadRequired(int codeId, int section);              // throws ResourceMissingException ("LP4"-style message)
        public int     GetSize(int codeId, int section);                   // GetPacketSize
        public Shape?  LoadShape(int codeId, int section);                 // Load + Packet.TryParse → frame table
        public void    LoadInto(int codeId, int section, Span<byte> destination); // LoadPacketIntoBuffer semantics (exact size check)
        public ReadOnlyMemory<byte> Fonts(int fontIndex);                  // apTextFonts replacement
    }

    /// Nested-packet view of a raw graphics section.
    public sealed class Shape
    {
        public ReadOnlyMemory<byte> Data { get; }        // the section bytes (offsets are relative to Data)
        public int FrameCount { get; }                   // (u16@4 >> 2) - 1
        public ReadOnlyMemory<byte> Frame(int index);    // Data[frameOffset..nextFrameOffset]
        public (short Right, short Left, short Top, short Bottom) Extents(int index);
        public object? PreparedCache { get; set; }       // replaces the pointer stored at shape-4
    }

    // ---- startup config ---------------------------------------------------
    public static class WingCmdrCfg
    {
        public static IReadOnlyList<string> ReadTokens(Stream cfg);               // fscanf("%s") equivalent
        public static string[] MergeWithArgs(IReadOnlyList<string> cfgTokens, string[] argv); // CFG first, then argv
    }

    public sealed class PacketFormatException : Exception { }
    public sealed class ResourceMissingException : Exception { public int CodeId, Section; public string FileName; }
}
```

Design notes:

* `Packet.Parse` must validate exactly as `SdlExtractOriginPacketSection` does and must mask
  offsets with `0x00FFFFFF`. `LoadSection` on an LZW entry requires `Length >= 4` and a non-zero,
  sane uncompressed size (the SDL cap is 16 MiB; the largest real value is 100 378 for
  `MIDGAME.V07`).
* Since all data files are small, `ReadAll` + slice is simpler and faster than the original
  open/seek/read-per-section pattern; keep `IGameFileSystem` so tests can use in-memory data.
* `ResourceLoader.Load` returns a fresh `byte[]` each call (the original returns a fresh block each
  call too; callers own it). Add an optional cache keyed by `(codeId, section)` if profiling shows
  repeated loads (`FetchDiskPacketRetrying(9, 1, 0)` for the intro font is called from many places).
* Kilrathi Saga data uses the same container with raw sections (and `MUSIC.MID` flag `0xE0`); the
  `IsLzw` rule (`Flag == 1`) covers both.
* Disk-prompt logic (`PromptInsertNumberedDisk`, `OpenDiskDataFile`, `DISK.nnn`) is not ported;
  `diskNumber` is retained in `DiskFileRecord` for diagnostics only.
* `DecompressPacketSection` and the near-heap are not ported.

---

## 8. Open questions / not verified

1. **Kilrathi Saga data layout** — no Saga files were available. Unverified: that Saga top-level
   packets use flag `0x00` (inferred from `SdlUsingDosData` checking only for `1`), that `MUSIC.MID`
   uses `0xE0` (from a code comment), and whether any Saga file is LZW-compressed (the Win32
   `PacketLoad` aborts on flag 1, so presumably none).
2. **`0x02` vs `0x00`** — both are treated as raw. Whether `2` once meant something to the DOS
   engine (e.g. "already-resident graphics", EGA/VGA variant) is unknown; `GetPacketSize` has a
   dedicated `case 2` identical to `default`, suggesting a historical distinction.
3. **`DAT_0059ab34` on Win32** — `GetCurrentDiskDriveHook()` returns 0, so `0 > 'B'` is false and
   the flag stays 0 in the Win32 build; disk prompts are then avoided only because the Saga
   `INSTALL.DAT` presumably records disk numbers whose `DISK.nnn` files exist, or lists disk 0.
   Not verifiable without Saga data. The SDL port forces the flag for DOS data.
4. **`DAT_005a7d9c`** (set by the `z` token and by `main`) — purpose unidentified.
5. **`fileClass`** — never read by game code; the meaning table in §1.4 is inferred from the file
   groupings.
6. **Font section layout** — only the first `u16` (height) is established here; glyph layout is in
   the text module (`text.c`), out of scope.
7. **Shape frame body** (RLE command stream after the 8-byte extents) is the graphics module's
   concern (`gr.c` `DecodeShapeFrame`); only the container framing is specified here.
8. **`ReadPacketSectionData` short reads** — the original cannot detect truncated files; the port
   should treat `declaredFileSize != actual length` as corrupt (the SDL extractor allows
   `declared <= actual`; recommend equality with a warning).
9. **`SeekPacketSection` return value** — the C source has no `return` statement on the main path
   (undefined in C, in practice returns EAX = the lseek result). Only `DecompressPacketSection`
   (dead) checks it.
10. **SM2 asset routing** — `SM2.EXE` reaches `MIDGAME.V06-08`, `PLANETS1.VGA`, `TALKING1.VGA`,
    `WINGMEN1.VGA`, `COMMSM2.DAT`, `INTRO1.DAT`, `SHIP.V19`, `SHIPTYPE.V14`, `PCSHIP.V05` somehow
    (probably a different `INSTALL.DAT` embedded or renamed files); this RE and the Saga build do
    not cover it, so the C# table only includes the four records from `SdlCompleteDosInstallTable`.
11. **`SHIP.V14` / code id 36** is listed but absent in the GOG install; whether any code path
    requests it (capital ship type 14) is untested.
12. **LZW encoder** — only decoding is specified; the game never writes packets (`SAVEGAME.WLD`
    is written raw via `WriteDataFileAtOffset`).
