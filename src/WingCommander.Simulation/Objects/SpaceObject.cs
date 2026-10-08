using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;

namespace WingCommander.Simulation.Objects;

/// <summary>
/// One of the 64 object slots: every per-object array of the original that applies to all
/// slots. The slot index is the identity (see <see cref="ObjectSlots"/>). The orientation
/// basis is i = <see cref="Right"/>, j = <see cref="Up"/>, k = <see cref="Forward"/> (unit 24.8
/// vectors, left-handed: forward +z, right +x, up +y).
/// </summary>
/// <remarks>C: the SPACE_OBJECT_COUNT arrays of include/globals.h (aeObjectType, aeObjectClass,
/// aShipPosition, aShipVelocity, aShip{Right,Up,Forward}Vector, anObject{Pitch,Yaw,Roll}Rotation,
/// anShipSpeed[64], asObjectCounter, acObjectOwner, asObjectCollisionRadius, ...).</remarks>
public struct SpaceObject
{
    /// <summary>aeObjectType.</summary>
    public ObjectType Type;

    /// <summary>aeObjectClass (<see cref="ObjectClass.Null"/> = free slot).</summary>
    public ObjectClass Class;

    /// <summary>aShipPosition: world position, 24.8.</summary>
    public FixedVector Position;

    /// <summary>aShipVelocity: displacement per frame, 24.8.</summary>
    public FixedVector Velocity;

    /// <summary>aShipRightVector (i).</summary>
    public FixedVector Right;

    /// <summary>aShipUpVector (j).</summary>
    public FixedVector Up;

    /// <summary>aShipForwardVector (k).</summary>
    public FixedVector Forward;

    /// <summary>anObjectPitchRotation: degrees per frame, decays by one per frame.</summary>
    public short PitchRotation;

    /// <summary>anObjectYawRotation.</summary>
    public short YawRotation;

    /// <summary>anObjectRollRotation.</summary>
    public short RollRotation;

    /// <summary>anShipSpeed: commanded speed, 24.8 units/frame.</summary>
    public int Speed;

    /// <summary>asObjectCounter: multi-use countdown (gun refire, lifetime, warp/death sequence,
    /// dust phase, a futurion's saved class).</summary>
    public short Counter;

    /// <summary>acObjectOwner: creator slot or -1.</summary>
    public sbyte Owner;

    /// <summary>asObjectCollisionRadius in units.</summary>
    public short CollisionRadius;

    /// <summary>asObjectRadarRadius: the mass in the collision/force model.</summary>
    public short RadarRadius;

    /// <summary>asObjectAfterburnerVelocity: misnamed rotational inertia constant.</summary>
    public short AfterburnerVelocity;

    /// <summary>asObjectScale: base sprite scale (0x100 = 1.0).</summary>
    public short Scale;

    /// <summary>asObjectScreenScale: projected scale of the last rendered frame.</summary>
    public short ScreenScale;

    /// <summary>asObjectScreenX relative to the view centre; <see cref="ObjectSlots.NotVisible"/> = not visible.</summary>
    public short ScreenX;

    /// <summary>asObjectScreenY.</summary>
    public short ScreenY;

    /// <summary>asObjectDrawX: final draw position.</summary>
    public short DrawX;

    /// <summary>asObjectDrawY.</summary>
    public short DrawY;

    /// <summary>asObjectDistance: eye distance in units, 0 = not visible.</summary>
    public short Distance;

    /// <summary>asPreviousObjectDistance.</summary>
    public short PreviousDistance;

    /// <summary>aObjectViewPosition: eye-space position.</summary>
    public FixedVector ViewPosition;

    /// <summary>asObjectScreenAngle: sprite roll 0..359 (dust: streak*0x10 + frame).</summary>
    public short ScreenAngle;

    /// <summary>asObjectFlip: 0x10 mirror X, 0x20 mirror Y.</summary>
    public short Flip;

    /// <summary>asObjectViewFrame: sprite frame within <see cref="Shape"/>.</summary>
    public short ViewFrame;

    /// <summary>apObjectShape: the sprite set.</summary>
    public ShapeRef Shape;

    /// <summary>asObjectAnimationDelay: frames until the next animation step.</summary>
    public short AnimationDelay;

    /// <summary>asObjectAnimationIndex: position in the animation script.</summary>
    public short AnimationIndex;

    /// <summary>acLastCollisionObject (collision debounce).</summary>
    public sbyte LastCollisionObject;

    /// <summary>acObjectCollisionGraceTicks: frames during which the owner is immune.</summary>
    public sbyte CollisionGraceTicks;

    /// <summary>asShipAccumulatedDamage: projectile payload, ship internal damage events, asteroid damage.</summary>
    public short AccumulatedDamage;

    /// <summary>
    /// Port addition (R2b): a new number whenever the slot is given to a new object, so renderers
    /// can tell an object's next tick from a new object in a reused slot. The simulation never
    /// reads it.
    /// </summary>
    public int SpawnId;

    /// <summary>Sets the basis to identity and clears the rotation rates.</summary>
    /// <remarks>C: init_ijk (0x418F60, geom.c).</remarks>
    public void InitIjk()
    {
        Forward = new FixedVector(0, 0, 0x100);
        Up = new FixedVector(0, 0x100, 0);
        Right = new FixedVector(0x100, 0, 0);
        RollRotation = 0;
        YawRotation = 0;
        PitchRotation = 0;
    }

    /// <summary>Copies the basis of <paramref name="source"/>.</summary>
    /// <remarks>C: copy_frame (0x418FD0, geom.c).</remarks>
    public void CopyFrameFrom(in SpaceObject source)
    {
        Right = source.Right;
        Up = source.Up;
        Forward = source.Forward;
    }

    /// <summary>Re-orthogonalises the basis: <c>right = up x fwd; up = fwd x right</c>, then
    /// normalises right, up, forward (float rounding makes the drift deterministic).</summary>
    /// <remarks>C: fix_objects_ijk (0x419050, geom.c).</remarks>
    public void FixIjk()
    {
        Right = VectorMath.Cross(Up, Forward);
        Up = VectorMath.Cross(Forward, Right);
        VectorMath.Normalize(ref Right);
        VectorMath.Normalize(ref Up);
        VectorMath.Normalize(ref Forward);
    }

    /// <summary>World direction to object-local coordinates <c>(v·right, v·up, v·fwd)</c>.</summary>
    /// <remarks>C: transform_to_objects_frame (0x4190B0, geom.c).</remarks>
    public readonly FixedVector TransformToObjectsFrame(in FixedVector source) =>
        new(VectorMath.Dot(source, Right), VectorMath.Dot(source, Up), VectorMath.Dot(source, Forward));

    /// <summary>Rotates up/forward about the right axis, then <see cref="FixIjk"/>.</summary>
    /// <remarks>C: alter_pitch (0x419110, geom.c).</remarks>
    public void AlterPitch(short angle)
    {
        VectorMath.RotateAboutI(angle, ref Up, ref Forward);
        FixIjk();
    }

    /// <summary>Rotates right/forward about the up axis, then <see cref="FixIjk"/>.</summary>
    /// <remarks>C: alter_yaw (0x419150, geom.c).</remarks>
    public void AlterYaw(short angle)
    {
        VectorMath.RotateAboutJ(angle, ref Right, ref Forward);
        FixIjk();
    }

    /// <summary>Rotates right/up about the forward axis, then <see cref="FixIjk"/>.</summary>
    /// <remarks>C: alter_roll (0x419190, geom.c).</remarks>
    public void AlterRoll(short angle)
    {
        VectorMath.RotateAboutK(angle, ref Right, ref Up);
        FixIjk();
    }

    /// <summary>Points the forward axis at <paramref name="point"/> (via <c>shrink_vector</c>, not a
    /// normalisation) and re-orthogonalises; up is kept, right recomputed.</summary>
    /// <remarks>C: point_at (0x418330, geom.c).</remarks>
    public void PointAt(in FixedVector point)
    {
        var direction = VectorMath.Delta(Position, point);
        VectorMath.ShrinkVector(ref direction);
        Forward = direction;
        FixIjk();
    }

    /// <summary>|velocity| in whole units, saturated.</summary>
    /// <remarks>C: real_velocity (0x41E7F0, disk.c).</remarks>
    public readonly short RealVelocity() => FixedMath.ToShortSaturating(Velocity.Magnitude());

    /// <summary>Sets the velocity to <c>forward * speed</c>.</summary>
    /// <remarks>C: fix_velocity (0x41E820, disk.c).</remarks>
    public void FixVelocity() => Velocity = VectorMath.Scale(Forward, Speed);

    /// <summary>Position offset by <paramref name="right"/>/<paramref name="up"/>/<paramref name="forward"/>
    /// units along this object's axes.</summary>
    /// <remarks>C: position_relative_ijk (0x418420, geom.c).</remarks>
    public readonly FixedVector PositionRelativeIjk(short right, short up, short forward)
    {
        var position = Position;
        VectorMath.PositionRelative(ref position, Right, right);
        VectorMath.PositionRelative(ref position, Up, up);
        VectorMath.PositionRelative(ref position, Forward, forward);
        return position;
    }

    /// <summary>This object's position offset along its forward, up and right axes (in that order).</summary>
    /// <remarks>C: offset_location (0x433F50, smart.c).</remarks>
    public readonly FixedVector OffsetLocation(in ShortVector offset)
    {
        var location = Position;
        VectorMath.PositionRelative(ref location, Forward, offset.Z);
        VectorMath.PositionRelative(ref location, Up, offset.Y);
        VectorMath.PositionRelative(ref location, Right, offset.X);
        return location;
    }

    public override readonly string ToString() => $"{Class} {Type} at {Position}";
}
