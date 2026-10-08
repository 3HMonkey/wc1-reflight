namespace WingCommander.Core.Rendering;

/// <summary>Sprite mirroring, the original's <c>asObjectFlip</c> bits.</summary>
[Flags]
public enum SpriteFlip : byte
{
    None = 0,

    /// <summary>Mirror left/right (0x10).</summary>
    Horizontal = 0x10,

    /// <summary>Mirror top/bottom (0x20).</summary>
    Vertical = 0x20,
}

/// <summary>
/// One sprite draw in logical 320x200 screen pixels. Unscaled, unrotated and unflipped, frame
/// pixel (i, j) lands on screen pixel (X - OriginX + i, Y - OriginY + j), like the software
/// path. Scale, flip and rotation act around the centre of the hot-spot pixel; positive angles
/// turn clockwise on screen (y points down).
/// </summary>
public struct SpriteInstance
{
    public SpriteInstance()
    {
        Scale = 1f;
        ObjectSlot = -1;
    }

    public SpriteInstance(SpriteImageKey image, float x, float y)
        : this()
    {
        Image = image;
        X = x;
        Y = y;
    }

    public SpriteImageKey Image;

    /// <summary>Screen column of the hot spot (sub-pixel positions allowed).</summary>
    public float X;

    /// <summary>Screen row of the hot spot.</summary>
    public float Y;

    /// <summary>Rotation in degrees, clockwise on screen (<c>asObjectScreenAngle</c>).</summary>
    public float Angle;

    /// <summary>Magnification, 1 = 0x100 (<c>asObjectScreenScale / 256</c>).</summary>
    public float Scale;

    public SpriteFlip Flip;

    /// <summary>Simulation slot the sprite shows, or -1 (effects, HUD-less extras). Used by R3 to swap in meshes.</summary>
    public short ObjectSlot;
}

/// <summary>
/// The sprites of one space frame in painter order (first = farthest). Reusable: the producer
/// calls <see cref="Clear"/> and <see cref="Add(in SpriteInstance)"/> every frame without allocating
/// once the capacity is reached.
/// </summary>
public sealed class SpriteDrawList
{
    private SpriteInstance[] _items;

    public SpriteDrawList(int capacity = 128)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _items = new SpriteInstance[Math.Max(capacity, 4)];
    }

    public int Count { get; private set; }

    public ReadOnlySpan<SpriteInstance> Items => _items.AsSpan(0, Count);

    /// <summary>
    /// Screen rectangle sprites are clipped to: the space buffer's area on the screen (view
    /// geometry origin + size). Default: the whole screen.
    /// </summary>
    public ScreenRect Clip { get; set; } = ScreenRect.Full;

    public void Clear() => Count = 0;

    public void Add(in SpriteInstance sprite)
    {
        if (Count == _items.Length)
            Array.Resize(ref _items, _items.Length * 2);
        _items[Count++] = sprite;
    }

    /// <summary>Appends a default sprite (scale 1, slot -1) and returns it for in-place initialisation.</summary>
    public ref SpriteInstance Add()
    {
        if (Count == _items.Length)
            Array.Resize(ref _items, _items.Length * 2);
        ref SpriteInstance item = ref _items[Count++];
        item = new SpriteInstance();
        return ref item;
    }
}
