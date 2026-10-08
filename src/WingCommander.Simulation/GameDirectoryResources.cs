using WingCommander.Core.Resources;

namespace WingCommander.Simulation;

/// <summary><see cref="ISimulationResources"/> over the game data directory (INSTALL.DAT logical files).</summary>
public sealed class GameDirectoryResources(GameDirectory directory) : ISimulationResources
{
    public GameDirectory Directory { get; } = directory;

    public bool SectionExists(int logicalFile, int section)
    {
        if (!Directory.InstallTable.TryGet(logicalFile, out var record) || !Directory.Exists(record.Name))
            return false;
        var packet = Directory.OpenPacket(logicalFile);
        return section >= 0 && section < packet.SectionCount;
    }

    public ReadOnlyMemory<byte> LoadSection(int logicalFile, int section) => Directory.LoadSection(logicalFile, section);
}
