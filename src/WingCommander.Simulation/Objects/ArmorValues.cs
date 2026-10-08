using System.Runtime.CompilerServices;

namespace WingCommander.Simulation.Objects;

/// <summary>Per-ship armor: [0] front, [1] rear, [2] left, [3] right.</summary>
/// <remarks>C: aasShipArmor[10][4] (globals.h).</remarks>
[InlineArray(4)]
public struct ArmorValues
{
    public const int Front = 0;
    public const int Rear = 1;
    public const int Left = 2;
    public const int Right = 3;

    private short _element0;
}
