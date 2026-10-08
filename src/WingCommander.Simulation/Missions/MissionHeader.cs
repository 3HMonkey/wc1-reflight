namespace WingCommander.Simulation.Missions;

/// <summary>
/// Per-mission header from MODULE section 0 (0x18 bytes). <see cref="InitialMissionShips"/> are
/// the player's team (wingmen) spawned at the entry nav point; -1 = empty.
/// </summary>
/// <remarks>C: MissionHeaderDisk (cmpgn.c) → nMissionEntryNavPoint, nHomeMissionShipIndex,
/// nPlayerMissionShipIndex, nInitialMissionShipIndices[8], DAT_005a86a6.</remarks>
public sealed record MissionHeader(
    short EntryNavPoint,
    short HomeMissionShip,
    short PlayerMissionShip,
    IReadOnlyList<short> InitialMissionShips,
    short Field16);
