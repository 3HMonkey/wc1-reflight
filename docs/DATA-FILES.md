# GOG DOS data files (`<GOG install>`)

Top level: `WC.EXE` (WC1 + Secret Missions 1), `SM2.EXE` (Secret Missions 2 "Crusade"),
`TRANSFER.EXE`/`TRANS2.EXE` (pilot transfer), `WINGCMDR.CFG` / `CRUSADE.CFG` (11 bytes,
`v a904 z` — the command-line options file read by `LoadWingCmdrCfgFile`), DOSBox configs.
Cloud saves overlay `cloud_saves/`.

All game data is in `GAMEDAT/`. Nearly every file is an **Origin packet container**
(see `analysis/resources.md`). Sizes from the GOG install:

| File | Size | Content (from reference code) |
| --- | ---: | --- |
| INSTALL.DAT | 1184 | 74 x 16-byte `DiskFileRecord` {name[13], disk, class, logicalFile}; maps logical file ids to filenames. DOS table gets 4 extra SM2 records added by the port. |
| MODULE.000/.001/.002 | 22k/18k/17k | Mission data per campaign (0 = original, 1 = Secret Missions, 2 = SM2): nav points (77 B), ships (42 B), map/flight-plan records (64 B). |
| CAMP.000/.001/.002 | 651/456/437 | Campaign tree (series/mission branching). |
| BRIEFING.000/.001/.002 | 222k/85k/90k | Briefing/debriefing/rec-room conversation scripts and text. |
| BRIEFING.VGA | 516k | Briefing room graphics. |
| TITLE.VGA / TITLE1.VGA | 271k/50k | Title screens (TITLE1 = SM2). |
| COCKPIT.VGA | 50k | Cockpit frames. |
| OBJECTS.VGA | 126k | Space object shapes (weapons, explosions, debris...). |
| PCSHIP.V00-V05 | | Player ship cockpits/resources (packet 6 = view geometries). |
| SHIP.V04-V21, SHIPTYPE.V00-V29 | | Per-ship-type shape sets and type data. |
| PLANETS.VGA / PLANETS1.VGA | 16k/12k | Background planets. |
| MIDGAME.V00-V08 | | Mid-game cutscenes. |
| RECROOM.VGA, SCRAMBLE.VGA, TALKING.VGA, TALKING1.VGA, PILOTANM.VGA, WINGMEN.VGA, WINGMEN1.VGA, SERIES.VGA, ARROW.VGA | | Non-flight screen art, talking heads, scramble/launch animation, pilot animations. |
| FONTS.FNT | 7664 | Packed game fonts (sections = fonts). |
| GAME.PAL | 1120 | Palette(s). CONVERT.PAL (256 B) = conversion table. |
| COMMUNIC.DAT / COMMSM2.DAT | 12320 | Comm (wingman radio) text/data. |
| MUSIC.MID | 139k | OriginFX music sequences (packet container). |
| WINGLDR.TIM | 5596 | AdLib/OPL2 timbre bank for OriginFX. |
| STRAX.DRV, SUPERTM.DRV, TM.DRV | | DOS sound-driver code (x86); not needed, we emulate OPL2 directly. |
| SAVEGAME.WLD / CRUSADE.WLD | 6624 | Save game slots (8 x 0x33C `SaveGameDiskRecord`). |
| DISK.001-.010, INTRO.DAT, INTRO1.DAT | | Disk markers (floppy era), intro definitions. |

## Verified structure (2026-10-07, via `wc1tool dump --nested`)

Directory-entry flag byte: **0 = raw**, **1 = LZW** (u32 size prefix), **2 = raw section
that is itself a packet container** (nested). The reference port only special-cases 1.

| File | Sections | Notes |
| --- | --- | --- |
| MODULE.000 | 6 | all LZW |
| TITLE.VGA | 18 | all flag 2 = nested packets; e.g. [0] has 3 raw sub-sections (6178/10314/6601 B), [1] has 60 sub-sections (~100-350 B each = small shape frames) |
| OBJECTS.VGA | 18 | all nested packets (shape tables per object kind) |
| FONTS.FNT | 4 | LZW; decoded 12949 / 6660 / 4342 / 17470 B (4 fonts) |
| MUSIC.MID | 41 | LZW; [0] decodes to 13264 B |
| CAMP.000 | 3 | LZW; decoded 416 / 1170 / 104 B |
| GAME.PAL | - | **not a packet**: raw 1120 bytes of palette data |

Tool usage: `wc1tool dump TITLE.VGA --nested`, `wc1tool hex TITLE.VGA 1/0 --length 64`,
`wc1tool install` (prints the logical file table; env `WC1_GAME_DIR` or `--game`).
