# Progress: resources (Core)

## Status

Done and verified 2026-10-07. Every section of MODULE/TITLE/FONTS/OBJECTS/COCKPIT/MUSIC/
CAMP/BRIEFING/SHIPTYPE/PCSHIP decodes to its declared size (`PacketFileTests`). The LZW
decoder is a direct port of `SdlDecompressOriginLzw`; the analysis verified the same
algorithm bit-exactly over all ~170 LZW sections of the GOG data.

## Mapping

| C (file) | C# | Status |
| --- | --- | --- |
| `SdlDecompressOriginLzw` (sdl/resources.c) | `Core.Resources.OriginLzw.Decompress` | done |
| `SdlExtractOriginPacketSection`, `OpenPacketSection`, `GetPacketSize`, `PacketLoad` (pload.c, screens.c) | `Core.Resources.PacketFile` (`Parse`, `GetSection`, `GetDecodedSize`, `OpenNested`) | done |
| `LoadInstallDat`, `SdlCompleteDosInstallTable` | `Core.Resources.InstallTable.Parse` | done |
| `pDiskFileRecords[]` code ids | `Core.Resources.LogicalFile` constants | done |
| `SdlUsingDosData`, `SdlResolvePath` | `Core.Resources.GameDirectory` (`IsDosData`, case-insensitive lookup) | done |
| `FetchDiskPacketRetrying`, `LoadPacketAllocated`, `LoadPacketIntoBuffer` | `GameDirectory.LoadSection` | done (no retries needed) |
| `PromptInsertNumberedDisk`, `OpenDiskDataFile`, DISK.nnn | not ported (hard-disk install) | n/a |
| `LoadWingCmdrCfgFile` | todo (Game: startup options) | todo |
| `RewriteDiskFileGraphicsExtensions` (.V?? -> .E??/.T??) | not needed (VGA only) | n/a |

## Deviations

- Flag values: 1 = LZW, everything else raw. Flag 2 (DOS graphics) sections are nested
  packets; `PacketFile.OpenNested` parses them. Flag 0xFF marks empty sections.
- Errors throw `GameDataException` instead of setting `nPacketError`.
- Decoded sections are cached per `PacketFile`; callers must treat them as read-only.

## Open questions

See `analysis/resources.md` §8.
