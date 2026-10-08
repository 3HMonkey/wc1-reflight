using System.Runtime.CompilerServices;

namespace WingCommander.Simulation.Objects;

/// <summary>Per-ship shield strengths: [0] fore, [1] aft.</summary>
/// <remarks>C: aasShipShield[10][2], aasShipMaximumShield[10][2] (globals.h).</remarks>
[InlineArray(2)]
public struct ShieldValues
{
    public const int Fore = 0;
    public const int Aft = 1;

    private short _element0;
}
