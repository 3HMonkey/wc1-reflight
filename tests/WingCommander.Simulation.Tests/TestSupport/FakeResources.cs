using WingCommander.Core.Numerics;
using WingCommander.Simulation;

namespace WingCommander.Simulation.Tests.TestSupport;

/// <summary>Resource provider without game data: every section exists (or none does) and is empty.</summary>
internal sealed class FakeResources(bool everythingExists = true) : ISimulationResources
{
    public List<(int LogicalFile, int Section)> Requests { get; } = [];

    public bool SectionExists(int logicalFile, int section)
    {
        Requests.Add((logicalFile, section));
        return everythingExists;
    }

    public ReadOnlyMemory<byte> LoadSection(int logicalFile, int section) => ReadOnlyMemory<byte>.Empty;

    public static SpaceSimulation CreateSimulation(bool everythingExists = true, uint seed = 12345) =>
        new(new CRandom(seed), new FakeResources(everythingExists));
}
