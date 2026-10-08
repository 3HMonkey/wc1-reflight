namespace WingCommander.Core.Rendering;

/// <summary>
/// Identifies one decoded shape frame: the packet section of a shape (the simulation's
/// <c>ShapeRef(logicalFile, section)</c>) plus the frame inside it. Capital ships keep one packet
/// per view frame: their key is <c>(type + 22, viewFrame, 0)</c>.
/// </summary>
public readonly record struct SpriteImageKey(short LogicalFile, short Section, short Frame)
{
    public static SpriteImageKey Create(int logicalFile, int section, int frame) =>
        new(checked((short)logicalFile), checked((short)section), checked((short)frame));

    public override string ToString() => $"{LogicalFile}:{Section}#{Frame}";
}

/// <summary>
/// One shape frame decoded to palette indices (Graphics' <c>ShapeFrameDecoder</c> into a bitmap
/// pre-filled with <see cref="TransparentIndex"/>). Immutable once created; renderers upload it
/// once and look colours up in the live palette at draw time, so palette effects apply.
/// </summary>
public sealed class SpriteImage
{
    /// <summary>Index that is never drawn (the gaps between RLE spans).</summary>
    public const byte TransparentIndex = 255;

    public SpriteImage(int width, int height, int originX, int originY, ReadOnlyMemory<byte> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (pixels.Length < width * height)
            throw new ArgumentException($"A {width}x{height} sprite needs {width * height} pixels, got {pixels.Length}.", nameof(pixels));
        Width = width;
        Height = height;
        OriginX = originX;
        OriginY = originY;
        Pixels = pixels;
    }

    /// <summary>Frame width: leftExtent + rightExtent + 1.</summary>
    public int Width { get; }

    /// <summary>Frame height: topExtent + bottomExtent + 1.</summary>
    public int Height { get; }

    /// <summary>Hot spot column inside the frame (leftExtent).</summary>
    public int OriginX { get; }

    /// <summary>Hot spot row inside the frame (topExtent).</summary>
    public int OriginY { get; }

    /// <summary>Width * Height palette indices, row-major; <see cref="TransparentIndex"/> is not drawn.</summary>
    public ReadOnlyMemory<byte> Pixels { get; }

    public bool IsEmpty => Width == 0 || Height == 0;
}
