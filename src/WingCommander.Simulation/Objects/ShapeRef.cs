namespace WingCommander.Simulation.Objects;

/// <summary>
/// Reference to a graphics/data packet section by (logical file, section) instead of the
/// original byte pointer. The simulation only stores and compares these; the Game/Graphics
/// layer resolves them to decoded shapes. <c>default</c> is the null pointer
/// (<see cref="None"/>), matching the zero-initialised C globals.
/// </summary>
/// <remarks>C: the <c>unsigned char *</c> results of FetchDiskPacketRetrying(logicalFile, section, flags)
/// stored in apObjectShape, ObjectTypeData.shapeSet/animation/shape, aObjectResourceSlots.</remarks>
public readonly record struct ShapeRef
{
    private readonly short _logicalFilePlusOne;
    private readonly short _section;

    public ShapeRef(int logicalFile, int section)
    {
        if (logicalFile < 0 || logicalFile >= short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(logicalFile));
        _logicalFilePlusOne = (short)(logicalFile + 1);
        _section = (short)section;
    }

    /// <summary>The null pointer.</summary>
    public static ShapeRef None => default;

    public bool IsNone => _logicalFilePlusOne == 0;

    /// <summary>0-based logical file (INSTALL.DAT id - 1); -1 for <see cref="None"/>.</summary>
    public int LogicalFile => _logicalFilePlusOne - 1;

    /// <summary>Section inside the packet file.</summary>
    public int Section => _section;

    public override string ToString() => IsNone ? "null" : $"{LogicalFile}:{Section}";
}
