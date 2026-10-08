using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// The original sprite projection, computed by the simulation because targeting, easy2see, the
// hazards and the AI read its results: geom.c transform_objects_to_your_view .. get_right_shape,
// the nav pointer (cockpt.c), engine flames and child placement (logic.c) and the depth sort
// (eventmgr.c).
public sealed partial class SpaceSimulation
{
    /// <summary>Cosine limit of the view cone (0x94 / 256 = 0.578, half-angle ≈ 54.7°).</summary>
    private const int ViewConeCosine = 0x94;

    /// <summary>Whether <paramref name="point"/> lies in front of the eye inside its view cone.</summary>
    /// <remarks>C: IsPointWithinEyeViewCone (0x41A130, geom.c).</remarks>
    public bool IsPointWithinEyeViewCone(in FixedVector point)
    {
        ref readonly var eye = ref Objects[ObjectSlots.Eye];
        var direction = VectorMath.Delta(eye.Position, point);
        int distance = direction.Magnitude();
        if (eye.CollisionRadius * 0x100 > distance)
            return false;
        var viewPosition = eye.TransformToObjectsFrame(direction);
        if (eye.CollisionRadius * 0x100 > viewPosition.Z)
            return false;
        return FixedMath.Divide(viewPosition.Z, distance) >= ViewConeCosine;
    }

    /// <summary>
    /// Projects slots 0..60 into the space view: eye-relative position, cone and near-plane culling
    /// (<see cref="ObjectSlots.NotVisible"/>), sprite scale <c>scale * (W/2) / (distance - radius)</c>,
    /// pinhole screen position relative to the view centre, distance in units (0 = not visible),
    /// planet roll, dust frame and the sprite frame/angle/flip of ships and missiles.
    /// </summary>
    /// <remarks>C: transform_objects_to_your_view (0x41A1D0, geom.c), see docs/analysis/simulation.md §2.4.</remarks>
    public void TransformObjectsToYourView()
    {
        ClosestVisibleObject = -1;
        DrawNavPointer();
        ref readonly var eye = ref Objects[ObjectSlots.Eye];
        int eyeRadius = eye.CollisionRadius * 0x100;
        int width = (short)(ScreenWidth & ~1);
        for (short obj = 0; obj <= ObjectSlots.LastMoving; obj++)
        {
            ref var o = ref Objects[obj];
            if (o.Class == ObjectClass.Null || o.Class == ObjectClass.FixedObject || obj == NavPointerObject)
                continue;
            o.PreviousDistance = o.Distance;
            o.Distance = 0;
            if (o.Class == ObjectClass.Futurion)
            {
                o.ScreenX = ObjectSlots.NotVisible;
                continue;
            }
            var direction = o.Class is ObjectClass.Planet or ObjectClass.Star
                ? o.Position
                : VectorMath.Delta(eye.Position, o.Position);
            int distance = direction.Magnitude();
            if (distance < eyeRadius ||
                (o.Class == ObjectClass.Dust && distance > 1400 << 8))
            {
                o.ScreenX = ObjectSlots.NotVisible;
                continue;
            }
            o.ViewPosition = eye.TransformToObjectsFrame(direction);
            if (o.ViewPosition.Z < eyeRadius || FixedMath.Divide(o.ViewPosition.Z, distance) < ViewConeCosine)
            {
                o.ScreenX = ObjectSlots.NotVisible;
                continue;
            }
            int objectRadius = o.CollisionRadius * 0x100;
            if (distance <= objectRadius)
                distance = objectRadius + 1;
            if (o.Class > ObjectClass.Dust)
            {
                int scaleFactor = FixedMath.Divide(width << 15, distance - objectRadius);
                o.ScreenScale = unchecked((short)(FixedMath.Multiply((ushort)o.Scale, scaleFactor) >> 8));
                if ((ushort)o.ScreenScale > 0x1fff)
                    o.ScreenScale = 0x2000;
                if ((ushort)o.ScreenScale < 5)
                {
                    o.ScreenX = ObjectSlots.NotVisible;
                    continue;
                }
            }
            o.Distance = unchecked((short)(distance >> 8));
            o.ScreenX = unchecked((short)(FixedMath.Divide(FixedMath.Multiply(width << 7, o.ViewPosition.X), o.ViewPosition.Z) >> 8));
            o.ScreenY = unchecked((short)(FixedMath.Divide(FixedMath.Multiply(width << 7, o.ViewPosition.Y), o.ViewPosition.Z) >> 8));
            switch (o.Class)
            {
                case ObjectClass.Planet:
                    if (o.ScreenScale == 0xff)
                        SetBackgroundObjectsRotation(obj, direction);
                    break;
                case ObjectClass.Dust:
                    short dustSize = unchecked((short)(FixedMath.Multiply(0x900, FixedMath.Divide(eye.CollisionRadius << 8, distance)) >> 8));
                    if (dustSize > 3)
                        dustSize = 3;
                    o.ViewFrame = unchecked((short)(((o.Counter + SpaceFrame) & 3) + (o.ScreenAngle & 0x10) + (3 - dustSize) * 4));
                    break;
                case ObjectClass.Missile:
                case ObjectClass.Ship:
                case ObjectClass.CapitalShip:
                    GetRightShape(obj, direction);
                    break;
            }
        }
    }

    /// <summary>Rolls a background planet's sprite so that it stays upright relative to the eye.</summary>
    /// <remarks>C: set_background_objects_rotation (0x41A530, geom.c).</remarks>
    public void SetBackgroundObjectsRotation(short obj, FixedVector direction)
    {
        direction = VectorMath.Negate(direction);
        var spherical = default(SphericalVector);
        VectorMath.RectangularToSpherical(direction, ref spherical);
        ref var scratch = ref Objects[ObjectSlots.Scratch];
        scratch.InitIjk();
        scratch.AlterYaw(unchecked((short)-spherical.Yaw));
        scratch.AlterPitch(unchecked((short)-spherical.Pitch));
        ref readonly var eye = ref Objects[ObjectSlots.Eye];
        var projectedUp = new FixedVector(VectorMath.Dot(eye.Up, scratch.Right), VectorMath.Dot(eye.Up, scratch.Up), 0);
        VectorMath.Normalize(ref projectedUp);
        short angle = unchecked((short)FixedMath.ArcCos(projectedUp.Y));
        if (projectedUp.X >= 0)
            angle = unchecked((short)(360 - angle));
        Objects[obj].ScreenAngle = angle;
        Objects[obj].ScreenScale = 0xff;
    }

    /// <summary>
    /// Picks the pre-rendered view of a ship or missile: the direction to the eye in the object's
    /// frame is quantised to one of 62 views (7 pitch bands × 12 yaw sectors, top and bottom) with
    /// ±16° hysteresis; the sprite frame and flip come from the view table (missiles/turrets and the
    /// Kilrathi starbase have their own), the screen roll aligns the sprite with the eye's up vector.
    /// Capital ships load one shape per view (<c>ShapeRef(type + 22, frame)</c>, view frame 0).
    /// </summary>
    /// <remarks>C: get_right_shape (0x41A610, geom.c). The capital ship packet cache and the screen
    /// refresh calls are rendering concerns and not modelled.</remarks>
    public void GetRightShape(short obj, FixedVector direction)
    {
        var right = new FixedVector(0x100, 0, 0);
        var up = new FixedVector(0, 0x100, 0);
        var forward = new FixedVector(0, 0, 0x100);
        direction = VectorMath.Negate(direction);
        var spherical = default(SphericalVector);
        VectorMath.RectangularToSpherical(direction, ref spherical);
        VectorMath.RotateAboutJ(unchecked((short)-spherical.Yaw), ref right, ref forward);
        VectorMath.RotateAboutI(unchecked((short)-spherical.Pitch), ref up, ref forward);
        VectorMath.Normalize(ref up);
        VectorMath.Normalize(ref forward);
        ref var o = ref Objects[obj];
        var objectForward = o.TransformToObjectsFrame(forward);
        var eyeUp = o.TransformToObjectsFrame(Objects[ObjectSlots.Eye].Up);
        VectorMath.RectangularToSpherical(objectForward, ref spherical);

        short pitchBand = (short)(spherical.Pitch / 30 + 3);
        short remainder = (short)(spherical.Pitch % 30);
        if (remainder >= 16)
        {
            pitchBand++;
            if (pitchBand > 5)
                pitchBand = 6;
        }
        else if (remainder < -15)
        {
            pitchBand--;
            if (pitchBand < 1)
                pitchBand = 0;
        }
        short yawSector = (short)((12 - spherical.Yaw / 30) % 12);
        remainder = (short)(spherical.Yaw % 30);
        if (remainder >= 16)
            yawSector = (short)((yawSector + 11) % 12);
        else if (remainder < -15)
            yawSector = (short)((yawSector + 1) % 12);
        if (yawSector < 0)
            yawSector += 12;
        int directionIndex = pitchBand switch
        {
            0 => 0,
            6 => 61,
            _ => pitchBand * 12 + yawSector - 11,
        };

        var projectedUp = new FixedVector(
            VectorMath.Dot(eyeUp, DirectionViewRight[directionIndex]),
            VectorMath.Dot(eyeUp, DirectionViewUp[directionIndex]),
            0);
        VectorMath.Normalize(ref projectedUp);
        short angle = unchecked((short)FixedMath.ArcCos(projectedUp.Y));
        if (projectedUp.X >= 0)
            angle = unchecked((short)(360 - angle));

        var objectClass = o.Class;
        var type = o.Type;
        if (objectClass == ObjectClass.Missile || type == ObjectType.Turret)
            directionIndex += GeometryTables.DirectionViewCount;
        else if (type == ObjectType.KilrathiBase)
            directionIndex += GeometryTables.DirectionViewCount * 2;
        short frame = GeometryTables.DirectionShapeFrame[directionIndex];
        if (frame == 0)
            angle += 90;
        if (frame == 36 && objectClass != ObjectClass.Missile)
            angle -= 90;
        o.Flip = (short)(GeometryTables.DirectionShapeFlip[directionIndex] << 4);
        angle %= 360;
        if (angle < 0)
            angle += 360;
        o.ScreenAngle = angle;

        if (objectClass == ObjectClass.CapitalShip)
        {
            if (Ships[obj].CapitalShipViewFrame != frame)
            {
                o.ViewFrame = 0;
                Ships[obj].CapitalShipViewFrame = frame;
                o.Shape = new ShapeRef(ShipLogicalFile(type), frame);
            }
        }
        else
        {
            o.ViewFrame = frame;
        }
    }

    /// <summary>Removes the nav pointer object.</summary>
    /// <remarks>C: remove_nav_pointer (0x4168A0, cockpt.c).</remarks>
    public void RemoveNavPointer()
    {
        if (NavPointerObject != -1)
            RemoveObject(NavPointerObject);
    }

    /// <summary>
    /// While the right VDU shows navigation (cockpit or chase view, not during the autopilot) the nav
    /// pointer occupies an effect slot (class PLANET, view frame 3 of the cockpit's target-lock shape,
    /// distance 0x4a38) projected onto the current objective; otherwise the slot is freed.
    /// </summary>
    /// <remarks>C: draw_nav_pointer (0x4168C0, cockpt.c). The shape is the cockpit's
    /// <c>pTargetLockShape</c>, which the simulation does not know: <see cref="SpaceObject.Shape"/> stays
    /// <see cref="ShapeRef.None"/> and the renderer draws <see cref="NavPointerObject"/> specially. An
    /// objective index outside the table projects the origin (the original read outside the table).</remarks>
    public void DrawNavPointer()
    {
        bool active = Cockpit.GetVduMode(1) == 5 && CannedSceneMode != 4 && (CameraViewMode == 0 || CameraViewMode == 4);
        if (!active)
        {
            RemoveNavPointer();
            return;
        }
        short obj = NavPointerObject;
        if (obj == -1)
        {
            obj = FindVacant3dObject();
            NavPointerObject = obj;
            if (obj == -1)
                return;
            ref var pointer = ref Objects[obj];
            pointer.ViewFrame = 3;
            pointer.Owner = -1;
            pointer.ScreenAngle = 0;
            pointer.ScreenScale = 0x100;
            pointer.Class = ObjectClass.Planet;
            pointer.Shape = ShapeRef.None;
            pointer.ScreenX = ObjectSlots.NotVisible;
            pointer.Distance = 0;
        }
        var objectivePosition = (uint)CurrentObjective < (uint)MissionObjectives.Length
            ? MissionObjectives[CurrentObjective].Position
            : FixedVector.Zero;
        ref readonly var eye = ref Objects[ObjectSlots.Eye];
        var direction = VectorMath.Delta(eye.Position, objectivePosition);
        int distance = direction.Magnitude();
        if (eye.CollisionRadius * 0x100 >= distance)
            return;
        var viewPosition = eye.TransformToObjectsFrame(direction);
        if (eye.CollisionRadius * 0x100 > viewPosition.Z)
            return;
        if (FixedMath.Divide(viewPosition.Z, distance) < ViewConeCosine)
            return;
        int width = (short)(ScreenWidth & ~1);
        ref var o = ref Objects[obj];
        o.ScreenX = unchecked((short)(FixedMath.Divide(FixedMath.Multiply(width << 7, viewPosition.X), viewPosition.Z) >> 8));
        o.ScreenY = unchecked((short)(FixedMath.Divide(FixedMath.Multiply(width << 7, viewPosition.Y), viewPosition.Z) >> 8));
        o.Distance = 0x4a38;
    }

    /// <summary>Screen angle of an attachment drawn at <paramref name="angle"/> on ship
    /// <paramref name="ship"/>'s sprite (mirrored like the sprite, plus its rotation, 0..359).</summary>
    /// <remarks>C: flip_angle (0x4213D0, logic.c).</remarks>
    public short FlipAngle(short ship, short angle)
    {
        short flip = Objects[ship].Flip;
        if ((flip & 0x10) != 0)
            angle = unchecked((short)(180 - angle));
        if ((flip & 0x20) != 0)
            angle = unchecked((short)-angle);
        angle = unchecked((short)(angle + Objects[ship].ScreenAngle));
        angle = (short)(angle % 360);
        if (angle < 0)
            angle += 360;
        return angle;
    }

    /// <summary>
    /// Engine flames: for every visible ship or missile with thrust, the exhaust table of its type
    /// gives the flames of its current view frame; each becomes a THRUSTERS object (removed again
    /// by the next <see cref="HouseKeepObjects"/>) with a random size and frame (afterburner frames
    /// when burning). Consumes random numbers on every prepared view frame.
    /// </summary>
    /// <remarks>C: place_exhaust_on_ships (0x421430, logic.c).</remarks>
    public void PlaceExhaustOnShips()
    {
        for (short ship = 0; ship < ObjectSlots.ShipSlotCount; ship++)
        {
            ref readonly var s = ref Objects[ship];
            if (s.Class < ObjectClass.Missile || s.Speed == 0 ||
                Ships[ship].SpecialManeuver == SpecialManeuver.KillEngines ||
                s.ScreenX == ObjectSlots.NotVisible)
            {
                continue;
            }
            var table = ExhaustTables[(int)s.Type];
            if (table is null)
                continue;
            short listOffset = table.GetListOffset(s.ViewFrame);
            if (listOffset == -1)
                continue;
            int position = listOffset;
            while (table.ReadShort(position) != -1)
            {
                short obj = FindVacant3dObject();
                if (obj == -1)
                    return;
                SetObjectsData(obj, ObjectType.Thrusters, ship);
                ref var flame = ref Objects[obj];
                short frame = table.ReadShort(position);
                short scale = table.ReadShort(position + 2);
                scale = unchecked((short)(scale - Random.InRange(0, 32)));
                if (Ships[ship].ExhaustHeat == 0)
                    scale = unchecked((short)(scale - 32));
                flame.Scale = scale;
                flame.Distance = table.ReadShort(position + 4);
                flame.ScreenAngle = FlipAngle(ship, table.ReadShort(position + 6));
                flame.Flip = 0;
                flame.ScreenX = table.ReadShort(position + 8);
                flame.ScreenY = table.ReadShort(position + 10);
                position += ExhaustTable.RecordSize;
                flame.ViewFrame = Ships[ship].SpecialManeuver == SpecialManeuver.Afterburner
                    ? unchecked((short)(frame * 3 + Random.InRange(0, 2)))
                    : unchecked((short)(frame * 2 + 12 + Random.InRange(0, 1)));
            }
        }
    }

    /// <summary>
    /// Places the 2D children of ships (engine flames): their offsets, scaled by the parent's screen
    /// scale, mirrored and rotated like the parent's sprite, are added to the parent's screen
    /// position; distance and scale follow the parent.
    /// </summary>
    /// <remarks>C: reposition_fixed_child_objects (0x4215E0, logic.c), without the SDL port's
    /// floating-point thruster anchors. A child without a valid parent keeps its offsets.</remarks>
    public void RepositionFixedChildObjects()
    {
        for (short obj = ObjectSlots.FirstEffect; obj <= ObjectSlots.LastMoving; obj++)
        {
            ref var o = ref Objects[obj];
            if (o.Class != ObjectClass.FixedObject)
                continue;
            short parent = o.Owner;
            if ((uint)parent >= ObjectSlots.Count)
                continue;
            ref readonly var p = ref Objects[parent];
            if (o.Type is ObjectType.Turret or ObjectType.Thrusters)
            {
                short angle = p.ScreenAngle;
                int sine = FixedMath.Sin(angle);
                int cosine = FixedMath.Cos(angle);
                ushort parentScale = (ushort)p.ScreenScale;
                int right = o.ScreenX * parentScale;
                o.Distance = unchecked((short)(o.Distance + p.Distance));
                if ((p.Flip & 0x10) != 0)
                    right = -right;
                int up = o.ScreenY * parentScale;
                if ((p.Flip & 0x20) != 0)
                    up = -up;
                o.ScreenX = unchecked((short)((FixedMath.Multiply(right, cosine) - FixedMath.Multiply(up, sine)) >> 8));
                o.ScreenY = unchecked((short)((FixedMath.Multiply(up, cosine) + FixedMath.Multiply(right, sine)) >> 8));
                o.ScreenX = unchecked((short)(o.ScreenX + p.ScreenX));
                o.ScreenY = unchecked((short)(o.ScreenY + p.ScreenY));
            }
            o.ScreenScale = unchecked((short)((ushort)p.ScreenScale * (ushort)o.Scale >> 8));
        }
    }

    /// <summary>
    /// Painter's order into <see cref="SortedObjects"/> (-1 terminated): first the slot with the
    /// largest distance (visible or not, lowest slot on ties), then the visible slots by decreasing
    /// distance (lowest slot first on ties).
    /// </summary>
    /// <remarks>C: sort_object_depth (0x436460, eventmgr.c).</remarks>
    public void SortObjectDepth()
    {
        int previous = -999999999;
        int bestObject = -1;
        Span<bool> placed = stackalloc bool[ObjectSlots.Count];
        for (int obj = 0; obj < ObjectSlots.Count; obj++)
        {
            int distance = (ushort)Objects[obj].Distance;
            if (previous < distance)
            {
                previous = distance;
                bestObject = obj;
            }
        }
        for (int sorted = 0; sorted < ObjectSlots.Count; sorted++)
        {
            int best = -1;
            SortedObjects[sorted] = bestObject;
            if (bestObject == -1)
                return;
            placed[bestObject] = true;
            bestObject = -1;
            for (int obj = 0; obj < ObjectSlots.Count; obj++)
            {
                if (placed[obj] || Objects[obj].ScreenX == ObjectSlots.NotVisible)
                    continue;
                int distance = (ushort)Objects[obj].Distance;
                if (best < distance && previous >= distance)
                {
                    bestObject = obj;
                    best = distance;
                }
            }
        }
    }
}
