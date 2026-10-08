namespace WingCommander.Simulation.Data;

/// <summary>
/// Animation command streams compiled into the executable (0x00466030-0x00466400) and used by
/// <c>animate_shape</c>. Each entry is a 32-bit record; the command is its low word:
/// <c>0x9000|n</c> jump to index n, <c>0xa000</c> remove the object, <c>(c &amp; 0xc00) == 0x400</c>
/// grow the scale by <c>(c &amp; 0x3f)/64</c>, <c>0x800</c> shrink, otherwise set frame <c>c &amp; 0x3f</c>.
/// </summary>
/// <remarks>C: anAnim* arrays (globals.c).</remarks>
public static class AnimationScripts
{
    /// <remarks>C: anAnimExplosion0 (0x00466030).</remarks>
    internal static readonly uint[] Explosion0 = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0xa000, 0];

    /// <remarks>C: anAnimExplosion1 (0x00466060).</remarks>
    internal static readonly uint[] Explosion1 =
    [
        0, 0x406, 1, 0x406, 2, 0x406, 0x406, 3, 0x406, 0x406,
        4, 0x406, 0x406, 0x406, 5, 0x406, 0x406, 0x406, 0x406,
        0x406, 0xa000, 0,
    ];

    /// <remarks>C: anAnimExplosion2 (0x004660b8).</remarks>
    internal static readonly uint[] Explosion2 = [0, 0x406, 1, 0x406, 2, 0x406, 3, 0x406, 4, 0x406, 5, 6, 7, 0xa000];

    /// <remarks>C: anAnimLaserSpark (0x004660f0).</remarks>
    internal static readonly uint[] LaserSpark = [0, 1, 2, 3, 4, 5, 0xa000, 0];

    /// <remarks>C: anAnimBlueSpark (0x00466110).</remarks>
    internal static readonly uint[] BlueSpark = [0, 1, 2, 3, 0xa000, 0];

    /// <remarks>C: anAnimRedSpark (0x00466128).</remarks>
    internal static readonly uint[] RedSpark = [0, 1, 2, 3, 0xa000, 0];

    /// <remarks>C: anAnimSparkTrail (0x00466140).</remarks>
    internal static readonly uint[] SparkTrail = [0, 1, 2, 3, 0xa000, 0];

    /// <remarks>C: anAnimGirder (0x00466158).</remarks>
    internal static readonly uint[] Girder = [0, 1, 2, 3, 4, 5, 0x9000, 0];

    /// <remarks>C: anAnimTubing (0x00466178).</remarks>
    internal static readonly uint[] Tubing = [6, 7, 8, 9, 10, 11, 0x9000, 0];

    /// <remarks>C: anAnimGlass (0x00466198).</remarks>
    internal static readonly uint[] Glass =
    [
        12, 13, 14, 15, 16, 17, 18, 19, 0x92, 0x91, 0x90,
        0x8f, 0x8e, 0x8d, 0x8c, 20, 0x9000, 0,
    ];

    /// <remarks>C: anAnimORing (0x004661e0).</remarks>
    internal static readonly uint[] ORing = [21, 22, 23, 24, 25, 26, 0x9000, 0];

    /// <remarks>C: anAnimPipe (0x00466200).</remarks>
    internal static readonly uint[] Pipe = [27, 28, 29, 30, 31, 32, 0x9000, 0];

    /// <remarks>C: anAnimMetalSheet (0x00466220).</remarks>
    internal static readonly uint[] MetalSheet = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 0x9000];

    /// <remarks>C: anAnimWing (0x00466258).</remarks>
    internal static readonly uint[] Wing =
    [
        13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26,
        27, 28, 0x9000, 0,
    ];

    /// <remarks>C: anAnimMine (0x004662a0).</remarks>
    internal static readonly uint[] Mine = [0, 1, 2, 0x41, 0x9000, 0, 0, 1, 2, 3, 4, 5, 0x9000, 0];

    /// <remarks>C: anAnimAsteroidForward (0x004662d8).</remarks>
    internal static readonly uint[] AsteroidForward = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 0x9000, 0];

    /// <remarks>C: anAnimAsteroidShortForward (0x00466318).</remarks>
    internal static readonly uint[] AsteroidShortForward = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 0x9000];

    /// <remarks>C: anAnimAsteroidReverse (0x00466350).</remarks>
    internal static readonly uint[] AsteroidReverse = [13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 0x9000, 0];

    /// <remarks>C: anAnimAsteroidShortReverse (0x00466390).</remarks>
    internal static readonly uint[] AsteroidShortReverse = [12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 0x9000];

    /// <remarks>C: anAnimEjectedPilot (0x004663c8).</remarks>
    internal static readonly uint[] EjectedPilot = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 0x9000];

    /// <remarks>C: anAnimHyperspaceJumpFlash (0x00466400).</remarks>
    internal static readonly uint[] HyperspaceJumpFlash =
    [
        0, 0x402, 0x404, 0x404, 0x408, 0x408, 0x408, 0x410,
        0x410, 0x410, 0x410, 0x820, 0x820, 0x810, 0x808, 0x804,
        1, 0x804, 0x804, 0x804, 0x804, 0xa000,
    ];

    /// <summary>Command: jump to index <c>c &amp; 0x0fff</c>.</summary>
    public const int JumpCommand = 0x9000;

    /// <summary>Command: remove the object.</summary>
    public const int RemoveCommand = 0xa000;
}
