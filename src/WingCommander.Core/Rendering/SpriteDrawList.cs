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

    /// <summary>Vertical magnification when it differs from <see cref="Scale"/> (HUD lines are a stretched pixel); 0 = <see cref="Scale"/>.</summary>
    public float ScaleY;

    public SpriteFlip Flip;

    /// <summary>Simulation slot the sprite shows, or -1 (effects, HUD-less extras). Pairs the sprites of consecutive ticks (R2b); R3 swaps in meshes.</summary>
    public short ObjectSlot;

    /// <summary>
    /// Set when the sprite showed the same thing one simulation tick earlier (R2b): renderers draw
    /// it in between the two (<see cref="At"/>), so motion is smooth at any display rate.
    /// </summary>
    public bool HasPrevious;

    public float PreviousX;

    public float PreviousY;

    public float PreviousAngle;

    public float PreviousScale;

    public float PreviousScaleY;

    /// <summary>The vertical magnification in effect.</summary>
    public readonly float VerticalScale => ScaleY != 0f ? ScaleY : Scale;

    /// <summary>
    /// The sprite at <paramref name="t"/> between the previous tick (0) and this one (1, or without
    /// a previous state): position and scale move linearly, the angle turns the short way; the
    /// image is this tick's.
    /// </summary>
    public readonly SpriteInstance At(float t)
    {
        if (!HasPrevious || t >= 1f)
            return this;
        t = MathF.Max(t, 0f);
        SpriteInstance sprite = this;
        sprite.X = PreviousX + (X - PreviousX) * t;
        sprite.Y = PreviousY + (Y - PreviousY) * t;
        sprite.Scale = PreviousScale + (Scale - PreviousScale) * t;
        if (ScaleY != 0f)
            sprite.ScaleY = PreviousScaleY + (ScaleY - PreviousScaleY) * t;
        float turn = ((Angle - PreviousAngle) % 360f + 540f) % 360f - 180f;
        sprite.Angle = PreviousAngle + turn * t;
        return sprite;
    }
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

    /// <summary>The sprite at <paramref name="index"/>, for changes after it was added.</summary>
    public ref SpriteInstance this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Count, nameof(index));
            return ref _items[index];
        }
    }

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
