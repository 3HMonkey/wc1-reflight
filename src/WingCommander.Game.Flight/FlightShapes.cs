using WingCommander.Core.Resources;
using WingCommander.Game.Resources;
using WingCommander.Graphics.Shapes;
using WingCommander.Simulation;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

/// <summary>
/// Resolves the simulation's shape references (logical file, section) to decoded shape tables.
/// Missing files, missing sections and sections that are not shape tables (raw tables, empty
/// sections) resolve to null, which every drawing call treats like the original's null pointer.
/// Results are cached for the lifetime of the game (the original's load/free choreography only
/// matters where it is observable, and those places keep their own flags).
/// </summary>
/// <remarks>C: FetchDiskPacketRetrying / apObjectShape / the cockpit shape globals.</remarks>
internal sealed class FlightShapes(GameResources resources, ISimulationResources simulationResources)
{
    private readonly Dictionary<(int File, int Section), ShapeTable?> _cache = [];
    private readonly Dictionary<ShapeTable, (int File, int Section)> _origins = new(ReferenceEqualityComparer.Instance);

    /// <summary>The shape table of a section, or null.</summary>
    public ShapeTable? Get(int logicalFile, int section)
    {
        if (_cache.TryGetValue((logicalFile, section), out var cached))
            return cached;
        ShapeTable? shape = null;
        if (logicalFile >= 0 && section >= 0 && simulationResources.SectionExists(logicalFile, section))
        {
            var data = resources.GetSection(logicalFile, section);
            if (ShapeTable.TryParse($"{logicalFile}:{section}", data, out var parsed))
                shape = parsed;
        }
        _cache[(logicalFile, section)] = shape;
        if (shape is not null)
            _origins[shape] = (logicalFile, section);
        return shape;
    }

    /// <summary>The shape table behind a simulation reference, or null.</summary>
    public ShapeTable? Get(ShapeRef shape) => shape.IsNone ? null : Get(shape.LogicalFile, shape.Section);

    /// <summary>The (logical file, section) a shape table was loaded from (the R2 sprite image key).</summary>
    public bool TryGetOrigin(ShapeTable shape, out int logicalFile, out int section)
    {
        if (_origins.TryGetValue(shape, out var origin))
        {
            (logicalFile, section) = origin;
            return true;
        }
        (logicalFile, section) = (-1, -1);
        return false;
    }

    /// <summary>True when the section exists and is a shape table.</summary>
    public bool Exists(int logicalFile, int section) => Get(logicalFile, section) is not null;

    /// <summary>The raw bytes of a section (empty when it does not exist).</summary>
    public ReadOnlyMemory<byte> GetSection(int logicalFile, int section) =>
        logicalFile >= 0 && section >= 0 && simulationResources.SectionExists(logicalFile, section)
            ? resources.GetSection(logicalFile, section)
            : ReadOnlyMemory<byte>.Empty;

    /// <summary>Logical file of the cockpit set <paramref name="cockpit"/> (PCSHIP.V00..V04).</summary>
    /// <remarks>C: <c>cCockpitLogicalFile = cCockpitView + 17</c>.</remarks>
    public static int CockpitFile(int cockpit) => LogicalFile.PcShipV00 + cockpit;
}
