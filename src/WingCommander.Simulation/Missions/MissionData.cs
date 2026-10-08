namespace WingCommander.Simulation.Missions;

/// <summary>
/// One mission as parsed from a MODULE.00x file: everything <c>LoadMissionData</c> copies into
/// the runtime tables (header globals, 16 nav points, 16 objective sources, 32 ship records and
/// the two 40-byte text blocks). Immutable snapshot; <see cref="SpaceSimulation.LoadMissionData(MissionData)"/>
/// copies it into the mutable runtime arrays.
/// </summary>
/// <remarks>C: the result of LoadMissionData (0x4059B0, cmpgn.c).</remarks>
public sealed class MissionData
{
    internal MissionData(
        int series, int mission, MissionHeader header, MissionNavPoint[] navPoints,
        MissionObjectiveSource[] objectiveSources, MissionShipRecord[] ships,
        byte[] missionAux, byte[] seriesAux)
    {
        Series = series;
        Mission = mission;
        Header = header;
        NavPoints = navPoints;
        ObjectiveSources = objectiveSources;
        Ships = ships;
        MissionAux = missionAux;
        SeriesAux = seriesAux;
        MissionName = MissionModule.DecodeText(missionAux.AsSpan());
        SeriesName = MissionModule.DecodeText(seriesAux.AsSpan());
    }

    /// <summary>Series number (0 = training simulator, campaign series are 1-based).</summary>
    public int Series { get; }

    /// <summary>Mission within the series (0..3).</summary>
    public int Mission { get; }

    /// <summary><c>mission + series * 4</c>.</summary>
    public int MissionIndex => MissionModule.GetMissionIndex(Series, Mission);

    public MissionHeader Header { get; }

    /// <summary>The 16 loaded nav point records (aMissionNavPoints[0..15]).</summary>
    public IReadOnlyList<MissionNavPoint> NavPoints { get; }

    /// <summary>The 16 objective records (aMissionObjectiveSources).</summary>
    public IReadOnlyList<MissionObjectiveSource> ObjectiveSources { get; }

    /// <summary>The 32 ship records (aMissionShips[0..31]).</summary>
    public IReadOnlyList<MissionShipRecord> Ships { get; }

    /// <summary>MODULE section 4 record (abMissionAuxData): the mission (wing) name.</summary>
    public IReadOnlyList<byte> MissionAux { get; }

    /// <summary>MODULE section 5 record of the series (abSeriesAuxData): the system name.</summary>
    public IReadOnlyList<byte> SeriesAux { get; }

    /// <summary>Text of <see cref="MissionAux"/> up to the first NUL (e.g. "Alpha Wing").</summary>
    public string MissionName { get; }

    /// <summary>Text of <see cref="SeriesAux"/> up to the first NUL (e.g. "Enyo").</summary>
    public string SeriesName { get; }

    /// <summary>Number of objective records before the -1 terminator (at most 16).</summary>
    public int ObjectiveCount
    {
        get
        {
            int count = 0;
            while (count < ObjectiveSources.Count && ObjectiveSources[count].Type != -1)
                count++;
            return count;
        }
    }
}
