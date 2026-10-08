namespace WingCommander.Simulation.Objects;

/// <summary>
/// Read position in a canned (scripted) command stream; replaces the original
/// <c>const short *</c> that was advanced in place.
/// </summary>
/// <remarks>C: apCannedSequence[12] (globals.h).</remarks>
public struct CannedSequenceCursor
{
    /// <summary>The command stream, or null for "no sequence" (the null pointer).</summary>
    public short[]? Script;

    /// <summary>Index of the next word to read.</summary>
    public int Position;

    public CannedSequenceCursor(short[]? script)
    {
        Script = script;
        Position = 0;
    }

    public readonly bool IsNull => Script is null;

    /// <summary>Reads the next word (<c>*command++</c>).</summary>
    public short Next() => Script![Position++];
}
