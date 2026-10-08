using System.Runtime.CompilerServices;

namespace WingCommander.Simulation.Missions;

/// <summary>
/// Four nav-point triggers as eight signed bytes: pairs <c>{newType, navIndex}</c>; a newType of
/// -1 is "no trigger". Entering the nav sphere sets <c>navPoints[navIndex].Type = newType</c>.
/// </summary>
/// <remarks>C: MissionNavPoint.triggers[4][2] (include/wcdata.h).</remarks>
[InlineArray(8)]
public struct NavTriggers
{
    private sbyte _element0;
}
