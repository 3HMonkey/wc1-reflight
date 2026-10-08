namespace WingCommander.Simulation;

/// <summary>
/// Data access of the simulation. Graphics are referenced by (logical file, section) through
/// <see cref="Objects.ShapeRef"/>; the simulation only asks whether a section exists (the
/// original's <c>FetchDiskPacketRetrying != 0</c>) and reads the raw bytes it interprets itself
/// (MODULE mission tables, ship exhaust tables).
/// </summary>
public interface ISimulationResources
{
    /// <summary>True when section <paramref name="section"/> of logical file
    /// <paramref name="logicalFile"/> can be loaded.</summary>
    /// <remarks>C: FetchDiskPacketRetrying(logicalFile, section, flags) returning non-null.</remarks>
    bool SectionExists(int logicalFile, int section);

    /// <summary>Decoded bytes of a section.</summary>
    /// <remarks>C: FetchDiskPacketRetrying / LoadPacketAllocated.</remarks>
    ReadOnlyMemory<byte> LoadSection(int logicalFile, int section);
}
