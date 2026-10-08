using System.Numerics;

namespace WingCommander.Core.Rendering;

/// <summary>
/// The eye of a <see cref="SpaceViewState"/>, in float world units (the simulation's 24.8 fixed
/// point / 256) with an orthonormal basis.
/// </summary>
public struct SpaceCamera
{
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 Right;
    public Vector3 Up;
    public Vector3 Forward;

    /// <summary>Near-plane distance (the eye's collision radius).</summary>
    public float NearRadius;

    /// <summary>Projection focal length in logical pixels: (ScreenWidth &amp; ~1) / 2, 160 for a 320-wide view.</summary>
    public float FocalLength;

    /// <summary>Projection centre on the 320x200 screen: geometry origin + nViewCenterX/Y.</summary>
    public float CenterX;

    public float CenterY;

    /// <summary>The space buffer on the screen (view geometry origin and size).</summary>
    public ScreenRect Viewport;
}

/// <summary>
/// One object of a <see cref="SpaceViewState"/>: identity, 3D state (meshes, interpolation) and
/// the sprite the original draws for it. Field names follow the simulation's
/// <c>WingCommander.Simulation.SpaceObjectView</c>; the Game converts fixed point to float and
/// view-centre-relative screen positions to absolute screen positions.
/// </summary>
public struct SpaceObjectState
{
    /// <summary>Simulation slot 0..60: pairs the objects of two consecutive ticks.</summary>
    public short Slot;

    /// <summary>ObjectType number (ship type, missile, effect ...).</summary>
    public short Type;

    /// <summary>ObjectClass number (ship, capital ship, projectile, star, dust, planet ...).</summary>
    public short Class;

    /// <summary>Owning slot or -1 (projectiles, effects, engine flames).</summary>
    public short Owner;

    /// <summary>The original projection put the object on screen this tick.</summary>
    public bool Visible;

    /// <summary>Stars and planets: <see cref="Position"/> is a direction from the eye, not a world position.</summary>
    public bool IsSkyObject;

    /// <summary>The cockpit's nav pointer pseudo-object.</summary>
    public bool IsNavPointer;

    /// <summary>World position (sky objects: direction from the eye times their distance).</summary>
    public Vector3 Position;

    /// <summary>World velocity per 20 Hz tick.</summary>
    public Vector3 Velocity;

    public Vector3 Right;
    public Vector3 Up;
    public Vector3 Forward;

    /// <summary>Object scale, 1 = 0x100.</summary>
    public float Scale;

    /// <summary>Collision radius in world units (size for culling and meshes).</summary>
    public float CollisionRadius;

    /// <summary>Position in the eye's frame at the projection (x right, y up, z ahead).</summary>
    public Vector3 ViewPosition;

    /// <summary>The sprite of the original draw list: draw shape + view frame (stars/dust: constellation shape).</summary>
    public SpriteImageKey Sprite;

    public SpriteFlip Flip;

    /// <summary>Hot spot on the 320x200 screen (geometry origin + view centre + projected offset).</summary>
    public float ScreenX;

    public float ScreenY;

    /// <summary>Sprite magnification on screen, 1 = 0x100.</summary>
    public float ScreenScale;

    /// <summary>Sprite rotation in degrees.</summary>
    public float ScreenAngle;

    /// <summary>Distance from the eye at the projection (0 = not visible); the painter's key.</summary>
    public int Distance;

    /// <summary>Engine heat for glow effects: 0 off, 2 thrust, 3 afterburner.</summary>
    public sbyte ExhaustHeat;
}

/// <summary>
/// The 3D state of the space view after one 20 Hz simulation tick, in renderer terms. The Game
/// fills it from <c>Simulation.SpaceViewSnapshot</c> (CaptureSpaceView) and keeps the previous
/// tick in a second instance (<see cref="CopyFrom"/>) for interpolation. Reusable: nothing
/// allocates once the capacity is reached.
/// </summary>
public sealed class SpaceViewState
{
    private SpaceObjectState[] _objects;
    private short[] _drawOrder;

    public SpaceViewState(int capacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _objects = new SpaceObjectState[capacity];
        _drawOrder = new short[capacity];
    }

    /// <summary>nSpaceFrame of the captured state.</summary>
    public int SpaceFrame { get; set; }

    /// <summary>Camera view mode (0 cockpit front ... 15 scripted); only mode 0 has a HUD.</summary>
    public int CameraViewMode { get; set; }

    public SpaceCamera Camera;

    public int Count { get; private set; }

    public Span<SpaceObjectState> Objects => _objects.AsSpan(0, Count);

    public int DrawCount { get; private set; }

    /// <summary>Indices into <see cref="Objects"/>, farthest first (the original painter's order).</summary>
    public ReadOnlySpan<short> DrawOrder => _drawOrder.AsSpan(0, DrawCount);

    public void Clear()
    {
        Count = 0;
        DrawCount = 0;
    }

    /// <summary>Appends an object and returns it for in-place initialisation.</summary>
    public ref SpaceObjectState Add()
    {
        if (Count == _objects.Length)
            Array.Resize(ref _objects, _objects.Length * 2);
        ref SpaceObjectState item = ref _objects[Count++];
        item = default;
        item.Owner = -1;
        item.Scale = 1f;
        item.ScreenScale = 1f;
        return ref item;
    }

    /// <summary>Appends an index into <see cref="Objects"/> to the draw order.</summary>
    public void AddToDrawOrder(int objectIndex)
    {
        if ((uint)objectIndex >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(objectIndex));
        if (DrawCount == _drawOrder.Length)
            Array.Resize(ref _drawOrder, _drawOrder.Length * 2);
        _drawOrder[DrawCount++] = (short)objectIndex;
    }

    /// <summary>Copies <paramref name="other"/> into this instance (keeps the previous tick without allocating).</summary>
    public void CopyFrom(SpaceViewState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (_objects.Length < other.Count)
            _objects = new SpaceObjectState[other._objects.Length];
        if (_drawOrder.Length < other.DrawCount)
            _drawOrder = new short[other._drawOrder.Length];
        other.Objects.CopyTo(_objects);
        other.DrawOrder.CopyTo(_drawOrder);
        Count = other.Count;
        DrawCount = other.DrawCount;
        SpaceFrame = other.SpaceFrame;
        CameraViewMode = other.CameraViewMode;
        Camera = other.Camera;
    }

    /// <summary>Finds the object of <paramref name="slot"/> (interpolation partner), or -1.</summary>
    public int IndexOfSlot(short slot)
    {
        for (int i = 0; i < Count; i++)
        {
            if (_objects[i].Slot == slot)
                return i;
        }
        return -1;
    }
}
