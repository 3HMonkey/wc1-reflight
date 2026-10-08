using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// Asteroid and mine fields (winmain.c): field registration, hazard spawning around the player,
// hazard management, visibility (easy2see) and asteroid shards.
public sealed partial class SpaceSimulation
{
    /// <summary>Base flight times of moving asteroids for the four cockpit views (Hornet..Raptor).</summary>
    /// <remarks>C: acHazardTravelTimeByView[8] (winmain.c).</remarks>
    private static ReadOnlySpan<sbyte> HazardTravelTimeByView => [56, 52, 75, 73, 0, 0, 0, 0];

    /// <summary>Pitch window (min, max) per cockpit view in which a hazard may come at the player.</summary>
    /// <remarks>C: acHazardPitchRange[8] (winmain.c).</remarks>
    private static ReadOnlySpan<sbyte> HazardPitchRange => [-10, 4, -8, 8, -12, 8, -8, 8];

    /// <summary>Registers an asteroid/mine field (at most 7 per sphere).</summary>
    /// <remarks>C: add_hazard_field (0x401C00, winmain.c).</remarks>
    public void AddHazardField(ObjectType type, FixedVector center, short radius, short density)
    {
        if (HazardFieldCount >= HazardFieldSlots)
            return;
        ref var field = ref HazardFields[HazardFieldCount];
        field.Type = type;
        field.Center = center;
        field.OuterRadius = radius;
        field.InnerRadius = radius;
        field.Density = density;
        HazardFieldCount++;
    }

    /// <summary>Removes a hazard object (-1 is a no-op apart from the sound source and counter).</summary>
    /// <remarks>C: remove_hazard (0x4011D0, winmain.c).</remarks>
    public void RemoveHazard(sbyte hazard)
    {
        Events.ReleaseSoundSource(hazard);
        RemoveObject(hazard);
        ActiveHazards = ScalarMath.MaxShort(0, unchecked((short)(ActiveHazards - 1)));
    }

    /// <summary>Removes all 20 hazard slots and deactivates the field.</summary>
    /// <remarks>C: remove_all_hazards (0x401210, winmain.c).</remarks>
    public void RemoveAllHazards()
    {
        for (int slot = 0; slot < HazardObjectSlots; slot++)
        {
            RemoveHazard(HazardObjects[slot]);
            HazardObjects[slot] = -1;
        }
        ActiveHazardField = -1;
    }

    /// <summary>Whether <paramref name="obj"/>'s sprite really shows in the space view (its screen box
    /// intersects the viewport); false when it was culled.</summary>
    /// <remarks>C: easy2see (0x401040, winmain.c), through <see cref="ShapeBounds"/>.</remarks>
    public bool Easy2See(short obj)
    {
        ref readonly var o = ref Objects[obj];
        short x = o.ScreenX;
        if (x == ObjectSlots.NotVisible)
            return false;
        x = unchecked((short)(x + ViewCenterX));
        short y = unchecked((short)(o.ScreenY + ViewCenterY));
        Span<short> bounds = stackalloc short[4];
        return ShapeBounds.GetTransformedShapeBounds(x, y, o.Shape, o.ViewFrame, o.ScreenAngle, o.ScreenScale, o.Flip, bounds) != 0;
    }

    /// <summary>Chips a rock chunk (40 frames) off <paramref name="asteroid"/> along
    /// <paramref name="direction"/>, tumbling slightly off that heading at the asteroid's speed + 0..5.</summary>
    /// <remarks>C: make_shard (0x4010C0, winmain.c).</remarks>
    public void MakeShard(short asteroid, FixedVector direction)
    {
        short fragment = FindVacant3dObject();
        if (fragment == -1)
            return;
        SetObjectsData(fragment, ObjectType.RockChunk, asteroid);
        ref var f = ref Objects[fragment];
        f.Counter = 40;
        f.Owner = unchecked((sbyte)asteroid);
        VectorMath.SetLength(ref direction, (short)(Objects[asteroid].CollisionRadius >> 1));
        f.Position = VectorMath.Add(Objects[asteroid].Position, direction);
        f.Forward = direction;
        f.FixIjk();
        f.AlterYaw(Random.Signed(20));
        f.AlterPitch(Random.Signed(20));
        f.Velocity = f.Forward;
        short speed = unchecked((short)(RealVelocity(asteroid) + Random.InRange(0, 5)));
        VectorMath.SetLength(ref f.Velocity, speed);
    }

    /// <summary>How far the reference speed is from 25, doubled (spreads the arrival times).</summary>
    /// <remarks>C: difficulty (0x401250, winmain.c).</remarks>
    public short Difficulty() => (short)(System.Math.Abs(25 - (int)HazardReferenceSpeed) * 2);

    /// <summary>Drift speed of a still asteroid: 10..17.</summary>
    /// <remarks>C: asteroid_velocity (0x401270, winmain.c).</remarks>
    public short AsteroidVelocity() => ScalarMath.MinShort(20, (short)(Random.BelowOrEqual(7) + 10));

    /// <summary>Rotates the basis axes of <paramref name="obj"/> one way or the other (50 %) and
    /// optionally reverses its forward axis (50 %).</summary>
    /// <remarks>C: skew_randomly (0x401290, winmain.c).</remarks>
    public void SkewRandomly(short obj, bool allowReverse)
    {
        ref var o = ref Objects[obj];
        if (Random.Below(100) < 50)
        {
            var saved = o.Right;
            o.Right = o.Forward;
            o.Forward = o.Up;
            o.Up = saved;
        }
        else
        {
            var saved = o.Up;
            o.Up = o.Forward;
            o.Forward = o.Right;
            o.Right = saved;
        }
        if (allowReverse && Random.Below(100) < 50)
            o.Forward = VectorMath.Negate(o.Forward);
    }

    /// <summary>
    /// Truncates the low 16 bits of a 24.8 component to a multiple of <paramref name="quantum"/>
    /// (signed): the original passes the address of the 32-bit component as a <c>short*</c>, so the
    /// upper half is untouched.
    /// </summary>
    /// <remarks>C: align (0x401390, winmain.c) applied to <c>(short *)&amp;aShipPosition[obj].x</c>.</remarks>
    public static void Align(ref int component, short quantum)
    {
        short current = unchecked((short)component);
        current = unchecked((short)(current - current % quantum));
        component = (component & unchecked((int)0xFFFF0000)) | (ushort)current;
    }

    /// <summary>
    /// Places hazard <paramref name="obj"/> at <paramref name="position"/>: mines face the player,
    /// skewed, still; moving asteroids head for where the player will be in 7..80 frames; other
    /// asteroids drift (80 %) about the field centre and start up to 1000 units back. Nothing moves
    /// while Kilrathi are within 16000.
    /// </summary>
    /// <remarks>C: init_hazard (0x4013B0, winmain.c). Its <c>type == OBJECT_TYPE_ASTEROID_FIELD</c>
    /// separation test is never true (the type is an asteroid or a mine).</remarks>
    public void InitHazard(short obj, FixedVector position, short moving)
    {
        short hazardMoves = moving;
        var type = ObjectType.SpaceMine;
        ref var field = ref HazardFields[ActiveHazardField];
        if (field.Type == ObjectType.AsteroidField)
            type = (ObjectType)((int)ObjectType.Asteroid1 + Random.BelowOrEqual(5));
        SetObjectsData(obj, type, -1);
        ref var o = ref Objects[obj];
        o.Position = position;

        short speed;
        FixedVector vector;
        if (type == ObjectType.SpaceMine)
        {
            o.PointAt(Objects[ObjectSlots.Player].Position);
            speed = 2;
            SkewRandomly(obj, true);
            hazardMoves = 0;
        }
        else if (hazardMoves != 0)
        {
            short travelTime = 65;
            if (CockpitView < 4)
                travelTime = HazardTravelTimeByView[CockpitView < 0 ? 4 : CockpitView];
            travelTime = unchecked((short)(travelTime + Random.BelowOrEqual(15)));
            travelTime = unchecked((short)(travelTime - Random.BelowOrEqual(Difficulty())));
            travelTime = ScalarMath.MaxShort(IntroSecondaryScene ? (short)45 : (short)7, travelTime);
            vector = VectorMath.Scale(Objects[ObjectSlots.Player].Velocity, travelTime << 8);
            vector = VectorMath.Add(Objects[ObjectSlots.Player].Position, vector);
            o.PointAt(vector);
            speed = VectorMath.DistanceBetweenPoints(vector, o.Position);
            travelTime = ScalarMath.MaxShort(3, unchecked((short)(travelTime - Random.Below(5))));
            speed = unchecked((short)(speed / travelTime));
        }
        else
        {
            o.PointAt(field.Center);
            speed = 0;
            SkewRandomly(obj, true);
            if (Random.Below(100) >= 20)
                speed = AsteroidVelocity();
        }
        if (KilrathiNear(0, 16000))
            speed = 0;
        o.Velocity = VectorMath.Scale(o.Forward, speed << 8);

        if (hazardMoves == 0)
        {
            int separation = type == ObjectType.AsteroidField ? 1500 : Random.BelowOrEqual(1000) << 8;
            vector = VectorMath.Scale(o.Forward, separation);
            o.Position = VectorMath.Subtract(o.Position, vector);
        }
        if (type == ObjectType.SpaceMine)
        {
            Align(ref o.Position.X, 200);
            Align(ref o.Position.Y, 200);
            Align(ref o.Position.Z, 200);
        }
        ActiveHazards++;
        o.Counter = 0;
        o.CollisionGraceTicks = 0;
    }

    /// <summary>Whether <paramref name="point"/> is within 4300 units of field <paramref name="field"/>'s edge.</summary>
    /// <remarks>C: near_field (0x401680, winmain.c).</remarks>
    public bool NearField(int field, in FixedVector point) =>
        VectorMath.IsPointWithinRange(HazardFields[field].Center, point, unchecked((short)(HazardFields[field].InnerRadius + 4300)));

    /// <summary>Whether <paramref name="point"/> lies inside field <paramref name="field"/>.</summary>
    /// <remarks>C: within_field (0x4016A0, winmain.c).</remarks>
    public bool WithinField(int field, in FixedVector point) =>
        VectorMath.IsPointWithinRange(HazardFields[field].Center, point, HazardFields[field].InnerRadius);

    /// <summary>
    /// A spawn point 3050 units ahead of the player, ±20° pitch and ±35° yaw off the nose (biased
    /// into the turn), outside 3000 units and inside the active field. <paramref name="moving"/>
    /// tells whether the hazard should come at the player (in the cockpit only within the view's
    /// pitch window, 60 %; else in a ring off the nose, 30 %).
    /// </summary>
    /// <remarks>C: try_far_spot (0x4016C0, winmain.c).</remarks>
    public bool TryFarSpot(out FixedVector spot, out short moving)
    {
        CopyFrame(0, ObjectSlots.Scratch);
        ref var scratch = ref Objects[ObjectSlots.Scratch];
        scratch.Position = Objects[ObjectSlots.Player].Position;
        short pitch = Random.Signed(20);
        short yaw = Random.Signed(35);
        if (CameraViewMode == 0 && CockpitView <= 3 && CockpitView >= 0)
        {
            sbyte minimum = HazardPitchRange[CockpitView * 2];
            moving = pitch > minimum &&
                     pitch < HazardPitchRange[CockpitView * 2 + 1] &&
                     System.Math.Abs((int)yaw) < 19 &&
                     Random.Below(100) < 60
                ? (short)1
                : (short)0;
        }
        else
        {
            moving = System.Math.Abs((int)pitch) > 5 && System.Math.Abs((int)pitch) < 20 &&
                     System.Math.Abs((int)yaw) > 5 && System.Math.Abs((int)yaw) < 20 &&
                     Random.Below(100) < 30
                ? (short)1
                : (short)0;
        }
        pitch = unchecked((short)(pitch + ScalarMath.FindRatio(-15, 15, Objects[ObjectSlots.Player].PitchRotation, -150, 150)));
        yaw = unchecked((short)(yaw + ScalarMath.FindRatio(-15, 15, Objects[ObjectSlots.Player].YawRotation, -150, 150)));
        VectorMath.RotateAboutJ(yaw, ref scratch.Right, ref scratch.Forward);
        VectorMath.RotateAboutI(pitch, ref scratch.Up, ref scratch.Forward);
        spot = scratch.PositionRelativeIjk(0, 0, 3050);
        bool outsideRange = !VectorMath.IsPointWithinRange(Objects[ObjectSlots.Player].Position, spot, 3000);
        return outsideRange && WithinField(ActiveHazardField, spot);
    }

    /// <summary>Radius around the player beyond which off-screen hazards are recycled: 4300 when
    /// still, 3100 at speed 20 and above.</summary>
    /// <remarks>C: rear_sphere (0x401870, winmain.c).</remarks>
    public short RearSphere() => ScalarMath.FindRatio(0, 20, HazardReferenceSpeed, 4300, 3100);

    /// <summary>Whether hazard <paramref name="obj"/> is still close enough to the player to keep.</summary>
    /// <remarks>C: ok_hazard_spot (0x401890, winmain.c).</remarks>
    public bool OkHazardSpot(short obj)
    {
        int range = 4300;
        if (Objects[obj].ScreenX == ObjectSlots.NotVisible)
            range = RearSphere();
        return VectorMath.IsPointWithinRange(Objects[ObjectSlots.Player].Position, Objects[obj].Position, unchecked((short)range));
    }

    /// <summary>Spawns one hazard ahead of the player; the slot or -1.</summary>
    /// <remarks>C: make_hazard (0x4018D0, winmain.c).</remarks>
    public short MakeHazard()
    {
        short obj = FindVacant3dObject();
        if (obj != -1 && TryFarSpot(out var spot, out short moving))
            InitHazard(obj, spot, moving);
        else
            obj = -1;
        return obj;
    }

    /// <summary>Frees a dust speck slot for hazards.</summary>
    /// <remarks>C: extra_hazard (0x401930, winmain.c).</remarks>
    public void ExtraHazard(short obj)
    {
        if (Objects[obj].Class == ObjectClass.Dust)
            Objects[obj].Class = ObjectClass.Null;
    }

    /// <summary>A visible, far, slow mine accelerates toward the player's position 20 frames ahead.</summary>
    /// <remarks>C: approach (0x401950, winmain.c).</remarks>
    public void Approach(short obj)
    {
        var target = VectorMath.Scale(Objects[ObjectSlots.Player].Velocity, 20 << 8);
        target = VectorMath.Add(Objects[ObjectSlots.Player].Position, target);
        Objects[obj].PointAt(target);
        var thrust = VectorMath.Scale(Objects[obj].Forward, 20 << 8);
        Objects[obj].Velocity = VectorMath.Add(Objects[obj].Velocity, thrust);
    }

    /// <summary>Every 20 view frames (staggered by slot): removes a hazard left behind, lets a far mine
    /// approach.</summary>
    /// <remarks>C: manage_hazard (0x4019E0, winmain.c).</remarks>
    public void ManageHazard(short obj, short slot)
    {
        if (RenderedSpaceFrame % 20 != slot)
            return;
        if (!OkHazardSpot(obj))
        {
            RemoveHazard(unchecked((sbyte)obj));
            return;
        }
        ref readonly var o = ref Objects[obj];
        if (o.Type == ObjectType.SpaceMine && o.ScreenX != ObjectSlots.NotVisible &&
            (ushort)o.Distance > 1500 && RealVelocity(obj) < 20)
        {
            Approach(obj);
        }
    }

    /// <summary>Attract mode: the player ship follows the eye at speed 100 and the field moves along.</summary>
    /// <remarks>C: match_ship_to_eye (0x401A60, winmain.c).</remarks>
    public void MatchShipToEye()
    {
        ref var player = ref Objects[ObjectSlots.Player];
        ref readonly var eye = ref Objects[ObjectSlots.Eye];
        player.Position = eye.Position;
        HazardReferenceSpeed = 100;
        player.Right = eye.Right;
        player.Up = eye.Up;
        player.Forward = eye.Forward;
        player.Velocity = VectorMath.Scale(player.Forward, 100 << 8);
        HazardFields[ActiveHazardField].Center = eye.Position;
    }

    /// <summary>Per view frame inside a field: manage the hazards and, with probability
    /// <c>(speed + 30) / 216</c>, fill an empty hazard slot.</summary>
    /// <remarks>C: update_hazards (0x401B30, winmain.c).</remarks>
    public void UpdateHazards()
    {
        short emptySlot = -1;
        if (IntroSecondaryScene)
            MatchShipToEye();
        else
            HazardReferenceSpeed = RealVelocity(0);
        for (short slot = 0; slot < HazardObjectSlots; slot++)
        {
            if (HazardObjects[slot] != -1)
                ManageHazard(HazardObjects[slot], slot);
            else
                emptySlot = slot;
        }
        if (emptySlot != -1 && Random.BelowOrEqual(215) < HazardReferenceSpeed + 30)
            HazardObjects[emptySlot] = unchecked((sbyte)MakeHazard());
    }

    /// <summary>Activates field <paramref name="region"/> with three hazards (slots 1..3).</summary>
    /// <remarks>C: start_hazard_field (0x401BC0, winmain.c).</remarks>
    public void StartHazardField(short region)
    {
        RemoveAllHazards();
        ActiveHazardField = region;
        short slot = 1;
        do
        {
            HazardObjects[slot] = unchecked((sbyte)MakeHazard());
        }
        while (slot++ < 3);
    }

    /// <summary>Every 16 frames: enter the first field the player approaches, leave the active one
    /// when far from it.</summary>
    /// <remarks>C: check_hazards (0x401C60, winmain.c).</remarks>
    public void CheckHazards()
    {
        if (IntroSecondaryScene)
            return;
        if (ActiveHazardField == -1)
        {
            for (short region = 0; region < HazardFieldCount; region++)
            {
                if (region != ActiveHazardField && NearField(region, Objects[ObjectSlots.Player].Position))
                {
                    StartHazardField(region);
                    return;
                }
            }
        }
        else if (!NearField(ActiveHazardField, Objects[ObjectSlots.Player].Position))
        {
            RemoveAllHazards();
        }
    }
}
