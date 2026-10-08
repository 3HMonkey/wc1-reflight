namespace WingCommander.Simulation.Data;

/// <summary>
/// Weighted maneuver pair: <c>RandomBelowOrEqual(100) &lt; Threshold ? Primary : Secondary</c>;
/// -1 means "no maneuver".
/// </summary>
/// <remarks>C: ManeuverChoice (include/wcdata.h).</remarks>
public readonly record struct ManeuverChoice(sbyte Threshold, sbyte Primary, sbyte Secondary);
