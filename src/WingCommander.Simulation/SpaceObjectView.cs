using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

/// <summary>
/// One object of a <see cref="SpaceViewSnapshot"/>: identity, 3D state for a modern renderer
/// (position and orientation basis in world units, 24.8 fixed point) and the original sprite
/// projection as a fallback (shape, view frame, flip, screen angle and scale, screen position).
/// Data only.
/// </summary>
public struct SpaceObjectView
{
    /// <summary>Object slot (the object's identity while it lives; slots are reused).</summary>
    public short Slot;

    /// <summary>Changes whenever the slot is given to a new object (<see cref="SpaceObject.SpawnId"/>).</summary>
    public int SpawnId;

    public ObjectType Type;

    public ObjectClass Class;

    /// <summary>Owning object slot or -1 (projectiles, missiles, effects, engine flames).</summary>
    public sbyte Owner;

    /// <summary>World position (24.8). Stars and planets are sky objects: their position is the
    /// direction from the eye (15000 units for stars), not a world position.</summary>
    public FixedVector Position;

    /// <summary>World velocity per 20 Hz frame (24.8), for interpolation.</summary>
    public FixedVector Velocity;

    /// <summary>Orientation basis (unit vectors in 8.8: 0x100 = 1).</summary>
    public FixedVector Right;

    public FixedVector Up;

    public FixedVector Forward;

    /// <summary>Sprite scale in 8.8 (0x100 = 100 %).</summary>
    public short Scale;

    /// <summary>Collision radius in units (also the object's size for culling).</summary>
    public short CollisionRadius;

    /// <summary>The object's own shape (sprite set; capital ships: the shape of the current view
    /// frame, <c>ShapeRef(type + 22, frame)</c>; the nav pointer: none).</summary>
    public ShapeRef Shape;

    /// <summary>The shape the original draw list uses: the constellation shape for stars and dust
    /// (unscaled), the object's shape otherwise (planets scaled, as in the SDL port).</summary>
    public ShapeRef DrawShape;

    /// <summary>Sprite frame (ships: one of 37 pre-rendered views; capital ships: 0 of the per-view shape).</summary>
    public short ViewFrame;

    /// <summary>Sprite mirroring: 0x10 horizontal, 0x20 vertical.</summary>
    public short Flip;

    /// <summary>Sprite rotation in degrees (0..359).</summary>
    public short ScreenAngle;

    /// <summary>Sprite scale on screen in 8.8 (0x100 = 1:1).</summary>
    public short ScreenScale;

    /// <summary>Screen position relative to the view centre (add <see cref="SpaceViewSnapshot.ViewCenterX"/>/Y);
    /// <see cref="ObjectSlots.NotVisible"/> in X when the projection culled the object.</summary>
    public short ScreenX;

    public short ScreenY;

    /// <summary>Distance from the eye in units at the last projection (0 = not visible).</summary>
    public short Distance;

    /// <summary>Position in the eye's frame (x right, y up, z ahead) at the last projection.</summary>
    public FixedVector ViewPosition;

    /// <summary>The cockpit's nav pointer (drawn with the cockpit target-lock shape, frame 3).</summary>
    public bool IsNavPointer;

    /// <summary>Ships and missiles: side, special maneuver (afterburner glow), engine heat
    /// (0 off, 2 thrust, 3 afterburner), commanded speed (24.8) and the capital ship view frame.</summary>
    public Side Side;

    public SpecialManeuver SpecialManeuver;

    public sbyte ExhaustHeat;

    public int Speed;

    public short CapitalShipViewFrame;

    /// <summary>Whether the original projection put the object on screen.</summary>
    public readonly bool Visible => ScreenX != ObjectSlots.NotVisible;
}
