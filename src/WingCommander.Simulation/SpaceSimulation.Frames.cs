using WingCommander.Core.Numerics;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// Orientation, facing/range queries and steering-goal geometry on object slots (geom.c).
public sealed partial class SpaceSimulation
{
    /// <remarks>C: init_ijk (0x418F60, geom.c).</remarks>
    public void InitIjk(short obj) => Objects[obj].InitIjk();

    /// <remarks>C: copy_frame (0x418FD0, geom.c).</remarks>
    public void CopyFrame(short source, short destination) => Objects[destination].CopyFrameFrom(Objects[source]);

    /// <remarks>C: fix_objects_ijk (0x419050, geom.c).</remarks>
    public void FixObjectsIjk(short obj) => Objects[obj].FixIjk();

    /// <remarks>C: transform_to_objects_frame (0x4190B0, geom.c).</remarks>
    public FixedVector TransformToObjectsFrame(in FixedVector source, short obj) => Objects[obj].TransformToObjectsFrame(source);

    /// <remarks>C: alter_pitch (0x419110, geom.c).</remarks>
    public void AlterPitch(short angle, short obj) => Objects[obj].AlterPitch(angle);

    /// <remarks>C: alter_yaw (0x419150, geom.c).</remarks>
    public void AlterYaw(short angle, short obj) => Objects[obj].AlterYaw(angle);

    /// <remarks>C: alter_roll (0x419190, geom.c).</remarks>
    public void AlterRoll(short angle, short obj) => Objects[obj].AlterRoll(angle);

    /// <remarks>C: point_at (0x418330, geom.c).</remarks>
    public void PointAt(short obj, FixedVector point) => Objects[obj].PointAt(point);

    /// <summary>Points the eye at an object.</summary>
    /// <remarks>C: look_at (0x4183A0, geom.c).</remarks>
    public void LookAt(short obj) => PointAt(ObjectSlots.Eye, Objects[obj].Position);

    /// <remarks>C: position_relative_ijk (0x418420, geom.c).</remarks>
    public FixedVector PositionRelativeIjk(short obj, short right, short up, short forward) =>
        Objects[obj].PositionRelativeIjk(right, up, forward);

    /// <remarks>C: offset_location (0x433F50, smart.c).</remarks>
    public FixedVector OffsetLocation(short obj, in Data.ShortVector offset) => Objects[obj].OffsetLocation(offset);

    /// <summary>Moves an object <paramref name="distance"/> units along <paramref name="direction"/>.</summary>
    /// <remarks>C: MoveObjectAlongDirection (0x4198A0, geom.c).</remarks>
    public void MoveObjectAlongDirection(short obj, FixedVector direction, short distance)
    {
        VectorMath.SetLength(ref direction, distance);
        Objects[obj].Position = VectorMath.Add(Objects[obj].Position, direction);
    }

    /// <summary>Distance from the object's surface to a point; leaves the delta in <see cref="ToTarget"/>.</summary>
    /// <remarks>C: distance_from_point (0x419210, geom.c).</remarks>
    public short DistanceFromPoint(short obj, in FixedVector point)
    {
        ToTarget = VectorMath.Delta(Objects[obj].Position, point);
        int magnitude = ToTarget.Magnitude();
        return unchecked((short)(FixedMath.ToShortSaturating(magnitude) - Objects[obj].CollisionRadius));
    }

    /// <summary>Surface-to-surface distance (both radii subtracted).</summary>
    /// <remarks>C: distance_from_object (0x419260, geom.c).</remarks>
    public short DistanceFromObject(short obj, short other) =>
        unchecked((short)(DistanceFromPoint(obj, Objects[other].Position) - Objects[other].CollisionRadius));

    /// <summary>Sets <see cref="TargetRange"/> (radius subtracted twice, as in the original),
    /// <see cref="NormalizedToTarget"/> and <see cref="FacingToTarget"/>.</summary>
    /// <remarks>C: get_facing_range_from_point (0x419290, geom.c).</remarks>
    public void GetFacingRangeFromPoint(short obj, in FixedVector point)
    {
        TargetRange = unchecked((short)(DistanceFromPoint(obj, point) - Objects[obj].CollisionRadius));
        NormalizedToTarget = ToTarget;
        VectorMath.Normalize(ref NormalizedToTarget);
        FacingToTarget = ScalarMath.FacingPercent(VectorMath.Dot(NormalizedToTarget, Objects[obj].Forward));
    }

    /// <summary>As <see cref="GetFacingRangeFromPoint"/> toward another object, also subtracting its
    /// radius and setting <see cref="TargetFacing"/> (is the other object facing us).</summary>
    /// <remarks>C: get_facing_range_from_object (0x419310, geom.c). Object -1 reads the globals before the
    /// object tables like the original (<see cref="PositionOf"/>).</remarks>
    public void GetFacingRangeFromObject(short obj, short other)
    {
        var point = PositionOf(other);
        GetFacingRangeFromPoint(obj, point);
        TargetRange = unchecked((short)(TargetRange - CollisionRadiusOf(other)));
        NormalizedToTarget = VectorMath.Negate(NormalizedToTarget);
        TargetFacing = ScalarMath.FacingPercent(VectorMath.Dot(NormalizedToTarget, ForwardOf(other)));
    }

    /// <summary>
    /// Position of object <paramref name="obj"/>, including -1. The AI uses -1 (no target, or a stale scan
    /// result) as an object index in a few places (<c>perform_maneuver</c> without a target,
    /// <c>Mkill_missile</c>); the original then reads the 12 bytes before <c>aShipPosition</c>
    /// (0x0059c490), which in the Kilrathi Saga image are <c>asViableTargetDistance[10..15]</c>
    /// (0x0059c470) taken as three 32-bit words. The port reproduces that layout.
    /// </summary>
    /// <remarks>C: <c>aShipPosition[-1]</c>; layout from the address map of globals.c.</remarks>
    public FixedVector PositionOf(int obj)
    {
        if (obj != -1)
            return Objects[obj].Position;
        var d = ViableTargetDistance;
        return new FixedVector(
            unchecked((int)((ushort)d[10] | (uint)(ushort)d[11] << 16)),
            unchecked((int)((ushort)d[12] | (uint)(ushort)d[13] << 16)),
            unchecked((int)((ushort)d[14] | (uint)(ushort)d[15] << 16)));
    }

    /// <summary>Velocity of object <paramref name="obj"/>, including -1: <c>aShipVelocity[-1]</c>
    /// (0x0059c004) is the bytes 4..15 of <c>abFlightPath</c> (0x0059c000) in the Kilrathi Saga image.</summary>
    /// <remarks>See <see cref="PositionOf"/>.</remarks>
    public FixedVector VelocityOf(int obj)
    {
        if (obj != -1)
            return Objects[obj].Velocity;
        var p = FlightPath;
        return new FixedVector(Word(p, 4), Word(p, 8), Word(p, 12));

        static int Word(sbyte[] bytes, int at) => unchecked(
            (byte)bytes[at] | (byte)bytes[at + 1] << 8 | (byte)bytes[at + 2] << 16 | (byte)bytes[at + 3] << 24);
    }

    /// <summary>Forward vector of object <paramref name="obj"/>, including -1: <c>aShipForwardVector[-1]</c>
    /// (0x0059bcd4) is the up vector of the scratch slot 63 (<c>aShipUpVector</c> ends there).</summary>
    /// <remarks>See <see cref="PositionOf"/>.</remarks>
    public FixedVector ForwardOf(int obj) => obj != -1 ? Objects[obj].Forward : Objects[ObjectSlots.Scratch].Up;

    /// <summary>Collision radius of object <paramref name="obj"/>, including -1:
    /// <c>asObjectCollisionRadius[-1]</c> (0x0059d70e) lies in padding after
    /// <c>aasShipMaximumShield</c> and is taken as 0.</summary>
    /// <remarks>See <see cref="PositionOf"/>.</remarks>
    public short CollisionRadiusOf(int obj) => obj != -1 ? Objects[obj].CollisionRadius : (short)0;

    /// <remarks>C: ship_vs_point (0x419390, geom.c).</remarks>
    public void ShipVsPoint(short obj, in FixedVector point) => GetFacingRangeFromPoint(obj, point);

    /// <remarks>C: ship_vs_ship (0x4193B0, geom.c).</remarks>
    public void ShipVsShip(short obj, short other) => GetFacingRangeFromObject(obj, other);

    /// <summary>Sets and returns <see cref="FacingToTarget"/> toward a point.</summary>
    /// <remarks>C: facing_to_object (0x4193D0, geom.c).</remarks>
    public short FacingToObject(short obj, in FixedVector point)
    {
        var direction = VectorMath.Delta(Objects[obj].Position, point);
        VectorMath.Normalize(ref direction);
        FacingToTarget = ScalarMath.FacingPercent(VectorMath.Dot(direction, Objects[obj].Forward));
        return FacingToTarget;
    }

    /// <summary>Roll (degrees, wrapped) that aligns <paramref name="obj"/>'s up with <paramref name="reference"/>'s frame.</summary>
    /// <remarks>C: match_roll_orientation (0x419440, geom.c).</remarks>
    public short MatchRollOrientation(short obj, short reference)
    {
        ref readonly var o = ref Objects[obj];
        ref readonly var r = ref Objects[reference];
        var roll = new FixedVector(VectorMath.Dot(o.Up, r.Right), VectorMath.Dot(o.Up, r.Up), 0);
        VectorMath.Normalize(ref roll);
        short angle = unchecked((short)FixedMath.ArcCos(roll.Y));
        if (roll.X >= 0)
            angle = unchecked((short)(360 - angle));
        return ScalarMath.WrapDegrees(angle);
    }

    /// <summary>
    /// Converts a world direction into yaw/pitch goals for <paramref name="obj"/>. Mode 1 uses the
    /// spherical solver (turn rate subtracted from the yaw), mode 0 the ArcSin solver with a
    /// behind-you correction. Returns 1 (goals untouched) for a zero direction.
    /// </summary>
    /// <remarks>C: set_ship_rotation_goals (0x4194D0, geom.c).</remarks>
    public int SetShipRotationGoals(short obj, short turnRate, in FixedVector direction, short pointingMode, ref short yawGoal, ref short pitchGoal)
    {
        var spherical = default(SphericalVector);
        var local = TransformToObjectsFrame(direction, obj);
        if (pointingMode == 1)
        {
            VectorMath.RectangularToSpherical(local, ref spherical);
            if (spherical.Radius == 0)
                return 1;
            spherical.Yaw = spherical.Yaw <= 0
                ? unchecked((short)(spherical.Yaw + turnRate))
                : unchecked((short)(spherical.Yaw - turnRate));
        }
        else
        {
            int magnitude = local.Magnitude();
            if (magnitude == 0)
                return 1;
            spherical.Yaw = unchecked((short)FixedMath.ArcSin(FixedMath.Divide(local.X, FixedMath.PlanarMagnitude(local.X, local.Z))));
            spherical.Pitch = unchecked((short)-FixedMath.ArcSin(FixedMath.Divide(local.Y, magnitude)));
            if (local.Z < 0)
            {
                spherical.Yaw = unchecked((short)-spherical.Yaw);
                spherical.Pitch = spherical.Pitch <= 0
                    ? unchecked((short)(spherical.Pitch - 180))
                    : unchecked((short)(spherical.Pitch + 180));
            }
            spherical.Pitch = spherical.Pitch <= 0
                ? unchecked((short)(spherical.Pitch + turnRate))
                : unchecked((short)(spherical.Pitch - turnRate));
        }
        yawGoal = ScalarMath.WrapDegrees(unchecked((short)-spherical.Yaw));
        pitchGoal = ScalarMath.WrapDegrees(unchecked((short)-spherical.Pitch));
        return 0;
    }

    /// <remarks>C: point_ship (0x419620, geom.c).</remarks>
    public void PointShip(short obj, short turnRate, in FixedVector direction)
    {
        ref var ship = ref Ships[obj];
        SetShipRotationGoals(obj, turnRate, direction, ship.PointingMode, ref ship.YawGoal, ref ship.PitchGoal);
    }

    /// <remarks>C: point_ship_at_point (0x419660, geom.c).</remarks>
    public void PointShipAtPoint(short obj, in FixedVector point) =>
        PointShip(obj, 0, VectorMath.Delta(Objects[obj].Position, point));

    /// <remarks>C: point_ship_at_object (0x4196A0, geom.c); object -1: see <see cref="PositionOf"/>.</remarks>
    public void PointShipAtObject(short obj, short other) => PointShipAtPoint(obj, PositionOf(other));

    /// <summary>Capital ships point away from the other object (reverse direction).</summary>
    /// <remarks>C: point_capital_ship_at_object (0x4196C0, geom.c).</remarks>
    public void PointCapitalShipAtObject(short obj, short other) =>
        PointShip(obj, 0, VectorMath.Delta(Objects[other].Position, Objects[obj].Position));

    /// <summary>Aims at a point 500 + radius units behind the other object.</summary>
    /// <remarks>C: point_ship_behind_object (0x419710, geom.c).</remarks>
    public void PointShipBehindObject(short obj, short other)
    {
        var point = Objects[other].Position;
        VectorMath.PositionRelative(ref point, Objects[other].Forward, unchecked((short)(-500 - Objects[other].CollisionRadius)));
        PointShipAtPoint(obj, point);
    }

    /// <summary>Aims at a point 500 + radius units along the other object's up axis.</summary>
    /// <remarks>C: point_ship_below_object (0x419790, geom.c).</remarks>
    public void PointShipBelowObject(short obj, short other)
    {
        var point = Objects[other].Position;
        VectorMath.PositionRelative(ref point, Objects[other].Up, unchecked((short)(Objects[other].CollisionRadius + 500)));
        PointShipAtPoint(obj, point);
    }

    /// <remarks>C: point_perpendicular_to_point (0x419810, geom.c).</remarks>
    public void PointPerpendicularToPoint(short obj, in FixedVector point)
    {
        PointShipAtPoint(obj, point);
        ref var ship = ref Ships[obj];
        ship.YawGoal = ship.YawGoal < 0 ? unchecked((short)(ship.YawGoal + 90)) : unchecked((short)(ship.YawGoal - 90));
    }

    /// <remarks>C: point_perpendicular (0x419850, geom.c).</remarks>
    public void PointPerpendicular(short obj, short other) => PointPerpendicularToPoint(obj, Objects[other].Position);

    /// <remarks>C: point_parallel (0x419870, geom.c).</remarks>
    public void PointParallel(short obj, short other)
    {
        if (other != -1)
            PointShip(obj, 0, Objects[other].Forward);
    }
}
