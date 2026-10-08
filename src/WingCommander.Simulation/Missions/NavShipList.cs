using System.Runtime.CompilerServices;

namespace WingCommander.Simulation.Missions;

/// <summary>The ten mission-ship record indices spawned at a nav point (-1 = empty).</summary>
/// <remarks>C: MissionNavPoint.missionShips[10] (include/wcdata.h).</remarks>
[InlineArray(10)]
public struct NavShipList
{
    private short _element0;
}
