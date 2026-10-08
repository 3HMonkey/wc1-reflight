using System.Runtime.CompilerServices;
using WingCommander.Simulation.Data;

namespace WingCommander.Simulation.Missions;

/// <summary>The two object types whose shapes are loaded when entering a nav sphere
/// (<see cref="ObjectType.None"/> = unused).</summary>
/// <remarks>C: MissionNavPoint.preloadObjectTypes[2] (include/wcdata.h).</remarks>
[InlineArray(2)]
public struct NavPreloadTypes
{
    private ObjectType _element0;
}
